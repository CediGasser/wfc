using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Wfc.Authoring;
using Wfc.Editor;
using Wfc.Tests.Solver;

namespace Wfc.Tests
{
    public class ChunkEditingTests
    {
        private readonly TestObjects objects = new TestObjects();

        [TearDown]
        public void TearDown()
        {
            ChunkToolState.ClearSelection();
            objects.DestroyAll();
        }

        [Test]
        public void TransformRotatesTilesAndPaintsGeneratedOnes()
        {
            WfcGenerator generator = objects.Generator(TestRules.Checker(), new int3(3, 1, 1), 1, out _);
            generator.Generate(animate: false);
            Chunk chunk = generator.Chunk;
            int generated = generator.Result.GetTile(1);
            chunk.SetCell(0, generator.Result.GetTile(0), Orientation.Identity);

            ChunkEditing.TransformCells(chunk, new[] { 0, 1 }, Orientation.RotationY90, "Rotate Tile");

            Assert.That(chunk.Cells.GetOrientation(0), Is.EqualTo(Orientation.RotationY90));
            Assert.That(chunk.Cells.GetTile(1), Is.EqualTo(generated), "a rotated generated tile becomes one of the chunk's");
            Assert.That(chunk.Cells.GetOrientation(1), Is.EqualTo(Orientation.RotationY90 * generator.Result.GetOrientation(1)));
            Assert.That(chunk.Cells.IsEmpty(2), Is.True);
        }

        [Test]
        public void EditsCanBeUndone()
        {
            Chunk chunk = objects.Chunk(objects.TileSet(objects.Definition("a")), new int3(2, 1, 1));
            Undo.IncrementCurrentGroup();
            ChunkEditing.SetCell(chunk, 1, 0, Orientation.MirrorX);
            Undo.IncrementCurrentGroup();
            ChunkEditing.ClearCells(chunk, new[] { 1 });
            Undo.IncrementCurrentGroup();

            Undo.PerformUndo();
            Assert.That(chunk.Cells.GetOrientation(1), Is.EqualTo(Orientation.MirrorX));
            Undo.PerformUndo();
            Assert.That(chunk.Cells.IsEmpty(1), Is.True);
        }

        [Test]
        public void KeepCellsOnlyKeepsGeneratedTiles()
        {
            WfcGenerator generator = objects.Generator(TestRules.Checker(), new int3(3, 1, 1), 1, out _);
            generator.Generate(animate: false);
            Chunk chunk = generator.Chunk;
            chunk.SetCell(0, generator.Result.GetTile(0), Orientation.MirrorX);

            Assert.That(ChunkEditing.KeepCells(chunk, new[] { 0, 1 }), Is.EqualTo(1));
            Assert.That(chunk.Cells.GetOrientation(0), Is.EqualTo(Orientation.MirrorX), "the chunk's own tile stays");
            Assert.That(chunk.Cells.GetTile(1), Is.EqualTo(generator.Result.GetTile(1)));
            Assert.That(chunk.Cells.IsEmpty(2), Is.True, "cells that weren't asked for stay");
        }

        [Test]
        public void GrowingTheNegativeSideKeepsTilesInPlace()
        {
            Chunk chunk = objects.Chunk(objects.TileSet(objects.Definition("a")), new int3(2, 2, 2));
            chunk.transform.SetPositionAndRotation(new Vector3(5f, 0f, 1f), Quaternion.Euler(0f, 90f, 0f));
            chunk.SetCell(new int3(1, 0, 1), 0, Orientation.Identity);
            Vector3 before = chunk.CellToWorld(new int3(1, 0, 1), Orientation.Identity).GetColumn(3);

            ChunkEditing.ResizeSide(chunk, Direction.NegX, 2);

            Assert.That(chunk.Size, Is.EqualTo(new int3(4, 2, 2)));
            Assert.That(chunk.Cells.GetTile(chunk.Cells.ToIndex(new int3(3, 0, 1))), Is.EqualTo(0));
            Vector3 after = chunk.CellToWorld(new int3(3, 0, 1), Orientation.Identity).GetColumn(3);
            Assert.That(Vector3.Distance(before, after), Is.LessThan(1e-4f));

            ChunkEditing.ResizeSide(chunk, Direction.PosY, -5);
            Assert.That(chunk.Size.y, Is.EqualTo(1), "never smaller than one cell");
        }

