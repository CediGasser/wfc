using System.Collections.Generic;
using UnityEditor;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>Turns tile definitions into the <see cref="TileSpec"/>s rule derivation works on.</summary>
    public static class TileSpecs
    {
        /// <summary>One spec per slot of the tile set, in slot order; empty slots become undefined specs without variants.</summary>
        public static List<TileSpec> From(TileSet set, bool detectSymmetry = true)
        {
            var specs = new List<TileSpec>(set != null ? set.Count : 0);
            for (int slot = 0; set != null && slot < set.Count; slot++)
            {
                TileDefinition definition = set[slot];
                specs.Add(definition != null ? From(definition, detectSymmetry) : new TileSpec { name = $"(empty slot {slot})" });
            }
            return specs;
        }

        /// <summary>The spec of one tile, detecting its symmetry first if the model changed since the last detection.</summary>
        public static TileSpec From(TileDefinition definition, bool detectSymmetry = true)
        {
            if (detectSymmetry)
            {
                MeshSymmetryDetector.UpdateDefinition(definition);
            }
            return new TileSpec
            {
                id = GetStableId(definition),
                name = definition.name,
                allowed = definition.AllowedOrientations,
                symmetry = definition.EffectiveSymmetry,
                baseWeight = definition.BaseWeight,
                isInvisible = !definition.HasModel,
            };
        }

        /// <summary>The definition's asset GUID, or its name for definitions that aren't saved as assets (tests).</summary>
        public static string GetStableId(TileDefinition definition)
        {
            string path = AssetDatabase.GetAssetPath(definition);
            return string.IsNullOrEmpty(path) ? $"unsaved:{definition.name}" : AssetDatabase.AssetPathToGUID(path);
        }
    }
}
