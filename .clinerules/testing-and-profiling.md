# Testing & Profiling in this project

Unity 6 project (`6000.3.8f1`), URP 2D. The Unity Editor is usually already open and
connected via the Unity MCP server, which is the fastest way to measure real behaviour.

## Test budget: MINIMAL BY DEFAULT

Do the smallest thing that proves the change works. **Ask the user before any
"extensive" testing** — don't just launch into it.

**Minimal (do freely, no permission needed)**
- Read the relevant code before assuming anything.
- Compile/warning check (`refresh_unity`, then `read_console` filtered to errors/warnings).
- One targeted correctness check: query parity, a single invocation, a value comparison.
- One short sanity measurement if the task is about performance.

**Extensive (ASK FIRST)**
- Multiple enter/exit play-mode cycles, or restarting play mode repeatedly.
- Long sampling runs, stress loops, or spawning/destroying many objects in the loaded scene.
- Creating test GameObjects/prefabs and leaving them in, or editing scene/prefab assets.
- Batch screenshots, full test-suite runs, or player builds.
- Anything that mutates the open scene, a prefab, or an asset on disk.

State what you're about to do and why, then wait. Clean up every test artifact you
create, and confirm `git status` shows only the intended files when you're done.

## Measuring: use the Editor, and measure reality

Prefer live measurement over reasoning about what "should" be slow. The tools:

| Tool | Use |
| --- | --- |
| `manage_editor` (`play` / `stop`) | enter/exit play mode to measure the real game |
| `execute_code` | C# compiled in-memory in the Editor — instrumentation, reflection, stopwatch timing, spawning probes |
| `manage_profiler` `get_frame_timing` | authoritative CPU/GPU frame split (`cpu_main_thread_frame_time_ms` vs `cpu_frame_time_ms`) |
| `manage_profiler` `get_counters` | render counters (draw calls, batches, triangles) |
| `read_console` | compile errors, warnings, runtime spam |
| `manage_camera` `screenshot` | visual proof; `include_image: true` to actually look at it |
| shell (`grep`, `git`, `python3`) | static analysis, asset/prefab YAML, diffs |

A useful trick: `execute_code` can push values into the running game via
`System.AppDomain.CurrentDomain.SetData(...)` and reflect onto private fields, which makes
it easy to instrument without editing source.

## Traps that produce FAKE results

These cost real time. Check them before believing a number.

1. **The Editor throttles to ~10 fps when unfocused.** With `Application.runInBackground`
   false (the default) and the Editor window not focused, you will measure ~9.5 fps no
   matter what the game is doing. This has already fooled one investigation. Always check
   `Application.isFocused`, and force `runInBackground = true`,
   `QualitySettings.vSyncCount = 0`, `Application.targetFrameRate = -1` before sampling.
2. **vsync caps everything at the display rate** (60 here). 57 fps means "cheap enough",
   not "measured". Compare *costs* (stopwatch, frame ms) rather than fps where possible.
3. **`cpu_frame_time_ms` ≈ 100 with a tiny main thread means the bottleneck is
   present/display, not your code.** Read `cpu_main_thread_frame_time_ms` first.
4. **Warm up before timing.** First-call costs (shader compile, material creation, JIT,
   TMP atlas generation) are not steady-state.
5. **A domain reload invalidates `execute_code` state.** Anything stashed in
   `AppDomain` data is gone after a recompile.
6. **`Time.timeScale`, a game-over/pause overlay, or a hidden Canvas can dominate the
   frame.** Check `GameStateManager.Instance.CurrentState` and `PauseUI.OutcomeResolved`
   before benchmarking.
7. **Editor-only measurements are not build measurements.** Say so when reporting.

## Reporting

Give the number, the method, the conditions, and the before/after. If you could not
reproduce the reported problem, say that plainly instead of inventing a cause — the user
needs to know whether the fix addresses the real symptom.