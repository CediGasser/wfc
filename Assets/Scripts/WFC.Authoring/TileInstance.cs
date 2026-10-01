using UnityEngine;

namespace Wfc.Authoring
{
    /// <summary>
    /// A placed tile in a ruleset sample. Its transform relative to the <see cref="RulesetAuthoring"/> root is its
    /// cell (integer position) and orientation; everything else comes from the definition. The model is shown as a
    /// hidden child that isn't saved with the scene, so changing a definition's model updates every instance.
    /// </summary>
    [ExecuteAlways, SelectionBase, DisallowMultipleComponent]
    [AddComponentMenu("WFC/Tile Instance")]
    public sealed class TileInstance : MonoBehaviour
    {
        private const HideFlags VisualFlags = HideFlags.HideAndDontSave;

        [SerializeField] private TileDefinition definition;

        public TileDefinition Definition
        {
            get => definition;
            set
            {
                definition = value;
                RebuildVisual();
            }
        }

        public RulesetAuthoring Root => GetComponentInParent<RulesetAuthoring>(true);

        /// <summary>This instance's transform expressed in its root's grid space (identity scale for snapped tiles).</summary>
        public Matrix4x4 GetMatrixInRoot(RulesetAuthoring root)
        {
            return root.transform.worldToLocalMatrix * transform.localToWorldMatrix;
        }

        public void RebuildVisual()
        {
            DestroyVisual();
            if (definition == null || definition.Model == null)
            {
                return;
            }

            GameObject visual = Instantiate(definition.Model, transform, false);
            visual.name = $"{definition.Model.name} (visual)";
            foreach (Transform part in visual.GetComponentsInChildren<Transform>(true))
            {
                part.gameObject.hideFlags = VisualFlags;
            }
#if UNITY_EDITOR
            // Clicks go to the instance (through its gizmo), never to the hidden model.
            UnityEditor.SceneVisibilityManager.instance.DisablePicking(visual, true);
#endif
        }

        private void OnEnable()
        {
            TileDefinition.Changed += OnDefinitionChanged;
            RebuildVisual();
        }

        private void OnDisable()
        {
            TileDefinition.Changed -= OnDefinitionChanged;
            DestroyVisual();
        }

        private void OnValidate()
        {
            RebuildDelayed();
        }

        private void OnDefinitionChanged(TileDefinition changed)
        {
            if (changed == definition)
            {
                RebuildDelayed();
            }
        }

        private void RebuildDelayed()
        {
#if UNITY_EDITOR
            // Objects can't be created or destroyed inside OnValidate, so defer to the next editor update.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled)
                {
                    RebuildVisual();
                }
            };
#endif
        }

        private void DestroyVisual()
        {
            // Removes every non-saved child, including visuals left over from duplication or domain reloads.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if ((child.hideFlags & HideFlags.DontSave) != 0)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(child);
                    }
                    else
                    {
                        DestroyImmediate(child);
                    }
                }
            }
        }
    }
}
