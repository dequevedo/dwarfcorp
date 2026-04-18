using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using DwarfCorp.Gui;
using Microsoft.Xna.Framework;

namespace DwarfCorp
{
    public static class PerformanceMonitor
    {
        public class PerformanceFunction
        {
            public String Name;
            public int FrameCalls;
            public long FrameTicks;
        }

        /// <summary>
        /// One sample of frame-level timing captured every call to <see cref="Render"/>.
        /// Kept in a rolling buffer so we can export a historical slice for offline analysis.
        /// </summary>
        public struct FrameSample
        {
            public double WallClockSeconds; // seconds since process start
            public float Fps;
            public float FrameTimeMs;
        }

        private class PerformanceFrame
        {
            public PerformanceFrame ParentFrame;
            public Stopwatch Stopwatch;
            public long Ticks;
            public PerformanceFunction Function;
        }

        private static Dictionary<String, Object> Metrics = new Dictionary<String, Object>();

        public static void SetMetric(String Name, Object Value)
        {
            lock (Metrics)
                Metrics.Upsert(Name, Value);
        }

        public static IEnumerable<KeyValuePair<String, Object>> EnumerateMetrics()
        {
            var result = new List<KeyValuePair<String, Object>>();
            lock (Metrics)
            {
                foreach (var metric in Metrics)
                    result.Add(metric);
            }
            return result;
        }

        private static float[] FPSBuffer = new float[100];
        private static int k = 0;
        [ThreadStatic]
        private static PerformanceFrame CurrentFrame;

        [ThreadStatic]
        private static Dictionary<String, PerformanceFunction> Functions = new Dictionary<string, PerformanceFunction>();

        private static Stopwatch FPSWatch = null;
        private static Stopwatch FPSFaultTimer = null;
        private static bool SentPerfReport = false;

        // Rolling history of frame samples (approx 10 s at 60 fps). Captured every Render() call,
        // regardless of whether the debug console is visible, so an Export can always include
        // context.
        private const int HistoryCapacity = 600;
        private static readonly FrameSample[] History = new FrameSample[HistoryCapacity];
        private static int HistoryWrite = 0;
        private static int HistoryCount = 0;
        private static readonly Stopwatch WallClock = Stopwatch.StartNew();
        private static readonly object HistoryLock = new object();

        /// <summary>
        /// Snapshot of the last observed per-function timing. Copied from the thread-static
        /// Functions dict inside Render(), so exports from any thread see the last frame's data.
        /// </summary>
        private static List<PerformanceFunction> LastFrameFunctions = new List<PerformanceFunction>();
        private static readonly object FunctionSnapshotLock = new object();

        /// <summary>
        /// When true, function-level timings are captured each frame even if the debug console
        /// is hidden. Set by the ProfilerPanel while it's visible.
        /// </summary>
        public static bool EnableFrameCapture = false;

        private static bool ShouldCaptureFrames()
        {
            return DwarfGame.IsConsoleVisible || EnableFrameCapture;
        }

        public static void BeginFrame()
        {
            if (ShouldCaptureFrames())
            {
                CurrentFrame = null;
                Functions.Clear();
                __pushFrame("Root");
            }
        }

        public static void Render()
        {
            var FPS = 0;
            double elapsedMilliseconds = 0.0;

            if (FPSWatch == null)
                FPSWatch = Stopwatch.StartNew();
            else
            {
                FPSWatch.Stop();
                FPS = (int)Math.Floor(1.0f / (float)FPSWatch.Elapsed.TotalSeconds);
                elapsedMilliseconds = FPSWatch.Elapsed.TotalMilliseconds;
                FPSWatch = Stopwatch.StartNew();
            }

            FPSBuffer[k % 100] = FPS;
            k++;
            var avgFPS = (int)FPSBuffer.Average();

            // Always capture a history sample so the CSV export has useful context regardless
            // of whether the in-game debug console is open.
            lock (HistoryLock)
            {
                History[HistoryWrite] = new FrameSample
                {
                    WallClockSeconds = WallClock.Elapsed.TotalSeconds,
                    Fps = FPS,
                    FrameTimeMs = (float)elapsedMilliseconds
                };
                HistoryWrite = (HistoryWrite + 1) % HistoryCapacity;
                if (HistoryCount < HistoryCapacity) HistoryCount++;
            }

            if (ShouldCaptureFrames())
            {
                PopFrame();

                // Snapshot function timings for Export so a non-rendering thread can pull them
                // without touching the thread-static dictionary.
                lock (FunctionSnapshotLock)
                {
                    LastFrameFunctions = Functions.Values
                        .Select(f => new PerformanceFunction { Name = f.Name, FrameCalls = f.FrameCalls, FrameTicks = f.FrameTicks })
                        .ToList();
                }
            }

            if (DwarfGame.IsConsoleVisible)
            {
                var output = DwarfGame.GetConsoleTile("PERFORMANCE");
                output.Lines.Clear();
                output.Lines.Add(String.Format("Frame time: {0:000.000}", elapsedMilliseconds));

                foreach (var function in Functions.OrderBy(f => f.Value.FrameTicks).Reverse())
                    output.Lines.Add(String.Format("{1:0000} {2:000} {0}\n", function.Value.Name, function.Value.FrameCalls, function.Value.FrameTicks / 1000));

                output.Invalidate();

                var fps = DwarfGame.GetConsoleTile("FPS");
                if (fps.Children[0] is Gui.Widgets.TextGrid)
                {
                    fps.RemoveChild(fps.Children[0]);
                    fps.AddChild(new Gui.Widgets.Graph()
                    {
                        AutoLayout = AutoLayout.DockFill,
                        ScaleGraphRange = 5.0f
                    });

                    fps.Layout();
                }

                var graph = fps.Children[0] as Gui.Widgets.Graph;
                graph.Values.Add((float)FPSWatch.Elapsed.TotalMilliseconds);
                while (graph.Values.Count > graph.GraphWidth)
                    graph.Values.RemoveAt(0);

                graph.MinLabelString = String.Format("FPS: {0:000} (avg: {1})", FPS, avgFPS);

                graph.Invalidate();
            }
        }

