using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>
    /// State shared between the chunk tool, its overlay, the shortcuts, the tile palette and the inspectors: the
    /// selected cells and face, the palette tile and the brush orientation. Kept in a ScriptableSingleton, so it
    /// survives script reloads.
    /// </summary>
    public sealed class ChunkToolState : ScriptableSingleton<ChunkToolState>
    {
        public readonly struct Candidate
        {
            public Candidate(int variant, int tile, TileDefinition definition, Orientation orientation, float weight)
            {
                Variant = variant;
                Tile = tile;
                Definition = definition;
                Orientation = orientation;
                Weight = weight;
            }

            public int Variant { get; }
            public int Tile { get; }
            public TileDefinition Definition { get; }
            public Orientation Orientation { get; }
            /// <summary>The connection weight towards the selected face.</summary>
            public float Weight { get; }
        }

        [SerializeField] private TileDefinition paletteTile;
        [SerializeField] private byte brushOrientation;
        [SerializeField] private Chunk chunk;
        [SerializeField] private List<int> selectedCells = new List<int>();
        [SerializeField] private int faceCell = -1;
        [SerializeField] private Direction face;
        [SerializeField] private int ghostIndex;
        [SerializeField] private bool fitAllNeighbours;

        public static event Action Changed;

        /// <summary>The tile placed by Shift+Click; chosen in the tile palette.</summary>
        public static TileDefinition PaletteTile
        {
            get => instance.paletteTile;
            set
            {
                instance.paletteTile = value;
                NotifyChanged();
            }
        }

        /// <summary>The orientation the palette tile is placed in; R, T and M turn it while no cell is selected.</summary>
        public static Orientation BrushOrientation
        {
            get => Orientation.FromIndex(instance.brushOrientation);
            set
            {
                instance.brushOrientation = value.Index;
                NotifyChanged();
            }
        }

        /// <summary>The chunk the selection belongs to.</summary>
        public static Chunk Chunk => instance.chunk;

        public static IReadOnlyList<int> SelectedCells => instance.selectedCells;

        /// <summary>Show only candidates that fit every filled neighbour of the target cell, not just the selected face.</summary>
        public static bool FitAllNeighbours
        {
            get => instance.fitAllNeighbours;
            set
            {
                instance.fitAllNeighbours = value;
                NotifyChanged();
            }
        }

        public static int GhostIndex
        {
            get => instance.ghostIndex;
            set
            {
                instance.ghostIndex = value;
                NotifyChanged();
            }
        }

        /// <summary>The selected face: a cell of <see cref="Chunk"/> and a direction in the chunk's grid.</summary>
        public static bool TryGetFace(out int cell, out Direction direction)
        {
            cell = instance.faceCell;
            direction = instance.face;
            return instance.chunk != null && cell >= 0 && cell < instance.chunk.CellCount;
        }

        /// <summary>Selects cells, optionally with a face of one of them whose neighbour can be filled.</summary>
        public static void Select(Chunk chunk, IEnumerable<int> cells, int faceCell = -1, Direction face = default)
        {
            ChunkToolState state = instance;
            state.chunk = chunk;
            state.selectedCells.Clear();
            if (cells != null)
            {
                state.selectedCells.AddRange(cells.Distinct());
            }
            state.faceCell = faceCell;
            state.face = face;
            state.ghostIndex = 0;
            NotifyChanged();
        }

        /// <summary>Adds a cell to the selection or removes it, keeping the rest.</summary>
        public static void Toggle(Chunk chunk, int cell)
        {
            if (instance.chunk != chunk)
            {
                Select(chunk, new[] { cell });
                return;
            }
            if (!instance.selectedCells.Remove(cell))
            {
                instance.selectedCells.Add(cell);
            }
            instance.faceCell = -1;
            NotifyChanged();
        }

        public static void ClearSelection() => Select(null, null);

        public static void NotifyChanged()
        {
            Changed?.Invoke();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// The rules that apply to a chunk: its sample rule source's current analysis, or its generator's baked ruleset.
        /// Null if there are none or their tile set differs from the chunk's.
        /// </summary>
        public static RulesetData GetRules(Chunk chunk)
        {
            if (chunk == null)
            {
                return null;
            }
            if (chunk.TryGetComponent(out SampleRuleSource source))
            {
                RuleSourceAnalysis analysis = LiveRuleAnalysis.Get(source);
                return analysis != null && analysis.tileSet == chunk.TileSet ? analysis.Data : null;
            }
            if (chunk.TryGetComponent(out WfcGenerator generator) && generator.Ruleset != null && generator.Ruleset.TileSet == chunk.TileSet)
            {
                return generator.Ruleset.Data;
            }
            return null;
        }

        /// <summary>
        /// What the rules allow in the cell next to <paramref name="cell"/> in <paramref name="direction"/>, in variant
        /// order. With <paramref name="fitAll"/>, only variants that also fit every other filled neighbour of that cell.
        /// </summary>
        public static List<Candidate> GetCandidates(Chunk chunk, RulesetData rules, int cell, Direction direction, bool fitAll)
        {
            var candidates = new List<Candidate>();
            ChunkData cells = chunk.Cells;
            var target = cells.ToCell(cell) + direction.ToVector();
            int variant = rules != null ? ShownVariant(chunk, rules, cell) : -1;
            if (variant < 0 || !cells.Contains(target))
            {
                return candidates;
            }

            HashSet<int> fitting = null;
            if (fitAll)
            {
                for (int d = 0; d < Directions.Count; d++)
                {
                    var next = target + ((Direction)d).ToVector();
                    if (!cells.Contains(next) || cells.ToIndex(next) == cell)
                    {
                        continue;
                    }
                    int neighbour = ShownVariant(chunk, rules, cells.ToIndex(next));
                    if (neighbour < 0)
                    {
                        continue;
                    }
                    var (start, end) = rules.GetNeighbourRange(neighbour, ((Direction)d).Opposite());
                    var allowed = new HashSet<int>(rules.adjacencyVariant.Skip(start).Take(end - start));
                    if (fitting == null)
                    {
                        fitting = allowed;
                    }
                    else
                    {
                        fitting.IntersectWith(allowed);
                    }
                }
            }

            var (first, last) = rules.GetNeighbourRange(variant, direction);
            for (int i = first; i < last; i++)
            {
                int candidate = rules.adjacencyVariant[i];
                if (fitting != null && !fitting.Contains(candidate))
                {
                    continue;
                }
                int tile = rules.variantTile[candidate];
                candidates.Add(new Candidate(candidate, tile, chunk.GetDefinition(tile), rules.GetVariantOrientation(candidate), rules.GetEntryWeight(i)));
            }
            return candidates;
        }

        /// <summary>The candidate currently previewed, wrapping the ghost index into range.</summary>
        public static Candidate? GetCurrentCandidate(List<Candidate> candidates)
        {
            if (candidates.Count == 0)
            {
                return null;
            }
            int index = ((GhostIndex % candidates.Count) + candidates.Count) % candidates.Count;
            return candidates[index];
        }

        public static Color GetCandidateColor(Candidate candidate)
        {
            Color color = candidate.Definition != null ? candidate.Definition.GizmoColor : Color.white;
            color.a = 0.45f;
            return color;
        }

        /// <summary>The variant a cell shows (its own tile, else a generated one), or -1 if it's empty or the rules don't have it.</summary>
        public static int ShownVariant(Chunk chunk, RulesetData rules, int cell)
        {
            int tile = ChunkLayers.GetShownTile(chunk.gameObject, cell, out Orientation orientation, out _);
            return tile < 0 ? -1 : rules.FindVariant(tile, orientation);
        }
    }
}
