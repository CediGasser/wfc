using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Wfc.Authoring;
using Wfc.Tests.Solver;

namespace Wfc.Tests
{
    public class WfcGeneratorTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in created)
            {
                if (obj != null)
                {
                    Object.DestroyImmediate(obj);
                }
            }
            created.Clear();
        }

        /// <summary>An in-memory ruleset whose tiles all have a cube as model, in the data's tile order.</summary>
        private Ruleset CreateRuleset(TestRules rules, out RulesetData data, out TileDefinition[] tiles)
        {
            data = rules.Data();
            tiles = new TileDefinition[data.TileCount];
            for (int t = 0; t < tiles.Length; t++)
            {
                GameObject model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                model.name = data.tileNames[t];
                created.Add(model);
                tiles[t] = ScriptableObject.CreateInstance<TileDefinition>();
                tiles[t].name = data.tileNames[t];
                tiles[t].SetModel(model);
                created.Add(tiles[t]);
            }
            var ruleset = ScriptableObject.CreateInstance<Ruleset>();
            ruleset.Set(tiles, data, "test");
            created.Add(ruleset);
            return ruleset;
        }

        private WfcGenerator CreateGenerator(Ruleset ruleset, Vector3Int size, uint seed)
        {
            var generator = new GameObject("generator").AddComponent<WfcGenerator>();
            created.Add(generator.gameObject);
            generator.Ruleset = ruleset;
            generator.Size = size;
            generator.Seed = seed;
            return generator;
        }

        private static int[] Layout(WfcGenerator generator)
        {
            return Enumerable.Range(0, generator.Solver.Grid.CellCount).Select(generator.Solver.GetCollapsedVariant).ToArray();
        }

        [Test]
        public void GenerateInstantlyFillsEveryCell()
        {
            WfcGenerator generator = CreateGenerator(CreateRuleset(TestRules.Checker(), out _, out _), new Vector3Int(4, 3, 2), 1);
            generator.Generate(animate: false);

            Assert.That(generator.LastError, Is.Null);
            Assert.That(generator.Solver.Status, Is.EqualTo(SolverStatus.Done));
            Assert.That(generator.DecidedCount, Is.EqualTo(24));
            Assert.That(generator.Output.childCount, Is.EqualTo(24));
            Assert.That(generator.Output.gameObject.hideFlags & HideFlags.DontSave, Is.EqualTo(HideFlags.DontSave), "generated tiles aren't saved");
        }

        [Test]
        public void GeneratedTilesSitOnTheirCells()
        {
            WfcGenerator generator = CreateGenerator(CreateRuleset(TestRules.Checker(), out RulesetData data, out _), new Vector3Int(2, 2, 2), 3);
            generator.Generate(animate: false);
            foreach (Transform tile in generator.Output)
            {
                Vector3 position = tile.localPosition;
                int cell = generator.Solver.Grid.ToIndex(OrientationUnity.RoundToInt3(position));
                string expected = data.tileNames[data.variantTile[generator.Solver.GetCollapsedVariant(cell)]];
                Assert.That(tile.name, Does.StartWith(expected));
            }
        }

        [Test]
        public void SameSeedGivesTheSameLayout()
        {
            Ruleset ruleset = CreateRuleset(TestRules.HeavyLight(), out _, out _);
            WfcGenerator first = CreateGenerator(ruleset, new Vector3Int(5, 5, 5), 9);
            WfcGenerator second = CreateGenerator(ruleset, new Vector3Int(5, 5, 5), 9);
            first.Generate(animate: false);
            second.Generate(animate: false);
            Assert.That(Layout(first), Is.EqualTo(Layout(second)));
        }

        [Test]
        public void StepOnceAdvancesGradually()
        {
            WfcGenerator generator = CreateGenerator(CreateRuleset(TestRules.HeavyLight(), out _, out _), new Vector3Int(3, 3, 3), 2);
            generator.StepOnce();
            Assert.That(generator.Steps, Is.EqualTo(1));
            Assert.That(generator.DecidedCount, Is.EqualTo(1));
            Assert.That(generator.Output.childCount, Is.EqualTo(1));
            generator.StepOnce();
            Assert.That(generator.DecidedCount, Is.EqualTo(2));
            generator.RunToEnd();
            Assert.That(generator.Solver.Status, Is.EqualTo(SolverStatus.Done));
            Assert.That(generator.Output.childCount, Is.EqualTo(27));
        }

        [Test]
        public void ClearRemovesTheOutput()
        {
            WfcGenerator generator = CreateGenerator(CreateRuleset(TestRules.Checker(), out _, out _), new Vector3Int(2, 2, 2), 1);
            generator.Generate(animate: false);
            generator.Clear();
            Assert.That(generator.Output, Is.Null);
            Assert.That(generator.Solver, Is.Null);
        }

        [Test]
        public void BoundaryTileConstrainsTheEdge()
        {
            Ruleset ruleset = CreateRuleset(TestRules.Layers(), out RulesetData data, out TileDefinition[] tiles);
            WfcGenerator generator = CreateGenerator(ruleset, new Vector3Int(3, 1, 3), 1);
            TileDefinition bottom = tiles[System.Array.IndexOf(data.tileNames, "bottom")];
            generator.SetBoundary(Direction.NegY, new BoundarySide(BoundaryMode.Tile, bottom));
            generator.Generate(animate: false);

            Assert.That(generator.Solver.Status, Is.EqualTo(SolverStatus.Done));
            Assert.That(Layout(generator), Is.All.EqualTo(TestRules.Variant(data, "top")));
        }

        [Test]
        public void FailedGenerationReportsTheContradictionCell()
        {
            // Three layers of top/bottom tiles can never work: the generation gives up after its attempts.
            WfcGenerator generator = CreateGenerator(CreateRuleset(TestRules.Layers(), out _, out _), new Vector3Int(1, 3, 1), 1);
            generator.Generate(animate: false);

            Assert.That(generator.Solver.Status, Is.EqualTo(SolverStatus.Contradiction));
            Assert.That(generator.Attempts, Is.EqualTo(10), "default max attempts");
            Assert.That(generator.TryGetContradiction(out Unity.Mathematics.int3 cell), Is.True);
            Assert.That(generator.Solver.Wave.GetCount(generator.Solver.Grid.ToIndex(cell)), Is.EqualTo(0));
        }

        [Test]
        public void SuccessfulGenerationHasNoContradiction()
        {
            WfcGenerator generator = CreateGenerator(CreateRuleset(TestRules.Checker(), out _, out _), new Vector3Int(3, 3, 3), 1);
            generator.Generate(animate: false);
            Assert.That(generator.TryGetContradiction(out _), Is.False);
        }

        [Test]
        public void MissingRulesetReportsAnError()
        {
            WfcGenerator generator = CreateGenerator(null, new Vector3Int(2, 2, 2), 1);
            generator.Generate(animate: false);
            Assert.That(generator.LastError, Is.Not.Null);
            Assert.That(generator.Solver, Is.Null);
        }
    }
}
