using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>A marker across every face where two sample cells form a connection.</summary>
    public static class SampleRuleSourceGizmos
    {
        public static readonly Color ConnectedColor = new Color(0.3f, 0.95f, 0.4f, 1f);
        public static readonly Color DisabledColor = new Color(1f, 0.8f, 0.2f, 1f);

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
        private static void DrawConnections(SampleRuleSource source, GizmoType type)
        {
            if (!source.ShowConnections)
            {
                return;
            }
            RuleSourceAnalysis analysis = LiveRuleAnalysis.Get(source);
            if (analysis == null || !analysis.MatchesChunk(source.Chunk))
            {
                return;
            }

            Gizmos.matrix = source.transform.localToWorldMatrix;
            foreach (CellPair pair in analysis.result.pairs)
            {
                Vector3 cell = WfcGrid.ToCell(analysis.sampleSize, pair.cellA).ToVector3();
                Vector3 direction = pair.direction.ToVector().ToVector3();
                Vector3 face = cell + direction * 0.5f;
                Gizmos.color = analysis.Data.connections[pair.connection].weight > 0f ? ConnectedColor : DisabledColor;
                Gizmos.DrawLine(face - direction * 0.2f, face + direction * 0.2f);
                Gizmos.DrawSphere(face, 0.035f);
            }
        }
    }
}
