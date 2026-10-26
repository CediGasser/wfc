using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;

namespace Wfc.Tests
{
    public class SampleRulesTests
    {
        private static readonly int3 Origin = int3.zero;

        /// <summary>All orientations that map the given set of faces onto itself, e.g. the symmetry of a pipe with those open ends.</summary>
        internal static OrientationSet Stabilizer(params Direction[] faces)
        {
            var set = OrientationSet.Empty;
            foreach (Orientation o in OrientationSet.All)
            {
                if (faces.All(face => faces.Contains(o.Apply(face))))
                {
                    set = set.With(o);
                }
            }
            return set;
        }

        internal static readonly OrientationSet StraightPipeSymmetry = Stabilizer(Direction.PosX, Direction.NegX);
        private static readonly OrientationSet LPipeSymmetry = Stabilizer(Direction.PosX, Direction.PosY);

        internal static TileSpec Tile(string name, OrientationSet allowed, OrientationSet symmetry, float baseWeight = 1f, bool isInvisible = false)
        {
            return new TileSpec { id = name, name = name, allowed = allowed, symmetry = symmetry, baseWeight = baseWeight, isInvisible = isInvisible };
        }

        /// <summary>Sample rules for tiles placed at arbitrary cells, separate samples kept apart by empty cells.</summary>
        private sealed class Input
        {
            public readonly List<TileSpec> tiles = new List<TileSpec>();
            public readonly SampleBuilder placements = new SampleBuilder();
            public readonly SampleRules rules = new SampleRules();

            public Input Place(int tile, int3 cell, Orientation orientation = default)
            {
                placements.Place(tile, cell, orientation);
                return this;
            }

            public DerivedRules Derive()
            {
                rules.sample = placements.Build();
                return rules.Derive(tiles);
            }

            public int CellOf(int3 cell) => placements.IndexOf(rules.sample, cell);
        }

        internal static int[] Neighbours(RulesetData data, int variant, Direction direction)
        {
            var (start, end) = data.GetNeighbourRange(variant, direction);
            return data.adjacencyVariant.Skip(start).Take(end - start).ToArray();
        }

        internal static int[] VariantsOf(RulesetData data, int tile) => data.GetVariants(tile).ToArray();

        [Test]
        public void VariantCountsFollowSymmetry()
        {
            var input = new Input();
            input.tiles.Add(Tile("straight", OrientationSet.AllRotations, StraightPipeSymmetry));
            input.tiles.Add(Tile("corner", OrientationSet.AllRotations, LPipeSymmetry));
            input.tiles.Add(Tile("floor", OrientationSet.YawMirror, OrientationSet.All));
            input.tiles.Add(Tile("asymmetric", OrientationSet.All, OrientationSet.IdentityOnly));
            for (int t = 0; t < 4; t++)
            {
                input.Place(t, new int3(2 * t, 0, 0));
            }

            RulesetData data = input.Derive().data;

            Assert.That(VariantsOf(data, 0).Length, Is.EqualTo(3), "straight pipe: one variant per axis");
            Assert.That(VariantsOf(data, 1).Length, Is.EqualTo(12), "L-pipe: 24 rotations / 2 rotational symmetries");
            Assert.That(VariantsOf(data, 2).Length, Is.EqualTo(1), "a symmetric floor has a single variant");
            Assert.That(VariantsOf(data, 3).Length, Is.EqualTo(48));
        }

        [Test]
        public void VariantWeightsSpreadTheBaseWeight()
        {
            var input = new Input();
            input.tiles.Add(Tile("corner", OrientationSet.AllRotations, LPipeSymmetry, baseWeight: 3f));
            input.tiles.Add(Tile("floor", OrientationSet.YawMirror, OrientationSet.All, baseWeight: 2f));
            input.Place(0, Origin).Place(1, new int3(2, 0, 0));

            RulesetData data = input.Derive().data;

            foreach (int v in VariantsOf(data, 0))
            {
                Assert.That(data.variantWeight[v], Is.EqualTo(3f / 12f).Within(1e-6f));
            }
            Assert.That(data.variantWeight[VariantsOf(data, 1)[0]], Is.EqualTo(2f));
        }

