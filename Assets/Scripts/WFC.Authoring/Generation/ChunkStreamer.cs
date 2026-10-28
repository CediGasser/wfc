using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Wfc.Authoring
{
    /// <summary>
    /// Generates an endless horizontal grid of chunks around a target (the player) in Play mode. Every chunk is
    /// generated on a worker thread so that it fits the chunks generated before it (<see cref="ChunkSeams"/>): their
    /// cells along the seam are fixed. If that fails, a band of the neighbours' cells along the seam is generated again
    /// together with the chunk, deeper with each of <see cref="bandDepths"/>. Neighbouring chunks (diagonals included)
    /// are never generated at the same time, since each would miss the other's seam. Generated chunks are kept when
    /// they go out of range, so returning shows the same land; only their GameObjects (<see cref="Chunk"/> and
    /// <see cref="ChunkView"/>, children of this object) are removed. Chunk (cx, cz) starts at local position
    /// (cx · size.x, 0, cz · size.z).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("WFC/Chunk Streamer")]
    public sealed class ChunkStreamer : MonoBehaviour
    {
        /// <summary>How one chunk was generated.</summary>
        public readonly struct ChunkStats
        {
            public ChunkStats(int band, int attempts, long milliseconds)
            {
                Band = band;
                Attempts = attempts;
                Milliseconds = milliseconds;
            }

            /// <summary>Depth of the neighbours' band generated again; 0 if the seams could be continued as they were.</summary>
            public int Band { get; }
            /// <summary>Solver attempts of the successful run.</summary>
            public int Attempts { get; }
            /// <summary>Time of all runs for this chunk, including failed band depths.</summary>
            public long Milliseconds { get; }
        }

        private sealed class Job
        {
            public Task<ChunkSeams.Result> task;
            public CancellationTokenSource cancellation;
            public int bandIndex = -1;
            public Stopwatch watch;
        }

        [Tooltip("Rules for every chunk; its tile set is the chunks' tile set.")]
        [SerializeField] private Ruleset ruleset;
        [Tooltip("Chunks are generated around this transform, e.g. the player or the camera.")]
        [SerializeField] private Transform target;
        [SerializeField] private int3 chunkSize = new int3(24, 6, 24);
        [Tooltip("Chunks up to this many chunks away from the target's chunk (along x and z) are generated and shown.")]
        [SerializeField, Min(0)] private int radius = 2;
        [Tooltip("Seed of the world; each chunk derives its own from it and its coordinate.")]
        [SerializeField] private uint worldSeed = 1;
        [SerializeField] private CellHeuristic heuristic = CellHeuristic.WeightSum;
        [Tooltip("Attempts with new seeds per band depth before freeing a deeper band of the neighbours.")]
        [SerializeField, Min(1)] private int maxAttempts = 10;
        [Tooltip("Depths of the neighbours' band generated again when a chunk can't continue their seams, tried in order.")]
        [SerializeField] private int[] bandDepths = { 2, 4 };
        [SerializeField, Min(1)] private int maxConcurrentJobs = 2;

        [Header("Boundaries: a tile assumed below and above every chunk")]
        [SerializeField] private BoundarySide bottom;
        [SerializeField] private BoundarySide top;

        private readonly Dictionary<int2, ChunkData> generated = new Dictionary<int2, ChunkData>();
        private readonly Dictionary<int2, Chunk> shown = new Dictionary<int2, Chunk>();
        private readonly Dictionary<int2, Job> running = new Dictionary<int2, Job>();
        private readonly Dictionary<int2, ChunkStats> stats = new Dictionary<int2, ChunkStats>();
        private readonly HashSet<int2> failed = new HashSet<int2>();
        private CompiledRules rules;
        private List<int> bottomVariants;
        private List<int> topVariants;

        public Ruleset Ruleset
        {
            get => ruleset;
            set => ruleset = value;
        }

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        public int3 ChunkSize => chunkSize;

        /// <summary>Every generated chunk by coordinate, shown or not.</summary>
        public IReadOnlyDictionary<int2, ChunkData> Generated => generated;

        public IReadOnlyDictionary<int2, ChunkStats> Stats => stats;

        /// <summary>Chunks that couldn't be generated even with the deepest band; they stay empty.</summary>
        public IReadOnlyCollection<int2> Failed => failed;

        public int RunningCount => running.Count;

        /// <summary>The coordinate of the chunk containing a position in world space.</summary>
        public int2 ChunkAt(Vector3 position)
        {
            Vector3 local = transform.InverseTransformPoint(position);
            return new int2((int)math.floor((local.x + 0.5f) / chunkSize.x), (int)math.floor((local.z + 0.5f) / chunkSize.z));
        }

        private void Start()
        {
            if (ruleset == null || ruleset.Data == null || ruleset.Data.VariantCount == 0 || ruleset.TileSet == null)
            {
                Debug.LogError($"{name}: assign a baked ruleset.", this);
                enabled = false;
                return;
            }
            rules = new CompiledRules(ruleset.Data);
            bottomVariants = GetOutside(bottom, Direction.NegY);
            topVariants = GetOutside(top, Direction.PosY);
        }

        private List<int> GetOutside(BoundarySide boundary, Direction side)
        {
            List<int> outside = boundary.GetOutsideVariants(ruleset.Data, ruleset.TileSet);
            if (outside != null && outside.Count == 0)
            {
                Debug.LogWarning($"{name}: boundary tile {boundary.tile.name} isn't part of the ruleset, so the {side.ToShortString()} side is left unconstrained.", this);
                return null;
            }
            return outside;
        }

        private void Update()
        {
            if (rules == null)
            {
                return;
            }
            CollectFinishedJobs();
            int2 centre = target != null ? ChunkAt(target.position) : int2.zero;
            foreach (int2 coordinate in WantedChunks(centre))
            {
                if (generated.ContainsKey(coordinate))
                {
                    Show(coordinate);
                }
                else if (running.Count < maxConcurrentJobs && CanStart(coordinate))
                {
                    StartJob(coordinate, new Job { watch = Stopwatch.StartNew() });
                }
            }
            foreach (int2 coordinate in shown.Keys.Where(c => Distance(c, centre) > radius + 1).ToList())
            {
                Destroy(shown[coordinate].gameObject);
                shown.Remove(coordinate);
            }
        }

        private void OnDisable()
        {
            foreach (Job job in running.Values)
            {
                job.cancellation.Cancel();
            }
            running.Clear();
            foreach (Chunk chunk in shown.Values)
            {
                if (chunk != null)
                {
                    Destroy(chunk.gameObject);
                }
            }
            shown.Clear();
        }

        /// <summary>Chunks within <see cref="radius"/> of the centre, nearest first.</summary>
        private IEnumerable<int2> WantedChunks(int2 centre)
        {
            var wanted = new List<int2>();
            for (int x = -radius; x <= radius; x++)
            {
                for (int z = -radius; z <= radius; z++)
                {
                    wanted.Add(centre + new int2(x, z));
                }
            }
            return wanted.OrderBy(c => math.lengthsq(c - centre));
        }

        private static int Distance(int2 a, int2 b) => math.cmax(math.abs(a - b));

        private bool CanStart(int2 coordinate)
        {
            return !running.ContainsKey(coordinate) && !failed.Contains(coordinate) && running.Keys.All(c => Distance(c, coordinate) > 1);
        }

        /// <summary>Starts the job with its next band depth: 0 first, then <see cref="bandDepths"/>.</summary>
        private void StartJob(int2 coordinate, Job job)
        {
            job.bandIndex++;
            int band = job.bandIndex == 0 ? 0 : bandDepths[job.bandIndex - 1];
            // The worker reads copies, so applying other results in the meantime can't disturb it.
            var neighbours = new Dictionary<int2, ChunkData>();
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    int2 other = coordinate + new int2(x, z);
                    if (generated.TryGetValue(other, out ChunkData data))
                    {
                        neighbours[other] = data.Clone();
                    }
                }
            }

            RulesetData rulesetData = ruleset.Data;
            CompiledRules compiled = rules;
            int3 size = chunkSize;
            uint seed = ChunkSeed(coordinate, band);
            CellHeuristic cellHeuristic = heuristic;
            int attempts = maxAttempts;
            List<int> below = bottomVariants, above = topVariants;
            job.cancellation = new CancellationTokenSource();
            CancellationToken token = job.cancellation.Token;
            job.task = Task.Run(() => ChunkSeams.Generate(compiled, rulesetData, size, coordinate,
                c => neighbours.TryGetValue(c, out ChunkData data) ? data : null, band, seed, cellHeuristic,
                solver =>
                {
                    if (below != null)
                    {
                        solver.ConstrainSide(Direction.NegY, below);
                    }
                    if (above != null)
                    {
                        solver.ConstrainSide(Direction.PosY, above);
                    }
                },
                attempts, token), token);
            running[coordinate] = job;
        }

        private uint ChunkSeed(int2 coordinate, int band)
        {
            uint mixed = worldSeed * 73856093u ^ (uint)coordinate.x * 19349663u ^ (uint)coordinate.y * 83492791u ^ (uint)band * 2654435761u;
            return WfcSolver.NextSeed(mixed);
        }

        private void CollectFinishedJobs()
        {
            foreach (var (coordinate, job) in running.Where(j => j.Value.task.IsCompleted).ToList())
            {
                running.Remove(coordinate);
                if (job.task.IsCanceled)
                {
                    continue;
                }
                if (job.task.IsFaulted)
                {
                    Debug.LogException(job.task.Exception, this);
                    failed.Add(coordinate);
                    continue;
                }

                ChunkSeams.Result result = job.task.Result;
                int band = job.bandIndex == 0 ? 0 : bandDepths[job.bandIndex - 1];
                if (result.Status == SolverStatus.Done)
                {
                    Apply(coordinate, result);
                    stats[coordinate] = new ChunkStats(band, result.Attempts, job.watch.ElapsedMilliseconds);
                }
                else if (job.bandIndex < bandDepths.Length)
                {
                    // Nothing next to it started meanwhile, since this chunk still counted as running.
                    StartJob(coordinate, job);
                }
                else
                {
                    Debug.LogWarning($"{name}: chunk {coordinate} doesn't fit its neighbours, even with a band of {band} cells generated again. It stays empty.", this);
                    failed.Add(coordinate);
                }
            }
        }

        private void Apply(int2 coordinate, ChunkSeams.Result result)
        {
            generated[coordinate] = result.Chunk;
            foreach (ChunkSeams.BandCell cell in result.BandCells)
            {
                generated[cell.Chunk].Set(cell.Cell, cell.Tile, cell.Orientation);
                if (shown.TryGetValue(cell.Chunk, out Chunk chunk))
                {
                    chunk.SetCell(cell.Cell, cell.Tile, cell.Orientation);
                }
            }
        }

        private void Show(int2 coordinate)
        {
            if (shown.ContainsKey(coordinate))
            {
                return;
            }
            var chunkObject = new GameObject($"Chunk {coordinate.x}, {coordinate.y}");
            chunkObject.transform.SetParent(transform, false);
            chunkObject.transform.localPosition = new Vector3(coordinate.x * chunkSize.x, 0f, coordinate.y * chunkSize.z);
            var chunk = chunkObject.AddComponent<Chunk>();
            chunk.TileSet = ruleset.TileSet;
            chunk.SetCells(generated[coordinate]);
            chunkObject.AddComponent<ChunkView>();
            shown[coordinate] = chunk;
        }
    }
}
