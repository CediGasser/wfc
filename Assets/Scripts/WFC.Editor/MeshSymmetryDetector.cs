using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>
    /// Finds the orientations that map a model onto itself by comparing its vertex positions (tagged with their
    /// material) before and after applying each of the 48 grid orientations. Triangles are ignored on purpose:
    /// how a quad face was split into triangles shouldn't make a symmetric model asymmetric.
    /// </summary>
    public static class MeshSymmetryDetector
    {
        /// <summary>Vertex positions are compared on a 1 mm grid.</summary>
        private const float Quantum = 0.001f;

        private readonly struct Point : IEquatable<Point>
        {
            public Point(int material, int3 position)
            {
                Material = material;
                Position = position;
            }

            public int Material { get; }
            public int3 Position { get; }

            public bool Equals(Point other) => Material == other.Material && Position.Equals(other.Position);

            public override bool Equals(object obj) => obj is Point other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(Material, Position);
        }

        /// <summary>The symmetry group of the model in tile space. Models without meshes count as fully symmetric.</summary>
        public static OrientationSet Detect(GameObject model)
        {
            if (model == null)
            {
                return OrientationSet.All;
            }
            HashSet<Point> points = CollectPoints(model);
            if (points.Count == 0)
            {
                return OrientationSet.All;
            }

            var symmetry = OrientationSet.IdentityOnly;
            for (int i = 1; i < Orientation.Count; i++)
            {
                Orientation orientation = Orientation.FromIndex(i);
                if (MapsOntoItself(points, orientation))
                {
                    symmetry = symmetry.With(orientation);
                }
            }
            return symmetry;
        }

        /// <summary>
        /// Detects and stores the symmetry on the definition if it's outdated or forced. It's derived data, so this
        /// deliberately doesn't create an Undo entry.
        /// </summary>
        public static void UpdateDefinition(TileDefinition definition, bool force = false)
        {
            if (definition == null || (!force && IsUpToDate(definition)))
            {
                return;
            }
            definition.SetDetectedSymmetry(Detect(definition.Model), GetSourceKey(definition.Model));
            EditorUtility.SetDirty(definition);
        }

        /// <summary>False if the model was replaced or reimported since its symmetry was detected.</summary>
        public static bool IsUpToDate(TileDefinition definition)
        {
            return definition.Model == null || definition.DetectedSymmetrySource == GetSourceKey(definition.Model);
        }

        /// <summary>The model asset's GUID plus its import hash, which changes whenever the file is reimported with new content.</summary>
        private static string GetSourceKey(GameObject model)
        {
            if (model == null)
            {
                return "none";
            }
            string path = AssetDatabase.GetAssetPath(model);
            if (string.IsNullOrEmpty(path))
            {
                return $"unsaved:{model.name}";
            }
            return $"{AssetDatabase.AssetPathToGUID(path)}:{AssetDatabase.GetAssetDependencyHash(path)}";
        }

        private static bool MapsOntoItself(HashSet<Point> points, Orientation orientation)
        {
            foreach (Point point in points)
            {
                int3 mapped = orientation.Apply(point.Position);
                if (!ContainsNear(points, point.Material, mapped))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ContainsNear(HashSet<Point> points, int material, int3 position)
        {
            if (points.Contains(new Point(material, position)))
            {
                return true;
            }
            // Positions that rounded to neighbouring grid points still count as equal.
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        if (points.Contains(new Point(material, position + new int3(x, y, z))))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static HashSet<Point> CollectPoints(GameObject model)
        {
            var points = new HashSet<Point>();
            var materials = new List<Material>();
            var meshes = new List<Mesh>();
            var filters = new List<MeshFilter>();
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null)
                {
                    meshes.Add(filter.sharedMesh);
                    filters.Add(filter);
                }
            }
            if (meshes.Count == 0)
            {
                return points;
            }

            // The model root has no parent here, so localToWorldMatrix is exactly the tile-space transform a TileInstance uses.
            Matrix4x4 rootToTile = model.transform.parent == null ? Matrix4x4.identity : model.transform.parent.worldToLocalMatrix;
            using (Mesh.MeshDataArray dataArray = Mesh.AcquireReadOnlyMeshData(meshes))
            {
                for (int m = 0; m < dataArray.Length; m++)
                {
                    Mesh.MeshData data = dataArray[m];
                    Matrix4x4 toTile = rootToTile * filters[m].transform.localToWorldMatrix;
                    Material[] rendererMaterials = filters[m].TryGetComponent(out MeshRenderer meshRenderer) ? meshRenderer.sharedMaterials : Array.Empty<Material>();

                    using var vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
                    data.GetVertices(vertices);
                    for (int sub = 0; sub < data.subMeshCount; sub++)
                    {
                        Material material = sub < rendererMaterials.Length ? rendererMaterials[sub] : null;
                        int materialTag = material == null ? -1 : IndexOf(materials, material);
                        using var indices = new NativeArray<int>(data.GetSubMesh(sub).indexCount, Allocator.Temp);
                        data.GetIndices(indices, sub);
                        foreach (int index in indices)
                        {
                            Vector3 position = toTile.MultiplyPoint3x4(vertices[index]);
                            points.Add(new Point(materialTag, OrientationUnity.RoundToInt3(position / Quantum)));
                        }
                    }
                }
            }
            return points;
        }

        private static int IndexOf(List<Material> materials, Material material)
        {
            int index = materials.IndexOf(material);
            if (index < 0)
            {
                materials.Add(material);
                index = materials.Count - 1;
            }
            return index;
        }
    }
}
