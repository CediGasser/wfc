using System.Linq;
using NUnit.Framework;
using Wfc.Tests.Solver;

namespace Wfc.Tests
{
    public class RulesetDataTests
    {
        [Test]
        public void FingerprintDoesNotDependOnTileOrder()
        {
            RulesetData Build(params string[] order)
            {
                var rules = new TestRules();
                var tiles = order.ToDictionary(name => name, name => rules.SymmetricTile(name));
                return rules.Pair(tiles["a"], tiles["b"]).Pair(tiles["b"], tiles["b"]).Data();
            }

            Assert.That(RulesetFingerprint.Describe(Build("b", "a")), Is.EqualTo(RulesetFingerprint.Describe(Build("a", "b"))));
        }

        [Test]
        public void FingerprintSeesWeightChanges()
        {
            RulesetData plain = TestRules.WeightedConnections().Data();
            RulesetData changed = plain.WithConnectionWeights(plain.connections.Select(c => c.weight * 2f).ToArray());
            Assert.That(RulesetFingerprint.Describe(changed), Is.Not.EqualTo(RulesetFingerprint.Describe(plain)));
        }

        [Test]
        public void WithConnectionWeightsLeavesTheOriginalAlone()
        {
            RulesetData data = TestRules.WeightedConnections().Data();
            float[] before = data.connections.Select(c => c.weight).ToArray();
            RulesetData changed = data.WithConnectionWeights(Enumerable.Repeat(3f, data.connections.Length).ToArray());

            Assert.That(data.connections.Select(c => c.weight), Is.EqualTo(before));
            Assert.That(changed.connections.Select(c => c.weight), Is.All.EqualTo(3f));
            Assert.That(Enumerable.Range(0, changed.adjacencyVariant.Length).All(i => changed.GetEntryWeight(i) == 3f), Is.True);
        }

        [Test]
        public void WithConnectionWeightsMatchesOverrides()
        {
            // Re-weighting baked rules must give the same solver input as deriving them again with overrides.
            TestRules rules = TestRules.WeightedConnections();
            RulesetData data = rules.Data();
            float[] weights = data.connections.Select((c, i) => 0.5f + i).ToArray();
            for (int c = 0; c < weights.Length; c++)
            {
                rules.Weight(data.tileNames[data.connections[c].tileA], data.tileNames[data.connections[c].tileB], weights[c]);
            }
            Assert.That(Adjacency(data.WithConnectionWeights(weights)), Is.EqualTo(Adjacency(rules.Data())));
        }

        /// <summary>The adjacency lines of the fingerprint: neighbours and weights, all the solver uses besides variant weights.</summary>
        private static string[] Adjacency(RulesetData data)
        {
            return RulesetFingerprint.Describe(data).Split('\n').Where(line => line.StartsWith("adjacent ")).ToArray();
        }

        [Test]
        public void WithConnectionWeightsCantForbidConnections()
        {
            RulesetData data = TestRules.Checker().Data();
            Assert.That(() => data.WithConnectionWeights(new[] { 0f }), Throws.ArgumentException);
            Assert.That(() => data.WithConnectionWeights(new[] { 1f, 2f }), Throws.ArgumentException);
        }
    }
}
