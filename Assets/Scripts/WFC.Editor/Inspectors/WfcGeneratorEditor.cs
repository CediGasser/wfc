using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    [CustomEditor(typeof(WfcGenerator))]
    public sealed class WfcGeneratorEditor : UnityEditor.Editor
    {
        private bool wasAnimating;

        public override void OnInspectorGUI()
        {
            var generator = (WfcGenerator)target;
            DrawDefaultInspector();
            EditorGUILayout.Space();

            if (!string.IsNullOrEmpty(generator.LastError))
            {
                EditorGUILayout.HelpBox(generator.LastError, MessageType.Warning);
            }
            EditorGUILayout.LabelField("Status", Describe(generator), EditorStyles.wordWrappedLabel);
            DrawContradiction(generator);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate (Animated)"))
                {
                    generator.Generate(animate: true);
                }
                if (GUILayout.Button("Generate Instantly"))
                {
                    generator.Generate(animate: false);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Step"))
                {
                    generator.StepOnce();
                }
                using (new EditorGUI.DisabledScope(!generator.IsAnimating))
                {
                    if (GUILayout.Button("Stop"))
                    {
                        generator.Stop();
                    }
                }
                using (new EditorGUI.DisabledScope(generator.Solver == null || generator.Solver.Status != SolverStatus.Running))
                {
                    if (GUILayout.Button("Finish"))
                    {
                        generator.RunToEnd();
                    }
                }
                if (GUILayout.Button("Clear"))
                {
                    generator.Clear();
                }
            }

            // Keep repainting while animating, plus once more so the final state (e.g. a contradiction) shows up.
            if (generator.IsAnimating || wasAnimating)
            {
                Repaint();
            }
            wasAnimating = generator.IsAnimating;
        }

        /// <summary>Where the generation failed and what surrounds that cell, since the neighbours are what ruled every tile out.</summary>
        private static void DrawContradiction(WfcGenerator generator)
        {
            if (!generator.TryGetContradiction(out int3 cell))
            {
                return;
            }
            WfcSolver solver = generator.Solver;
            RulesetData data = generator.Ruleset.Data;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox($"No tile fits cell {cell} (highlighted red in the Scene view), after {generator.Attempts} attempt(s). " +
                    "Its neighbours below are what ruled every tile out: add a sample that shows a tile fitting between them, or relax the boundaries.", MessageType.Error);
                if (GUILayout.Button("Frame", GUILayout.Width(60), GUILayout.Height(40)))
                {
                    Vector3 center = generator.transform.TransformPoint(cell.ToVector3());
                    SceneView.lastActiveSceneView?.Frame(new Bounds(center, Vector3.one * 4f), false);
                }
            }

            int index = solver.Grid.ToIndex(cell);
            for (int d = 0; d < Directions.Count; d++)
            {
                var direction = (Direction)d;
                int neighbour = solver.Grid.GetNeighbour(index, direction);
                string description;
                if (neighbour < 0)
                {
                    BoundarySide boundary = generator.GetBoundary(direction);
                    description = boundary.mode == BoundaryMode.Tile && boundary.tile != null ? $"outside: boundary {boundary.tile.name}" : "outside: unconstrained";
                }
                else
                {
                    int variant = solver.GetCollapsedVariant(neighbour);
                    int count = solver.Wave.GetCount(neighbour);
                    description = variant >= 0
                        ? $"{data.tileNames[data.variantTile[variant]]} {data.GetVariantOrientation(variant)}"
                        : count == 0 ? "no tile either" : $"undecided ({count} options)";
                }
                EditorGUILayout.LabelField($"  {direction.ToShortString()}", description, EditorStyles.miniLabel);
            }
        }

        private static string Describe(WfcGenerator generator)
        {
            WfcSolver solver = generator.Solver;
            if (solver == null)
            {
                return "Not generated.";
            }
            string state = solver.Status switch
            {
                SolverStatus.Done => "Done",
                SolverStatus.Contradiction => "Contradiction (gave up)",
                _ => generator.IsAnimating ? "Generating…" : "Paused",
            };
            return $"{state}: {generator.DecidedCount}/{solver.Grid.CellCount} cells decided, {generator.Steps} steps, attempt {generator.Attempts}, seed {solver.Seed}";
        }
    }
}
