using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Unity.Mathematics;
using Wfc.Tests.Solver;

namespace Wfc.Tests
{
    public class ChunkSeamsTests
    {
        private static ChunkSeams.Result Generate(RulesetData data, int3 size, int2 coordinate, Dictionary<int2, ChunkData> world, int band, uint seed = 1)
        {
            return ChunkSeams.Generate(new CompiledRules(data), data, size, coordinate,
                c => world.TryGetValue(c, out ChunkData chunk) ? chunk : null, band, seed, CellHeuristic.Entropy, null, 10);
        }

        /// <summary>A chunk filled with one tile per cell: the cell index plus 1000 times a chunk id.</summary>
        private static ChunkData Numbered(int3 size, int id)
        {
            var chunk = new ChunkData(size);
            for (int cell = 0; cell < chunk.CellCount; cell++)
            {
                chunk.Set(cell, id * 1000 + cell, Orientation.Identity);
            }
            return chunk;
        }

        /// <summary>Every pair of neighbouring cells in the world, also across chunk borders, is allowed by the rules.</summary>
        private static void AssertWorldIsValid(RulesetData data, int3 size, Dictionary<int2, ChunkData> world)
        {
            var rules = new CompiledRules(data);
            int Variant(int2 chunk, int3 cell)
            {
                ChunkData source = world[chunk];
                int index = source.ToIndex(cell);
                return data.FindVariant(source.GetTile(index), source.GetOrientation(index));
            }

            foreach (var (coordinate, chunk) in world)
            {
                for (int index = 0; index < chunk.CellCount; index++)
                {
                    int3 cell = chunk.ToCell(index);
                    Assert.That(Variant(coordinate, cell), Is.GreaterThanOrEqualTo(0), $"chunk {coordinate}, cell {cell} has no variant");
                    foreach (Direction direction in Directions.Positive)
                    {
                        int3 next = cell + direction.ToVector();
                        int2 nextChunk = coordinate;
                        if (next.x == size.x)
                        {
                            next.x = 0;
                            nextChunk.x++;
                        }
                        if (next.z == size.z)
                        {
                            next.z = 0;
                            nextChunk.y++;
                        }
                        if (next.y >= size.y || !world.ContainsKey(nextChunk))
                        {
                            continue;
                        }
                        Assert.That(rules.IsAllowed(Variant(coordinate, cell), direction, Variant(nextChunk, next)), Is.True,
                            $"chunk {coordinate} cell {cell} doesn't fit chunk {nextChunk} cell {next} ({direction.ToShortString()})");
                    }
                }
            }
        }

        [Test]
        public void InputHoldsTheTouchingCellsOfAllNeighbours()
        {
            var size = new int3(2, 1, 2);
            var world = new Dictionary<int2, ChunkData>
            {
                [new int2(1, 0)] = Numbered(size, 1),
                [new int2(-1, -1)] = Numbered(size, 2),
                [new int2(0, 0)] = Numbered(size, 3),
            };

            ChunkData input = ChunkSeams.BuildInput(size, int2.zero, c => world.TryGetValue(c, out ChunkData chunk) ? chunk : null, 0);

            Assert.That(input.Size, Is.EqualTo(new int3(4, 1, 4)));
            Assert.That(input.GetTile(input.ToIndex(new int3(3, 0, 1))), Is.EqualTo(1000 + size.x * 0), "right neighbour, its cell (0, 0, 0)");
            Assert.That(input.GetTile(input.ToIndex(new int3(3, 0, 2))), Is.EqualTo(1000 + size.x * 1), "right neighbour, its cell (0, 0, 1)");
            Assert.That(input.GetTile(input.ToIndex(new int3(0, 0, 0))), Is.EqualTo(2000 + 3), "diagonal neighbour, its cell (1, 0, 1)");
            Assert.That(input.IsEmpty(input.ToIndex(new int3(1, 0, 1))), Is.True, "the chunk itself is generated, even if it exists");
            Assert.That(input.IsEmpty(input.ToIndex(new int3(0, 0, 1))), Is.True, "missing neighbours leave the margin free");
            Assert.That(input.CountNonEmpty(), Is.EqualTo(3));
        }

