using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;

namespace Wfc.Tests.Solver
{
    [Category("Solver")]
    public class WfcSolverTests
    {
        private static WfcSolver Create(TestRules rules, int3 size, uint seed, out RulesetData data)
        {
            data = rules.Data();
            return new WfcSolver(new CompiledRules(data), size, seed);
        }

        private static int[] Result(WfcSolver solver)
        {
            return Enumerable.Range(0, solver.Grid.CellCount).Select(solver.GetCollapsedVariant).ToArray();
        }

        // Observe and collapse -----------------------------------------------------------------------------------

        [Test]
        public void StartsRunningWithEverythingPossible()
        {
            WfcSolver solver = Create(TestRules.HeavyLight(), new int3(2, 3, 4), 1, out _);
            Assert.That(solver.Status, Is.EqualTo(SolverStatus.Running));
            Assert.That(solver.Seed, Is.EqualTo(1u));
            Assert.That(solver.Grid.CellCount, Is.EqualTo(24));
            Assert.That(solver.Wave.GetCount(0), Is.EqualTo(2));
            Assert.That(solver.GetCollapsedVariant(0), Is.EqualTo(-1));
        }

        [Test]
        public void LowestEntropyCellIsTheMostConstrainedOne()
        {
            // a, b and c are compatible with everything, so constraining one cell to two options doesn't spread.
            WfcSolver solver = Create(TestRules.WeightedConnections(), new int3(4, 4, 4), 3, out RulesetData data);
            int constrained = solver.Grid.ToIndex(new int3(2, 1, 3));
            Assert.That(solver.Constrain(constrained, new[] { TestRules.Variant(data, "a"), TestRules.Variant(data, "b") }), Is.True);
            Assert.That(solver.FindLowestEntropyCell(), Is.EqualTo(constrained));
        }

        [Test]
        public void LowestEntropyIgnoresDecidedCells()
        {
            WfcSolver solver = Create(TestRules.WeightedConnections(), new int3(2, 1, 1), 3, out RulesetData data);
            Assert.That(solver.Constrain(0, new[] { TestRules.Variant(data, "a") }), Is.True);
            Assert.That(solver.FindLowestEntropyCell(), Is.EqualTo(1), "cell 0 is decided, so cell 1 is the only candidate");
            solver.CollapseCell(1);
            Assert.That(solver.FindLowestEntropyCell(), Is.EqualTo(-1));
        }

        [Test]
        public void TiesAreBrokenTheSameWayForTheSameSeed()
        {
            int First(uint seed) => Create(TestRules.HeavyLight(), new int3(6, 6, 6), seed, out _).FindLowestEntropyCell();
            Assert.That(First(7), Is.EqualTo(First(7)));
            Assert.That(Enumerable.Range(1, 10).Select(s => First((uint)s)).Distinct().Count(), Is.GreaterThan(1),
                "different seeds should start at different cells");
        }

        [Test]
        public void CollapseLeavesExactlyOnePossibleVariant()
        {
            WfcSolver solver = Create(TestRules.ManyVariants(), new int3(1, 1, 1), 5, out _);
            solver.Constrain(0, new[] { 10, 20, 30 });
            int chosen = solver.CollapseCell(0);
            Assert.That(new[] { 10, 20, 30 }, Does.Contain(chosen), "never a banned variant");
            Assert.That(solver.Wave.GetCount(0), Is.EqualTo(1));
            Assert.That(solver.GetCollapsedVariant(0), Is.EqualTo(chosen));
        }

        [Test]
        public void CollapseFollowsVariantWeights()
        {
            // heavy (3) vs light (1), always compatible: about 75% heavy.
            WfcSolver solver = Create(TestRules.HeavyLight(), new int3(10, 10, 10), 11, out RulesetData data);
            Assert.That(solver.Run(), Is.EqualTo(SolverStatus.Done));
            int heavy = TestRules.Variant(data, "heavy");
            float share = Result(solver).Count(v => v == heavy) / 1000f;
            Assert.That(share, Is.EqualTo(0.75f).Within(0.06f));
        }

