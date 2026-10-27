using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wfc.Authoring
{
    /// <summary>
    /// A component that provides tiles for the cells of the <see cref="Chunk"/> on its GameObject, such as the chunk's
    /// own saved cells or a generator's current result. <see cref="ChunkView"/> shows the layers stacked: per cell, the
    /// first visible layer (lowest <see cref="LayerOrder"/>) that has a tile there wins. Every layer covers the chunk's
    /// cells, indexed like <see cref="ChunkData"/>, with tiles of the chunk's tile set.
    /// </summary>
    public interface IChunkLayer
    {
        /// <summary>Lower orders are shown on top. The chunk's own cells are 0.</summary>
        int LayerOrder { get; }

        bool IsLayerVisible { get; }

        /// <summary>The tile this layer has in a cell, or <see cref="ChunkData.Empty"/>.</summary>
        int GetTile(int cell, out Orientation orientation);
    }

    /// <summary>Finds and combines the layers on a GameObject, and tells views when a layer changed.</summary>
    public static class ChunkLayers
    {
        /// <summary>Raised when a layer's tiles or visibility changed: the layer and the cell, or <see cref="Chunk.AllCells"/>.</summary>
        public static event Action<Component, int> Changed;

        /// <summary>Layers call this after changing; views on the same GameObject update.</summary>
        public static void Notify(Component layer, int cell) => Changed?.Invoke(layer, cell);

        /// <summary>The layers on a GameObject, topmost first.</summary>
        public static List<IChunkLayer> GetLayers(GameObject gameObject)
        {
            var layers = new List<IChunkLayer>(gameObject.GetComponents<IChunkLayer>());
            layers.Sort((a, b) => a.LayerOrder.CompareTo(b.LayerOrder));
            return layers;
        }

        /// <summary>
        /// The tile shown in a cell: the one of the first visible layer that has one, or <see cref="ChunkData.Empty"/>.
        /// <paramref name="from"/> is that layer, or null.
        /// </summary>
        public static int GetShownTile(IReadOnlyList<IChunkLayer> layers, int cell, out Orientation orientation, out IChunkLayer from)
        {
            foreach (IChunkLayer layer in layers)
            {
                // Destroyed components compare equal to null only through UnityEngine.Object.
                if (layer is UnityEngine.Object unityObject && unityObject == null || !layer.IsLayerVisible)
                {
                    continue;
                }
                int tile = layer.GetTile(cell, out orientation);
                if (tile >= 0)
                {
                    from = layer;
                    return tile;
                }
            }
            orientation = Orientation.Identity;
            from = null;
            return ChunkData.Empty;
        }

        /// <inheritdoc cref="GetShownTile(IReadOnlyList{IChunkLayer}, int, out Orientation, out IChunkLayer)"/>
        public static int GetShownTile(GameObject gameObject, int cell, out Orientation orientation, out IChunkLayer from)
        {
            return GetShownTile(GetLayers(gameObject), cell, out orientation, out from);
        }
    }
}
