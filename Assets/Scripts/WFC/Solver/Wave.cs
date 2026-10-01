using System;
using Unity.Mathematics;

namespace Wfc
{
    /// <summary>
    /// The remaining possible variants of every cell, one bitset per cell, with entropy kept up to date incrementally
    /// as variants are removed.
    /// </summary>
    public sealed class Wave
    {
        private readonly ulong[] domains;
        private readonly ulong[] fullMask;
        private readonly int[] counts;
        private readonly double[] sumWeights;
        private readonly double[] sumWeightLogWeights;
        private readonly double[] weights;
        private readonly double[] weightLogWeights;
        private readonly double fullSumWeights;
        private readonly double fullSumWeightLogWeights;

        /// <summary>Creates a wave where every variant is possible in every cell. Weights come from <paramref name="rules"/>.</summary>
        public Wave(int cellCount, CompiledRules rules)
        {
            CellCount = cellCount;
            VariantCount = rules.VariantCount;
            WordsPerCell = rules.WordsPerMask;
            domains = new ulong[cellCount * WordsPerCell];
            counts = new int[cellCount];
            sumWeights = new double[cellCount];
            sumWeightLogWeights = new double[cellCount];

            weights = new double[VariantCount];
            weightLogWeights = new double[VariantCount];
            for (int v = 0; v < VariantCount; v++)
            {
                double w = rules.GetVariantWeight(v);
                weights[v] = w;
                weightLogWeights[v] = w > 0 ? w * Math.Log(w) : 0;
                fullSumWeights += w;
                fullSumWeightLogWeights += weightLogWeights[v];
            }

            fullMask = new ulong[WordsPerCell];
            for (int v = 0; v < VariantCount; v++)
            {
                fullMask[v >> 6] |= 1UL << (v & 63);
            }
            Reset();
        }

        public int CellCount { get; }

        public int VariantCount { get; }

        /// <summary>Words per cell bitset, the same as <see cref="CompiledRules.WordsPerMask"/>.</summary>
        public int WordsPerCell { get; }

        public bool IsPossible(int cell, int variant)
        {
            return (domains[cell * WordsPerCell + (variant >> 6)] & (1UL << (variant & 63))) != 0;
        }

        /// <summary>Number of variants still possible in the cell.</summary>
        public int GetCount(int cell) => counts[cell];

        /// <summary>The cell's bitset (<see cref="WordsPerCell"/> words).</summary>
        public ReadOnlySpan<ulong> GetDomain(int cell)
        {
            return new ReadOnlySpan<ulong>(domains, cell * WordsPerCell, WordsPerCell);
        }

        /// <summary>Removes one variant from the cell. Returns true if it was possible before.</summary>
        public bool Ban(int cell, int variant)
        {
            int index = cell * WordsPerCell + (variant >> 6);
            ulong bit = 1UL << (variant & 63);
            if ((domains[index] & bit) == 0)
            {
                return false;
            }
            domains[index] &= ~bit;
            Removed(cell, variant);
            return true;
        }

        /// <summary>Keeps only the variants set in <paramref name="allowed"/> (bitwise AND). Returns true if the cell lost variants.</summary>
        public bool Restrict(int cell, ReadOnlySpan<ulong> allowed)
        {
            bool changed = false;
            int offset = cell * WordsPerCell;
            for (int word = 0; word < WordsPerCell; word++)
            {
                ulong current = domains[offset + word];
                ulong removed = current & ~allowed[word];
                if (removed == 0)
                {
                    continue;
                }
                domains[offset + word] = current & allowed[word];
                changed = true;
                while (removed != 0)
                {
                    int bit = math.tzcnt(removed);
                    removed &= removed - 1;
                    Removed(cell, word * 64 + bit);
                }
            }
            return changed;
        }

        /// <summary>
        /// Shannon entropy (natural log) of the remaining variants' weights: log(Σw) − Σ(w·log w) / Σw.
        /// 0 for cells with one or no variant left.
        /// </summary>
        public float GetEntropy(int cell)
        {
            if (counts[cell] <= 1 || sumWeights[cell] <= 0)
            {
                return 0f;
            }
            double sum = sumWeights[cell];
            return (float)Math.Max(0, Math.Log(sum) - sumWeightLogWeights[cell] / sum);
        }

        /// <summary>Makes every variant possible in every cell again.</summary>
        public void Reset()
        {
            for (int cell = 0; cell < CellCount; cell++)
            {
                Array.Copy(fullMask, 0, domains, cell * WordsPerCell, WordsPerCell);
                counts[cell] = VariantCount;
                sumWeights[cell] = fullSumWeights;
                sumWeightLogWeights[cell] = fullSumWeightLogWeights;
            }
        }

        private void Removed(int cell, int variant)
        {
            counts[cell]--;
            sumWeights[cell] -= weights[variant];
            sumWeightLogWeights[cell] -= weightLogWeights[variant];
        }
    }
}