        [Test]
        public void CollapseFollowsConnectionWeightsOfDecidedNeighbours()
        {
            // Next to a decided "a", the connection weights are a:1, b:1, c:9, so c should win about 9 out of 11 times.
            int cCount = 0;
            const int trials = 400;
            for (uint seed = 1; seed <= trials; seed++)
            {
                WfcSolver solver = Create(TestRules.WeightedConnections(), new int3(2, 1, 1), seed, out RulesetData data);
                solver.Constrain(1, new[] { TestRules.Variant(data, "a") });
                if (solver.CollapseCell(0) == TestRules.Variant(data, "c"))
                {
                    cCount++;
                }
            }
            Assert.That(cCount / (float)trials, Is.EqualTo(9f / 11f).Within(0.07f));
        }

        // Step, status and restarts ------------------------------------------------------------------------------

        [Test]
        public void SingleSelfConnectedTileFillsTheGrid()
        {
            WfcSolver solver = Create(TestRules.SingleTile(), new int3(4, 3, 2), 1, out RulesetData data);
            Assert.That(solver.Run(), Is.EqualTo(SolverStatus.Done));
            Assert.That(Result(solver), Is.All.EqualTo(TestRules.Variant(data, "block")));
        }

        [Test]
        public void CheckerboardResultsAreValid()
        {
            for (uint seed = 1; seed <= 5; seed++)
            {
                WfcSolver solver = Create(TestRules.Checker(), new int3(3, 4, 5), seed, out RulesetData data);
                Assert.That(solver.Run(), Is.EqualTo(SolverStatus.Done), $"seed {seed}");
                SolutionAssert.IsValid(solver, data);
            }
        }

        [Test]
        public void RandomResultsAreValid()
        {
            for (uint seed = 1; seed <= 5; seed++)
            {
                WfcSolver solver = Create(TestRules.WeightedConnections(), new int3(5, 5, 5), seed, out RulesetData data);
                Assert.That(solver.Run(), Is.EqualTo(SolverStatus.Done), $"seed {seed}");
                SolutionAssert.IsValid(solver, data);
            }
        }

        [Test]
        public void SameSeedGivesTheSameResult()
        {
            int[] Solve(uint seed)
            {
                WfcSolver solver = Create(TestRules.HeavyLight(), new int3(6, 6, 6), seed, out _);
                solver.Run();
                return Result(solver);
            }
            Assert.That(Solve(42), Is.EqualTo(Solve(42)));
            Assert.That(Solve(42), Is.Not.EqualTo(Solve(43)));
        }

        [Test]
        public void StepReportsChangedCells()
        {
            WfcSolver solver = Create(TestRules.Checker(), new int3(3, 3, 3), 2, out _);
            Assert.That(solver.Step(), Is.EqualTo(SolverStatus.Done), "one collapse decides the whole checkerboard");
            Assert.That(solver.ChangedCells.Count, Is.EqualTo(27), "the collapsed cell plus every propagated cell");
            Assert.That(solver.ChangedCells.Distinct().Count(), Is.EqualTo(27));
        }

        [Test]
        public void StepAfterTheEndDoesNothing()
        {
            WfcSolver solver = Create(TestRules.SingleTile(), new int3(2, 2, 2), 1, out _);
            solver.Run();
            int[] before = Result(solver);
            Assert.That(solver.Step(), Is.EqualTo(SolverStatus.Done));
            Assert.That(Result(solver), Is.EqualTo(before));
        }

        [Test]
        public void RunStopsAfterMaxSteps()
        {
            WfcSolver solver = Create(TestRules.HeavyLight(), new int3(5, 5, 5), 1, out _);
            Assert.That(solver.Run(maxSteps: 3), Is.EqualTo(SolverStatus.Running));
            int decided = Result(solver).Count(v => v >= 0);
            Assert.That(decided, Is.EqualTo(3), "heavy/light never propagates, so each step decides exactly one cell");
        }

