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
| `Assets/Scripts/WFC/` | `Wfc` assembly: pure C# core. `Core/` holds `Direction`, `Orientation` (the 48 grid orientations) and `OrientationSet`. `Chunks/` holds `ChunkData` (a box of cells, the tile arrangement format), `ChunkBridge` (painted cells → solver constraints, solver → cells) and `ChunkSeams` (generates one chunk of an endless chunk grid so it fits its neighbours). `Rules/` holds `IRuleSource` with `SampleRules` (learns from a sample chunk) and `SocketRules` (face labels), the shared `RulesetBuilder` and `VariantTable` (variants, connections, weights), `RulesetData` (solver-ready rules), `RuleAnalysis` (diagnostics on the finished rules), `RulesetFingerprint` (canonical text for comparing rules), `ConnectionKey` (stable connection identity for overrides) and `ConnectionStatistics` (compares generated grids with the samples, adjusts connection weights). `Solver/` holds `WfcGrid`, `CompiledRules`, `Wave`, `Propagator` and `WfcSolver`. |
| `Assets/Scripts/WFC.Authoring/` | `Wfc.Authoring` assembly (runtime): `Tiles/` holds `TileDefinition` (asset per tile), `TileSet` (ordered tiles; the slot is the tile index everywhere) and `PropSet` (decoration on a tile's sockets). `Chunks/` holds `Chunk` (the cells of a sample, a generator's area or any arrangement), `IChunkLayer` (anything that provides tiles for those cells), `ChunkView` (renders the layers), `ChunkAsset` (cells stored as an asset) and `ChunkRaycast`. `Rules/` holds `RuleSource` with `SampleRuleSource` and `SocketRuleSource`, and `Ruleset` (baked asset). `Generation/` holds `WfcGenerator` and `ChunkStreamer`. `OrientationUnity` (transform conversions) sits at the top. |
| `Assets/Scripts/WFC.Editor/` | `Wfc.Editor` assembly (Editor only): `RulesetBaker`, `TileSpecs`, `LiveRuleAnalysis`, `MeshSymmetryDetector`, `WeightCalibrator`, `ChunkEditing`, gizmos, the `ChunkTool` with its state, overlay and shortcuts, inspectors, and the windows under `Window > WFC`. |
| `Assets/Scripts/` | Demo/presentation MonoBehaviours in `Assembly-CSharp`, e.g. `FlyCamera` (RMB look, WASD, Space/Shift up/down, scroll = speed) and `WalkingCharacter` (first person on a `CharacterController`: mouse look, WASD, Shift runs, Space jumps, Escape frees the cursor). |
| `Assets/Scenes/` | One scene per demo. Rule sources live in the same scene as the demo that uses them (e.g. `BasicDemo` → `Pipes Ruleset` samples, `SomeLandscape` → `Landscape Sockets` socket rules for the landscape and castle tiles). `InfiniteLandscape` streams chunks of the landscape rules around a walking player (the landscape models import with mesh colliders). |
| `Assets/Content/<Demo>/` | Per-demo models, tile definitions, the tile set and the baked ruleset assets (e.g. `Pipes/PipesTileSet.asset`, `Pipes/PipesRuleset.asset`). `Landscape/Tiles/*.prefab` and `Landscape/Props/*.prefab` wrap the 3 m FBX tiles and props (pivot at the bottom) into 1-unit cells centred on the pivot. |
| `Assets/Settings/` | URP pipeline, renderer, global settings, volume profile. |
| `Assets/Tests/EditMode/` | `Wfc.Tests.EditMode` assembly: NUnit unit tests and performance tests. `Solver/` holds the solver tests (category `Solver`) with the `TestRules` builder and `SolutionAssert.IsValid`. |

## Conventions