        [Test]
        public void CandidatesComeFromTheSampleRules()
        {
            TileDefinition a = objects.Definition("a", withModel: false), b = objects.Definition("b", withModel: false);
            Chunk chunk = objects.Chunk(objects.TileSet(a, b), new int3(3, 1, 1));
            chunk.SetCell(0, 0, Orientation.Identity);
            chunk.SetCell(1, 1, Orientation.Identity);
            chunk.gameObject.AddComponent<SampleRuleSource>();
            LiveRuleAnalysis.MarkDirty();

            RulesetData rules = ChunkToolState.GetRules(chunk);
            var nextToA = ChunkToolState.GetCandidates(chunk, rules, 0, Direction.PosX, fitAll: false);
            var nextToB = ChunkToolState.GetCandidates(chunk, rules, 1, Direction.PosX, fitAll: false);

            Assert.That(nextToA.Select(c => c.Definition), Is.EqualTo(new[] { b }), "only b was ever next to a");
            Assert.That(nextToB.Select(c => c.Definition), Is.EqualTo(new[] { a }));
            Assert.That(ChunkToolState.GetCandidates(chunk, rules, 0, Direction.NegX, fitAll: false), Is.Empty, "the neighbouring cell is outside the chunk");
            Assert.That(ChunkToolState.GetCandidates(chunk, rules, 2, Direction.PosX, fitAll: false), Is.Empty, "an empty cell has no neighbours to offer");
        }

        [Test]
        public void FittingAllNeighboursNarrowsTheCandidates()
        {
            TileDefinition a = objects.Definition("a", withModel: false), b = objects.Definition("b", withModel: false), c = objects.Definition("c", withModel: false);
            Chunk chunk = objects.Chunk(objects.TileSet(a, b, c), new int3(7, 1, 1));
            // Learned: a-b, b-c, a-c. Between a and c, only b fits both.
            chunk.SetCell(0, 0, Orientation.Identity);
            chunk.SetCell(1, 1, Orientation.Identity);
            chunk.SetCell(2, 2, Orientation.Identity);
            chunk.SetCell(3, 0, Orientation.Identity);
            chunk.gameObject.AddComponent<SampleRuleSource>();
            RulesetData rules = RulesetBaker.Analyze(chunk.GetComponent<SampleRuleSource>(), detectSymmetry: false).Data;
            // A test chunk: a, empty, c.
            Chunk test = objects.Chunk(chunk.TileSet, new int3(3, 1, 1));
            test.SetCell(0, 0, Orientation.Identity);
            test.SetCell(2, 2, Orientation.Identity);

            var any = ChunkToolState.GetCandidates(test, rules, 0, Direction.PosX, fitAll: false);
            var fitting = ChunkToolState.GetCandidates(test, rules, 0, Direction.PosX, fitAll: true);

            Assert.That(any.Select(x => x.Definition), Is.EquivalentTo(new[] { b, c }));
            Assert.That(fitting.Select(x => x.Definition), Is.EqualTo(new[] { b }));
        }

        [Test]
        public void GeneratorChunksUseTheBakedRuleset()
        {
            TileDefinition a = objects.Definition("a", withModel: false);
            Chunk sample = objects.Chunk(objects.TileSet(a), new int3(2, 1, 1));
            sample.SetCell(0, 0, Orientation.Identity);
            sample.SetCell(1, 0, Orientation.Identity);
            var source = sample.gameObject.AddComponent<SampleRuleSource>();
            source.Ruleset = objects.Track(ScriptableObject.CreateInstance<Ruleset>());
            RulesetBaker.Bake(source);

            var generator = objects.Track(new GameObject("generator")).AddComponent<WfcGenerator>();
            generator.Ruleset = source.Ruleset;
            Assert.That(ChunkToolState.GetRules(generator.Chunk), Is.Null, "the generator's chunk has no tile set yet");
            generator.Chunk.TileSet = sample.TileSet;
            Assert.That(ChunkToolState.GetRules(generator.Chunk), Is.SameAs(source.Ruleset.Data));
        }
    }
}