        [Test]
        public void TilesNotInTheSampleGetNoVariants()
        {
            var input = new Input();
            input.tiles.Add(Tile("a", OrientationSet.Yaw, OrientationSet.All));
            input.tiles.Add(Tile("unused", OrientationSet.Yaw, OrientationSet.All));
            input.tiles.Add(new TileSpec { name = "empty slot" });
            input.Place(0, Origin).Place(0, new int3(1, 0, 0));

            RulesetData data = input.Derive().data;

            Assert.That(VariantsOf(data, 1), Is.Empty);
            Assert.That(VariantsOf(data, 2), Is.Empty);
            Assert.That(data.TileCount, Is.EqualTo(3), "tile indices stay the same");
            Assert.That(data.diagnostics.Where(d => d.tile == 1 || d.tile == 2), Is.Empty);
        }

        [Test]
        public void HorizontalPipePairAlsoYieldsVerticalChains()
        {
            var input = new Input();
            input.tiles.Add(Tile("straight", OrientationSet.AllRotations, StraightPipeSymmetry));
            input.Place(0, Origin).Place(0, new int3(1, 0, 0));

            RulesetData data = input.Derive().data;

            Assert.That(data.connections.Length, Is.EqualTo(1));
            foreach (int v in VariantsOf(data, 0))
            {
                Direction axis = data.GetVariantOrientation(v).Apply(Direction.PosX);
                for (int d = 0; d < Directions.Count; d++)
                {
                    var direction = (Direction)d;
                    bool alongPipe = direction == axis || direction == axis.Opposite();
                    Assert.That(Neighbours(data, v, direction), alongPipe ? Is.EqualTo(new[] { v }) : Is.Empty,
                        $"variant along {axis.ToShortString()}, direction {direction.ToShortString()}");
                }
            }
        }

        [Test]
        public void FloorAndScaffoldingOnlyExpandAroundTheVerticalAxis()
        {
            var input = new Input();
            input.tiles.Add(Tile("floor", OrientationSet.YawMirror, OrientationSet.All));
            input.tiles.Add(Tile("scaffolding", OrientationSet.YawMirror, OrientationSet.IdentityOnly));
            input.Place(0, Origin).Place(1, new int3(0, 1, 0));

            RulesetData data = input.Derive().data;

            int floor = VariantsOf(data, 0).Single();
            int[] scaffolding = VariantsOf(data, 1);
            Assert.That(scaffolding.Length, Is.EqualTo(8));
            Assert.That(Neighbours(data, floor, Direction.PosY), Is.EquivalentTo(scaffolding));
            Assert.That(Neighbours(data, floor, Direction.NegY), Is.Empty, "nothing was learned below the floor");
            foreach (int v in scaffolding)
            {
                Assert.That(Neighbours(data, v, Direction.NegY), Is.EqualTo(new[] { floor }));
                Assert.That(Neighbours(data, v, Direction.PosY), Is.Empty, "the floor must never end up above scaffolding");
            }
        }

        [Test]
        public void FixedTileIsNotExpanded()
        {
            var input = new Input();
            input.tiles.Add(Tile("sign", OrientationSet.IdentityOnly, OrientationSet.IdentityOnly));
            input.tiles.Add(Tile("block", OrientationSet.AllRotations, OrientationSet.IdentityOnly));
            input.Place(0, Origin).Place(1, new int3(1, 0, 0));

            RulesetData data = input.Derive().data;

            int sign = VariantsOf(data, 0).Single();
            Assert.That(data.connections.Single().memberCount, Is.EqualTo(1));
            Assert.That(Neighbours(data, sign, Direction.PosX).Length, Is.EqualTo(1));
            int block = data.FindVariant(1, Orientation.Identity);
            Assert.That(Neighbours(data, block, Direction.NegX), Is.EqualTo(new[] { sign }));
            Assert.That(VariantsOf(data, 1).Count(v => Neighbours(data, v, Direction.NegX).Length > 0), Is.EqualTo(1));
        }

        [Test]
        public void MirroredExpansionsNeedBothTilesToAllowMirroring()
        {
            Connection Derive(OrientationSet allowedB)
            {
                var input = new Input();
                input.tiles.Add(Tile("a", OrientationSet.All, OrientationSet.IdentityOnly));
                input.tiles.Add(Tile("b", allowedB, OrientationSet.IdentityOnly));
                input.Place(0, Origin).Place(1, new int3(0, 0, 1));
                return input.Derive().data.connections.Single();
            }

            Assert.That(Derive(OrientationSet.AllRotations).memberCount, Is.EqualTo(24));
            Assert.That(Derive(OrientationSet.All).memberCount, Is.EqualTo(48));
        }

