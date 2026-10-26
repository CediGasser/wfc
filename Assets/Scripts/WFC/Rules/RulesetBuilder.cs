using System;
using System.Collections.Generic;
using System.Linq;

namespace Wfc
{
    /// <summary>
    /// Shared back end of every rule source: holds the variants of the tiles, collects which variant pairs may be
    /// neighbours, groups them into connections and turns everything into <see cref="RulesetData"/>. A connection is
    /// one allowed pair expanded by every orientation both tiles allow (the world can be rotated as a whole), keyed by
    /// the smallest <see cref="ConnectionKey"/> of its members, so the same connection gets the same key whichever
    /// source produced it.
    /// </summary>
    public sealed class RulesetBuilder
    {
        private sealed class ConnectionBuilder
        {
            public Connection connection;
            /// <summary>Every variant pair the connection expands into, each in its canonical form.</summary>
            public readonly List<(int a, int d, int b)> members = new List<(int, int, int)>();
        }

        private readonly IReadOnlyList<TileSpec> tiles;
        private readonly Dictionary<string, int> tileById = new Dictionary<string, int>();
        private readonly List<ConnectionBuilder> connections = new List<ConnectionBuilder>();
        private readonly Dictionary<(int, int, int), int> connectionOfMember = new Dictionary<(int, int, int), int>();

        /// <param name="takesPart">Which tiles get variants; null for every tile.</param>
        /// <param name="extraSymmetry">Per tile, a group that further restricts the effective symmetry; null for none.</param>
        public RulesetBuilder(IReadOnlyList<TileSpec> tiles, IReadOnlyList<bool> takesPart = null, IReadOnlyList<OrientationSet> extraSymmetry = null)
        {
            this.tiles = tiles;
            Variants = new VariantTable(tiles, takesPart, extraSymmetry, Diagnostics);
            for (int t = 0; t < tiles.Count; t++)
            {
                if (Variants.IsValid(t))
                {
                    tileById[tiles[t].id] = t;
                }
            }
        }

        public VariantTable Variants { get; }

        /// <summary>Problems found so far; they end up in <see cref="RulesetData.diagnostics"/>.</summary>
        public List<RuleDiagnostic> Diagnostics { get; } = new List<RuleDiagnostic>();

        public int ConnectionCount => connections.Count;

        public void Report(DiagnosticSeverity severity, DiagnosticCode code, string message, int tile = -1, params int[] cells)
        {
            Diagnostics.Add(new RuleDiagnostic(severity, code, message, tile, cells));
        }

        /// <summary>
        /// Records one observation of tile B in orientation <paramref name="b"/> next to tile A in orientation
        /// <paramref name="a"/>, in <paramref name="direction"/> from A. Both tiles must have variants and both
        /// orientations must be allowed. Returns the connection (index in the order of discovery).
        /// </summary>
        public int Observe(int tileA, Orientation a, Direction direction, int tileB, Orientation b)
        {
            int connection = Allow(tileA, a, direction, tileB, b);
            connections[connection].connection.observations++;
            return connection;
        }

        /// <summary>Like <see cref="Observe"/>, without counting an observation. Does nothing for known pairs.</summary>
        public int Allow(int tileA, Orientation a, Direction direction, int tileB, Orientation b)
        {
            var pair = CanonicalTriple(Variants.VariantOf(tileA, a), direction, Variants.VariantOf(tileB, b));
            if (connectionOfMember.TryGetValue(pair, out int known))
            {
                return known;
            }

            var created = new ConnectionBuilder();
            ConnectionKey best = default;
            bool hasBest = false;
            // Rotating the whole pair by h keeps it valid as long as both tiles allow h.
            foreach (Orientation h in Variants.Allowed(tileA).Intersect(Variants.Allowed(tileB)))
            {
                int orientationA = Variants.Representative(tileA, h * a);
                int orientationB = Variants.Representative(tileB, h * b);
                Direction mapped = h.Apply(direction);
                var member = CanonicalTriple(Variants.VariantOf(tileA, h * a), mapped, Variants.VariantOf(tileB, h * b));
                if (connectionOfMember.TryAdd(member, connections.Count))
                {
                    created.members.Add(member);
                }

                // The smallest key over all members identifies the connection, independent of tile order.
                ConnectionKey key = new ConnectionKey(tiles[tileA].id, orientationA, mapped, tiles[tileB].id, orientationB).Undirected;
                if (!hasBest || key.CompareTo(best) < 0)
                {
                    best = key;
                    hasBest = true;
                }
            }

            created.connection = new Connection
            {
                key = best,
                tileA = tileById.TryGetValue(best.tileA ?? string.Empty, out int keyTileA) ? keyTileA : tileA,
                tileB = tileById.TryGetValue(best.tileB ?? string.Empty, out int keyTileB) ? keyTileB : tileB,
            };
            connections.Add(created);
            return connections.Count - 1;
        }

