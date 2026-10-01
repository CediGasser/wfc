using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>Cell wireframes for tile instances and connection markers across touching faces.</summary>
    public static class RulesetGizmos
    {
        public static readonly Color ConnectedColor = new Color(0.3f, 0.95f, 0.4f, 1f);
        public static readonly Color DisabledColor = new Color(1f, 0.8f, 0.2f, 1f);
        public static readonly Color SkippedColor = new Color(1f, 0.25f, 0.25f, 1f);

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.Pickable)]
        private static void DrawInstance(TileInstance instance, GizmoType type)
        {
            RulesetAuthoring root = instance.Root;
            bool selected = (type & GizmoType.Selected) != 0;
            if (root != null && !root.ShowCells && !selected)
            {
                return;
            }

            Color color = instance.Definition != null ? instance.Definition.GizmoColor : Color.magenta;
            Gizmos.matrix = instance.transform.localToWorldMatrix;

            // Invisible solid cube: makes the whole cell clickable, even for empty tiles such as air.
            Gizmos.color = new Color(color.r, color.g, color.b, 0f);
            Gizmos.DrawCube(Vector3.zero, Vector3.one * 0.98f);

            if (instance.Definition == null || instance.Definition.IsEmpty)
            {
                Gizmos.color = new Color(color.r, color.g, color.b, selected ? 0.18f : 0.07f);
                Gizmos.DrawCube(Vector3.zero, Vector3.one * 0.98f);
            }
            Gizmos.color = new Color(color.r, color.g, color.b, selected ? 1f : 0.35f);
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one * (selected ? 1f : 0.98f));
        }

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
        private static void DrawConnections(RulesetAuthoring root, GizmoType type)
        {
            if (!root.ShowConnections)
            {
                return;
            }
            RulesetBaker.Analysis analysis = RulesetLiveAnalysis.Get(root);
            if (analysis == null)
            {
                return;
            }

            Gizmos.matrix = root.transform.localToWorldMatrix;
            foreach (ObservedPair pair in analysis.result.pairs)
            {
                Vector3 cell = analysis.input.instances[pair.sourceA].cell.ToVector3();
                Vector3 direction = pair.direction.ToVector().ToVector3();
                Vector3 face = cell + direction * 0.5f;
                Gizmos.color = pair.orbit < 0 ? SkippedColor
                    : analysis.Data.orbits[pair.orbit].weight > 0f ? ConnectedColor
                    : DisabledColor;
                Gizmos.DrawLine(face - direction * 0.2f, face + direction * 0.2f);
                Gizmos.DrawSphere(face, 0.035f);
            }
        }
    }
}
