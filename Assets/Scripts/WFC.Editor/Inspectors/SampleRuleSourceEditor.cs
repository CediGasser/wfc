using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    [CustomEditor(typeof(SampleRuleSource))]
    public sealed class SampleRuleSourceEditor : RuleSourceEditor
    {
        protected override void DrawSourceSettings(RuleSource source)
        {
            var sample = (SampleRuleSource)source;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("weighting"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("emptyCellTile"));
            if (sample.EmptyCellTile != null && sample.TileSet != null && !sample.TileSet.Contains(sample.EmptyCellTile))
            {
                EditorGUILayout.HelpBox($"{sample.EmptyCellTile.name} isn't in the chunk's tile set {sample.TileSet.name}, so empty cells carry no information.", MessageType.Warning);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("showConnections"));
            EditorGUILayout.LabelField(
                "The sample is the Chunk on this GameObject: tiles touching face to face become connections. Empty cells count as the empty-cell tile, " +
                "or carry no information without one, e.g. to keep separate samples apart. Paint cells with the WFC Chunk Tool.",
                EditorStyles.wordWrappedMiniLabel);
        }

        protected override void DrawActions(RuleSource source, RuleSourceAnalysis analysis)
        {
            if (GUILayout.Button(new GUIContent("Edit Cells", "Switch to the WFC Chunk Tool to paint the sample.")))
            {
                ChunkEditing.ActivateTool();
            }
            base.DrawActions(source, analysis);
        }
    }
}
