using NUnit.Framework;
using Unity.Mathematics;

namespace Wfc.Tests
{
    public class ChunkDataTests
    {
        [Test]
        public void NewChunkIsEmpty()
        {
            var chunk = new ChunkData(new int3(2, 3, 4));
            Assert.That(chunk.CellCount, Is.EqualTo(24));
            Assert.That(chunk.tiles, Is.All.EqualTo(ChunkData.Empty));
            Assert.That(chunk.CountNonEmpty(), Is.Zero);
        }

        [Test]
        public void SizeIsAtLeastOne()
        {
            Assert.That(new ChunkData(new int3(0, -2, 3)).Size, Is.EqualTo(new int3(1, 1, 3)));
        }

        [Test]
        public void IndexingMatchesTheSolverGrid()
        {
            var size = new int3(3, 4, 5);
            var chunk = new ChunkData(size);
            var grid = new WfcGrid(size);
            for (int index = 0; index < chunk.CellCount; index++)
            {
                int3 cell = chunk.ToCell(index);
                Assert.That(cell, Is.EqualTo(grid.ToCell(index)));
                Assert.That(chunk.ToIndex(cell), Is.EqualTo(index));
            }
        }

        [Test]
        public void SetStoresTileAndOrientation()
        {
            var chunk = new ChunkData(new int3(2, 1, 1));
            chunk.Set(1, 3, Orientation.RotationY90);
            Assert.That(chunk.GetTile(1), Is.EqualTo(3));
            Assert.That(chunk.GetOrientation(1), Is.EqualTo(Orientation.RotationY90));
            Assert.That(chunk.IsEmpty(0), Is.True);

            chunk.Set(1, -5, Orientation.RotationY90);
            Assert.That(chunk.IsEmpty(1), Is.True);
            Assert.That(chunk.orientations[1], Is.Zero, "empty cells have orientation 0");
        }

        [Test]
        public void ResizedMovesCellsByTheOffset()
        {
            var chunk = new ChunkData(new int3(3, 1, 1));
            chunk.Set(0, 1, Orientation.Identity);
            chunk.Set(2, 2, Orientation.MirrorX);

            ChunkData resized = chunk.Resized(new int3(2, 2, 1), new int3(-1, 0, 0));

            Assert.That(resized.Size, Is.EqualTo(new int3(2, 2, 1)));
            Assert.That(resized.CountNonEmpty(), Is.EqualTo(1), "the cell at x = 0 moved outside");
            int moved = resized.ToIndex(new int3(1, 0, 0));
            Assert.That(resized.GetTile(moved), Is.EqualTo(2));
            Assert.That(resized.GetOrientation(moved), Is.EqualTo(Orientation.MirrorX));
            Assert.That(chunk.CountNonEmpty(), Is.EqualTo(2), "the original is unchanged");
        }

        [Test]
        public void CloneIsIndependent()
        {
            var chunk = new ChunkData(new int3(2, 2, 2));
            chunk.Set(3, 1, Orientation.Identity);
            ChunkData clone = chunk.Clone();
            Assert.That(clone.ContentEquals(chunk), Is.True);
            clone.Clear(3);
            Assert.That(chunk.GetTile(3), Is.EqualTo(1));
            Assert.That(clone.ContentEquals(chunk), Is.False);
        }

        [Test]
        public void ValidateRepairsInconsistentData()
        {
            var chunk = new ChunkData(new int3(2, 1, 1));
            chunk.size = new int3(2, 2, 1);
            chunk.tiles[0] = 4;
            chunk.orientations[0] = 7;
            chunk.tiles[1] = -3;

            Assert.That(chunk.Validate(), Is.True);
            Assert.That(chunk.tiles.Length, Is.EqualTo(4));
            Assert.That(chunk.orientations.Length, Is.EqualTo(4));
            Assert.That(chunk.GetTile(0), Is.EqualTo(4), "valid cells are kept");
            Assert.That(chunk.IsEmpty(1), Is.True, "invalid tiles are emptied");
            Assert.That(chunk.IsEmpty(3), Is.True, "new cells are empty");

            chunk.orientations[0] = Orientation.Count;
            Assert.That(chunk.Validate(), Is.True);
            Assert.That(chunk.IsEmpty(0), Is.True, "an unknown orientation empties the cell");
            Assert.That(chunk.Validate(), Is.False, "nothing left to repair");
        }
    }
}
