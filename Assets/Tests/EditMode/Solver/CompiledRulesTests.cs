using NUnit.Framework;

namespace Wfc.Tests.Solver
{
    [Category("Solver")]
    public class CompiledRulesTests
    {
        private static bool BitSet(System.ReadOnlySpan<ulong> mask, int bit) => (mask[bit >> 6] & (1UL << (bit & 63))) != 0;

        [Test]
        public void CountsMatchTheRulesetData()
        {
            RulesetData data = TestRules.Layers().Data();
            var rules = new CompiledRules(data);
            Assert.That(rules.VariantCount, Is.EqualTo(data.VariantCount));
            Assert.That(rules.WordsPerMask, Is.EqualTo(1));
            for (int v = 0; v < data.VariantCount; v++)
            {
                Assert.That(rules.GetTile(v), Is.EqualTo(data.variantTile[v]));
                Assert.That(rules.GetVariantWeight(v), Is.EqualTo(data.variantWeight[v]));
            }
        }

        [Test]
        public void MoreThan64VariantsNeedSeveralWords()
        {
            var rules = TestRules.ManyVariants().Compile();
            Assert.That(rules.VariantCount, Is.EqualTo(96));
            Assert.That(rules.WordsPerMask, Is.EqualTo(2));
            Assert.That(rules.GetCompatible(0, Direction.PosX).Length, Is.EqualTo(2));
        }

        [Test]
        public void CompatibleBitsetsMatchTheAdjacencyLists()
        {
            RulesetData data = TestRules.Layers().Data();
            var rules = new CompiledRules(data);
            for (int v = 0; v < data.VariantCount; v++)
            {
                for (int d = 0; d < Directions.Count; d++)
                {
                    var direction = (Direction)d;
                    var (start, end) = data.GetNeighbourRange(v, direction);
                    System.ReadOnlySpan<ulong> compatible = rules.GetCompatible(v, direction);
                    Assert.That(compatible.Length, Is.EqualTo(rules.WordsPerMask));
                    for (int n = 0; n < data.VariantCount; n++)
                    {
                        bool listed = System.Array.IndexOf(data.adjacencyVariant, n, start, end - start) >= 0;
                        Assert.That(BitSet(compatible, n), Is.EqualTo(listed), $"variant {n} next to {v} towards {direction.ToShortString()}");
                        Assert.That(rules.IsAllowed(v, direction, n), Is.EqualTo(listed));
                    }
                }
            }
        }

        [Test]
        public void LayersRulesHaveTheExpectedShape()
        {
            RulesetData data = TestRules.Layers().Data();
            var rules = new CompiledRules(data);
            int bottom = TestRules.Variant(data, "bottom");
            int top = TestRules.Variant(data, "top");

            Assert.That(rules.IsAllowed(bottom, Direction.PosY, top), Is.True);
            Assert.That(rules.IsAllowed(top, Direction.NegY, bottom), Is.True);
            Assert.That(rules.IsAllowed(top, Direction.PosY, bottom), Is.False);
            Assert.That(rules.IsAllowed(bottom, Direction.PosY, bottom), Is.False);
            Assert.That(rules.IsAllowed(bottom, Direction.PosX, bottom), Is.True);
            Assert.That(rules.IsAllowed(bottom, Direction.PosX, top), Is.False);
        }

        [Test]
        public void AllowedIsSymmetric()
        {
            var rules = TestRules.Checker().Compile();
            for (int a = 0; a < rules.VariantCount; a++)
            {
                for (int d = 0; d < Directions.Count; d++)
                {
                    for (int b = 0; b < rules.VariantCount; b++)
                    {
                        Assert.That(rules.IsAllowed(a, (Direction)d, b), Is.EqualTo(rules.IsAllowed(b, ((Direction)d).Opposite(), a)));
                    }
                }
            }
        }

        [Test]
        public void ConnectionWeightsComeFromTheRules()
        {
            RulesetData data = TestRules.WeightedConnections().Data();
            var rules = new CompiledRules(data);
            int a = TestRules.Variant(data, "a");
            int b = TestRules.Variant(data, "b");
            int c = TestRules.Variant(data, "c");

            Assert.That(rules.GetConnectionWeight(a, Direction.PosX, c), Is.EqualTo(9f));
            Assert.That(rules.GetConnectionWeight(c, Direction.NegY, a), Is.EqualTo(9f), "learned along X, expanded to all axes, same in both directions");
            Assert.That(rules.GetConnectionWeight(a, Direction.PosZ, b), Is.EqualTo(1f));

            RulesetData checker = TestRules.Checker().Data();
            var checkerRules = new CompiledRules(checker);
            int black = TestRules.Variant(checker, "black");
            Assert.That(checkerRules.GetConnectionWeight(black, Direction.PosX, black), Is.EqualTo(0f), "not allowed means weight 0");
        }
    }
}
