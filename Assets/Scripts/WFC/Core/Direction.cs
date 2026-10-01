using System;
using Unity.Mathematics;

namespace Wfc
{
    /// <summary>
    /// The six face directions of a grid cell. Opposite directions differ only in the lowest bit.
    /// </summary>
    public enum Direction : byte
    {
        PosX = 0,
        NegX = 1,
        PosY = 2,
        NegY = 3,
        PosZ = 4,
        NegZ = 5,
    }

    public static class Directions
    {
        public const int Count = 6;

        /// <summary>The three positive directions, used to visit every pair of neighbouring cells exactly once.</summary>
        public static readonly Direction[] Positive = { Direction.PosX, Direction.PosY, Direction.PosZ };

        public static Direction Opposite(this Direction direction)
        {
            return (Direction)((int)direction ^ 1);
        }

        public static int Axis(this Direction direction)
        {
            return (int)direction >> 1;
        }

        public static bool IsPositive(this Direction direction)
        {
            return ((int)direction & 1) == 0;
        }

        public static int3 ToVector(this Direction direction)
        {
            int3 vector = int3.zero;
            vector[direction.Axis()] = direction.IsPositive() ? 1 : -1;
            return vector;
        }

        /// <summary>Converts a unit axis vector such as (0, -1, 0) back into its direction.</summary>
        public static Direction FromVector(int3 vector)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                if (vector[axis] != 0)
                {
                    return (Direction)(axis * 2 + (vector[axis] > 0 ? 0 : 1));
                }
            }
            throw new ArgumentException($"{vector} is not a unit axis vector.", nameof(vector));
        }

        public static string ToShortString(this Direction direction)
        {
            return direction switch
            {
                Direction.PosX => "+X",
                Direction.NegX => "-X",
                Direction.PosY => "+Y",
                Direction.NegY => "-Y",
                Direction.PosZ => "+Z",
                _ => "-Z",
            };
        }
    }
}
