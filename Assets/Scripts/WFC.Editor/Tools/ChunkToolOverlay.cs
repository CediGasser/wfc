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
    /// <summary>Scene view panel of the chunk tool: the selected cell and face, what the rules allow next to it, and the ghost being previewed.</summary>
    [Overlay(typeof(SceneView), "wfc-chunk-tool", "WFC Chunk", true)]
    public sealed class ChunkToolOverlay : Overlay, ITransientOverlay
    {
        public bool visible => ToolManager.activeToolType == typeof(ChunkTool);

        public override VisualElement CreatePanelContent()
        {
            var container = new IMGUIContainer(OnGUI) { style = { width = 300 } };
            ChunkToolState.Changed += container.MarkDirtyRepaint;
            LiveRuleAnalysis.Updated += container.MarkDirtyRepaint;
            container.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                ChunkToolState.Changed -= container.MarkDirtyRepaint;
                LiveRuleAnalysis.Updated -= container.MarkDirtyRepaint;
            });
            return container;
        }

        private static void OnGUI()
        {
            Chunk chunk = ChunkToolState.Chunk;
            if (chunk == null || ChunkToolState.SelectedCells.Count == 0)
            {
                EditorGUILayout.LabelField("Click a tile to select it and see what fits next to the face under the mouse. Ctrl/Cmd+Click adds cells to the selection.", EditorStyles.wordWrappedMiniLabel);
                DrawHints();
                return;
            }

            DrawSelection(chunk);
            if (ChunkToolState.TryGetFace(out int cell, out Direction face))
            {
                DrawCandidates(chunk, cell, face);
            }
            DrawHints();
        }

        private static void DrawSelection(Chunk chunk)
        {
            IReadOnlyList<int> cells = ChunkToolState.SelectedCells;
            List<IChunkLayer> layers = ChunkLayers.GetLayers(chunk.gameObject);
            bool anyGenerated = cells.Any(c => ChunkLayers.GetShownTile(layers, c, out _, out IChunkLayer from) >= 0 && from is not Chunk);
            if (cells.Count > 1)
            {
                EditorGUILayout.LabelField($"{cells.Count} cells selected", EditorStyles.boldLabel);
            }
            else
            {
                int cell = cells[0];
                int tile = ChunkLayers.GetShownTile(layers, cell, out Orientation orientation, out IChunkLayer from);
                bool generated = tile >= 0 && from is not Chunk;
                TileDefinition definition = chunk.GetDefinition(tile);
                string name = tile < 0 ? "Empty" : definition != null ? definition.name : $"Missing tile {tile}";
                EditorGUILayout.LabelField($"{name}{(generated ? " (generated)" : "")}", EditorStyles.boldLabel);
                string faceText = ChunkToolState.TryGetFace(out _, out Direction face) && tile >= 0
                    ? $", face {orientation.Inverse.Apply(face).ToShortString()} (pointing {face.ToShortString()})" : "";
                EditorGUILayout.LabelField($"Cell {chunk.Cells.ToCell(cell)}, {orientation}{faceText}", EditorStyles.wordWrappedMiniLabel);
                if (definition != null && !definition.AllowedOrientations.Contains(orientation))
                {
                    EditorGUILayout.HelpBox($"{definition.name} doesn't allow this orientation, so the cell is ignored.", MessageType.Warning);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (anyGenerated && GUILayout.Button(new GUIContent("Keep", "Turn the selected generated tiles into painted ones"), EditorStyles.miniButtonLeft))
                {
                    ChunkEditing.KeepCells(chunk, cells.ToArray());
                }
                if (GUILayout.Button(new GUIContent("Erase", "Erase the selected cells (Delete)"), anyGenerated ? EditorStyles.miniButtonRight : EditorStyles.miniButton))
                {
                    ChunkEditing.ClearCells(chunk, cells.ToArray());
                }
            }
        }

        private static void DrawCandidates(Chunk chunk, int cell, Direction face)
        {
            RulesetData rules = ChunkToolState.GetRules(chunk);
            if (rules == null)
            {
                EditorGUILayout.HelpBox("No rules for this chunk: add a Sample Rule Source, or a WFC Generator with a baked ruleset of the same tile set.", MessageType.Info);
                return;
            }
            EditorGUILayout.Space(2);
            ChunkToolState.FitAllNeighbours = EditorGUILayout.ToggleLeft(new GUIContent("Fit all neighbours", "Only show tiles that fit every filled neighbour of the target cell"), ChunkToolState.FitAllNeighbours);
            List<ChunkToolState.Candidate> candidates = ChunkToolState.GetCandidates(chunk, rules, cell, face, ChunkToolState.FitAllNeighbours);
            if (candidates.Count == 0)
            {
                bool outside = !chunk.Cells.Contains(chunk.Cells.ToCell(cell) + face.ToVector());
                string reason = outside ? "The neighbouring cell is outside the chunk."
                    : ChunkToolState.ShownVariant(chunk, rules, cell) < 0 ? "The rules don't have this tile in this orientation."
                    : "Nothing is allowed there yet. Place a tile to teach a connection.";
                EditorGUILayout.HelpBox(reason, MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Allowed next to this face", EditorStyles.miniBoldLabel);
            foreach (var group in candidates.GroupBy(c => c.Definition))
            {
                string name = group.Key != null ? group.Key.name : "(missing)";
                EditorGUILayout.LabelField($"  {name}", $"{group.Count()} variant(s), weight {group.Max(c => c.Weight):0.##}", EditorStyles.miniLabel);
            }

            ChunkToolState.Candidate current = ChunkToolState.GetCurrentCandidate(candidates).Value;
            int index = candidates.FindIndex(c => c.Variant == current.Variant);
            string currentName = current.Definition != null ? current.Definition.name : "(missing)";
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Preview {index + 1}/{candidates.Count}: {currentName} {current.Orientation}", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("◀ [", EditorStyles.miniButtonLeft))
                {
                    ChunkToolState.GhostIndex--;
                }
                if (GUILayout.Button("] ▶", EditorStyles.miniButtonMid))
                {
                    ChunkToolState.GhostIndex++;
                }
                if (GUILayout.Button("Place (Enter)", EditorStyles.miniButtonRight))
                {
                    ChunkTool.PlaceCurrentGhost();
                }
            }
        }

        private static readonly (string label, string tooltip, Orientation change)[] Turns =
        {
            ("⟲Y", "Rotate around Y (Shift+R)", Orientation.RotationY90.Inverse),
            ("⟳Y", "Rotate around Y (R)", Orientation.RotationY90),
            ("⟲X", "Tilt around X (Shift+T)", Orientation.RotationX90.Inverse),
            ("⟳X", "Tilt around X (T)", Orientation.RotationX90),
            ("⟲Z", "Tilt around Z", Orientation.RotationZ90.Inverse),
            ("⟳Z", "Tilt around Z", Orientation.RotationZ90),
            ("Mirror", "Mirror along X (M)", Orientation.MirrorX),
        };

        private static void DrawHints()
        {
            bool cells = ChunkToolState.Chunk != null && ChunkToolState.SelectedCells.Count > 0;
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(cells ? "Turn the selected cells" : "Turn the brush", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var (label, tooltip, change) in Turns)
                {
                    if (GUILayout.Button(new GUIContent(label, tooltip), EditorStyles.miniButton))
                    {
                        ChunkShortcuts.Apply(change, label == "Mirror" ? "Mirror Tile" : "Rotate Tile");
                    }
                }
            }
            TileDefinition palette = ChunkToolState.PaletteTile;
            string tile = palette != null ? $"{palette.name} {ChunkToolState.BrushOrientation}" : "none (Window > WFC > Tile Palette)";
            EditorGUILayout.LabelField($"Shift+Click places: {tile}. Delete erases; R / Shift+R rotate, T / Shift+T tilt, M mirrors.", EditorStyles.wordWrappedMiniLabel);
        }
    }
}
