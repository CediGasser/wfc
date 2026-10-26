using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>
    /// Inspector for every rule source: bake status, diagnostics and the derived connections with their weight
    /// overrides. Each kind of source draws its own settings in <see cref="DrawSourceSettings"/>.
    /// </summary>
    [CustomEditor(typeof(RuleSource), true)]
    public class RuleSourceEditor : UnityEditor.Editor
    {
        private bool showDiagnostics = true;
        private bool showConnectionList = true;
        private Vector2 connectionScroll;

        protected virtual void OnEnable()
        {
            LiveRuleAnalysis.Updated += Repaint;
        }

        protected virtual void OnDisable()
        {
            LiveRuleAnalysis.Updated -= Repaint;
        }

        public override void OnInspectorGUI()
        {
            var source = (RuleSource)target;
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ruleset"), new GUIContent("Ruleset Asset"));
            DrawSourceSettings(source);
            if (serializedObject.ApplyModifiedProperties())
            {
                LiveRuleAnalysis.MarkDirty();
            }

            RuleSourceAnalysis analysis = LiveRuleAnalysis.Get(source);
            EditorGUILayout.Space();
            DrawStatus(source, analysis);
            EditorGUILayout.Space();
            DrawActions(source, analysis);
            EditorGUILayout.Space();
            DrawDiagnostics(analysis);
            DrawConnections(source, analysis);
            DrawOrphanedOverrides(source, analysis);
        }

        /// <summary>The source's own settings; by default every serialized field except the shared ones.</summary>
        protected virtual void DrawSourceSettings(RuleSource source)
        {
            DrawPropertiesExcluding(serializedObject, "m_Script", "ruleset", "overrides");
        }

        protected virtual void DrawActions(RuleSource source, RuleSourceAnalysis analysis)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Tile Palette"))
                {
                    TilePaletteWindow.Open();
                }
                if (GUILayout.Button("Connection Matrix"))
                {
                    ConnectionMatrixWindow.Open(source);
                }
            }
        }

        private static void DrawStatus(RuleSource source, RuleSourceAnalysis analysis)
        {
            RulesetData data = analysis.Data;
            int tiles = Enumerable.Range(0, data.TileCount).Count(t => data.GetVariants(t).Any());
            EditorGUILayout.LabelField("Rules", $"{tiles} tile types, {data.VariantCount} variants, {data.connections.Length} connections");

            string state;
            MessageType type;
            if (source.Ruleset == null)
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
                state = "The rules changed since the last bake.";
                type = MessageType.Warning;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox(state, type);
                if (GUILayout.Button("Bake", GUILayout.Width(80), GUILayout.Height(38)))
                {
                    Ruleset asset = RulesetBaker.Bake(source);
                    EditorGUIUtility.PingObject(asset);
                }
            }
        }

        private void DrawDiagnostics(RuleSourceAnalysis analysis)
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
                bool canSelect = analysis.MatchesChunk(analysis.Chunk);
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
                        using (new EditorGUI.DisabledScope(!canSelect || diagnostic.cells.Length == 0))
                        {
                            if (GUILayout.Button(new GUIContent("Select", "Highlight the cells this is about"), GUILayout.Width(52), GUILayout.Height(38)))
                            {
                                ChunkEditing.SelectCells(analysis.Chunk, diagnostic.cells);
                            }
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawConnections(RuleSource source, RuleSourceAnalysis analysis)
        {
            RulesetData data = analysis.Data;
            showConnectionList = EditorGUILayout.BeginFoldoutHeaderGroup(showConnectionList, $"Connections ({data.connections.Length})");
            if (showConnectionList)
            {
                EditorGUILayout.LabelField("Each row is one connection, including all its rotated variants. Faces are in each tile's own frame. Set a weight to override it; 0 forbids the connection.", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Connection", EditorStyles.miniBoldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("Seen", EditorStyles.miniBoldLabel, GUILayout.Width(36));
                    GUILayout.Label("Weight", EditorStyles.miniBoldLabel, GUILayout.Width(118));
                    GUILayout.Space(56);
                }
                bool canSelect = analysis.MatchesChunk(analysis.Chunk) && analysis.result.pairs.Count > 0;
                connectionScroll = EditorGUILayout.BeginScrollView(connectionScroll, GUILayout.MaxHeight(320));
                for (int i = 0; i < data.connections.Length; i++)
                {
                    Connection connection = data.connections[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var label = new GUIContent(data.DescribeConnection(connection), $"{connection.memberCount} variant pairs\n{connection.key}");
                        GUILayout.Label(label, connection.overridden ? EditorStyles.boldLabel : EditorStyles.label, GUILayout.MinWidth(120));
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(source.HasObservations ? connection.observations.ToString() : "–", GUILayout.Width(36));
                        float weight = EditorGUILayout.DelayedFloatField(connection.weight, GUILayout.Width(60));
                        if (!Mathf.Approximately(weight, connection.weight))
                        {
                            Undo.RecordObject(source, "Override Connection Weight");
                            source.SetOverride(connection.key, weight);
                            EditorUtility.SetDirty(source);
                        }
                        using (new EditorGUI.DisabledScope(!connection.overridden))
                        {
                            if (GUILayout.Button(new GUIContent("Reset", "Remove the override"), EditorStyles.miniButton, GUILayout.Width(54)))
                            {
                                Undo.RecordObject(source, "Reset Connection Weight");
                                source.RemoveOverride(connection.key);
                                EditorUtility.SetDirty(source);
                            }
                        }
                        using (new EditorGUI.DisabledScope(!canSelect))
                        {
                            if (GUILayout.Button(new GUIContent("Select", "Highlight the sample cells that produced this connection"), EditorStyles.miniButton, GUILayout.Width(52)))
                            {
                                ChunkEditing.SelectCells(analysis.Chunk, analysis.GetConnectionCells(i));
                            }
                        }
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static void DrawOrphanedOverrides(RuleSource source, RuleSourceAnalysis analysis)
        {
            var known = analysis.Data.connections.Select(o => o.key).ToHashSet();
            WeightOverride[] orphaned = source.Overrides.Where(o => !known.Contains(o.key.Undirected)).ToArray();
            if (orphaned.Length == 0)
            {
                return;
            }
            Dictionary<string, string> names = analysis.specs.Where(s => s.IsDefined).GroupBy(s => s.id).ToDictionary(g => g.Key, g => g.First().name);
            string Name(string id) => names.TryGetValue(id ?? string.Empty, out string name) ? name : id;

            EditorGUILayout.LabelField($"Unused overrides ({orphaned.Length})", EditorStyles.boldLabel);
            foreach (WeightOverride weightOverride in orphaned)
            {
                ConnectionKey key = weightOverride.key;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent($"{Name(key.tileA)}#{key.orientationA} {key.Direction.ToShortString()} {Name(key.tileB)}#{key.orientationB}  = {weightOverride.weight:0.##}", key.ToString()), EditorStyles.miniLabel);
                    if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(60)))
                    {
                        Undo.RecordObject(source, "Remove Connection Override");
                        source.RemoveOverride(key);
                        EditorUtility.SetDirty(source);
                    }
                }
            }
        }
    }
}
