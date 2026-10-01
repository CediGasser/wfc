using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;

namespace Wfc.Tests
{
    public class RuleDerivationTests
    {
        private static readonly int3 Origin = int3.zero;

        /// <summary>All orientations that map the given set of faces onto itself, e.g. the symmetry of a pipe with those open ends.</summary>
        private static OrientationSet Stabilizer(params Direction[] faces)
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

        private static readonly OrientationSet StraightPipeSymmetry = Stabilizer(Direction.PosX, Direction.NegX);
        private static readonly OrientationSet LPipeSymmetry = Stabilizer(Direction.PosX, Direction.PosY);

        private static TileSpec Tile(string name, OrientationSet allowed, OrientationSet symmetry, float baseWeight = 1f, bool isEmpty = false)
        {
            return new TileSpec { id = name, name = name, allowed = allowed, symmetry = symmetry, baseWeight = baseWeight, isEmpty = isEmpty };
        }

        private static void Place(DerivationInput input, int tile, int3 cell, Orientation orientation = default)
        {
            input.instances.Add(new SampleInstance { tile = tile, cell = cell, orientation = orientation });
        }

        private static int[] Neighbours(RulesetData data, int variant, Direction direction)
        {
            var (start, end) = data.GetNeighbourRange(variant, direction);
            return data.adjacencyVariant.Skip(start).Take(end - start).ToArray();
        }

        private static int[] VariantsOf(RulesetData data, int tile)
        {
            return Enumerable.Range(0, data.VariantCount).Where(v => data.variantTile[v] == tile).ToArray();
        }

        [Test]
        public void VariantCountsFollowSymmetry()
        {
            var input = new DerivationInput();
            input.tiles.Add(Tile("straight", OrientationSet.AllRotations, StraightPipeSymmetry));
            input.tiles.Add(Tile("corner", OrientationSet.AllRotations, LPipeSymmetry));
            input.tiles.Add(Tile("floor", OrientationSet.YawMirror, OrientationSet.All));
            input.tiles.Add(Tile("asymmetric", OrientationSet.All, OrientationSet.IdentityOnly));

            RulesetData data = RuleDerivation.Derive(input).data;

            Assert.That(VariantsOf(data, 0).Length, Is.EqualTo(3), "straight pipe: one variant per axis");
            Assert.That(VariantsOf(data, 1).Length, Is.EqualTo(12), "L-pipe: 24 rotations / 2 rotational symmetries");
            Assert.That(VariantsOf(data, 2).Length, Is.EqualTo(1), "a symmetric floor has a single variant");
            Assert.That(VariantsOf(data, 3).Length, Is.EqualTo(48));
        }

        [Test]
        public void VariantWeightsSpreadTheBaseWeight()
        {
            var input = new DerivationInput();
            input.tiles.Add(Tile("corner", OrientationSet.AllRotations, LPipeSymmetry, baseWeight: 3f));
            input.tiles.Add(Tile("floor", OrientationSet.YawMirror, OrientationSet.All, baseWeight: 2f));

            RulesetData data = RuleDerivation.Derive(input).data;

            foreach (int v in VariantsOf(data, 0))
            {
                Assert.That(data.variantWeight[v], Is.EqualTo(3f / 12f).Within(1e-6f));
            }
            Assert.That(data.variantWeight[VariantsOf(data, 1)[0]], Is.EqualTo(2f));
        }

        [Test]
        public void HorizontalPipePairAlsoYieldsVerticalChains()
        {
            var input = new DerivationInput();
            input.tiles.Add(Tile("straight", OrientationSet.AllRotations, StraightPipeSymmetry));
            Place(input, 0, Origin);
            Place(input, 0, new int3(1, 0, 0));

            RulesetData data = RuleDerivation.Derive(input).data;

            Assert.That(data.orbits.Length, Is.EqualTo(1));
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
            var input = new DerivationInput();
            input.tiles.Add(Tile("floor", OrientationSet.YawMirror, OrientationSet.All));
            input.tiles.Add(Tile("scaffolding", OrientationSet.YawMirror, OrientationSet.IdentityOnly));
            Place(input, 0, Origin);
            Place(input, 1, new int3(0, 1, 0));

            RulesetData data = RuleDerivation.Derive(input).data;

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
            var input = new DerivationInput();
            input.tiles.Add(Tile("sign", OrientationSet.IdentityOnly, OrientationSet.IdentityOnly));
            input.tiles.Add(Tile("block", OrientationSet.AllRotations, OrientationSet.IdentityOnly));
            Place(input, 0, Origin);
            Place(input, 1, new int3(1, 0, 0));

            RulesetData data = RuleDerivation.Derive(input).data;

            int sign = VariantsOf(data, 0).Single();
            Assert.That(data.orbits.Single().memberCount, Is.EqualTo(1));
            Assert.That(Neighbours(data, sign, Direction.PosX).Length, Is.EqualTo(1));
            int block = data.FindVariant(1, Orientation.Identity);
            Assert.That(Neighbours(data, block, Direction.NegX), Is.EqualTo(new[] { sign }));
            Assert.That(VariantsOf(data, 1).Count(v => Neighbours(data, v, Direction.NegX).Length > 0), Is.EqualTo(1));
        }

