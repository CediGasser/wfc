using System;
using System.Collections.Generic;

namespace Wfc
{
    /// <summary>
    /// One connection: an allowed pair plus every variant pair it expands into under the orientations both tiles
    /// allow. All of those pairs share the connection's weight.
    /// </summary>
    [Serializable]
    public struct Connection
    {
        public ConnectionKey key;
        public int tileA;
        public int tileB;
        /// <summary>How many sample pairs produced this connection; 0 for rules that don't come from samples.</summary>
        public int observations;
        /// <summary>Weight from the observations (or 1), or the override. 0 means the connection is forbidden.</summary>
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
        /// <summary>
        /// Per tile * <see cref="Orientation.Count"/> + orientation index: the variant that shows the tile in that
        /// orientation (any orientation of the variant's coset), or -1 if the orientation isn't allowed.
        /// </summary>
        public int[] orientationVariant = Array.Empty<int>();
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

        /// <summary>
        /// The variant that shows <paramref name="tile"/> in <paramref name="orientation"/>, which may be any orientation
        /// that looks like the variant's. -1 if the tile has no variants or doesn't allow the orientation.
        /// </summary>
        public int FindVariant(int tile, Orientation orientation)
        {
            if (tile < 0 || tile >= TileCount)
            {
                return -1;
            }
            int slot = tile * Orientation.Count + orientation.Index;
            if (slot < orientationVariant.Length)
            {
                return orientationVariant[slot];
            }
            // Rules baked before the orientation table existed: only the representative orientation is known.
            for (int v = 0; v < variantTile.Length; v++)
            {
                if (variantTile[v] == tile && variantOrientation[v] == orientation.Index)
                {
                    return v;
                }
            }
            return -1;
        }

        /// <summary>Every variant of a tile, in ascending order.</summary>
        public IEnumerable<int> GetVariants(int tile)
        {
            for (int v = 0; v < variantTile.Length; v++)
            {
                if (variantTile[v] == tile)
                {
                    yield return v;
                }
            }
        }

        /// <summary>For example "floor +Y ↔ scaffolding -Y": the touching faces of both tiles, each in its tile's own frame.</summary>
        public string DescribeConnection(Connection connection)
        {
            Direction direction = connection.key.Direction;
            Direction faceA = Orientation.FromIndex(connection.key.orientationA).Inverse.Apply(direction);
            Direction faceB = Orientation.FromIndex(connection.key.orientationB).Inverse.Apply(direction.Opposite());
            return $"{tileNames[connection.tileA]} {faceA.ToShortString()}  ↔  {tileNames[connection.tileB]} {faceB.ToShortString()}";
        }

        /// <summary>
        /// A copy with other connection weights (one per connection, in order). The adjacency is shared with this
        /// ruleset, so a weight may not change between 0 (forbidden, left out of the adjacency) and positive.
        /// </summary>
        /// <exception cref="ArgumentException">If the count doesn't match or a weight crosses 0.</exception>
        public RulesetData WithConnectionWeights(IReadOnlyList<float> weights)
        {
            if (weights.Count != connections.Length)
            {
                throw new ArgumentException($"Expected {connections.Length} weights, got {weights.Count}.", nameof(weights));
            }
            var copy = (RulesetData)MemberwiseClone();
            copy.connections = (Connection[])connections.Clone();
            for (int c = 0; c < connections.Length; c++)
            {
                if (weights[c] > 0f != connections[c].weight > 0f)
                {
                    throw new ArgumentException($"The weight of connection {c} can't change between 0 and positive.", nameof(weights));
                }
                copy.connections[c].weight = weights[c];
            }
            return copy;
        }
    }
}
