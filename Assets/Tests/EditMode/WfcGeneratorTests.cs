using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Wfc.Authoring;
using Wfc.Tests.Solver;

namespace Wfc.Tests
{
    public class WfcGeneratorTests
    {
        private readonly TestObjects objects = new TestObjects();

        [TearDown]
        public void TearDown() => objects.DestroyAll();

        /// <summary>An in-memory ruleset whose tiles all have a cube as model, with a tile set in the data's tile order.</summary>
        private Ruleset CreateRuleset(TestRules rules, out RulesetData data)
        {
            data = rules.Data();
            TileSet tileSet = objects.TileSet(data.tileNames.Select(name => objects.Definition(name)).ToArray());
            var ruleset = objects.Track(ScriptableObject.CreateInstance<Ruleset>());
            ruleset.Set(tileSet, data, "test");
            return ruleset;
        }

        private WfcGenerator CreateGenerator(Ruleset ruleset, int3 size, uint seed)
        {
            var generator = objects.Track(new GameObject("generator")).AddComponent<WfcGenerator>();
            generator.Ruleset = ruleset;
            generator.Seed = seed;
            generator.Chunk.TileSet = ruleset != null ? ruleset.TileSet : null;
            generator.Chunk.Resize(size, int3.zero);
            return generator;
        }

        private static int[] Layout(WfcGenerator generator)
        {
            return Enumerable.Range(0, generator.Solver.Grid.CellCount).Select(generator.Solver.GetCollapsedVariant).ToArray();
        }

        private static int TileIndex(RulesetData data, string name) => System.Array.IndexOf(data.tileNames, name);

        [Test]
        public void GeneratorBringsItsChunk()
        {
            var generator = objects.Track(new GameObject("generator")).AddComponent<WfcGenerator>();
            Assert.That(generator.GetComponent<Chunk>(), Is.Not.Null);
        }

        [Test]
        public void GenerateInstantlyFillsTheChunk()
        {
            WfcGenerator generator = CreateGenerator(CreateRuleset(TestRules.Checker(), out _), new int3(4, 3, 2), 1);
            generator.Generate(animate: false);

            Assert.That(generator.LastError, Is.Null);
            Assert.That(generator.Solver.Status, Is.EqualTo(SolverStatus.Done));
            Assert.That(generator.DecidedCount, Is.EqualTo(24));
            Assert.That(generator.Result.CountNonEmpty(), Is.EqualTo(24));
            Assert.That(generator.Chunk.Cells.CountNonEmpty(), Is.Zero, "the result isn't authored");
        }

        [Test]
        public void GeneratedCellsMatchTheSolver()
        {
            WfcGenerator generator = CreateGenerator(CreateRuleset(TestRules.Checker(), out RulesetData data), new int3(2, 2, 2), 3);
            generator.Generate(animate: false);
            for (int cell = 0; cell < 8; cell++)
            {
                int variant = generator.Solver.GetCollapsedVariant(cell);
                Assert.That(generator.Result.GetTile(cell), Is.EqualTo(data.variantTile[variant]));
            }
        }

        [Test]
        public void SameSeedGivesTheSameLayout()
        {
            Ruleset ruleset = CreateRuleset(TestRules.HeavyLight(), out _);
            WfcGenerator first = CreateGenerator(ruleset, new int3(5, 5, 5), 9);
            WfcGenerator second = CreateGenerator(ruleset, new int3(5, 5, 5), 9);
            first.Generate(animate: false);
            second.Generate(animate: false);
            Assert.That(Layout(first), Is.EqualTo(Layout(second)));
        }

        [Test]
        public void StepOnceAdvancesGradually()
        {
            WfcGenerator generator = CreateGenerator(CreateRuleset(TestRules.HeavyLight(), out _), new int3(3, 3, 3), 2);
            generator.StepOnce();
            Assert.That(generator.Steps, Is.EqualTo(1));
            Assert.That(generator.DecidedCount, Is.EqualTo(1));
            Assert.That(generator.Result.CountNonEmpty(), Is.EqualTo(1));
            generator.StepOnce();
            Assert.That(generator.DecidedCount, Is.EqualTo(2));
            generator.RunToEnd();
            Assert.That(generator.Solver.Status, Is.EqualTo(SolverStatus.Done));
            Assert.That(generator.Result.CountNonEmpty(), Is.EqualTo(27));
        }

        [Test]
        public void ClearRemovesTheResult()
        {
            WfcGenerator generator = CreateGenerator(CreateRuleset(TestRules.Checker(), out _), new int3(2, 2, 2), 1);
            generator.Generate(animate: false);
            generator.Clear();
            Assert.That(generator.Result, Is.Null);
            Assert.That(generator.Solver, Is.Null);
        }

        [Test]
        public void BoundaryTileConstrainsTheEdge()
        {
            Ruleset ruleset = CreateRuleset(TestRules.Layers(), out RulesetData data);
            WfcGenerator generator = CreateGenerator(ruleset, new int3(3, 1, 3), 1);
            TileDefinition bottom = ruleset.TileSet[TileIndex(data, "bottom")];
            generator.SetBoundary(Direction.NegY, new BoundarySide(BoundaryMode.Tile, bottom));
            generator.Generate(animate: false);

            Assert.That(generator.Solver.Status, Is.EqualTo(SolverStatus.Done));
            Assert.That(Layout(generator), Is.All.EqualTo(TestRules.Variant(data, "top")));
        }

