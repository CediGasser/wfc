using System;
using Unity.Mathematics;
using UnityEngine;

namespace Wfc.Authoring
{
    /// <summary>
    /// Walks the cells a ray passes through in a box of cells, in the box's local space (cell (x, y, z) spans
    /// (x, y, z) ± 0.5), in order, visiting each cell once (Amanatides &amp; Woo).
    /// </summary>
    public static class ChunkRaycast
    {
        public struct Hit
        {
            public int3 cell;
            /// <summary>The face of <see cref="cell"/> the ray entered through, i.e. the face pointing back towards the ray's origin.</summary>
            public Direction face;
            /// <summary>Distance along the (normalized) ray to where it entered <see cref="cell"/>.</summary>
            public float distance;
            /// <summary>True if <see cref="cell"/> is the first solid cell; false if the ray left the box without hitting one.</summary>
            public bool solid;
        }

        /// <summary>
        /// The first cell for which <paramref name="isSolid"/> returns true. If the ray crosses the box without hitting
        /// one, <paramref name="hit"/> is the last cell it passed (the far wall), with <see cref="Hit.solid"/> false.
        /// Returns false only if the ray misses the box.
        /// </summary>
        public static bool Raycast(int3 size, Vector3 origin, Vector3 direction, Func<int3, bool> isSolid, out Hit hit)
        {
            hit = default;
            if (direction.sqrMagnitude < 1e-12f)
            {
                return false;
            }
            direction.Normalize();

            // Where the ray enters and leaves the box (slab test).
            Vector3 min = -0.5f * Vector3.one;
            Vector3 max = new Vector3(size.x, size.y, size.z) - 0.5f * Vector3.one;
            float tNear = float.NegativeInfinity, tFar = float.PositiveInfinity;
            int nearAxis = -1;
            for (int a = 0; a < 3; a++)
            {
                if (Mathf.Abs(direction[a]) < 1e-9f)
                {
                    if (origin[a] < min[a] || origin[a] > max[a])
                    {
                        return false;
                    }
                    continue;
                }
                float t1 = (min[a] - origin[a]) / direction[a];
                float t2 = (max[a] - origin[a]) / direction[a];
                if (t1 > t2)
                {
                    (t1, t2) = (t2, t1);
                }
                if (t1 > tNear)
                {
                    tNear = t1;
                    nearAxis = a;
                }
                tFar = Mathf.Min(tFar, t2);
            }
            if (tNear > tFar || tFar < 0f)
            {
                return false;
            }

            float t = Mathf.Max(tNear, 0f);
            Vector3 start = origin + direction * t;
            var cell = math.clamp(new int3(Mathf.RoundToInt(start.x), Mathf.RoundToInt(start.y), Mathf.RoundToInt(start.z)), 0, size - 1);
            // Started inside the box: there is no entry face, so take the face the ray mostly travels away from.
            int axis = tNear >= 0f && nearAxis >= 0 ? nearAxis : DominantAxis(direction);
            Direction face = FaceTowards(axis, direction[axis]);

            var step = new int3((int)Mathf.Sign(direction.x), (int)Mathf.Sign(direction.y), (int)Mathf.Sign(direction.z));
            Vector3 tMax = default, tDelta = default;
            for (int a = 0; a < 3; a++)
            {
                if (Mathf.Abs(direction[a]) < 1e-9f)
                {
                    tMax[a] = float.PositiveInfinity;
                    tDelta[a] = float.PositiveInfinity;
                    continue;
                }
                float boundary = cell[a] + (direction[a] > 0f ? 0.5f : -0.5f);
                tMax[a] = (boundary - origin[a]) / direction[a];
                tDelta[a] = 1f / Mathf.Abs(direction[a]);
            }

            while (true)
            {
                hit = new Hit { cell = cell, face = face, distance = t, solid = false };
                if (isSolid(cell))
                {
                    hit.solid = true;
                    return true;
                }
                int next = tMax.x <= tMax.y && tMax.x <= tMax.z ? 0 : tMax.y <= tMax.z ? 1 : 2;
                t = tMax[next];
                cell[next] += step[next];
                if (t > tFar || cell[next] < 0 || cell[next] >= size[next])
                {
                    return true;
                }
                face = FaceTowards(next, direction[next]);
                tMax[next] += tDelta[next];
            }
        }

        /// <summary>The face on <paramref name="axis"/> that a ray moving with <paramref name="component"/> enters a cell through.</summary>
        private static Direction FaceTowards(int axis, float component) => (Direction)(axis * 2 + (component > 0f ? 1 : 0));

        private static int DominantAxis(Vector3 direction)
        {
            Vector3 abs = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
            return abs.x >= abs.y && abs.x >= abs.z ? 0 : abs.y >= abs.z ? 1 : 2;
        }
    }
}
