using System;
using DwarfCorp.GameStates;
using ImGuiNET;
using Microsoft.Xna.Framework;

namespace DwarfCorp.Gui.Debug
{
    /// <summary>
    /// ImGui panel aimed at the post-migration "3D viewport is all black"
    /// mystery. Shows the render-state that's easy to get wrong in the
    /// FNA → MonoGame DX11 port (render target, viewport, backbuffer,
    /// shader parameter sanity) and exposes the <see cref="Shader.TransposeMatrices"/>
    /// toggle so the matrix-convention hypothesis can be tested in-game
    /// without rebuilding. Toggled together with the other panels via F12.
    /// </summary>
    public sealed class RenderStateInspector : IDebugPanel
    {
        public void Draw(GameTime time)
        {
            // Sample GC state once per frame so the GC section below, the
            // hitch-correlation glance, and any future profiler metric all
            // see the same numbers.
            GcTracker.Sample();

            ImGui.SetNextWindowPos(new System.Numerics.Vector2(360, 8), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(460, 560), ImGuiCond.FirstUseEver);

            if (!ImGui.Begin("Render Inspector"))
            {
                ImGui.End();
                return;
            }

            DrawDiagnosticsToggles();
            ImGui.Separator();
            DrawGcInfo();
            ImGui.Separator();
            DrawGraphicsDeviceInfo();
            ImGui.Separator();
            DrawWorldRendererInfo();
            ImGui.Separator();
            DrawDefaultShaderInfo();

            ImGui.End();
        }