        [Test]
        public void UnsolvableRulesAreDetectedOnTheFirstStep()
        {
            // The rod only connects along X, so a 1×2×1 column has nothing that may sit above or below it.
            WfcSolver solver = Create(TestRules.XOnly(), new int3(1, 2, 1), 1, out _);
            Assert.That(solver.Step(), Is.EqualTo(SolverStatus.Contradiction));
            Assert.That(solver.Status, Is.EqualTo(SolverStatus.Contradiction));
            Assert.That(solver.Step(), Is.EqualTo(SolverStatus.Contradiction), "stays in contradiction");
        }

        [Test]
        public void ContradictingConstraintIsReported()
        {
            WfcSolver solver = Create(TestRules.Checker(), new int3(2, 1, 1), 1, out RulesetData data);
            int black = TestRules.Variant(data, "black");
            Assert.That(solver.Constrain(0, new[] { black }), Is.True);
            Assert.That(solver.ContradictionCell, Is.EqualTo(-1));
            Assert.That(solver.Constrain(1, new[] { black }), Is.False, "two black cells can't be neighbours");
            Assert.That(solver.Status, Is.EqualTo(SolverStatus.Contradiction));
            Assert.That(solver.ContradictionCell, Is.EqualTo(1));
        }

        [Test]
        public void ContradictionCellIsTheCellWithoutVariants()
        {
            WfcSolver solver = Create(TestRules.Layers(), new int3(1, 3, 1), 1, out _);
            Assert.That(solver.Run(), Is.EqualTo(SolverStatus.Contradiction));
            Assert.That(solver.ContradictionCell, Is.GreaterThanOrEqualTo(0));
            Assert.That(solver.Wave.GetCount(solver.ContradictionCell), Is.EqualTo(0), "the reported cell has no variant left");
            for (int cell = 0; cell < solver.Grid.CellCount; cell++)
            {
                if (cell != solver.ContradictionCell)
                {
                    Assert.That(solver.Wave.GetCount(cell), Is.GreaterThan(0), "propagation stops at the first empty cell");
                }
            }

            solver.Reset(2);
            Assert.That(solver.ContradictionCell, Is.EqualTo(-1));
        }

        [Test]
        public void ResetKeepsConstraintsAndStartsOver()
        {
            WfcSolver solver = Create(TestRules.HeavyLight(), new int3(4, 4, 4), 1, out RulesetData data);
            int light = TestRules.Variant(data, "light");
            solver.Constrain(10, new[] { light });
            solver.Run();

            solver.Reset(99);
            Assert.That(solver.Status, Is.EqualTo(SolverStatus.Running));
            Assert.That(solver.Seed, Is.EqualTo(99u));
            Assert.That(solver.GetCollapsedVariant(10), Is.EqualTo(light), "constraint re-applied");
            Assert.That(Result(solver).Count(v => v >= 0), Is.EqualTo(1), "everything else is open again");
            Assert.That(solver.Run(), Is.EqualTo(SolverStatus.Done));
            Assert.That(solver.GetCollapsedVariant(10), Is.EqualTo(light));
        }

        [Test]
        public void ResetClearsAContradiction()
        {
            WfcSolver solver = Create(TestRules.XOnly(), new int3(1, 2, 1), 1, out _);
            Assert.That(solver.Run(), Is.EqualTo(SolverStatus.Contradiction));
            solver.Reset(2);
            Assert.That(solver.Status, Is.EqualTo(SolverStatus.Running));
        }

        // Boundaries ---------------------------------------------------------------------------------------------

