using Unity.Mathematics;
using UnityEngine;

namespace Wfc.Authoring
{
    /// <summary>
    /// Conversions between grid orientations and Unity transforms. Mirrored orientations are expressed as
    /// a rotation combined with a scale of (-1, 1, 1).
    /// </summary>
    public static class OrientationUnity
    {
        public static Matrix4x4 ToMatrix4x4(this Orientation orientation)
        {
            int3x3 m = orientation.Matrix;
            Matrix4x4 result = Matrix4x4.identity;
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    result[row, column] = m[column][row];
                }
            }
            return result;
        }

        public static void ToRotationScale(this Orientation orientation, out Quaternion rotation, out Vector3 scale)
        {
            Matrix4x4 m = orientation.ToMatrix4x4();
            scale = Vector3.one;
            if (orientation.IsMirrored)
            {
                // M = R · diag(-1, 1, 1), so R is M with its first column negated.
                m.SetColumn(0, -m.GetColumn(0));
                scale = new Vector3(-1f, 1f, 1f);
            }
            rotation = Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));
        }

        /// <summary>
        /// The orientation closest to the rotation/mirroring part of <paramref name="matrix"/>; scale is ignored.
        /// <paramref name="error"/> is the largest deviation of a normalized matrix entry, 0 for exact grid orientations.
        /// </summary>
        public static Orientation Nearest(Matrix4x4 matrix, out float error)
        {
            var normalized = new Vector3[3];
            for (int column = 0; column < 3; column++)
            {
                Vector3 axis = matrix.GetColumn(column);
                normalized[column] = axis.sqrMagnitude > 1e-12f ? axis.normalized : Vector3.zero;
            }

            Orientation best = Orientation.Identity;
            float bestDistance = float.MaxValue;
            error = float.MaxValue;
            for (int i = 0; i < Orientation.Count; i++)
            {
                Orientation candidate = Orientation.FromIndex(i);
                int3x3 m = candidate.Matrix;
                float distance = 0f;
                float maxDeviation = 0f;
                for (int column = 0; column < 3; column++)
                {
                    for (int row = 0; row < 3; row++)
                    {
                        float deviation = Mathf.Abs(normalized[column][row] - m[column][row]);
                        distance += deviation * deviation;
                        maxDeviation = Mathf.Max(maxDeviation, deviation);
                    }
                }
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                    error = maxDeviation;
                }
            }
            return best;
        }

        public static Vector3 ToVector3(this int3 value) => new Vector3(value.x, value.y, value.z);

        public static int3 RoundToInt3(Vector3 value) => new int3(Mathf.RoundToInt(value.x), Mathf.RoundToInt(value.y), Mathf.RoundToInt(value.z));
    }
}
