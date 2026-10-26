using System;
using System.Collections.Generic;

namespace Wfc
{
    /// <summary>
    /// Something rules can be derived from, such as samples (<see cref="SampleRules"/>) or sockets
    /// (<see cref="SocketRules"/>). Every source produces the same solver-ready <see cref="RulesetData"/>, so the
    /// solver, the analysis and the editor tools work with any of them. Sources are only asked when rules are derived
    /// or baked; the solver itself only ever sees the derived data.
    /// </summary>
    public interface IRuleSource
    {
        /// <summary>
        /// Derives rules for <paramref name="tiles"/>; a tile's position in the list is its tile index. Tiles the source
        /// doesn't use get no variants.
        /// </summary>
        DerivedRules Derive(IReadOnlyList<TileSpec> tiles);

        /// <summary>Stable hash of everything that influences <see cref="Derive"/>, used to detect outdated bakes.</summary>
        string ComputeHash(IReadOnlyList<TileSpec> tiles);
    }

    /// <summary>The result of <see cref="IRuleSource.Derive"/>.</summary>
    public sealed class DerivedRules
    {
        public RulesetData data;
        public string inputHash;
        /// <summary>Variant per cell of the source's sample, or -1 for empty and ignored cells. Empty for sources without cells.</summary>
        public int[] cellVariant = Array.Empty<int>();
        /// <summary>Neighbouring sample cells and the connection each pair produced. Empty for sources without cells.</summary>
        public List<CellPair> pairs = new List<CellPair>();
    }

    /// <summary>Two neighbouring sample cells and the connection they produced (index into <see cref="RulesetData.connections"/>).</summary>
    public struct CellPair
    {
        public int cellA;
        public int cellB;
        /// <summary>Direction from A to B.</summary>
        public Direction direction;
        public int connection;
    }

    /// <summary>FNV-1a over values, formatted as 16 hex digits. Independent of culture and platform.</summary>
    internal struct StableHash
    {
        private ulong value;

        public static StableHash Create() => new StableHash { value = 14695981039346656037UL };

        public void Add(ulong data)
        {
            for (int i = 0; i < 8; i++)
            {
                value = (value ^ ((data >> (i * 8)) & 0xff)) * 1099511628211UL;
            }
        }

        public void Add(int data) => Add((ulong)(uint)data);

        public void Add(float data) => Add(BitConverter.SingleToInt32Bits(data));

        public void Add(bool data) => Add(data ? 1 : 0);

        public void Add(string data)
        {
            data ??= string.Empty;
            Add(data.Length);
            foreach (char c in data)
            {
                Add(c);
            }
        }

        public void Add(TileSpec tile)
        {
            Add(tile.id);
            Add(tile.allowed.Mask);
            Add(tile.symmetry.Mask);
            Add(tile.baseWeight);
            Add(tile.isInvisible);
        }

        public void Add(IReadOnlyList<WeightOverride> overrides)
        {
            Add(overrides.Count);
            foreach (WeightOverride weightOverride in overrides)
            {
                ConnectionKey key = weightOverride.key;
                Add(key.tileA);
                Add(key.orientationA);
                Add(key.direction);
                Add(key.tileB);
                Add(key.orientationB);
                Add(weightOverride.weight);
            }
        }

        public override string ToString() => value.ToString("x16");
    }
}
