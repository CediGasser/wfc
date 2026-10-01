using System;
using System.Collections.Generic;

namespace Wfc
{
    /// <summary>
    /// Constraint propagation: after a cell lost variants, its neighbours may only keep variants that are compatible
    /// with at least one remaining variant of that cell. Changes spread through the grid until nothing changes.
    /// </summary>
    public sealed class Propagator
    {
        private readonly CompiledRules rules;
        private readonly WfcGrid grid;
        private readonly Wave wave;
        private readonly Queue<int> queue = new Queue<int>();
        private readonly bool[] queued;
        private readonly bool[] changed;
        private readonly List<int> changedCells = new List<int>();
        private readonly ulong[] allowed;

        public Propagator(CompiledRules rules, WfcGrid grid, Wave wave)
        {
            this.rules = rules;
            this.grid = grid;
            this.wave = wave;
            queued = new bool[grid.CellCount];
            changed = new bool[grid.CellCount];
            allowed = new ulong[rules.WordsPerMask];
        }

        /// <summary>
        /// Cells whose variants were removed by <see cref="Propagate"/> since the last <see cref="ClearChanged"/>,
        /// each listed once. Enqueued cells are only included if propagation itself shrank them.
        /// </summary>
        public IReadOnlyList<int> ChangedCells => changedCells;

        /// <summary>The cell that ran out of variants in the last failed <see cref="Propagate"/>, or -1.</summary>
        public int ContradictionCell { get; private set; } = -1;

        /// <summary>Marks a cell whose variants shrank, so its neighbours get re-checked. Enqueuing a cell twice is harmless.</summary>
        public void Enqueue(int cell)
        {
            if (!queued[cell])
            {
                queued[cell] = true;
                queue.Enqueue(cell);
            }
        }

        /// <summary>Processes the queue until it's empty. Returns false as soon as a cell has no variants left.</summary>
        public bool Propagate()
        {
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                queued[cell] = false;
                if (wave.GetCount(cell) == 0)
                {
                    return Fail(cell);
                }

                ReadOnlySpan<ulong> domain = wave.GetDomain(cell);
                for (int d = 0; d < Directions.Count; d++)
                {
                    var direction = (Direction)d;
                    int neighbour = grid.GetNeighbour(cell, direction);
                    if (neighbour < 0)
                    {
                        continue;
                    }

                    // Union of everything the cell's remaining variants allow on that side.
                    Array.Clear(allowed, 0, allowed.Length);
                    for (int word = 0; word < domain.Length; word++)
                    {
                        ulong bits = domain[word];
                        while (bits != 0)
                        {
                            int variant = word * 64 + Unity.Mathematics.math.tzcnt(bits);
                            bits &= bits - 1;
                            ReadOnlySpan<ulong> compatible = rules.GetCompatible(variant, direction);
                            for (int w = 0; w < allowed.Length; w++)
                            {
                                allowed[w] |= compatible[w];
                            }
                        }
                    }

                    if (!wave.Restrict(neighbour, allowed))
                    {
                        continue;
                    }
                    if (!changed[neighbour])
                    {
                        changed[neighbour] = true;
                        changedCells.Add(neighbour);
                    }
                    if (wave.GetCount(neighbour) == 0)
                    {
                        return Fail(neighbour);
                    }
                    Enqueue(neighbour);
                }
            }
            return true;
        }

        public void ClearChanged()
        {
            foreach (int cell in changedCells)
            {
                changed[cell] = false;
            }
            changedCells.Clear();
        }

        /// <summary>Empties the queue and the changed list and forgets the contradiction, e.g. after the wave was reset.</summary>
        public void Clear()
        {
            ClearQueue();
            ClearChanged();
            ContradictionCell = -1;
        }

        private bool Fail(int cell)
        {
            ContradictionCell = cell;
            ClearQueue();
            return false;
        }

        private void ClearQueue()
        {
            while (queue.Count > 0)
            {
                queued[queue.Dequeue()] = false;
            }
        }
    }
}
