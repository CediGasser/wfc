using NUnit.Framework;
using Unity.Mathematics;
using Unity.PerformanceTesting;

namespace Wfc.Tests.Solver
{
    /// <summary>Baselines for solver optimizations. Compare results in Window > Analysis > Performance Test Report.</summary>
    [Category("Solver")]
    public class SolverPerformanceTests
    {
        [Test, Performance]
        public void Solve16CubedWithWeightedConnections()
        {
            CompiledRules rules = TestRules.WeightedConnections().Compile();
            uint seed = 1;
            Measure.Method(() =>
                {
                    var solver = new WfcSolver(rules, new int3(16, 16, 16), seed++);
                    Assert.That(solver.Run(), Is.EqualTo(SolverStatus.Done));
                })
                .WarmupCount(1)
                .MeasurementCount(5)
                .Run();
        }

        [Test, Performance]
        public void Solve16CubedCheckerboard()
        {
            // One collapse propagates through the whole grid: mostly measures propagation.
            CompiledRules rules = TestRules.Checker().Compile();
            uint seed = 1;
            Measure.Method(() =>
                {
                    var solver = new WfcSolver(rules, new int3(16, 16, 16), seed++);
                    Assert.That(solver.Run(), Is.EqualTo(SolverStatus.Done));
                })
                .WarmupCount(1)
                .MeasurementCount(5)
                .Run();
        }
    }
}
