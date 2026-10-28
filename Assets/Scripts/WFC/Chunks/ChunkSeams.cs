using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace Wfc
{
    /// <summary>
    /// Generates one chunk of an unbounded horizontal grid of equally sized chunks so that it fits the chunks generated
    /// before it. Chunk (cx, cz) covers cells (cx · size.x + x, y, cz · size.z + z). The solver grid is the chunk plus a
    /// margin on its four horizontal sides; margin cells that lie in existing chunks are fixed (painted), the others are
    /// free and thrown away. With a band of k, the k cells next to the chunk inside its four side neighbours are freed
    /// as well and generated again together with the chunk, while the cells behind them stay fixed (Merrell's
    /// "modifying in blocks"). That gives the solver room when the neighbours' borders alone can't be continued.
    /// Neither side ever changes the cells of diagonal neighbours. No Unity API, so it can run on a worker thread.
    /// </summary>
    public static class ChunkSeams
    {
        /// <summary>A cell of a neighbouring chunk that was generated again in the freed band.</summary>
        public readonly struct BandCell
        {
            public BandCell(int2 chunk, int cell, int tile, Orientation orientation)
            {
                Chunk = chunk;
                Cell = cell;
                Tile = tile;
                Orientation = orientation;
            }

            public int2 Chunk { get; }
            public int Cell { get; }
            public int Tile { get; }
            public Orientation Orientation { get; }
        }

        public sealed class Result
        {
            public SolverStatus Status { get; internal set; }
            public int Attempts { get; internal set; }
            /// <summary>The generated chunk, or null if the generation failed.</summary>
            public ChunkData Chunk { get; internal set; }
            /// <summary>Cells of side neighbours generated again in the band; empty without a band.</summary>
            public List<BandCell> BandCells { get; } = new List<BandCell>();
        }

        /// <summary>The margin around the chunk for a band of <paramref name="band"/> cells: the band plus one fixed cell.</summary>
        public static int Margin(int band) => band + 1;

        /// <summary>
        /// The solver input: a chunk of the extended size whose cells inside existing chunks (from
        /// <paramref name="lookup"/>, null for missing ones) are copies of their tiles, except the chunk itself and the
        /// band. Extended cell (x, y, z) is chunk cell (x − margin, y, z − margin).
        /// </summary>
        public static ChunkData BuildInput(int3 chunkSize, int2 coordinate, Func<int2, ChunkData> lookup, int band)
        {
            int margin = Margin(band);
            var input = new ChunkData(ExtendedSize(chunkSize, margin));
            for (int cell = 0; cell < input.CellCount; cell++)
            {
                int3 local = input.ToCell(cell) - new int3(margin, 0, margin);
                if (!TryLocate(chunkSize, coordinate, local, band, out int2 owner, out int ownerCell))
                {
                    continue;
                }
                ChunkData source = lookup(owner);
                if (source != null && !source.IsEmpty(ownerCell))
                {
                    input.Set(cell, source.GetTile(ownerCell), source.GetOrientation(ownerCell));
                }
            }
            return input;
        }

        /// <summary>
        /// Generates the chunk at <paramref name="coordinate"/>. <paramref name="configure"/> applies further
        /// constraints to the new solver, e.g. the tiles below and above; it runs before the neighbours are painted in.
        /// Restarts with new seeds after contradictions, up to <paramref name="maxAttempts"/> attempts in total.
        /// </summary>
        /// <exception cref="OperationCanceledException">If <paramref name="cancellation"/> is cancelled.</exception>
        public static Result Generate(CompiledRules rules, RulesetData data, int3 chunkSize, int2 coordinate,
            Func<int2, ChunkData> lookup, int band, uint seed, CellHeuristic heuristic, Action<WfcSolver> configure,
            int maxAttempts, CancellationToken cancellation = default)
        {
            int margin = Margin(band);
            ChunkData input = BuildInput(chunkSize, coordinate, lookup, band);
            var solver = new WfcSolver(rules, input.Size, seed) { Heuristic = heuristic };
            configure?.Invoke(solver);
            var result = new Result();
            if (solver.Status == SolverStatus.Contradiction || !ChunkBridge.Prefill(solver, data, input))
            {
                // The fixed cells contradict each other, so new seeds can't help.
                result.Status = SolverStatus.Contradiction;
                result.Attempts = 1;
                return result;
            }

            while (true)
            {
                result.Attempts++;
                while (solver.Status == SolverStatus.Running)
                {
                    cancellation.ThrowIfCancellationRequested();
                    solver.Step();
                }
                if (solver.Status == SolverStatus.Done || result.Attempts >= maxAttempts)
                {
                    break;
                }
                solver.Reset(WfcSolver.NextSeed(solver.Seed));
            }
            result.Status = solver.Status;
            if (result.Status != SolverStatus.Done)
            {
                return result;
            }

            var output = new ChunkData(input.Size);
            ChunkBridge.ReadResult(solver, data, output);
            result.Chunk = new ChunkData(chunkSize);
            for (int cell = 0; cell < output.CellCount; cell++)
            {
                int3 local = output.ToCell(cell) - new int3(margin, 0, margin);
                int tile = output.GetTile(cell);
                Orientation orientation = output.GetOrientation(cell);
                if (WfcGrid.Contains(chunkSize, local))
                {
                    result.Chunk.Set(result.Chunk.ToIndex(local), tile, orientation);
                }
                else if (IsInBand(chunkSize, local, band) && TryLocate(chunkSize, coordinate, local, 0, out int2 owner, out int ownerCell)
                    && lookup(owner) != null)
                {
                    result.BandCells.Add(new BandCell(owner, ownerCell, tile, orientation));
                }
            }
            return result;
        }

        private static int3 ExtendedSize(int3 chunkSize, int margin) => chunkSize + new int3(2 * margin, 0, 2 * margin);

        /// <summary>How far a cell outside the chunk lies along x and z (0 = right next to it), or −1 inside the range.</summary>
        private static int2 Outside(int3 chunkSize, int3 local)
        {
            int Axis(int value, int size) => value < 0 ? -value - 1 : value >= size ? value - size : -1;
            return new int2(Axis(local.x, chunkSize.x), Axis(local.z, chunkSize.z));
        }

        /// <summary>Whether a cell outside the chunk lies in a side neighbour, closer to the chunk than <paramref name="band"/>.</summary>
        private static bool IsInBand(int3 chunkSize, int3 local, int band)
        {
            int2 outside = Outside(chunkSize, local);
            bool side = (outside.x < 0) != (outside.y < 0);
            return side && math.cmax(outside) < band;
        }

        /// <summary>
        /// The chunk and cell index of a cell given relative to the chunk at <paramref name="coordinate"/>. False for
        /// cells of that chunk itself and of the band, which are generated.
        /// </summary>
        private static bool TryLocate(int3 chunkSize, int2 coordinate, int3 local, int band, out int2 owner, out int ownerCell)
        {
            owner = default;
            ownerCell = -1;
            if (WfcGrid.Contains(chunkSize, local) || IsInBand(chunkSize, local, band))
            {
                return false;
            }
            var offset = new int2((int)math.floor(local.x / (float)chunkSize.x), (int)math.floor(local.z / (float)chunkSize.z));
            owner = coordinate + offset;
            int3 inOwner = local - new int3(offset.x * chunkSize.x, 0, offset.y * chunkSize.z);
            ownerCell = WfcGrid.ToIndex(chunkSize, inOwner);
            return true;
        }
    }
}
