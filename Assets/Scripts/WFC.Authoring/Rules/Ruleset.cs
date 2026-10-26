using UnityEngine;

namespace Wfc.Authoring
{
    /// <summary>
    /// Baked, solver-ready rules produced by a <see cref="RuleSource"/>. Tile indices in <see cref="Data"/> are slots of
    /// <see cref="TileSet"/>.
    /// </summary>
    public sealed class Ruleset : ScriptableObject
    {
        [SerializeField] private TileSet tileSet;
        [SerializeField] private RulesetData data = new RulesetData();
        [SerializeField] private string sourceHash;

        /// <summary>The tile set whose slots the tile indices in <see cref="Data"/> refer to.</summary>
        public TileSet TileSet => tileSet;

        public RulesetData Data => data;

        /// <summary>Hash of the source and settings this ruleset was baked from.</summary>
        public string SourceHash => sourceHash;

        public void Set(TileSet bakedTileSet, RulesetData bakedData, string hash)
        {
            tileSet = bakedTileSet;
            data = bakedData;
            sourceHash = hash;
        }
    }
}
