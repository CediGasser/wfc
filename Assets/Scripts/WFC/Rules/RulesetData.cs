using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Wfc
{
    public enum WeightSource
    {
        /// <summary>Every connection that appears in a sample has weight 1.</summary>
        Presence,
        /// <summary>A connection's weight is how often it appears in the samples.</summary>
        Frequency,
    }

    /// <summary>One tile as seen by rule derivation.</summary>
    public struct TileSpec
    {
        /// <summary>Stable id (the definition asset's GUID), used for override keys.</summary>
        public string id;
        public string name;
        public OrientationSet allowed;
        /// <summary>Geometric symmetry of the model. Only the part inside <see cref="allowed"/> is used.</summary>
        public OrientationSet symmetry;
        public float baseWeight;
        /// <summary>Invisible tiles such as air. Used for diagnostics only.</summary>
        public bool isEmpty;
    }

    /// <summary>One placed tile in a sample. Its index in the input list is its source id.</summary>
    public struct SampleInstance
    {
        public int tile;
        public int3 cell;
        public Orientation orientation;
    }

    public sealed class DerivationInput
    {
        public readonly List<TileSpec> tiles = new List<TileSpec>();
        public readonly List<SampleInstance> instances = new List<SampleInstance>();
        public readonly List<WeightOverride> overrides = new List<WeightOverride>();
        public WeightSource weightSource = WeightSource.Frequency;

        /// <summary>Stable hash of everything that influences the result, used to detect outdated bakes.</summary>
        public string ComputeHash()
        {
            ulong hash = 14695981039346656037UL;
            void Add(string value)
            {
                foreach (char c in value ?? string.Empty)
                {
                    hash = (hash ^ c) * 1099511628211UL;
                }
                hash = (hash ^ 0xff) * 1099511628211UL;
            }
            Add(((int)weightSource).ToString());
            foreach (TileSpec tile in tiles)
            {
                Add($"{tile.id}|{tile.allowed.Mask}|{tile.symmetry.Mask}|{tile.baseWeight:R}|{tile.isEmpty}");
            }
            foreach (SampleInstance instance in instances)
            {
                Add($"{instance.tile}|{instance.cell.x},{instance.cell.y},{instance.cell.z}|{instance.orientation.Index}");
            }
            foreach (WeightOverride weightOverride in overrides)
            {
                Add($"{weightOverride.key}|{weightOverride.weight:R}");
            }
            return hash.ToString("x16");
        }
    }

    /// <summary>
    /// One learned connection: an observed pair plus every variant pair it expands into under the allowed
    /// orientations. All of those pairs share the connection's weight.
    /// </summary>
    [Serializable]
    public struct Connection
    {
        public ConnectionKey key;
        public int tileA;
        public int tileB;
        /// <summary>How many observed sample pairs produced this connection.</summary>
        public int observations;
        /// <summary>Weight from the <see cref="WeightSource"/>, or the override. 0 means the connection is forbidden.</summary>
        public float weight;
        public bool overridden;
        /// <summary>Number of distinct variant pairs the connection expands into.</summary>
        public int memberCount;
    }

    /// <summary>
    /// Solver-ready rules: variants (tile + orientation) with weights, and for every variant and direction the
    /// list of variants allowed next to it, each with the connection it belongs to. Adjacency is stored in CSR form and is
    /// symmetric: if B is listed next to A in direction d, A is listed next to B in the opposite direction.
    /// </summary>
    [Serializable]
    public sealed class RulesetData
    {
        public string[] tileIds = Array.Empty<string>();
        public string[] tileNames = Array.Empty<string>();
        public int[] variantTile = Array.Empty<int>();
        public byte[] variantOrientation = Array.Empty<byte>();
        public float[] variantWeight = Array.Empty<float>();
        /// <summary>Neighbours of variant v in direction d are at [adjacencyStart[v*6+d], adjacencyStart[v*6+d+1]).</summary>
        public int[] adjacencyStart = Array.Empty<int>();
        public int[] adjacencyVariant = Array.Empty<int>();
        /// <summary>For every adjacency entry, the index of its connection in <see cref="connections"/>.</summary>
        public int[] adjacencyConnection = Array.Empty<int>();
        public Connection[] connections = Array.Empty<Connection>();
        public RuleDiagnostic[] diagnostics = Array.Empty<RuleDiagnostic>();

        public int TileCount => tileIds.Length;

        public int VariantCount => variantTile.Length;

        public Orientation GetVariantOrientation(int variant) => Orientation.FromIndex(variantOrientation[variant]);

        public (int start, int end) GetNeighbourRange(int variant, Direction direction)
        {
            int slot = variant * Directions.Count + (int)direction;
            return (adjacencyStart[slot], adjacencyStart[slot + 1]);
        }

        /// <summary>The weight of adjacency entry <paramref name="entry"/>, i.e. of its connection.</summary>
        public float GetEntryWeight(int entry) => connections[adjacencyConnection[entry]].weight;

        public int FindVariant(int tile, Orientation orientation)
        {
            for (int v = 0; v < variantTile.Length; v++)
            {
                if (variantTile[v] == tile && variantOrientation[v] == orientation.Index)
                {
                    return v;
                }
            }
            return -1;
        }
    }

    /// <summary>A pair of touching sample instances and the connection it produced (index into <see cref="RulesetData.connections"/>).</summary>
    public struct ObservedPair
    {
        public int sourceA;
        public int sourceB;
        /// <summary>Direction from A to B in sample space.</summary>
        public Direction direction;
        public int connection;
    }

    public sealed class DerivationResult
    {
        public RulesetData data;
        public List<ObservedPair> pairs = new List<ObservedPair>();
        /// <summary>Variant index per source instance, or -1 if the instance was skipped.</summary>
        public int[] instanceVariant = Array.Empty<int>();
        public string inputHash;
    }
}
