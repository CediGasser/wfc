using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Wfc
{
    /// <summary>
    /// Compares generated grids with the samples, per learned connection and per tile, and adjusts connection weights
    /// so the generated shares move towards the sample shares (iterative proportional fitting).
    /// </summary>
    public static class ConnectionStatistics
    {
        /// <summary>Largest factor a weight changes by in one <see cref="UpdateWeights"/> step.</summary>
        public const double MaxStep = 4;
        public const float MinWeight = 1e-3f;
        public const float MaxWeight = 1e3f;

        /// <summary>
        /// Adds, for every pair of neighbouring decided cells, one to the count of the connection they form
        /// (index into <see cref="RulesetData.connections"/>).
        /// </summary>
        public static void CountConnections(WfcSolver solver, long[] counts)
        {
            WfcGrid grid = solver.Grid;
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                int a = solver.GetCollapsedVariant(cell);
                if (a < 0)
                {
                    continue;
                }
                foreach (Direction direction in Directions.Positive)
                {
                    int neighbour = grid.GetNeighbour(cell, direction);
                    int b = neighbour >= 0 ? solver.GetCollapsedVariant(neighbour) : -1;
                    int connection = b >= 0 ? solver.Rules.GetConnection(a, direction, b) : -1;
                    if (connection >= 0)
                    {
                        counts[connection]++;
                    }
                }
            }
        }

        /// <summary>Adds one to the count of every decided cell's tile.</summary>
        public static void CountTiles(WfcSolver solver, long[] counts)
        {
            for (int cell = 0; cell < solver.Grid.CellCount; cell++)
            {
                int variant = solver.GetCollapsedVariant(cell);
                if (variant >= 0)
                {
                    counts[solver.Rules.GetTile(variant)]++;
                }
            }
        }

        /// <summary>Counts as shares of their total, leaving out (and setting to 0) the excluded entries.</summary>
        public static double[] Shares(IReadOnlyList<long> counts, bool[] excluded)
        {
            double total = 0;
            for (int i = 0; i < counts.Count; i++)
            {
                total += excluded[i] ? 0 : counts[i];
            }
            var shares = new double[counts.Count];
            for (int i = 0; i < counts.Count; i++)
            {
                shares[i] = excluded[i] || total <= 0 ? 0 : counts[i] / total;
            }
            return shares;
        }

        /// <summary>Half the summed absolute difference: the share of entries that would have to change to match.</summary>
        public static double TotalVariation(double[] p, double[] q)
        {
            double sum = 0;
            for (int i = 0; i < p.Length; i++)
            {
                sum += Math.Abs(p[i] - q[i]);
            }
            return sum / 2;
        }

        /// <summary>
        /// Moves every weight by (target / output)^alpha, at most by a factor of <see cref="MaxStep"/> either way
        /// (connections that never appeared grow by that factor). Excluded entries and weights of 0 (forbidden
        /// connections) stay as they are. Afterwards the adjusted weights are scaled to a geometric mean of 1, which
        /// doesn't change any choice (a common factor cancels per cell), and clamped to
        /// [<see cref="MinWeight"/>, <see cref="MaxWeight"/>].
        /// </summary>
        public static void UpdateWeights(float[] weights, double[] target, double[] output, double alpha, bool[] excluded)
        {
            double logSum = 0;
            int adjusted = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                if (excluded[i] || weights[i] <= 0)
                {
                    continue;
                }
                double factor = output[i] <= 0 ? MaxStep
                    : target[i] <= 0 ? 1 / MaxStep
                    : math.clamp(Math.Pow(target[i] / output[i], alpha), 1 / MaxStep, MaxStep);
                weights[i] = (float)(weights[i] * factor);
                logSum += Math.Log(weights[i]);
                adjusted++;
            }
            if (adjusted == 0)
            {
                return;
            }
            double scale = Math.Exp(-logSum / adjusted);
            for (int i = 0; i < weights.Length; i++)
            {
                if (!excluded[i] && weights[i] > 0)
                {
                    weights[i] = math.clamp((float)(weights[i] * scale), MinWeight, MaxWeight);
                }
            }
        }
    }
}
