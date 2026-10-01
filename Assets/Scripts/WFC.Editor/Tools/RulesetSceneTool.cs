using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>
    /// Scene tool for ruleset samples. Hover a tile's cell to highlight a face, click to select it, and see a
    /// ghost of every tile the samples allow next to that face. [ and ] cycle the ghosts, Enter places the
    /// current one, Shift+Click places the palette tile, Esc clears the face selection.
    /// </summary>
    [EditorTool("WFC Ruleset Tool", typeof(TileInstance))]
    public sealed class RulesetSceneTool : EditorTool
    {
        private static readonly Color HoverColor = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color HoverOutline = new Color(1f, 1f, 1f, 0.8f);
        private static readonly Color SelectedColor = new Color(1f, 0.85f, 0.2f, 0.25f);
        private static readonly Color SelectedOutline = new Color(1f, 0.85f, 0.2f, 1f);

        private GUIContent icon;
        private RulesetToolState.FaceSelection? hover;

        public override GUIContent toolbarIcon => icon ??= new GUIContent("WFC", "WFC Ruleset Tool: select faces, preview and place tiles");

        public override void OnActivated()
        {
            RulesetToolState.Changed += Repaint;
            RulesetLiveAnalysis.Updated += Repaint;
        }

        public override void OnWillBeDeactivated()
        {
            RulesetToolState.Changed -= Repaint;
            RulesetLiveAnalysis.Updated -= Repaint;
            hover = null;
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView)
            {
                return;
            }
            RulesetAuthoring root = GetRoot();
            if (root == null)
            {
                return;
            }
            RulesetBaker.Analysis analysis = RulesetLiveAnalysis.Get(root);
            Event current = Event.current;
            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            switch (current.type)
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(controlId);
                    break;
                case EventType.MouseMove:
                    RulesetToolState.FaceSelection? newHover = Raycast(root, analysis, current.mousePosition);
                    if (!SameFace(newHover, hover))
                    {
                        hover = newHover;
                        window.Repaint();
                    }
                    break;
                case EventType.MouseDown when current.button == 0 && !current.alt:
                    hover = Raycast(root, analysis, current.mousePosition);
                    if (hover is { } clicked)
                    {
                        if (current.shift && RulesetToolState.PaletteTile != null)
                        {
                            PlaceNextTo(root, analysis, clicked, RulesetToolState.PaletteTile, Orientation.Identity);
                        }
                        else
                        {
                            Selection.activeGameObject = clicked.Instance.gameObject;
                            RulesetToolState.SelectedFace = clicked;
                        }
                    }
                    else
                    {
                        RulesetToolState.SelectedFace = null;
                    }
                    GUIUtility.hotControl = controlId;
                    current.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == controlId:
                    GUIUtility.hotControl = 0;
                    current.Use();
                    break;
                case EventType.KeyDown:
                    HandleKey(root, analysis, current);
                    break;
                case EventType.Repaint:
                    Draw(root, analysis);
                    break;
            }
        }

        /// <summary>Places the previewed ghost next to the selected face.</summary>
        public static bool PlaceCurrentGhost()
        {
            if (RulesetToolState.SelectedFace is not { } face)
            {
                return false;
            }
            RulesetAuthoring root = face.Instance.Root;
            RulesetBaker.Analysis analysis = RulesetLiveAnalysis.Get(root);
            if (RulesetToolState.GetCurrentCandidate(RulesetToolState.GetCandidates(analysis, face)) is not { } candidate)
            {
                return false;
            }
            return PlaceNextTo(root, analysis, face, candidate.Definition, candidate.Orientation);
        }

        private static bool PlaceNextTo(RulesetAuthoring root, RulesetBaker.Analysis analysis, RulesetToolState.FaceSelection face, TileDefinition definition, Orientation orientation)
        {
            int3 cell = RulesetToolState.GetNeighbourCell(face);
            if (RulesetEditorUtility.GetOccupiedCells(analysis).Contains(cell))
            {
                SceneView.lastActiveSceneView?.ShowNotification(new GUIContent($"Cell {cell} is already occupied"), 1.5);
                return false;
            }
            TileInstance placed = RulesetEditorUtility.CreateInstance(root, face.Instance.transform.parent, definition, cell, orientation);
            Selection.activeGameObject = placed.gameObject;
            RulesetToolState.SelectedFace = new RulesetToolState.FaceSelection(placed, face.Direction);
            RulesetLiveAnalysis.MarkDirty();
            return true;
        }

        private static RulesetAuthoring GetRoot()
        {
            if (RulesetToolState.SelectedRoot is { } selectedRoot)
            {
                return selectedRoot;
            }
            return Selection.activeGameObject != null && Selection.activeGameObject.TryGetComponent(out TileInstance instance) ? instance.Root : null;
        }

        private void HandleKey(RulesetAuthoring root, RulesetBaker.Analysis analysis, Event current)
        {
            if (RulesetToolState.SelectedFace == null)
            {
                return;
            }
            switch (current.keyCode)
            {
                case KeyCode.LeftBracket:
                    RulesetToolState.GhostIndex--;
                    current.Use();
                    break;
                case KeyCode.RightBracket:
                    RulesetToolState.GhostIndex++;
                    current.Use();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    PlaceCurrentGhost();
                    current.Use();
                    break;
                case KeyCode.Escape:
                    RulesetToolState.SelectedFace = null;
                    current.Use();
                    break;
            }
        }

        private void Draw(RulesetAuthoring root, RulesetBaker.Analysis analysis)
        {
            if (hover is { } hovered && !SameFace(hovered, RulesetToolState.SelectedFace))
            {
                DrawFace(root, RulesetToolState.GetCell(hovered), hovered.Direction, HoverColor, HoverOutline);
            }
            if (RulesetToolState.SelectedFace is not { } face || face.Instance.Root != root)
            {
                return;
            }
            DrawFace(root, RulesetToolState.GetCell(face), face.Direction, SelectedColor, SelectedOutline);

            List<RulesetToolState.Candidate> candidates = RulesetToolState.GetCandidates(analysis, face);
            if (RulesetToolState.GetCurrentCandidate(candidates) is { } candidate)
            {
                Matrix4x4 matrix = GridPlacement.GetWorldMatrix(root.transform, RulesetToolState.GetNeighbourCell(face), candidate.Orientation);
                RulesetEditorUtility.DrawGhost(candidate.Definition, matrix, RulesetToolState.GetCandidateColor(candidate));
                using (new Handles.DrawingScope(SelectedOutline, root.transform.localToWorldMatrix))
                {
                    Handles.DrawWireCube(RulesetToolState.GetNeighbourCell(face).ToVector3(), Vector3.one);
                }
            }
        }

        private static void DrawFace(RulesetAuthoring root, int3 cell, Direction direction, Color fill, Color outline)
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
            using (new Handles.DrawingScope(root.transform.localToWorldMatrix))
            {
                Handles.DrawSolidRectangleWithOutline(corners, fill, outline);
            }
        }

        /// <summary>The nearest cell face of a tile instance under the mouse, in the root's grid space.</summary>
        private static RulesetToolState.FaceSelection? Raycast(RulesetAuthoring root, RulesetBaker.Analysis analysis, Vector2 mousePosition)
        {
            Ray worldRay = HandleUtility.GUIPointToWorldRay(mousePosition);
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
            Vector3 origin = toRoot.MultiplyPoint(worldRay.origin);
            Vector3 direction = toRoot.MultiplyVector(worldRay.direction);

            RulesetToolState.FaceSelection? best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < analysis.instances.Count; i++)
            {
                TileInstance instance = analysis.instances[i];
                if (instance == null)
                {
                    continue;
                }
                Vector3 cell = analysis.input.instances[i].cell.ToVector3();
                if (IntersectBox(origin, direction, cell - Vector3.one * 0.5f, cell + Vector3.one * 0.5f, out float distance, out int axis)
                    && distance < bestDistance)
                {
                    bestDistance = distance;
                    var face = (Direction)(axis * 2 + (direction[axis] > 0 ? 1 : 0));
                    best = new RulesetToolState.FaceSelection(instance, face);
                }
            }
            return best;
        }

        /// <summary>Slab test. <paramref name="axis"/> is the axis of the face the ray enters through.</summary>
        private static bool IntersectBox(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max, out float distance, out int axis)
        {
            float tNear = float.MinValue;
            float tFar = float.MaxValue;
            axis = 0;
            for (int a = 0; a < 3; a++)
            {
                if (Mathf.Abs(direction[a]) < 1e-8f)
                {
                    if (origin[a] < min[a] || origin[a] > max[a])
                    {
                        distance = 0f;
                        return false;
                    }
                    continue;
                }
                float t1 = (min[a] - origin[a]) / direction[a];
                float t2 = (max[a] - origin[a]) / direction[a];
                if (t1 > t2)
                {
                    (t1, t2) = (t2, t1);
                }
                if (t1 > tNear)
                {
                    tNear = t1;
                    axis = a;
                }
                tFar = Mathf.Min(tFar, t2);
            }
            distance = tNear;
            return tNear <= tFar && tNear > 0f;
        }

        private static bool SameFace(RulesetToolState.FaceSelection? a, RulesetToolState.FaceSelection? b)
        {
            if (a == null || b == null)
            {
                return a == null && b == null;
            }
            return a.Value.Instance == b.Value.Instance && a.Value.Direction == b.Value.Direction;
        }

        private static void Repaint() => SceneView.RepaintAll();
    }
}
