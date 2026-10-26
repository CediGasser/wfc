using System.IO;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>Derives the rules of a <see cref="RuleSource"/> and writes them into its <see cref="Ruleset"/> asset.</summary>
    public static class RulesetBaker
    {
        public static RuleSourceAnalysis Analyze(RuleSource source, bool detectSymmetry = true)
        {
            TileSet tileSet = source.TileSet;
            var analysis = new RuleSourceAnalysis
            {
                source = source,
                tileSet = tileSet,
                specs = TileSpecs.From(tileSet, detectSymmetry),
                sampleSize = source is SampleRuleSource sample && sample.Chunk != null ? sample.Chunk.Size : int3.zero,
            };
            analysis.result = source.CreateRules().Derive(analysis.specs);
            if (tileSet == null)
            {
                analysis.result.data.diagnostics = analysis.result.data.diagnostics.Prepend(new RuleDiagnostic(DiagnosticSeverity.Error,
                    DiagnosticCode.MissingTile, "No tile set is assigned, so there are no tiles to derive rules for.")).ToArray();
            }
            return analysis;
        }

        /// <summary>Derives the rules and writes them into the source's ruleset asset, creating it next to the scene if needed.</summary>
        public static Ruleset Bake(RuleSource source)
        {
            RuleSourceAnalysis analysis = Analyze(source);
            Ruleset asset = source.Ruleset;
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<Ruleset>();
                AssetDatabase.CreateAsset(asset, GetDefaultAssetPath(source));
                Undo.RecordObject(source, "Bake WFC Ruleset");
                source.Ruleset = asset;
                EditorUtility.SetDirty(source);
            }

            Undo.RecordObject(asset, "Bake WFC Ruleset");
            asset.Set(analysis.tileSet, analysis.Data, analysis.InputHash);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            LiveRuleAnalysis.MarkDirty();
            return asset;
        }

        private static string GetDefaultAssetPath(RuleSource source)
        {
            string scenePath = source.gameObject.scene.path;
            string folder = string.IsNullOrEmpty(scenePath) ? "Assets" : Path.GetDirectoryName(scenePath)?.Replace('\\', '/');
            return AssetDatabase.GenerateUniqueAssetPath($"{folder}/{source.gameObject.name}.asset");
        }
    }
}
