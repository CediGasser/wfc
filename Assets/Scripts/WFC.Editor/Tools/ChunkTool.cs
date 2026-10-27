using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>
    /// Scene tool for editing the cells of a chunk: samples as well as painted cells of a generator. Click a tile to
    /// select its cell and the face under the mouse, and see a ghost of every tile the rules allow next to that face
    /// ([ and ] cycle, Enter places). Shift+Click places the palette tile, Ctrl/Cmd+Click adds cells to the
    /// selection, Delete erases the selected cells, R / T / M rotate, tilt or mirror them (see <see cref="ChunkShortcuts"/>),
    /// Esc clears the selection.
    /// </summary>
    [EditorTool("WFC Chunk Tool", typeof(Chunk))]
    public sealed class ChunkTool : EditorTool
    {
        private static readonly Color HoverColor = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color HoverOutline = new Color(1f, 1f, 1f, 0.8f);
        private static readonly Color SelectedColor = new Color(1f, 0.85f, 0.2f, 0.25f);
        private static readonly Color SelectedOutline = new Color(1f, 0.85f, 0.2f, 1f);

        private GUIContent icon;
        private ChunkRaycast.Hit? hover;

        public override GUIContent toolbarIcon => icon ??= new GUIContent("WFC", "WFC Chunk Tool: select cells, preview and place tiles");

        public override void OnActivated()
        {
            ChunkToolState.Changed += Repaint;
            LiveRuleAnalysis.Updated += Repaint;
        }

        public override void OnWillBeDeactivated()
        {
            ChunkToolState.Changed -= Repaint;
            LiveRuleAnalysis.Updated -= Repaint;
            hover = null;
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView || target is not Chunk chunk)
            {
                return;
            }
            Event current = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            switch (current.type)
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(controlId);
                    break;
                case EventType.MouseMove:
                    ChunkRaycast.Hit? newHover = Raycast(chunk, current.mousePosition);
                    if (!SameHit(newHover, hover))
                    {
                        hover = newHover;
                        window.Repaint();
                    }
                    break;
                case EventType.MouseDown when current.button == 0 && !current.alt:
                    hover = Raycast(chunk, current.mousePosition);
                    OnClick(chunk, hover, current);
                    GUIUtility.hotControl = controlId;
                    current.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == controlId:
                    GUIUtility.hotControl = 0;
                    current.Use();
                    break;
                case EventType.KeyDown:
                    HandleKey(chunk, current);
                    break;
                case EventType.ValidateCommand:
                case EventType.ExecuteCommand:
                    HandleCommand(chunk, current);
                    break;
                case EventType.Repaint:
                    Draw(chunk);
                    break;
            }
        }

        /// <summary>Places the previewed ghost next to the selected face.</summary>
        public static bool PlaceCurrentGhost()
        {
            Chunk chunk = ChunkToolState.Chunk;
            if (!ChunkToolState.TryGetFace(out int cell, out Direction face))
            {
                return false;
            }
            List<ChunkToolState.Candidate> candidates = ChunkToolState.GetCandidates(chunk, ChunkToolState.GetRules(chunk), cell, face, ChunkToolState.FitAllNeighbours);
            if (ChunkToolState.GetCurrentCandidate(candidates) is not { } candidate)
            {
                return false;
            }
            return Place(chunk, chunk.Cells.ToCell(cell) + face.ToVector(), candidate.Tile, candidate.Orientation, face);
        }

        private static void OnClick(Chunk chunk, ChunkRaycast.Hit? clicked, Event current)
        {
            if (clicked is not { } hit)
            {
                ChunkToolState.ClearSelection();
                return;
            }
            int cell = chunk.Cells.ToIndex(hit.cell);
            if (current.shift)
            {
                PlacePaletteTile(chunk, hit);
            }
            else if (EditorGUI.actionKey)
            {
                if (hit.solid)
                {
                    ChunkToolState.Toggle(chunk, cell);
                }
            }
            else if (hit.solid)
            {
                ChunkToolState.Select(chunk, new[] { cell }, cell, hit.face);
            }
            else
            {
                ChunkToolState.ClearSelection();
            }
        }

        private static void PlacePaletteTile(Chunk chunk, ChunkRaycast.Hit hit)
        {
            TileDefinition definition = ChunkToolState.PaletteTile;
            if (definition == null)
            {
                Notify("Choose a tile in the tile palette first (Window > WFC > Tile Palette)");
                return;
            }
            int tile = chunk.TileSet != null ? chunk.TileSet.IndexOf(definition) : -1;
            if (tile < 0)
            {
                Notify($"{definition.name} isn't in the chunk's tile set");
                return;
            }
            // Next to the clicked face, or right into the far wall of an empty chunk.
            int3 target = hit.solid ? hit.cell + hit.face.ToVector() : hit.cell;
            Place(chunk, target, tile, ChunkToolState.BrushOrientation, hit.face);
        }

        private static bool Place(Chunk chunk, int3 target, int tile, Orientation orientation, Direction face)
        {
            if (!chunk.Cells.Contains(target))
            {
                Notify($"Cell {target} is outside the chunk; make it bigger in the inspector first");
                return false;
            }
            int index = chunk.Cells.ToIndex(target);
            if (!chunk.Cells.IsEmpty(index))
            {
                Notify($"Cell {target} is already taken; erase it first");
                return false;
            }
            ChunkEditing.SetCell(chunk, index, tile, orientation);
            ChunkToolState.Select(chunk, new[] { index }, index, face);
            return true;
        }

        private static void HandleKey(Chunk chunk, Event current)
        {
            switch (current.keyCode)
            {
                case KeyCode.LeftBracket when ChunkToolState.TryGetFace(out _, out _):
                    ChunkToolState.GhostIndex--;
                    current.Use();
                    break;
                case KeyCode.RightBracket when ChunkToolState.TryGetFace(out _, out _):
                    ChunkToolState.GhostIndex++;
                    current.Use();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (PlaceCurrentGhost())
                    {
                        current.Use();
                    }
                    break;
                case KeyCode.Escape when ChunkToolState.SelectedCells.Count > 0:
                    ChunkToolState.ClearSelection();
                    current.Use();
                    break;
            }
        }

        /// <summary>
        /// Delete and Frame Selected act on the selected cells while there are any; without this, Delete would delete
        /// the chunk's GameObject.
        /// </summary>
        private static void HandleCommand(Chunk chunk, Event current)
        {
            bool hasCells = ChunkToolState.Chunk == chunk && ChunkToolState.SelectedCells.Count > 0;
            bool delete = current.commandName == "SoftDelete" || current.commandName == "Delete";
            bool frame = current.commandName == "FrameSelected";
            if (!hasCells || (!delete && !frame))
            {
                return;
            }
            if (current.type == EventType.ExecuteCommand)
            {
                int[] cells = ChunkToolState.SelectedCells.ToArray();
                if (delete)
                {
                    ChunkEditing.ClearCells(chunk, cells);
                    ChunkToolState.Select(chunk, cells.Where(cell => ChunkLayers.GetShownTile(chunk.gameObject, cell, out _, out _) >= 0));
                }
                else
                {
                    ChunkEditing.Frame(chunk, cells);
                }
            }
            current.Use();
        }

        private void Draw(Chunk chunk)
        {
            if (hover is { } hovered)
            {
                if (hovered.solid)
                {
                    DrawFace(chunk, hovered.cell, hovered.face, HoverColor, HoverOutline);
                }
                else
                {
                    using (new Handles.DrawingScope(HoverOutline, chunk.transform.localToWorldMatrix))
                    {
                        Handles.DrawWireCube(hovered.cell.ToVector3(), Vector3.one * 0.96f);
                    }
                }
            }
            if (ChunkToolState.Chunk != chunk || !ChunkToolState.TryGetFace(out int cell, out Direction face))
            {
                return;
            }
            int3 position = chunk.Cells.ToCell(cell);
            DrawFace(chunk, position, face, SelectedColor, SelectedOutline);

            List<ChunkToolState.Candidate> candidates = ChunkToolState.GetCandidates(chunk, ChunkToolState.GetRules(chunk), cell, face, ChunkToolState.FitAllNeighbours);
            if (ChunkToolState.GetCurrentCandidate(candidates) is { } candidate)
            {
                int3 target = position + face.ToVector();
                TileGhost.Draw(candidate.Definition, chunk.CellToWorld(target, candidate.Orientation), ChunkToolState.GetCandidateColor(candidate));
                using (new Handles.DrawingScope(SelectedOutline, chunk.transform.localToWorldMatrix))
                {
                    Handles.DrawWireCube(target.ToVector3(), Vector3.one);
                }
            }
        }

        private static void DrawFace(Chunk chunk, int3 cell, Direction direction, Color fill, Color outline)
        {
            Vector3 normal = direction.ToVector().ToVector3();
            Vector3 center = cell.ToVector3() + normal * 0.5f;
            Vector3 u = direction.Axis() == 0 ? Vector3.up : Vector3.right;
            Vector3 v = Vector3.Cross(normal, u);
            var corners = new[]
            {
                center + (u + v) * 0.5f,
                center + (u - v) * 0.5f,
                center - (u + v) * 0.5f,
                center - (u - v) * 0.5f,
            };
            using (new Handles.DrawingScope(chunk.transform.localToWorldMatrix))
            {
                Handles.DrawSolidRectangleWithOutline(corners, fill, outline);
            }
        }

        /// <summary>The first cell under the mouse that shows a tile, or the far wall of the chunk if there's none.</summary>
        private static ChunkRaycast.Hit? Raycast(Chunk chunk, Vector2 mousePosition)
        {
            Ray worldRay = HandleUtility.GUIPointToWorldRay(mousePosition);
            Matrix4x4 toChunk = chunk.transform.worldToLocalMatrix;
            Vector3 origin = toChunk.MultiplyPoint(worldRay.origin);
            Vector3 direction = toChunk.MultiplyVector(worldRay.direction);
            List<IChunkLayer> layers = ChunkLayers.GetLayers(chunk.gameObject);
            ChunkData cells = chunk.Cells;
            bool Solid(int3 cell) => ChunkLayers.GetShownTile(layers, cells.ToIndex(cell), out _, out _) >= 0;
            return ChunkRaycast.Raycast(chunk.Size, origin, direction, Solid, out ChunkRaycast.Hit hit) ? hit : null;
        }

        private static bool SameHit(ChunkRaycast.Hit? a, ChunkRaycast.Hit? b)
        {
            if (a == null || b == null)
            {
                return a == null && b == null;
            }
            return a.Value.cell.Equals(b.Value.cell) && a.Value.face == b.Value.face && a.Value.solid == b.Value.solid;
        }

        private static void Notify(string message)
        {
            SceneView.lastActiveSceneView?.ShowNotification(new GUIContent(message), 1.5);
        }

        private static void Repaint() => SceneView.RepaintAll();
    }
}
