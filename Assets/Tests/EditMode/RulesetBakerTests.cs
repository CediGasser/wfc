using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Wfc.Authoring;
using Wfc.Editor;

namespace Wfc.Tests
{
    public class RulesetBakerTests
    {
        private readonly TestObjects objects = new TestObjects();

        [TearDown]
        public void TearDown() => objects.DestroyAll();

        private SampleRuleSource Source(TileSet set, int3 size)
        {
            Chunk chunk = objects.Chunk(set, size);
            return chunk.gameObject.AddComponent<SampleRuleSource>();
        }

        [Test]
        public void AnalyzeLearnsFromTheChunk()
        {
            TileDefinition block = objects.Definition("block", OrientationPreset.YawMirror);
            TileDefinition air = objects.Definition("air", withModel: false);
            SampleRuleSource source = Source(objects.TileSet(block, air), new int3(2, 2, 1));
            source.Chunk.SetCell(new int3(0, 0, 0), 0, Orientation.Identity);
            source.EmptyCellTile = air;

            RuleSourceAnalysis analysis = RulesetBaker.Analyze(source, detectSymmetry: false);

            Assert.That(analysis.specs.Select(s => s.name), Is.EqualTo(new[] { "block", "air" }));
            Assert.That(analysis.specs[1].isInvisible, Is.True);
            Assert.That(analysis.result.pairs.Count, Is.EqualTo(4), "every neighbouring pair in a 2 × 2 box");
            Assert.That(analysis.result.cellVariant[0], Is.EqualTo(analysis.Data.FindVariant(0, Orientation.Identity)));
            Assert.That(analysis.Data.connections.Length, Is.EqualTo(3), "block-air sideways, block-air above, air-air");
            Assert.That(analysis.IsBakedAssetUpToDate, Is.False);
        }

        [Test]
        public void WithoutAnEmptyCellTileEmptyCellsCarryNoInformation()
        {
            TileDefinition block = objects.Definition("block");
            SampleRuleSource source = Source(objects.TileSet(block, objects.Definition("air", withModel: false)), new int3(3, 1, 1));
            source.Chunk.SetCell(0, 0, Orientation.Identity);
            source.Chunk.SetCell(2, 0, Orientation.Identity);

            RuleSourceAnalysis analysis = RulesetBaker.Analyze(source, detectSymmetry: false);

            Assert.That(analysis.result.pairs, Is.Empty);
            Assert.That(analysis.Data.GetVariants(1), Is.Empty, "air isn't used");
        }

        [Test]
        public void OverridesOnTheSourceAreApplied()
        {
            TileDefinition block = objects.Definition("block", OrientationPreset.YawMirror);
            SampleRuleSource source = Source(objects.TileSet(block), new int3(2, 1, 1));
            source.Chunk.SetCell(0, 0, Orientation.Identity);
            source.Chunk.SetCell(1, 0, Orientation.Identity);

            ConnectionKey key = RulesetBaker.Analyze(source, detectSymmetry: false).Data.connections.Single().key;
            source.SetOverride(key, 3f);

            Connection connection = RulesetBaker.Analyze(source, detectSymmetry: false).Data.connections.Single();
            Assert.That(connection.weight, Is.EqualTo(3f));
            Assert.That(connection.overridden, Is.True);
            Assert.That(source.TryGetOverride(key.Reversed, out float weight) && weight == 3f, Is.True);
        }

