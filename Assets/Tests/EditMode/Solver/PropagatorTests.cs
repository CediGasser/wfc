using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;

namespace Wfc.Tests.Solver
{
    [Category("Solver")]
    public class PropagatorTests
    {
        private RulesetData data;
        private CompiledRules rules;
        private int bottom;
        private int top;

        [SetUp]
        public void SetUp()
        {
            data = TestRules.Layers().Data();
            rules = new CompiledRules(data);
            bottom = TestRules.Variant(data, "bottom");
            top = TestRules.Variant(data, "top");
        }

        private (WfcGrid grid, Wave wave, Propagator propagator) Create(int3 size)
        {
            var grid = new WfcGrid(size);
            var wave = new Wave(grid.CellCount, rules);
            return (grid, wave, new Propagator(rules, grid, wave));
        }

        private static void EnqueueAll(Propagator propagator, WfcGrid grid)
        {
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                propagator.Enqueue(cell);
            }
        }

        [Test]
        public void EmptyQueueChangesNothing()
        {
            var (grid, wave, propagator) = Create(new int3(2, 2, 2));
            Assert.That(propagator.Propagate(), Is.True);
            Assert.That(propagator.ChangedCells, Is.Empty);
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                Assert.That(wave.GetCount(cell), Is.EqualTo(2));
            }
        }

        [Test]
        public void TwoLayerColumnIsDeterminedByPropagationAlone()
        {
            var (grid, wave, propagator) = Create(new int3(1, 2, 1));
            EnqueueAll(propagator, grid);

            Assert.That(propagator.Propagate(), Is.True);
            int lower = grid.ToIndex(new int3(0, 0, 0));
            int upper = grid.ToIndex(new int3(0, 1, 0));
            Assert.That(wave.GetCount(lower), Is.EqualTo(1));
            Assert.That(wave.IsPossible(lower, bottom), Is.True);
            Assert.That(wave.GetCount(upper), Is.EqualTo(1));
            Assert.That(wave.IsPossible(upper, top), Is.True);
            Assert.That(propagator.ChangedCells, Is.EquivalentTo(new[] { lower, upper }));
        }

        [Test]
        public void ThreeLayerColumnIsAContradiction()
        {
            var (grid, _, propagator) = Create(new int3(1, 3, 1));
            EnqueueAll(propagator, grid);
            Assert.That(propagator.Propagate(), Is.False);
        }

        [Test]
        public void RemovingAVariantSpreadsToNeighbours()
        {
            // 2 wide, 2 tall: making one cell bottom forces its horizontal neighbour to bottom (bottom only sits next
            // to bottom), the cell above to top, and through those, the diagonal cell to top as well.
            var (grid, wave, propagator) = Create(new int3(2, 2, 1));
            int origin = grid.ToIndex(new int3(0, 0, 0));
            int right = grid.ToIndex(new int3(1, 0, 0));
            int above = grid.ToIndex(new int3(0, 1, 0));
            int diagonal = grid.ToIndex(new int3(1, 1, 0));

            wave.Ban(origin, top);
            propagator.Enqueue(origin);
            Assert.That(propagator.Propagate(), Is.True);

            Assert.That(wave.GetCount(above), Is.EqualTo(1));
            Assert.That(wave.IsPossible(above, top), Is.True, "only top fits above bottom");
            Assert.That(wave.GetCount(right), Is.EqualTo(1));
            Assert.That(wave.IsPossible(right, bottom), Is.True, "bottom only sits next to bottom horizontally");
            Assert.That(wave.IsPossible(diagonal, top), Is.True, "reached via its neighbours");
            Assert.That(wave.GetCount(diagonal), Is.EqualTo(1));
            Assert.That(propagator.ChangedCells, Is.EquivalentTo(new[] { above, right, diagonal }), "the enqueued origin itself wasn't shrunk by propagation");
        }

        [Test]
        public void CheckerboardFollowsFromOneCell()
        {
            RulesetData checker = TestRules.Checker().Data();
            var checkerRules = new CompiledRules(checker);
            int black = TestRules.Variant(checker, "black");
            int white = TestRules.Variant(checker, "white");
            var grid = new WfcGrid(new int3(3, 3, 3));
            var wave = new Wave(grid.CellCount, checkerRules);
            var propagator = new Propagator(checkerRules, grid, wave);

            wave.Ban(0, white);
            propagator.Enqueue(0);
            Assert.That(propagator.Propagate(), Is.True);

            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                int3 c = grid.ToCell(cell);
                int expected = (c.x + c.y + c.z) % 2 == 0 ? black : white;
                Assert.That(wave.GetCount(cell), Is.EqualTo(1), $"cell {c}");
                Assert.That(wave.IsPossible(cell, expected), Is.True, $"cell {c}");
            }
            Assert.That(propagator.ChangedCells.Count, Is.EqualTo(grid.CellCount - 1));
            Assert.That(propagator.ChangedCells.Distinct().Count(), Is.EqualTo(propagator.ChangedCells.Count), "each cell listed once");
        }

        [Test]
        public void ClearChangedAndClear()
        {
            var (grid, wave, propagator) = Create(new int3(1, 2, 1));
            EnqueueAll(propagator, grid);
            propagator.Propagate();
            propagator.ClearChanged();
            Assert.That(propagator.ChangedCells, Is.Empty);

            wave.Reset();
            EnqueueAll(propagator, grid);
            propagator.Clear();
            Assert.That(propagator.Propagate(), Is.True);
            Assert.That(wave.GetCount(0), Is.EqualTo(2), "the cleared queue wasn't processed");
        }
    }
}
