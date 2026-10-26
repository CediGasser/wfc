using System;
using Unity.Mathematics;

namespace Wfc
{
    /// <summary>
    /// A box of cells, each either empty or holding one tile in one orientation: the format for samples, generated
    /// output and partially painted areas. A tile is an index into a tile list (in Unity, a TileSet slot). Cells are
    /// stored in flat arrays indexed like <see cref="WfcGrid"/>, so cell i of a chunk is cell i of a solver grid of the
    /// same size. The fields are public for serialization; change the size only through <see cref="Resized"/> or
    /// <see cref="CopyFrom"/>, so the arrays always match it.
    /// </summary>
    [Serializable]
    public sealed class ChunkData
    {
        /// <summary>The tile value of an empty cell.</summary>
        public const int Empty = -1;

        public int3 size;
        /// <summary>Tile per cell, or <see cref="Empty"/>.</summary>
        public int[] tiles;
        /// <summary><see cref="Orientation.Index"/> per cell; 0 for empty cells. Any of the 48 orientations is stored as given.</summary>
        public byte[] orientations;

        public ChunkData() : this(new int3(1, 1, 1))
        {
        }

        /// <summary>A chunk of the given size (at least 1 in every dimension) with every cell empty.</summary>
        public ChunkData(int3 size)
        {
            this.size = math.max(size, 1);
            tiles = new int[CellCount];
            orientations = new byte[CellCount];
            Array.Fill(tiles, Empty);
        }

        public int3 Size => size;

        public int CellCount => size.x * size.y * size.z;

        public bool Contains(int3 cell) => WfcGrid.Contains(size, cell);

        public int ToIndex(int3 cell) => WfcGrid.ToIndex(size, cell);

        public int3 ToCell(int index) => WfcGrid.ToCell(size, index);

        public bool IsEmpty(int cell) => tiles[cell] < 0;

        public int GetTile(int cell) => tiles[cell];

        public Orientation GetOrientation(int cell) => Orientation.FromIndex(orientations[cell]);

        /// <summary>Puts <paramref name="tile"/> into the cell; a negative tile empties it.</summary>
        public void Set(int cell, int tile, Orientation orientation)
        {
            bool empty = tile < 0;
            tiles[cell] = empty ? Empty : tile;
            orientations[cell] = empty ? (byte)0 : orientation.Index;
        }

        public void Clear(int cell) => Set(cell, Empty, Orientation.Identity);

        /// <summary>Empties every cell.</summary>
        public void Clear()
        {
            Array.Fill(tiles, Empty);
            Array.Clear(orientations, 0, orientations.Length);
        }

        public int CountNonEmpty()
        {
            int count = 0;
            foreach (int tile in tiles)
            {
                count += tile >= 0 ? 1 : 0;
            }
            return count;
        }

        public ChunkData Clone()
        {
            var clone = new ChunkData();
            clone.CopyFrom(this);
            return clone;
        }

        /// <summary>Takes over the size and cells of <paramref name="other"/>, copying its arrays.</summary>
        public void CopyFrom(ChunkData other)
        {
            size = other.size;
            tiles = (int[])other.tiles.Clone();
            orientations = (byte[])other.orientations.Clone();
        }

        /// <summary>
        /// A copy with a different size. Cell c moves to c + <paramref name="offset"/>; cells that end up outside the
        /// new size are dropped, new cells are empty.
        /// </summary>
        public ChunkData Resized(int3 newSize, int3 offset)
        {
            var resized = new ChunkData(newSize);
            for (int cell = 0; cell < CellCount; cell++)
            {
                int3 target = ToCell(cell) + offset;
                if (!IsEmpty(cell) && resized.Contains(target))
                {
                    int index = resized.ToIndex(target);
                    resized.tiles[index] = tiles[cell];
                    resized.orientations[index] = orientations[cell];
                }
            }
            return resized;
        }

        /// <summary>
        /// Repairs data that doesn't fit together, e.g. after a serialized file was edited by hand: a size below 1,
        /// arrays of the wrong length, invalid tiles or orientations. Returns true if anything was changed.
        /// </summary>
        public bool Validate()
        {
            bool changed = false;
            if (math.any(size < 1))
            {
                size = math.max(size, 1);
                changed = true;
            }
            int count = CellCount;
            if (tiles == null || tiles.Length != count)
            {
                int[] old = tiles ?? Array.Empty<int>();
                tiles = new int[count];
                Array.Fill(tiles, Empty);
                Array.Copy(old, tiles, Math.Min(old.Length, count));
                changed = true;
            }
            if (orientations == null || orientations.Length != count)
            {
                byte[] old = orientations ?? Array.Empty<byte>();
                orientations = new byte[count];
                Array.Copy(old, orientations, Math.Min(old.Length, count));
                changed = true;
            }
            for (int cell = 0; cell < count; cell++)
            {
                bool invalid = tiles[cell] < Empty || orientations[cell] >= Orientation.Count;
                if (invalid || (tiles[cell] == Empty && orientations[cell] != 0))
                {
                    // An unreadable cell is emptied; an empty cell always has orientation 0.
                    Clear(cell);
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>True if both chunks have the same size and the same tile and orientation in every cell.</summary>
        public bool ContentEquals(ChunkData other)
        {
            if (other == null || !size.Equals(other.size))
            {
                return false;
            }
            for (int cell = 0; cell < CellCount; cell++)
            {
                if (tiles[cell] != other.tiles[cell] || orientations[cell] != other.orientations[cell])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
