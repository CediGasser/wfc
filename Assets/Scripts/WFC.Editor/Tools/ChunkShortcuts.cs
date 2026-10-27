using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>
    /// Rotating and mirroring while the chunk tool is active: R / Shift+R turn 90° around the vertical axis, T / Shift+T
    /// tilt 90° around X, M mirrors. They act on the selected cells, each around its own cell, or on the brush (the
    /// orientation new tiles are placed in) if no cell is selected. The keys are bound to a context that is only
    /// active in the chunk tool, so they don't clash with other shortcuts. Rebind under Edit > Shortcuts > WFC.
    /// </summary>
    [InitializeOnLoad]
    public static class ChunkShortcuts
    {
        /// <summary>Active while the chunk tool is.</summary>
        public sealed class ToolContext : IShortcutContext
        {
            public bool active => ToolManager.activeToolType == typeof(ChunkTool);
        }

        static ChunkShortcuts()
        {
            ShortcutManager.RegisterContext(new ToolContext());
        }

        [Shortcut("WFC/Chunk Tool/Rotate Around Y", typeof(ToolContext), KeyCode.R)]
        private static void RotateY() => Apply(Orientation.RotationY90, "Rotate Tile");

        [Shortcut("WFC/Chunk Tool/Rotate Back Around Y", typeof(ToolContext), KeyCode.R, ShortcutModifiers.Shift)]
        private static void RotateYBack() => Apply(Orientation.RotationY90.Inverse, "Rotate Tile");

        [Shortcut("WFC/Chunk Tool/Tilt Around X", typeof(ToolContext), KeyCode.T)]
        private static void RotateX() => Apply(Orientation.RotationX90, "Rotate Tile");

        [Shortcut("WFC/Chunk Tool/Tilt Back Around X", typeof(ToolContext), KeyCode.T, ShortcutModifiers.Shift)]
        private static void RotateXBack() => Apply(Orientation.RotationX90.Inverse, "Rotate Tile");

        [Shortcut("WFC/Chunk Tool/Mirror", typeof(ToolContext), KeyCode.M)]
        private static void Mirror() => Apply(Orientation.MirrorX, "Mirror Tile");

        /// <summary>Applies <paramref name="change"/> in grid space to the selected cells, or to the brush.</summary>
        public static void Apply(Orientation change, string undoName)
        {
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
