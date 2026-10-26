using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Wfc.Tests
{
    /// <summary>
    /// Builds a sample chunk from tiles placed at arbitrary cells, including negative ones: the chunk is the bounding
    /// box of all placements, everything else stays empty. Separate samples just need a gap between them (with
    /// <see cref="SampleRules.emptyCellTile"/> left at "no information").
    /// </summary>
    internal sealed class SampleBuilder
    {
        private readonly Dictionary<int3, (int tile, Orientation orientation)> placements = new Dictionary<int3, (int, Orientation)>();

        public int Count => placements.Count;

        /// <summary>The cell of the chunk's (0, 0, 0), i.e. the smallest placed cell, after <see cref="Build"/>.</summary>
        public int3 Origin { get; private set; }

        public SampleBuilder Place(int tile, int3 cell, Orientation orientation = default)
        {
            if (!placements.TryAdd(cell, (tile, orientation)))
            {
                throw new ArgumentException($"Cell {cell} is already taken.");
            }
            return this;
        }

        public ChunkData Build()
        {
            int3 min = new int3(int.MaxValue), max = new int3(int.MinValue);
            foreach (int3 cell in placements.Keys)
            {
                min = math.min(min, cell);
                max = math.max(max, cell);
            }
            if (placements.Count == 0)
            {
                min = max = int3.zero;
            }
            Origin = min;
            var chunk = new ChunkData(max - min + 1);
            foreach (var (cell, (tile, orientation)) in placements)
            {
                chunk.Set(chunk.ToIndex(cell - min), tile, orientation);
            }
            return chunk;
        }

        /// <summary>The chunk index of a placed cell, after <see cref="Build"/>.</summary>
        public int IndexOf(ChunkData chunk, int3 cell) => chunk.ToIndex(cell - Origin);
    }
}
