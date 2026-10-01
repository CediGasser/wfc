using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    [CustomEditor(typeof(TileDefinition))]
    public sealed class TileDefinitionEditor : UnityEditor.Editor
    {
        private static readonly string[] PermutationLabels = { "XYZ", "XZY", "YXZ", "YZX", "ZXY", "ZYX" };

        private SerializedProperty model;
        private SerializedProperty allowedOrientations;
        private SerializedProperty customOrientationMask;
        private SerializedProperty symmetry;
        private SerializedProperty baseWeight;
        private SerializedProperty gizmoColor;
        private bool showCustomGrid = true;

        private void OnEnable()
        {
            model = serializedObject.FindProperty("model");
            allowedOrientations = serializedObject.FindProperty("allowedOrientations");
            customOrientationMask = serializedObject.FindProperty("customOrientationMask");
            symmetry = serializedObject.FindProperty("symmetry");
            baseWeight = serializedObject.FindProperty("baseWeight");
            gizmoColor = serializedObject.FindProperty("gizmoColor");
        }

        public override void OnInspectorGUI()
        {
            var definition = (TileDefinition)target;
            serializedObject.Update();

            EditorGUILayout.PropertyField(model);
            if (model.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("No model: this is an empty tile, such as air.", MessageType.None);
            }
            EditorGUILayout.PropertyField(allowedOrientations);
            if ((OrientationPreset)allowedOrientations.enumValueIndex == OrientationPreset.Custom)
            {
                DrawCustomOrientations();
            }
            EditorGUILayout.PropertyField(symmetry);
            EditorGUILayout.PropertyField(baseWeight);
            EditorGUILayout.PropertyField(gizmoColor);
            bool changed = serializedObject.ApplyModifiedProperties();
            if (changed)
            {
                definition.NotifyChanged();
            }

            EditorGUILayout.Space();
            DrawSymmetryInfo(definition);
        }

        private void DrawCustomOrientations()
        {
            var set = new OrientationSet(customOrientationMask.ulongValue);
            showCustomGrid = EditorGUILayout.Foldout(showCustomGrid, $"Custom orientations ({set.Count} of 48)", true);
            if (showCustomGrid)
            {
                EditorGUILayout.LabelField("Each button is one orientation, grouped by axis permutation and sign flips. Hover a button to see where the local axes end up; red ones are mirrored.", EditorStyles.wordWrappedMiniLabel);
                for (int p = 0; p < 6; p++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(PermutationLabels[p], GUILayout.Width(36));
                        for (int signs = 0; signs < 8; signs++)
                        {
                            Orientation orientation = Orientation.FromIndex(p * 8 + signs);
                            string label = $"{((signs & 1) != 0 ? "−" : "+")}{((signs & 2) != 0 ? "−" : "+")}{((signs & 4) != 0 ? "−" : "+")}";
                            bool on = set.Contains(orientation);
                            Color previous = GUI.backgroundColor;
                            if (orientation.IsMirrored)
                            {
                                GUI.backgroundColor = new Color(1f, 0.85f, 0.85f);
                            }
                            bool now = GUILayout.Toggle(on, new GUIContent(label, $"{orientation}{(orientation.IsMirrored ? " (mirrored)" : "")}"), EditorStyles.miniButton, GUILayout.Width(34));
                            GUI.backgroundColor = previous;
                            if (now != on)
                            {
                                set = now ? set.With(orientation) : new OrientationSet(set.Mask & ~(1UL << orientation.Index));
                            }
                        }
                    }
                }
            }
            customOrientationMask.ulongValue = set.Mask;

            if (!set.IsGroup)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox("These orientations aren't closed under combination (or lack the identity). Rule derivation will extend them.", MessageType.Warning);
                    if (GUILayout.Button("Close", GUILayout.Width(60), GUILayout.Height(38)))
                    {
                        customOrientationMask.ulongValue = set.Closure().Mask;
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (OrientationPreset preset in new[] { OrientationPreset.Yaw, OrientationPreset.YawMirror, OrientationPreset.AllRotations })
                {
                    if (GUILayout.Button($"Copy {preset}", EditorStyles.miniButton))
                    {
                        customOrientationMask.ulongValue = OrientationSet.FromPreset(preset).Mask;
                    }
                }
            }
        }

        private static void DrawSymmetryInfo(TileDefinition definition)
        {
            OrientationSet allowed = definition.AllowedOrientations.Closure();
            OrientationSet effective = definition.EffectiveSymmetry.Intersect(allowed).Closure();
            int variants = allowed.Count / Mathf.Max(1, effective.Count);

            EditorGUILayout.LabelField("Symmetry", EditorStyles.boldLabel);
            if (definition.Model != null)
            {
                OrientationSet detected = definition.DetectedSymmetry;
                int rotations = 0;
                foreach (Orientation o in detected)
                {
                    rotations += o.IsMirrored ? 0 : 1;
                }
                string state = MeshSymmetryDetector.IsUpToDate(definition) ? "" : "  (outdated, model changed)";
                EditorGUILayout.LabelField("Detected", $"{detected.Count} symmetries ({rotations} rotations){state}");
            }
            EditorGUILayout.LabelField("Variants", $"{variants} distinct of {allowed.Count} allowed orientations");

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(definition.Model == null))
                {
                    if (GUILayout.Button("Re-detect Symmetry"))
                    {
                        MeshSymmetryDetector.UpdateDefinition(definition, force: true);
                        definition.NotifyChanged();
                    }
                }
                if (GUILayout.Button("Show Variants"))
                {
                    VariantViewerWindow.Open(definition);
                }
            }
        }
    }
}
