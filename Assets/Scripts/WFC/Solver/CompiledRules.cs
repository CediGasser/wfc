using System;

namespace Wfc
{
    /// <summary>
    /// Solver-ready form of <see cref="RulesetData"/>: for every variant and direction a bitset of the variants allowed
    /// next to it, plus fast access to variant and connection weights. Built once per ruleset.
    /// </summary>
    public sealed class CompiledRules
    {
        private readonly int[] variantTile;
        private readonly float[] variantWeight;
        // [variant][direction][word]
        private readonly ulong[] compatible;
        // [variant][direction][neighbour], 0 where not allowed
        private readonly float[] connectionWeight;

        public CompiledRules(RulesetData data)
        {
            VariantCount = data.VariantCount;
            WordsPerMask = Math.Max(1, (VariantCount + 63) / 64);
            variantTile = (int[])data.variantTile.Clone();
            variantWeight = (float[])data.variantWeight.Clone();
            compatible = new ulong[VariantCount * Directions.Count * WordsPerMask];
            connectionWeight = new float[VariantCount * Directions.Count * VariantCount];

            for (int v = 0; v < VariantCount; v++)
            {
                for (int d = 0; d < Directions.Count; d++)
                {
                    var (start, end) = data.GetNeighbourRange(v, (Direction)d);
                    int maskOffset = MaskOffset(v, (Direction)d);
                    for (int i = start; i < end; i++)
                    {
                        int neighbour = data.adjacencyVariant[i];
                        compatible[maskOffset + (neighbour >> 6)] |= 1UL << (neighbour & 63);
                        connectionWeight[WeightIndex(v, (Direction)d, neighbour)] = data.adjacencyWeight[i];
                    }
                }
            }
        }

        public int VariantCount { get; }

        /// <summary>Number of 64-bit words in one variant bitset: ceil(VariantCount / 64), at least 1.</summary>
        public int WordsPerMask { get; }

        /// <summary>The tile index (into <see cref="RulesetData.tileNames"/>) a variant belongs to.</summary>
        public int GetTile(int variant) => variantTile[variant];

        /// <summary>Base weight of a variant (<see cref="RulesetData.variantWeight"/>).</summary>
        public float GetVariantWeight(int variant) => variantWeight[variant];

        /// <summary>
        /// Bitset of <see cref="WordsPerMask"/> words: bit n is set if variant n may sit next to
        /// <paramref name="variant"/> in <paramref name="direction"/>.
        /// </summary>
        public ReadOnlySpan<ulong> GetCompatible(int variant, Direction direction)
        {
            return new ReadOnlySpan<ulong>(compatible, MaskOffset(variant, direction), WordsPerMask);
        }

        /// <summary>True if <paramref name="neighbour"/> may sit next to <paramref name="variant"/> in <paramref name="direction"/>.</summary>
        public bool IsAllowed(int variant, Direction direction, int neighbour)
        {
            return (compatible[MaskOffset(variant, direction) + (neighbour >> 6)] & (1UL << (neighbour & 63))) != 0;
        }

        /// <summary>The learned connection weight between the two variants in that direction, or 0 if they can't be neighbours.</summary>
        public float GetConnectionWeight(int variant, Direction direction, int neighbour)
        {
            return connectionWeight[WeightIndex(variant, direction, neighbour)];
        }

        private int MaskOffset(int variant, Direction direction) => (variant * Directions.Count + (int)direction) * WordsPerMask;

        private int WeightIndex(int variant, Direction direction, int neighbour) => (variant * Directions.Count + (int)direction) * VariantCount + neighbour;
    }
}
