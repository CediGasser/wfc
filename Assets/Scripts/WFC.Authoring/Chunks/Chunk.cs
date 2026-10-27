using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Wfc.Authoring
{
    /// <summary>
    /// A box of tiles: a size, the <see cref="TileSet"/> its cells refer to and, per cell, a tile in an orientation or
    /// nothing. The same component holds a ruleset sample, the area a generator fills (painted cells are kept) or any
    /// other arrangement. Its transform is the grid: cell (x, y, z) is centred at local position (x, y, z).
    /// Its cells are saved with the scene and form the topmost layer (<see cref="IChunkLayer"/>); other components,
    /// such as a generator's result, add layers underneath. The chunk only holds data; <see cref="ChunkView"/> shows
    /// the layers.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("WFC/Chunk")]
    public sealed class Chunk : MonoBehaviour, IChunkLayer
    {
        /// <summary>Passed to the change events when every cell may have changed.</summary>
        public const int AllCells = -1;

        [SerializeField] private TileSet tileSet;
        [SerializeField] private ChunkData cells = new ChunkData(new int3(8, 4, 8));

        /// <summary>Raised when cells change, with the cell index or <see cref="AllCells"/>.</summary>
        public event Action<int> Changed;

        /// <summary>Raised when the cells of any chunk change, e.g. for editor tooling that analyses samples.</summary>
        public static event Action<Chunk> AnyChanged;

        public TileSet TileSet
        {
            get => tileSet;
            set
            {
                tileSet = value;
                NotifyAll();
            }
        }

        public int3 Size => cells.Size;

        public int CellCount => cells.CellCount;

        /// <summary>The saved cells. Read them freely, but change them through the chunk so listeners are notified.</summary>
        public ChunkData Cells => cells;

        public int LayerOrder => 0;

        public bool IsLayerVisible => true;

        public int GetTile(int cell, out Orientation orientation)
        {
            orientation = cells.GetOrientation(cell);
            return cells.GetTile(cell);
        }

        /// <summary>The definition of a tile index, or null for empty cells, empty slots and chunks without a tile set.</summary>
        public TileDefinition GetDefinition(int tile) => tileSet != null ? tileSet[tile] : null;

        public void SetCell(int cell, int tile, Orientation orientation)
        {
            cells.Set(cell, tile, orientation);
            Notify(cell);
        }

        public void SetCell(int3 cell, int tile, Orientation orientation) => SetCell(cells.ToIndex(cell), tile, orientation);

        public void ClearCell(int cell) => SetCell(cell, ChunkData.Empty, Orientation.Identity);

        /// <summary>Empties every authored cell.</summary>
        public void ClearCells()
        {
            cells.Clear();
            NotifyAll();
        }

        /// <summary>Replaces size and cells with a copy of <paramref name="data"/>; the tiles must refer to this chunk's tile set.</summary>
        public void SetCells(ChunkData data)
        {
            cells = data.Clone();
            NotifyAll();
        }

        /// <summary>Changes the size; cell c moves to c + <paramref name="offset"/>, cells outside the new size are dropped.</summary>
        public void Resize(int3 size, int3 offset)
        {
            cells = cells.Resized(size, offset);
            NotifyAll();
        }

        /// <summary>Stores a copy of the cells in <paramref name="asset"/>.</summary>
        public void SaveTo(ChunkAsset asset) => asset.Set(tileSet, cells);

        /// <summary>
        /// Replaces size and cells with those of <paramref name="asset"/>. A chunk without a tile set takes the asset's;
        /// otherwise tiles are matched by definition. If this chunk's tile set lacks some of them, nothing changes,
        /// they're added to <paramref name="missing"/> and false is returned.
        /// </summary>
        public bool LoadFrom(ChunkAsset asset, List<TileDefinition> missing = null)
        {
            if (tileSet == null)
            {
                tileSet = asset.TileSet;
            }
            if (!TryRemap(asset.Cells, asset.TileSet, tileSet, out ChunkData remapped, missing))
            {
                return false;
            }
            SetCells(remapped);
            return true;
        }

        /// <summary>
        /// A copy of <paramref name="source"/> whose tiles refer to <paramref name="to"/> instead of
        /// <paramref name="from"/>, matched by definition. Fails if <paramref name="to"/> lacks a used tile (listed in
        /// <paramref name="missing"/>); cells in empty slots of <paramref name="from"/> become empty.
        /// </summary>
        public static bool TryRemap(ChunkData source, TileSet from, TileSet to, out ChunkData result, List<TileDefinition> missing = null)
        {
            result = source.Clone();
            if (from == to)
            {
                return true;
            }
            bool ok = true;
            var map = new Dictionary<int, int>();
            for (int cell = 0; cell < result.CellCount; cell++)
            {
                int tile = result.GetTile(cell);
                if (tile < 0)
                {
                    continue;
                }
                if (!map.TryGetValue(tile, out int target))
                {
                    TileDefinition definition = from != null ? from[tile] : null;
                    target = definition != null && to != null ? to.IndexOf(definition) : ChunkData.Empty;
                    if (definition != null && target < 0)
                    {
                        ok = false;
                        missing?.Add(definition);
                    }
                    map.Add(tile, target);
                }
                result.Set(cell, target, result.GetOrientation(cell));
            }
            if (!ok)
            {
                result = null;
            }
            return ok;
        }

        /// <summary>World matrix of a cell with a tile in the given orientation, e.g. for drawing previews.</summary>
        public Matrix4x4 CellToWorld(int3 cell, Orientation orientation)
        {
            return transform.localToWorldMatrix * Matrix4x4.Translate(cell.ToVector3()) * orientation.ToMatrix4x4();
        }

        private void Notify(int cell)
        {
            Changed?.Invoke(cell);
            AnyChanged?.Invoke(this);
            ChunkLayers.Notify(this, cell);
        }

        private void NotifyAll() => Notify(AllCells);

        private void OnValidate()
        {
            cells ??= new ChunkData();
            cells.Validate();
#if UNITY_EDITOR
            // Inspector edits and Undo arrive here. Listeners may create or destroy objects, which isn't allowed
            // during OnValidate, so notify them afterwards.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null)
                {
                    NotifyAll();
                }
            };
#endif
        }
    }
}
