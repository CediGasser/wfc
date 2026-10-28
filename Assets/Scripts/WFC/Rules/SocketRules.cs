using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace Wfc
{
    /// <summary>
    /// Rules from sockets: every tile gets a label per local face, and two tiles may be neighbours wherever their
    /// touching faces fit. Every fitting variant pair becomes part of a connection, with the same keys and the same
    /// expansion as sample rules, so overrides, analysis and the solver work unchanged. Every connection has weight 1
    /// unless overridden. Faces that nothing fits (e.g. a typo in a label) show up as dead ends in
    /// <see cref="RuleAnalysis"/>. Only tiles with at least one label get variants.
    /// <para>
    /// A label consists of one or more parts separated by <c>|</c>, each at a fixed point of the face:
    /// <list type="bullet">
    /// <item>Side faces: 1 part (centre), 2 parts (left and right edge) or 3 parts (left edge, centre, right edge),
    /// left and right as seen from outside with the tile's +Y up.</item>
    /// <item>Top and bottom faces: 1 part (centre), 4 parts (corners +X+Z, −X+Z, −X−Z, +X−Z, counter-clockwise seen
    /// from above) or 5 parts (those corners and the centre).</item>
    /// </list>
    /// Two faces fit if their parts lie on the same points once both tiles are placed, and the parts at every point
    /// are equal (unless listed in <see cref="exclusive"/>) or a pair listed in <see cref="compatible"/>. Rotating or mirroring a tile moves its parts with it,
    /// so a single-part label fits however the face is turned, while e.g. <c>a|b</c> only meets <c>b|a</c> and a
    /// four-part top only stacks in the matching rotation. An empty part fits nothing.
    /// </para>
    /// </summary>
    public sealed class SocketRules : IRuleSource
    {
        /// <summary>Separates the parts of a label.</summary>
        public const char PartSeparator = '|';

        /// <summary>
        /// Per tile index: the labels of the six local faces in <see cref="Direction"/> order. Missing entries, null
        /// entries and tiles without any label take no part; an empty label fits nothing.
        /// </summary>
        public List<string[]> faceLabels = new List<string[]>();
        /// <summary>Pairs of different label parts that fit each other, in either order.</summary>
        public List<(string a, string b)> compatible = new List<(string, string)>();
        /// <summary>
        /// Label parts that don't fit themselves, only the parts they're paired with in <see cref="compatible"/>, e.g.
        /// the crest of a slope that may meet flat ground but not another crest.
        /// </summary>
        public List<string> exclusive = new List<string>();
        public List<WeightOverride> overrides = new List<WeightOverride>();

        /// <summary>A label part at a point of the tile's cube, in coordinates where the cube spans −2..2.</summary>
        private readonly struct Part
        {
            public Part(int3 point, string label)
            {
                Point = point;
                Label = label;
            }

            public int3 Point { get; }
            public string Label { get; }
        }

        public DerivedRules Derive(IReadOnlyList<TileSpec> tiles)
        {
            var faces = new Part[tiles.Count][][];
            var problems = new List<(int tile, string message)>();
            var takesPart = new bool[tiles.Count];
            var stabilizers = new OrientationSet[tiles.Count];
            for (int t = 0; t < tiles.Count; t++)
            {
                string[] labels = GetLabels(t);
                faces[t] = new Part[Directions.Count][];
                for (int face = 0; face < Directions.Count; face++)
                {
                    faces[t][face] = ParseFace(labels[face], (Direction)face, out string problem);
                    if (problem != null)
                    {
                        problems.Add((t, $"{tiles[t].name}, face {((Direction)face).ToShortString()}: {problem}"));
                    }
                }
                takesPart[t] = labels.Any(label => label.Length > 0);
                stabilizers[t] = Stabilizer(faces[t]);
            }

            var builder = new RulesetBuilder(tiles, takesPart, stabilizers);
            foreach (var (tile, message) in problems)
            {
                builder.Report(DiagnosticSeverity.Error, DiagnosticCode.InvalidSocket, message, tile);
            }
            VariantTable variants = builder.Variants;
            var fits = new HashSet<(string, string)>();
            foreach (var (a, b) in compatible)
            {
                fits.Add((Normalize(a), Normalize(b)));
                fits.Add((Normalize(b), Normalize(a)));
            }
            var exclusiveParts = new HashSet<string>(exclusive.Select(Normalize));
            bool Fit(string a, string b) => a.Length > 0 && b.Length > 0 && ((a == b && !exclusiveParts.Contains(a)) || fits.Contains((a, b)));

            // Per variant and world direction: the parts of the face pointing there, at their world points.
            var patterns = new Part[variants.VariantCount * Directions.Count][];
            for (int v = 0; v < variants.VariantCount; v++)
            {
                Orientation orientation = Orientation.FromIndex(variants.VariantOrientation[v]);
                for (int d = 0; d < Directions.Count; d++)
                {
                    Part[] local = faces[variants.VariantTile[v]][(int)orientation.Inverse.Apply((Direction)d)];
                    patterns[v * Directions.Count + d] = Transform(local, orientation, int3.zero);
                }
            }

            for (int a = 0; a < variants.VariantCount; a++)
            {
                for (int b = 0; b < variants.VariantCount; b++)
                {
                    foreach (Direction direction in Directions.Positive)
                    {
                        Part[] faceA = patterns[a * Directions.Count + (int)direction];
                        Part[] faceB = patterns[b * Directions.Count + (int)direction.Opposite()];
                        if (Touch(faceA, faceB, direction.ToVector() * 4, Fit))
                        {
                            builder.Allow(variants.VariantTile[a], Orientation.FromIndex(variants.VariantOrientation[a]), direction,
                                variants.VariantTile[b], Orientation.FromIndex(variants.VariantOrientation[b]));
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
            if (exclusive.Count > 0)
            {
                hash.Add("exclusive");
                foreach (string part in exclusive.Select(Normalize).OrderBy(p => p, StringComparer.Ordinal))
                {
                    hash.Add(part);
                }
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
        /// The parts of a face label at their points, sorted by point. A label with an unsupported number of parts
        /// gets no parts (it fits nothing) and a problem description.
        /// </summary>
        private static Part[] ParseFace(string label, Direction face, out string problem)
        {
            problem = null;
            if (label.Length == 0)
            {
                return Array.Empty<Part>();
            }
            string[] parts = label.Split(PartSeparator).Select(part => part.Trim()).ToArray();
            int3 normal = face.ToVector();
            int3 center = normal * 2;
            int3[] points;
            if (face.Axis() == 1)
            {
                var corners = new[] { new int3(2, 0, 2), new int3(-2, 0, 2), new int3(-2, 0, -2), new int3(2, 0, -2) };
                points = parts.Length switch
                {
                    1 => new[] { center },
                    4 => corners.Select(c => c + center).ToArray(),
                    5 => corners.Select(c => c + center).Append(center).ToArray(),
                    _ => null,
                };
                problem = points == null ? $"\"{label}\" has {parts.Length} parts; top and bottom faces take 1, 4 or 5." : null;
            }
            else
            {
                // Seen from outside with +Y up, right is normal × up.
                var right = new int3(-normal.z, 0, normal.x) * 2;
                points = parts.Length switch
                {
                    1 => new[] { center },
                    2 => new[] { center - right, center + right },
                    3 => new[] { center - right, center, center + right },
                    _ => null,
                };
                problem = points == null ? $"\"{label}\" has {parts.Length} parts; side faces take 1, 2 or 3." : null;
            }
            if (points == null)
            {
                return Array.Empty<Part>();
            }
            return Sorted(points.Select((point, i) => new Part(point, parts[i])));
        }

        /// <summary>The parts moved by an orientation and an offset, sorted by point.</summary>
        private static Part[] Transform(Part[] parts, Orientation orientation, int3 offset)
        {
            return Sorted(parts.Select(part => new Part(orientation.Apply(part.Point) + offset, part.Label)));
        }

        private static Part[] Sorted(IEnumerable<Part> parts)
        {
            return parts.OrderBy(p => p.Point.x).ThenBy(p => p.Point.y).ThenBy(p => p.Point.z).ToArray();
        }

        /// <summary>
        /// True if face A and face B, of a tile at <paramref name="offset"/> from A's, put their parts on the same
        /// points and every pair of parts fits.
        /// </summary>
        private static bool Touch(Part[] a, Part[] b, int3 offset, Func<string, string, bool> fit)
        {
            if (a.Length == 0 || a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                // Sorting is unaffected by the translation, so the parts line up by index.
                if (!a[i].Point.Equals(b[i].Point + offset) || !fit(a[i].Label, b[i].Label))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The orientations that map every face's parts onto the parts of its image face. Orientations of a tile that
        /// differ by anything else look different to the sockets, so they must stay separate variants.
        /// </summary>
        private static OrientationSet Stabilizer(Part[][] faces)
        {
            var set = OrientationSet.Empty;
            foreach (Orientation s in OrientationSet.All)
            {
                bool keeps = true;
                for (int face = 0; face < Directions.Count && keeps; face++)
                {
                    Part[] moved = Transform(faces[face], s, int3.zero);
                    Part[] image = faces[(int)s.Apply((Direction)face)];
                    keeps = moved.Length == image.Length
                        && moved.Zip(image, (m, i) => m.Point.Equals(i.Point) && m.Label == i.Label).All(same => same);
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
