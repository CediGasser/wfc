using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>
    /// Editing operations on chunks for the inspectors, the chunk tool and its shortcuts. Every change is recorded for
    /// Undo, and each call is one Undo step.
    /// </summary>
    public static class ChunkEditing
    {
        /// <summary>Puts a tile into a cell; a negative tile empties it.</summary>
        public static void SetCell(Chunk chunk, int cell, int tile, Orientation orientation, string undoName = "Place Tile")
        {
            Record(chunk, undoName);
            chunk.SetCell(cell, tile, orientation);
            Finish(chunk);
        }

        /// <summary>Empties the authored cells; generated tiles underneath show again.</summary>
        public static void ClearCells(Chunk chunk, IEnumerable<int> cells)
        {
            Record(chunk, "Erase Tiles");
            foreach (int cell in cells)
            {
                chunk.ClearCell(cell);
            }
            Finish(chunk);
        }

        /// <summary>
        /// Applies <paramref name="change"/> (in the chunk's grid) to the tile shown in each cell, each around its own
        /// cell. A generated tile becomes one of the chunk's own in its new orientation.
        /// </summary>
        public static void TransformCells(Chunk chunk, IEnumerable<int> cells, Orientation change, string undoName)
        {
            Record(chunk, undoName);
            List<IChunkLayer> layers = ChunkLayers.GetLayers(chunk.gameObject);
            foreach (int cell in cells)
            {
                int tile = ChunkLayers.GetShownTile(layers, cell, out Orientation orientation, out _);
                if (tile >= 0)
                {
                    chunk.SetCell(cell, tile, change * orientation);
                }
            }
            Finish(chunk);
        }

        /// <summary>Copies the tiles other layers (e.g. a generator) show in the cells into the chunk. Returns how many were kept.</summary>
        public static int KeepCells(Chunk chunk, IEnumerable<int> cells)
        {
            Record(chunk, "Keep Generated Tiles");
            List<IChunkLayer> layers = ChunkLayers.GetLayers(chunk.gameObject);
            int kept = 0;
            foreach (int cell in cells)
            {
                int tile = ChunkLayers.GetShownTile(layers, cell, out Orientation orientation, out IChunkLayer from);
                if (tile >= 0 && from is not Chunk)
                {
                    chunk.SetCell(cell, tile, orientation);
                    kept++;
                }
            }
            Finish(chunk);
            return kept;
        }

        /// <summary>
        /// Grows (positive <paramref name="delta"/>) or shrinks the chunk on one side. On the negative sides the
        /// transform moves too, so every cell stays where it is in the world.
        /// </summary>
        public static void ResizeSide(Chunk chunk, Direction side, int delta)
        {
            int axis = side.Axis();
            var size = chunk.Size;
            size[axis] = math.max(1, size[axis] + delta);
            int applied = size[axis] - chunk.Size[axis];
            if (applied == 0)
            {
                return;
            }
            var offset = int3.zero;
            if (!side.IsPositive())
            {
                offset[axis] = applied;
                Undo.RecordObject(chunk.transform, "Resize Chunk");
                var shift = int3.zero;
                shift[axis] = -applied;
                chunk.transform.position += chunk.transform.TransformVector(shift.ToVector3());
            }
            Record(chunk, "Resize Chunk");
            chunk.Resize(size, offset);
            Finish(chunk);
            ChunkToolState.ClearSelection();
        }

        /// <summary>Selects the chunk and the cells, switches to the chunk tool and frames the cells in the Scene view.</summary>
        public static void SelectCells(Chunk chunk, IEnumerable<int> cells)
        {
            int[] list = cells.Where(cell => cell >= 0 && cell < chunk.CellCount).Distinct().ToArray();
            Selection.activeGameObject = chunk.gameObject;
            ChunkToolState.Select(chunk, list);
            ActivateTool();
            if (list.Length > 0)
            {
                Frame(chunk, list);
            }
        }

        /// <summary>Switches to the chunk tool once the selection change has been processed.</summary>
        public static void ActivateTool()
        {
            EditorApplication.delayCall += () =>
            {
                if (Selection.activeGameObject != null && Selection.activeGameObject.TryGetComponent(out Chunk _))
                {
                    ToolManager.SetActiveTool<ChunkTool>();
                }
            };
        }

        /// <summary>Frames the bounds of the cells in the last active Scene view.</summary>
        public static void Frame(Chunk chunk, IReadOnlyList<int> cells)
        {
            var bounds = new Bounds(chunk.transform.TransformPoint(chunk.Cells.ToCell(cells[0]).ToVector3()), Vector3.zero);
            foreach (int cell in cells)
            {
                Vector3 center = chunk.Cells.ToCell(cell).ToVector3();
                bounds.Encapsulate(chunk.transform.TransformPoint(center - Vector3.one * 0.5f));
                bounds.Encapsulate(chunk.transform.TransformPoint(center + Vector3.one * 0.5f));
            }
            bounds.Expand(2f);
            SceneView.lastActiveSceneView?.Frame(bounds, false);
        }

        private static void Record(Chunk chunk, string undoName)
        {
            Undo.RecordObject(chunk, undoName);
        }

        private static void Finish(Chunk chunk)
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(chunk);
        }
    }
}
