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
| `Assets/Scripts/WFC/` | `Wfc` assembly (`Wfc.asmdef`): the solver core. Tests reference this assembly. |
| `Assets/Scripts/` | Demo/presentation MonoBehaviours in `Assembly-CSharp`, e.g. `FlyCamera` (RMB look, WASD, Space/Shift up/down, scroll = speed). |
| `Assets/Scenes/` | One scene per demo. |
| `Assets/Content/<Demo>/` | Per-demo tiles, prefabs, materials, ScriptableObject tile sets. |
| `Assets/Settings/` | URP pipeline, renderer, global settings, volume profile. |
| `Assets/Tests/EditMode/` | `Wfc.Tests.EditMode` assembly: NUnit unit tests and performance tests. |

## Conventions

- The solver core in `Wfc` doesn't depend on MonoBehaviours or scene state. Demos wrap it with thin components that visualize its state.
- The solver can be **stepped** (observe → propagate → report changed cells), so demos can animate and inspect it.
- Randomness is **seeded and deterministic** (`System.Random` or `Unity.Mathematics.Random` with an explicit seed, never `UnityEngine.Random`).
- Intricate low-level logic gets unit tests, and optimization claims get a `[Test, Performance]` benchmark (`Measure.Method(...)`) compared against the previous implementation.

## Unity CLI: drive the running Editor

The `unity` CLI (1.0.0-beta, `~/.unity/bin/unity`) talks to the open Editor through the `com.unity.pipeline` package. **When the Editor is open, drive it through the CLI instead of hand-editing `.unity`, `.prefab` or `.asset` YAML.** Set `UNITY_NO_BANNER=1`, and use `--result-only` or `--json` when parsing output. `unity skill show` prints the full guide, and `unity command --query <term> --detail full` shows a command's parameters.

```bash
unity status                                      # Editor connected? state must be "ready"
unity recompile --json                            # after every C# change: compile + errors/warnings
unity command console --result-only               # console entries with stack traces
unity command clear_console
unity command run_tests --mode EditMode --result-only [--filter <name>]
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
- `eval` doesn't accept `using` directives, so use fully qualified type names (`UnityEditor.AssetDatabase`, ...).
- Create, move, rename and delete assets through Unity, not the shell, or the `.meta` GUIDs break.
- `capture_game_view --save_path` resolves relative paths under `Assets/`. Delete the capture afterwards with `delete_asset`.
- Play mode only advances while the Editor has focus. Before input tests (`simulate_key --key W --action down|up`), run `set_autotick --enable true --interval_ms 16`, then `editor_focus`, then `editor_play`. Check that `Time.frameCount` actually increases (via `eval`); if it stays at ~2, focus didn't take, so repeat.
- Batch-mode `unity run`, `unity test` and `unity build` fail while the Editor is open (`Temp/UnityLockfile`). Use the live `run_tests` / `build` commands.
- If the Editor starts with compile errors it opens in **Safe Mode**, and the CLI can't connect. Fix the errors from the logs (`~/Library/Logs/Unity/Editor.log`), then restart Unity.
- Changes to the open scene land in the user's Editor. Say so before changing scenes or assets, and save explicitly (`save_all` or `EditorSceneManager.SaveScene`).
