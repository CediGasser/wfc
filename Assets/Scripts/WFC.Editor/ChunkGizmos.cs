using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>
    /// The chunk's bounds, a clickable box per cell that selects the chunk, and while it's selected, outlines of its
    /// own cells and of the cells selected with the chunk tool.
    /// </summary>
    public static class ChunkGizmos
    {
        public static readonly Color HighlightColor = new Color(1f, 0.85f, 0.2f, 1f);
        public static readonly Color MissingTileColor = Color.magenta;

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.Pickable)]
        private static void DrawChunk(Chunk chunk, GizmoType type)
        {
            bool selected = (type & GizmoType.Selected) != 0;
            Gizmos.matrix = chunk.transform.localToWorldMatrix;
            Vector3 size = chunk.Size.ToVector3();
            Gizmos.color = new Color(1f, 1f, 1f, selected ? 0.6f : 0.25f);
            Gizmos.DrawWireCube((size - Vector3.one) * 0.5f, size);

            ChunkData cells = chunk.Cells;
            for (int cell = 0; cell < cells.CellCount; cell++)
            {
                if (cells.IsEmpty(cell))
                {
                    continue;
                }
                TileDefinition definition = chunk.GetDefinition(cells.GetTile(cell));
                Color color = definition != null ? definition.GizmoColor : MissingTileColor;
                Vector3 center = cells.ToCell(cell).ToVector3();

                // Invisible solid cube: makes the whole cell clickable, even for invisible tiles such as air.
                Gizmos.color = new Color(color.r, color.g, color.b, 0f);
                Gizmos.DrawCube(center, Vector3.one * 0.98f);
                if (!selected)
                {
                    continue;
                }
                // While selected, outline the chunk's own cells, so they stand apart from generated ones.
                if (definition == null || !definition.HasModel)
                {
                    Gizmos.color = new Color(color.r, color.g, color.b, 0.18f);
                    Gizmos.DrawCube(center, Vector3.one * 0.98f);
                }
                Gizmos.color = new Color(color.r, color.g, color.b, 0.6f);
                Gizmos.DrawWireCube(center, Vector3.one * 0.98f);
            }

            if (ChunkToolState.Chunk == chunk)
            {
                Gizmos.color = HighlightColor;
                foreach (int cell in ChunkToolState.SelectedCells)
                {
                    if (cell >= 0 && cell < cells.CellCount)
                    {
                        Gizmos.DrawWireCube(cells.ToCell(cell).ToVector3(), Vector3.one * 1.04f);
                    }
                }
            }
        }
    }
}
