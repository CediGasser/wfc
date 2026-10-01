using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>
    /// Turns the samples under a <see cref="RulesetAuthoring"/> root into a derivation input, derives the rules and
    /// writes them into the root's <see cref="Ruleset"/> asset.
    /// </summary>
    public static class RulesetBaker
    {
        public sealed class Analysis
        {
            public RulesetAuthoring root;
            public DerivationInput input;
            public DerivationResult result;
            /// <summary>Tile definitions in tile-index order.</summary>
            public List<TileDefinition> tiles = new List<TileDefinition>();
            /// <summary>Instances in source-id order (the index used by diagnostics and observed pairs).</summary>
            public List<TileInstance> instances = new List<TileInstance>();
            /// <summary>Instances without a definition; they take no part in derivation.</summary>
            public List<TileInstance> unassigned = new List<TileInstance>();

            public RulesetData Data => result.data;

            public string InputHash => result.inputHash;

            public bool IsBakedAssetUpToDate => root.Target != null && root.Target.SourceHash == InputHash;
        }

        public static Analysis Analyze(RulesetAuthoring root, bool detectSymmetry = true)
        {
            var analysis = new Analysis { root = root, input = new DerivationInput() };
            var extraDiagnostics = new List<RuleDiagnostic>();
            var tileIndex = new Dictionary<TileDefinition, int>();

            TileInstance[] all = root.GetComponentsInChildren<TileInstance>(false);
            foreach (TileDefinition definition in all.Select(i => i.Definition).Where(d => d != null).Distinct()
                         .OrderBy(d => AssetDatabase.GetAssetPath(d)).ThenBy(d => d.name))
            {
                if (detectSymmetry)
                {
                    MeshSymmetryDetector.UpdateDefinition(definition);
                }
                tileIndex.Add(definition, analysis.tiles.Count);
                analysis.tiles.Add(definition);
                analysis.input.tiles.Add(new TileSpec
                {
                    id = GetStableId(definition),
                    name = definition.name,
                    allowed = definition.AllowedOrientations,
                    symmetry = definition.EffectiveSymmetry,
                    baseWeight = definition.BaseWeight,
                    isEmpty = definition.IsEmpty,
                });
            }

            foreach (TileInstance instance in all)
            {
                if (instance.Definition == null)
                {
                    analysis.unassigned.Add(instance);
                    continue;
                }
                GridPlacement.Placement placement = GridPlacement.Read(instance.transform, root.transform);
                int sourceId = analysis.instances.Count;
                analysis.instances.Add(instance);
                analysis.input.instances.Add(new SampleInstance
                {
                    tile = tileIndex[instance.Definition],
                    cell = placement.Cell,
                    orientation = placement.Orientation,
                });
                if (!placement.IsSnapped)
                {
                    extraDiagnostics.Add(new RuleDiagnostic(DiagnosticSeverity.Warning, DiagnosticCode.UnsnappedInstance,
                        $"{instance.name} isn't aligned to the grid. It's treated as cell {placement.Cell}, orientation {placement.Orientation}.",
                        tileIndex[instance.Definition], sourceId));
                }
            }
            if (analysis.unassigned.Count > 0)
            {
                extraDiagnostics.Add(new RuleDiagnostic(DiagnosticSeverity.Warning, DiagnosticCode.MissingDefinition,
                    $"{analysis.unassigned.Count} tile instance(s) have no definition and are ignored: {string.Join(", ", analysis.unassigned.Select(i => i.name))}."));
            }

            analysis.input.weightSource = root.WeightSource;
            analysis.input.overrides.AddRange(root.Overrides);
            analysis.result = RuleDerivation.Derive(analysis.input);
            analysis.result.data.diagnostics = extraDiagnostics.Concat(analysis.result.data.diagnostics).ToArray();
            return analysis;
        }

        /// <summary>Derives the rules and writes them into the root's ruleset asset, creating it next to the scene if needed.</summary>
        public static Ruleset Bake(RulesetAuthoring root)
        {
            Analysis analysis = Analyze(root);
            Ruleset asset = root.Target;
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<Ruleset>();
                AssetDatabase.CreateAsset(asset, GetDefaultAssetPath(root));
                Undo.RecordObject(root, "Bake WFC Ruleset");
                root.Target = asset;
                EditorUtility.SetDirty(root);
            }

            Undo.RecordObject(asset, "Bake WFC Ruleset");
            asset.Set(analysis.tiles.ToArray(), analysis.Data, analysis.InputHash);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            RulesetLiveAnalysis.MarkDirty();
            return asset;
        }

        /// <summary>The definition's asset GUID, or its name for definitions that aren't saved as assets (tests).</summary>
        public static string GetStableId(TileDefinition definition)
        {
            string path = AssetDatabase.GetAssetPath(definition);
            return string.IsNullOrEmpty(path) ? $"unsaved:{definition.name}" : AssetDatabase.AssetPathToGUID(path);
        }

        private static string GetDefaultAssetPath(RulesetAuthoring root)
        {
            string scenePath = root.gameObject.scene.path;
            string folder = string.IsNullOrEmpty(scenePath) ? "Assets" : Path.GetDirectoryName(scenePath)?.Replace('\\', '/');
            return AssetDatabase.GenerateUniqueAssetPath($"{folder}/{root.gameObject.name}.asset");
        }
    }
}
