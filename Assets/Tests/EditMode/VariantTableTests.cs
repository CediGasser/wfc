using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Wfc.Tests
{
    public class VariantTableTests
    {
        /// <summary>All orientations that map the pipe's open ends (±X) onto each other.</summary>
        private static readonly OrientationSet StraightPipeSymmetry = new OrientationSet(OrientationSet.All.ToList()
            .Where(o => o.Apply(Direction.PosX).Axis() == 0)
            .Aggregate(0UL, (mask, o) => mask | (1UL << o.Index)));

        private static TileSpec Tile(string name, OrientationSet allowed, OrientationSet symmetry)
        {
            return new TileSpec { id = name, name = name, allowed = allowed, symmetry = symmetry, baseWeight = 1f };
        }

        [Test]
        public void FindVariantAcceptsEveryOrientationOfAVariant()
        {
            var tiles = new List<TileSpec> { Tile("straight", OrientationSet.AllRotations, StraightPipeSymmetry) };
            RulesetData data = new RulesetBuilder(tiles).Build(false, null, out _);

            Assert.That(data.VariantCount, Is.EqualTo(3), "one variant per axis");
            foreach (Orientation orientation in OrientationSet.All)
            {
                int variant = data.FindVariant(0, orientation);
                if (!OrientationSet.AllRotations.Contains(orientation))
                {
                    Assert.That(variant, Is.EqualTo(-1), $"{orientation} isn't allowed");
                    continue;
                }
                Direction axis = orientation.Apply(Direction.PosX);
                Direction variantAxis = data.GetVariantOrientation(variant).Apply(Direction.PosX);
                Assert.That(variantAxis.Axis(), Is.EqualTo(axis.Axis()), $"{orientation} looks like variant {variant}");
            }
        }

        [Test]
        public void RepresentativesMatchTheVariants()
        {
            var tiles = new List<TileSpec>
            {
                Tile("straight", OrientationSet.AllRotations, StraightPipeSymmetry),
                Tile("block", OrientationSet.YawMirror, OrientationSet.IdentityOnly),
            };
            var table = new VariantTable(tiles);
            for (int t = 0; t < tiles.Count; t++)
            {
                IEnumerable<int> expected = Enumerable.Range(0, table.VariantCount).Where(v => table.VariantTile[v] == t).Select(v => (int)table.VariantOrientation[v]);
                Assert.That(VariantTable.Representatives(table.Allowed(t), table.Effective(t)).Select(o => (int)o.Index), Is.EqualTo(expected));
            }
        }

        [Test]
        public void TilesThatDontTakePartGetNoVariants()
        {
            var tiles = new List<TileSpec>
            {
                Tile("a", OrientationSet.Yaw, OrientationSet.IdentityOnly),
                Tile("b", OrientationSet.Yaw, OrientationSet.IdentityOnly),
                new TileSpec { name = "empty slot" },
            };
            var table = new VariantTable(tiles, new[] { false, true, true });
            Assert.That(table.IsValid(0), Is.False);
            Assert.That(table.IsValid(2), Is.False, "undefined tiles never get variants");
            Assert.That(table.VariantTile, Is.All.EqualTo(1));
            Assert.That(table.VariantOf(0, Orientation.Identity), Is.EqualTo(VariantTable.None));
        }

        [Test]
        public void DuplicateIdsAreReportedAndIgnored()
        {
            var tiles = new List<TileSpec> { Tile("a", OrientationSet.Yaw, OrientationSet.All), Tile("a", OrientationSet.Yaw, OrientationSet.All) };
            var diagnostics = new List<RuleDiagnostic>();
            var table = new VariantTable(tiles, diagnostics: diagnostics);
            Assert.That(table.IsValid(0), Is.True);
            Assert.That(table.IsValid(1), Is.False);
            Assert.That(diagnostics.Single().tile, Is.EqualTo(1));
        }

        [Test]
        public void ExtraSymmetrySplitsVariants()
        {
            var tiles = new List<TileSpec> { Tile("cube", OrientationSet.Yaw, OrientationSet.All) };
            Assert.That(new VariantTable(tiles).VariantCount, Is.EqualTo(1));
            Assert.That(new VariantTable(tiles, extraSymmetry: new[] { OrientationSet.IdentityOnly }).VariantCount, Is.EqualTo(4));
        }
    }
}
