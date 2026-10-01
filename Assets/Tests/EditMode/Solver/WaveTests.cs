using NUnit.Framework;
using UnityEngine;

namespace Wfc.Tests.Solver
{
    [Category("Solver")]
    public class WaveTests
    {
        private const float Tolerance = 1e-4f;

        [Test]
        public void StartsWithEveryVariantPossible()
        {
            var rules = TestRules.ManyVariants().Compile();
            var wave = new Wave(10, rules);
            Assert.That(wave.CellCount, Is.EqualTo(10));
            Assert.That(wave.VariantCount, Is.EqualTo(96));
            Assert.That(wave.WordsPerCell, Is.EqualTo(rules.WordsPerMask));
            for (int cell = 0; cell < wave.CellCount; cell++)
            {
                Assert.That(wave.GetCount(cell), Is.EqualTo(96));
                for (int v = 0; v < wave.VariantCount; v++)
                {
                    Assert.That(wave.IsPossible(cell, v), Is.True);
                }
            }
        }

        [Test]
        public void UnusedBitsInTheLastWordStayClear()
        {
            // 96 variants: the second word must only have its lowest 32 bits set.
            var wave = new Wave(1, TestRules.ManyVariants().Compile());
            Assert.That(wave.GetDomain(0)[0], Is.EqualTo(ulong.MaxValue));
            Assert.That(wave.GetDomain(0)[1], Is.EqualTo(0xFFFFFFFFUL));
        }

        [Test]
        public void BanRemovesOneVariantOnce()
        {
            var wave = new Wave(3, TestRules.ManyVariants().Compile());
            Assert.That(wave.Ban(1, 70), Is.True);
            Assert.That(wave.Ban(1, 70), Is.False, "already banned");
            Assert.That(wave.IsPossible(1, 70), Is.False);
            Assert.That(wave.GetCount(1), Is.EqualTo(95));
            Assert.That(wave.GetCount(0), Is.EqualTo(96), "other cells are untouched");
            Assert.That(wave.GetCount(2), Is.EqualTo(96));
        }

        [Test]
        public void RestrictKeepsTheIntersection()
        {
            var wave = new Wave(1, TestRules.ManyVariants().Compile());
            ulong[] allowed = { 0b1011UL, 1UL << 10 };   // variants 0, 1, 3 and 74
            Assert.That(wave.Restrict(0, allowed), Is.True);
            Assert.That(wave.GetCount(0), Is.EqualTo(4));
            Assert.That(wave.IsPossible(0, 3), Is.True);
            Assert.That(wave.IsPossible(0, 2), Is.False);
            Assert.That(wave.IsPossible(0, 74), Is.True);

            Assert.That(wave.Restrict(0, allowed), Is.False, "nothing left to remove");
            ulong[] none = { 0UL, 0UL };
            Assert.That(wave.Restrict(0, none), Is.True);
            Assert.That(wave.GetCount(0), Is.EqualTo(0));
        }

        [Test]
        public void EntropyOfEqualWeightsIsLogOfTheCount()
        {
            var wave = new Wave(1, TestRules.ManyVariants().Compile());
            Assert.That(wave.GetEntropy(0), Is.EqualTo(Mathf.Log(96)).Within(Tolerance));
            wave.Ban(0, 5);
            Assert.That(wave.GetEntropy(0), Is.EqualTo(Mathf.Log(95)).Within(Tolerance), "updated incrementally after a ban");
        }

        [Test]
        public void EntropyUsesTheWeights()
        {
            // heavy has weight 3, light weight 1: H = log(4) - (3·log 3 + 1·log 1) / 4.
            var wave = new Wave(1, TestRules.HeavyLight().Compile());
            float expected = Mathf.Log(4f) - 3f * Mathf.Log(3f) / 4f;
            Assert.That(wave.GetEntropy(0), Is.EqualTo(expected).Within(Tolerance));
        }

        [Test]
        public void DecidedAndEmptyCellsHaveZeroEntropy()
        {
            RulesetData data = TestRules.HeavyLight().Data();
            var wave = new Wave(2, new CompiledRules(data));
            wave.Ban(0, TestRules.Variant(data, "heavy"));
            Assert.That(wave.GetEntropy(0), Is.EqualTo(0f));
            wave.Ban(1, 0);
            wave.Ban(1, 1);
            Assert.That(wave.GetCount(1), Is.EqualTo(0));
            Assert.That(wave.GetEntropy(1), Is.EqualTo(0f));
        }

        [Test]
        public void ResetMakesEverythingPossibleAgain()
        {
            var wave = new Wave(2, TestRules.ManyVariants().Compile());
            wave.Ban(0, 1);
            wave.Restrict(1, new ulong[] { 1UL, 0UL });
            wave.Reset();
            Assert.That(wave.GetCount(0), Is.EqualTo(96));
            Assert.That(wave.GetCount(1), Is.EqualTo(96));
            Assert.That(wave.GetEntropy(1), Is.EqualTo(Mathf.Log(96)).Within(Tolerance));
        }
    }
}
