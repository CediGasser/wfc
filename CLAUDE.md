# WFC: 3D tiled Wave Function Collapse in Unity

University project (HSLU) exploring **3D tiled Wave Function Collapse**. Each experiment/demo gets its own scene.

Planned experiments (the list will grow):
- **Ruleset analysis**: inspecting and validating tile sets / adjacency rules (unreachable tiles, contradiction-prone combinations, statistics).
- **Infinite on-demand generation** for large worlds (chunked generation, consistent chunk borders).
- **Parallelization and other optimizations** of the solver (Jobs/Burst, bitset domains, propagation strategies).
- **Re-generation of already generated tiles**, e.g. for non-Euclidean worlds where regions get regenerated while keeping their neighbours consistent.

## Stack

- Unity **6000.6.3f1**, **URP** (`Assets/Settings/UniversalRP.asset`), **Input System only**: the legacy `UnityEngine.Input` class throws, so use `Keyboard.current` / `Mouse.current` or Input Actions.
- Burst, Collections and Mathematics are installed for the parallelization work.
- Test Framework + Performance Testing (`Unity.PerformanceTesting`).
- IDE: Rider.

## Layout

| Path | Contents |
|---|---|
| `Assets/Scripts/WFC/` | `Wfc` assembly: pure C# core. `Core/` holds `Direction`, `Orientation` (the 48 grid orientations) and `OrientationSet`; `Rules/` holds `RuleDerivation` (learns rules from samples) and `RulesetData` (solver-ready rules); `Solver/` holds `WfcGrid`, `CompiledRules`, `Wave`, `Propagator` and `WfcSolver`. |
| `Assets/Scripts/WFC.Authoring/` | `Wfc.Authoring` assembly (runtime): `TileDefinition` (asset per tile), `TileInstance` (a placed tile), `RulesetAuthoring` (sample root), `Ruleset` (baked asset), `GridPlacement` and `OrientationUnity` (transform conversions), `Generation/WfcGenerator`. |
| `Assets/Scripts/WFC.Editor/` | `Wfc.Editor` assembly (Editor only): `RulesetBaker`, `MeshSymmetryDetector`, live analysis, snapping, gizmos, the `RulesetSceneTool` and its overlay, shortcuts, inspectors, and the windows under `Window > WFC`. |
| `Assets/Scripts/` | Demo/presentation MonoBehaviours in `Assembly-CSharp`, e.g. `FlyCamera` (RMB look, WASD, Space/Shift up/down, scroll = speed). |
| `Assets/Scenes/` | One scene per demo. Ruleset samples live in the same scene as the demo that uses them (e.g. `BasicDemo` → `Pipes Ruleset`). |
| `Assets/Content/<Demo>/` | Per-demo models, `Tiles/*.asset` tile definitions, and the baked ruleset asset (e.g. `wfc_demo_pipes/PipesRuleset.asset`). |
| `Assets/Settings/` | URP pipeline, renderer, global settings, volume profile. |
| `Assets/Tests/EditMode/` | `Wfc.Tests.EditMode` assembly: NUnit unit tests and performance tests. `Solver/` holds the solver tests (category `Solver`) with the `TestRules` builder and `SolutionAssert.IsValid`. |

## Conventions

- The solver core in `Wfc` doesn't depend on MonoBehaviours or scene state. Demos wrap it with thin components that visualize its state.
- The solver can be **stepped** (observe → propagate → report changed cells), so demos can animate and inspect it.
- Randomness is **seeded and deterministic** (`System.Random` or `Unity.Mathematics.Random` with an explicit seed, never `UnityEngine.Random`).
- **Solver:** `WfcSolver` is a state machine. Each `Step()` does one observation: collapse the lowest-entropy cell, then propagate with bitset arc consistency (`Propagator`). The first step propagates every cell once. `ChangedCells` reports what changed. Constraints (`Constrain`, `ConstrainSide` for boundaries) are re-applied by `Reset`. Restarts use `WfcSolver.NextSeed`. The doc comments in `Solver/` are the contract the `Solver` tests check; keep both in sync.
- **Generator:** `WfcGenerator` (in `Wfc.Authoring/Generation/`) drives the solver from `Update`, also outside Play mode (via `EditorApplication.QueuePlayerLoopUpdate`), and spawns tiles under a `DontSave` child, so output is never saved with the scene. Per-side boundaries treat a tile as lying just outside the grid. `BasicDemo` has a "Pipes Generator" next to the "Pipes Ruleset" samples.
- Intricate low-level logic gets unit tests, and optimization claims get a `[Test, Performance]` benchmark (`Measure.Method(...)`) compared against the previous implementation.

## Rulesets

