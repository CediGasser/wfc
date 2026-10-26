using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>
    /// Calibrates the connection weights of a rule source so generated grids contain each connection about as often as
    /// the source observed it (so far only samples count observations). Every iteration generates a batch of grids
    /// with the generator's boundaries, compares the connection shares with the observation shares and adjusts the
    /// weights (<see cref="ConnectionStatistics.UpdateWeights"/>). A connection weight enters a collapse once per
    /// decided neighbour, so full steps overshoot: when an iteration is worse than the best one so far, the next step
    /// starts from the best weights again with half the step size. Runs one attempt per editor update, so the editor
    /// stays usable. The best iteration can be applied as weight overrides on the source.
    /// </summary>
    public sealed class WeightCalibrator
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Grid used for calibration. Keep it close to the samples' size: large grids are slow and fail more often.")]
            public Vector3Int size = new Vector3Int(12, 10, 12);
            [Min(1)] public int generationsPerIteration = 40;
            [Min(1)] public int attemptsPerGeneration = 10;
            [Min(1)] public int iterations = 8;
            [Tooltip("Initial step size: 1 jumps straight to the measured ratio, smaller values are steadier. Halved whenever an iteration gets worse.")]
            [Range(0.05f, 1f)] public float alpha = 0.25f;
            public uint seed = 1;
        }

        public sealed class Iteration
        {
            public float[] weights;
            public int generations;
            public int done;
            public int attempts;
            public double connectionDistance = double.NaN;
            public double tileDistance = double.NaN;
            /// <summary>Step size that produced this iteration's weights (0 for the starting weights).</summary>
            public double alpha;
            public double[] connectionShares;
        }

        private readonly WfcGenerator generator;
        private readonly RuleSource source;
        private readonly Settings settings;
        private readonly RuleSourceAnalysis analysis;
        private readonly double[] targetConnections;
        private readonly long[] sampleTiles;
        private readonly bool[] excludedConnections;
        private readonly bool[] excludedTiles;
        private double alpha;

        private float[] weights;
        private CompiledRules rules;
        private WfcSolver solver;
        private long[] connectionCounts;
        private long[] tileCounts;
        private Iteration current;
        private int generationAttempts;

        /// <exception cref="System.ArgumentException">If the source doesn't count observations.</exception>
        public WeightCalibrator(WfcGenerator generator, RuleSource source, Settings settings)
        {
            if (!source.HasObservations)
            {
                throw new ArgumentException($"{source.name} doesn't observe connections, so there's nothing to calibrate towards.", nameof(source));
            }
            this.generator = generator;
            this.source = source;
            this.settings = settings;
            analysis = RulesetBaker.Analyze(source, detectSymmetry: false);
            Connection[] connections = analysis.Data.connections;
            weights = connections.Select(c => c.weight).ToArray();

            // Connections the samples forbid (weight 0) take no part; the rest are compared by observation share.
            excludedConnections = connections.Select(c => c.weight <= 0).ToArray();
            targetConnections = ConnectionStatistics.Shares(connections.Select(c => (long)c.observations).ToArray(), excludedConnections);
            sampleTiles = new long[analysis.Data.TileCount];
            for (int i = 0; i < analysis.result.cellVariant.Length; i++)
            {
                int variant = analysis.result.cellVariant[i];
                if (variant >= 0)
                {
                    sampleTiles[analysis.Data.variantTile[variant]]++;
                }
            }
            excludedTiles = new bool[analysis.Data.TileCount];
            ExcludeImpossibleTiles();
            alpha = settings.alpha;
        }

        public bool IsRunning { get; private set; }

        public List<Iteration> Iterations { get; } = new List<Iteration>();

        /// <summary>The finished iteration with the smallest connection distance, or null.</summary>
        public Iteration Best => Iterations.Where(i => !double.IsNaN(i.connectionDistance)).OrderBy(i => i.connectionDistance).FirstOrDefault();

        /// <summary>Tiles that can't appear inside the calibration grid at all, e.g. a boundary tile that only fits outside.</summary>
        public List<string> ExcludedTileNames { get; } = new List<string>();

        public string Progress => current == null ? (IsRunning ? "Starting…" : "Idle")
            : $"Iteration {Iterations.Count + 1}/{settings.iterations}: generation {current.generations + 1}/{settings.generationsPerIteration}, {current.done} done, {current.attempts} attempts";

        public RuleSource Source => source;

        /// <summary>Raised after every attempt and when the calibration ends.</summary>
        public event Action Changed;

        public void Start()
        {
            if (IsRunning)
            {
                return;
            }
            IsRunning = true;
            BeginIteration();
            EditorApplication.update += Tick;
        }

        public void Cancel()
        {
            Stop();
        }

        /// <summary>Writes the best iteration's weights as overrides on the source and bakes it.</summary>
        public void Apply()
        {
            Iteration best = Best;
            if (best == null)
            {
                return;
            }
            Undo.RecordObject(source, "Apply Calibrated Connection Weights");
            Connection[] connections = analysis.Data.connections;
            for (int c = 0; c < connections.Length; c++)
            {
                if (!excludedConnections[c])
                {
                    source.SetOverride(connections[c].key, best.weights[c]);
                }
            }
            EditorUtility.SetDirty(source);
            RulesetBaker.Bake(source);
        }

        private void BeginIteration()
        {
            // The derived rules with the current weights, as a bake with these weights as overrides would produce them:
            // weights never change between 0 and positive, so the adjacency stays the same.
            Connection[] connections = analysis.Data.connections;
            rules = new CompiledRules(analysis.Data.WithConnectionWeights(weights));
            connectionCounts = new long[connections.Length];
            tileCounts = new long[analysis.Data.TileCount];
            current = new Iteration { weights = (float[])weights.Clone(), alpha = Iterations.Count == 0 ? 0 : alpha };
            solver = null;
        }

        private void Tick()
        {
            try
            {
                Attempt();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Stop();
            }
            // Stop() already reported the end.
            if (IsRunning)
            {
                Changed?.Invoke();
            }
        }

        private void Attempt()
        {
            if (solver == null)
            {
                uint seed = math.max(1u, settings.seed + (uint)Iterations.Count * 100003u + (uint)current.generations * 7919u);
                var size = new int3(math.max(1, settings.size.x), math.max(1, settings.size.y), math.max(1, settings.size.z));
                // Weights don't change the variants, so the analysed data describes them for the boundaries.
                solver = generator.CreateSolver(rules, analysis.Data, analysis.tileSet, size, seed);
                generationAttempts = 0;
            }
            else
            {
                solver.Reset(WfcSolver.NextSeed(solver.Seed));
            }
            generationAttempts++;
            current.attempts++;

            if (solver.Run() == SolverStatus.Done)
            {
                current.done++;
                ConnectionStatistics.CountConnections(solver, connectionCounts);
                ConnectionStatistics.CountTiles(solver, tileCounts);
            }
            else if (generationAttempts < settings.attemptsPerGeneration)
            {
                return;
            }
            solver = null;
            if (++current.generations < settings.generationsPerIteration)
            {
                return;
            }
            FinishIteration();
        }

        private void FinishIteration()
        {
            current.connectionShares = ConnectionStatistics.Shares(connectionCounts, excludedConnections);
            if (current.done > 0)
            {
                current.connectionDistance = ConnectionStatistics.TotalVariation(targetConnections, current.connectionShares);
                current.tileDistance = ConnectionStatistics.TotalVariation(
                    ConnectionStatistics.Shares(sampleTiles, excludedTiles), ConnectionStatistics.Shares(tileCounts, excludedTiles));
            }
            Iterations.Add(current);
            current = null;

            if (Iterations.Count >= settings.iterations)
            {
                Stop();
                return;
            }
            // Step from the best weights so far; if the last step made things worse, take a smaller one.
            Iteration best = Best;
            if (best == null)
            {
                Stop();
                return;
            }
            if (best != Iterations[^1])
            {
                alpha *= 0.5;
            }
            weights = (float[])best.weights.Clone();
            ConnectionStatistics.UpdateWeights(weights, targetConnections, best.connectionShares, alpha, excludedConnections);
            BeginIteration();
        }

        /// <summary>
        /// Tiles that no cell can hold once the boundaries are applied and propagated (e.g. the tile below the floor
        /// that only exists as the boundary). Their connections can't be matched and are left out of the comparison.
        /// </summary>
        private void ExcludeImpossibleTiles()
        {
            var size = new int3(math.max(1, settings.size.x), math.max(1, settings.size.y), math.max(1, settings.size.z));
            var probe = generator.CreateSolver(new CompiledRules(analysis.Data), analysis.Data, analysis.tileSet, size, 1);
            probe.PropagateInitially();
            var possible = new bool[analysis.Data.TileCount];
            for (int cell = 0; cell < probe.Grid.CellCount; cell++)
            {
                for (int v = 0; v < analysis.Data.VariantCount; v++)
                {
                    possible[analysis.Data.variantTile[v]] |= probe.Wave.IsPossible(cell, v);
                }
            }
            Connection[] connections = analysis.Data.connections;
            for (int t = 0; t < possible.Length; t++)
            {
                if (!possible[t] && sampleTiles[t] > 0)
                {
                    excludedTiles[t] = true;
                    ExcludedTileNames.Add(analysis.Data.tileNames[t]);
                }
            }
            for (int c = 0; c < connections.Length; c++)
            {
                excludedConnections[c] |= excludedTiles[connections[c].tileA] || excludedTiles[connections[c].tileB];
            }
            double[] shares = ConnectionStatistics.Shares(connections.Select(c => (long)c.observations).ToArray(), excludedConnections);
            Array.Copy(shares, targetConnections, shares.Length);
        }

        private void Stop()
        {
            IsRunning = false;
            solver = null;
            current = null;
            EditorApplication.update -= Tick;
            Changed?.Invoke();
        }
    }
}