        [Test]
        public void ConnectionKeyDoesNotDependOnHowThePairIsPlaced()
        {
            ConnectionKey Derive(Orientation worldRotation, int3 offset)
            {
                var input = new Input();
                input.tiles.Add(Tile("a", OrientationSet.YawMirror, OrientationSet.IdentityOnly));
                input.tiles.Add(Tile("b", OrientationSet.YawMirror, OrientationSet.IdentityOnly));
                input.Place(0, offset, worldRotation);
                input.Place(1, offset + worldRotation.Apply(new int3(1, 0, 0)), worldRotation * Orientation.RotationY90);
                return input.Derive().data.connections.Single().key;
            }

            ConnectionKey reference = Derive(Orientation.Identity, Origin);
            Assert.That(Derive(Orientation.RotationY90, new int3(5, 2, -3)), Is.EqualTo(reference));
            Assert.That(Derive(Orientation.MirrorX * Orientation.RotationY90, new int3(-7, 0, 1)), Is.EqualTo(reference));
            Assert.That(reference.Reversed.Undirected, Is.EqualTo(reference.Undirected));
        }

        [Test]
        public void AdjacencyIsSymmetric()
        {
            var input = new Input();
            input.tiles.Add(Tile("floor", OrientationSet.YawMirror, OrientationSet.All));
            input.tiles.Add(Tile("straight", OrientationSet.AllRotations, StraightPipeSymmetry));
            input.tiles.Add(Tile("corner", OrientationSet.AllRotations, LPipeSymmetry));
            input.tiles.Add(Tile("air", OrientationSet.All, OrientationSet.All, isInvisible: true));
            input.Place(0, Origin).Place(0, new int3(1, 0, 0));
            input.Place(1, new int3(0, 1, 0), Orientation.RotationY90);
            input.Place(2, new int3(0, 1, 1), Orientation.RotationX90);
            input.Place(3, new int3(1, 1, 0)).Place(3, new int3(1, 2, 0));

            AssertSymmetric(input.Derive().data);
        }

        internal static void AssertSymmetric(RulesetData data)
        {
            for (int v = 0; v < data.VariantCount; v++)
            {
                for (int d = 0; d < Directions.Count; d++)
                {
                    var (start, end) = data.GetNeighbourRange(v, (Direction)d);
                    for (int i = start; i < end; i++)
                    {
                        int n = data.adjacencyVariant[i];
                        var (backStart, backEnd) = data.GetNeighbourRange(n, ((Direction)d).Opposite());
                        int back = System.Array.IndexOf(data.adjacencyVariant, v, backStart, backEnd - backStart);
                        Assert.That(back, Is.GreaterThanOrEqualTo(0), $"{v} lists {n} in {(Direction)d}, but not the other way round");
                        Assert.That(data.GetEntryWeight(back), Is.EqualTo(data.GetEntryWeight(i)));
                    }
                }
            }
        }

        [Test]
        public void FrequencyCountsObservationsNotSymmetricDuplicates()
        {
            var input = new Input();
            input.tiles.Add(Tile("floor", OrientationSet.YawMirror, OrientationSet.All));
            input.tiles.Add(Tile("scaffolding", OrientationSet.YawMirror, OrientationSet.IdentityOnly));
            // Two separate stacks, one rotated: the same connection observed twice.
            input.Place(0, Origin).Place(1, new int3(0, 1, 0));
            input.Place(0, new int3(3, 0, 0)).Place(1, new int3(3, 1, 0), Orientation.RotationY90);
            // A floor pair: symmetric tiles, one observation.
            input.Place(0, new int3(6, 0, 0)).Place(0, new int3(7, 0, 0));

            input.rules.weighting = SampleWeighting.Frequency;
            Connection[] connections = input.Derive().data.connections;
            Assert.That(connections.Length, Is.EqualTo(2));
            Assert.That(connections.Single(o => o.tileA != o.tileB).observations, Is.EqualTo(2));
            Assert.That(connections.Single(o => o.tileA != o.tileB).weight, Is.EqualTo(2f));
            Assert.That(connections.Single(o => o.tileA == o.tileB).weight, Is.EqualTo(1f));

            input.rules.weighting = SampleWeighting.Presence;
            Assert.That(input.Derive().data.connections.All(o => o.weight == 1f), Is.True);
        }

