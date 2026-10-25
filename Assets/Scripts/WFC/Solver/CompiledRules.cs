using System;

namespace Wfc
{
    /// <summary>
    /// Solver-ready form of <see cref="RulesetData"/>: for every variant and direction a bitset of the variants allowed
    /// next to it (for propagation) and the same neighbours as a sorted list with connection weights (for collapsing),
    /// plus fast access to variant weights. Built once per ruleset.
    /// </summary>
    public sealed class CompiledRules
    {
        private readonly int[] variantTile;
        private readonly float[] variantWeight;
        // [variant][direction][word]
        private readonly ulong[] compatible;
        // CSR lists, copied from RulesetData: neighbours of slot v*6+d are at [neighbourStart[slot], neighbourStart[slot+1]).
        private readonly int[] neighbourStart;
        private readonly int[] neighbourVariant;
        private readonly float[] neighbourWeight;
        private readonly int[] neighbourConnection;

        public CompiledRules(RulesetData data)
        {
            VariantCount = data.VariantCount;
            WordsPerMask = Math.Max(1, (VariantCount + 63) / 64);
            ConnectionCount = data.connections.Length;
            variantTile = (int[])data.variantTile.Clone();
            variantWeight = (float[])data.variantWeight.Clone();
            compatible = new ulong[VariantCount * Directions.Count * WordsPerMask];
            neighbourStart = (int[])data.adjacencyStart.Clone();
            neighbourVariant = (int[])data.adjacencyVariant.Clone();
            neighbourConnection = (int[])data.adjacencyConnection.Clone();
            neighbourWeight = new float[neighbourVariant.Length];

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
                        neighbourWeight[i] = data.GetEntryWeight(i);
                    }
                }
            }
        }

        public int VariantCount { get; }

        /// <summary>Number of 64-bit words in one variant bitset: ceil(VariantCount / 64), at least 1.</summary>
        public int WordsPerMask { get; }

        /// <summary>Number of learned connections (<see cref="RulesetData.connections"/>).</summary>
        public int ConnectionCount { get; }

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

        /// <summary>The variants allowed next to <paramref name="variant"/> in <paramref name="direction"/>, in ascending order.</summary>
        public ReadOnlySpan<int> GetNeighbourVariants(int variant, Direction direction)
        {
            int slot = variant * Directions.Count + (int)direction;
            return new ReadOnlySpan<int>(neighbourVariant, neighbourStart[slot], neighbourStart[slot + 1] - neighbourStart[slot]);
        }

        /// <summary>Connection weights parallel to <see cref="GetNeighbourVariants"/>.</summary>
        public ReadOnlySpan<float> GetNeighbourWeights(int variant, Direction direction)
        {
            int slot = variant * Directions.Count + (int)direction;
            return new ReadOnlySpan<float>(neighbourWeight, neighbourStart[slot], neighbourStart[slot + 1] - neighbourStart[slot]);
        }

        /// <summary>The learned connection weight between the two variants in that direction, or 0 if they can't be neighbours.</summary>
        public float GetConnectionWeight(int variant, Direction direction, int neighbour)
        {
            int index = FindEntry(variant, direction, neighbour);
            return index < 0 ? 0f : neighbourWeight[index];
        }

        /// <summary>The connection (index into <see cref="RulesetData.connections"/>) the two neighbours form, or -1 if they can't be neighbours.</summary>
        public int GetConnection(int variant, Direction direction, int neighbour)
        {
            int index = FindEntry(variant, direction, neighbour);
            return index < 0 ? -1 : neighbourConnection[index];
        }

        private int FindEntry(int variant, Direction direction, int neighbour)
        {
            int slot = variant * Directions.Count + (int)direction;
            int start = neighbourStart[slot];
            int index = Array.BinarySearch(neighbourVariant, start, neighbourStart[slot + 1] - start, neighbour);
            return index < 0 ? -1 : index;
        }

        private int MaskOffset(int variant, Direction direction) => (variant * Directions.Count + (int)direction) * WordsPerMask;
    }
}
