using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DwarfCorp.Gui;
using Microsoft.Xna.Framework;

namespace DwarfCorp.Gui.Widgets
{
    /// <summary>
    /// In-game profiler UI. While visible it forces
    /// <see cref="PerformanceMonitor.EnableFrameCapture"/> to true so per-function timings
    /// continue to be captured even when the debug console is hidden. Exports a CSV snapshot
    /// at a user-selected history range — convenient for handing to an AI assistant to
    /// triage framerate issues offline.
    /// </summary>
    public class ProfilerPanel : Window
    {
        private const int DefaultWidth = 520;
        private const int DefaultHeight = 420;

        private Widget FunctionListContainer;
        private Widget FpsReadout;
        private Widget LastExportLabel;
        private ComboBox RangeCombo;

        // Refresh the displayed breakdown only a few times per second to keep the panel cheap.
        private double _refreshAccumulator = 0.0;
        private const double RefreshInterval = 0.25;

        // Map ComboBox index → history frame count (0 means "all").
        private static readonly int[] RangeFrames = new[] { 60, 300, 600, 0 };
        private static readonly string[] RangeLabels = new[]
        {
            "Last ~1s (60 frames)",
            "Last ~5s (300 frames)",
            "Last ~10s (600 frames)",
            "All captured history"
        };

        public override void Construct()
        {
            StartingSize = new Rectangle(64, 64, DefaultWidth, DefaultHeight);
            base.Construct();

            Text = "Profiler";
            TextColor = new Vector4(1, 1, 1, 1);
            Font = "font16";

            // --- Top row: range selector + Export + Close buttons ----------------------
            var topRow = AddChild(new Widget
            {
                AutoLayout = AutoLayout.DockTop,
                MinimumSize = new Point(0, 32),
                Padding = new Margin(2, 2, 4, 4),
                Transparent = true
            });

            topRow.AddChild(new Widget
            {
                Text = "Range:",
                AutoLayout = AutoLayout.DockLeft,
                MinimumSize = new Point(60, 0),
                TextColor = new Vector4(1, 1, 1, 1),
                TextVerticalAlign = VerticalAlign.Center
            });

            RangeCombo = topRow.AddChild(new ComboBox
            {
                AutoLayout = AutoLayout.DockLeft,
                MinimumSize = new Point(200, 24),
                TextColor = new Vector4(0, 0, 0, 1)
            }) as ComboBox;
            RangeCombo.Items.AddRange(RangeLabels);
            RangeCombo.SilentSetSelectedIndex(2); // default: last 10s

            var exportButton = topRow.AddChild(new Button
            {
                Text = "Export CSV",
                AutoLayout = AutoLayout.DockRight,
                MinimumSize = new Point(110, 24),
                OnClick = (sender, args) => DoExport()
            });

            var closeButton = topRow.AddChild(new Button
            {
                Text = "Close",
                AutoLayout = AutoLayout.DockRight,
                MinimumSize = new Point(70, 24),
                OnClick = (sender, args) => { Hidden = true; Invalidate(); }
            });

            // --- FPS readout ----------------------------------------------------------
            FpsReadout = AddChild(new Widget
            {
                AutoLayout = AutoLayout.DockTop,
                MinimumSize = new Point(0, 24),
                Text = "FPS: --   Frame time: --",
                TextColor = new Vector4(1, 1, 1, 1),
                Font = "font10",
                TextHorizontalAlign = HorizontalAlign.Left,
                TextVerticalAlign = VerticalAlign.Center,
                Padding = new Margin(2, 2, 8, 4)
            });

            LastExportLabel = AddChild(new Widget
            {
                AutoLayout = AutoLayout.DockBottom,
                MinimumSize = new Point(0, 20),
                Text = "",
                TextColor = new Vector4(1, 1, 0.6f, 1),
                Font = "font8",
                TextHorizontalAlign = HorizontalAlign.Left,
                TextVerticalAlign = VerticalAlign.Center,
                Padding = new Margin(2, 2, 8, 4)
            });

            // --- Scrolling-ish function list (simple column of rows) ------------------
            FunctionListContainer = AddChild(new Widget
            {
                AutoLayout = AutoLayout.DockFill,
                Padding = new Margin(2, 2, 8, 8),
                Transparent = true
            });

            OnUpdate = (sender, time) =>
            {
                // Track visibility transitions to flip the capture flag without overriding
                // the Hidden property. This runs every frame so is effectively immediate.
                if (_wasHidden && !Hidden)
                    PerformanceMonitor.EnableFrameCapture = true;
                else if (!_wasHidden && Hidden)
                    PerformanceMonitor.EnableFrameCapture = false;
                _wasHidden = Hidden;

                if (Hidden) return;
                _refreshAccumulator += time.ElapsedGameTime.TotalSeconds;
                if (_refreshAccumulator >= RefreshInterval)
                {
                    _refreshAccumulator = 0;
                    RefreshContents();
                }
            };
            Root.RegisterForUpdate(this);

            _wasHidden = Hidden;
            PerformanceMonitor.EnableFrameCapture = !Hidden;
        }

