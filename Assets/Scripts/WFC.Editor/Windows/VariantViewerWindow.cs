using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>Renders every distinct variant of a tile definition, to check how symmetric orientations were merged.</summary>
    public sealed class VariantViewerWindow : EditorWindow
    {
        private const int ImageSize = 128;

        [SerializeField] private TileDefinition definition;
        private readonly Dictionary<int, Texture> images = new Dictionary<int, Texture>();
        private PreviewRenderUtility preview;
        private TileDefinition renderedFor;
        private ulong renderedMask;
        private Vector2 scroll;

        [MenuItem("Window/WFC/Variant Viewer")]
        public static void Open() => Open(Selection.activeObject as TileDefinition);

        public static void Open(TileDefinition definition)
        {
            var window = GetWindow<VariantViewerWindow>("WFC Variants");
            if (definition != null)
            {
                window.definition = definition;
                window.ClearImages();
            }
        }

        /// <summary>The representative orientation of every variant, as rule derivation computes them.</summary>
        public static List<Orientation> GetVariants(TileDefinition definition)
        {
            OrientationSet allowed = definition.AllowedOrientations.Closure();
            OrientationSet effective = definition.EffectiveSymmetry.Intersect(allowed).Closure();
            var variants = new List<Orientation>();
            foreach (Orientation g in allowed)
            {
                int representative = g.Index;
                foreach (Orientation s in effective)
                {
                    representative = Mathf.Min(representative, (g * s).Index);
                }
                if (representative == g.Index)
                {
                    variants.Add(g);
                }
            }
            return variants;
        }

        private void OnEnable() => TileDefinition.Changed += OnDefinitionChanged;

        private void OnDisable()
        {
            TileDefinition.Changed -= OnDefinitionChanged;
            ClearImages();
            preview?.Cleanup();
            preview = null;
        }

        private void OnDefinitionChanged(TileDefinition changed)
        {
            if (changed == definition)
            {
                ClearImages();
                Repaint();
            }
        }

        private void OnGUI()
        {
            var newDefinition = (TileDefinition)EditorGUILayout.ObjectField("Tile", definition, typeof(TileDefinition), false);
            if (newDefinition != definition)
            {
                definition = newDefinition;
                ClearImages();
            }
            if (definition == null)
            {
                EditorGUILayout.HelpBox("Choose a tile definition.", MessageType.Info);
                return;
            }
            MeshSymmetryDetector.UpdateDefinition(definition);
            List<Orientation> variants = GetVariants(definition);
            EditorGUILayout.LabelField($"{variants.Count} distinct variants of {definition.AllowedOrientations.Closure().Count} allowed orientations. Axes: red X, green Y, blue Z.", EditorStyles.wordWrappedMiniLabel);
            if (definition.Model == null)
            {
                EditorGUILayout.HelpBox("This tile has no model, so all its orientations look the same.", MessageType.None);
                return;
            }

            ulong mask = definition.AllowedOrientations.Mask ^ definition.EffectiveSymmetry.Mask;
            if (renderedFor != definition || renderedMask != mask)
            {
                ClearImages();
                renderedFor = definition;
                renderedMask = mask;
            }

            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 20f) / (ImageSize + 8f)));
            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < variants.Count; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int j = i; j < Mathf.Min(i + columns, variants.Count); j++)
                    {
                        using (new EditorGUILayout.VerticalScope(GUILayout.Width(ImageSize)))
                        {
                            GUILayout.Label(GetImage(variants[j]), GUILayout.Width(ImageSize), GUILayout.Height(ImageSize));
                            GUILayout.Label(variants[j].ToString(), EditorStyles.centeredGreyMiniLabel, GUILayout.Width(ImageSize));
                        }
                    }
                    GUILayout.FlexibleSpace();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private Texture GetImage(Orientation orientation)
        {
            if (images.TryGetValue(orientation.Index, out Texture image) && image != null)
            {
                return image;
            }
            if (preview == null)
            {
                preview = new PreviewRenderUtility();
                preview.camera.fieldOfView = 30f;
                preview.camera.nearClipPlane = 0.1f;
                preview.camera.farClipPlane = 20f;
                preview.camera.transform.position = new Vector3(2.4f, 2.0f, -3.2f);
                preview.camera.transform.LookAt(Vector3.zero);
                preview.lights[0].intensity = 1.2f;
                preview.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
                preview.lights[1].intensity = 0.6f;
                preview.ambientColor = new Color(0.25f, 0.25f, 0.25f);
            }

            preview.BeginPreview(new Rect(0, 0, ImageSize, ImageSize), GUIStyle.none);
            Matrix4x4 cell = orientation.ToMatrix4x4();
            foreach (MeshFilter filter in definition.Model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || !filter.TryGetComponent(out MeshRenderer meshRenderer))
                {
                    continue;
                }
                Matrix4x4 matrix = cell * filter.transform.localToWorldMatrix;
                Material[] materials = meshRenderer.sharedMaterials;
                for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++)
                {
                    Material material = sub < materials.Length ? materials[sub] : null;
                    if (material != null)
                    {
                        preview.DrawMesh(filter.sharedMesh, matrix, material, sub);
                    }
                }
            }
            DrawAxes();
            preview.camera.Render();
            Texture rendered = preview.EndPreview();

            // EndPreview reuses its render texture, so keep a copy per variant.
            var copy = new RenderTexture(ImageSize, ImageSize, 0) { hideFlags = HideFlags.HideAndDontSave };
            Graphics.Blit(rendered, copy);
            images[orientation.Index] = copy;
            return copy;
        }

        private void DrawAxes()
        {
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            Material axisMaterial = GetAxisMaterial();
            DrawAxis(cube, axisMaterial, new Vector3(0.75f, 0f, 0f), new Vector3(0.5f, 0.02f, 0.02f), Color.red);
            DrawAxis(cube, axisMaterial, new Vector3(0f, 0.75f, 0f), new Vector3(0.02f, 0.5f, 0.02f), Color.green);
            DrawAxis(cube, axisMaterial, new Vector3(0f, 0f, 0.75f), new Vector3(0.02f, 0.02f, 0.5f), Color.blue);
        }

        private void DrawAxis(Mesh cube, Material material, Vector3 position, Vector3 size, Color color)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            preview.DrawMesh(cube, Matrix4x4.TRS(position, Quaternion.identity, size), material, 0, block);
        }

        private static Material axisMaterial;

        private static Material GetAxisMaterial()
        {
            if (axisMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                axisMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            return axisMaterial;
        }

        private void ClearImages()
        {
            foreach (Texture image in images.Values)
            {
                if (image != null)
                {
                    DestroyImmediate(image);
                }
            }
            images.Clear();
        }
    }
}
