using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Wfc.Authoring
{
    /// <summary>
    /// Shows a <see cref="Chunk"/> and every other <see cref="IChunkLayer"/> on its GameObject: per cell, an instance
    /// of the model of the topmost visible layer's tile, kept in sync as layers change. Disabling a layer component
    /// (e.g. the generator) hides its tiles. The instances are hidden and never saved, so editing a tile definition
    /// updates every chunk. Rendering lives here rather than in the layers, so all of them share one renderer that
    /// can later be swapped, e.g. for instanced rendering of large worlds.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Chunk))]
    [AddComponentMenu("WFC/Chunk View")]
    public sealed class ChunkView : MonoBehaviour
    {
        private const string ContainerName = "Tiles (not saved)";
        private const HideFlags VisualFlags = HideFlags.HideAndDontSave;

        private Chunk chunk;
        private List<IChunkLayer> layers = new List<IChunkLayer>();
        private Transform container;
        private GameObject[] shown;
        // Per cell: tile << 6 | orientation of the shown instance, or -1 if nothing is shown.
        private int[] shownKey;

        public Chunk Chunk => chunk != null ? chunk : chunk = GetComponent<Chunk>();

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
            ChunkLayers.Changed += OnLayerChanged;
            TileDefinition.Changed += OnDefinitionChanged;
            TileSet.Changed += OnTileSetChanged;
            PropSet.Changed += OnPropSetChanged;
            SyncAll();
        }

        private void OnDisable()
        {
            ChunkLayers.Changed -= OnLayerChanged;
            TileDefinition.Changed -= OnDefinitionChanged;
            TileSet.Changed -= OnTileSetChanged;
            PropSet.Changed -= OnPropSetChanged;
            DestroyVisuals();
        }

        private void OnLayerChanged(Component layer, int cell)
        {
            if (layer == null || layer.gameObject != gameObject)
            {
                return;
            }
            if (cell == Chunk.AllCells || shown == null || shown.Length != Chunk.CellCount || !layers.Contains(layer as IChunkLayer))
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

        private void OnPropSetChanged(PropSet changed)
        {
            TileSet tileSet = Chunk.TileSet;
            if (tileSet == null)
            {
                return;
            }
            foreach (TileDefinition definition in tileSet.Tiles)
            {
                if (definition != null && definition.Props == changed)
                {
                    RebuildDelayed();
                    return;
                }
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
            // Layers come and go with their components, so look them up again.
            layers = ChunkLayers.GetLayers(gameObject);
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
            int tile = ChunkLayers.GetShownTile(layers, cell, out Orientation orientation, out _);
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
            if (definition.Props != null)
            {
                PlaceProps(definition.Props, model.transform, wrapper.transform);
            }
            foreach (Transform part in wrapper.GetComponentsInChildren<Transform>(true))
            {
                part.gameObject.hideFlags = VisualFlags;
            }
#if UNITY_EDITOR
            // Clicks go to the chunk (through its cell gizmos), never to the hidden models.
            UnityEditor.SceneVisibilityManager.instance.DisablePicking(wrapper, true);
#endif
            return wrapper;
        }

        /// <summary>
        /// Fills the model's sockets with props. Each socket is seeded by its world position, so a cell gets the same
        /// props whenever it's shown again, independent of the chunk layout.
        /// </summary>
        private static void PlaceProps(PropSet propSet, Transform model, Transform wrapper)
        {
            foreach (Transform socket in model.GetComponentsInChildren<Transform>(true))
            {
                if (!propSet.IsSocket(socket))
                {
                    continue;
                }
                var key = (int3)math.round((float3)socket.position * 100f);
                if (!propSet.Pick(math.hash(key), out GameObject prefab, out float yaw, out float scale))
                {
                    continue;
                }
                GameObject prop = Instantiate(prefab, wrapper, false);
                prop.transform.localPosition = wrapper.InverseTransformPoint(socket.position);
                prop.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                prop.transform.localScale = Vector3.one * scale;
            }
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
