using System;
using Unity.Mathematics;

namespace Wfc
{
    /// <summary>
    /// One of the 48 ways to map a grid-aligned cube onto itself, stored as a signed permutation matrix:
    /// 24 proper rotations and 24 mirrored ones. Index 0 is the identity. All operations are table lookups.
    /// </summary>
    public readonly struct Orientation : IEquatable<Orientation>
    {
        public const int Count = 48;

        public static readonly Orientation Identity = new Orientation(0);

        private static readonly int3x3[] Matrices = new int3x3[Count];
        private static readonly int[] Determinants = new int[Count];
        private static readonly byte[] Products = new byte[Count * Count];
        private static readonly byte[] Inverses = new byte[Count];
        private static readonly byte[] DirectionMaps = new byte[Count * Directions.Count];

        // Axis permutations, row r of the matrix has its non-zero entry in column Permutations[p][r].
        private static readonly int[][] Permutations =
        {
            new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
            new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 },
        };

        // Unity's left-handed rotations by +90 degrees, matching Quaternion.Euler.
        public static readonly Orientation RotationX90;
        public static readonly Orientation RotationY90;
        public static readonly Orientation RotationZ90;
        /// <summary>Reflection across the YZ plane (x becomes -x), the same as a scale of (-1, 1, 1).</summary>
        public static readonly Orientation MirrorX;

        static Orientation()
        {
            for (int p = 0; p < Permutations.Length; p++)
            {
                for (int signs = 0; signs < 8; signs++)
                {
                    var entries = new int[9];
                    for (int row = 0; row < 3; row++)
                    {
                        entries[row * 3 + Permutations[p][row]] = ((signs >> row) & 1) == 1 ? -1 : 1;
                    }
                    int index = p * 8 + signs;
                    Matrices[index] = new int3x3(
                        entries[0], entries[1], entries[2],
                        entries[3], entries[4], entries[5],
                        entries[6], entries[7], entries[8]);
                    Determinants[index] = ComputeDeterminant(Matrices[index]);
                }
            }

            for (int a = 0; a < Count; a++)
            {
                for (int b = 0; b < Count; b++)
                {
                    byte product = IndexOf(math.mul(Matrices[a], Matrices[b]));
                    Products[a * Count + b] = product;
                    if (product == 0)
                    {
                        Inverses[a] = (byte)b;
                    }
                }
                for (int d = 0; d < Directions.Count; d++)
                {
                    int3 mapped = math.mul(Matrices[a], ((Direction)d).ToVector());
                    DirectionMaps[a * Directions.Count + d] = (byte)Directions.FromVector(mapped);
                }
            }

            RotationX90 = FromRows(new int3(1, 0, 0), new int3(0, 0, -1), new int3(0, 1, 0));
            RotationY90 = FromRows(new int3(0, 0, 1), new int3(0, 1, 0), new int3(-1, 0, 0));
            RotationZ90 = FromRows(new int3(0, -1, 0), new int3(1, 0, 0), new int3(0, 0, 1));
            MirrorX = FromRows(new int3(-1, 0, 0), new int3(0, 1, 0), new int3(0, 0, 1));
        }

        private Orientation(int index)
        {
            Index = (byte)index;
        }

        public byte Index { get; }

        public int3x3 Matrix => Matrices[Index];

        /// <summary>+1 for rotations, -1 for mirrored orientations.</summary>
        public int Determinant => Determinants[Index];

        public bool IsMirrored => Determinants[Index] < 0;

        public Orientation Inverse => new Orientation(Inverses[Index]);

        public static Orientation FromIndex(int index)
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Orientation index must be in [0, 48).");
            }
            return new Orientation(index);
        }

        /// <summary>Returns false if the matrix isn't a signed permutation matrix.</summary>
        public static bool TryFromMatrix(int3x3 matrix, out Orientation orientation)
        {
            int index = TryIndexOf(matrix);
            orientation = index >= 0 ? new Orientation(index) : Identity;
            return index >= 0;
        }

        /// <summary>Composition that applies <paramref name="second"/> first, then <paramref name="first"/> (matrix product).</summary>
        public static Orientation operator *(Orientation first, Orientation second)
        {
            return new Orientation(Products[first.Index * Count + second.Index]);
        }

        public static bool operator ==(Orientation a, Orientation b) => a.Index == b.Index;

        public static bool operator !=(Orientation a, Orientation b) => a.Index != b.Index;

        public Direction Apply(Direction direction)
        {
            return (Direction)DirectionMaps[Index * Directions.Count + (int)direction];
        }

        public int3 Apply(int3 vector)
        {
            return math.mul(Matrices[Index], vector);
        }

        public bool Equals(Orientation other) => Index == other.Index;

        public override bool Equals(object obj) => obj is Orientation other && Equals(other);

        public override int GetHashCode() => Index;

        /// <summary>For example "#13 X→+Z Y→+Y Z→-X": where each local axis ends up.</summary>
        public override string ToString()
        {
            return $"#{Index} X→{Apply(Direction.PosX).ToShortString()} Y→{Apply(Direction.PosY).ToShortString()} Z→{Apply(Direction.PosZ).ToShortString()}";
        }

        private static Orientation FromRows(int3 row0, int3 row1, int3 row2)
        {
            return new Orientation(IndexOf(new int3x3(
                row0.x, row0.y, row0.z,
                row1.x, row1.y, row1.z,
                row2.x, row2.y, row2.z)));
        }

        private static byte IndexOf(int3x3 matrix)
        {
            int index = TryIndexOf(matrix);
            if (index < 0)
            {
                throw new ArgumentException("Matrix is not a signed permutation matrix.", nameof(matrix));
            }
            return (byte)index;
        }

        private static int TryIndexOf(int3x3 matrix)
        {
            var permutation = new int[3];
            int signs = 0;
            for (int row = 0; row < 3; row++)
            {
                int found = -1;
                for (int column = 0; column < 3; column++)
                {
                    // int3x3 is column-major: matrix[column][row].
                    int value = matrix[column][row];
                    if (value == 0)
                    {
                        continue;
                    }
                    if (found >= 0 || (value != 1 && value != -1))
                    {
                        return -1;
                    }
                    found = column;
                    if (value < 0)
                    {
                        signs |= 1 << row;
                    }
                }
                if (found < 0)
                {
                    return -1;
                }
                permutation[row] = found;
            }
            for (int p = 0; p < Permutations.Length; p++)
            {
                if (Permutations[p][0] == permutation[0] && Permutations[p][1] == permutation[1] && Permutations[p][2] == permutation[2])
                {
                    return p * 8 + signs;
                }
            }
            return -1;
        }

        private static int ComputeDeterminant(int3x3 m)
        {
            // Row-major element access m[column][row].
            int a = m[0][0], b = m[1][0], c = m[2][0];
            int d = m[0][1], e = m[1][1], f = m[2][1];
            int g = m[0][2], h = m[1][2], i = m[2][2];
            return a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        }
    }
}
