using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    [CustomEditor(typeof(Chunk))]
    public sealed class ChunkEditor : UnityEditor.Editor
    {
        private Vector3Int pendingSize;
        private bool editingSize;
        private ChunkAsset assetToLoad;

        private static readonly (Direction side, string label)[] Sides =
        {
            (Direction.NegX, "−X"), (Direction.PosX, "+X"), (Direction.NegY, "−Y"), (Direction.PosY, "+Y"), (Direction.NegZ, "−Z"), (Direction.PosZ, "+Z"),
        };

        public override void OnInspectorGUI()
        {
            var chunk = (Chunk)target;
            if (GUILayout.Button(new GUIContent("Edit Cells", "Switch to the WFC Chunk Tool: click tiles to select them, Shift+Click to place the palette tile.")))
            {
                ChunkEditing.ActivateTool();
            }
            EditorGUILayout.Space();
            DrawTileSet(chunk);
            DrawSize(chunk);
            DrawResizeSides(chunk);
            EditorGUILayout.Space();
            DrawStatistics(chunk);
            EditorGUILayout.Space();
            DrawActions(chunk);
            EditorGUILayout.Space();
            DrawAsset(chunk);
        }

        private static void DrawTileSet(Chunk chunk)
        {
            var set = (TileSet)EditorGUILayout.ObjectField(new GUIContent("Tile Set", "The tiles the cells refer to, by slot."), chunk.TileSet, typeof(TileSet), false);
            if (set == chunk.TileSet)
            {
                return;
            }
            if (chunk.TileSet == null || chunk.Cells.CountNonEmpty() == 0)
            {
                Undo.RecordObject(chunk, "Change Tile Set");
                chunk.TileSet = set;
                return;
            }
            var missing = new List<TileDefinition>();
            if (!Chunk.TryRemap(chunk.Cells, chunk.TileSet, set, out ChunkData remapped, missing))
            {
                EditorUtility.DisplayDialog("Change Tile Set", $"{set.name} lacks tiles this chunk uses: {string.Join(", ", missing.Distinct().Select(d => d.name))}.", "OK");
                return;
            }
            Undo.RecordObject(chunk, "Change Tile Set");
            chunk.TileSet = set;
            chunk.SetCells(remapped);
        }

        private void DrawSize(Chunk chunk)
        {
            var size = new Vector3Int(chunk.Size.x, chunk.Size.y, chunk.Size.z);
            if (!editingSize)
            {
                pendingSize = size;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                pendingSize = EditorGUILayout.Vector3IntField("Size", pendingSize);
                editingSize = pendingSize != size;
                using (new EditorGUI.DisabledScope(!editingSize))
                {
                    if (GUILayout.Button(new GUIContent("Apply", "Cells outside the new size are removed."), GUILayout.Width(52)))
                    {
                        Undo.RecordObject(chunk, "Resize Chunk");
                        chunk.Resize(math.max(new int3(pendingSize.x, pendingSize.y, pendingSize.z), 1), int3.zero);
                        editingSize = false;
                    }
                }
            }
        }

        /// <summary>Grows or shrinks one side by a cell; the other cells stay where they are in the world.</summary>
        private static void DrawResizeSides(Chunk chunk)
        {
            foreach (var (label, delta) in new[] { ("Grow", 1), ("Shrink", -1) })
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel(label);
                    foreach (var (side, sideLabel) in Sides)
                    {
                        if (GUILayout.Button(new GUIContent(sideLabel, $"{label} the {sideLabel} side by one cell"), EditorStyles.miniButton))
                        {
                            ChunkEditing.ResizeSide(chunk, side, delta);
                        }
                    }
                }
            }
        }

        private static void DrawStatistics(Chunk chunk)
        {
            ChunkData cells = chunk.Cells;
            int filled = cells.CountNonEmpty();
            int generated = 0;
            if (chunk.Generated != null)
            {
                for (int cell = 0; cell < cells.CellCount; cell++)
                {
                    generated += cells.IsEmpty(cell) && !chunk.Generated.IsEmpty(cell) ? 1 : 0;
                }
            }
            EditorGUILayout.LabelField("Cells", $"{filled} of {cells.CellCount} filled{(generated > 0 ? $", {generated} generated (not saved)" : "")}");

            int missing = Enumerable.Range(0, cells.CellCount).Count(cell => !cells.IsEmpty(cell) && chunk.GetDefinition(cells.GetTile(cell)) == null);
            if (chunk.TileSet == null)
            {
                EditorGUILayout.HelpBox("Assign a tile set; the cells refer to its slots.", MessageType.Warning);
            }
            else if (missing > 0)
            {
                EditorGUILayout.HelpBox($"{missing} cell(s) use empty or unknown slots of {chunk.TileSet.name}.", MessageType.Warning);
            }
        }

        private static void DrawActions(Chunk chunk)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(chunk.Generated == null))
                {
                    if (GUILayout.Button(new GUIContent("Keep Generated", "Turns the generated tiles into authored ones, so they're saved and can be edited.")))
                    {
                        Undo.RecordObject(chunk, "Keep Generated Tiles");
                        chunk.KeepGenerated();
                    }
                }
                using (new EditorGUI.DisabledScope(chunk.Cells.CountNonEmpty() == 0))
                {
                    if (GUILayout.Button("Clear Cells"))
                    {
                        Undo.RecordObject(chunk, "Clear Chunk");
                        chunk.ClearCells();
                    }
                }
            }
        }

        private void DrawAsset(Chunk chunk)
        {
            EditorGUILayout.LabelField("Chunk Asset", EditorStyles.boldLabel);
            if (GUILayout.Button(new GUIContent("Save As Asset…", "Stores the authored cells in a new chunk asset.")))
            {
                string path = EditorUtility.SaveFilePanelInProject("Save Chunk", chunk.name, "asset", "Choose where to save the chunk asset.");
                if (!string.IsNullOrEmpty(path))
                {
                    var asset = CreateInstance<ChunkAsset>();
                    chunk.SaveTo(asset);
                    AssetDatabase.CreateAsset(asset, path);
                    AssetDatabase.SaveAssets();
                    EditorGUIUtility.PingObject(asset);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                assetToLoad = (ChunkAsset)EditorGUILayout.ObjectField(assetToLoad, typeof(ChunkAsset), false);
                using (new EditorGUI.DisabledScope(assetToLoad == null))
                {
                    if (GUILayout.Button(new GUIContent("Load", "Replaces size and cells with the asset's."), GUILayout.Width(52)))
                    {
                        Undo.RecordObject(chunk, "Load Chunk Asset");
                        var missing = new List<TileDefinition>();
                        if (!chunk.LoadFrom(assetToLoad, missing))
                        {
                            EditorUtility.DisplayDialog("Load Chunk Asset", $"{chunk.TileSet.name} lacks tiles the asset uses: {string.Join(", ", missing.Distinct().Select(d => d.name))}.", "OK");
                        }
                    }
                }
            }
        }
    }
}
