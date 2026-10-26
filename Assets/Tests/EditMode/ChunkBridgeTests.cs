using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using Wfc.Tests.Solver;

namespace Wfc.Tests
{
    public class ChunkBridgeTests
    {
        private static int TileIndex(RulesetData data, string name) => Array.IndexOf(data.tileNames, name);

        [Test]
        public void PaintedCellsAreKept()
        {
            RulesetData data = TestRules.Checker().Data();
            var chunk = new ChunkData(new int3(3, 2, 1));
            chunk.Set(0, TileIndex(data, "white"), Orientation.Identity);
            var solver = new WfcSolver(new CompiledRules(data), chunk.Size, 1);

            Assert.That(ChunkBridge.Prefill(solver, data, chunk), Is.True);
            Assert.That(solver.Run(), Is.EqualTo(SolverStatus.Done));
            SolutionAssert.IsValid(solver, data);
            Assert.That(solver.GetCollapsedVariant(0), Is.EqualTo(TestRules.Variant(data, "white")));
            Assert.That(solver.GetCollapsedVariant(1), Is.EqualTo(TestRules.Variant(data, "black")));
        }

        [Test]
        public void LookalikeOrientationsResolveToTheirVariant()
        {
            RulesetData data = TestRules.Checker().Data();
            var chunk = new ChunkData(new int3(2, 1, 1));
            // Black looks the same in every orientation, so any orientation means its single variant.
            chunk.Set(1, TileIndex(data, "black"), Orientation.MirrorX * Orientation.RotationY90);
            var solver = new WfcSolver(new CompiledRules(data), chunk.Size, 1);
            var unresolved = new List<int>();

            Assert.That(ChunkBridge.Prefill(solver, data, chunk, unresolved), Is.True);
            Assert.That(unresolved, Is.Empty);
            Assert.That(solver.GetCollapsedVariant(1), Is.EqualTo(TestRules.Variant(data, "black")));
            Assert.That(solver.GetCollapsedVariant(0), Is.EqualTo(TestRules.Variant(data, "white")), "decided by propagation");
        }

        [Test]
        public void UnknownTilesAndDisallowedOrientationsAreUnresolved()
        {
            RulesetData data = TestRules.Layers().Data();
            var chunk = new ChunkData(new int3(3, 1, 1));
            chunk.Set(0, 42, Orientation.Identity);
            chunk.Set(1, TileIndex(data, "bottom"), Orientation.RotationY90);
            chunk.Set(2, TileIndex(data, "bottom"), Orientation.Identity);
            var solver = new WfcSolver(new CompiledRules(data), chunk.Size, 1);
            var unresolved = new List<int>();

            Assert.That(ChunkBridge.Prefill(solver, data, chunk, unresolved), Is.True);
            Assert.That(unresolved, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(solver.GetCollapsedVariant(2), Is.EqualTo(TestRules.Variant(data, "bottom")));
        }

        [Test]
        public void ContradictingPaintIsReportedRightAway()
        {
            RulesetData data = TestRules.Checker().Data();
            var chunk = new ChunkData(new int3(2, 1, 1));
            chunk.Set(0, TileIndex(data, "black"), Orientation.Identity);
            chunk.Set(1, TileIndex(data, "black"), Orientation.Identity);
            var solver = new WfcSolver(new CompiledRules(data), chunk.Size, 1);

            Assert.That(ChunkBridge.Prefill(solver, data, chunk), Is.False);
            Assert.That(solver.Status, Is.EqualTo(SolverStatus.Contradiction));
        }

        [Test]
        public void ResultCanBePaintedBack()
        {
            RulesetData data = TestRules.HeavyLight().Data();
            var size = new int3(4, 3, 2);
            var first = new WfcSolver(new CompiledRules(data), size, 7);
            Assert.That(first.Run(), Is.EqualTo(SolverStatus.Done));
            var chunk = new ChunkData(size);
            ChunkBridge.ReadResult(first, data, chunk);
            Assert.That(chunk.CountNonEmpty(), Is.EqualTo(chunk.CellCount));

            var second = new WfcSolver(new CompiledRules(data), size, 99);
            Assert.That(ChunkBridge.Prefill(second, data, chunk), Is.True);
            for (int cell = 0; cell < chunk.CellCount; cell++)
            {
                Assert.That(second.GetCollapsedVariant(cell), Is.EqualTo(first.GetCollapsedVariant(cell)));
            }
        }

        [Test]
        public void ReadingSelectedCellsLeavesTheOthers()
        {
            RulesetData data = TestRules.HeavyLight().Data();
            var solver = new WfcSolver(new CompiledRules(data), new int3(5, 1, 1), 3);
            solver.Step();
            int collapsed = solver.ChangedCells[0];
            int undecided = (collapsed + 1) % 5;
            int untouched = (collapsed + 2) % 5;
            var chunk = new ChunkData(new int3(5, 1, 1));
            for (int cell = 0; cell < chunk.CellCount; cell++)
            {
                chunk.Set(cell, TileIndex(data, "light"), Orientation.RotationY90);
            }

            ChunkBridge.ReadResult(solver, data, chunk, new[] { collapsed, undecided });

            int variant = solver.GetCollapsedVariant(collapsed);
            Assert.That(chunk.GetTile(collapsed), Is.EqualTo(data.variantTile[variant]));
            Assert.That(chunk.GetOrientation(collapsed), Is.EqualTo(data.GetVariantOrientation(variant)));
            Assert.That(chunk.IsEmpty(undecided), Is.True, "undecided cells are emptied");
            Assert.That(chunk.GetOrientation(untouched), Is.EqualTo(Orientation.RotationY90), "cells that weren't asked for stay");
        }

        [Test]
        public void SizesMustMatch()
        {
            RulesetData data = TestRules.SingleTile().Data();
            var solver = new WfcSolver(new CompiledRules(data), new int3(2, 2, 2), 1);
            Assert.That(() => ChunkBridge.Prefill(solver, data, new ChunkData(new int3(2, 2, 1))), Throws.ArgumentException);
            Assert.That(() => ChunkBridge.ReadResult(solver, data, new ChunkData(new int3(1, 2, 2))), Throws.ArgumentException);
        }
    }
}
