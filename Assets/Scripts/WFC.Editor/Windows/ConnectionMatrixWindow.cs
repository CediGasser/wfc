using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>Tile × tile overview of the connections of a rule source. Click a cell to list its connections.</summary>
    public sealed class ConnectionMatrixWindow : EditorWindow
    {
        private const float Cell = 28f;
        private const float HeaderWidth = 110f;

        [SerializeField] private RuleSource source;
        private int selectedA = -1;
        private int selectedB = -1;
        private Vector2 scroll;
        private Vector2 listScroll;

        [MenuItem("Window/WFC/Connection Matrix")]
        public static void Open() => Open(Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<RuleSource>() : null);

        public static void Open(RuleSource source)
        {
            var window = GetWindow<ConnectionMatrixWindow>("WFC Connections");
            if (source != null)
            {
                window.source = source;
            }
        }

        private void OnEnable() => LiveRuleAnalysis.Updated += Repaint;

        private void OnDisable() => LiveRuleAnalysis.Updated -= Repaint;

        private void OnGUI()
        {
            source = (RuleSource)EditorGUILayout.ObjectField("Rule Source", source, typeof(RuleSource), true);
            if (source == null)
            {
                source = FindObjectsByType<RuleSource>(FindObjectsInactive.Exclude).FirstOrDefault();
                if (source == null)
                {
                    EditorGUILayout.HelpBox("No rule source in the open scenes.", MessageType.Info);
                    return;
                }
            }

            RuleSourceAnalysis analysis = LiveRuleAnalysis.Get(source);
            RulesetData data = analysis.Data;
            // Only tiles that take part; a tile set may hold tiles the source doesn't use.
            int[] tiles = Enumerable.Range(0, data.TileCount).Where(t => data.GetVariants(t).Any()).ToArray();
            int n = tiles.Length;
            var row = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                row[tiles[i]] = i;
            }
            var counts = new int[n, n];
            foreach (Connection connection in data.connections.Where(o => o.weight > 0f))
            {
                counts[row[connection.tileA], row[connection.tileB]]++;
                if (connection.tileA != connection.tileB)
                {
                    counts[row[connection.tileB], row[connection.tileA]]++;
                }
            }
            int max = Mathf.Max(1, counts.Cast<int>().DefaultIfEmpty(0).Max());

            EditorGUILayout.LabelField("Number of connections between two tiles (any faces, any orientations). Red rows can't connect to anything.", EditorStyles.wordWrappedMiniLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Min(position.height * 0.6f, HeaderWidth + n * Cell + 20f)));
            Rect area = GUILayoutUtility.GetRect(HeaderWidth + n * Cell, HeaderWidth + n * Cell);
            for (int b = 0; b < n; b++)
            {
                var labelRect = new Rect(area.x + HeaderWidth + b * Cell, area.y, Cell, HeaderWidth - 4f);
                Matrix4x4 previous = GUI.matrix;
                GUIUtility.RotateAroundPivot(-90f, new Vector2(labelRect.x + Cell * 0.5f, labelRect.yMax));
                GUI.Label(new Rect(labelRect.x + Cell * 0.5f, labelRect.yMax - 9f, HeaderWidth - 4f, 18f), data.tileNames[tiles[b]], EditorStyles.miniLabel);
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
                GUI.Label(rowLabel, data.tileNames[tiles[a]], EditorStyles.miniLabel);
                GUI.contentColor = previousColor;

                for (int b = 0; b < n; b++)
                {
                    var rect = new Rect(area.x + HeaderWidth + b * Cell, area.y + HeaderWidth + a * Cell, Cell - 2f, Cell - 2f);
                    float t = counts[a, b] / (float)max;
                    Color color = counts[a, b] == 0 ? new Color(0.25f, 0.25f, 0.25f) : Color.Lerp(new Color(0.2f, 0.4f, 0.25f), new Color(0.3f, 0.95f, 0.4f), t);
                    if (tiles[a] == selectedA && tiles[b] == selectedB)
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
                        selectedA = tiles[a];
                        selectedB = tiles[b];
                        Event.current.Use();
                        Repaint();
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            DrawSelection(analysis);
        }

        private void DrawSelection(RuleSourceAnalysis analysis)
        {
            RulesetData data = analysis.Data;
            if (selectedA < 0 || selectedB < 0 || selectedA >= data.TileCount || selectedB >= data.TileCount)
            {
                return;
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"{data.tileNames[selectedA]} ↔ {data.tileNames[selectedB]}", EditorStyles.boldLabel);
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            for (int i = 0; i < data.connections.Length; i++)
            {
                Connection connection = data.connections[i];
                bool matches = (connection.tileA == selectedA && connection.tileB == selectedB) || (connection.tileA == selectedB && connection.tileB == selectedA);
                if (!matches)
                {
                    continue;
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    string seen = analysis.source.HasObservations ? $"seen {connection.observations}×, " : "";
                    EditorGUILayout.LabelField(data.DescribeConnection(connection), $"{seen}weight {connection.weight:0.##}{(connection.overridden ? " (override)" : "")}");
                    using (new EditorGUI.DisabledScope(!analysis.MatchesChunk(analysis.Chunk) || analysis.result.pairs.Count == 0))
                    {
                        if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(52)))
                        {
                            ChunkEditing.SelectCells(analysis.Chunk, analysis.GetConnectionCells(i));
                        }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