- The solver core in `Wfc` doesn't depend on MonoBehaviours or scene state. Demos wrap it with thin components that visualize its state.
- The solver can be **stepped** (observe → propagate → report changed cells), so demos can animate and inspect it.
- Randomness is **seeded and deterministic** (`System.Random` or `Unity.Mathematics.Random` with an explicit seed, never `UnityEngine.Random`).
- **Solver:** `WfcSolver` is a state machine. Each `Step()` does one observation: collapse the cell picked by `Heuristic` (`Entropy`: lowest weighted entropy; `WeightSum`: lowest sum of remaining weights, so cells that can still hold a heavy tile like air go last), then propagate with bitset arc consistency (`Propagator`). The first step propagates every cell once. `Wave` tracks which cells lost variants (`ChangedCells`, exposed by the solver per step) and how many are decided (`DecidedCount`). Constraints (`Constrain`, `ConstrainSide` for boundaries) are re-applied by `Reset`. Restarts use `WfcSolver.NextSeed`. The doc comments in `Solver/` are the contract the `Solver` tests check; keep both in sync.
- **Chunks:** a `Chunk` holds a size, a `TileSet` and per cell a tile slot (or empty) and an orientation (`ChunkData`, indexed like `WfcGrid`, so chunk cell i is solver cell i). It's the only place tile arrangements live: samples, generator areas and `ChunkAsset`s. A chunk's cells are saved ("painted") and form the topmost `IChunkLayer`; other components add layers underneath, e.g. the generator's unsaved result. `ChunkView` renders the layer stack (per cell, the first visible layer with a tile) through hidden, unsaved model instances, so it also works in Play mode and builds; gizmos are only for editor overlays. Disabling a layer component hides its tiles. Edit cells with the WFC Chunk Tool (select a chunk, Tools overlay): click selects, Shift+Click places the palette tile, Delete erases, R / T / M rotate, tilt and mirror (bound to a shortcut context that's only active in the tool); every edit goes through `ChunkEditing` (Undo).
- **Tile sets:** a `TileSet`'s slot is a tile's index in chunks, rule sources, baked rulesets and generators, so they must share one tile set. Slots are never reordered or reused; removing a tile leaves an empty slot.
- **Props:** a `TileDefinition` can reference a `PropSet`. `ChunkView` places one of its props (by weight, with probability `fillChance`) on each socket of the tile's model, i.e. each transform whose name starts with the set's `socketPrefix`. Each socket is seeded by its world position, so a cell always gets the same props. Props are visual only and don't affect rules or ruleset hashes. Landscape: `ForestTrees` fills every `TreeSocket` of the forest tiles (sockets come with the FBXs), `GrassProps` sparsely puts bushes and rocks on the `PropSocket`s added to the `Ground_Grass_*` tile prefabs.
- **Generator:** `WfcGenerator` (in `Wfc.Authoring/Generation/`) requires a `Chunk` and fills it: painted cells become constraints (`ChunkBridge.Prefill`), everything else is generated into its result, a layer under the chunk's cells. Disabling the component hides the result without losing it. It drives the solver from `Update`, also outside Play mode (via `EditorApplication.QueuePlayerLoopUpdate`). Its `heuristic` is passed to the solver (`SomeLandscape` uses `WeightSum` with a heavy Air, so the ground is decided before the castle's upper layers). Per-side boundaries treat a tile as lying just outside the chunk. "Keep Result" turns the result into painted cells, "New Sample" makes a sample from it. `BasicDemo` has a "Pipes Generator" next to the "Pipes Ruleset" sample.
- **Streaming:** `ChunkStreamer` generates chunks (Play mode only) within a radius of a target on worker threads (`ChunkSeams.Generate`, no Unity API there). The solver grid is the chunk plus a margin; margin cells in existing chunks are painted, so seams match. If a chunk can't continue its neighbours, a band of their cells along the seam is generated again with it (`bandDepths`). Neighbouring chunks, diagonals included, never generate at the same time. Generated chunks are kept when out of range; only their GameObjects (`Chunk` + `ChunkView`) go.
- Intricate low-level logic gets unit tests, and optimization claims get a `[Test, Performance]` benchmark (`Measure.Method(...)`) compared against the previous implementation.

## Rulesets

