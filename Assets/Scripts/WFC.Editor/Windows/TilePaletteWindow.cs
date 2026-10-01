using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>Lists all tile definitions; the chosen one is placed by Shift+Clicking a face with the WFC Ruleset Tool.</summary>
    public sealed class TilePaletteWindow : EditorWindow
    {
        private const float ButtonSize = 76f;

        private readonly List<TileDefinition> definitions = new List<TileDefinition>();
        private Vector2 scroll;

        [MenuItem("Window/WFC/Tile Palette")]
        public static void Open() => GetWindow<TilePaletteWindow>("WFC Tiles");

        private void OnEnable()
        {
            Refresh();
            EditorApplication.projectChanged += Refresh;
            RulesetToolState.Changed += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= Refresh;
            RulesetToolState.Changed -= Repaint;
        }

        private void Refresh()
        {
            definitions.Clear();
            definitions.AddRange(AssetDatabase.FindAssets("t:" + nameof(TileDefinition))
                .Select(guid => AssetDatabase.LoadAssetAtPath<TileDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(definition => definition != null)
                .OrderBy(definition => AssetDatabase.GetAssetPath(definition)));
            Repaint();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(RulesetToolState.PaletteTile != null ? $"Placing: {RulesetToolState.PaletteTile.name}" : "No tile chosen", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton))
                {
                    RulesetToolState.PaletteTile = null;
                }
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
                {
                    Refresh();
                }
            }
            EditorGUILayout.LabelField("Choose a tile, then Shift+Click a face with the WFC Ruleset Tool to place it in the neighbouring cell.", EditorStyles.wordWrappedMiniLabel);

            if (definitions.Count == 0)
            {
                EditorGUILayout.HelpBox("No tile definitions found. Create one with Assets > Create > WFC > Tile Definition.", MessageType.Info);
                return;
            }

            bool loading = false;
            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 20f) / (ButtonSize + 4f)));
            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < definitions.Count; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int j = i; j < Mathf.Min(i + columns, definitions.Count); j++)
                    {
                        loading |= DrawButton(definitions[j]);
                    }
                    GUILayout.FlexibleSpace();
                }
            }
            EditorGUILayout.EndScrollView();

            if (loading)
            {
                Repaint();
            }
        }

        /// <summary>Returns true while the preview image is still loading.</summary>
        private static bool DrawButton(TileDefinition definition)
        {
            Texture preview = definition.Model != null ? AssetPreview.GetAssetPreview(definition.Model) : null;
            bool loading = definition.Model != null && preview == null && AssetPreview.IsLoadingAssetPreviews();
            if (preview == null)
            {
                preview = AssetPreview.GetMiniThumbnail(definition);
            }

            bool chosen = RulesetToolState.PaletteTile == definition;
            Color previous = GUI.backgroundColor;
            if (chosen)
            {
                GUI.backgroundColor = new Color(0.4f, 0.75f, 1f);
            }
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(ButtonSize)))
            {
                if (GUILayout.Button(new GUIContent(preview, definition.name), GUILayout.Width(ButtonSize), GUILayout.Height(ButtonSize)))
                {
                    RulesetToolState.PaletteTile = chosen ? null : definition;
                    EditorGUIUtility.PingObject(definition);
                }
                GUILayout.Label(definition.name, EditorStyles.centeredGreyMiniLabel, GUILayout.Width(ButtonSize));
            }
            GUI.backgroundColor = previous;
            return loading;
        }
    }
}
