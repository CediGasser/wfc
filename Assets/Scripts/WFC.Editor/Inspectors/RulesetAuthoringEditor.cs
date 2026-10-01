using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    [CustomEditor(typeof(RulesetAuthoring))]
    public sealed class RulesetAuthoringEditor : UnityEditor.Editor
    {
        private SerializedProperty rulesetAsset;
        private SerializedProperty weightSource;
        private SerializedProperty fillTile;
        private SerializedProperty showCells;
        private SerializedProperty showConnections;
        private bool showDiagnostics = true;
        private bool showOrbits = true;
        private Vector2 orbitScroll;

        private void OnEnable()
        {
            rulesetAsset = serializedObject.FindProperty("target");
            weightSource = serializedObject.FindProperty("weightSource");
            fillTile = serializedObject.FindProperty("fillTile");
            showCells = serializedObject.FindProperty("showCells");
            showConnections = serializedObject.FindProperty("showConnections");
            RulesetLiveAnalysis.Updated += Repaint;
        }

        private void OnDisable()
        {
            RulesetLiveAnalysis.Updated -= Repaint;
        }

        public override void OnInspectorGUI()
        {
            var root = (RulesetAuthoring)target;
            serializedObject.Update();
            EditorGUILayout.PropertyField(rulesetAsset, new GUIContent("Ruleset Asset"));
            EditorGUILayout.PropertyField(weightSource);
            EditorGUILayout.PropertyField(fillTile);
            using (new EditorGUILayout.HorizontalScope())
            {
                showCells.boolValue = GUILayout.Toggle(showCells.boolValue, "Cells", EditorStyles.miniButtonLeft);
                showConnections.boolValue = GUILayout.Toggle(showConnections.boolValue, "Connections", EditorStyles.miniButtonRight);
            }
            if (serializedObject.ApplyModifiedProperties())
            {
                RulesetLiveAnalysis.MarkDirty();
            }

            RulesetBaker.Analysis analysis = RulesetLiveAnalysis.Get(root);
            EditorGUILayout.Space();
            DrawStatus(root, analysis);
            EditorGUILayout.Space();
            DrawActions(root, analysis);
            EditorGUILayout.Space();
            DrawDiagnostics(analysis);
            DrawOrbits(root, analysis);
            DrawOrphanedOverrides(root, analysis);
        }

        private static void DrawStatus(RulesetAuthoring root, RulesetBaker.Analysis analysis)
        {
            RulesetData data = analysis.Data;
            EditorGUILayout.LabelField("Samples", $"{analysis.instances.Count} tiles placed, {data.TileCount} tile types, {data.VariantCount} variants, {data.orbits.Length} connections");

            string state;
            MessageType type;
            if (root.Target == null)
            {
                state = "Not baked yet. Bake creates a ruleset asset next to the scene.";
                type = MessageType.Info;
            }
            else if (analysis.IsBakedAssetUpToDate)
            {
                state = "The ruleset asset is up to date.";
                type = MessageType.None;
            }
            else
            {
                state = "The samples changed since the last bake.";
                type = MessageType.Warning;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox(state, type);
                if (GUILayout.Button("Bake", GUILayout.Width(80), GUILayout.Height(38)))
                {
                    Ruleset asset = RulesetBaker.Bake(root);
                    EditorGUIUtility.PingObject(asset);
                }
            }
        }

        private static void DrawActions(RulesetAuthoring root, RulesetBaker.Analysis analysis)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(root.FillTile == null || analysis.instances.Count == 0))
                {
                    string label = root.FillTile != null ? $"Fill Empty Cells With {root.FillTile.name}" : "Fill Empty Cells (set a fill tile)";
                    if (GUILayout.Button(new GUIContent(label, "Fills the empty cells inside the bounding box of every group of touching tiles.")))
                    {
                        int count = RulesetEditorUtility.FillEmptyCells(root, root.FillTile);
                        SceneView.lastActiveSceneView?.ShowNotification(new GUIContent($"Added {count} {root.FillTile.name} tiles"), 1.5);
                    }
                }
                TileDefinition palette = RulesetToolState.PaletteTile;
                bool originFree = !RulesetEditorUtility.GetOccupiedCells(analysis).Contains(int3.zero);
                using (new EditorGUI.DisabledScope(palette == null || !originFree))
                {
                    string label = palette != null ? $"Place {palette.name} at Origin" : "Place at Origin (choose a palette tile)";
                    if (GUILayout.Button(new GUIContent(label, "Starts a sample at cell (0, 0, 0) with the tile chosen in the palette.")))
                    {
                        Selection.activeGameObject = RulesetEditorUtility.CreateInstance(root, root.transform, palette, int3.zero, Orientation.Identity).gameObject;
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Tile Palette"))
                {
                    TilePaletteWindow.Open();
                }
                if (GUILayout.Button("Connection Matrix"))
                {
                    ConnectionMatrixWindow.Open(root);
                }
            }
            DrawHelp();
        }

        private static void DrawHelp()
        {
            EditorGUILayout.LabelField(
                "Every tile below this object is a sample; tiles touching face to face become connections. Leave an empty cell between separate samples. " +
                "Select a tile and switch to the WFC tool in the Tools overlay to pick faces and preview what fits. Alt+1/2/3 rotate, Alt+4 mirrors.",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawDiagnostics(RulesetBaker.Analysis analysis)
        {
            RuleDiagnostic[] diagnostics = analysis.Data.diagnostics;
            int problems = diagnostics.Count(d => d.severity != DiagnosticSeverity.Info);
            showDiagnostics = EditorGUILayout.BeginFoldoutHeaderGroup(showDiagnostics, $"Diagnostics ({problems} problems, {diagnostics.Length - problems} notes)");
            if (showDiagnostics)
            {
                if (diagnostics.Length == 0)
                {
                    EditorGUILayout.LabelField("No problems found.", EditorStyles.miniLabel);
                }
                foreach (RuleDiagnostic diagnostic in diagnostics.OrderByDescending(d => d.severity))
                {
                    MessageType type = diagnostic.severity switch
                    {
                        DiagnosticSeverity.Error => MessageType.Error,
                        DiagnosticSeverity.Warning => MessageType.Warning,
                        _ => MessageType.Info,
                    };
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.HelpBox(diagnostic.message, type);
                        using (new EditorGUI.DisabledScope(diagnostic.sourceIds.Length == 0))
                        {
                            if (GUILayout.Button("Select", GUILayout.Width(52), GUILayout.Height(38)))
                            {
                                RulesetEditorUtility.SelectInstances(analysis, diagnostic.sourceIds);
                            }
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawOrbits(RulesetAuthoring root, RulesetBaker.Analysis analysis)
        {
            RulesetData data = analysis.Data;
            showOrbits = EditorGUILayout.BeginFoldoutHeaderGroup(showOrbits, $"Connections ({data.orbits.Length})");
            if (showOrbits)
            {
                EditorGUILayout.LabelField("Each row is one learned connection, including all its rotated variants. Faces are in each tile's own frame. Set a weight to override it; 0 forbids the connection.", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Connection", EditorStyles.miniBoldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("Seen", EditorStyles.miniBoldLabel, GUILayout.Width(36));
                    GUILayout.Label("Weight", EditorStyles.miniBoldLabel, GUILayout.Width(118));
                    GUILayout.Space(56);
                }
                orbitScroll = EditorGUILayout.BeginScrollView(orbitScroll, GUILayout.MaxHeight(320));
                for (int i = 0; i < data.orbits.Length; i++)
                {
                    Orbit orbit = data.orbits[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUIContent label = new GUIContent(RulesetEditorUtility.DescribeOrbit(data, orbit), $"{orbit.memberCount} variant pairs\n{orbit.key}");
                        GUILayout.Label(label, orbit.overridden ? EditorStyles.boldLabel : EditorStyles.label, GUILayout.MinWidth(120));
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(orbit.observations.ToString(), GUILayout.Width(36));
                        float weight = EditorGUILayout.DelayedFloatField(orbit.weight, GUILayout.Width(60));
                        if (!Mathf.Approximately(weight, orbit.weight))
                        {
                            Undo.RecordObject(root, "Override Connection Weight");
                            root.SetOverride(orbit.key, weight);
                            EditorUtility.SetDirty(root);
                            RulesetLiveAnalysis.MarkDirty();
                        }
                        using (new EditorGUI.DisabledScope(!orbit.overridden))
                        {
                            if (GUILayout.Button(new GUIContent("Reset", "Remove the override"), EditorStyles.miniButton, GUILayout.Width(54)))
                            {
                                Undo.RecordObject(root, "Reset Connection Weight");
                                root.RemoveOverride(orbit.key);
                                EditorUtility.SetDirty(root);
                                RulesetLiveAnalysis.MarkDirty();
                            }
                        }
                        if (GUILayout.Button(new GUIContent("Select", "Select the sample tiles that produced this connection"), EditorStyles.miniButton, GUILayout.Width(52)))
                        {
                            RulesetEditorUtility.SelectInstances(analysis, RulesetEditorUtility.GetOrbitSources(analysis, i));
                        }
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static void DrawOrphanedOverrides(RulesetAuthoring root, RulesetBaker.Analysis analysis)
        {
            var known = analysis.Data.orbits.Select(o => o.key).ToHashSet();
            var orphaned = root.Overrides.Where(o => !known.Contains(o.key.Undirected)).ToArray();
            if (orphaned.Length == 0)
            {
                return;
            }
            EditorGUILayout.LabelField($"Unused overrides ({orphaned.Length})", EditorStyles.boldLabel);
            foreach (WeightOverride weightOverride in orphaned)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{weightOverride.key}  = {weightOverride.weight:0.##}", EditorStyles.miniLabel);
                    if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(60)))
                    {
                        Undo.RecordObject(root, "Remove Connection Override");
                        root.RemoveOverride(weightOverride.key);
                        EditorUtility.SetDirty(root);
                        RulesetLiveAnalysis.MarkDirty();
                    }
                }
            }
        }
    }
}
