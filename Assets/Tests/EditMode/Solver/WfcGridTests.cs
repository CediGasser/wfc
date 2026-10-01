using System;
using NUnit.Framework;
using Unity.Mathematics;

namespace Wfc.Tests.Solver
{
    [Category("Solver")]
    public class WfcGridTests
    {
        [Test]
        public void SizeAndCellCount()
        {
            var grid = new WfcGrid(new int3(3, 4, 5));
            Assert.That(grid.Size, Is.EqualTo(new int3(3, 4, 5)));
            Assert.That(grid.CellCount, Is.EqualTo(60));
        }

        [Test]
        public void RejectsEmptySizes()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WfcGrid(new int3(0, 1, 1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WfcGrid(new int3(2, -1, 2)));
        }

        [Test]
        public void IndexOrderIsXThenYThenZ()
        {
            var grid = new WfcGrid(new int3(3, 4, 5));
            Assert.That(grid.ToIndex(int3.zero), Is.EqualTo(0));
            Assert.That(grid.ToIndex(new int3(1, 0, 0)), Is.EqualTo(1));
            Assert.That(grid.ToIndex(new int3(0, 1, 0)), Is.EqualTo(3));
            Assert.That(grid.ToIndex(new int3(0, 0, 1)), Is.EqualTo(12));
            Assert.That(grid.ToIndex(new int3(2, 3, 4)), Is.EqualTo(59));
        }

        [Test]
        public void IndexAndCellRoundTrip()
        {
            var grid = new WfcGrid(new int3(3, 4, 5));
            for (int i = 0; i < grid.CellCount; i++)
            {
                int3 cell = grid.ToCell(i);
                Assert.That(grid.Contains(cell), Is.True);
                Assert.That(grid.ToIndex(cell), Is.EqualTo(i));
            }
        }

        [Test]
        public void ContainsChecksAllBounds()
        {
            var grid = new WfcGrid(new int3(2, 2, 2));
            Assert.That(grid.Contains(new int3(1, 1, 1)), Is.True);
            Assert.That(grid.Contains(new int3(-1, 0, 0)), Is.False);
            Assert.That(grid.Contains(new int3(0, 2, 0)), Is.False);
            Assert.That(grid.Contains(new int3(0, 0, 2)), Is.False);
        }

        [Test]
        public void NeighboursMatchCellOffsets()
        {
            var grid = new WfcGrid(new int3(3, 4, 5));
            for (int i = 0; i < grid.CellCount; i++)
            {
                for (int d = 0; d < Directions.Count; d++)
                {
                    var direction = (Direction)d;
                    int3 expected = grid.ToCell(i) + direction.ToVector();
                    int neighbour = grid.GetNeighbour(i, direction);
                    if (grid.Contains(expected))
                    {
                        Assert.That(neighbour, Is.EqualTo(grid.ToIndex(expected)));
                        Assert.That(grid.GetNeighbour(neighbour, direction.Opposite()), Is.EqualTo(i));
                    }
                    else
                    {
                        Assert.That(neighbour, Is.EqualTo(-1), $"{grid.ToCell(i)} towards {direction.ToShortString()} is outside");
                    }
                }
            }
        }

        [Test]
        public void SingleCellGridHasNoNeighbours()
        {
            var grid = new WfcGrid(new int3(1, 1, 1));
            for (int d = 0; d < Directions.Count; d++)
            {
                Assert.That(grid.GetNeighbour(0, (Direction)d), Is.EqualTo(-1));
            }
        }
    }
}
