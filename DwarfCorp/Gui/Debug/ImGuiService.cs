using System;
using System.Collections.Generic;
using ImGuiNET;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using ZLogger;

namespace DwarfCorp.Gui.Debug
{
    /// <summary>
    /// Owns the single ImGui renderer for the game and dispatches per-frame
    /// draws to registered debug panels. Starts disabled; F12 toggles visibility.
    ///
    /// This is the foundation for the L.1 step — debug/dev UI on top of the
    /// existing DwarfCorp/Gui/* system. Future work that ports the game's UI
    /// (ProfilerPanel, build menus, stockpiles) over to ImGui plugs into the
    /// same <see cref="RegisterPanel"/> registry.
    /// </summary>
    public static class ImGuiService
    {
        private static ImGuiRenderer _renderer;
        private static readonly List<IDebugPanel> _panels = new();
        private static KeyboardState _prevKeyboard;
        private static bool _visible;
        private static ILogger _log;

        public static bool IsInitialized => _renderer != null;
        public static bool Visible => _visible;

        /// <summary>Called once from DwarfGame.Initialize after the GraphicsDevice exists.</summary>
        public static void Initialize(Game game)
        {
            if (_renderer != null) return;

            _log = DwarfCorp.Infrastructure.Services.GetLogger("ImGuiService");
            try
            {
                _renderer = new ImGuiRenderer(game);
                _renderer.RebuildFontAtlas();
                RegisterPanel(new DebugOverlay());
                _log.ZLogInformation($"ImGui initialized with {_panels.Count} panel(s)");
            }
            catch (Exception ex)
            {
                _log.ZLogError($"ImGui init failed: {ex.Message}");
                _renderer = null;
            }
        }

        public static void RegisterPanel(IDebugPanel panel)
        {
            if (panel == null) return;
            _panels.Add(panel);
        }

        /// <summary>
        /// Called once per frame from DwarfGame.Draw, after all game content has
        /// already rendered. ImGui sits on top as an overlay.
        /// </summary>
        public static void Render(GameTime time)
        {
            if (_renderer == null) return;

            ProcessHotkeys();

            if (!_visible)
                return;

            try
            {
                _renderer.BeforeLayout(time);
                for (int i = 0; i < _panels.Count; i++)
                {
                    try { _panels[i].Draw(time); }
                    catch (Exception ex) { _log.ZLogError($"Panel {_panels[i].GetType().Name} threw: {ex.Message}"); }
                }
                _renderer.AfterLayout();
            }
            catch (Exception ex)
            {
                _log.ZLogError($"ImGui render threw: {ex.Message}");
            }
        }

        private static void ProcessHotkeys()
        {
            var kb = Keyboard.GetState();
            // Edge-detect F12 press (not hold).
            if (kb.IsKeyDown(Keys.F12) && _prevKeyboard.IsKeyUp(Keys.F12))
            {
                _visible = !_visible;
                _log?.ZLogInformation($"ImGui debug overlay {(_visible ? "shown" : "hidden")}");
            }
            _prevKeyboard = kb;
        }
    }

    /// <summary>A panel that renders into an existing ImGui frame.</summary>
    public interface IDebugPanel
    {
        void Draw(GameTime time);
    }
}
