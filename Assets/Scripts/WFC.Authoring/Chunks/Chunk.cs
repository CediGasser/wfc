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
    /// Besides the authored cells, which are saved with the scene, a chunk can hold a generated layer that is never
    /// saved: what a generator produced. The chunk only holds data; <see cref="ChunkView"/> shows it.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("WFC/Chunk")]
    public sealed class Chunk : MonoBehaviour
    {
        /// <summary>Passed to the change events when every cell may have changed.</summary>
        public const int AllCells = -1;

        [SerializeField] private TileSet tileSet;
        [SerializeField] private ChunkData cells = new ChunkData(new int3(8, 4, 8));

        [NonSerialized] private ChunkData generated;

        /// <summary>Raised when authored cells change, with the cell index or <see cref="AllCells"/>.</summary>
        public event Action<int> Changed;

        /// <summary>Raised when generated cells change, with the cell index or <see cref="AllCells"/>.</summary>
        public event Action<int> GeneratedChanged;

        /// <summary>Raised when the authored cells of any chunk change, e.g. for editor tooling that analyses samples.</summary>
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

        /// <summary>The authored cells. Read them freely, but change them through the chunk so listeners are notified.</summary>
        public ChunkData Cells => cells;

        /// <summary>The generated layer, or null if nothing was generated since it was last cleared.</summary>
        public ChunkData Generated => generated;

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
            bool resized = !data.Size.Equals(cells.Size);
            cells = data.Clone();
            if (resized)
            {
                ClearGenerated();
            }
            NotifyAll();
        }

        /// <summary>Changes the size; cell c moves to c + <paramref name="offset"/>, cells outside the new size are dropped.</summary>
        public void Resize(int3 size, int3 offset)
        {
            cells = cells.Resized(size, offset);
            ClearGenerated();
            NotifyAll();
        }

        /// <summary>
        /// The tile a cell shows: the authored one if there is one, else the generated one (if
        /// <paramref name="includeGenerated"/>), else <see cref="ChunkData.Empty"/>.
        /// </summary>
        public int GetShownTile(int cell, bool includeGenerated, out Orientation orientation, out bool isGenerated)
        {
            isGenerated = false;
            if (!cells.IsEmpty(cell))
            {
                orientation = cells.GetOrientation(cell);
                return cells.GetTile(cell);
            }
            if (includeGenerated && generated != null && !generated.IsEmpty(cell))
            {
                isGenerated = true;
                orientation = generated.GetOrientation(cell);
                return generated.GetTile(cell);
            }
            orientation = Orientation.Identity;
            return ChunkData.Empty;
        }

        /// <summary>The generated layer, created empty if there is none. Write into it, then call <see cref="NotifyGenerated(int)"/>.</summary>
        public ChunkData GetOrCreateGenerated()
        {
            if (generated == null || !generated.Size.Equals(cells.Size))
            {
                generated = new ChunkData(cells.Size);
            }
            return generated;
        }

        public void NotifyGenerated(int cell) => GeneratedChanged?.Invoke(cell);

        public void NotifyGenerated(IEnumerable<int> changedCells)
        {
            foreach (int cell in changedCells)
            {
                GeneratedChanged?.Invoke(cell);
            }
        }

        public void ClearGenerated()
        {
            if (generated != null)
            {
                generated = null;
                GeneratedChanged?.Invoke(AllCells);
            }
        }

        /// <summary>
        /// Turns the generated tiles into authored ones: copies them into the empty authored cells and clears the
        /// generated layer. Returns the number of cells filled.
        /// </summary>
        public int KeepGenerated()
        {
            if (generated == null)
            {
                return 0;
            }
            int kept = 0;
            for (int cell = 0; cell < cells.CellCount; cell++)
            {
                if (cells.IsEmpty(cell) && !generated.IsEmpty(cell))
                {
                    cells.Set(cell, generated.GetTile(cell), generated.GetOrientation(cell));
                    kept++;
                }
            }
            generated = null;
            NotifyAll();
            GeneratedChanged?.Invoke(AllCells);
            return kept;
        }

        /// <summary>Stores a copy of the authored cells in <paramref name="asset"/>.</summary>
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
        }

        private void NotifyAll() => Notify(AllCells);

        private void OnValidate()
        {
            cells ??= new ChunkData();
            cells.Validate();
            if (generated != null && !generated.Size.Equals(cells.Size))
            {
                generated = null;
            }
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
