using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace Wfc
{
    /// <summary>
    /// Learns adjacency rules from sample placements. Every pair of face-to-face neighbouring instances is an
    /// observation; it is expanded by every orientation both tiles allow (the world can be rotated as a whole),
    /// and variants that look identical (symmetries inside the allowed set) are merged. The result is a
    /// symmetric adjacency list per variant and direction, with one weight per learned connection. Problems with
    /// the input are reported as diagnostics, and the finished rules are checked by <see cref="RuleAnalysis"/>.
    /// </summary>
    public static class RuleDerivation
    {
        public static DerivationResult Derive(DerivationInput input)
        {
            DerivationResult result = new Context(input).Run();
            result.data.diagnostics = result.data.diagnostics.Concat(RuleAnalysis.Diagnose(input, result)).ToArray();
            return result;
        }

        private sealed class ConnectionBuilder
        {
            public Connection connection;
            /// <summary>Every variant pair the connection expands into, each in its canonical form.</summary>
            public HashSet<(int a, int d, int b)> members;
        }

        private sealed class Context
        {
            private const int NoVariant = -1;

            private readonly DerivationInput input;
            private readonly List<RuleDiagnostic> diagnostics = new List<RuleDiagnostic>();
            private readonly int tileCount;
            private readonly bool[] validTile;
            private readonly OrientationSet[] allowed;
            private readonly OrientationSet[] effective;
            private readonly int[] representative;
            private readonly int[] variantOf;
            private readonly List<int> variantTile = new List<int>();
            private readonly List<byte> variantOrientation = new List<byte>();
            private readonly Orientation[] normalized;
            private readonly bool[] valid;
            private readonly int[] instanceVariant;
            private readonly Dictionary<int3, int> cells = new Dictionary<int3, int>();
            private readonly Dictionary<string, int> tileById = new Dictionary<string, int>();
            private readonly List<ConnectionBuilder> connections = new List<ConnectionBuilder>();
            private readonly Dictionary<ConnectionKey, int> connectionByKey = new Dictionary<ConnectionKey, int>();
            private readonly List<ObservedPair> pairs = new List<ObservedPair>();

            public Context(DerivationInput input)
            {
                this.input = input;
                tileCount = input.tiles.Count;
                validTile = new bool[tileCount];
                allowed = new OrientationSet[tileCount];
                effective = new OrientationSet[tileCount];
                representative = new int[tileCount * Orientation.Count];
                variantOf = new int[tileCount * Orientation.Count];
                int instanceCount = input.instances.Count;
                normalized = new Orientation[instanceCount];
                valid = new bool[instanceCount];
                instanceVariant = Enumerable.Repeat(NoVariant, instanceCount).ToArray();
            }

            public DerivationResult Run()
            {
                PrepareTiles();
                BuildVariants();
                NormalizeInstances();
                CollectCells();
                CollectPairs();
                ApplyWeights();
                BuildAdjacency(out int[] adjacencyStart, out int[] adjacencyVariant, out int[] adjacencyConnection);

                var data = new RulesetData
                {
                    tileIds = input.tiles.Select(t => t.id).ToArray(),
                    tileNames = input.tiles.Select(t => t.name).ToArray(),
                    variantTile = variantTile.ToArray(),
                    variantOrientation = variantOrientation.ToArray(),
                    variantWeight = ComputeVariantWeights(),
                    adjacencyStart = adjacencyStart,
                    adjacencyVariant = adjacencyVariant,
                    adjacencyConnection = adjacencyConnection,
                    connections = connections.Select(c =>
                    {
                        Connection connection = c.connection;
                        connection.memberCount = c.members.Count;
                        return connection;
                    }).ToArray(),
                    diagnostics = diagnostics.ToArray(),
                };
                return new DerivationResult
                {
                    data = data,
                    pairs = pairs,
                    instanceVariant = instanceVariant,
                    inputHash = input.ComputeHash(),
                };
            }

            private void PrepareTiles()
            {
                for (int t = 0; t < tileCount; t++)
                {
                    TileSpec spec = input.tiles[t];
                    if (!tileById.TryAdd(spec.id ?? string.Empty, t))
                    {
                        Report(DiagnosticSeverity.Error, DiagnosticCode.InvalidTile, $"{spec.name} has the same id as another tile ({spec.id}).", t);
                    }
                    allowed[t] = spec.allowed;
                    if (!spec.allowed.IsGroup)
                    {
                        int[] sources = Enumerable.Range(0, input.instances.Count).Where(i => input.instances[i].tile == t).ToArray();
                        Report(DiagnosticSeverity.Error, DiagnosticCode.AllowedSetNotAGroup,
                            $"{spec.name}: the allowed orientations must contain the identity and every combination of its members. The tile is ignored.", t, sources);
                        continue;
                    }
                    validTile[t] = true;
                    OrientationSet geometric = spec.symmetry.With(Orientation.Identity).Closure();
                    effective[t] = geometric.Intersect(spec.allowed);
                }
            }

            private void BuildVariants()
            {
                for (int t = 0; t < tileCount; t++)
                {
                    for (int o = 0; o < Orientation.Count; o++)
                    {
                        representative[t * Orientation.Count + o] = NoVariant;
                        variantOf[t * Orientation.Count + o] = NoVariant;
                    }
                    if (!validTile[t])
                    {
                        continue;
                    }
                    // Variants are the cosets g·S of the effective symmetry S; the smallest index represents each.
                    foreach (Orientation g in allowed[t])
                    {
                        int rep = g.Index;
                        foreach (Orientation s in effective[t])
                        {
                            rep = math.min(rep, (g * s).Index);
                        }
                        representative[t * Orientation.Count + g.Index] = rep;
                        if (rep == g.Index)
                        {
                            variantOf[t * Orientation.Count + g.Index] = variantTile.Count;
                            variantTile.Add(t);
                            variantOrientation.Add(g.Index);
                        }
                    }
                }
            }

            private float[] ComputeVariantWeights()
            {
                var counts = new int[tileCount];
                foreach (int tile in variantTile)
                {
                    counts[tile]++;
                }
                // The base weight is spread across the variants, so rotatable tiles aren't more common just because they rotate.
                return variantTile.Select(tile => input.tiles[tile].baseWeight / counts[tile]).ToArray();
            }

            private void NormalizeInstances()
            {
                for (int i = 0; i < input.instances.Count; i++)
                {
                    SampleInstance instance = input.instances[i];
                    int t = instance.tile;
                    if (t < 0 || t >= tileCount)
                    {
                        Report(DiagnosticSeverity.Error, DiagnosticCode.InvalidTile, $"Instance {i} references an unknown tile.", -1, i);
                        continue;
                    }
                    if (!validTile[t])
                    {
                        continue;
                    }
                    if (!allowed[t].Contains(instance.orientation))
                    {
                        Report(DiagnosticSeverity.Warning, DiagnosticCode.InvalidOrientation,
                            $"{input.tiles[t].name} is placed in orientation {instance.orientation}, which its allowed orientations don't include. It is ignored.", t, i);
                        continue;
                    }

                    normalized[i] = Orientation.FromIndex(representative[t * Orientation.Count + instance.orientation.Index]);
                    instanceVariant[i] = variantOf[t * Orientation.Count + normalized[i].Index];
                    valid[i] = true;
                }
            }

            private void CollectCells()
            {
                for (int i = 0; i < input.instances.Count; i++)
                {
                    if (!valid[i])
                    {
                        continue;
                    }
                    int3 cell = input.instances[i].cell;
                    if (cells.TryGetValue(cell, out int other))
                    {
                        Report(DiagnosticSeverity.Error, DiagnosticCode.OverlappingInstances,
                            $"{TileName(other)} and {TileName(i)} both occupy cell {cell}. The second one is ignored.", -1, other, i);
                        valid[i] = false;
                        instanceVariant[i] = NoVariant;
                        continue;
                    }
                    cells.Add(cell, i);
                }
            }

            private string TileName(int instance) => input.tiles[input.instances[instance].tile].name;

            private void CollectPairs()
            {
                for (int i = 0; i < input.instances.Count; i++)
                {
                    if (!valid[i])
                    {
                        continue;
                    }
                    foreach (Direction direction in Directions.Positive)
                    {
                        if (cells.TryGetValue(input.instances[i].cell + direction.ToVector(), out int j))
                        {
                            pairs.Add(new ObservedPair { sourceA = i, sourceB = j, direction = direction, connection = Observe(i, direction, j) });
                        }
                    }
                }
            }

            private int Observe(int a, Direction direction, int b)
            {
                int tileA = input.instances[a].tile;
                int tileB = input.instances[b].tile;
                Orientation gA = normalized[a];
                Orientation gB = normalized[b];

                var members = new HashSet<(int, int, int)>();
                ConnectionKey best = default;
                bool hasBest = false;
                // Rotating the whole pair by h keeps it valid as long as both tiles allow h.
                foreach (Orientation h in allowed[tileA].Intersect(allowed[tileB]))
                {
                    int orientationA = representative[tileA * Orientation.Count + (h * gA).Index];
                    int orientationB = representative[tileB * Orientation.Count + (h * gB).Index];
                    Direction mapped = h.Apply(direction);
                    int variantA = variantOf[tileA * Orientation.Count + orientationA];
                    int variantB = variantOf[tileB * Orientation.Count + orientationB];
                    members.Add(CanonicalTriple(variantA, mapped, variantB));

                    // The smallest key over all members identifies the connection, independent of tile order.
                    ConnectionKey key = new ConnectionKey(input.tiles[tileA].id, orientationA, mapped, input.tiles[tileB].id, orientationB).Undirected;
                    if (!hasBest || key.CompareTo(best) < 0)
                    {
                        best = key;
                        hasBest = true;
                    }
                }

                if (connectionByKey.TryGetValue(best, out int existing))
                {
                    connections[existing].connection.observations++;
                    return existing;
                }
                connectionByKey.Add(best, connections.Count);
                connections.Add(new ConnectionBuilder
                {
                    connection = new Connection
                    {
                        key = best,
                        tileA = tileById.TryGetValue(best.tileA ?? string.Empty, out int keyTileA) ? keyTileA : tileA,
                        tileB = tileById.TryGetValue(best.tileB ?? string.Empty, out int keyTileB) ? keyTileB : tileB,
                        observations = 1,
                    },
                    members = members,
                });
                return connections.Count - 1;
            }

            private static (int, int, int) CanonicalTriple(int a, Direction direction, int b)
            {
                var forward = (a, (int)direction, b);
                var reversed = (b, (int)direction.Opposite(), a);
                return forward.CompareTo(reversed) <= 0 ? forward : reversed;
            }

            private void ApplyWeights()
            {
                var overrides = new Dictionary<ConnectionKey, float>();
                foreach (WeightOverride weightOverride in input.overrides)
                {
                    overrides[weightOverride.key.Undirected] = weightOverride.weight;
                }
                var used = new HashSet<ConnectionKey>();
                foreach (ConnectionBuilder builder in connections)
                {
                    ref Connection connection = ref builder.connection;
                    connection.weight = input.weightSource == WeightSource.Frequency ? connection.observations : 1f;
                    if (overrides.TryGetValue(connection.key, out float weight))
                    {
                        connection.weight = math.max(0f, weight);
                        connection.overridden = true;
                        used.Add(connection.key);
                    }
                }
                foreach (ConnectionKey key in overrides.Keys)
                {
                    if (!used.Contains(key))
                    {
                        Report(DiagnosticSeverity.Warning, DiagnosticCode.OrphanedOverride,
                            $"The weight override for {key} doesn't match any connection in the samples anymore.");
                    }
                }
            }

            /// <summary>Lists every member of every connection with a weight above 0, in both directions.</summary>
            private void BuildAdjacency(out int[] start, out int[] neighbour, out int[] connection)
            {
                // Per slot: neighbour variant -> connection. Connections partition the variant pairs, so each
                // neighbour belongs to exactly one connection.
                var slots = new SortedDictionary<int, int>[variantTile.Count * Directions.Count];
                void Add(int from, Direction direction, int to, int index)
                {
                    int slot = from * Directions.Count + (int)direction;
                    (slots[slot] ??= new SortedDictionary<int, int>()).TryAdd(to, index);
                }

                for (int c = 0; c < connections.Count; c++)
                {
                    if (connections[c].connection.weight <= 0f)
                    {
                        continue;
                    }
                    foreach (var (a, d, b) in connections[c].members)
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

            private void Report(DiagnosticSeverity severity, DiagnosticCode code, string message, int tile = -1, params int[] sources)
            {
                diagnostics.Add(new RuleDiagnostic(severity, code, message, tile, sources));
            }
        }
    }
}
