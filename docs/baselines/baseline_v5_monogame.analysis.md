# baseline_v5_monogame.csv — analysis

**Captured:** 2026-04-19T22:45:44Z, via ProfilerPanel (F11) Export CSV button.
**Build:** post-M.4, commit `2ca7bfbf7`-ish (right before the M.5 framework commit).
**Session:** Main menu → New Game → Quickplay, left running idle. 600 frames
in the ring buffer (~20 s of the session captured — the buffer is fixed-size,
so earlier frames rolled out).

## Percentiles

| | Frame ms | FPS equivalent |
|---|---|---|
| **p50** | 16.85 | 59.3 |
| p95 | 89.86 | 11.1 |
| p99 | 559.21 | 1.8 |
| max | 1060.83 | 0.9 |

Median frame hits 60 FPS. Problem is the long tail.

## Hitch distribution (600 frames)

- 81 frames > 33 ms (13.5 %) — drop below 30 fps
- 27 frames > 100 ms (4.5 %) — noticeable stutter
- 7 frames > 500 ms (1.2 %) — real hitches, player-visible freeze

## Last captured frame, top functions (µs)

```
Root              4199
Render            3228
Update             969
GUI Mesh Gen       763     ← 18 % of the frame, regenerated every tick
AISystem           121
Component Update   117
PhysicsSystem       35
EnumRootLFrus       34
Transform Update     4
ChunkRenderer.Update 1
```

## Named metrics (session totals)

```
GUI Mesh Size     3064 triangles
VISIBLE CHUNKS    119
Physics Objects    19
Transforms         90
COMPONENTS        748   (~8 components per entity)
ENTITIES UPDATED   90
AI Objects         16
INSTANCES DRAWN   235
```

## Diagnosis

Steady-state performance is fine (median frame ≈ 60 FPS on this hardware).
The Steam reviews reporting "FPS drops in long sessions" / "freezes with 18+
dwarfs" are almost certainly describing the **long-tail hitches**, not a
general CPU bound. That matches the shape of this data: p50 ≪ p95 ≪ p99.

Three likely sources for the hitches, ordered by confidence:

1. **Chunk rebuild on the main thread.** The parallelization attempt
   (original Fase 1.1) was reverted for heap corruption, so chunk mesh
   regen currently blocks the render thread. Any dig/place event can park
   the frame for however long the rebuild takes. Matches the 500 ms - 1 s
   hitches we see.
2. **GC Gen2 pauses.** ArrayPool adoption is still at zero. `ComponentManager`
   and spatial queries still churn collection allocations per frame.
3. **GUI mesh re-generation.** 763 µs of the last frame was spent rebuilding
   GUI geometry. That's 18 % of a 4 ms frame on a scene that looked
   completely static to the profiler — the current GUI system invalidates
   aggressively.

## What this validates in the plan

- **Fase B.1** (split mesh-gen from GPU upload) targets hitch #1 head-on.
  It's ordered first in the CPU-side work for a reason.
- **Fase C.3** (ArrayPool in spatial queries) + **Fase L.4** (Arch ECS)
  attack hitch #2.
- **TODO item 35** (full GUI migration to ImGui) eventually retires the
  763 µs/frame GUI cost — deferred but on the list.

## Caveats

- Only 600 frames in the ring buffer, so this captures the last ~20 s, not
  a representative 10-min window.
- Session didn't actively exercise chunk rebuild (no dig orders issued) —
  the real hitch frequency under active gameplay is almost certainly higher
  than what's here.
- `GameSettings.EnableOutline = false` for this run (TODO 28 / viewport
  bug) — real render path with outline on might add post-FX cost.
- Built on `Debug` configuration. A `Release` baseline would be more
  representative of shipped-game behaviour; capture one of those after
  meaningful perf work lands.
