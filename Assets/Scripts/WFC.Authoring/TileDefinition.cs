using System;
using UnityEngine;

namespace Wfc.Authoring
{
    /// <summary>
    /// Everything a tile is, independent of where it's placed: its model, the orientations it may appear in,
    /// its symmetry and its base weight. Placed copies (<see cref="TileInstance"/>) only reference this asset.
    /// </summary>
    [CreateAssetMenu(menuName = "WFC/Tile Definition", fileName = "NewTile", order = 0)]
    public sealed class TileDefinition : ScriptableObject
    {
        public enum SymmetryMode
        {
            /// <summary>Use the symmetry detected from the model's geometry.</summary>
            Auto,
            /// <summary>Treat every orientation as a distinct variant, even if the model looks identical.</summary>
            Asymmetric,
        }

        [Tooltip("Model shown for this tile. Its pivot is the cell centre and it should fill a 1 m cube. Leave empty for air or other invisible tiles.")]
        [SerializeField] private GameObject model;

        [Tooltip("Orientations this tile may appear in. Choose the full set even for symmetric models; identical-looking variants are merged automatically.")]
        [SerializeField] private OrientationPreset allowedOrientations = OrientationPreset.YawMirror;

        [SerializeField] private ulong customOrientationMask = 1;

        [Tooltip("Auto merges variants that look identical. Asymmetric keeps every orientation distinct.")]
        [SerializeField] private SymmetryMode symmetry = SymmetryMode.Auto;

        [Tooltip("Relative frequency of the tile, spread across all its variants.")]
        [SerializeField, Min(0f)] private float baseWeight = 1f;

        [SerializeField] private Color gizmoColor = new Color(0.45f, 0.75f, 1f, 1f);

        [SerializeField, HideInInspector] private ulong detectedSymmetryMask = 1;
        [SerializeField, HideInInspector] private string detectedSymmetrySource;

        /// <summary>Raised whenever a definition is edited, so placed instances can refresh their visuals.</summary>
        public static event Action<TileDefinition> Changed;

        public GameObject Model => model;

        public bool IsEmpty => model == null;

        public OrientationPreset AllowedPreset => allowedOrientations;

        public OrientationSet AllowedOrientations => OrientationSet.FromPreset(allowedOrientations, customOrientationMask);

        public SymmetryMode Symmetry => symmetry;

        public float BaseWeight => baseWeight;

        public Color GizmoColor => gizmoColor;

        public OrientationSet DetectedSymmetry => new OrientationSet(detectedSymmetryMask).With(Orientation.Identity);

        /// <summary>Identifies the model import the symmetry was detected from, so editor code can tell when it's outdated.</summary>
        public string DetectedSymmetrySource => detectedSymmetrySource;

        /// <summary>The symmetry used for rule derivation.</summary>
        public OrientationSet EffectiveSymmetry
        {
            get
            {
                if (symmetry == SymmetryMode.Asymmetric)
                {
                    return OrientationSet.IdentityOnly;
                }
                return model == null ? OrientationSet.All : DetectedSymmetry;
            }
        }

        public void SetDetectedSymmetry(OrientationSet detected, string source)
        {
            detectedSymmetryMask = detected.With(Orientation.Identity).Mask;
            detectedSymmetrySource = source;
        }

        public void SetAllowedOrientations(OrientationPreset preset, ulong customMask = 1)
        {
            allowedOrientations = preset;
            customOrientationMask = customMask;
            NotifyChanged();
        }

        public void SetModel(GameObject newModel)
        {
            model = newModel;
            NotifyChanged();
        }

        public void SetBaseWeight(float weight) => baseWeight = Mathf.Max(0f, weight);

        public void SetGizmoColor(Color color) => gizmoColor = color;

        public void NotifyChanged() => Changed?.Invoke(this);

        private void OnValidate() => NotifyChanged();
    }
}