        [Test]
        public void ConnectionsAreSortedByKey()
        {
            var input = new Input();
            input.tiles.Add(Tile("floor", OrientationSet.YawMirror, OrientationSet.All));
            input.tiles.Add(Tile("scaffolding", OrientationSet.YawMirror, OrientationSet.IdentityOnly));
            input.tiles.Add(Tile("straight", OrientationSet.AllRotations, StraightPipeSymmetry));
            input.Place(2, Origin).Place(2, new int3(1, 0, 0)).Place(0, new int3(0, -1, 0)).Place(1, new int3(1, 1, 0)).Place(0, new int3(1, -1, 0));

            DerivedRules result = input.Derive();
            Connection[] connections = result.data.connections;
            Assert.That(connections.Length, Is.GreaterThan(2));
            Assert.That(connections.Select(c => c.key), Is.EqualTo(connections.Select(c => c.key).OrderBy(k => k)));
            foreach (CellPair pair in result.pairs)
            {
                Assert.That(result.cellVariant[pair.cellA], Is.GreaterThanOrEqualTo(0));
                Assert.That(result.data.adjacencyConnection.Contains(pair.connection), Is.True, "pairs point at the sorted connections");
            }
        }

        [Test]
        public void OverridesChangeOrRemoveConnections()
        {
            var input = new Input();
            input.tiles.Add(Tile("floor", OrientationSet.YawMirror, OrientationSet.All));
            input.tiles.Add(Tile("scaffolding", OrientationSet.YawMirror, OrientationSet.IdentityOnly));
            input.Place(0, Origin).Place(1, new int3(0, 1, 0));
            ConnectionKey key = input.Derive().data.connections.Single().key;

            input.rules.overrides.Add(new WeightOverride(key.Reversed, 5f));
            RulesetData weighted = input.Derive().data;
            Assert.That(weighted.connections.Single().weight, Is.EqualTo(5f), "overrides match regardless of key direction");
            Assert.That(weighted.connections.Single().overridden, Is.True);
            Assert.That(Enumerable.Range(0, weighted.adjacencyVariant.Length).All(i => weighted.GetEntryWeight(i) == 5f), Is.True);

            input.rules.overrides[0] = new WeightOverride(key, 0f);
            RulesetData removed = input.Derive().data;
            Assert.That(removed.adjacencyVariant, Is.Empty);
            Assert.That(removed.diagnostics.Any(d => d.code == DiagnosticCode.TileNeverConnected), Is.True);

            input.rules.overrides[0] = new WeightOverride(new ConnectionKey("nope", 0, Direction.PosX, "nope", 0), 2f);
            Assert.That(input.Derive().data.diagnostics.Any(d => d.code == DiagnosticCode.OrphanedOverride), Is.True);
        }

        [Test]
        public void EmptyCellsCountAsTheEmptyCellTile()
        {
            var input = new Input();
            input.tiles.Add(Tile("block", OrientationSet.All, OrientationSet.All));
            input.tiles.Add(Tile("air", OrientationSet.All, OrientationSet.All, isInvisible: true));
            // block, empty, block: with air as the empty-cell tile, the middle cell shows block next to air twice.
            input.Place(0, Origin).Place(0, new int3(2, 0, 0));

            input.rules.emptyCellTile = ChunkData.Empty;
            DerivedRules unknown = input.Derive();
            Assert.That(unknown.data.connections, Is.Empty, "empty cells carry no information");
            Assert.That(VariantsOf(unknown.data, 1), Is.Empty);

            input.rules.emptyCellTile = 1;
            DerivedRules filled = input.Derive();
            Connection connection = filled.data.connections.Single();
            Assert.That(new[] { connection.tileA, connection.tileB }, Is.EquivalentTo(new[] { 0, 1 }));
            Assert.That(connection.observations, Is.EqualTo(2));
            Assert.That(filled.cellVariant[input.CellOf(new int3(1, 0, 0))], Is.EqualTo(VariantsOf(filled.data, 1).Single()));
        }

        [Test]
        public void CellsOutsideTheChunkCarryNoInformation()
        {
            var input = new Input();
            input.tiles.Add(Tile("block", OrientationSet.All, OrientationSet.All));
            input.tiles.Add(Tile("air", OrientationSet.All, OrientationSet.All, isInvisible: true));
            input.Place(0, Origin);
            input.rules.emptyCellTile = 1;

            RulesetData data = input.Derive().data;
            Assert.That(data.connections, Is.Empty, "a single cell has no neighbours, whatever the empty-cell tile");
        }