        /// <summary>
        /// The finished rules; call once. Connections are sorted by key, so their order doesn't depend on the order of
        /// discovery; <paramref name="remap"/> maps each connection index returned by <see cref="Observe"/> or
        /// <see cref="Allow"/> to its index in <see cref="RulesetData.connections"/>. A connection's weight is its
        /// number of observations if <paramref name="weightByObservations"/>, else 1, unless overridden; connections
        /// with weight 0 are left out of the adjacency.
        /// </summary>
        public RulesetData Build(bool weightByObservations, IEnumerable<WeightOverride> overrides, out int[] remap)
        {
            int[] order = Enumerable.Range(0, connections.Count).OrderBy(i => connections[i].connection.key).ToArray();
            var sorted = new List<ConnectionBuilder>(order.Length);
            remap = new int[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                sorted.Add(connections[order[i]]);
                remap[order[i]] = i;
            }

            ApplyWeights(sorted, weightByObservations, overrides);
            BuildAdjacency(sorted, out int[] adjacencyStart, out int[] adjacencyVariant, out int[] adjacencyConnection);
            return new RulesetData
            {
                tileIds = tiles.Select(t => t.id ?? string.Empty).ToArray(),
                tileNames = tiles.Select(t => t.name ?? string.Empty).ToArray(),
                variantTile = Variants.VariantTile.ToArray(),
                variantOrientation = Variants.VariantOrientation.ToArray(),
                variantWeight = Variants.ComputeVariantWeights(tiles),
                orientationVariant = Variants.BuildOrientationVariant(),
                adjacencyStart = adjacencyStart,
                adjacencyVariant = adjacencyVariant,
                adjacencyConnection = adjacencyConnection,
                connections = sorted.Select(c =>
                {
                    Connection connection = c.connection;
                    connection.memberCount = c.members.Count;
                    return connection;
                }).ToArray(),
                diagnostics = Diagnostics.ToArray(),
            };
        }

        private static (int, int, int) CanonicalTriple(int a, Direction direction, int b)
        {
            var forward = (a, (int)direction, b);
            var reversed = (b, (int)direction.Opposite(), a);
            return forward.CompareTo(reversed) <= 0 ? forward : reversed;
        }

        private void ApplyWeights(List<ConnectionBuilder> sorted, bool weightByObservations, IEnumerable<WeightOverride> overrides)
        {
            var byKey = new Dictionary<ConnectionKey, float>();
            foreach (WeightOverride weightOverride in overrides ?? Enumerable.Empty<WeightOverride>())
            {
                byKey[weightOverride.key.Undirected] = weightOverride.weight;
            }
            var used = new HashSet<ConnectionKey>();
            foreach (ConnectionBuilder builder in sorted)
            {
                ref Connection connection = ref builder.connection;
                connection.weight = weightByObservations ? connection.observations : 1f;
                if (byKey.TryGetValue(connection.key, out float weight))
                {
                    connection.weight = Math.Max(0f, weight);
                    connection.overridden = true;
                    used.Add(connection.key);
                }
            }
            foreach (ConnectionKey key in byKey.Keys)
            {
                if (!used.Contains(key))
                {
                    Report(DiagnosticSeverity.Warning, DiagnosticCode.OrphanedOverride,
                        $"The weight override for {key} doesn't match any connection anymore.");
                }
            }
        }

        /// <summary>Lists every member of every connection with a weight above 0, in both directions.</summary>
        private void BuildAdjacency(List<ConnectionBuilder> sorted, out int[] start, out int[] neighbour, out int[] connection)
        {
            // Per slot: neighbour variant -> connection. Connections partition the variant pairs, so each neighbour
            // belongs to exactly one connection.
            var slots = new SortedDictionary<int, int>[Variants.VariantCount * Directions.Count];
            void Add(int from, Direction direction, int to, int index)
            {
                int slot = from * Directions.Count + (int)direction;
                (slots[slot] ??= new SortedDictionary<int, int>()).TryAdd(to, index);
            }

            for (int c = 0; c < sorted.Count; c++)
            {
                if (sorted[c].connection.weight <= 0f)
                {
                    continue;
                }
                foreach (var (a, d, b) in sorted[c].members)
                {
                    Add(a, (Direction)d, b, c);
                    Add(b, ((Direction)d).Opposite(), a, c);
                }
            }

            start = new int[slots.Length + 1];
            var neighbours = new List<int>();
            var owners = new List<int>();
            for (int slot = 0; slot < slots.Length; slot++)
            {
                start[slot] = neighbours.Count;
                if (slots[slot] == null)
                {
                    continue;
                }
                foreach (var (n, c) in slots[slot])
                {
                    neighbours.Add(n);
                    owners.Add(c);
                }
            }
            start[slots.Length] = neighbours.Count;
            neighbour = neighbours.ToArray();
            connection = owners.ToArray();
        }
    }
}
