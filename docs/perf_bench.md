# DwarfCorp Performance Benchmark Protocol

Reproducible recipe for capturing profiler CSVs that can be compared across
migration milestones. Every baseline we save under [`docs/baselines/`](./baselines/)
should have been generated with this exact procedure — otherwise the diffs
don't mean anything.

## When to capture a baseline

Capture (or re-capture) whenever a milestone lands that could move the perf
needle:

| Milestone | Baseline filename |
|---|---|
| Current FNA 26 Vulkan state before any migration work | `baseline_v5_fna.csv` |
| After M.1-M.5 (MonoGame port complete, no optimizations yet) | `baseline_v5_monogame.csv` |
| After Fase B (chunk rebuild refactor) | `baseline_v5_cpu.csv` |
| After Fase L.4 (Arch ECS migrated) | `baseline_v5_arch.csv` |

## Canonical stress scene

The benchmark needs to be the same every run. Use the **stock Quickplay**
settings:

1. Launch the game (`dotnet run --project DwarfCorp/DwarfCorpFNA.csproj`) with
   `DWARFCORP_QUICKPLAY` unset — we want the main menu.
2. Main menu → **New Game → Quickplay**. This path seeds the world
   deterministically and spawns the default dwarf roster (11 dwarfs with the
   default job mix: Miners / Crafters / Farmer / Manager / Musketeer / Soldier
   / Wizard — matches what the launcher pushes).
3. Accept every default. Don't tweak WorldSize, seed, difficulty, or dwarf
   count — those are the ones the roll-out table assumes.
4. Wait until `LoadState` finishes pushing `PlayState` (you'll see dwarfs
   spawn and the minimap light up).
5. Don't give orders, don't pan the camera aggressively. Park the camera at
   the starting view. The goal is to measure **the baseline simulation +
   render load with no player input**, not benchmark input handling.
6. Let the game run for **10 minutes of wall clock** before capturing.
   (The first ~1 min is noisy while chunks are still generating; the
   profiler's ring buffer is long enough to capture the whole stable window.)
7. Quit cleanly (Alt-F4 or the menu's Exit). The auto-export hook in
   `FNAProgram.SignalShutdown` writes the CSV if `DWARFCORP_PERF_EXPORT`
   is set (see below).

## How to capture

### One-line scripted (preferred)

```powershell
# Windows PowerShell
$env:DWARFCORP_PERF_EXPORT = "docs\baselines\baseline_v5_monogame.csv"
dotnet run --project DwarfCorp/DwarfCorpFNA.csproj
# …play the scene for 10 min, then quit cleanly…
Remove-Item Env:DWARFCORP_PERF_EXPORT
```

On shutdown you should see `[PERF] Exported profiler history -> docs\baselines\baseline_v5_monogame.csv`
in stdout.

### Manual (F11 panel)

1. Open the Profiler Panel with **F11** during the run.
2. Use the "Range" dropdown to select how many frames to include.
3. Click **Export CSV**. The file lands wherever the panel is configured to
   write (usually alongside the exe in `bin/…`); move it into
   `docs/baselines/` with the correct filename.

## What the CSV contains

- Frame history (ms per frame, FPS) for up to N frames.
- Per-function hot-path timings (ticks, calls/frame) from the last captured
  frame.
- Named metrics (counters we bump via `PerformanceMonitor.SetMetric(...)`).
- Header comments with UTC timestamp and capture settings.

Everything is plain UTF-8 CSV with `#`-prefixed header lines, so diff tools
and spreadsheet imports both work cleanly.

## What to compare

For each migration milestone, write up the delta in the milestone's commit
message or in `PERF_PLAN_EXTREME.md`. The three numbers that matter most:

1. **p50 frame ms** — median frame time. If this regresses we have a problem.
2. **p95 frame ms** — tail latency. A big gap between p50 and p95 means
   occasional hitches.
3. **GC Gen2 collections over the 10-min window** — stays at 0 in a healthy
   steady state. Anything non-zero flags an allocation leak somewhere.

If profiling surfaces a specific hot function (say `ComponentManager.Update`
jumping from 0.3ms to 2ms), that goes in the commit message for the
milestone and becomes the input for the next subsystem rewrite in
[`PERF_PLAN_EXTREME.md`](../PERF_PLAN_EXTREME.md).

## Known caveats

- First launch after clean build is slower because of JIT + content cache
  priming. Always throw out the first capture; use the second or third run.
- `[DWARFCORP_AUTOEXIT_SECONDS]` from `run-quick.ps1` terminates early and
  doesn't give the 10-min window time to stabilize. **Don't use
  `run-quick.ps1` for baselines** — it's a smoke-test tool, not a
  benchmark harness.
- The post-migration "3D viewport is all black when outline is on" issue
  (TODO 28) means `GameSettings.EnableOutline` must stay `false` during
  baselines until that's fixed, otherwise you're measuring "render nothing"
  instead of the real render path.
