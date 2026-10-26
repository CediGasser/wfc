using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    [CustomEditor(typeof(SocketRuleSource))]
    public sealed class SocketRuleSourceEditor : RuleSourceEditor
    {
        private const float NameWidth = 110f;
        private const float LabelWidth = 52f;

        private static readonly Direction[] Faces = { Direction.PosX, Direction.NegX, Direction.PosY, Direction.NegY, Direction.PosZ, Direction.NegZ };

        private bool showSockets = true;

        protected override void DrawSourceSettings(RuleSource source)
        {
            var sockets = (SocketRuleSource)source;
            var tileSet = (TileSet)EditorGUILayout.ObjectField("Tile Set", sockets.TileSet, typeof(TileSet), false);
            if (tileSet != sockets.TileSet)
            {
                Undo.RecordObject(sockets, "Change Tile Set");
                sockets.SetTileSet(tileSet);
            }
            EditorGUILayout.LabelField(
                "Every face gets a label, in the tile's own frame. Two tiles may be neighbours where their touching faces have the same label or a compatible pair. " +
                "An empty label fits nothing; tiles without labels don't take part.", EditorStyles.wordWrappedMiniLabel);
            if (tileSet == null)
            {
                return;
            }

            showSockets = EditorGUILayout.Foldout(showSockets, "Sockets", true);
            if (showSockets)
            {
                DrawSocketTable(sockets, tileSet);
                DrawCompatiblePairs(sockets);
                DrawLabelUsage(sockets, tileSet);
            }
        }

        private static void DrawSocketTable(SocketRuleSource sockets, TileSet tileSet)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Tile", EditorStyles.miniBoldLabel, GUILayout.Width(NameWidth));
                foreach (Direction face in Faces)
                {
                    GUILayout.Label(face.ToShortString(), EditorStyles.miniBoldLabel, GUILayout.Width(LabelWidth));
                }
            }
            for (int slot = 0; slot < tileSet.Count; slot++)
            {
                TileDefinition tile = tileSet[slot];
                if (tile == null)
                {
                    continue;
                }
                SocketRuleSource.TileSockets entry = sockets.GetSockets(tile);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(new GUIContent($"{slot}: {tile.name}", tile.name), GUILayout.Width(NameWidth));
                    foreach (Direction face in Faces)
                    {
                        string label = entry != null ? entry[face] : string.Empty;
                        string edited = EditorGUILayout.DelayedTextField(label, GUILayout.Width(LabelWidth));
                        if (edited != label)
                        {
                            Undo.RecordObject(sockets, "Edit Socket");
                            sockets.SetLabel(tile, face, edited);
                            EditorUtility.SetDirty(sockets);
                        }
                    }
                }
            }

            int unused = sockets.Sockets.Count(s => s.tile == null || !tileSet.Contains(s.tile));
            if (unused > 0)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox($"{unused} tile(s) with sockets aren't in {tileSet.name} anymore.", MessageType.Info);
                    if (GUILayout.Button("Remove", GUILayout.Width(64), GUILayout.Height(38)))
                    {
                        Undo.RecordObject(sockets, "Remove Unused Sockets");
                        sockets.RemoveUnusedSockets();
                        EditorUtility.SetDirty(sockets);
                    }
                }
            }
        }

        private static void DrawCompatiblePairs(SocketRuleSource sockets)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(new GUIContent("Compatible Pairs", "Different labels that fit each other, e.g. a plug and its socket. Equal labels always fit."), EditorStyles.boldLabel);
            for (int i = 0; i < sockets.Compatible.Count; i++)
            {
                SocketRuleSource.SocketPair pair = sockets.Compatible[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    string a = EditorGUILayout.DelayedTextField(pair.a ?? string.Empty);
                    GUILayout.Label("↔", GUILayout.Width(16));
                    string b = EditorGUILayout.DelayedTextField(pair.b ?? string.Empty);
                    if (a != pair.a || b != pair.b)
                    {
                        Undo.RecordObject(sockets, "Edit Compatible Pair");
                        sockets.SetCompatible(i, a, b);
                        EditorUtility.SetDirty(sockets);
                    }
                    if (GUILayout.Button("−", EditorStyles.miniButton, GUILayout.Width(22)))
                    {
                        Undo.RecordObject(sockets, "Remove Compatible Pair");
                        sockets.RemoveCompatible(i);
                        EditorUtility.SetDirty(sockets);
                        GUIUtility.ExitGUI();
                    }
                }
            }
            if (GUILayout.Button("Add Pair", EditorStyles.miniButton, GUILayout.Width(80)))
            {
                Undo.RecordObject(sockets, "Add Compatible Pair");
                sockets.AddCompatible(string.Empty, string.Empty);
                EditorUtility.SetDirty(sockets);
            }
        }

        /// <summary>How often each label is used; a label on a single face is often a typo.</summary>
        private static void DrawLabelUsage(SocketRuleSource sockets, TileSet tileSet)
        {
            var counts = new Dictionary<string, int>();
            foreach (SocketRuleSource.TileSockets entry in sockets.Sockets.Where(s => s.tile != null && tileSet.Contains(s.tile)))
            {
                foreach (string label in entry.ToArray().Select(l => l.Trim()).Where(l => l.Length > 0))
                {
                    counts[label] = counts.TryGetValue(label, out int count) ? count + 1 : 1;
                }
            }
            if (counts.Count == 0)
            {
                return;
            }
            EditorGUILayout.Space();
            string usage = string.Join(", ", counts.OrderBy(c => c.Key).Select(c => $"{c.Key} ×{c.Value}"));
            EditorGUILayout.LabelField($"Labels: {usage}", EditorStyles.wordWrappedMiniLabel);
            string[] single = counts.Where(c => c.Value == 1 && !sockets.Compatible.Any(p => p.a == c.Key || p.b == c.Key)).Select(c => c.Key).ToArray();
            if (single.Length > 0)
            {
                EditorGUILayout.HelpBox($"Used on one face only, so they can only fit that face of another copy of the same tile: {string.Join(", ", single)}. Check for typos.", MessageType.Info);
            }
        }
    }
}
