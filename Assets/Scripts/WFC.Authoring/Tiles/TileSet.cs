using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wfc.Authoring
{
    /// <summary>
    /// An ordered list of tile definitions. A tile's position in the list (its slot) is its tile index everywhere: in
    /// chunks, rule sources, baked rulesets and generators. Slots are never reordered or reused: removing a tile leaves
    /// its slot empty, so cells that still refer to it are reported instead of silently showing another tile.
    /// </summary>
    [CreateAssetMenu(menuName = "WFC/Tile Set", fileName = "NewTileSet", order = 1)]
    public sealed class TileSet : ScriptableObject
    {
        [SerializeField] private List<TileDefinition> tiles = new List<TileDefinition>();

        /// <summary>Raised whenever a tile set is edited.</summary>
        public static event Action<TileSet> Changed;

        /// <summary>Number of slots, including empty ones.</summary>
        public int Count => tiles.Count;

        public IReadOnlyList<TileDefinition> Tiles => tiles;

        /// <summary>The tile in a slot, or null for an empty slot or an index outside the set.</summary>
        public TileDefinition this[int index] => index >= 0 && index < tiles.Count ? tiles[index] : null;

        /// <summary>The slot of a tile, or -1.</summary>
        public int IndexOf(TileDefinition definition) => definition == null ? -1 : tiles.IndexOf(definition);

        public bool Contains(TileDefinition definition) => IndexOf(definition) >= 0;

        /// <summary>Adds a tile in a new slot at the end and returns its index. A tile that's already in the set keeps its slot.</summary>
        public int Append(TileDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }
            int existing = IndexOf(definition);
            if (existing >= 0)
            {
                return existing;
            }
            tiles.Add(definition);
            NotifyChanged();
            return tiles.Count - 1;
        }

        /// <summary>Empties a slot. Every other tile keeps its index.</summary>
        public void RemoveAt(int index)
        {
            if (index >= 0 && index < tiles.Count && tiles[index] != null)
            {
                tiles[index] = null;
                NotifyChanged();
            }
        }

        public void NotifyChanged() => Changed?.Invoke(this);

        private void OnValidate()
        {
            // A tile in two slots would make cells ambiguous; keep the first.
            for (int i = 0; i < tiles.Count; i++)
            {
                if (tiles[i] != null && tiles.IndexOf(tiles[i]) < i)
                {
                    Debug.LogWarning($"{name}: {tiles[i].name} is already in slot {tiles.IndexOf(tiles[i])}, so slot {i} is emptied.", this);
                    tiles[i] = null;
                }
            }
            NotifyChanged();
        }
    }
}
