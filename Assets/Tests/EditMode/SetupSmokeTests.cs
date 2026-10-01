using NUnit.Framework;
using Unity.PerformanceTesting;

namespace Wfc.Tests
{
    /// <summary>
    /// Placeholder proving the test and performance frameworks are wired up. Delete once real solver tests exist.
    /// </summary>
    public class SetupSmokeTests
    {
        [Test]
        public void TestFrameworkRuns()
        {
            Assert.That(1 + 1, Is.EqualTo(2));
        }

        [Test, Performance]
        public void PerformanceFrameworkMeasures()
        {
            var bits = new ulong[1024];
            Measure.Method(() =>
                {
                    for (int i = 0; i < bits.Length; i++)
                    {
                        bits[i] = ~bits[i];
                    }
                })
                .WarmupCount(5)
                .MeasurementCount(20)
                .Run();
        }
    }
}