        [Test]
        public void MissingTilesAreReported()
        {
            var input = new Input();
            input.tiles.Add(Tile("block", OrientationSet.All, OrientationSet.All));
            input.tiles.Add(new TileSpec { name = "removed" });
            input.Place(0, Origin).Place(1, new int3(1, 0, 0)).Place(7, new int3(2, 0, 0));
            input.rules.emptyCellTile = ChunkData.Empty;

            DerivedRules result = input.Derive();
            RuleDiagnostic[] missing = result.data.diagnostics.Where(d => d.code == DiagnosticCode.MissingTile).ToArray();
            Assert.That(missing.SelectMany(d => d.cells), Is.EquivalentTo(new[] { input.CellOf(new int3(1, 0, 0)), input.CellOf(new int3(2, 0, 0)) }));
            Assert.That(result.cellVariant[input.CellOf(new int3(1, 0, 0))], Is.EqualTo(-1));
            Assert.That(result.data.connections, Is.Empty);
        }

        [Test]
        public void DiagnosticsReportProblems()
        {
            var notAGroup = new OrientationSet(1UL | (1UL << Orientation.RotationY90.Index));
            var input = new Input();
            input.tiles.Add(Tile("floor", OrientationSet.IdentityOnly, OrientationSet.All));
            input.tiles.Add(Tile("sign", OrientationSet.IdentityOnly, OrientationSet.IdentityOnly));
            input.tiles.Add(Tile("air", OrientationSet.All, OrientationSet.All, isInvisible: true));
            input.tiles.Add(Tile("broken", notAGroup, OrientationSet.IdentityOnly));
            input.tiles.Add(Tile("lonely", OrientationSet.IdentityOnly, OrientationSet.IdentityOnly));
            input.Place(0, Origin);
            input.Place(1, new int3(1, 0, 0), Orientation.RotationY90);      // not allowed
            input.Place(2, new int3(0, 1, 0));
            input.Place(3, new int3(0, -1, 0));                              // tile with an invalid allowed set
            input.Place(0, new int3(5, 0, 0), Orientation.RotationY90);      // looks identical, but still not allowed
            input.Place(4, new int3(9, 0, 0));                               // touches nothing

            DerivedRules result = input.Derive();
            DiagnosticCode[] codes = result.data.diagnostics.Select(d => d.code).ToArray();

            Assert.That(codes, Does.Contain(DiagnosticCode.DisallowedOrientation));
            Assert.That(codes, Does.Contain(DiagnosticCode.MissingSelfConnection));
            Assert.That(codes, Does.Contain(DiagnosticCode.DeadEnd));
            Assert.That(codes, Does.Contain(DiagnosticCode.TileNeverConnected));
            RuleDiagnostic notAGroupError = result.data.diagnostics.Single(d => d.code == DiagnosticCode.AllowedSetNotAGroup);
            Assert.That(notAGroupError.severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(notAGroupError.cells, Is.EqualTo(new[] { input.CellOf(new int3(0, -1, 0)) }));
            Assert.That(VariantsOf(result.data, 3), Is.Empty, "a tile with an invalid allowed set gets no variants");
            foreach (int3 skipped in new[] { new int3(1, 0, 0), new int3(0, -1, 0), new int3(5, 0, 0) })
            {
                Assert.That(result.cellVariant[input.CellOf(skipped)], Is.EqualTo(-1), $"cell {skipped}");
            }
            Assert.That(result.pairs.Count, Is.EqualTo(1), "only floor and air touch");
            RuleDiagnostic deadEnd = result.data.diagnostics.First(d => d.code == DiagnosticCode.DeadEnd);
            Assert.That(deadEnd.faces, Is.Not.Zero);
        }

        [Test]
        public void InputHashChangesWithTheInput()
        {
            var input = new Input();
            input.tiles.Add(Tile("floor", OrientationSet.YawMirror, OrientationSet.All));
            input.Place(0, Origin);
            string before = input.Derive().inputHash;
            Assert.That(input.Derive().inputHash, Is.EqualTo(before));
            input.Place(0, new int3(1, 0, 0));
            Assert.That(input.Derive().inputHash, Is.Not.EqualTo(before));
            string placed = input.Derive().inputHash;
            input.rules.emptyCellTile = 0;
            Assert.That(input.Derive().inputHash, Is.Not.EqualTo(placed));
        }
    }
}
