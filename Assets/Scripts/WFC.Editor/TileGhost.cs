using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>See-through previews of tiles in the Scene view, e.g. what fits next to a selected face.</summary>
    public static class TileGhost
    {
        private static Material ghostMaterial;

        /// <summary>Draws a see-through copy of a tile. Call during a Repaint event in the Scene view.</summary>
        public static void Draw(TileDefinition definition, Matrix4x4 cellToWorld, Color color)
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

            Material material = GetMaterial();
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

        private static Material GetMaterial()
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