        private static void DrawGcInfo()
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.85f, 0.2f, 1f), "GC (Gen 2 hitch correlation)");

            // Colour the Gen-2 line red when a tick was observed this frame,
            // so a hitch frame is visually matched with a Gen-2 spike.
            // Allocation-pressure meter. Colour bands: calm ≤ 5/s, busy ≤ 15/s, hot > 15/s.
            // Drops meaningfully after C.3 allocation fixes → confirms those fixes worked.
            double g0rate = GcTracker.Gen0PerSecond();
            System.Numerics.Vector4 rateColour =
                g0rate <= 5 ? new System.Numerics.Vector4(0.5f, 0.9f, 0.5f, 1f)
                : g0rate <= 15 ? new System.Numerics.Vector4(0.95f, 0.85f, 0.3f, 1f)
                : new System.Numerics.Vector4(1f, 0.35f, 0.35f, 1f);
            ImGui.TextColored(rateColour, $"Gen 0 rate: {g0rate:0.0} /s (last 2 s window)");

            ImGui.Text($"Gen 0: {GcTracker.Gen0Count}  (+{GcTracker.Gen0Delta})");
            ImGui.Text($"Gen 1: {GcTracker.Gen1Count}  (+{GcTracker.Gen1Delta})");
            if (GcTracker.Gen2Delta > 0)
                ImGui.TextColored(new System.Numerics.Vector4(1f, 0.35f, 0.35f, 1f),
                    $"Gen 2: {GcTracker.Gen2Count}  (+{GcTracker.Gen2Delta})  ← tick this frame");
            else
                ImGui.Text($"Gen 2: {GcTracker.Gen2Count}  (+0)");

            double secs = GcTracker.SecondsSinceLastGen2();
            if (secs < 0)
                ImGui.Text("Last Gen 2: (none yet this session)");
            else
                ImGui.Text($"Last Gen 2: {secs:0.0} s ago");

            // Total heap in MB. Signed "since last frame" — negative right after a collection freed memory.
            double mb = GcTracker.TotalMemoryBytes / (1024.0 * 1024.0);
            double deltaKb = GcTracker.AllocBytesSinceLastSample / 1024.0;
            ImGui.Text($"Total heap: {mb:0.0} MB   (Δ last frame: {deltaKb:+0.0;-0.0;0.0} KB)");

            var events = GcTracker.RecentGen2Events();
            if (events.Length > 0)
            {
                ImGui.Spacing();
                ImGui.TextWrapped("Recent Gen 2 timestamps (newest first):");
                ImGui.Indent();
                var nowUtc = System.DateTime.UtcNow;
                foreach (var t in events)
                    ImGui.Text($"-{(nowUtc - t).TotalSeconds:0.0} s");
                ImGui.Unindent();
            }
            ImGui.TextWrapped(
                "If a visible hitch coincides with a Gen 2 tick, GC pressure is the likely cause. " +
                "Fase C.3 (ArrayPool in spatial queries) is the next commit to attack that directly.");
        }

        private static void DrawDiagnosticsToggles()
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.85f, 0.2f, 1f), "Diagnostics");

            bool transpose = Shader.TransposeMatrices;
            if (ImGui.Checkbox("Transpose shader matrices (row-major → column-major)", ref transpose))
                Shader.TransposeMatrices = transpose;
            ImGui.TextWrapped(
                "Tests the matrix-convention hypothesis. If flipping this brings the viewport back, " +
                "FNA was row-major and MonoGame DX11 is column-major — then bake Transpose in and retire the toggle.");

            ImGui.Spacing();

            bool outline = GameSettings.Current.EnableOutline;
            if (ImGui.Checkbox("Enable screen-space outline post-effect", ref outline))
                GameSettings.Current.EnableOutline = outline;
            ImGui.TextWrapped(
                "The outline effect redirects the main scene to an offscreen render target and blits it back " +
                "with ScreenSpaceOutline.fx. That shader was bumped to SM 4.0 profiles during M.2 — if the blit " +
                "is broken the whole 3D viewport stays black. Uncheck to bypass the effect entirely and see if " +
                "the world renders without it.");

            bool glow = GameSettings.Current.EnableGlow;
            if (ImGui.Checkbox("Enable bloom/glow post-effect", ref glow))
                GameSettings.Current.EnableGlow = glow;
            ImGui.TextWrapped("Same kind of hypothesis, but for the bloom pass. Untick to rule bloom out as the culprit.");
        }

        private static void DrawGraphicsDeviceInfo()
        {
            var game = GameState.Game;
            var device = game?.GraphicsDevice;
            if (device == null || device.IsDisposed)
            {
                ImGui.TextColored(new System.Numerics.Vector4(1, 0.3f, 0.3f, 1), "GraphicsDevice unavailable");
                return;
            }

            var pp = device.PresentationParameters;
            ImGui.Text($"Backbuffer:  {pp.BackBufferWidth} x {pp.BackBufferHeight}  ({pp.BackBufferFormat})");
            ImGui.Text($"Viewport:    {device.Viewport.Width} x {device.Viewport.Height} @ ({device.Viewport.X},{device.Viewport.Y})");
            ImGui.Text($"Depth:       {pp.DepthStencilFormat}   MSAA: {pp.MultiSampleCount}");
            ImGui.Text($"VSync:       {game.Graphics?.SynchronizeWithVerticalRetrace}");

            var rts = device.GetRenderTargets();
            if (rts == null || rts.Length == 0)
                ImGui.Text("Render targets: (default backbuffer)");
            else
            {
                ImGui.Text($"Render targets bound: {rts.Length}");
                for (int i = 0; i < Math.Min(rts.Length, 4); i++)
                {
                    var t = rts[i].RenderTarget as Microsoft.Xna.Framework.Graphics.Texture2D;
                    ImGui.Text($"  [{i}] {t?.Width}x{t?.Height} {t?.Format}");
                }
            }

            ImGui.Text($"Rasterizer:  {device.RasterizerState?.Name ?? "(null)"}  Cull={device.RasterizerState?.CullMode}");
            ImGui.Text($"Depth/Stenc: {device.DepthStencilState?.Name ?? "(null)"}");
            ImGui.Text($"Blend:       {device.BlendState?.Name ?? "(null)"}");
        }

        private static void DrawWorldRendererInfo()
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.9f, 1f, 1f), "WorldRenderer");
            try
            {
                var state = GameStateManager.ActiveState as PlayState;
                var world = state?.World;
                var renderer = world?.Renderer;
                if (renderer == null)
                {
                    ImGui.Text("(WorldRenderer not active — not in PlayState)");
                    return;
                }

                ImGui.Text($"ShowingWorld: {world.ShowingWorld}");
                ImGui.Text($"Camera pos:   {renderer.Camera?.Position}");
                ImGui.Text($"Camera target:{renderer.Camera?.Target}");
            }
            catch (Exception ex)
            {
                ImGui.TextColored(new System.Numerics.Vector4(1, 0.4f, 0.4f, 1), $"Error: {ex.Message}");
            }
        }

        private static void DrawDefaultShaderInfo()
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.9f, 1f, 1f), "DefaultShader parameters");
            try
            {
                var state = GameStateManager.ActiveState as PlayState;
                var shader = state?.World?.Renderer?.DefaultShader;
                if (shader == null)
                {
                    ImGui.Text("(DefaultShader not yet built)");
                    return;
                }

                ImGui.Text($"Effect:      {shader.GetType().Name}  (disposed={shader.IsDisposed})");
                ImGui.Text($"Technique:   {shader.CurrentTechnique?.Name ?? "(none)"}");
                ImGui.Text($"Params:      {shader.Parameters.Count}");

                var lights = shader.Parameters["xLightPositions"];
                if (lights != null)
                {
                    ImGui.Text($"  xLightPositions: Elements.Count = {lights.Elements.Count}  (C# sends {Shader.MaxLights})");
                }
                DumpMatrix(shader, "xWorld");
                DumpMatrix(shader, "xView");
                DumpMatrix(shader, "xProjection");
            }
            catch (Exception ex)
            {
                ImGui.TextColored(new System.Numerics.Vector4(1, 0.4f, 0.4f, 1), $"Error: {ex.Message}");
            }
        }

        private static void DumpMatrix(Shader shader, string paramName)
        {
            var p = shader.Parameters[paramName];
            if (p == null)
            {
                ImGui.Text($"  {paramName}: (missing)");
                return;
            }
            var m = p.GetValueMatrix();
            ImGui.Text($"  {paramName}: [{m.M11:0.00} {m.M12:0.00} {m.M13:0.00} {m.M14:0.00}] ...");
        }
    }
}
