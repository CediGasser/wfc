using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>Shared helpers for the ruleset inspectors, windows and scene tool.</summary>
    public static class RulesetEditorUtility
    {
        private static Material ghostMaterial;

        /// <summary>Creates a tile instance on a cell, with Undo.</summary>
        public static TileInstance CreateInstance(RulesetAuthoring root, Transform parent, TileDefinition definition, int3 cell, Orientation orientation, string undoName = "Place Tile")
        {
            var gameObject = new GameObject(definition.name);
            Undo.RegisterCreatedObjectUndo(gameObject, undoName);
            gameObject.transform.SetParent(parent != null ? parent : root.transform, false);
            var instance = gameObject.AddComponent<TileInstance>();
            instance.Definition = definition;
            GridPlacement.Write(gameObject.transform, root.transform, cell, orientation);
            gameObject.transform.hasChanged = false;
            return instance;
        }

        public static HashSet<int3> GetOccupiedCells(RulesetBaker.Analysis analysis)
        {
            return new HashSet<int3>(analysis.input.instances.Select(i => i.cell));
        }

        /// <summary>For example "floor +Y ↔ scaffolding -Y": the faces of both tiles, in each tile's own frame.</summary>
        public static string DescribeOrbit(RulesetData data, Orbit orbit)
        {
            Direction direction = orbit.key.Direction;
            Direction faceA = Orientation.FromIndex(orbit.key.orientationA).Inverse.Apply(direction);
            Direction faceB = Orientation.FromIndex(orbit.key.orientationB).Inverse.Apply(direction.Opposite());
            return $"{data.tileNames[orbit.tileA]} {faceA.ToShortString()}  ↔  {data.tileNames[orbit.tileB]} {faceB.ToShortString()}";
        }

        /// <summary>
        /// Fills empty cells with a tile, separately for every group of touching tiles (within that group's bounding
        /// box), so separate samples stay separate. Returns the number of created instances.
        /// </summary>
        public static int FillEmptyCells(RulesetAuthoring root, TileDefinition fill)
        {
            RulesetBaker.Analysis analysis = RulesetBaker.Analyze(root);
            HashSet<int3> occupied = GetOccupiedCells(analysis);
            var visited = new HashSet<int3>();
            var toFill = new HashSet<int3>();
            foreach (int3 start in occupied)
            {
                if (!visited.Add(start))
                {
                    continue;
                }
                int3 min = start, max = start;
                var queue = new Queue<int3>();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int3 cell = queue.Dequeue();
                    min = math.min(min, cell);
                    max = math.max(max, cell);
                    for (int d = 0; d < Directions.Count; d++)
                    {
                        int3 next = cell + ((Direction)d).ToVector();
                        if (occupied.Contains(next) && visited.Add(next))
                        {
                            queue.Enqueue(next);
                        }
                    }
                }
                for (int x = min.x; x <= max.x; x++)
                {
                    for (int y = min.y; y <= max.y; y++)
                    {
                        for (int z = min.z; z <= max.z; z++)
                        {
                            var cell = new int3(x, y, z);
                            if (!occupied.Contains(cell))
                            {
                                toFill.Add(cell);
                            }
                        }
                    }
                }
            }

            Undo.SetCurrentGroupName($"Fill Empty Cells With {fill.name}");
            int group = Undo.GetCurrentGroup();
            foreach (int3 cell in toFill.OrderBy(c => c.y).ThenBy(c => c.z).ThenBy(c => c.x))
            {
                CreateInstance(root, root.transform, fill, cell, Orientation.Identity, $"Fill Empty Cells With {fill.name}");
            }
            Undo.CollapseUndoOperations(group);
            return toFill.Count;
        }

        /// <summary>Draws a see-through copy of a tile. Call during a Repaint event in the Scene view.</summary>
        public static void DrawGhost(TileDefinition definition, Matrix4x4 cellToWorld, Color color)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            if (definition == null || definition.Model == null)
            {
                using (new Handles.DrawingScope(color, cellToWorld))
                {
                    Handles.DrawWireCube(Vector3.zero, Vector3.one * 0.9f);
                }
                return;
            }

            Material material = GetGhostMaterial();
            material.SetColor("_Color", color);
            foreach (MeshFilter filter in definition.Model.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }
                Matrix4x4 matrix = cellToWorld * filter.transform.localToWorldMatrix;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    material.SetPass(0);
                    Graphics.DrawMeshNow(mesh, matrix, sub);
                }
            }
        }

        public static void SelectInstances(RulesetBaker.Analysis analysis, IEnumerable<int> sourceIds)
        {
            Object[] objects = sourceIds.Where(id => id >= 0 && id < analysis.instances.Count)
                .Select(id => (Object)analysis.instances[id].gameObject).Distinct().ToArray();
            if (objects.Length > 0)
            {
                Selection.objects = objects;
                SceneView.lastActiveSceneView?.FrameSelected();
            }
        }

        public static IEnumerable<int> GetOrbitSources(RulesetBaker.Analysis analysis, int orbit)
        {
            return analysis.result.pairs.Where(p => p.orbit == orbit).SelectMany(p => new[] { p.sourceA, p.sourceB });
        }

        private static Material GetGhostMaterial()
        {
            if (ghostMaterial == null)
            {
                ghostMaterial = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                ghostMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                ghostMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                ghostMaterial.SetInt("_Cull", (int)CullMode.Back);
                ghostMaterial.SetInt("_ZWrite", 0);
                ghostMaterial.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            }
            return ghostMaterial;
        }
    }
}
