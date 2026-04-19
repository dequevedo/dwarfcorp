using System;
using DwarfCorp.GameStates;
using ImGuiNET;
using Microsoft.Xna.Framework;

namespace DwarfCorp.Gui.Debug
{
    /// <summary>
    /// First ImGui panel shipped with L.1. Read-only diagnostic window:
    /// FPS, frame time, active GameState, and basic GraphicsDevice info —
    /// specifically the kind of data needed to diagnose the post-M.5
    /// invisible-main-menu issue (is the state stack right? is the backbuffer
    /// the expected size? are we rendering at all?).
    ///
    /// Toggled with F12 via <see cref="ImGuiService"/>.
    /// </summary>
    public sealed class DebugOverlay : IDebugPanel
    {
        private float _fpsSmoothed;
        private int _frameCount;
        private double _frameAccumMs;
        private double _lastSampleMs;

        public void Draw(GameTime time)
        {
            AccumulateFps(time);

            ImGui.SetNextWindowPos(new System.Numerics.Vector2(8, 8), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(340, 200), ImGuiCond.FirstUseEver);

            if (!ImGui.Begin("DwarfCorp Debug (F12)"))
            {
                ImGui.End();
                return;
            }

            ImGui.Text($"FPS: {_fpsSmoothed:0.0}");
            ImGui.Text($"Frame: {time.ElapsedGameTime.TotalMilliseconds:0.00} ms");
            ImGui.Separator();

            var game = GameState.Game;
            if (game != null && game.GraphicsDevice != null && !game.GraphicsDevice.IsDisposed)
            {
                var pp = game.GraphicsDevice.PresentationParameters;
                ImGui.Text($"Backbuffer: {pp.BackBufferWidth} x {pp.BackBufferHeight}");
                ImGui.Text($"MSAA: {pp.MultiSampleCount}");
                ImGui.Text($"VSync: {game.Graphics?.SynchronizeWithVerticalRetrace}");
            }
            else
            {
                ImGui.TextColored(new System.Numerics.Vector4(1, 0.4f, 0.4f, 1), "GraphicsDevice unavailable");
            }
            ImGui.Separator();

            ImGui.Text("Game state stack:");
            var active = SafeActiveStateName();
            ImGui.Indent();
            ImGui.Text(active ?? "(none)");
            ImGui.Unindent();

            ImGui.End();
        }

        private void AccumulateFps(GameTime time)
        {
            _frameCount++;
            _frameAccumMs += time.ElapsedGameTime.TotalMilliseconds;
            _lastSampleMs += time.ElapsedGameTime.TotalMilliseconds;
            if (_lastSampleMs >= 250)
            {
                _fpsSmoothed = (float)(_frameCount * 1000.0 / _frameAccumMs);
                _frameCount = 0;
                _frameAccumMs = 0;
                _lastSampleMs = 0;
            }
        }

        private static string SafeActiveStateName()
        {
            try
            {
                return GameStateManager.ActiveState?.GetType().Name;
            }
            catch { return "(error)"; }
        }
    }
}
