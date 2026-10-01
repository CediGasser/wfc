using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>Scene view panel for the selected face: what the samples allow next to it, and the ghost being previewed.</summary>
    [Overlay(typeof(SceneView), "wfc-face-selection", "WFC Face", true)]
    public sealed class FaceSelectionOverlay : Overlay, ITransientOverlay
    {
        public bool visible => ToolManager.activeToolType == typeof(RulesetSceneTool);

        public override VisualElement CreatePanelContent()
        {
            var container = new IMGUIContainer(OnGUI) { style = { width = 300 } };
            RulesetToolState.Changed += container.MarkDirtyRepaint;
            RulesetLiveAnalysis.Updated += container.MarkDirtyRepaint;
            container.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                RulesetToolState.Changed -= container.MarkDirtyRepaint;
                RulesetLiveAnalysis.Updated -= container.MarkDirtyRepaint;
            });
            return container;
        }

        private static void OnGUI()
        {
            if (RulesetToolState.SelectedFace is not { } face)
            {
                EditorGUILayout.LabelField("Click a face of a tile to see what fits next to it.", EditorStyles.wordWrappedMiniLabel);
                DrawPaletteHint();
                return;
            }

            RulesetAuthoring root = face.Instance.Root;
            RulesetBaker.Analysis analysis = RulesetLiveAnalysis.Get(root);
            GridPlacement.Placement placement = GridPlacement.Read(face.Instance.transform, root.transform);
            Direction localFace = placement.Orientation.Inverse.Apply(face.Direction);
            string tileName = face.Instance.Definition != null ? face.Instance.Definition.name : face.Instance.name;
            EditorGUILayout.LabelField($"{tileName}, face {localFace.ToShortString()}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Cell {placement.Cell}, pointing {face.Direction.ToShortString()} in the grid", EditorStyles.miniLabel);

            List<RulesetToolState.Candidate> candidates = RulesetToolState.GetCandidates(analysis, face);
            if (candidates.Count == 0)
            {
                int source = analysis.instances.IndexOf(face.Instance);
                bool ignored = source < 0 || analysis.result.instanceVariant[source] < 0;
                EditorGUILayout.HelpBox(ignored
                    ? "This tile is ignored by the analysis. See the diagnostics on the ruleset root."
                    : "Nothing was learned next to this face yet. Place a tile here to teach a connection.", MessageType.Info);
                DrawPaletteHint();
                return;
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Allowed next to this face", EditorStyles.miniBoldLabel);
            foreach (var group in candidates.GroupBy(c => c.Definition))
            {
                float weight = group.Max(c => c.Weight);
                EditorGUILayout.LabelField($"  {group.Key.name}", $"{group.Count()} variant(s), weight {weight:0.##}", EditorStyles.miniLabel);
            }

            RulesetToolState.Candidate current = RulesetToolState.GetCurrentCandidate(candidates).Value;
            int index = candidates.FindIndex(c => c.Variant == current.Variant);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Preview {index + 1}/{candidates.Count}: {current.Definition.name} {current.Orientation}", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("◀ [", EditorStyles.miniButtonLeft))
                {
                    RulesetToolState.GhostIndex--;
                }
                if (GUILayout.Button("] ▶", EditorStyles.miniButtonMid))
                {
                    RulesetToolState.GhostIndex++;
                }
                if (GUILayout.Button("Place (Enter)", EditorStyles.miniButtonRight))
                {
                    RulesetSceneTool.PlaceCurrentGhost();
                }
            }
            DrawPaletteHint();
        }

        private static void DrawPaletteHint()
        {
            string palette = RulesetToolState.PaletteTile != null ? RulesetToolState.PaletteTile.name : "none (Window > WFC > Tile Palette)";
            EditorGUILayout.LabelField($"Shift+Click a face to place: {palette}", EditorStyles.wordWrappedMiniLabel);
        }
    }
}
