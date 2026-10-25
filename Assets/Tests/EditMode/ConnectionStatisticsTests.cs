using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using Wfc.Tests.Solver;

namespace Wfc.Tests
{
    public class ConnectionStatisticsTests
    {
        [Test]
        public void CheckerboardIsOneConnectionBetweenEveryNeighbourPair()
        {
            RulesetData data = TestRules.Checker().Data();
            var rules = new CompiledRules(data);
            var solver = new WfcSolver(rules, new int3(3, 3, 3), 1);
            Assert.That(solver.Run(), Is.EqualTo(SolverStatus.Done));

            var connections = new long[rules.ConnectionCount];
            ConnectionStatistics.CountConnections(solver, connections);
            Assert.That(connections, Is.EqualTo(new long[] { 54 }), "3 axes × 18 neighbouring pairs, all black–white");

            var tiles = new long[data.TileCount];
            ConnectionStatistics.CountTiles(solver, tiles);
            Assert.That(tiles.Sum(), Is.EqualTo(27));
            Assert.That(tiles.OrderBy(n => n), Is.EqualTo(new long[] { 13, 14 }));
        }

        [Test]
        public void SharesLeaveOutExcludedEntries()
        {
            double[] shares = ConnectionStatistics.Shares(new long[] { 1, 3, 100 }, new[] { false, false, true });
            Assert.That(shares, Is.EqualTo(new[] { 0.25, 0.75, 0 }));
            Assert.That(ConnectionStatistics.TotalVariation(shares, new[] { 0.5, 0.5, 0 }), Is.EqualTo(0.25).Within(1e-12));
        }

        [Test]
        public void UpdateMovesWeightsTowardsTheTarget()
        {
            float[] weights = { 1f, 1f, 1f, 0f, 2f };
            double[] target = { 0.5, 0.25, 0.25, 0.1, 0.1 };
            double[] output = { 0.25, 0.25, 0.5, 0.1, 0.5 };
            bool[] excluded = { false, false, false, false, true };

            ConnectionStatistics.UpdateWeights(weights, target, output, 1.0, excluded);

            // Factors 2, 1 and 0.5 already have a geometric mean of 1.
            Assert.That(weights[0], Is.EqualTo(2f).Within(1e-5f));
            Assert.That(weights[1], Is.EqualTo(1f).Within(1e-5f));
            Assert.That(weights[2], Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(weights[3], Is.EqualTo(0f), "forbidden connections stay forbidden");
            Assert.That(weights[4], Is.EqualTo(2f), "excluded connections stay as they are");
        }

        [Test]
        public void UpdateStepsAreLimited()
        {
            float[] weights = { 1f, 1f };
            ConnectionStatistics.UpdateWeights(weights, new[] { 0.9, 0.1 }, new[] { 0.0, 0.9 }, 1.0, new[] { false, false });
            // Never seen: ×4. Far too common: ×1/4 (not ×1/9). Then scaled to a geometric mean of 1.
            Assert.That(weights[0] / weights[1], Is.EqualTo(16f).Within(1e-3f));
            Assert.That(weights[0] * weights[1], Is.EqualTo(1f).Within(1e-5f));
        }
    }
}