        [Test]
        public void MirroredExpansionsNeedBothTilesToAllowMirroring()
        {
            DerivationInput Build(OrientationSet allowedB)
            {
                var input = new DerivationInput();
                input.tiles.Add(Tile("a", OrientationSet.All, OrientationSet.IdentityOnly));
                input.tiles.Add(Tile("b", allowedB, OrientationSet.IdentityOnly));
                Place(input, 0, Origin);
                Place(input, 1, new int3(0, 0, 1));
                return input;
            }

            Assert.That(RuleDerivation.Derive(Build(OrientationSet.AllRotations)).data.orbits.Single().memberCount, Is.EqualTo(24));
            Assert.That(RuleDerivation.Derive(Build(OrientationSet.All)).data.orbits.Single().memberCount, Is.EqualTo(48));
        }

        [Test]
        public void OrbitKeyDoesNotDependOnHowThePairIsPlaced()
        {
            OrbitKey Derive(Orientation worldRotation, int3 offset)
            {
                var input = new DerivationInput();
                input.tiles.Add(Tile("a", OrientationSet.YawMirror, OrientationSet.IdentityOnly));
                input.tiles.Add(Tile("b", OrientationSet.YawMirror, OrientationSet.IdentityOnly));
                Place(input, 0, offset, worldRotation);
                Place(input, 1, offset + worldRotation.Apply(new int3(1, 0, 0)), worldRotation * Orientation.RotationY90);
                return RuleDerivation.Derive(input).data.orbits.Single().key;
            }

            OrbitKey reference = Derive(Orientation.Identity, Origin);
            Assert.That(Derive(Orientation.RotationY90, new int3(5, 2, -3)), Is.EqualTo(reference));
            Assert.That(Derive(Orientation.MirrorX * Orientation.RotationY90, new int3(-7, 0, 1)), Is.EqualTo(reference));
            Assert.That(reference.Reversed.Undirected, Is.EqualTo(reference.Undirected));
        }

