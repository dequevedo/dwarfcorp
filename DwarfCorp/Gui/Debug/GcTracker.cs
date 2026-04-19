using System;
using System.Collections.Generic;

namespace DwarfCorp.Gui.Debug
{
    /// <summary>
    /// Snapshots GC state every time <see cref="Sample"/> is called and
    /// keeps a rolling record of Gen 2 collection events with their
    /// timestamps. The Render Inspector reads it each frame to surface
    /// correlation between frame hitches and Gen 2 pauses — the main
    /// remaining suspect for the 500-1500 ms tail hitches baseline_v5_cpu.csv
    /// didn't fix.
    ///
    /// Zero overhead when not polled: <see cref="GC.CollectionCount(int)"/>
    /// is a one-instruction read.
    /// </summary>
    public static class GcTracker
    {
        private const int MaxGen2Events = 16;

        private static int _lastGen0, _lastGen1, _lastGen2;
        private static long _lastTotalBytes;
        private static DateTime _lastSample = DateTime.UtcNow;

        private static int _gen0Delta, _gen1Delta, _gen2Delta;
        private static long _allocBytesSinceLastSample;

        private static readonly Queue<DateTime> _recentGen2Events = new(MaxGen2Events);

        public static int Gen0Count => _lastGen0;
        public static int Gen1Count => _lastGen1;
        public static int Gen2Count => _lastGen2;
        public static long TotalMemoryBytes => _lastTotalBytes;

        public static int Gen0Delta => _gen0Delta;
        public static int Gen1Delta => _gen1Delta;
        public static int Gen2Delta => _gen2Delta;
        public static long AllocBytesSinceLastSample => _allocBytesSinceLastSample;

        /// <summary>Seconds since the most recent Gen 2 event the tracker observed, or -1 if none yet.</summary>
        public static double SecondsSinceLastGen2()
        {
            if (_recentGen2Events.Count == 0) return -1;
            return (DateTime.UtcNow - PeekNewest(_recentGen2Events)).TotalSeconds;
        }

        /// <summary>Snapshot of the recent Gen 2 event timestamps (most recent first).</summary>
        public static DateTime[] RecentGen2Events()
        {
            var arr = _recentGen2Events.ToArray();
            Array.Reverse(arr);
            return arr;
        }

        /// <summary>
        /// Poll current GC state. Call once per frame from the render-loop
        /// panel draw; it's a few reads + a small queue update.
        /// </summary>
        public static void Sample()
        {
            int g0 = GC.CollectionCount(0);
            int g1 = GC.CollectionCount(1);
            int g2 = GC.CollectionCount(2);
            long mem = GC.GetTotalMemory(forceFullCollection: false);
            var now = DateTime.UtcNow;

            _gen0Delta = g0 - _lastGen0;
            _gen1Delta = g1 - _lastGen1;
            _gen2Delta = g2 - _lastGen2;

            // Signed delta: can go negative right after a collection freed memory.
            _allocBytesSinceLastSample = mem - _lastTotalBytes;

            if (_gen2Delta > 0)
            {
                for (int i = 0; i < _gen2Delta; i++)
                {
                    if (_recentGen2Events.Count >= MaxGen2Events)
                        _recentGen2Events.Dequeue();
                    _recentGen2Events.Enqueue(now);
                }
            }

            _lastGen0 = g0;
            _lastGen1 = g1;
            _lastGen2 = g2;
            _lastTotalBytes = mem;
            _lastSample = now;
        }

        private static DateTime PeekNewest(Queue<DateTime> q)
        {
            // Queue exposes the front; for newest, copy and grab the last. Cheap at
            // MaxGen2Events = 16.
            DateTime newest = default;
            foreach (var t in q) newest = t;
            return newest;
        }
    }
}
