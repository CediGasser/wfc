using System.Linq;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>
    /// Rotates or mirrors the selected tile instances by 90° steps in their root's grid, each around its own cell.
    /// Rebind under Edit > Shortcuts > WFC.
    /// </summary>
    public static class RulesetShortcuts
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

        /// <summary>Applies <paramref name="change"/> in grid space to every selected tile instance.</summary>
        public static void Apply(Orientation change, string undoName)
        {
            TileInstance[] instances = Selection.gameObjects
                .Select(go => go.GetComponent<TileInstance>())
                .Where(instance => instance != null && instance.Root != null)
                .ToArray();
            if (instances.Length == 0)
            {
                return;
            }
            Undo.SetCurrentGroupName(undoName);
            foreach (TileInstance instance in instances)
            {
                RulesetAuthoring root = instance.Root;
                GridPlacement.Placement placement = GridPlacement.Read(instance.transform, root.transform);
                RulesetSnapping.Place(instance, root, placement.Cell, change * placement.Orientation, undoName);
            }
            RulesetLiveAnalysis.MarkDirty();
        }
    }
}
