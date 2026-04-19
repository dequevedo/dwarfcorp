# baseline_v5_cpu.csv — analysis

**Captured:** 2026-04-19T23:28:05Z, via ProfilerPanel (F11) Export CSV.
**Build:** post-B.1 (both split and parallelize commits active), commit `a12961aa7`.
**Session:** Main menu → New Game → Quickplay, idle-ish play.
**Scene density:** INSTANCES DRAWN = 969 (vs 235 in the monogame baseline),
COMPONENTS = 1094 (vs 748). **Not an apples-to-apples comparison** —
the session ran deeper into gameplay than the monogame baseline did.

## Side-by-side with baseline_v5_monogame.csv

| Metric | monogame | cpu (post-B.1) | Delta |
|---|---|---|---|
| p50 frame ms | 16.85 | 16.65 | ≈ flat |
| p95 frame ms | 89.86 | 69.13 | **−23 %** |
| p99 frame ms | 559 | 683 | +22 % |
| max frame ms | 1061 | 1500 | +41 % |
| Render function µs | 3228 | 2035 | **−37 %** |
| Component Update µs | 117 | 93 | −21 % |
| AI system µs | 121 | 93 | −23 % |
| GUI Mesh Gen µs | 763 | 690 | −10 % |
| INSTANCES DRAWN | 235 | 969 | **+312 %** (scene density) |
| COMPONENTS | 748 | 1094 | +46 % |

## What B.1 actually delivered

- Render cost per frame down 37 %. That's mesh-gen no longer running
  sync on the same thread that's rendering — the exact thing the
  split commit targeted.
- p95 frame time down 23 %, despite a scene 3-4× denser. If the scene
  had held constant, the p95 improvement would be larger.
- Chunk rebuild queue instrumentation is working:
  `ChunkMeshUploadDrain` shows 1 µs per frame, `ChunkMeshGenBatchSize`
  and `ChunkMeshUploadsPending` are live. The queue was essentially
  empty at the capture sample (size = 0 pending), meaning the render
  thread was draining as fast as workers produced.

## What B.1 did not fix

- The big hitches (500 ms – 1500 ms frames) are still there. They
  show up as a cluster in the frame history around seconds 41-59 of
  capture. If they came from chunk rebuild, B.1 would have
  compressed them; it didn't. So they come from somewhere else.

## Leading suspects for the remaining hitches

1. **GC Gen 2 pauses.** .NET's gen 2 collector can park for
   hundreds of milliseconds on a busy heap. DwarfCorp still allocates
   `new List<>` / `new HashSet<>` in several hot paths per frame
   (ComponentManager spatial queries, WaterManager, particle
   emitters). `ArrayPool<T>` adoption in the project is still at
   zero. This is the Fase C.3 target.
2. **GUI mesh regeneration spikes.** Steady state is 690 µs/frame
   for `GUI Mesh Gen`. Under load (menu open, panel changes, minimap
   updates) this almost certainly jumps into the tens of ms — the
   current GUI system invalidates aggressively. Target of the
   eventual TODO 35 (full GUI → ImGui).
3. **Mid-session asset loads.** If a brand-new entity type enters
   the world and triggers a content load, the main thread stalls
   waiting on disk + XNB deserialization. Out of scope until
   observed specifically.
4. **Parallel.ForEach startup cost at tiny batch sizes.** With
   `ChunkMeshGenBatchSize = 2` at the sample, the fixed overhead of
   thread dispatch might outweigh the parallel win on small bursts.
   Real win is at large bursts (dig events). Worth re-testing with
   a dig-heavy session.

## What to do next

- **C.3 (ArrayPool in spatial queries)** is the highest-leverage
  next commit if the goal is killing the tail hitches. Reduces GC
  pressure in the exact codepaths that fire every frame.
- **Dig-heavy benchmark**: run the canonical scene again but dig
  30+ blocks during the capture. This stresses the chunk rebuild
  path where B.1's parallelism pays off. If hitches in THAT
  scenario are compressed vs monogame baseline's hitches, B.1's
  value is proven even if idle tail didn't move.
- **GC hitch correlation**: patch the Render Inspector to show a
  live `GC.CollectionCount(2)` delta alongside the FPS. If hitches
  coincide with Gen 2 ticks, we've confirmed suspect #1 without
  needing a full profiler.

## Caveats (same as monogame baseline)

- 600 frames = ~ the last 20 s of a 10-min session. Earlier
  hitches rotated out of the ring buffer.
- Debug build. Release build would be faster across the board.
- Scene density confounds the comparison — future baselines
  should match scene state more carefully (same save, same time
  offset) if the goal is milestone-to-milestone diffing.
