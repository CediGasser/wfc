using Unity.Mathematics;
using UnityEngine;

namespace Wfc.Authoring
{
    /// <summary>Reads and writes a tile instance's cell and orientation in its root's grid space.</summary>
    public static class GridPlacement
    {
        /// <summary>Largest position or matrix deviation still considered snapped.</summary>
        public const float SnapTolerance = 1e-3f;

        public readonly struct Placement
        {
            public Placement(int3 cell, Orientation orientation, float positionError, float orientationError)
            {
                Cell = cell;
                Orientation = orientation;
                PositionError = positionError;
                OrientationError = orientationError;
            }

            public int3 Cell { get; }
            public Orientation Orientation { get; }
            public float PositionError { get; }
            public float OrientationError { get; }
            public bool IsSnapped => PositionError <= SnapTolerance && OrientationError <= SnapTolerance;
        }

        /// <summary>The nearest cell and orientation for the instance's current transform.</summary>
        public static Placement Read(Transform instance, Transform root)
        {
            Matrix4x4 matrix = root.worldToLocalMatrix * instance.localToWorldMatrix;
            Vector3 position = matrix.GetColumn(3);
            int3 cell = OrientationUnity.RoundToInt3(position);
            Orientation orientation = OrientationUnity.Nearest(matrix, out float orientationError);
            float positionError = (position - cell.ToVector3()).magnitude;
            return new Placement(cell, orientation, positionError, orientationError);
        }

        /// <summary>Moves the instance exactly onto a cell and orientation of the root's grid, respecting intermediate parents.</summary>
        public static void Write(Transform instance, Transform root, int3 cell, Orientation orientation)
        {
            Matrix4x4 local = GetLocalMatrix(instance.parent, root, cell, orientation);
            Orientation localOrientation = OrientationUnity.Nearest(local, out _);
            localOrientation.ToRotationScale(out Quaternion rotation, out Vector3 scale);
            instance.localPosition = local.GetColumn(3);
            instance.localRotation = rotation;
            instance.localScale = scale;
        }

        /// <summary>World matrix of a cell with the given orientation, e.g. for drawing previews.</summary>
        public static Matrix4x4 GetWorldMatrix(Transform root, int3 cell, Orientation orientation)
        {
            return root.localToWorldMatrix * Matrix4x4.Translate(cell.ToVector3()) * orientation.ToMatrix4x4();
        }

        private static Matrix4x4 GetLocalMatrix(Transform parent, Transform root, int3 cell, Orientation orientation)
        {
            Matrix4x4 world = GetWorldMatrix(root, cell, orientation);
            return parent != null ? parent.worldToLocalMatrix * world : world;
        }
    }
}
