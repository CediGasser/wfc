using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Wfc
{
    public enum SolverStatus
    {
        /// <summary>Undecided cells remain.</summary>
        Running,
        /// <summary>Every cell has exactly one variant left.</summary>
        Done,
        /// <summary>Some cell has no variant left.</summary>
        Contradiction,
    }

    /// <summary>
    /// Wave function collapse on a box-shaped grid, driven one observation at a time. All state lives in fields, so it
    /// can be stepped, inspected and reset at any point. A cell with exactly one variant left counts as decided.
    /// </summary>
    public sealed class WfcSolver
    {
        /// <summary>Tie-break noise added to entropies, small enough to never reorder genuinely different entropies.</summary>
        private const float NoiseScale = 1e-4f;

        private readonly Propagator propagator;
        private readonly List<(int cell, ulong[] mask)> constraints = new List<(int, ulong[])>();
        private readonly float[] noise;
        private readonly bool[] changedFlags;
        private readonly List<int> changedCells = new List<int>();
        private readonly double[] candidateWeights;
        private Unity.Mathematics.Random random;
        private bool initialPropagationDone;

        /// <param name="seed">Seed for all random choices. Must not be 0 (Unity.Mathematics.Random doesn't accept 0).</param>
        public WfcSolver(CompiledRules rules, int3 size, uint seed)
        {
            if (seed == 0)
            {
                throw new ArgumentException("The seed must not be 0.", nameof(seed));
            }
            Rules = rules;
            Grid = new WfcGrid(size);
            Wave = new Wave(Grid.CellCount, rules);
            propagator = new Propagator(rules, Grid, Wave);
            noise = new float[Grid.CellCount];
            changedFlags = new bool[Grid.CellCount];
            candidateWeights = new double[rules.VariantCount];
            Reset(seed);
        }

        public CompiledRules Rules { get; }

        public WfcGrid Grid { get; }

        public Wave Wave { get; }

        public SolverStatus Status { get; private set; }

        /// <summary>The seed of the current attempt.</summary>
        public uint Seed { get; private set; }

        /// <summary>
        /// The cell that ran out of variants while <see cref="Status"/> is <see cref="SolverStatus.Contradiction"/>, or -1.
        /// The wave is left as it was at that moment, so the cell's neighbours show what ruled every variant out.
        /// </summary>
        public int ContradictionCell { get; private set; } = -1;

        /// <summary>Cells that lost variants during the last <see cref="Step"/>, including the collapsed cell. Each listed once.</summary>
        public IReadOnlyList<int> ChangedCells => changedCells;

        /// <summary>A deterministic follow-up seed for restarts; never 0.</summary>
        public static uint NextSeed(uint seed) => Hash(seed + 0x9E3779B9u);

        /// <summary>PCG-style integer hash, never 0.</summary>
        private static uint Hash(uint value)
        {
            uint state = value * 747796405u + 2891336453u;
            uint word = ((state >> (int)((state >> 28) + 4u)) ^ state) * 277803737u;
            uint hashed = (word >> 22) ^ word;
            return hashed == 0 ? 1u : hashed;
        }

        /// <summary>
        /// Keeps only <paramref name="allowedVariants"/> in the cell and propagates. Constraints are remembered and
        /// re-applied by <see cref="Reset"/> (for boundaries and painted cells). Returns false and sets the status to
        /// <see cref="SolverStatus.Contradiction"/> if this makes the grid unsolvable.
        /// </summary>
        public bool Constrain(int cell, IEnumerable<int> allowedVariants)
        {
            var mask = new ulong[Rules.WordsPerMask];
            foreach (int variant in allowedVariants)
            {
                mask[variant >> 6] |= 1UL << (variant & 63);
            }
            return Constrain(cell, mask);
        }

        /// <summary>Like <see cref="Constrain(int, IEnumerable{int})"/>, with the allowed variants as a bitset.</summary>
        public bool Constrain(int cell, ReadOnlySpan<ulong> allowedMask)
        {
            ulong[] mask = allowedMask.ToArray();
            constraints.Add((cell, mask));
            return Status != SolverStatus.Contradiction && Apply(cell, mask);
        }

        /// <summary>
        /// Behaves as if a layer of <paramref name="outsideVariants"/> lay just outside the given side of the grid: every
        /// cell on that side may only keep variants that fit next to at least one of them. Stored like any other
        /// constraint, so <see cref="Reset"/> re-applies it.
        /// </summary>
        public bool ConstrainSide(Direction side, IEnumerable<int> outsideVariants)
        {
            var mask = new ulong[Rules.WordsPerMask];
            foreach (int outside in outsideVariants)
            {
                // The grid cell sits in the opposite direction, seen from the outside cell.
                ReadOnlySpan<ulong> compatible = Rules.GetCompatible(outside, side.Opposite());
                for (int word = 0; word < mask.Length; word++)
                {
                    mask[word] |= compatible[word];
                }
            }

            bool ok = true;
            for (int cell = 0; cell < Grid.CellCount; cell++)
            {
                if (Grid.GetNeighbour(cell, side) < 0)
                {
                    ok &= Constrain(cell, mask);
                }
            }
            return ok;
        }

        /// <summary>
        /// The undecided cell (more than one variant left) with the lowest entropy. Ties are broken by seeded noise,
        /// so the same seed always picks the same cell. Returns -1 if every cell is decided.
        /// </summary>
        public int FindLowestEntropyCell()
        {
            int best = -1;
            float bestEntropy = float.MaxValue;
            for (int cell = 0; cell < Grid.CellCount; cell++)
            {
                if (Wave.GetCount(cell) <= 1)
                {
                    continue;
                }
                float entropy = Wave.GetEntropy(cell) + noise[cell];
                if (entropy < bestEntropy)
                {
                    bestEntropy = entropy;
                    best = cell;
                }
            }
            return best;
        }

        /// <summary>
        /// Picks one remaining variant for the cell and removes all others, without propagating. The probability of a
        /// variant is its base weight times the product of its connection weights with all decided neighbours.
        /// Returns the chosen variant.
        /// </summary>
        public int CollapseCell(int cell)
        {
            if (Wave.GetCount(cell) == 0)
            {
                return -1;
            }

            double total = 0;
            double baseTotal = 0;
            ReadOnlySpan<ulong> domain = Wave.GetDomain(cell);
            for (int word = 0; word < domain.Length; word++)
            {
                ulong bits = domain[word];
                while (bits != 0)
                {
                    int variant = word * 64 + math.tzcnt(bits);
                    bits &= bits - 1;
                    double weight = Rules.GetVariantWeight(variant);
                    baseTotal += weight;
                    for (int d = 0; d < Directions.Count; d++)
                    {
                        int neighbour = Grid.GetNeighbour(cell, (Direction)d);
                        if (neighbour >= 0 && Wave.GetCount(neighbour) == 1)
                        {
                            weight *= Rules.GetConnectionWeight(variant, (Direction)d, GetCollapsedVariant(neighbour));
                        }
                    }
                    candidateWeights[variant] = weight;
                    total += weight;
                }
            }

            // If the decided neighbours rule out everything (they haven't been propagated yet), fall back to base weights.
            bool useBase = total <= 0;
            int chosen = Pick(domain, random.NextDouble() * (useBase ? baseTotal : total), useBase);

            var mask = new ulong[Rules.WordsPerMask];
            mask[chosen >> 6] = 1UL << (chosen & 63);
            if (Wave.Restrict(cell, mask))
            {
                propagator.Enqueue(cell);
            }
            return chosen;
        }

        /// <summary>
        /// One observation: find the lowest-entropy cell, collapse it, propagate. The first call also propagates every
        /// cell once, so rules that make the grid unsolvable are detected even before anything is collapsed.
        /// Returns <see cref="SolverStatus.Done"/> as soon as every cell is decided (also when that happens through
        /// propagation in this step). Does nothing once the status isn't <see cref="SolverStatus.Running"/>.
        /// </summary>
        public SolverStatus Step()
        {
            if (Status != SolverStatus.Running)
            {
                return Status;
            }
            ClearChangedCells();
            propagator.ClearChanged();

            if (!initialPropagationDone)
            {
                initialPropagationDone = true;
                for (int cell = 0; cell < Grid.CellCount; cell++)
                {
                    propagator.Enqueue(cell);
                }
                if (!PropagateAndCollect())
                {
                    return Status;
                }
            }

            int target = FindLowestEntropyCell();
            if (target < 0)
            {
                Status = SolverStatus.Done;
                return Status;
            }

            CollapseCell(target);
            MarkChanged(target);
            if (!PropagateAndCollect())
            {
                return Status;
            }
            if (FindLowestEntropyCell() < 0)
            {
                Status = SolverStatus.Done;
            }
            return Status;
        }

        /// <summary>Steps until the status isn't Running or <paramref name="maxSteps"/> steps were taken.</summary>
        public SolverStatus Run(int maxSteps = int.MaxValue)
        {
            for (int i = 0; i < maxSteps && Status == SolverStatus.Running; i++)
            {
                Step();
            }
            return Status;
        }

        /// <summary>
        /// Runs to the end; on a contradiction, resets with a new seed derived deterministically from the current one
        /// and tries again, up to <paramref name="maxAttempts"/> attempts in total.
        /// </summary>
        public SolverStatus RunWithRestarts(int maxAttempts, out int attempts)
        {
            attempts = 0;
            while (true)
            {
                attempts++;
                if (Run() != SolverStatus.Contradiction || attempts >= maxAttempts)
                {
                    return Status;
                }
                Reset(NextSeed(Seed));
            }
        }

        /// <summary>Starts over with a fresh wave and the given seed, re-applying all constraints. Status becomes Running.</summary>
        public void Reset(uint seed)
        {
            if (seed == 0)
            {
                throw new ArgumentException("The seed must not be 0.", nameof(seed));
            }
            Seed = seed;
            // Random uses its seed directly as xorshift state, so similar seeds would give similar first numbers.
            random = new Unity.Mathematics.Random(Hash(seed));
            for (int cell = 0; cell < noise.Length; cell++)
            {
                noise[cell] = random.NextFloat() * NoiseScale;
            }
            Wave.Reset();
            propagator.Clear();
            ClearChangedCells();
            initialPropagationDone = false;
            Status = SolverStatus.Running;
            ContradictionCell = -1;

            foreach (var (cell, mask) in constraints)
            {
                if (!Apply(cell, mask))
                {
                    break;
                }
            }
            ClearChangedCells();
            propagator.ClearChanged();
        }

        /// <summary>The cell's only remaining variant, or -1 if it's undecided or has no variant left.</summary>
        public int GetCollapsedVariant(int cell)
        {
            return Wave.GetCount(cell) == 1 ? GetFirstPossible(Wave.GetDomain(cell)) : -1;
        }

        private bool Apply(int cell, ulong[] mask)
        {
            if (Wave.Restrict(cell, mask))
            {
                MarkChanged(cell);
                if (Wave.GetCount(cell) == 0)
                {
                    Status = SolverStatus.Contradiction;
                    ContradictionCell = cell;
                    return false;
                }
                propagator.Enqueue(cell);
            }
            return PropagateAndCollect();
        }

        private bool PropagateAndCollect()
        {
            bool ok = propagator.Propagate();
            foreach (int cell in propagator.ChangedCells)
            {
                MarkChanged(cell);
            }
            propagator.ClearChanged();
            if (!ok)
            {
                Status = SolverStatus.Contradiction;
                ContradictionCell = propagator.ContradictionCell;
            }
            return ok;
        }

        private void MarkChanged(int cell)
        {
            if (!changedFlags[cell])
            {
                changedFlags[cell] = true;
                changedCells.Add(cell);
            }
        }

        private void ClearChangedCells()
        {
            foreach (int cell in changedCells)
            {
                changedFlags[cell] = false;
            }
            changedCells.Clear();
        }

        /// <summary>Walks the possible variants, subtracting weights from <paramref name="target"/> until it drops below 0.</summary>
        private int Pick(ReadOnlySpan<ulong> domain, double target, bool useBaseWeights)
        {
            int last = -1;
            for (int word = 0; word < domain.Length; word++)
            {
                ulong bits = domain[word];
                while (bits != 0)
                {
                    int variant = word * 64 + math.tzcnt(bits);
                    bits &= bits - 1;
                    double weight = useBaseWeights ? Rules.GetVariantWeight(variant) : candidateWeights[variant];
                    if (weight <= 0)
                    {
                        continue;
                    }
                    last = variant;
                    target -= weight;
                    if (target < 0)
                    {
                        return variant;
                    }
                }
            }
            // Rounding left a tiny remainder, or every weight is 0: take the last candidate, or any possible variant.
            return last >= 0 ? last : GetFirstPossible(domain);
        }

        private static int GetFirstPossible(ReadOnlySpan<ulong> domain)
        {
            for (int word = 0; word < domain.Length; word++)
            {
                if (domain[word] != 0)
                {
                    return word * 64 + math.tzcnt(domain[word]);
                }
            }
            return -1;
        }
    }
}
