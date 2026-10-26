using System.Collections.Generic;
using System.Linq;

namespace Wfc
{
    /// <summary>
    /// Rules from sockets: every tile gets a label per local face, and two tiles may be neighbours wherever their
    /// touching faces fit, i.e. carry the same label or a pair listed in <see cref="compatible"/>. Every fitting
    /// variant pair becomes part of a connection, with the same keys and the same expansion as sample rules, so
    /// overrides, analysis and the solver work unchanged. Every connection has weight 1 unless overridden. Faces that
    /// nothing fits (e.g. a typo in a label) show up as dead ends in <see cref="RuleAnalysis"/>.
    /// A label describes a whole face: a face pattern that isn't symmetric within its plane needs separate labels
    /// for the orientations that matter. Only tiles with at least one label get variants.
    /// </summary>
    public sealed class SocketRules : IRuleSource
    {
        /// <summary>
        /// Per tile index: the labels of the six local faces in <see cref="Direction"/> order. Missing entries, null
        /// entries and tiles without any label take no part; an empty label fits nothing.
        /// </summary>
        public List<string[]> faceLabels = new List<string[]>();
        /// <summary>Pairs of different labels that fit each other, in either order.</summary>
        public List<(string a, string b)> compatible = new List<(string, string)>();
        public List<WeightOverride> overrides = new List<WeightOverride>();

        public DerivedRules Derive(IReadOnlyList<TileSpec> tiles)
        {
            string[][] labels = Enumerable.Range(0, tiles.Count).Select(GetLabels).ToArray();
            var takesPart = new bool[tiles.Count];
            var stabilizers = new OrientationSet[tiles.Count];
            for (int t = 0; t < tiles.Count; t++)
            {
                takesPart[t] = labels[t].Any(label => label.Length > 0);
                stabilizers[t] = Stabilizer(labels[t]);
            }

            var builder = new RulesetBuilder(tiles, takesPart, stabilizers);
            VariantTable variants = builder.Variants;
            var fits = new HashSet<(string, string)>();
            foreach (var (a, b) in compatible)
            {
                fits.Add((Normalize(a), Normalize(b)));
                fits.Add((Normalize(b), Normalize(a)));
            }
            bool Fit(string a, string b) => a.Length > 0 && b.Length > 0 && (a == b || fits.Contains((a, b)));

            for (int a = 0; a < variants.VariantCount; a++)
            {
                int tileA = variants.VariantTile[a];
                Orientation orientationA = Orientation.FromIndex(variants.VariantOrientation[a]);
                for (int b = 0; b < variants.VariantCount; b++)
                {
                    int tileB = variants.VariantTile[b];
                    Orientation orientationB = Orientation.FromIndex(variants.VariantOrientation[b]);
                    foreach (Direction direction in Directions.Positive)
                    {
                        // The touching faces in each tile's own frame.
                        string faceA = labels[tileA][(int)orientationA.Inverse.Apply(direction)];
                        string faceB = labels[tileB][(int)orientationB.Inverse.Apply(direction.Opposite())];
                        if (Fit(faceA, faceB))
                        {
                            builder.Allow(tileA, orientationA, direction, tileB, orientationB);
                        }
                    }
                }
            }

            ReportSymmetry(builder, tiles);
            RulesetData data = builder.Build(false, overrides, out _);
            data.diagnostics = data.diagnostics.Concat(RuleAnalysis.Diagnose(data, tiles, null)).ToArray();
            return new DerivedRules { data = data, inputHash = ComputeHash(tiles) };
        }

        public string ComputeHash(IReadOnlyList<TileSpec> tiles)
        {
            StableHash hash = StableHash.Create();
            hash.Add("sockets");
            hash.Add(tiles.Count);
            for (int t = 0; t < tiles.Count; t++)
            {
                hash.Add(tiles[t]);
                foreach (string label in GetLabels(t))
                {
                    hash.Add(label);
                }
            }
            hash.Add(compatible.Count);
            foreach (var (a, b) in compatible)
            {
                hash.Add(Normalize(a));
                hash.Add(Normalize(b));
            }
            hash.Add(overrides);
            return hash.ToString();
        }

        /// <summary>The six normalized labels of a tile; all empty if it has none.</summary>
        private string[] GetLabels(int tile)
        {
            string[] given = tile < faceLabels.Count ? faceLabels[tile] : null;
            var labels = new string[Directions.Count];
            for (int face = 0; face < labels.Length; face++)
            {
                labels[face] = Normalize(given != null && face < given.Length ? given[face] : null);
            }
            return labels;
        }

        private static string Normalize(string label) => label?.Trim() ?? string.Empty;

        /// <summary>
        /// The orientations that map every face onto a face with the same label. Orientations of a tile that differ
        /// by anything else look different to the sockets, so they must stay separate variants.
        /// </summary>
        private static OrientationSet Stabilizer(string[] labels)
        {
            var set = OrientationSet.Empty;
            foreach (Orientation s in OrientationSet.All)
            {
                bool keeps = true;
                for (int face = 0; face < Directions.Count && keeps; face++)
                {
                    keeps = labels[(int)s.Apply((Direction)face)] == labels[face];
                }
                if (keeps)
                {
                    set = set.With(s);
                }
            }
            return set;
        }

        private static void ReportSymmetry(RulesetBuilder builder, IReadOnlyList<TileSpec> tiles)
        {
            VariantTable variants = builder.Variants;
            for (int t = 0; t < tiles.Count; t++)
            {
                if (!variants.IsValid(t))
                {
                    continue;
                }
                OrientationSet allowed = variants.Allowed(t);
                OrientationSet geometric = tiles[t].symmetry.With(Orientation.Identity).Closure().Intersect(allowed);
                if (!variants.Effective(t).Equals(geometric))
                {
                    int withoutSockets = allowed.Count / geometric.Count;
                    int withSockets = allowed.Count / variants.Effective(t).Count;
                    builder.Report(DiagnosticSeverity.Info, DiagnosticCode.SocketsBreakSymmetry,
                        $"{tiles[t].name}: its sockets tell apart orientations that look identical, so it has {withSockets} variants instead of {withoutSockets}.", t);
                }
            }
        }
    }
}