        [Test]
        public void MissingTilesAreReported()
        {
            TileDefinition block = objects.Definition("block");
            TileSet set = objects.TileSet(block, objects.Definition("gone"));
            SampleRuleSource source = Source(set, new int3(2, 1, 1));
            source.Chunk.SetCell(0, 0, Orientation.Identity);
            source.Chunk.SetCell(1, 1, Orientation.Identity);
            set.RemoveAt(1);

            RuleDiagnostic missing = RulesetBaker.Analyze(source, detectSymmetry: false).Data.diagnostics.Single(d => d.code == DiagnosticCode.MissingTile);
            Assert.That(missing.cells, Is.EqualTo(new[] { 1 }));

            source.EmptyCellTile = objects.Definition("elsewhere");
            source.Chunk.ClearCell(1);
            Assert.That(RulesetBaker.Analyze(source, detectSymmetry: false).Data.diagnostics.Count(d => d.code == DiagnosticCode.MissingTile), Is.EqualTo(1),
                "an empty-cell tile outside the tile set");

            source.Chunk.TileSet = null;
            Assert.That(RulesetBaker.Analyze(source, detectSymmetry: false).Data.diagnostics.Any(d => d.code == DiagnosticCode.MissingTile && d.severity == DiagnosticSeverity.Error), Is.True);
        }

        [Test]
        public void BakeWritesTheRulesetAsset()
        {
            TileSet set = objects.TileSet(objects.Definition("block"));
            SampleRuleSource source = Source(set, new int3(2, 1, 1));
            source.Chunk.SetCell(0, 0, Orientation.Identity);
            source.Chunk.SetCell(1, 0, Orientation.Identity);
            source.Ruleset = objects.Track(ScriptableObject.CreateInstance<Ruleset>());

            Ruleset baked = RulesetBaker.Bake(source);

            Assert.That(baked, Is.SameAs(source.Ruleset));
            Assert.That(baked.TileSet, Is.SameAs(set));
            Assert.That(baked.Data.connections.Length, Is.EqualTo(1));
            Assert.That(RulesetBaker.Analyze(source, detectSymmetry: false).IsBakedAssetUpToDate, Is.True);
        }

        [Test]
        public void SocketSourceBakesRulesAGeneratorCanUse()
        {
            TileDefinition road = objects.Definition("road", OrientationPreset.Yaw), grass = objects.Definition("grass", withModel: false);
            TileSet set = objects.TileSet(road, grass, objects.Definition("unused"));
            var source = objects.Track(new GameObject("sockets")).AddComponent<SocketRuleSource>();
            source.SetTileSet(set);
            // A straight road along X between grass sides, flat ground above and below.
            foreach (var (face, label) in new[] { (Direction.PosX, "road"), (Direction.NegX, "road"), (Direction.PosZ, "verge"), (Direction.NegZ, "verge"), (Direction.PosY, "sky"), (Direction.NegY, "ground") })
            {
                source.SetLabel(road, face, label);
            }
            foreach (Direction face in new[] { Direction.PosX, Direction.NegX, Direction.PosZ, Direction.NegZ })
            {
                source.SetLabel(grass, face, "grass");
            }
            source.SetLabel(grass, Direction.PosY, "sky");
            source.SetLabel(grass, Direction.NegY, "ground");
            source.AddCompatible("verge", "grass");
            source.AddCompatible("sky", "ground");
            source.Ruleset = objects.Track(ScriptableObject.CreateInstance<Ruleset>());

            RuleSourceAnalysis analysis = RulesetBaker.Analyze(source, detectSymmetry: false);
            Assert.That(analysis.Data.GetVariants(2), Is.Empty, "tiles without sockets take no part");
            Assert.That(analysis.Data.connections.All(c => c.observations == 0), Is.True);
            Assert.That(analysis.Data.diagnostics.Where(d => d.severity == DiagnosticSeverity.Error), Is.Empty);

            Ruleset baked = RulesetBaker.Bake(source);
            var generator = objects.Track(new GameObject("generator")).AddComponent<WfcGenerator>();
            generator.Ruleset = baked;
            generator.Chunk.TileSet = set;
            generator.Chunk.Resize(new int3(5, 1, 5), int3.zero);
            generator.Generate(animate: false);

            Assert.That(generator.Solver.Status, Is.EqualTo(SolverStatus.Done));
            Tests.Solver.SolutionAssert.IsValid(generator.Solver, baked.Data);
        }
    }
}
