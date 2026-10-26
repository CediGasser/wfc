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
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox(generator.LastError, MessageType.Warning);
                    if (generator.InvalidPaintedCells.Count > 0 && GUILayout.Button("Select", GUILayout.Width(52), GUILayout.Height(38)))
                    {
                        ChunkEditing.SelectCells(generator.Chunk, generator.InvalidPaintedCells);
                    }
                }
            }
            if (generator.Ruleset != null && generator.Ruleset.TileSet != null && generator.Chunk.TileSet != generator.Ruleset.TileSet)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox($"The chunk must use the ruleset's tile set ({generator.Ruleset.TileSet.name}).", MessageType.Error);
                    if (generator.Chunk.Cells.CountNonEmpty() == 0 && GUILayout.Button("Use It", GUILayout.Width(60), GUILayout.Height(38)))
                    {
                        Undo.RecordObject(generator.Chunk, "Use Ruleset Tile Set");
                        generator.Chunk.TileSet = generator.Ruleset.TileSet;
                    }
                }
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
            DrawResultActions(generator);

            EditorGUILayout.Space();
            DrawCalibration(generator);

            // Keep repainting while animating, plus once more so the final state (e.g. a contradiction) shows up.
            if (generator.IsAnimating || wasAnimating || calibrator is { IsRunning: true })
            {
                Repaint();
            }
            wasAnimating = generator.IsAnimating;
        }

        /// <summary>Ways to keep a generated result: as painted cells, as a chunk asset or as a new sample.</summary>
        private static void DrawResultActions(WfcGenerator generator)
        {
            bool hasResult = generator.Chunk.Generated != null && generator.Solver != null && generator.Solver.Status == SolverStatus.Done;
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!hasResult))
            {
                if (GUILayout.Button(new GUIContent("Keep Result", "Turns the generated tiles into painted cells of the chunk, so they're saved and can be edited.")))
                {
                    Undo.RecordObject(generator.Chunk, "Keep Generated Tiles");
                    generator.KeepResult();
                }
                if (GUILayout.Button(new GUIContent("Save As Chunk Asset…", "Stores painted and generated cells in a chunk asset.")))
                {
                    string path = EditorUtility.SaveFilePanelInProject("Save Result", $"{generator.name} Result", "asset", "Choose where to save the chunk asset.");
                    if (!string.IsNullOrEmpty(path))
                    {
                        var asset = CreateInstance<ChunkAsset>();
                        asset.Set(generator.Chunk.TileSet, generator.GetOutput());
                        AssetDatabase.CreateAsset(asset, path);
                        AssetDatabase.SaveAssets();
                        EditorGUIUtility.PingObject(asset);
                    }
                }
                if (GUILayout.Button(new GUIContent("New Sample", "Creates a sample (Chunk + Sample Rule Source) from the result next to the generator, to adjust and bake.")))
                {
                    Selection.activeGameObject = CreateSample(generator);
                }
            }
        }

        /// <summary>
        /// A new sample holding the generator's output. If the source of the generator's ruleset counts empty cells as
        /// a tile (usually air), that tile is left out again: the output has no gaps, so the rules stay the same.
        /// </summary>
        private static GameObject CreateSample(WfcGenerator generator)
        {
            ChunkData output = generator.GetOutput();
            TileSet tileSet = generator.Chunk.TileSet;
            TileDefinition emptyCellTile = FindObjectsByType<SampleRuleSource>(FindObjectsInactive.Exclude)
                .FirstOrDefault(s => s.Ruleset == generator.Ruleset)?.EmptyCellTile;
            int empty = tileSet.IndexOf(emptyCellTile);
            for (int cell = 0; empty >= 0 && cell < output.CellCount; cell++)
            {
                if (output.GetTile(cell) == empty && output.GetOrientation(cell) == Orientation.Identity)
                {
                    output.Clear(cell);
                }
            }

            var gameObject = new GameObject($"{generator.name} Sample");
            Undo.RegisterCreatedObjectUndo(gameObject, "New Sample From Result");
            Transform source = generator.transform;
            gameObject.transform.SetPositionAndRotation(source.TransformPoint(new Vector3(generator.Chunk.Size.x + 4, 0, 0)), source.rotation);
            var sample = gameObject.AddComponent<SampleRuleSource>();
            gameObject.AddComponent<ChunkView>();
            sample.Chunk.TileSet = tileSet;
            sample.Chunk.SetCells(output);
            sample.EmptyCellTile = empty >= 0 ? emptyCellTile : null;
            return gameObject;
        }

        private static void DrawCalibration(WfcGenerator generator)
        {
            showCalibration = EditorGUILayout.BeginFoldoutHeaderGroup(showCalibration, "Calibrate Connection Weights");
            if (showCalibration)
            {
                RuleSource source = generator.Ruleset == null ? null
                    : FindObjectsByType<RuleSource>(FindObjectsInactive.Exclude).FirstOrDefault(r => r.Ruleset == generator.Ruleset);
                EditorGUILayout.LabelField(
                    "Generates batches of grids with this generator's boundaries and adjusts every connection weight until each connection " +
                    "appears about as often as in the samples. The result is stored as weight overrides on the rule source.",
                    EditorStyles.wordWrappedMiniLabel);
                if (source == null || !source.HasObservations)
                {
                    EditorGUILayout.HelpBox(source == null ? "No rule source in the open scenes bakes into this generator's ruleset."
                        : $"{source.name} doesn't learn from samples, so there's nothing to calibrate towards.", MessageType.Info);
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
                        if (GUILayout.Button($"Calibrate {source.name}"))
                        {
                            calibrator = new WeightCalibrator(generator, source, CalibrationSettings);
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
                    using (new EditorGUI.DisabledScope(running || calibrator?.Best == null || calibrator.Source != source))
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