        private bool _wasHidden = false;

        private void RefreshContents()
        {
            var history = PerformanceMonitor.GetFrameHistory(60);
            if (history.Count > 0)
            {
                var latest = history[history.Count - 1];
                FpsReadout.Text = string.Format("FPS: {0:000}   Frame time: {1:0.00} ms   (capture: {2})",
                    latest.Fps, latest.FrameTimeMs,
                    PerformanceMonitor.EnableFrameCapture ? "on" : "off");
                FpsReadout.Invalidate();
            }

            var functions = PerformanceMonitor.GetLastFrameFunctions()
                .OrderByDescending(f => f.FrameTicks)
                .Take(20)
                .ToList();

            // Rebuild list widgets — cheap at 4 Hz for ≤ 20 rows.
            FunctionListContainer.Clear();
            if (functions.Count == 0)
            {
                FunctionListContainer.AddChild(new Widget
                {
                    AutoLayout = AutoLayout.DockTop,
                    MinimumSize = new Point(0, 18),
                    Text = "No frame captures yet. Move the camera or interact with the world.",
                    TextColor = new Vector4(0.9f, 0.9f, 0.9f, 1f),
                    Font = "font10"
                });
            }
            else
            {
                long swFreq = System.Diagnostics.Stopwatch.Frequency;
                foreach (var f in functions)
                {
                    long micros = swFreq > 0 ? (f.FrameTicks * 1_000_000L) / swFreq : 0;
                    FunctionListContainer.AddChild(new Widget
                    {
                        AutoLayout = AutoLayout.DockTop,
                        MinimumSize = new Point(0, 16),
                        Text = string.Format("{0,7} µs  [{1,3}x]  {2}", micros, f.FrameCalls, f.Name),
                        TextColor = new Vector4(1, 1, 1, 1),
                        Font = "font10"
                    });
                }
            }

            FunctionListContainer.Layout();
            FunctionListContainer.Invalidate();
        }

        private void DoExport()
        {
            var rangeIdx = RangeCombo != null ? RangeCombo.SelectedIndex : 2;
            if (rangeIdx < 0 || rangeIdx >= RangeFrames.Length) rangeIdx = 2;
            var frames = RangeFrames[rangeIdx];

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var fileName = string.Format("profile_{0}.csv", stamp);
            string path;
            try
            {
                // Drop it next to the working directory so the user can find it easily.
                path = Path.Combine(Directory.GetCurrentDirectory(), fileName);
                PerformanceMonitor.ExportCsv(path, frames);
                LastExportLabel.Text = "Exported: " + path;
            }
            catch (Exception e)
            {
                LastExportLabel.Text = "Export failed: " + e.Message;
            }
            LastExportLabel.Invalidate();
        }
    }
}
