namespace Wfc
{
    /// <summary>One tile as seen by rule derivation. Its index in the tile list is its tile index.</summary>
    public struct TileSpec
    {
        /// <summary>Stable id (the definition asset's GUID), used for override keys. Empty for an unused slot.</summary>
        public string id;
        public string name;
        public OrientationSet allowed;
        /// <summary>Geometric symmetry of the model. Only the part inside <see cref="allowed"/> is used.</summary>
        public OrientationSet symmetry;
        public float baseWeight;
        /// <summary>Tiles without a model, such as air. Used for diagnostics only.</summary>
        public bool isInvisible;

        /// <summary>False for an empty slot in a tile list; such tiles never get variants.</summary>
        public bool IsDefined => !string.IsNullOrEmpty(id);
    }
}
