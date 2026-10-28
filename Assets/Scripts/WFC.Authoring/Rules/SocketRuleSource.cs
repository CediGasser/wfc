using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wfc.Authoring
{
    /// <summary>
    /// Rules from sockets instead of samples: every tile of the tile set gets a label per face (in the tile's own
    /// frame), and two tiles may be neighbours wherever their touching faces fit. A label can consist of parts at
    /// points of the face (e.g. <c>left|right</c>), so face patterns that aren't symmetric work as well; see
    /// <see cref="SocketRules"/>. Tiles without any label don't take part.
    /// </summary>
    [AddComponentMenu("WFC/Socket Rule Source")]
    public sealed class SocketRuleSource : RuleSource
    {
        /// <summary>The labels of one tile's six faces.</summary>
        [Serializable]
        public sealed class TileSockets
        {
            public TileDefinition tile;
            public string posX = string.Empty;
            public string negX = string.Empty;
            public string posY = string.Empty;
            public string negY = string.Empty;
            public string posZ = string.Empty;
            public string negZ = string.Empty;

            public string this[Direction face]
            {
                get => face switch
                {
                    Direction.PosX => posX,
                    Direction.NegX => negX,
                    Direction.PosY => posY,
                    Direction.NegY => negY,
                    Direction.PosZ => posZ,
                    _ => negZ,
                } ?? string.Empty;
                set
                {
                    switch (face)
                    {
                        case Direction.PosX: posX = value; break;
                        case Direction.NegX: negX = value; break;
                        case Direction.PosY: posY = value; break;
                        case Direction.NegY: negY = value; break;
                        case Direction.PosZ: posZ = value; break;
                        default: negZ = value; break;
                    }
                }
            }

            /// <summary>The six labels in <see cref="Direction"/> order.</summary>
            public string[] ToArray()
            {
                var labels = new string[Directions.Count];
                for (int face = 0; face < labels.Length; face++)
                {
                    labels[face] = this[(Direction)face];
                }
                return labels;
            }
        }

        /// <summary>Two different labels that fit each other.</summary>
        [Serializable]
        public struct SocketPair
        {
            public string a;
            public string b;

            public SocketPair(string a, string b)
            {
                this.a = a;
                this.b = b;
            }
        }

        [Tooltip("The tiles the sockets are defined for.")]
        [SerializeField] private TileSet tileSet;
        [SerializeField] private List<TileSockets> sockets = new List<TileSockets>();
        [SerializeField] private List<SocketPair> compatible = new List<SocketPair>();
        [Tooltip("Label parts that don't fit themselves, only their compatible pairs.")]
        [SerializeField] private List<string> exclusive = new List<string>();

        public override TileSet TileSet => tileSet;

        public IReadOnlyList<TileSockets> Sockets => sockets;

        public IReadOnlyList<SocketPair> Compatible => compatible;

        public IReadOnlyList<string> Exclusive => exclusive;

        public void SetTileSet(TileSet set)
        {
            tileSet = set;
            NotifyChanged();
        }

        /// <summary>The labels of a tile, or null if it has none yet.</summary>
        public TileSockets GetSockets(TileDefinition tile) => sockets.Find(s => s.tile == tile && tile != null);

        public void SetLabel(TileDefinition tile, Direction face, string label)
        {
            TileSockets entry = GetSockets(tile);
            if (entry == null)
            {
                entry = new TileSockets { tile = tile };
                sockets.Add(entry);
            }
            entry[face] = label?.Trim() ?? string.Empty;
            NotifyChanged();
        }

        /// <summary>Forgets the labels of tiles that aren't in the tile set anymore. Returns how many were removed.</summary>
        public int RemoveUnusedSockets()
        {
            int removed = sockets.RemoveAll(s => s.tile == null || tileSet == null || !tileSet.Contains(s.tile));
            if (removed > 0)
            {
                NotifyChanged();
            }
            return removed;
        }

        public void AddCompatible(string a, string b)
        {
            compatible.Add(new SocketPair(a, b));
            NotifyChanged();
        }

        public void SetCompatible(int index, string a, string b)
        {
            compatible[index] = new SocketPair(a, b);
            NotifyChanged();
        }

        public void RemoveCompatible(int index)
        {
            compatible.RemoveAt(index);
            NotifyChanged();
        }

        public void AddExclusive(string part)
        {
            exclusive.Add(part);
            NotifyChanged();
        }

        public void SetExclusive(int index, string part)
        {
            exclusive[index] = part;
            NotifyChanged();
        }

        public void RemoveExclusive(int index)
        {
            exclusive.RemoveAt(index);
            NotifyChanged();
        }

        public override IRuleSource CreateRules()
        {
            var rules = new SocketRules();
            for (int slot = 0; tileSet != null && slot < tileSet.Count; slot++)
            {
                TileDefinition tile = tileSet[slot];
                rules.faceLabels.Add(tile != null ? GetSockets(tile)?.ToArray() : null);
            }
            foreach (SocketPair pair in compatible)
            {
                rules.compatible.Add((pair.a, pair.b));
            }
            rules.exclusive.AddRange(exclusive);
            rules.overrides.AddRange(Overrides);
            return rules;
        }
    }
}