        [Test]
        public void SideConstraintActsLikeAnOutsideLayer()
        {
            // With "bottom" just below the grid, the single layer can only be "top".
            WfcSolver solver = Create(TestRules.Layers(), new int3(2, 1, 2), 1, out RulesetData data);
            int bottom = TestRules.Variant(data, "bottom");
            Assert.That(solver.ConstrainSide(Direction.NegY, new[] { bottom }), Is.True);
            Assert.That(Result(solver), Is.All.EqualTo(TestRules.Variant(data, "top")));
            Assert.That(solver.Run(), Is.EqualTo(SolverStatus.Done));
        }

        [Test]
        public void SideConstraintPropagatesInwards()
        {
            // Black just outside +X makes the edge cell white, and the checkerboard rules carry it through the row.
            WfcSolver solver = Create(TestRules.Checker(), new int3(3, 1, 1), 4, out RulesetData data);
            int black = TestRules.Variant(data, "black");
            int white = TestRules.Variant(data, "white");
            Assert.That(solver.ConstrainSide(Direction.PosX, new[] { black }), Is.True);
            Assert.That(Result(solver), Is.EqualTo(new[] { white, black, white }));
        }

        [Test]
        public void UniformOutsideLayerCanBeImpossible()
        {
            // A whole face of white cells would touch each other, which a checkerboard forbids.
            WfcSolver solver = Create(TestRules.Checker(), new int3(3, 3, 3), 4, out RulesetData data);
            Assert.That(solver.ConstrainSide(Direction.PosX, new[] { TestRules.Variant(data, "black") }), Is.False);
            Assert.That(solver.Status, Is.EqualTo(SolverStatus.Contradiction));
        }

        [Test]
        public void SideConstraintSurvivesReset()
        {
            WfcSolver solver = Create(TestRules.Layers(), new int3(2, 1, 2), 1, out RulesetData data);
            solver.ConstrainSide(Direction.NegY, new[] { TestRules.Variant(data, "bottom") });
            solver.Reset(5);
            Assert.That(Result(solver), Is.All.EqualTo(TestRules.Variant(data, "top")));
        }

        [Test]
        public void ImpossibleSideConstraintIsAContradiction()
        {
            // Nothing may sit above "top", so a "top" layer below the grid can't work.
            WfcSolver solver = Create(TestRules.Layers(), new int3(2, 1, 2), 1, out RulesetData data);
            Assert.That(solver.ConstrainSide(Direction.NegY, new[] { TestRules.Variant(data, "top") }), Is.False);
            Assert.That(solver.Status, Is.EqualTo(SolverStatus.Contradiction));
        }

        // Restarts -------------------------------------------------------------------------------------------------

        [Test]
        public void RestartsGiveUpAfterMaxAttempts()
        {
            WfcSolver solver = Create(TestRules.XOnly(), new int3(1, 2, 1), 1, out _);
            Assert.That(solver.RunWithRestarts(5, out int attempts), Is.EqualTo(SolverStatus.Contradiction));
            Assert.That(attempts, Is.EqualTo(5));
        }

        [Test]
        public void SolvableRulesNeedOneAttempt()
        {
            WfcSolver solver = Create(TestRules.Checker(), new int3(4, 4, 4), 1, out RulesetData data);
            Assert.That(solver.RunWithRestarts(5, out int attempts), Is.EqualTo(SolverStatus.Done));
            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(solver.Seed, Is.EqualTo(1u), "no restart, so the seed is unchanged");
            SolutionAssert.IsValid(solver, data);
        }

        [Test]
        public void RestartSeedsAreDeterministic()
        {
            uint FinalSeed()
            {
                WfcSolver solver = Create(TestRules.XOnly(), new int3(1, 2, 1), 17, out _);
                solver.RunWithRestarts(3, out _);
                return solver.Seed;
            }
            Assert.That(FinalSeed(), Is.EqualTo(FinalSeed()));
            Assert.That(FinalSeed(), Is.Not.EqualTo(17u), "restarts use new seeds");
        }
    }
}
