using System;
using System.Collections.Generic;
using System.Linq;

namespace Wfc
{
    /// <summary>
    /// Connects chunks and the solver: painted chunk cells become constraints, decided solver cells become chunk
    /// content. The chunk's tiles and the ruleset's tiles must use the same indices (in Unity: the same TileSet), and
    /// the solver grid must have the chunk's size, so cell i of one is cell i of the other.
    /// </summary>
    public static class ChunkBridge
    {
        /// <summary>
        /// Constrains every non-empty cell to the variant that shows its tile in its orientation. Cells whose tile or
        /// orientation the rules don't have are skipped and added to <paramref name="unresolved"/>. Returns false if
        /// the painted cells contradict each other or the rules; the solver is then in
        /// <see cref="SolverStatus.Contradiction"/>. Like all constraints, they are re-applied by
        /// <see cref="WfcSolver.Reset"/>.
        /// </summary>
        /// <exception cref="ArgumentException">If the sizes differ.</exception>
        public static bool Prefill(WfcSolver solver, RulesetData data, ChunkData chunk, List<int> unresolved = null)
        {
            CheckSize(solver, chunk);
            var single = new int[1];
            for (int cell = 0; cell < chunk.CellCount; cell++)
            {
                if (chunk.IsEmpty(cell))
                {
                    continue;
                }
                single[0] = data.FindVariant(chunk.GetTile(cell), chunk.GetOrientation(cell));
                if (single[0] < 0)
                {
                    unresolved?.Add(cell);
                    continue;
                }
                solver.Constrain(cell, single);
            }
            return solver.Status != SolverStatus.Contradiction;
        }

        /// <summary>
        /// Writes the solver's decided cells into <paramref name="target"/> as tile and representative orientation, and
        /// empties undecided ones. Only <paramref name="cells"/> if given, else every cell.
        /// </summary>
        /// <exception cref="ArgumentException">If the sizes differ.</exception>
        public static void ReadResult(WfcSolver solver, RulesetData data, ChunkData target, IEnumerable<int> cells = null)
        {
            CheckSize(solver, target);
            foreach (int cell in cells ?? Enumerable.Range(0, target.CellCount))
            {
                int variant = solver.GetCollapsedVariant(cell);
                if (variant < 0)
                {
                    target.Clear(cell);
                }
                else
                {
                    target.Set(cell, data.variantTile[variant], data.GetVariantOrientation(variant));
                }
            }
        }

        private static void CheckSize(WfcSolver solver, ChunkData chunk)
        {
            if (!solver.Grid.Size.Equals(chunk.Size))
            {
                throw new ArgumentException($"The chunk ({chunk.Size}) and the solver grid ({solver.Grid.Size}) must have the same size.");
            }
        }
    }
}
