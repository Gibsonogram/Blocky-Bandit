# Agent workflow (Blocky Bandit / Castle Crook)

## Ship the fix, hand the testing back

The user tests the game themselves. Default to **solving the bug quickly and stopping** —
do not spend the session validating it.

- Fix the reported symptom, then report what changed and why. Say plainly what you
  could not reproduce.
- Do **not** enter/exit play mode repeatedly, run long probes, or sweep the whole UI
  looking for related issues unless the user asks.
- Do **not** edit scene/prefab/asset files to "fix" something when a code-level change
  can do it, and never leave test objects, probe screenshots or logging behind.
- One quick, targeted check is fine (compile, single value/invoke comparison). If a live
  probe would help you *diagnose* a real unknown, keep it to a single short session and
  clean up afterwards.
- Prefer self-contained changes in the script that owns the behaviour over touching
  shared UI prefabs, so the user can review a small diff.

See `.clinerules/testing-and-profiling.md` for the measurement tooling and its traps
(in particular: the editor throttles to ~10 fps when unfocused, and vsync caps
everything at the display rate).