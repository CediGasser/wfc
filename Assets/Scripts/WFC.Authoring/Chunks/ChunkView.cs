using Unity.Mathematics;
using UnityEngine;

namespace Wfc.Authoring
{
    /// <summary>
    /// Shows a <see cref="Chunk"/>: an instance of the tile's model in every non-empty cell (the authored tile, else
    /// the generated one), kept in sync as cells change. The instances are hidden and never saved, so editing a tile
    /// definition updates every chunk. Separate from the chunk, so chunks can also exist without any visuals.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Chunk))]
    [AddComponentMenu("WFC/Chunk View")]
    public sealed class ChunkView : MonoBehaviour
    {
        private const string ContainerName = "Tiles (not saved)";
        private const HideFlags VisualFlags = HideFlags.HideAndDontSave;

        [Tooltip("Show the tiles a generator produced for the empty cells.")]
        [SerializeField] private bool showGenerated = true;
        [Tooltip("Draw a wireframe around every authored cell in its tile's gizmo colour.")]
        [SerializeField] private bool showCellGizmos = true;

        private Chunk chunk;
        private Transform container;
        private GameObject[] shown;
        // Per cell: tile << 6 | orientation of the shown instance, or -1 if nothing is shown.
        private int[] shownKey;

        public Chunk Chunk => chunk != null ? chunk : chunk = GetComponent<Chunk>();

        public bool ShowGenerated
        {
            get => showGenerated;
            set
            {
                showGenerated = value;
                SyncAll();
            }
        }

        public bool ShowCellGizmos => showCellGizmos;

        /// <summary>The hidden object holding the model instances, or null while nothing is shown.</summary>
        public Transform Container => container;

        /// <summary>Throws away every instance and creates them again.</summary>
        public void Rebuild()
        {
            DestroyVisuals();
            SyncAll();
        }

        private void OnEnable()
        {
            chunk = GetComponent<Chunk>();
            DestroyVisuals();
            chunk.Changed += OnCellChanged;
            chunk.GeneratedChanged += OnCellChanged;
            TileDefinition.Changed += OnDefinitionChanged;
            TileSet.Changed += OnTileSetChanged;
            SyncAll();
        }

        private void OnDisable()
        {
            if (chunk != null)
            {
                chunk.Changed -= OnCellChanged;
                chunk.GeneratedChanged -= OnCellChanged;
            }
            TileDefinition.Changed -= OnDefinitionChanged;
            TileSet.Changed -= OnTileSetChanged;
            DestroyVisuals();
        }

        private void OnValidate() => RebuildDelayed();

        private void OnCellChanged(int cell)
        {
            if (cell == Chunk.AllCells || shown == null || shown.Length != Chunk.CellCount)
            {
                SyncAll();
            }
            else
            {
                Sync(cell);
            }
        }

        private void OnDefinitionChanged(TileDefinition definition)
        {
            if (Chunk.TileSet != null && Chunk.TileSet.Contains(definition))
            {
                RebuildDelayed();
            }
        }

        private void OnTileSetChanged(TileSet changed)
        {
            if (changed == Chunk.TileSet)
            {
                RebuildDelayed();
            }
        }

        private void RebuildDelayed()
        {
#if UNITY_EDITOR
            // Called from OnValidate, where objects can't be created or destroyed.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled)
                {
                    Rebuild();
                }
            };
#endif
        }

        private void SyncAll()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }
            int count = Chunk.CellCount;
            if (shown == null || shown.Length != count)
            {
                DestroyVisuals();
                shown = new GameObject[count];
                shownKey = new int[count];
                System.Array.Fill(shownKey, -1);
            }
            for (int cell = 0; cell < count; cell++)
            {
                Sync(cell);
            }
        }

        private void Sync(int cell)
        {
            if (shown == null)
            {
                return;
            }
            int tile = Chunk.GetShownTile(cell, showGenerated, out Orientation orientation, out _);
            TileDefinition definition = Chunk.GetDefinition(tile);
            int key = definition != null && definition.Model != null ? (tile << 6) | orientation.Index : -1;
            if (key == shownKey[cell])
            {
                return;
            }
            DestroyObject(shown[cell]);
            shown[cell] = null;
            shownKey[cell] = key;
            if (key >= 0)
            {
                shown[cell] = CreateInstance(definition, Chunk.Cells.ToCell(cell), orientation);
            }
        }

        private GameObject CreateInstance(TileDefinition definition, int3 cell, Orientation orientation)
        {
            var wrapper = new GameObject($"{definition.name} {cell}") { hideFlags = VisualFlags };
            wrapper.transform.SetParent(GetOrCreateContainer(), false);
            orientation.ToRotationScale(out Quaternion rotation, out Vector3 scale);
            wrapper.transform.localPosition = cell.ToVector3();
            wrapper.transform.localRotation = rotation;
            wrapper.transform.localScale = scale;

            GameObject model = Instantiate(definition.Model, wrapper.transform, false);
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                part.gameObject.hideFlags = VisualFlags;
            }
#if UNITY_EDITOR
            // Clicks go to the chunk (through its cell gizmos), never to the hidden models.
            UnityEditor.SceneVisibilityManager.instance.DisablePicking(wrapper, true);
#endif
            return wrapper;
        }

        private Transform GetOrCreateContainer()
        {
            if (container == null)
            {
                container = new GameObject(ContainerName) { hideFlags = VisualFlags }.transform;
                container.SetParent(transform, false);
            }
            return container;
        }

        /// <summary>
        /// Removes the container, including copies left over from duplicating the GameObject or from a domain reload,
        /// and forgets what was shown.
        /// </summary>
        private void DestroyVisuals()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (child.name == ContainerName && (child.hideFlags & HideFlags.DontSave) != 0)
                {
                    DestroyObject(child);
                }
            }
            container = null;
            shown = null;
            shownKey = null;
        }

        private static void DestroyObject(GameObject target)
        {
            if (target == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