- **Grid:** cell `(x, y, z)` is centred at integer position `(x, y, z)` in the `RulesetAuthoring` root's local space and spans ±0.5. Model pivots are the cell centre.
- **Orientations:** one of 48 signed permutation matrices (`Orientation`, index 0 = identity), composed as matrix products: `a * b` applies `b` first. Mirroring is `scale.x = -1` on the transform. `RotationY90` etc. match `Quaternion.Euler`.
- **Rules come from samples:** every `TileInstance` under a root is a sample. Each pair of tiles touching face to face is a connection, and empty cells mean "no information", so separate samples need a gap. Air is an explicit tile without a model.
- **Expansion and symmetry:**
  - Each observed pair is expanded by every orientation both tiles allow (`allowed_A ∩ allowed_B`).
  - Variants are the cosets of `detected symmetry ∩ allowed` within `allowed`, represented by the smallest orientation index.
  - Symmetries outside the allowed set don't count; that's how gravity is encoded. Give upright tiles `YawMirror`, not `Fixed`.
- **Weights:** undirected, one per learned connection ("orbit", keyed by `OrbitKey`). Presence = 1 by default, or frequency; overrides live on the root, and 0 forbids a connection. Each tile's base weight is spread across its variants.
- **Baking:** `RulesetBaker.Bake(root)` writes the `Ruleset` asset. "Out of date" means the baked asset's `SourceHash` differs from the current samples' hash. Symmetry is re-detected automatically when a model is reimported; the import hash is stored on the definition.

## Unity CLI: drive the running Editor

The `unity` CLI (1.0.0-beta, `~/.unity/bin/unity`) talks to the open Editor through the `com.unity.pipeline` package. **When the Editor is open, drive it through the CLI instead of hand-editing `.unity`, `.prefab` or `.asset` YAML.** Set `UNITY_NO_BANNER=1`, and use `--result-only` or `--json` when parsing output. `unity skill show` prints the full guide, and `unity command --query <term> --detail full` shows a command's parameters.

```bash
unity status                                      # Editor connected? state must be "ready"
unity recompile --json                            # after every C# change: compile + errors/warnings
unity command console --result-only               # console entries with stack traces
unity command clear_console
unity command run_tests --mode EditMode --result-only [--filter <name>]   # --filter Solver --filter_type category for the solver tests
unity command list_tests --mode EditMode --result-only
unity command get_scene_hierarchy --result-only
unity command eval '<C# statements; return value;>' --result-only
unity command editor_play / editor_stop
unity command capture_game_view --save_path <path in project> --source camera
unity command package_add --identifier <name@version> --confirm true
unity command rename_asset --asset <path> --new_name <name>        # keeps GUID
unity command move_asset --asset <path> --destination <path>       # keeps GUID
unity command delete_asset --asset <path> --confirm true
```

Pitfalls:
- `eval` doesn't accept `using` directives, so use fully qualified type names (`UnityEditor.AssetDatabase`, ...). Extension methods don't resolve either: call them statically (`Wfc.Directions.Axis(d)`).
- `recompile --json` can report 0 warnings while the console shows compiler warnings, such as obsolete APIs. Check `unity command console` too.
- Right after a recompile, `status` may still say `ready` before the domain reload starts, and the next command then fails with a connection error. Wait a few seconds, then retry until `recompile --json` answers with `"success": true`.
- Unity only reimports changed files when the Editor gets focus. After external file changes (e.g. models re-exported from Blender), run `editor_focus` or `eval 'UnityEditor.AssetDatabase.Refresh();'` before reading assets.
- `capture_scene_view`, `capture_game_view` and `screenshot` render the camera only: no gizmos, handles or tool drawings. Gizmo and tool visuals need checking by the user.
- Component tools (`EditorTool` with a target type) can only be activated after the selection change has been processed: select in one `eval`, then call `ToolManager.SetActiveTool` in the next.
- Create, move, rename and delete assets through Unity, not the shell, or the `.meta` GUIDs break.
- `capture_game_view --save_path` resolves relative paths under `Assets/`. Delete the capture afterwards with `delete_asset`.
- Play mode only advances while the Editor has focus. Before input tests (`simulate_key --key W --action down|up`), run `set_autotick --enable true --interval_ms 16`, then `editor_focus`, then `editor_play`. Check that `Time.frameCount` actually increases (via `eval`); if it stays at ~2, focus didn't take, so repeat.
- Batch-mode `unity run`, `unity test` and `unity build` fail while the Editor is open (`Temp/UnityLockfile`). Use the live `run_tests` / `build` commands.
- If the Editor starts with compile errors it opens in **Safe Mode**, and the CLI can't connect. Fix the errors from the logs (`Logs/Editor.log` in the project), then restart Unity.
- Changes to the open scene land in the user's Editor. Say so before changing scenes or assets, and save explicitly (`save_all` or `EditorSceneManager.SaveScene`).
