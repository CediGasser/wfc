using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    /// <summary>Lists the slots of a tile set. Tiles can be added at the end or removed (leaving an empty slot), never reordered.</summary>
    [CustomEditor(typeof(TileSet))]
    public sealed class TileSetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var set = (TileSet)target;
            EditorGUILayout.LabelField(
                "A tile's slot number is its index in chunks and rulesets. Slots are never reordered: removing a tile leaves its slot empty, " +
                "so cells that use it are reported instead of turning into another tile.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();

            for (int slot = 0; slot < set.Count; slot++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(slot.ToString(), GUILayout.Width(28));
                    TileDefinition definition = set[slot];
                    if (definition == null)
                    {
                        EditorGUILayout.LabelField("(empty slot)", EditorStyles.miniLabel);
                    }
                    else
                    {
                        using (new EditorGUI.DisabledScope(true))
                        {
                            EditorGUILayout.ObjectField(definition, typeof(TileDefinition), false);
                        }
                    }
                    using (new EditorGUI.DisabledScope(definition == null))
                    {
                        if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(60)))
                        {
                            Undo.RecordObject(set, "Remove Tile From Set");
                            set.RemoveAt(slot);
                            EditorUtility.SetDirty(set);
                        }
                    }
                }
            }

            EditorGUILayout.Space();
            var added = (TileDefinition)EditorGUILayout.ObjectField(new GUIContent("Add Tile", "Adds a tile in a new slot at the end. You can also drop several tile definitions here."),
                null, typeof(TileDefinition), false);
            if (added != null)
            {
                Add(set, added);
            }
            HandleDrop(set, GUILayoutUtility.GetLastRect());
        }

        private static void Add(TileSet set, TileDefinition definition)
        {
            if (set.Contains(definition))
            {
                return;
            }
            Undo.RecordObject(set, "Add Tile To Set");
            set.Append(definition);
            EditorUtility.SetDirty(set);
        }

        private static void HandleDrop(TileSet set, Rect area)
        {
            Event current = Event.current;
            if ((current.type != EventType.DragUpdated && current.type != EventType.DragPerform) || !area.Contains(current.mousePosition))
            {
                return;
            }
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (Object dropped in DragAndDrop.objectReferences)
                {
                    if (dropped is TileDefinition definition)
                    {
                        Add(set, definition);
                    }
                }
            }
            current.Use();
        }
    }
}