- **Grid:** cell `(x, y, z)` is centred at integer position `(x, y, z)` in the chunk's local space and spans ±0.5. Model pivots are the cell centre.
- **Orientations:** one of 48 signed permutation matrices (`Orientation`, index 0 = identity), composed as matrix products: `a * b` applies `b` first. Mirroring is `scale.x = -1` on the transform. `RotationY90` etc. match `Quaternion.Euler`.
- **Rule sources:** a `RuleSource` component (`SampleRuleSource` or `SocketRuleSource`) produces an `IRuleSource`, which derives the same `RulesetData` either way: variants, connections keyed by `ConnectionKey`, weights and overrides. Analysis, calibration, the connection matrix and the solver don't care where rules came from. Only tiles a source uses get variants.
- **Samples:** a `SampleRuleSource` learns from the `Chunk` on its GameObject. Each pair of cells touching face to face is a connection. Empty cells count as the source's `emptyCellTile` (usually air, a tile without a model); without one they carry no information, so separate samples in one chunk need a gap. Cells outside the chunk never carry information.
- **Sockets:** a `SocketRuleSource` gives every tile a label per face (in the tile's own frame). A label has one or more parts separated by `|`, each at a point of the face: side faces `centre`, `left|right` or `left|centre|right` (as seen from outside, +Y up), top/bottom faces `centre`, the four corners `+X+Z|−X+Z|−X−Z|+X−Z` or those plus `|centre`. Faces fit when their parts land on the same points and are equal or a compatible pair at each, so `a|b` meets `b|a` and multi-part tops only stack in matching rotation. Exclusive parts don't fit themselves, only their compatible pairs (the landscape's slope crests `GUc`/`GLc` and outer-corner peaks `GUp`/`GLp` meet flat ground or a further climb, never another crest). Variants are additionally split by orientations that change the labels.
- **Expansion and symmetry:**
  - Each observed pair is expanded by every orientation both tiles allow (`allowed_A ∩ allowed_B`).
  - Variants are the cosets of `detected symmetry ∩ allowed` within `allowed`, represented by the smallest orientation index.
  - Symmetries outside the allowed set don't count; that's how gravity is encoded. Give upright tiles `YawMirror`, not `Fixed`.
- **Weights:** a collapse picks a variant with probability ∝ its variant weight × the weights of its connections to all decided neighbours.
  - Variant weight: the tile's base weight spread across its variants. Leave it at 1 unless there's a reason; connection weights already carry how common a tile is.
  - Connection weight: one per connection, undirected. For samples Presence = 1 or Frequency = how often it was observed (the default); for sockets 1. Overrides on the rule source replace it (keyed by `ConnectionKey`), and 0 forbids the connection.
  - Pairwise weights are what keep structures (tanks, pipes) sample-sized and avoid contradictions: per-tile weights alone can't express "next to X, prefer Y".
  - **Calibration:** the generator inspector's "Calibrate Connection Weights" (`WeightCalibrator`) generates batches with the generator's boundaries and adjusts every connection weight until each connection appears about as often among neighbouring cells as in the samples. The best iteration is applied as overrides on the sample rule source. Tiles that never appear inside the grid (e.g. the boundary tile below) are left out.
- **Strict input:** a sample cell in an orientation its tile doesn't allow is ignored (even if it looks identical to an allowed one), and a tile whose allowed set isn't a group is an error and gets no variants. Painted generator cells the ruleset has no variant for are reported and generated over. `RulesetData.FindVariant` accepts any orientation that looks like a variant.
- **Baking:** `RulesetBaker.Bake(source)` writes the `Ruleset` asset (tile set + data). "Out of date" means the baked asset's `SourceHash` differs from the source's current hash. Symmetry is re-detected automatically when a model is reimported; the import hash is stored on the definition. To check that a change doesn't alter rules, compare `RulesetFingerprint.Describe` before and after.

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
- Play mode only advances while the Editor has focus. Before input tests (`simulate_key --key W --action down|up`), run `set_autotick --enable true --interval_ms 16`, then `editor_focus`, then `editor_play`. Check that `Time.frameCount` actually increases (via `eval`); if it stays at ~2, focus didn't take, so repeat. Keyboard input only reaches Play mode while the Game view has focus (Input System setting): focus it with `eval 'UnityEditor.EditorWindow.GetWindow(System.Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();'` right before `simulate_key`, and expect other CLI calls in between to take the focus away. `InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.Space))` via `eval` is a reliable alternative.
- Batch-mode `unity run`, `unity test` and `unity build` fail while the Editor is open (`Temp/UnityLockfile`). Use the live `run_tests` / `build` commands.
- If the Editor starts with compile errors it opens in **Safe Mode**, and the CLI can't connect. Fix the errors from the logs (`Logs/Editor.log` in the project), then restart Unity.
- Changes to the open scene land in the user's Editor. Say so before changing scenes or assets, and save explicitly (`save_all` or `EditorSceneManager.SaveScene`).
