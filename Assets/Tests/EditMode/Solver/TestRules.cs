using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;

namespace Wfc.Tests.Solver
{
    /// <summary>
    /// Builds small rulesets through <see cref="RuleDerivation"/>, the same way the editor does from samples, so solver
    /// tests run on realistic data. Tiles with <see cref="OrientationSet.All"/> allowed and full symmetry have exactly
    /// one variant, and one sample pair along X teaches the connection along every axis.
    /// </summary>
    internal sealed class TestRules
    {
        private readonly DerivationInput input = new DerivationInput();
        private readonly List<(string a, string b, float weight)> overrides = new List<(string, string, float)>();
        private int nextSampleZ;

        public int Tile(string name, OrientationSet allowed, OrientationSet symmetry, float baseWeight = 1f)
        {
            input.tiles.Add(new TileSpec { id = name, name = name, allowed = allowed, symmetry = symmetry, baseWeight = baseWeight });
            return input.tiles.Count - 1;
        }

        /// <summary>A tile with a single variant that may appear in any orientation.</summary>
        public int SymmetricTile(string name, float baseWeight = 1f) => Tile(name, OrientationSet.All, OrientationSet.All, baseWeight);

        public TestRules Place(int tile, int x, int y, int z, Orientation orientation = default)
        {
            input.instances.Add(new SampleInstance { tile = tile, cell = new int3(x, y, z), orientation = orientation });
            return this;
        }

        /// <summary>Places a separate sample pair: <paramref name="a"/> with <paramref name="b"/> to its +X side.</summary>
        public TestRules Pair(int a, int b)
        {
            Place(a, 0, 0, nextSampleZ);
            Place(b, 1, 0, nextSampleZ);
            nextSampleZ += 2;
            return this;
        }

        /// <summary>Overrides the weight of the (only) learned connection between two tiles.</summary>
        public TestRules Weight(string a, string b, float weight)
        {
            overrides.Add((a, b, weight));
            return this;
        }

        public RulesetData Data()
        {
            input.overrides.Clear();
            RulesetData derived = RuleDerivation.Derive(input).data;
            foreach (var (a, b, weight) in overrides)
            {
                Orbit orbit = derived.orbits.Single(o =>
                    (derived.tileNames[o.tileA] == a && derived.tileNames[o.tileB] == b) ||
                    (derived.tileNames[o.tileA] == b && derived.tileNames[o.tileB] == a));
                input.overrides.Add(new WeightOverride(orbit.key, weight));
            }
            return RuleDerivation.Derive(input).data;
        }

        public CompiledRules Compile() => new CompiledRules(Data());

        // Shared rulesets ------------------------------------------------------------------------------------------

        /// <summary>One tile that may sit next to itself everywhere.</summary>
        public static TestRules SingleTile()
        {
            var rules = new TestRules();
            int block = rules.SymmetricTile("block");
            return rules.Pair(block, block);
        }

        /// <summary>Black and white, only ever next to the other colour: a 3D checkerboard.</summary>
        public static TestRules Checker()
        {
            var rules = new TestRules();
            int black = rules.SymmetricTile("black");
            int white = rules.SymmetricTile("white");
            return rules.Pair(black, white);
        }

        /// <summary>
        /// Two fixed tiles: "top" only above "bottom", each next to itself horizontally, nothing above top or below
        /// bottom. A column of exactly two cells is fully determined; three cells are impossible.
        /// </summary>
        public static TestRules Layers()
        {
            var rules = new TestRules();
            int bottom = rules.Tile("bottom", OrientationSet.IdentityOnly, OrientationSet.IdentityOnly);
            int top = rules.Tile("top", OrientationSet.IdentityOnly, OrientationSet.IdentityOnly);
            rules.Place(bottom, 0, 0, 0).Place(bottom, 1, 0, 0).Place(bottom, 0, 0, 1);
            rules.Place(top, 0, 1, 0).Place(top, 1, 1, 0).Place(top, 0, 1, 1);
            return rules;
        }

        /// <summary>A fixed tile that only connects to itself along X, so any grid taller or deeper than 1 is impossible.</summary>
        public static TestRules XOnly()
        {
            var rules = new TestRules();
            int rod = rules.Tile("rod", OrientationSet.IdentityOnly, OrientationSet.IdentityOnly);
            return rules.Place(rod, 0, 0, 0).Place(rod, 1, 0, 0);
        }

        /// <summary>"heavy" (base weight 3) and "light" (1), both allowed next to anything.</summary>
        public static TestRules HeavyLight()
        {
            var rules = new TestRules();
            int heavy = rules.SymmetricTile("heavy", 3f);
            int light = rules.SymmetricTile("light", 1f);
            return rules.Pair(heavy, heavy).Pair(light, light).Pair(heavy, light);
        }

        /// <summary>Tiles a, b, c, all compatible with each other; the a–c connection has weight 9, all others 1.</summary>
        public static TestRules WeightedConnections()
        {
            var rules = new TestRules();
            int a = rules.SymmetricTile("a");
            int b = rules.SymmetricTile("b");
            int c = rules.SymmetricTile("c");
            rules.Pair(a, a).Pair(b, b).Pair(c, c).Pair(a, b).Pair(a, c).Pair(b, c);
            return rules.Weight("a", "c", 9f);
        }

        /// <summary>Two fully asymmetric tiles allowed in all 48 orientations: 96 variants, no connections.</summary>
        public static TestRules ManyVariants()
        {
            var rules = new TestRules();
            rules.Tile("left", OrientationSet.All, OrientationSet.IdentityOnly);
            rules.Tile("right", OrientationSet.All, OrientationSet.IdentityOnly);
            return rules;
        }

        public static int Variant(RulesetData data, string tileName)
        {
            return data.FindVariant(System.Array.IndexOf(data.tileNames, tileName), Orientation.Identity);
        }
    }

    internal static class SolutionAssert
    {
        /// <summary>
        /// Every cell is decided, and every pair of neighbouring cells is allowed by the ruleset's adjacency lists.
        /// Checks against <see cref="RulesetData"/> directly, independent of <see cref="CompiledRules"/>.
        /// </summary>
        public static void IsValid(WfcSolver solver, RulesetData data)
        {
            WfcGrid grid = solver.Grid;
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                int variant = solver.GetCollapsedVariant(cell);
                Assert.That(variant, Is.GreaterThanOrEqualTo(0), $"cell {grid.ToCell(cell)} is not decided");
                foreach (Direction direction in Directions.Positive)
                {
                    int neighbour = grid.GetNeighbour(cell, direction);
                    if (neighbour < 0)
                    {
                        continue;
                    }
                    int other = solver.GetCollapsedVariant(neighbour);
                    var (start, end) = data.GetNeighbourRange(variant, direction);
                    bool allowed = System.Array.IndexOf(data.adjacencyVariant, other, start, end - start) >= 0;
                    Assert.That(allowed, Is.True,
                        $"variant {other} at {grid.ToCell(neighbour)} isn't allowed {direction.ToShortString()} of variant {variant} at {grid.ToCell(cell)}");
                }
            }
        }
    }
}
