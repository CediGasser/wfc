using System.Linq;
using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>Tile × tile overview of the learned connections of a ruleset root. Click a cell to list its connections.</summary>
    public sealed class ConnectionMatrixWindow : EditorWindow
    {
        private const float Cell = 28f;
        private const float HeaderWidth = 110f;

        [SerializeField] private RulesetAuthoring root;
        private int selectedA = -1;
        private int selectedB = -1;
        private Vector2 scroll;
        private Vector2 listScroll;

        [MenuItem("Window/WFC/Connection Matrix")]
        public static void Open() => Open(Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<RulesetAuthoring>() : null);

        public static void Open(RulesetAuthoring root)
        {
            var window = GetWindow<ConnectionMatrixWindow>("WFC Connections");
            if (root != null)
            {
                window.root = root;
            }
        }

        private void OnEnable() => RulesetLiveAnalysis.Updated += Repaint;

        private void OnDisable() => RulesetLiveAnalysis.Updated -= Repaint;

        private void OnGUI()
        {
            root = (RulesetAuthoring)EditorGUILayout.ObjectField("Ruleset Root", root, typeof(RulesetAuthoring), true);
            if (root == null)
            {
                root = FindObjectsByType<RulesetAuthoring>(FindObjectsInactive.Exclude).FirstOrDefault();
                if (root == null)
                {
                    EditorGUILayout.HelpBox("No ruleset root in the open scenes.", MessageType.Info);
                    return;
                }
            }

            RulesetBaker.Analysis analysis = RulesetLiveAnalysis.Get(root);
            RulesetData data = analysis.Data;
            int n = data.TileCount;
            var counts = new int[n, n];
            foreach (Orbit orbit in data.orbits.Where(o => o.weight > 0f))
            {
                counts[orbit.tileA, orbit.tileB]++;
                if (orbit.tileA != orbit.tileB)
                {
                    counts[orbit.tileB, orbit.tileA]++;
                }
            }
            int max = Mathf.Max(1, counts.Cast<int>().DefaultIfEmpty(0).Max());

            EditorGUILayout.LabelField("Number of learned connections between two tiles (any faces, any orientations). Red rows can't connect to anything.", EditorStyles.wordWrappedMiniLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Min(position.height * 0.6f, HeaderWidth + n * Cell + 20f)));
            Rect area = GUILayoutUtility.GetRect(HeaderWidth + n * Cell, HeaderWidth + n * Cell);
            for (int b = 0; b < n; b++)
            {
                var labelRect = new Rect(area.x + HeaderWidth + b * Cell, area.y, Cell, HeaderWidth - 4f);
                Matrix4x4 previous = GUI.matrix;
                GUIUtility.RotateAroundPivot(-90f, new Vector2(labelRect.x + Cell * 0.5f, labelRect.yMax));
                GUI.Label(new Rect(labelRect.x + Cell * 0.5f, labelRect.yMax - 9f, HeaderWidth - 4f, 18f), data.tileNames[b], EditorStyles.miniLabel);
                GUI.matrix = previous;
            }
            for (int a = 0; a < n; a++)
            {
                bool isolated = Enumerable.Range(0, n).All(b => counts[a, b] == 0);
                var rowLabel = new Rect(area.x, area.y + HeaderWidth + a * Cell + 6f, HeaderWidth - 4f, 18f);
                Color previousColor = GUI.contentColor;
                if (isolated)
                {
                    GUI.contentColor = new Color(1f, 0.45f, 0.45f);
                }
                GUI.Label(rowLabel, data.tileNames[a], EditorStyles.miniLabel);
                GUI.contentColor = previousColor;

                for (int b = 0; b < n; b++)
                {
                    var rect = new Rect(area.x + HeaderWidth + b * Cell, area.y + HeaderWidth + a * Cell, Cell - 2f, Cell - 2f);
                    float t = counts[a, b] / (float)max;
                    Color color = counts[a, b] == 0 ? new Color(0.25f, 0.25f, 0.25f) : Color.Lerp(new Color(0.2f, 0.4f, 0.25f), new Color(0.3f, 0.95f, 0.4f), t);
                    if (a == selectedA && b == selectedB)
                    {
                        EditorGUI.DrawRect(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), Color.white);
                    }
                    EditorGUI.DrawRect(rect, color);
                    if (counts[a, b] > 0)
                    {
                        GUI.Label(rect, counts[a, b].ToString(), EditorStyles.centeredGreyMiniLabel);
                    }
                    if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                    {
                        selectedA = a;
                        selectedB = b;
                        Event.current.Use();
                        Repaint();
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            DrawSelection(analysis);
        }

        private void DrawSelection(RulesetBaker.Analysis analysis)
        {
            RulesetData data = analysis.Data;
            if (selectedA < 0 || selectedB < 0 || selectedA >= data.TileCount || selectedB >= data.TileCount)
            {
                return;
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"{data.tileNames[selectedA]} ↔ {data.tileNames[selectedB]}", EditorStyles.boldLabel);
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            for (int i = 0; i < data.orbits.Length; i++)
            {
                Orbit orbit = data.orbits[i];
                bool matches = (orbit.tileA == selectedA && orbit.tileB == selectedB) || (orbit.tileA == selectedB && orbit.tileB == selectedA);
                if (!matches)
                {
                    continue;
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(RulesetEditorUtility.DescribeOrbit(data, orbit), $"seen {orbit.observations}×, weight {orbit.weight:0.##}{(orbit.overridden ? " (override)" : "")}");
                    if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(52)))
                    {
                        RulesetEditorUtility.SelectInstances(analysis, RulesetEditorUtility.GetOrbitSources(analysis, i));
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
