using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wfc.Authoring
{
    /// <summary>
    /// Baked, solver-ready rules produced from a <see cref="RulesetAuthoring"/> root. Tile indices in
    /// <see cref="Data"/> refer to <see cref="Tiles"/>.
    /// </summary>
    public sealed class Ruleset : ScriptableObject
    {
        [SerializeField] private TileDefinition[] tiles = Array.Empty<TileDefinition>();
        [SerializeField] private RulesetData data = new RulesetData();
        [SerializeField] private string sourceHash;

        public IReadOnlyList<TileDefinition> Tiles => tiles;

        public RulesetData Data => data;

        /// <summary>Hash of the samples and settings this ruleset was baked from.</summary>
        public string SourceHash => sourceHash;

        public void Set(TileDefinition[] bakedTiles, RulesetData bakedData, string hash)
        {
            tiles = bakedTiles;
            data = bakedData;
            sourceHash = hash;
        }
    }
}
