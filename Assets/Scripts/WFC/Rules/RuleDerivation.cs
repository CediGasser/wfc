using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace Wfc
{
    /// <summary>
    /// Learns adjacency rules from sample placements. Every pair of face-to-face neighbouring instances is an
    /// observation; it is expanded by every orientation both tiles allow (the world can be rotated as a whole),
    /// and variants that look identical (symmetries inside the allowed set) are merged. The result is a
    /// symmetric, undirected rule set with one weight per learned connection.
    /// </summary>
    public static class RuleDerivation
    {
        public static DerivationResult Derive(DerivationInput input)
        {
            return new Context(input).Run();
        }

        private sealed class OrbitBuilder
        {
            public OrbitKey key;
            public int tileA;
            public int tileB;
            public int observations;
            public float weight;
            public bool overridden;
            public HashSet<(int a, int d, int b)> members;
        }

        private sealed class Context
        {
            private const int NoVariant = -1;

            private readonly DerivationInput input;
            private readonly List<RuleDiagnostic> diagnostics = new List<RuleDiagnostic>();
            private readonly int tileCount;
            private readonly OrientationSet[] allowed;
            private readonly OrientationSet[] geometric;
            private readonly OrientationSet[] effective;
            private readonly int[] representative;
            private readonly int[] variantOf;
            private readonly List<int> variantTile = new List<int>();
            private readonly List<byte> variantOrientation = new List<byte>();
            private readonly Dictionary<string, int> tileById = new Dictionary<string, int>();
            private readonly Orientation[] normalized;
            private readonly bool[] valid;
            private readonly int[] instanceVariant;
            private readonly Dictionary<int3, int> cells = new Dictionary<int3, int>();
            private readonly List<OrbitBuilder> orbits = new List<OrbitBuilder>();
            private readonly Dictionary<OrbitKey, int> orbitByKey = new Dictionary<OrbitKey, int>();
            private readonly List<ObservedPair> pairs = new List<ObservedPair>();

            public Context(DerivationInput input)
            {
                this.input = input;
                tileCount = input.tiles.Count;
                allowed = new OrientationSet[tileCount];
                geometric = new OrientationSet[tileCount];
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
                var adjacency = BuildAdjacency(out int[] adjacencyStart, out int[] adjacencyVariant, out float[] adjacencyWeight);
                DiagnoseTiles(adjacency);

                var data = new RulesetData
                {
                    tileIds = input.tiles.Select(t => t.id).ToArray(),
                    tileNames = input.tiles.Select(t => t.name).ToArray(),
                    variantTile = variantTile.ToArray(),
                    variantOrientation = variantOrientation.ToArray(),
                    variantWeight = ComputeVariantWeights(),
                    adjacencyStart = adjacencyStart,
                    adjacencyVariant = adjacencyVariant,
                    adjacencyWeight = adjacencyWeight,
                    orbits = orbits.Select(o => new Orbit
                    {
                        key = o.key,
                        tileA = o.tileA,
                        tileB = o.tileB,
                        observations = o.observations,
                        weight = o.weight,
                        overridden = o.overridden,
                        memberCount = o.members.Count,
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

                    OrientationSet set = spec.allowed;
                    if (!set.IsGroup)
                    {
                        set = set.Closure();
                        Report(DiagnosticSeverity.Warning, DiagnosticCode.AllowedSetNotAGroup,
                            $"{spec.name}: the allowed orientations aren't closed under combination, so they were extended to {set.Count} orientations.", t);
                    }
                    allowed[t] = set;
                    geometric[t] = spec.symmetry.With(Orientation.Identity).Closure();
                    effective[t] = geometric[t].Intersect(set).Closure();
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

                    Orientation g = instance.orientation;
                    string name = input.tiles[t].name;
                    if (!allowed[t].Contains(g))
                    {
                        if (!TryFindLookalike(t, g, out Orientation lookalike))
                        {
                            Report(DiagnosticSeverity.Warning, DiagnosticCode.InvalidOrientation,
                                $"{name} is placed in orientation {g}, which its allowed orientations don't include. It is ignored.", t, i);
                            continue;
                        }
                        Report(DiagnosticSeverity.Info, DiagnosticCode.OrientationMapped,
                            $"{name} is placed in orientation {g}, which it doesn't allow, but it looks identical to allowed orientation {lookalike}. It is treated as that.", t, i);
                        g = lookalike;
                    }

                    normalized[i] = Orientation.FromIndex(representative[t * Orientation.Count + g.Index]);
                    instanceVariant[i] = variantOf[t * Orientation.Count + normalized[i].Index];
                    valid[i] = true;
                }
            }

            private bool TryFindLookalike(int tile, Orientation placed, out Orientation lookalike)
            {
                // The placed model looks like allowed orientation a if a⁻¹·g is a geometric symmetry.
                foreach (Orientation candidate in allowed[tile])
                {
                    if (geometric[tile].Contains(candidate.Inverse * placed))
                    {
                        lookalike = candidate;
                        return true;
                    }
                }
                lookalike = Orientation.Identity;
                return false;
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
                            pairs.Add(new ObservedPair { sourceA = i, sourceB = j, direction = direction, orbit = Observe(i, direction, j) });
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
                OrbitKey best = default;
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

                    OrbitKey key = new OrbitKey(input.tiles[tileA].id, orientationA, mapped, input.tiles[tileB].id, orientationB).Undirected;
                    if (!hasBest || key.CompareTo(best) < 0)
                    {
                        best = key;
                        hasBest = true;
                    }
                }

                if (orbitByKey.TryGetValue(best, out int existing))
                {
                    orbits[existing].observations++;
                    return existing;
                }
                orbitByKey.Add(best, orbits.Count);
                orbits.Add(new OrbitBuilder
                {
                    key = best,
                    tileA = tileById.TryGetValue(best.tileA ?? string.Empty, out int keyTileA) ? keyTileA : tileA,
                    tileB = tileById.TryGetValue(best.tileB ?? string.Empty, out int keyTileB) ? keyTileB : tileB,
                    observations = 1,
                    members = members,
                });
                return orbits.Count - 1;
            }

            private static (int, int, int) CanonicalTriple(int a, Direction direction, int b)
            {
                var forward = (a, (int)direction, b);
                var reversed = (b, (int)direction.Opposite(), a);
                return forward.CompareTo(reversed) <= 0 ? forward : reversed;
            }

            private void ApplyWeights()
            {
                var overrides = new Dictionary<OrbitKey, float>();
                foreach (WeightOverride weightOverride in input.overrides)
                {
                    overrides[weightOverride.key.Undirected] = weightOverride.weight;
                }
                var used = new HashSet<OrbitKey>();
                foreach (OrbitBuilder orbit in orbits)
                {
                    orbit.weight = input.weightSource == WeightSource.Frequency ? orbit.observations : 1f;
                    if (overrides.TryGetValue(orbit.key, out float weight))
                    {
                        orbit.weight = math.max(0f, weight);
                        orbit.overridden = true;
                        used.Add(orbit.key);
                    }
                }
                foreach (OrbitKey key in overrides.Keys)
                {
                    if (!used.Contains(key))
                    {
                        Report(DiagnosticSeverity.Warning, DiagnosticCode.OrphanedOverride,
                            $"The weight override for {key} doesn't match any connection in the samples anymore.");
                    }
                }
            }

            private List<(int neighbour, float weight)>[] BuildAdjacency(out int[] start, out int[] neighbour, out float[] weight)
            {
                int variantCount = variantTile.Count;
                var slots = new Dictionary<int, float>[variantCount * Directions.Count];
                void Add(int from, Direction direction, int to, float w)
                {
                    int slot = from * Directions.Count + (int)direction;
                    slots[slot] ??= new Dictionary<int, float>();
                    slots[slot][to] = slots[slot].TryGetValue(to, out float existing) ? math.max(existing, w) : w;
                }

                foreach (OrbitBuilder orbit in orbits)
                {
                    if (orbit.weight <= 0f)
                    {
                        continue;
                    }
                    foreach (var (a, d, b) in orbit.members)
                    {
                        Add(a, (Direction)d, b, orbit.weight);
                        Add(b, ((Direction)d).Opposite(), a, orbit.weight);
                    }
                }

                var lists = new List<(int, float)>[slots.Length];
                start = new int[slots.Length + 1];
                var neighbours = new List<int>();
                var weights = new List<float>();
                for (int slot = 0; slot < slots.Length; slot++)
                {
                    start[slot] = neighbours.Count;
                    lists[slot] = slots[slot] == null
                        ? new List<(int, float)>()
                        : slots[slot].OrderBy(entry => entry.Key).Select(entry => (entry.Key, entry.Value)).ToList();
                    foreach (var (n, w) in lists[slot])
                    {
                        neighbours.Add(n);
                        weights.Add(w);
                    }
                }
                start[slots.Length] = neighbours.Count;
                neighbour = neighbours.ToArray();
                weight = weights.ToArray();
                return lists;
            }

            private void DiagnoseTiles(List<(int neighbour, float weight)>[] adjacency)
            {
                var connected = new bool[tileCount];
                var selfConnected = new bool[tileCount];
                foreach (OrbitBuilder orbit in orbits)
                {
                    if (orbit.weight <= 0f)
                    {
                        continue;
                    }
                    connected[orbit.tileA] = true;
                    connected[orbit.tileB] = true;
                    if (orbit.tileA == orbit.tileB)
                    {
                        selfConnected[orbit.tileA] = true;
                    }
                }

                for (int t = 0; t < tileCount; t++)
                {
                    int[] sources = Enumerable.Range(0, input.instances.Count).Where(i => valid[i] && input.instances[i].tile == t).ToArray();
                    if (sources.Length == 0)
                    {
                        continue;
                    }
                    string name = input.tiles[t].name;
                    if (!connected[t])
                    {
                        Report(DiagnosticSeverity.Warning, DiagnosticCode.TileNeverConnected,
                            $"{name} never touches another tile in the samples, so it can't be generated.", t, sources);
                        continue;
                    }
                    if (input.tiles[t].isEmpty && !selfConnected[t])
                    {
                        Report(DiagnosticSeverity.Warning, DiagnosticCode.MissingSelfConnection,
                            $"{name} never touches another {name} in the samples, so two {name} cells can't be neighbours.", t, sources);
                    }

                    int deadFaces = 0;
                    for (int v = 0; v < variantTile.Count; v++)
                    {
                        if (variantTile[v] != t)
                        {
                            continue;
                        }
                        Orientation inverse = Orientation.FromIndex(variantOrientation[v]).Inverse;
                        for (int d = 0; d < Directions.Count; d++)
                        {
                            if (adjacency[v * Directions.Count + d].Count == 0)
                            {
                                deadFaces |= 1 << (int)inverse.Apply((Direction)d);
                            }
                        }
                    }
                    if (deadFaces != 0)
                    {
                        string faces = string.Join(", ", Enumerable.Range(0, Directions.Count)
                            .Where(d => (deadFaces & (1 << d)) != 0)
                            .Select(d => ((Direction)d).ToShortString()));
                        Report(DiagnosticSeverity.Warning, DiagnosticCode.DeadEnd,
                            $"{name}: nothing was learned next to local face(s) {faces} in at least one orientation, so those variants only fit at the edge of the world.", t, sources);
                    }
                }
            }

            private void Report(DiagnosticSeverity severity, DiagnosticCode code, string message, int tile = -1, params int[] sources)
            {
                diagnostics.Add(new RuleDiagnostic(severity, code, message, tile, sources));
            }
        }
    }
}