        [Test]
        public void BandIsFreedInSideNeighboursOnly()
        {
            var size = new int3(3, 1, 3);
            var world = new Dictionary<int2, ChunkData>
            {
                [new int2(-1, 0)] = Numbered(size, 1),
                [new int2(-1, 1)] = Numbered(size, 2),
            };

            ChunkData input = ChunkSeams.BuildInput(size, int2.zero, c => world.TryGetValue(c, out ChunkData chunk) ? chunk : null, 1);

            Assert.That(ChunkSeams.Margin(1), Is.EqualTo(2));
            Assert.That(input.Size, Is.EqualTo(new int3(7, 1, 7)));
            for (int z = 2; z < 5; z++)
            {
                Assert.That(input.IsEmpty(input.ToIndex(new int3(1, 0, z))), Is.True, "the band next to the chunk is free");
                Assert.That(input.GetTile(input.ToIndex(new int3(0, 0, z))), Is.EqualTo(1000 + 1 + size.x * (z - 2)), "the cell behind the band is fixed");
            }
            Assert.That(input.GetTile(input.ToIndex(new int3(1, 0, 5))), Is.EqualTo(2000 + 2), "diagonal neighbours keep their cells next to the corner");
        }

        [Test]
        public void ChunkFitsItsNeighbours()
        {
            RulesetData data = TestRules.Checker().Data();
            var size = new int3(3, 2, 3);
            var world = new Dictionary<int2, ChunkData>();
            // In this order every chunk touches the earlier ones through a side, so the checkerboard phase carries over.
            foreach (int2 coordinate in new[] { new int2(0, 0), new int2(1, 0), new int2(0, 1), new int2(1, 1), new int2(-1, 0) })
            {
                ChunkSeams.Result result = Generate(data, size, coordinate, world, 0);
                Assert.That(result.Status, Is.EqualTo(SolverStatus.Done), $"chunk {coordinate}");
                Assert.That(result.BandCells, Is.Empty);
                world[coordinate] = result.Chunk;
            }

            AssertWorldIsValid(data, size, world);
        }

        [Test]
        public void BandCellsLieInSideNeighboursAndStillFit()
        {
            RulesetData data = TestRules.HeavyLight().Data();
            var size = new int3(4, 2, 4);
            var world = new Dictionary<int2, ChunkData>();
            foreach (int2 coordinate in new[] { new int2(0, 0), new int2(0, 1) })
            {
                world[coordinate] = Generate(data, size, coordinate, world, 0).Chunk;
            }
            ChunkData diagonalBefore = world[new int2(0, 1)].Clone();

            ChunkSeams.Result result = Generate(data, size, new int2(1, 0), world, 2, seed: 5);

            Assert.That(result.Status, Is.EqualTo(SolverStatus.Done));
            Assert.That(result.BandCells.Count, Is.EqualTo(2 * size.y * size.z), "two cells deep along the seam with chunk (0, 0)");
            foreach (ChunkSeams.BandCell cell in result.BandCells)
            {
                Assert.That(cell.Chunk, Is.EqualTo(new int2(0, 0)));
                Assert.That(world[cell.Chunk].ToCell(cell.Cell).x, Is.GreaterThanOrEqualTo(size.x - 2));
                world[cell.Chunk].Set(cell.Cell, cell.Tile, cell.Orientation);
            }
            world[new int2(1, 0)] = result.Chunk;
            Assert.That(world[new int2(0, 1)].ContentEquals(diagonalBefore), Is.True, "diagonal neighbours never change");
            AssertWorldIsValid(data, size, world);
        }

        [Test]
        public void CancellationStopsTheGeneration()
        {
            RulesetData data = TestRules.Checker().Data();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => ChunkSeams.Generate(new CompiledRules(data), data, new int3(3, 1, 3),
                int2.zero, _ => null, 0, 1, CellHeuristic.Entropy, null, 10, cancellation.Token));
        }
    }
}
