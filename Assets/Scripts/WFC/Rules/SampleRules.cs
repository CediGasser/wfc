using System.Collections.Generic;
using System.Linq;

namespace Wfc
{
    /// <summary>How sample rules weight a connection.</summary>
    public enum SampleWeighting
    {
        /// <summary>Every connection that appears in the sample has weight 1.</summary>
        Presence,
        /// <summary>A connection's weight is how often it appears in the sample.</summary>
        Frequency,
    }

    /// <summary>
    /// Rules learned from a sample chunk. Every pair of face-to-face neighbouring cells is an observation; it is
    /// expanded by every orientation both tiles allow (the world can be rotated as a whole), and variants that look
    /// identical are merged (<see cref="RulesetBuilder"/>). Empty cells count as <see cref="emptyCellTile"/>, usually
    /// air; if that is <see cref="ChunkData.Empty"/>, they carry no information, like the cells outside the chunk.
    /// Only tiles that appear in the sample get variants.
    /// </summary>
    public sealed class SampleRules : IRuleSource
    {
        public ChunkData sample = new ChunkData();
        /// <summary>The tile empty cells count as, or <see cref="ChunkData.Empty"/> for "no information".</summary>
        public int emptyCellTile = ChunkData.Empty;
        public SampleWeighting weighting = SampleWeighting.Frequency;
        public List<WeightOverride> overrides = new List<WeightOverride>();

        public DerivedRules Derive(IReadOnlyList<TileSpec> tiles)
        {
            int cellCount = sample.CellCount;
            var cellTile = new int[cellCount];
            var cellOrientation = new Orientation[cellCount];
            var takesPart = new bool[tiles.Count];
            var missing = new Dictionary<int, List<int>>();
            for (int cell = 0; cell < cellCount; cell++)
            {
                bool empty = sample.IsEmpty(cell);
                int tile = empty ? emptyCellTile : sample.GetTile(cell);
                cellOrientation[cell] = empty ? Orientation.Identity : sample.GetOrientation(cell);
                cellTile[cell] = ChunkData.Empty;
                if (tile == ChunkData.Empty)
                {
                    continue;
                }
                if (tile < 0 || tile >= tiles.Count || !tiles[tile].IsDefined)
                {
                    if (!missing.TryGetValue(tile, out List<int> cells))
                    {
                        missing.Add(tile, cells = new List<int>());
                    }
                    cells.Add(cell);
                    continue;
                }
                cellTile[cell] = tile;
                takesPart[tile] = true;
            }

            var builder = new RulesetBuilder(tiles, takesPart);
            ReportMissing(builder, missing);
            int[] cellVariant = ResolveVariants(builder, tiles, cellTile, cellOrientation);
            List<CellPair> pairs = CollectPairs(builder, cellTile, cellOrientation, cellVariant);
            foreach (RuleDiagnostic diagnostic in builder.Diagnostics)
            {
                // Point problems with a tile definition at the cells that use it.
                if (diagnostic.tile >= 0 && diagnostic.cells.Length == 0)
                {
                    diagnostic.cells = Enumerable.Range(0, cellCount).Where(cell => cellTile[cell] == diagnostic.tile).ToArray();
                }
            }

            RulesetData data = builder.Build(weighting == SampleWeighting.Frequency, overrides, out int[] remap);
            for (int i = 0; i < pairs.Count; i++)
            {
                CellPair pair = pairs[i];
                pair.connection = remap[pair.connection];
                pairs[i] = pair;
            }
            data.diagnostics = data.diagnostics.Concat(RuleAnalysis.Diagnose(data, tiles, cellVariant)).ToArray();
            return new DerivedRules
            {
                data = data,
                inputHash = ComputeHash(tiles),
                cellVariant = cellVariant,
                pairs = pairs,
            };
        }

        public string ComputeHash(IReadOnlyList<TileSpec> tiles)
        {
            StableHash hash = StableHash.Create();
            hash.Add("samples");
            hash.Add(tiles.Count);
            foreach (TileSpec tile in tiles)
            {
                hash.Add(tile);
            }
            hash.Add(sample.size.x);
            hash.Add(sample.size.y);
            hash.Add(sample.size.z);
            for (int cell = 0; cell < sample.CellCount; cell++)
            {
                hash.Add(sample.tiles[cell]);
                hash.Add(sample.orientations[cell]);
            }
            hash.Add(emptyCellTile);
            hash.Add((int)weighting);
            hash.Add(overrides);
            return hash.ToString();
        }

        private void ReportMissing(RulesetBuilder builder, Dictionary<int, List<int>> missing)
        {
            foreach (var (tile, cells) in missing)
            {
                string message = tile == emptyCellTile
                    ? $"Empty cells count as tile {tile}, which isn't in the tile set or whose slot is empty. They carry no information instead."
                    : $"{cells.Count} cell(s) use tile {tile}, which isn't in the tile set or whose slot is empty. They are ignored.";
                builder.Report(DiagnosticSeverity.Error, DiagnosticCode.MissingTile, message, -1, cells.ToArray());
            }
        }

        private static int[] ResolveVariants(RulesetBuilder builder, IReadOnlyList<TileSpec> tiles, int[] cellTile, Orientation[] cellOrientation)
        {
            VariantTable variants = builder.Variants;
            var cellVariant = Enumerable.Repeat(-1, cellTile.Length).ToArray();
            var disallowed = new Dictionary<int, List<int>>();
            for (int cell = 0; cell < cellTile.Length; cell++)
            {
                int tile = cellTile[cell];
                if (tile < 0 || !variants.IsValid(tile))
                {
                    continue;
                }
                if (!variants.Allowed(tile).Contains(cellOrientation[cell]))
                {
                    if (!disallowed.TryGetValue(tile, out List<int> cells))
                    {
                        disallowed.Add(tile, cells = new List<int>());
                    }
                    cells.Add(cell);
                    continue;
                }
                cellVariant[cell] = variants.VariantOf(tile, cellOrientation[cell]);
            }

            foreach (var (tile, cells) in disallowed)
            {
                string orientations = string.Join(", ", cells.Select(c => cellOrientation[c]).Distinct());
                builder.Report(DiagnosticSeverity.Warning, DiagnosticCode.DisallowedOrientation,
                    $"{tiles[tile].name} is placed in orientations its allowed orientations don't include ({orientations}) in {cells.Count} cell(s). They are ignored.",
                    tile, cells.ToArray());
            }
            return cellVariant;
        }

        private List<CellPair> CollectPairs(RulesetBuilder builder, int[] cellTile, Orientation[] cellOrientation, int[] cellVariant)
        {
            var pairs = new List<CellPair>();
            for (int cell = 0; cell < cellVariant.Length; cell++)
            {
                if (cellVariant[cell] < 0)
                {
                    continue;
                }
                foreach (Direction direction in Directions.Positive)
                {
                    Unity.Mathematics.int3 next = sample.ToCell(cell) + direction.ToVector();
                    if (!sample.Contains(next))
                    {
                        continue;
                    }
                    int neighbour = sample.ToIndex(next);
                    if (cellVariant[neighbour] < 0)
                    {
                        continue;
                    }
                    int connection = builder.Observe(cellTile[cell], cellOrientation[cell], direction, cellTile[neighbour], cellOrientation[neighbour]);
                    pairs.Add(new CellPair { cellA = cell, cellB = neighbour, direction = direction, connection = connection });
                }
            }
            return pairs;
        }
    }
}
