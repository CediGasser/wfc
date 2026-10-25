using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Wfc.Authoring;

namespace Wfc.Editor
{
    [CustomEditor(typeof(WfcGenerator))]
    public sealed class WfcGeneratorEditor : UnityEditor.Editor
    {
        // Static, so a running calibration survives the inspector being rebuilt (selection changes).
        private static readonly WeightCalibrator.Settings CalibrationSettings = new WeightCalibrator.Settings();
        private static WeightCalibrator calibrator;
        private static bool showCalibration;
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

            EditorGUILayout.Space();
            DrawCalibration(generator);

            // Keep repainting while animating, plus once more so the final state (e.g. a contradiction) shows up.
            if (generator.IsAnimating || wasAnimating || calibrator is { IsRunning: true })
            {
                Repaint();
            }
            wasAnimating = generator.IsAnimating;
        }

        private static void DrawCalibration(WfcGenerator generator)
        {
            showCalibration = EditorGUILayout.BeginFoldoutHeaderGroup(showCalibration, "Calibrate Connection Weights");
            if (showCalibration)
            {
                RulesetAuthoring root = generator.Ruleset == null ? null
                    : FindObjectsByType<RulesetAuthoring>(FindObjectsInactive.Exclude).FirstOrDefault(r => r.Target == generator.Ruleset);
                EditorGUILayout.LabelField(
                    "Generates batches of grids with this generator's boundaries and adjusts every connection weight until each connection " +
                    "appears about as often as in the samples. The result is stored as weight overrides on the ruleset root.",
                    EditorStyles.wordWrappedMiniLabel);
                if (root == null)
                {
                    EditorGUILayout.HelpBox("No ruleset root in the open scenes bakes into this generator's ruleset.", MessageType.Info);
                    EditorGUILayout.EndFoldoutHeaderGroup();
                    return;
                }

                bool running = calibrator is { IsRunning: true };
                using (new EditorGUI.DisabledScope(running))
                {
                    WeightCalibrator.Settings s = CalibrationSettings;
                    s.size = EditorGUILayout.Vector3IntField("Grid Size", s.size);
                    s.generationsPerIteration = Mathf.Max(1, EditorGUILayout.IntField("Generations / Iteration", s.generationsPerIteration));
                    s.attemptsPerGeneration = Mathf.Max(1, EditorGUILayout.IntField("Attempts / Generation", s.attemptsPerGeneration));
                    s.iterations = Mathf.Max(1, EditorGUILayout.IntField("Iterations", s.iterations));
                    s.alpha = EditorGUILayout.Slider(new GUIContent("Step Size", "Halved whenever an iteration gets worse than the best one."), s.alpha, 0.05f, 1f);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(running))
                    {
                        if (GUILayout.Button($"Calibrate {root.name}"))
                        {
                            calibrator = new WeightCalibrator(generator, root, CalibrationSettings);
                            calibrator.Start();
                        }
                    }
                    using (new EditorGUI.DisabledScope(!running))
                    {
                        if (GUILayout.Button("Cancel"))
                        {
                            calibrator.Cancel();
                        }
                    }
                    using (new EditorGUI.DisabledScope(running || calibrator?.Best == null || calibrator.Root != root))
                    {
                        if (GUILayout.Button("Apply Best"))
                        {
                            calibrator.Apply();
                        }
                    }
                }
                if (calibrator != null)
                {
                    DrawCalibrationResults(calibrator);
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static void DrawCalibrationResults(WeightCalibrator calibration)
        {
            if (calibration.IsRunning)
            {
                EditorGUILayout.LabelField(calibration.Progress, EditorStyles.miniLabel);
            }
            WeightCalibrator.Iteration best = calibration.Best;
            for (int i = 0; i < calibration.Iterations.Count; i++)
            {
                WeightCalibrator.Iteration iteration = calibration.Iterations[i];
                string distances = double.IsNaN(iteration.connectionDistance) ? "no grid finished"
                    : $"connections off by {iteration.connectionDistance:P1}, tiles by {iteration.tileDistance:P1}";
                string step = iteration.alpha > 0 ? $", step {iteration.alpha:0.###}" : ", start";
                EditorGUILayout.LabelField($"{(iteration == best ? "★" : " ")} Iteration {i + 1}",
                    $"{iteration.done}/{iteration.generations} done ({iteration.attempts} attempts), {distances}{step}", EditorStyles.miniLabel);
            }
            if (calibration.ExcludedTileNames.Count > 0)
            {
                EditorGUILayout.LabelField($"Left out (never appear inside the grid): {string.Join(", ", calibration.ExcludedTileNames)}", EditorStyles.wordWrappedMiniLabel);
            }
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
