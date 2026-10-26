using UnityEngine;

namespace Wfc.Authoring
{
    /// <summary>
    /// A tile arrangement stored as an asset: a tile set and cells that refer to it. Chunks save to and load from it,
    /// so arrangements (samples, generated results, painted areas) can move between scenes.
    /// </summary>
    [CreateAssetMenu(menuName = "WFC/Chunk Asset", fileName = "NewChunk", order = 2)]
    public sealed class ChunkAsset : ScriptableObject
    {
        [SerializeField] private TileSet tileSet;
        [SerializeField] private ChunkData cells = new ChunkData();

        public TileSet TileSet => tileSet;

        public ChunkData Cells => cells;

        /// <summary>Stores a copy of <paramref name="data"/>.</summary>
        public void Set(TileSet set, ChunkData data)
        {
            tileSet = set;
            cells = data.Clone();
        }

        private void OnValidate()
        {
            cells ??= new ChunkData();
            cells.Validate();
        }
    }
}
