using System;
using System.Threading;

namespace DwarfCorp
{
    /// <summary>
    /// Cross-thread atomic perf counters sampled into <see cref="PerformanceMonitor"/>
    /// metrics once per frame on the main thread.
    ///
    /// Exists because <c>PerformanceMonitor.PushFrame</c> uses <c>[ThreadStatic]</c>
    /// state — scopes pushed from worker threads (WaterManager.UpdateWater on an
    /// AutoScaleThread, PlanService workers) never show up in the render-thread CSV
    /// dump. Instead, workers bump these atomic fields with <see cref="Interlocked"/>,
    /// and <see cref="SnapshotIntoMetrics"/> runs once per frame on the main thread
    /// to publish deltas as named metrics. "ThisFrame" counters reset on snapshot
    /// via <see cref="Interlocked.Exchange(ref long, long)"/>; state counters
    /// (<see cref="WaterTickMicros"/>, <see cref="PlansMaxMicrosSession"/>) don't.
    /// </summary>
    public static class PerfCounters
    {
        // Water — updated by WaterManager.UpdateWater on the AutoScaleThread.
        public static long WaterTickMicros;            // last completed tick duration; state (not reset)
        public static long WaterDirtyCellsProcessed;   // accumulator, reset per frame
        public static long WaterDirtyChunksRebuilt;    // accumulator, reset per frame
        public static long LiquidGeomRebuilds;         // accumulator, reset per frame

        // Fase B.2 live — updated by geometry builder workers during chunk rebuild.
        // Lets the CSV show how effective greedy meshing is in practice (cells that
        // went through the mask vs. how many merged rectangles were emitted).
        public static long GreedyCellsMaskedThisFrame;       // accumulator, reset per frame
        public static long GreedyRectanglesEmittedThisFrame; // accumulator, reset per frame

        // Pathfinding — updated by PlanService worker threads.
        public static long PlansQueued;                // bumped by Service.AddRequest, accumulator
        public static long PlansStarted;               // bumped at HandleRequest entry, accumulator
        public static long PlansSucceeded;             // accumulator
        public static long PlansFailed;                // accumulator (Invalid + NoSolution + MaxExpansions)
        public static long PlansCancelled;             // accumulator
        public static long PlansMicrosSum;             // accumulator for averaging
        public static long PlansCompletedThisAccum;    // accumulator — count of completed plans feeding PlansMicrosSum
        public static long PlansMaxMicrosSession;      // running max since session start; state (not reset)

        /// <summary>Pull-based queue-depth sampler — PlanService wires this up at
        /// construction so the snapshot can read <c>Requests.Count</c> without a
        /// hard dependency from Tools/ → Planning/.</summary>
        public static Func<int> PathfindingQueueDepthSampler;

        /// <summary>Atomically set running max (lock-free).</summary>
        public static void UpdateMax(ref long field, long candidate)
        {
            long current;
            do
            {
                current = Volatile.Read(ref field);
                if (candidate <= current) return;
            } while (Interlocked.CompareExchange(ref field, candidate, current) != current);
        }

        /// <summary>
        /// Publish the per-frame counter deltas as PerformanceMonitor metrics.
        /// Call once per frame on the main thread.
        /// </summary>
        public static void SnapshotIntoMetrics()
        {
            long waterTick = Interlocked.Read(ref WaterTickMicros);
            long cells = Interlocked.Exchange(ref WaterDirtyCellsProcessed, 0);
            long chunks = Interlocked.Exchange(ref WaterDirtyChunksRebuilt, 0);
            long liqRebuilds = Interlocked.Exchange(ref LiquidGeomRebuilds, 0);

            long greedyCells = Interlocked.Exchange(ref GreedyCellsMaskedThisFrame, 0);
            long greedyRects = Interlocked.Exchange(ref GreedyRectanglesEmittedThisFrame, 0);

            long planQueuedDelta = Interlocked.Exchange(ref PlansQueued, 0);
            long planStarted = Interlocked.Exchange(ref PlansStarted, 0);
            long planSucc = Interlocked.Exchange(ref PlansSucceeded, 0);
            long planFail = Interlocked.Exchange(ref PlansFailed, 0);
            long planCanc = Interlocked.Exchange(ref PlansCancelled, 0);
            long planMicrosSum = Interlocked.Exchange(ref PlansMicrosSum, 0);
            long planDone = Interlocked.Exchange(ref PlansCompletedThisAccum, 0);
            long planMaxSess = Interlocked.Read(ref PlansMaxMicrosSession);
            int queueDepth = PathfindingQueueDepthSampler?.Invoke() ?? 0;

            PerformanceMonitor.SetMetric("Water.LastTickMicros", waterTick);
            PerformanceMonitor.SetMetric("Water.DirtyCellsProcessed", cells);
            PerformanceMonitor.SetMetric("Water.DirtyChunksRebuilt", chunks);
            PerformanceMonitor.SetMetric("Water.LiquidGeomRebuildsThisFrame", liqRebuilds);

            PerformanceMonitor.SetMetric("Greedy.CellsMaskedThisFrame", greedyCells);
            PerformanceMonitor.SetMetric("Greedy.RectanglesEmittedThisFrame", greedyRects);
            PerformanceMonitor.SetMetric("Greedy.MergeRatio", greedyRects > 0 ? ((double)greedyCells / greedyRects).ToString("F2") : "0");

            PerformanceMonitor.SetMetric("Pathfinding.PlansQueuedThisFrame", planQueuedDelta);
            PerformanceMonitor.SetMetric("Pathfinding.PlansStartedThisFrame", planStarted);
            PerformanceMonitor.SetMetric("Pathfinding.PlansSucceededThisFrame", planSucc);
            PerformanceMonitor.SetMetric("Pathfinding.PlansFailedThisFrame", planFail);
            PerformanceMonitor.SetMetric("Pathfinding.PlansCancelledThisFrame", planCanc);
            PerformanceMonitor.SetMetric("Pathfinding.AvgPlanMicros", planDone > 0 ? planMicrosSum / planDone : 0L);
            PerformanceMonitor.SetMetric("Pathfinding.MaxPlanMicrosSession", planMaxSess);
            PerformanceMonitor.SetMetric("Pathfinding.QueueDepth", queueDepth);

            Gui.Debug.GcTracker.PublishMetrics();
        }
    }
}
