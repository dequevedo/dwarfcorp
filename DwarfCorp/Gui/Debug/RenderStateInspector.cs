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
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(360, 8), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(420, 380), ImGuiCond.FirstUseEver);

            if (!ImGui.Begin("Render Inspector"))
            {
                ImGui.End();
                return;
            }

            DrawDiagnosticsToggles();
            ImGui.Separator();
            DrawGraphicsDeviceInfo();
            ImGui.Separator();
            DrawWorldRendererInfo();
            ImGui.Separator();
            DrawDefaultShaderInfo();

            ImGui.End();
        }

        private static void DrawDiagnosticsToggles()
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.85f, 0.2f, 1f), "Diagnostics");

            bool transpose = Shader.TransposeMatrices;
            if (ImGui.Checkbox("Transpose shader matrices (row-major → column-major)", ref transpose))
                Shader.TransposeMatrices = transpose;

            ImGui.TextWrapped(
                "If the 3D viewport is black and this checkbox flips it to visible, " +
                "FNA was passing row-major matrices and MonoGame DX11 is reading column-major. " +
                "That means the migration needs a permanent fix (either `row_major` annotations " +
                "in the .fx declarations or baking Transpose into the setters — then retire the toggle).");
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
