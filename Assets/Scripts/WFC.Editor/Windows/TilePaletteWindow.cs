using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>
    /// The tiles of the selected chunk's tile set; the chosen one is placed by Shift+Clicking with the WFC Chunk Tool.
    /// Without a selected chunk, every tile definition in the project is listed.
    /// </summary>
    public sealed class TilePaletteWindow : EditorWindow
    {
        private const float ButtonSize = 76f;

        private readonly List<TileDefinition> allDefinitions = new List<TileDefinition>();
        private Vector2 scroll;

        [MenuItem("Window/WFC/Tile Palette")]
        public static void Open() => GetWindow<TilePaletteWindow>("WFC Tiles");

        private void OnEnable()
        {
            Refresh();
            EditorApplication.projectChanged += Refresh;
            ChunkToolState.Changed += Repaint;
            TileSet.Changed += OnTileSetChanged;
            Selection.selectionChanged += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= Refresh;
            ChunkToolState.Changed -= Repaint;
            TileSet.Changed -= OnTileSetChanged;
            Selection.selectionChanged -= Repaint;
        }

        private void OnTileSetChanged(TileSet _) => Repaint();

        private void Refresh()
        {
            allDefinitions.Clear();
            allDefinitions.AddRange(AssetDatabase.FindAssets("t:" + nameof(TileDefinition))
                .Select(guid => AssetDatabase.LoadAssetAtPath<TileDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(definition => definition != null)
                .OrderBy(definition => AssetDatabase.GetAssetPath(definition)));
            Repaint();
        }

        /// <summary>The tile set of the selected chunk, else of the chunk the tool last worked on.</summary>
        private static TileSet ActiveTileSet()
        {
            if (Selection.activeGameObject != null && Selection.activeGameObject.TryGetComponent(out Chunk selected) && selected.TileSet != null)
            {
                return selected.TileSet;
            }
            return ChunkToolState.Chunk != null ? ChunkToolState.Chunk.TileSet : null;
        }

        private void OnGUI()
        {
            TileDefinition chosen = ChunkToolState.PaletteTile;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(chosen != null ? $"Placing: {chosen.name} {ChunkToolState.BrushOrientation}" : "No tile chosen", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Reset Rotation", "Place tiles in their default orientation again"), EditorStyles.toolbarButton))
                {
                    ChunkToolState.BrushOrientation = Orientation.Identity;
                }
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton))
                {
                    ChunkToolState.PaletteTile = null;
                }
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
                {
                    Refresh();
                }
            }

            TileSet tileSet = ActiveTileSet();
            var entries = new List<(TileDefinition definition, string label)>();
            if (tileSet != null)
            {
                EditorGUILayout.LabelField($"Tiles of {tileSet.name}. Choose one, then Shift+Click with the WFC Chunk Tool; Alt+1..4 rotate it while no cell is selected.", EditorStyles.wordWrappedMiniLabel);
                for (int slot = 0; slot < tileSet.Count; slot++)
                {
                    if (tileSet[slot] != null)
                    {
                        entries.Add((tileSet[slot], $"{slot}: {tileSet[slot].name}"));
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("Select a chunk to see its tile set. Showing every tile definition in the project.", EditorStyles.wordWrappedMiniLabel);
                entries.AddRange(allDefinitions.Select(d => (d, d.name)));
            }
            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox("No tiles. Create tile definitions with Assets > Create > WFC > Tile Definition and add them to a tile set.", MessageType.Info);
                return;
            }

            bool loading = false;
            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 20f) / (ButtonSize + 4f)));
            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < entries.Count; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int j = i; j < Mathf.Min(i + columns, entries.Count); j++)
                    {
                        loading |= DrawButton(entries[j].definition, entries[j].label);
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
        private static bool DrawButton(TileDefinition definition, string label)
        {
            Texture preview = definition.Model != null ? AssetPreview.GetAssetPreview(definition.Model) : null;
            bool loading = definition.Model != null && preview == null && AssetPreview.IsLoadingAssetPreviews();
            if (preview == null)
            {
                preview = AssetPreview.GetMiniThumbnail(definition);
            }

            bool chosen = ChunkToolState.PaletteTile == definition;
            Color previous = GUI.backgroundColor;
            if (chosen)
            {
                GUI.backgroundColor = new Color(0.4f, 0.75f, 1f);
            }
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(ButtonSize)))
            {
                if (GUILayout.Button(new GUIContent(preview, definition.name), GUILayout.Width(ButtonSize), GUILayout.Height(ButtonSize)))
                {
                    ChunkToolState.PaletteTile = chosen ? null : definition;
                    EditorGUIUtility.PingObject(definition);
                }
                GUILayout.Label(label, EditorStyles.centeredGreyMiniLabel, GUILayout.Width(ButtonSize));
            }
            GUI.backgroundColor = previous;
            return loading;
        }
    }
}
