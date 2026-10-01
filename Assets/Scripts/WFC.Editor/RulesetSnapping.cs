using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>
    /// Snaps tile instances back onto their root's grid after they were moved, rotated or scaled. Snapping waits
    /// until no handle is being dragged, so the regular move and rotate tools keep working.
    /// </summary>
    [InitializeOnLoad]
    public static class RulesetSnapping
    {
        private const double Interval = 0.1;

        private static readonly HashSet<TileInstance> Known = new HashSet<TileInstance>();
        private static double lastCheck;

        static RulesetSnapping()
        {
            EditorApplication.update += Update;
        }

        /// <summary>Puts an instance exactly onto a cell and orientation, with Undo.</summary>
        public static void Place(TileInstance instance, RulesetAuthoring root, Unity.Mathematics.int3 cell, Orientation orientation, string undoName = "Snap Tile")
        {
            Undo.RecordObject(instance.transform, undoName);
            GridPlacement.Write(instance.transform, root.transform, cell, orientation);
            instance.transform.hasChanged = false;
        }

        private static void Update()
        {
            if (GUIUtility.hotControl != 0 || EditorApplication.timeSinceStartup - lastCheck < Interval || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }
            lastCheck = EditorApplication.timeSinceStartup;
            Known.RemoveWhere(instance => instance == null);

            foreach (TileInstance instance in Object.FindObjectsByType<TileInstance>(FindObjectsInactive.Exclude))
            {
                Transform transform = instance.transform;
                if (Known.Add(instance))
                {
                    // Don't touch instances just because a scene was opened; only react to later edits.
                    transform.hasChanged = false;
                    continue;
                }
                if (!transform.hasChanged)
                {
                    continue;
                }
                transform.hasChanged = false;
                RulesetAuthoring root = instance.Root;
                if (root == null)
                {
                    continue;
                }
                GridPlacement.Placement placement = GridPlacement.Read(transform, root.transform);
                if (!placement.IsSnapped || HasNonUnitScale(transform))
                {
                    Place(instance, root, placement.Cell, placement.Orientation);
                }
            }
        }

        private static bool HasNonUnitScale(Transform transform)
        {
            Vector3 scale = transform.localScale;
            return Mathf.Abs(Mathf.Abs(scale.x) - 1f) > GridPlacement.SnapTolerance
                || Mathf.Abs(Mathf.Abs(scale.y) - 1f) > GridPlacement.SnapTolerance
                || Mathf.Abs(Mathf.Abs(scale.z) - 1f) > GridPlacement.SnapTolerance;
        }
    }
}