        [Test]
        public void FailedGenerationReportsTheContradictionCell()
        {
            // Three layers of top/bottom tiles can never work: the generation gives up after its attempts.
            WfcGenerator generator = CreateGenerator(CreateRuleset(TestRules.Layers(), out _), new int3(1, 3, 1), 1);
            generator.Generate(animate: false);

            Assert.That(generator.Solver.Status, Is.EqualTo(SolverStatus.Contradiction));
            Assert.That(generator.Attempts, Is.EqualTo(10), "default max attempts");
            Assert.That(generator.TryGetContradiction(out int3 cell), Is.True);
            Assert.That(generator.Solver.Wave.GetCount(generator.Solver.Grid.ToIndex(cell)), Is.EqualTo(0));
        }

        [Test]
        public void SuccessfulGenerationHasNoContradiction()
        {
            WfcGenerator generator = CreateGenerator(CreateRuleset(TestRules.Checker(), out _), new int3(3, 3, 3), 1);
            generator.Generate(animate: false);
            Assert.That(generator.TryGetContradiction(out _), Is.False);
        }

        [Test]
        public void MissingRulesetReportsAnError()
        {
            WfcGenerator generator = CreateGenerator(null, new int3(2, 2, 2), 1);
            generator.Generate(animate: false);
            Assert.That(generator.LastError, Is.Not.Null);
            Assert.That(generator.Solver, Is.Null);
        }

        [Test]
        public void ChunkMustUseTheRulesetsTileSet()
        {
            WfcGenerator generator = CreateGenerator(CreateRuleset(TestRules.Checker(), out _), new int3(2, 2, 2), 1);
            generator.Chunk.TileSet = objects.TileSet(objects.Definition("other"));
            generator.Generate(animate: false);
            Assert.That(generator.LastError, Does.Contain("tile set"));
            Assert.That(generator.Solver, Is.Null);
        }

        [Test]
        public void PaintedCellsAreKept()
        {
            WfcGenerator generator = CreateGenerator(CreateRuleset(TestRules.Checker(), out RulesetData data), new int3(3, 3, 1), 4);
            int white = TileIndex(data, "white");
            generator.Chunk.SetCell(new int3(1, 1, 0), white, Orientation.Identity);
            generator.Generate(animate: false);

            Assert.That(generator.LastError, Is.Null);
            Assert.That(generator.Solver.Status, Is.EqualTo(SolverStatus.Done));
            SolutionAssert.IsValid(generator.Solver, data);
            Assert.That(generator.GetOutput().GetTile(generator.Chunk.Cells.ToIndex(new int3(1, 1, 0))), Is.EqualTo(white));
            Assert.That(generator.GetOutput().CountNonEmpty(), Is.EqualTo(9));
        }

        [Test]
        public void InvalidPaintIsReportedAndGeneratedOver()
        {
            WfcGenerator generator = CreateGenerator(CreateRuleset(TestRules.Layers(), out RulesetData data), new int3(2, 2, 1), 1);
            // "bottom" is fixed, so a rotated one doesn't exist in the rules.
            generator.Chunk.SetCell(new int3(0, 0, 0), TileIndex(data, "bottom"), Orientation.RotationY90);
            generator.Generate(animate: false);

            Assert.That(generator.LastError, Is.Not.Null);
            Assert.That(generator.InvalidPaintedCells, Is.EqualTo(new[] { 0 }));
            Assert.That(generator.Solver.Status, Is.EqualTo(SolverStatus.Done));
        }

        [Test]
        public void ContradictingPaintFailsWithoutRetrying()
        {
            WfcGenerator generator = CreateGenerator(CreateRuleset(TestRules.Checker(), out RulesetData data), new int3(2, 1, 1), 1);
            int black = TileIndex(data, "black");
            generator.Chunk.SetCell(0, black, Orientation.Identity);
            generator.Chunk.SetCell(1, black, Orientation.Identity);
            generator.Generate(animate: false);

            Assert.That(generator.Solver.Status, Is.EqualTo(SolverStatus.Contradiction));
            Assert.That(generator.Attempts, Is.EqualTo(1));
            Assert.That(generator.LastError, Does.Contain("contradict"));
        }

        [Test]
        public void DisablingHidesTheResultWithoutLosingIt()
        {
            WfcGenerator generator = CreateGenerator(CreateRuleset(TestRules.HeavyLight(), out _), new int3(3, 2, 2), 5);
            generator.gameObject.AddComponent<ChunkView>();
            generator.Generate(animate: false);
            var view = generator.GetComponent<ChunkView>();
            Assert.That(view.Container.childCount, Is.EqualTo(12));
            ChunkData output = generator.GetOutput();

            generator.enabled = false;
            Assert.That(view.Container == null || view.Container.childCount == 0, Is.True, "the result is hidden");
            Assert.That(generator.Result, Is.Not.Null);

            generator.enabled = true;
            Assert.That(view.Container.childCount, Is.EqualTo(12));
            Assert.That(generator.GetOutput().ContentEquals(output), Is.True, "the same result comes back");
        }

        [Test]
        public void KeepResultTurnsTheResultIntoPaintedCells()
        {
            WfcGenerator generator = CreateGenerator(CreateRuleset(TestRules.HeavyLight(), out _), new int3(3, 2, 2), 5);
            generator.Generate(animate: false);
            ChunkData output = generator.GetOutput();

            Assert.That(generator.KeepResult(), Is.EqualTo(12));
            Assert.That(generator.Chunk.Cells.ContentEquals(output), Is.True);
            Assert.That(generator.Result, Is.Null);
            Assert.That(generator.Solver, Is.Null);

            // Generating again keeps everything, since every cell is painted now.
            generator.Seed = 77;
            generator.Generate(animate: false);
            Assert.That(generator.GetOutput().ContentEquals(output), Is.True);
        }
    }
}
