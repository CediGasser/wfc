using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace Wfc.Authoring
{
    public enum BoundaryMode
    {
        /// <summary>Nothing outside: the edge cells are unconstrained.</summary>
        None,
        /// <summary>A layer of this tile lies just outside the grid; edge cells must fit next to it.</summary>
        Tile,
    }

    [Serializable]
    public struct BoundarySide
    {
        public BoundaryMode mode;
        public TileDefinition tile;

        public BoundarySide(BoundaryMode mode, TileDefinition tile)
        {
            this.mode = mode;
            this.tile = tile;
        }
    }

    /// <summary>
    /// Fills its <see cref="Authoring.Chunk"/> from a baked <see cref="Ruleset"/>, either instantly or animated a few
    /// steps per frame (in Play mode and in the editor). The chunk's painted (authored) cells are kept and everything
    /// else is generated around them. The result is a layer under the chunk's own cells (<see cref="IChunkLayer"/>)
    /// that isn't saved; disabling the component hides it without losing it, and <see cref="KeepResult"/> turns it
    /// into the chunk's cells. Boundaries treat a tile as lying just outside a side.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Chunk))]
    [AddComponentMenu("WFC/WFC Generator")]
    public sealed class WfcGenerator : MonoBehaviour, IChunkLayer
    {
        [Tooltip("Must use the same tile set as the chunk.")]
        [SerializeField] private Ruleset ruleset;
        [Tooltip("Seed of the first attempt. 0 is replaced by 1.")]
        [SerializeField] private uint seed = 1;
        [Tooltip("Pick a new seed every time Generate is pressed.")]
        [SerializeField] private bool newSeedEachGeneration;
        [Tooltip("Which cell is collapsed next. Weight Sum decides cells that can still hold a heavy tile (e.g. air) last.")]
        [SerializeField] private CellHeuristic heuristic;
        [SerializeField, Min(1)] private int stepsPerFrame = 1;
        [Tooltip("Seconds between animation frames; 0 steps every frame.")]
        [SerializeField, Min(0f)] private float stepInterval;
        [Tooltip("Attempts with new seeds after contradictions before giving up.")]
        [SerializeField, Min(1)] private int maxAttempts = 10;
        [SerializeField] private bool generateOnStart;
        [SerializeField] private bool showUndecided = true;

        [Header("Boundaries: a tile assumed just outside each side")]
        [SerializeField] private BoundarySide bottom;
        [SerializeField] private BoundarySide top;
        [SerializeField] private BoundarySide left;
        [SerializeField] private BoundarySide right;
        [SerializeField] private BoundarySide back;
        [SerializeField] private BoundarySide front;

        private Chunk chunk;
        private WfcSolver solver;
        // What the solver decided so far, in chunk cells; not saved.
        private ChunkData result;
        private RulesetData data;
        private double nextStepTime;
        // The painted cells contradict each other or the boundaries, so new seeds can't help.
        private bool paintContradicts;
        private readonly List<int> invalidPaintedCells = new List<int>();

        public Chunk Chunk => chunk != null ? chunk : chunk = GetComponent<Chunk>();

        public Ruleset Ruleset
        {
            get => ruleset;
            set => ruleset = value;
        }

        public uint Seed
        {
            get => seed;
            set => seed = value;
        }

        public CellHeuristic Heuristic
        {
            get => heuristic;
            set => heuristic = value;
        }

        public int MaxAttempts
        {
            get => maxAttempts;
            set => maxAttempts = Mathf.Max(1, value);
        }

        /// <summary>The solver of the current generation, or null before the first one.</summary>
        public WfcSolver Solver => solver;

        public bool IsAnimating { get; private set; }

        public int Attempts { get; private set; }

        public int Steps { get; private set; }

        /// <summary>Why the last generation couldn't start or what it had to ignore, or null.</summary>
        public string LastError { get; private set; }

        /// <summary>Painted cells the ruleset has no variant for (unknown tile or disallowed orientation); they were generated over.</summary>
        public IReadOnlyList<int> InvalidPaintedCells => invalidPaintedCells;

        public int DecidedCount => solver?.Wave.DecidedCount ?? 0;

        /// <summary>The generated tiles, or null before the first generation and after <see cref="Clear"/>.</summary>
        public ChunkData Result => result;

        public int LayerOrder => 100;

        public bool IsLayerVisible => isActiveAndEnabled && result != null && result.Size.Equals(Chunk.Size);

        public int GetTile(int cell, out Orientation orientation)
        {
            orientation = result.GetOrientation(cell);
            return result.GetTile(cell);
        }

        /// <summary>
        /// The cell where the generation failed: the one left without any possible tile after the last attempt.
        /// Only available while the solver is in <see cref="SolverStatus.Contradiction"/>.
        /// </summary>
        public bool TryGetContradiction(out int3 cell)
        {
            cell = default;
            if (solver == null || solver.Status != SolverStatus.Contradiction || solver.ContradictionCell < 0)
            {
                return false;
            }
            cell = solver.Grid.ToCell(solver.ContradictionCell);
            return true;
        }

        public BoundarySide GetBoundary(Direction side)
        {
            return side switch
            {
                Direction.NegY => bottom,
                Direction.PosY => top,
                Direction.NegX => left,
                Direction.PosX => right,
                Direction.NegZ => back,
                _ => front,
            };
        }

        public void SetBoundary(Direction side, BoundarySide boundary)
        {
            switch (side)
            {
                case Direction.NegY: bottom = boundary; break;
                case Direction.PosY: top = boundary; break;
                case Direction.NegX: left = boundary; break;
                case Direction.PosX: right = boundary; break;
                case Direction.NegZ: back = boundary; break;
                default: front = boundary; break;
            }
        }

        /// <summary>Starts a new generation and either runs it to the end or animates it from <see cref="Update"/>.</summary>
        public void Generate(bool animate)
        {
            if (!Prepare())
            {
                return;
            }
            if (animate)
            {
                IsAnimating = solver.Status == SolverStatus.Running;
                nextStepTime = 0;
                RequestUpdate();
            }
            else
            {
                RunToEnd();
            }
        }

        /// <summary>Advances one step, starting a new generation first if none is running.</summary>
        public void StepOnce()
        {
            IsAnimating = false;
            if (solver == null || solver.Status != SolverStatus.Running)
            {
                if (!Prepare())
                {
                    return;
                }
            }
            Advance(write: true);
        }

        /// <summary>Finishes the current generation immediately (including restarts).</summary>
        public void RunToEnd()
        {
            IsAnimating = false;
            if (solver == null && !Prepare())
            {
                return;
            }
            while (solver.Status == SolverStatus.Running)
            {
                Advance(write: false);
            }
            WriteAll();
        }

        public void Stop() => IsAnimating = false;

        /// <summary>Removes the generated tiles and forgets the current generation.</summary>
        public void Clear()
        {
            IsAnimating = false;
            solver = null;
            result = null;
            ChunkLayers.Notify(this, Chunk.AllCells);
        }

        /// <summary>
        /// Turns the generated tiles into cells of the chunk, so they're saved and can be edited, painted over or used
        /// as a sample, and forgets the generation. Returns the number of cells filled.
        /// </summary>
        public int KeepResult()
        {
            ChunkData output = GetOutput();
            int kept = output.CountNonEmpty() - Chunk.Cells.CountNonEmpty();
            Chunk.SetCells(output);
            Clear();
            return kept;
        }

        /// <summary>A copy of what the chunk shows: its own cells, with the generated tiles in the empty ones.</summary>
        public ChunkData GetOutput()
        {
            ChunkData output = Chunk.Cells.Clone();
            if (result == null || !result.Size.Equals(output.Size))
            {
                return output;
            }
            for (int cell = 0; cell < output.CellCount; cell++)
            {
                if (output.IsEmpty(cell) && !result.IsEmpty(cell))
                {
                    output.Set(cell, result.GetTile(cell), result.GetOrientation(cell));
                }
            }
            return output;
        }

        private bool Prepare()
        {
            LastError = null;
            invalidPaintedCells.Clear();
            if (ruleset == null || ruleset.Data == null || ruleset.Data.VariantCount == 0)
            {
                LastError = "Assign a baked ruleset first.";
                return false;
            }
            if (ruleset.TileSet == null)
            {
                LastError = $"{ruleset.name} has no tile set. Bake it again.";
                return false;
            }
            if (Chunk.TileSet != ruleset.TileSet)
            {
                string chunkSet = Chunk.TileSet != null ? Chunk.TileSet.name : "no tile set";
                LastError = $"The chunk uses {chunkSet}, but {ruleset.name} uses {ruleset.TileSet.name}. Tile indices only match within one tile set.";
                return false;
            }
            if (newSeedEachGeneration)
            {
                seed = WfcSolver.NextSeed(seed ^ (uint)Environment.TickCount);
            }
            if (seed == 0)
            {
                seed = 1;
            }

            data = ruleset.Data;
            solver = CreateSolver(new CompiledRules(data), data, ruleset.TileSet, Chunk.Size, seed);
            paintContradicts = solver.Status == SolverStatus.Contradiction;
            if (!paintContradicts)
            {
                paintContradicts = !ChunkBridge.Prefill(solver, data, Chunk.Cells, invalidPaintedCells);
                if (paintContradicts)
                {
                    int3 cell = solver.Grid.ToCell(solver.ContradictionCell);
                    LastError = $"The painted cells contradict each other or the boundaries: no tile fits cell {cell}.";
                }
            }
            else
            {
                LastError = "The boundaries contradict the rules, so nothing fits.";
            }
            if (invalidPaintedCells.Count > 0)
            {
                LastError ??= $"{invalidPaintedCells.Count} painted cell(s) use a tile or orientation the ruleset doesn't have. They are generated over.";
            }
            Attempts = 1;
            Steps = 0;
            WriteAll();
            return true;
        }

        /// <summary>
        /// A solver for this generator's boundaries on any rules, e.g. rules derived from samples that aren't baked
        /// yet. <paramref name="tileSet"/> is the tile set <paramref name="data"/>'s tile indices refer to. Painted
        /// cells aren't applied, since the grid may have another size than the chunk.
        /// </summary>
        public WfcSolver CreateSolver(CompiledRules rules, RulesetData data, TileSet tileSet, int3 gridSize, uint solverSeed)
        {
            var created = new WfcSolver(rules, gridSize, solverSeed) { Heuristic = heuristic };
            for (int d = 0; d < Directions.Count; d++)
            {
                var side = (Direction)d;
                BoundarySide boundary = GetBoundary(side);
                if (boundary.mode != BoundaryMode.Tile || boundary.tile == null)
                {
                    continue;
                }
                int tile = tileSet != null ? tileSet.IndexOf(boundary.tile) : -1;
                List<int> outside = data.GetVariants(tile).ToList();
                if (outside.Count == 0)
                {
                    Debug.LogWarning($"{name}: boundary tile {boundary.tile.name} isn't part of the ruleset, so the {side.ToShortString()} side is left unconstrained.", this);
                    continue;
                }
                created.ConstrainSide(side, outside);
            }
            return created;
        }

        private void Advance(bool write)
        {
            SolverStatus status = solver.Step();
            Steps++;
            if (write)
            {
                WriteCells(solver.ChangedCells);
            }
            if (status != SolverStatus.Contradiction)
            {
                if (status == SolverStatus.Done)
                {
                    IsAnimating = false;
                }
                return;
            }

            if (paintContradicts || Attempts >= maxAttempts)
            {
                IsAnimating = false;
                return;
            }
            Attempts++;
            solver.Reset(WfcSolver.NextSeed(solver.Seed));
            if (write)
            {
                WriteAll();
            }
        }

        /// <summary>Copies the solver's state of the given cells into the result.</summary>
        private void WriteCells(IReadOnlyList<int> cells)
        {
            ChunkBridge.ReadResult(solver, data, GetOrCreateResult(), cells);
            foreach (int cell in cells)
            {
                ChunkLayers.Notify(this, cell);
            }
        }

        private void WriteAll()
        {
            ChunkBridge.ReadResult(solver, data, GetOrCreateResult());
            ChunkLayers.Notify(this, Chunk.AllCells);
        }

        private ChunkData GetOrCreateResult()
        {
            if (result == null || !result.Size.Equals(solver.Grid.Size))
            {
                result = new ChunkData(solver.Grid.Size);
            }
            return result;
        }

        private void Update()
        {
            if (!IsAnimating || solver == null)
            {
                return;
            }
            double now = Time.realtimeSinceStartupAsDouble;
            if (now >= nextStepTime)
            {
                for (int i = 0; i < stepsPerFrame && IsAnimating; i++)
                {
                    Advance(write: true);
                }
                nextStepTime = now + stepInterval;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    UnityEditor.SceneView.RepaintAll();
                }
#endif
            }
            if (IsAnimating)
            {
                RequestUpdate();
            }
        }

        private void Start()
        {
            if (Application.isPlaying && generateOnStart)
            {
                Generate(animate: true);
            }
        }

        private void OnEnable()
        {
            // Show a result kept while the component was disabled, and continue an interrupted animation.
            ChunkLayers.Notify(this, Chunk.AllCells);
            if (IsAnimating)
            {
                RequestUpdate();
            }
        }

        private void OnDisable()
        {
            // Hides the result (IsLayerVisible is false now); it comes back when the component is enabled again.
            ChunkLayers.Notify(this, Chunk.AllCells);
        }

        private static void RequestUpdate()
        {
#if UNITY_EDITOR
            // Outside Play mode, Update only runs when something changes; ask for the next one explicitly.
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            }
#endif
        }

        private void OnDrawGizmos()
        {
            if (solver == null || !solver.Grid.Size.Equals(Chunk.Size))
            {
                return;
            }
            Gizmos.matrix = transform.localToWorldMatrix;
            if (TryGetContradiction(out int3 failed))
            {
                DrawContradiction(failed.ToVector3());
            }
            if (!showUndecided || solver.Status != SolverStatus.Running)
            {
                return;
            }
            int variants = Mathf.Max(1, solver.Rules.VariantCount);
            for (int cell = 0; cell < solver.Grid.CellCount; cell++)
            {
                int count = solver.Wave.GetCount(cell);
                if (count > 1)
                {
                    // The more options are left, the bigger the marker.
                    Gizmos.color = new Color(0.6f, 0.9f, 1f, 0.5f);
                    Gizmos.DrawWireCube(solver.Grid.ToCell(cell).ToVector3(), Vector3.one * (0.1f + 0.6f * count / variants));
                }
            }
        }

        private void DrawContradiction(Vector3 center)
        {
            Gizmos.color = new Color(1f, 0.15f, 0.15f, 0.45f);
            Gizmos.DrawCube(center, Vector3.one);
            Gizmos.color = new Color(1f, 0.15f, 0.15f, 1f);
            Gizmos.DrawWireCube(center, Vector3.one * 1.04f);
            // Lines along the grid axes, so the cell stands out even when it's surrounded by tiles.
            Vector3 extent = Chunk.Size.ToVector3();
            Gizmos.color = new Color(1f, 0.15f, 0.15f, 0.6f);
            Gizmos.DrawLine(new Vector3(-0.5f, center.y, center.z), new Vector3(extent.x - 0.5f, center.y, center.z));
            Gizmos.DrawLine(new Vector3(center.x, -0.5f, center.z), new Vector3(center.x, extent.y - 0.5f, center.z));
            Gizmos.DrawLine(new Vector3(center.x, center.y, -0.5f), new Vector3(center.x, center.y, extent.z - 0.5f));
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.TransformPoint(center + Vector3.up * 0.7f), "Contradiction: no tile fits here");
#endif
        }
    }
}
