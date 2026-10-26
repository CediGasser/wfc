using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>
    /// While the chunk tool is active, rotates or mirrors the selected cells by 90° steps in the chunk's grid, each
    /// around its own cell, or the brush (the orientation new tiles are placed in) if no cell is selected.
    /// Rebind under Edit > Shortcuts > WFC.
    /// </summary>
    public static class ChunkShortcuts
    {
        [Shortcut("WFC/Rotate Tile +90° Around X", typeof(SceneView), KeyCode.Alpha1, ShortcutModifiers.Alt)]
        private static void RotateX() => Apply(Orientation.RotationX90, "Rotate Tile");

        [Shortcut("WFC/Rotate Tile -90° Around X", typeof(SceneView), KeyCode.Alpha1, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
        private static void RotateXBack() => Apply(Orientation.RotationX90.Inverse, "Rotate Tile");

        [Shortcut("WFC/Rotate Tile +90° Around Y", typeof(SceneView), KeyCode.Alpha2, ShortcutModifiers.Alt)]
        private static void RotateY() => Apply(Orientation.RotationY90, "Rotate Tile");

        [Shortcut("WFC/Rotate Tile -90° Around Y", typeof(SceneView), KeyCode.Alpha2, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
        private static void RotateYBack() => Apply(Orientation.RotationY90.Inverse, "Rotate Tile");

        [Shortcut("WFC/Rotate Tile +90° Around Z", typeof(SceneView), KeyCode.Alpha3, ShortcutModifiers.Alt)]
        private static void RotateZ() => Apply(Orientation.RotationZ90, "Rotate Tile");

        [Shortcut("WFC/Rotate Tile -90° Around Z", typeof(SceneView), KeyCode.Alpha3, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
        private static void RotateZBack() => Apply(Orientation.RotationZ90.Inverse, "Rotate Tile");

        [Shortcut("WFC/Mirror Tile Along X", typeof(SceneView), KeyCode.Alpha4, ShortcutModifiers.Alt)]
        private static void Mirror() => Apply(Orientation.MirrorX, "Mirror Tile");

        /// <summary>Applies <paramref name="change"/> in grid space to the selected cells, or to the brush.</summary>
        public static void Apply(Orientation change, string undoName)
        {
            if (ToolManager.activeToolType != typeof(ChunkTool))
            {
                return;
            }
            Chunk chunk = ChunkToolState.Chunk;
            if (chunk != null && ChunkToolState.SelectedCells.Count > 0)
            {
                ChunkEditing.TransformCells(chunk, ChunkToolState.SelectedCells.ToArray(), change, undoName);
                ChunkToolState.NotifyChanged();
            }
            else
            {
                ChunkToolState.BrushOrientation = change * ChunkToolState.BrushOrientation;
            }
        }
    }
}
