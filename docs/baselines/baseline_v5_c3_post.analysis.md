# baseline_v5_c3_post.csv — analysis

**Captured:** 2026-04-20T00:45:26Z (10-min Quickplay idle run).
**Build:** post-C.3 per-frame allocator fixes, commit `1028ba68d`.
**Scene density:** COMPONENTS=2225, INSTANCES=1073 — roughly **2× denser**
than `baseline_v5_cpu.csv` (COMPONENTS=1094, INSTANCES=969). The two
captures are idle Quickplay sessions but the c3_post run sat longer
before the ring buffer rolled, so it captured a later game state. Keep
that confounder in mind when reading the deltas.

## Side-by-side with baseline_v5_cpu.csv

| Metric | cpu (post-B.1) | c3_post | Delta |
|---|---|---|---|
| p50 frame ms | 16.65 | 17.31 | +4 % |
| **p95 frame ms** | **69.13** | **34.35** | **−50 %** |
| **p99 frame ms** | **683.00** | **328.75** | **−52 %** |
| max frame ms | 1500 | 3189 | — (shutdown-frame outlier) |
| hitches > 50 ms | 39 | 21 | **−46 %** |
| hitches > 100 ms | 23 | 18 | −22 % |
| COMPONENTS | 1094 | 2225 | +103 % (scene density) |
| INSTANCES DRAWN | 969 | 1073 | +11 % |

## What C.3 delivered

- **p95 tail latency halved** (69 → 34 ms). Hitches that used to push
  the 95th percentile up are fewer and shorter. This is the change
  the user-visible "FPS oscila muito" complaint was tracking.
- **p99 halved** (683 → 329 ms). The big pauses didn't vanish, but
  they're smaller and rarer.
- **hitches > 50 ms down 46 %** even with the scene **2× denser**. If
  scene density had held constant, the improvement would be larger.
- p50 barely moved (+4 %), which is expected: the steady-state frame
  already had plenty of headroom. C.3's target was GC pressure and
  tail hitches, not idle frame cost. Move-the-needle was the tail.

## What landed in commit 1028ba68d

1. `Gui/Root.cs` `Draw()` no longer walks the whole widget tree with
   `EnumerateTree` (recursive yield-iterator) just to call empty base
   `PostDraw` overrides. Routes through the existing
   `PostdrawItems` registration list instead. `TutorialIcon` now
   registers at `Construct`.
2. `WorldRenderer.Render`: big LINQ chain
   (`EnumerateIntersectingRootObjectsLoose.SelectMany(r => r.EnumerateAll()).Where(...)`)
   enumerated 3 × per frame is gone. New `EnumerateAllInto(List)` is
   an iterative walk (zero allocation). New fill-overload of
   `EnumerateIntersectingRootObjectsLoose(frustum, HashSet)` lets the
   caller reuse a single scratch set across frames.
   `ComponentRenderer` / `WaterRenderer` moved from
   `IEnumerable<GameComponent>` to `List<GameComponent>` so `foreach`
   uses the struct enumerator (no boxing).
3. Scissor `RasterizerState` for the 2D SpriteBatch pass was `new`'d
   every frame (MonoGame tracks state objects on the GPU side, so
   this leaks into the finalizer queue). Cached as `static readonly`.

## What C.3 did not fix

- Hitches in the 100-800 ms range are still present, just fewer
  (18 vs 23 over 600 frames, ≈ 3 % of frames). Something still
  allocates heavily enough to provoke occasional Gen-2 collections.
- The `GcTracker` widget in the Render Inspector (F12) is the
  right live signal — the user observed a red/yellow Gen 0 rate
  even at idle. That meter is still the daily driver for ongoing
  C.3 hunts.

## Leading suspects for the remaining hitches

1. **PhysicsSystem.Update** — not instrumented yet, allocations in
   the narrow-phase collision path suspected.
2. **ParticleManager** — particles are notorious for per-emission
   allocation in this codebase.
3. **AISystem** — plan cache rebuild and task churn are obvious
   candidates.
4. **Mid-session content loads** — if a new entity type spawns and
   drags a fresh XNB through the loader on the main thread, that
   alone is a hundreds-of-ms stall. Visible only in logs.
5. **ChunkMeshGen burst peaks** — steady state fine, but a
   multi-chunk invalidation (day/night cycle, large water flow, any
   dig-heavy activity) could still park the render thread on the
   GPU upload step even with B.1's split. Needs a dig-heavy
   capture to isolate.

## What to do next

- **C.3 continuation** — Physics / Particle / AI hot paths get the
  same treatment (scratch list / pooled allocations / remove LINQ
  closures).
- **Re-run with `GC.LatencyMode = GCLatencyMode.SustainedLowLatency`**
  as a diagnostic to see how much of the remaining hitch budget is
  pure GC vs something else. Revert immediately after — SLL is a
  debug-only probe, not a production setting.
- **Dig-heavy capture** — run the same protocol but dig 30+ blocks
  during the 10 min. Stresses the chunk rebuild pipeline to confirm
  B.1's value under load, and exercises the particle emitters +
  designation-update path for the C.3 follow-up hunt.

## Caveats

- 600 frames ≈ last 20 s of a 10-min session. Earlier hitches
  rotated out of the ring buffer.
- Debug build (`dotnet run`). Release build would shrink all
  numbers further.
- Scene density doubled between captures. Future baselines should
  either match save state or explicitly annotate the density deltas.
- The 3189 ms `max` is literally the shutdown frame — the game
  was exiting when the last sample was captured. Not a real hitch.
