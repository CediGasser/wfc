using System;
using Unity.Mathematics;

namespace Wfc
{
    /// <summary>
    /// Cell indexing for a box-shaped grid. Cells are stored in one flat array with x varying fastest:
    /// index = x + size.x * (y + size.y * z).
    /// </summary>
    public sealed class WfcGrid
    {
        private readonly int[] neighbours;

        /// <exception cref="ArgumentOutOfRangeException">If any size component is smaller than 1.</exception>
        public WfcGrid(int3 size)
        {
            if (math.any(size < 1))
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "Every grid dimension must be at least 1.");
            }
            Size = size;
            CellCount = size.x * size.y * size.z;

            neighbours = new int[CellCount * Directions.Count];
            for (int index = 0; index < CellCount; index++)
            {
                int3 cell = ToCell(index);
                for (int d = 0; d < Directions.Count; d++)
                {
                    int3 next = cell + ((Direction)d).ToVector();
                    neighbours[index * Directions.Count + d] = Contains(next) ? ToIndex(next) : -1;
                }
            }
        }

        public int3 Size { get; }

        public int CellCount { get; }

        public bool Contains(int3 cell) => Contains(Size, cell);

        /// <summary>Flat index of a cell. Only defined for cells inside the grid.</summary>
        public int ToIndex(int3 cell) => ToIndex(Size, cell);

        public int3 ToCell(int index) => ToCell(Size, index);

        /// <summary>True if <paramref name="cell"/> lies inside a grid of the given size.</summary>
        public static bool Contains(int3 size, int3 cell)
        {
            return math.all(cell >= 0) && math.all(cell < size);
        }

        /// <summary>
        /// Flat index of a cell in a grid of the given size, x varying fastest. Every box of cells in the project
        /// (solver grids, <see cref="ChunkData"/>) uses this layout, so the same index means the same cell.
        /// </summary>
        public static int ToIndex(int3 size, int3 cell)
        {
            return cell.x + size.x * (cell.y + size.y * cell.z);
        }

        public static int3 ToCell(int3 size, int index)
        {
            int x = index % size.x;
            int rest = index / size.x;
            return new int3(x, rest % size.y, rest / size.y);
        }

        /// <summary>Index of the neighbouring cell in <paramref name="direction"/>, or -1 if it's outside the grid.</summary>
        public int GetNeighbour(int index, Direction direction)
        {
            return neighbours[index * Directions.Count + (int)direction];
        }
    }
}