        public static void PushFrame(String Name)
        {
            if (ShouldCaptureFrames() && CurrentFrame != null)
                __pushFrame(Name);
        }

        private static void __pushFrame(String Name)
        {
            PerformanceFunction Function;
            if (!Functions.TryGetValue(Name, out Function))
            {
                Function = new PerformanceFunction
                {
                    Name = Name
                };

                Functions.Add(Name, Function);
            }

            Function.FrameCalls += 1;

            CurrentFrame = new PerformanceFrame
            {
                ParentFrame = CurrentFrame,
                Stopwatch = Stopwatch.StartNew(),
                Function = Function
            };
        }

        public static void PopFrame()
        {
            if (ShouldCaptureFrames() && CurrentFrame != null)
            {
                    CurrentFrame.Stopwatch.Stop();

                    CurrentFrame.Ticks = CurrentFrame.Stopwatch.ElapsedTicks;
                    CurrentFrame.Function.FrameTicks += CurrentFrame.Ticks;
                    CurrentFrame = CurrentFrame.ParentFrame;
            }
        }

        /// <summary>
        /// Returns the most recently snapshotted per-function timings (or empty list if
        /// frame capture is disabled). Safe to call from any thread.
        /// </summary>
        public static List<PerformanceFunction> GetLastFrameFunctions()
        {
            lock (FunctionSnapshotLock)
                return LastFrameFunctions
                    .Select(f => new PerformanceFunction { Name = f.Name, FrameCalls = f.FrameCalls, FrameTicks = f.FrameTicks })
                    .ToList();
        }

        /// <summary>
        /// Returns the last <paramref name="count"/> frame samples from the rolling history,
        /// oldest first. If <paramref name="count"/> &lt;= 0 or exceeds the recorded history,
        /// the entire buffer is returned.
        /// </summary>
        public static List<FrameSample> GetFrameHistory(int count)
        {
            lock (HistoryLock)
            {
                var n = (count <= 0 || count > HistoryCount) ? HistoryCount : count;
                var result = new List<FrameSample>(n);
                var start = (HistoryWrite - n + HistoryCapacity) % HistoryCapacity;
                for (int i = 0; i < n; ++i)
                    result.Add(History[(start + i) % HistoryCapacity]);
                return result;
            }
        }

        /// <summary>
        /// Writes a CSV dump of the last-frame per-function timings, the rolling FPS/frame-time
        /// history, and the current named metrics to <paramref name="path"/>. Intended to be
        /// consumed by external tooling (e.g. an AI assistant triaging perf issues).
        /// </summary>
        /// <param name="lastNFrames">How many frames of history to include; pass 0 for all.</param>
        public static void ExportCsv(string path, int lastNFrames = 0)
        {
            var history = GetFrameHistory(lastNFrames);
            var functions = GetLastFrameFunctions();
            var metrics = EnumerateMetrics().ToList();

            var inv = CultureInfo.InvariantCulture;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (var sw = new StreamWriter(path, false, Encoding.UTF8))
            {
                sw.WriteLine("# DwarfCorp profiler export");
                sw.WriteLine("# generated_utc," + DateTime.UtcNow.ToString("o", inv));
                sw.WriteLine("# frame_capture_enabled," + (ShouldCaptureFrames() ? "1" : "0"));
                sw.WriteLine("# frames_in_history," + history.Count);
                sw.WriteLine();

                sw.WriteLine("# Metrics");
                sw.WriteLine("name,value");
                foreach (var m in metrics)
                    sw.WriteLine(EscapeCsv(m.Key) + "," + EscapeCsv(m.Value == null ? "" : m.Value.ToString()));
                sw.WriteLine();

                sw.WriteLine("# Functions (last captured frame, sorted by ticks desc)");
                sw.WriteLine("name,calls,micros");
                var swFreq = Stopwatch.Frequency; // ticks per second
                foreach (var f in functions.OrderByDescending(f => f.FrameTicks))
                {
                    long micros = swFreq > 0 ? (f.FrameTicks * 1_000_000L) / swFreq : 0;
                    sw.WriteLine(EscapeCsv(f.Name) + "," + f.FrameCalls.ToString(inv) + "," + micros.ToString(inv));
                }
                sw.WriteLine();

                sw.WriteLine("# Frame history (rolling ring buffer, oldest first)");
                sw.WriteLine("t_seconds,fps,frame_ms");
                foreach (var s in history)
                    sw.WriteLine(
                        s.WallClockSeconds.ToString("F3", inv) + "," +
                        s.Fps.ToString("F1", inv) + "," +
                        s.FrameTimeMs.ToString("F3", inv));
            }
        }

        private static string EscapeCsv(string value)
        {
            if (value == null) return "";
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
