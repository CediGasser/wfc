using System;
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
    /// Generates a grid of tiles from a baked <see cref="Ruleset"/>, either instantly or animated a few steps per frame
    /// (in Play mode and in the editor). Its transform is the grid: cell (x, y, z) sits at local position (x, y, z).
    /// Generated tiles live under a child that isn't saved with the scene.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("WFC/WFC Generator")]
    public sealed class WfcGenerator : MonoBehaviour
    {
        private const string OutputName = "Generated (not saved)";

        [SerializeField] private Ruleset ruleset;
        [SerializeField] private Vector3Int size = new Vector3Int(8, 4, 8);
        [Tooltip("Seed of the first attempt. 0 is replaced by 1.")]
        [SerializeField] private uint seed = 1;
        [Tooltip("Pick a new seed every time Generate is pressed.")]
        [SerializeField] private bool newSeedEachGeneration;
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

        private WfcSolver solver;
        private RulesetData data;
        private GameObject[] generated;
        private double nextStepTime;

        public Ruleset Ruleset
        {
            get => ruleset;
            set => ruleset = value;
        }

        public Vector3Int Size
        {
            get => size;
            set => size = value;
        }

        public uint Seed
        {
            get => seed;
            set => seed = value;
        }

        /// <summary>The solver of the current generation, or null before the first one.</summary>
        public WfcSolver Solver => solver;

        public bool IsAnimating { get; private set; }

        public int Attempts { get; private set; }

        public int Steps { get; private set; }

        /// <summary>Why the last generation couldn't start, or null.</summary>
        public string LastError { get; private set; }

        public Transform Output => transform.Find(OutputName);

        public int DecidedCount
        {
            get
            {
                if (solver == null)
                {
                    return 0;
                }
                int decided = 0;
                for (int cell = 0; cell < solver.Grid.CellCount; cell++)
                {
                    decided += solver.Wave.GetCount(cell) == 1 ? 1 : 0;
                }
                return decided;
            }
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
            Advance(spawn: true);
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
                Advance(spawn: false);
            }
            SpawnAll();
        }

        public void Stop() => IsAnimating = false;

        /// <summary>Removes the generated tiles and forgets the current generation.</summary>
        public void Clear()
        {
            IsAnimating = false;
            solver = null;
            generated = null;
            DestroyOutput();
        }

        private bool Prepare()
        {
            LastError = null;
            if (ruleset == null || ruleset.Data == null || ruleset.Data.VariantCount == 0)
            {
                LastError = "Assign a baked ruleset first.";
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
            var gridSize = new int3(Mathf.Max(1, size.x), Mathf.Max(1, size.y), Mathf.Max(1, size.z));
            solver = new WfcSolver(new CompiledRules(data), gridSize, seed);
            ApplyBoundaries();
            Attempts = 1;
            Steps = 0;
            DestroyOutput();
            generated = new GameObject[solver.Grid.CellCount];
            SpawnAll();
            return true;
        }

        private void ApplyBoundaries()
        {
            for (int d = 0; d < Directions.Count; d++)
            {
                var side = (Direction)d;
                BoundarySide boundary = GetBoundary(side);
                if (boundary.mode != BoundaryMode.Tile || boundary.tile == null)
                {
                    continue;
                }
                int tile = IndexOfTile(boundary.tile);
                if (tile < 0)
                {
                    Debug.LogWarning($"{name}: boundary tile {boundary.tile.name} isn't part of the ruleset, so the {side.ToShortString()} side is left unconstrained.", this);
                    continue;
                }
                var outside = new System.Collections.Generic.List<int>();
                for (int v = 0; v < data.VariantCount; v++)
                {
                    if (data.variantTile[v] == tile)
                    {
                        outside.Add(v);
                    }
                }
                solver.ConstrainSide(side, outside);
            }
        }

        private int IndexOfTile(TileDefinition definition)
        {
            for (int i = 0; i < ruleset.Tiles.Count; i++)
            {
                if (ruleset.Tiles[i] == definition)
                {
                    return i;
                }
            }
            return -1;
        }

        private void Advance(bool spawn)
        {
            SolverStatus status = solver.Step();
            Steps++;
            if (spawn)
            {
                foreach (int cell in solver.ChangedCells)
                {
                    Spawn(cell);
                }
            }
            if (status != SolverStatus.Contradiction)
            {
                if (status == SolverStatus.Done)
                {
                    IsAnimating = false;
                }
                return;
            }

            if (Attempts >= maxAttempts)
            {
                IsAnimating = false;
                return;
            }
            Attempts++;
            solver.Reset(WfcSolver.NextSeed(solver.Seed));
            DestroyOutput();
            generated = new GameObject[solver.Grid.CellCount];
            if (spawn)
            {
                SpawnAll();
            }
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
                    Advance(spawn: true);
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

        private void OnDisable()
        {
            IsAnimating = false;
            DestroyOutput();
            generated = null;
            solver = null;
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

        private void SpawnAll()
        {
            for (int cell = 0; cell < solver.Grid.CellCount; cell++)
            {
                Spawn(cell);
            }
        }

        private void Spawn(int cell)
        {
            if (generated == null || generated[cell] != null)
            {
                return;
            }
            int variant = solver.GetCollapsedVariant(cell);
            if (variant < 0)
            {
                return;
            }
            TileDefinition definition = ruleset.Tiles[data.variantTile[variant]];
            if (definition == null || definition.Model == null)
            {
                return;
            }

            int3 position = solver.Grid.ToCell(cell);
            var wrapper = new GameObject($"{definition.name} {position}") { hideFlags = HideFlags.DontSave };
            wrapper.transform.SetParent(GetOrCreateOutput(), false);
            data.GetVariantOrientation(variant).ToRotationScale(out Quaternion rotation, out Vector3 scale);
            wrapper.transform.localPosition = position.ToVector3();
            wrapper.transform.localRotation = rotation;
            wrapper.transform.localScale = scale;

            GameObject model = Instantiate(definition.Model, wrapper.transform, false);
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                part.gameObject.hideFlags = HideFlags.DontSave;
            }
            generated[cell] = wrapper;
        }

        private Transform GetOrCreateOutput()
        {
            Transform output = Output;
            if (output == null)
            {
                output = new GameObject(OutputName) { hideFlags = HideFlags.DontSave }.transform;
                output.SetParent(transform, false);
            }
            return output;
        }

        private void DestroyOutput()
        {
            Transform output = Output;
            if (output == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Destroy(output.gameObject);
                // Destroy is deferred; rename so a new output can be created in the same frame.
                output.name = OutputName + " (destroyed)";
                output.SetParent(null);
            }
            else
            {
                DestroyImmediate(output.gameObject);
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Vector3 extent = new Vector3(Mathf.Max(1, size.x), Mathf.Max(1, size.y), Mathf.Max(1, size.z));
            Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
            Gizmos.DrawWireCube((extent - Vector3.one) * 0.5f, extent);

            if (solver == null)
            {
                return;
            }
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
            Vector3 extent = new Vector3(Mathf.Max(1, size.x), Mathf.Max(1, size.y), Mathf.Max(1, size.z));
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