        [Test]
        public void AdjacencyIsSymmetric()
        {
            var input = new DerivationInput();
            input.tiles.Add(Tile("floor", OrientationSet.YawMirror, OrientationSet.All));
            input.tiles.Add(Tile("straight", OrientationSet.AllRotations, StraightPipeSymmetry));
            input.tiles.Add(Tile("corner", OrientationSet.AllRotations, LPipeSymmetry));
            input.tiles.Add(Tile("air", OrientationSet.All, OrientationSet.All, isEmpty: true));
            Place(input, 0, Origin);
            Place(input, 0, new int3(1, 0, 0));
            Place(input, 1, new int3(0, 1, 0), Orientation.RotationY90);
            Place(input, 2, new int3(0, 1, 1), Orientation.RotationX90);
            Place(input, 3, new int3(1, 1, 0));
            Place(input, 3, new int3(1, 2, 0));

            RulesetData data = RuleDerivation.Derive(input).data;

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
                        Assert.That(data.adjacencyWeight[back], Is.EqualTo(data.adjacencyWeight[i]));
                    }
                }
            }
        }

        [Test]
        public void FrequencyCountsObservationsNotSymmetricDuplicates()
        {
            var input = new DerivationInput();
            input.tiles.Add(Tile("floor", OrientationSet.YawMirror, OrientationSet.All));
            input.tiles.Add(Tile("scaffolding", OrientationSet.YawMirror, OrientationSet.IdentityOnly));
            // Two separate stacks, one rotated: the same connection observed twice.
            Place(input, 0, Origin);
            Place(input, 1, new int3(0, 1, 0));
            Place(input, 0, new int3(3, 0, 0));
            Place(input, 1, new int3(3, 1, 0), Orientation.RotationY90);
            // A floor pair: symmetric tiles, one observation.
            Place(input, 0, new int3(6, 0, 0));
            Place(input, 0, new int3(7, 0, 0));

            input.weightSource = WeightSource.Frequency;
            Orbit[] orbits = RuleDerivation.Derive(input).data.orbits;
            Assert.That(orbits.Length, Is.EqualTo(2));
            Assert.That(orbits.Single(o => o.tileA != o.tileB).observations, Is.EqualTo(2));
            Assert.That(orbits.Single(o => o.tileA != o.tileB).weight, Is.EqualTo(2f));
            Assert.That(orbits.Single(o => o.tileA == o.tileB).weight, Is.EqualTo(1f));

            input.weightSource = WeightSource.Presence;
            Assert.That(RuleDerivation.Derive(input).data.orbits.All(o => o.weight == 1f), Is.True);
        }

        [Test]
        public void OverridesChangeOrRemoveConnections()
        {
            var input = new DerivationInput();
            input.tiles.Add(Tile("floor", OrientationSet.YawMirror, OrientationSet.All));
            input.tiles.Add(Tile("scaffolding", OrientationSet.YawMirror, OrientationSet.IdentityOnly));
            Place(input, 0, Origin);
            Place(input, 1, new int3(0, 1, 0));
            OrbitKey key = RuleDerivation.Derive(input).data.orbits.Single().key;

            input.overrides.Add(new WeightOverride(key.Reversed, 5f));
            RulesetData weighted = RuleDerivation.Derive(input).data;
            Assert.That(weighted.orbits.Single().weight, Is.EqualTo(5f), "overrides match regardless of key direction");
            Assert.That(weighted.orbits.Single().overridden, Is.True);
            Assert.That(weighted.adjacencyWeight.All(w => w == 5f), Is.True);

            input.overrides[0] = new WeightOverride(key, 0f);
            RulesetData removed = RuleDerivation.Derive(input).data;
            Assert.That(removed.adjacencyVariant, Is.Empty);
            Assert.That(removed.diagnostics.Any(d => d.code == DiagnosticCode.TileNeverConnected), Is.True);

            input.overrides[0] = new WeightOverride(new OrbitKey("nope", 0, Direction.PosX, "nope", 0), 2f);
            Assert.That(RuleDerivation.Derive(input).data.diagnostics.Any(d => d.code == DiagnosticCode.OrphanedOverride), Is.True);
        }

        [Test]
        public void DiagnosticsReportProblems()
        {
            var input = new DerivationInput();
            input.tiles.Add(Tile("floor", OrientationSet.IdentityOnly, OrientationSet.All));
            input.tiles.Add(Tile("sign", OrientationSet.IdentityOnly, OrientationSet.IdentityOnly));
            input.tiles.Add(Tile("air", OrientationSet.All, OrientationSet.All, isEmpty: true));
            Place(input, 0, Origin, Orientation.RotationY90);                 // looks identical to the allowed orientation
            Place(input, 1, new int3(1, 0, 0), Orientation.RotationY90);      // not allowed, not symmetric
            Place(input, 2, new int3(0, 1, 0));
            Place(input, 2, new int3(0, 1, 0));                               // overlap

            DerivationResult result = RuleDerivation.Derive(input);
            DiagnosticCode[] codes = result.data.diagnostics.Select(d => d.code).ToArray();

            Assert.That(codes, Does.Contain(DiagnosticCode.OrientationMapped));
            Assert.That(codes, Does.Contain(DiagnosticCode.InvalidOrientation));
            Assert.That(codes, Does.Contain(DiagnosticCode.OverlappingInstances));
            Assert.That(codes, Does.Contain(DiagnosticCode.MissingSelfConnection));
            Assert.That(codes, Does.Contain(DiagnosticCode.DeadEnd));
            Assert.That(result.instanceVariant[1], Is.EqualTo(-1));
            Assert.That(result.instanceVariant[3], Is.EqualTo(-1));
            Assert.That(result.pairs.Count, Is.EqualTo(1), "only floor and air touch");
        }

        [Test]
        public void InputHashChangesWithTheInput()
        {
            var input = new DerivationInput();
            input.tiles.Add(Tile("floor", OrientationSet.YawMirror, OrientationSet.All));
            Place(input, 0, Origin);
            string before = input.ComputeHash();
            Assert.That(input.ComputeHash(), Is.EqualTo(before));
            Place(input, 0, new int3(1, 0, 0));
            Assert.That(input.ComputeHash(), Is.Not.EqualTo(before));
        }
    }
}
