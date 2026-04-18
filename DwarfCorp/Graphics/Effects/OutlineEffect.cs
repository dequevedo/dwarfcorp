using System;
using DwarfCorp.GameStates;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DwarfCorp
{
    /// <summary>
    /// Screen-space outline post-process. Modeled after <see cref="FXAA"/>: the caller sets
    /// <see cref="Begin"/> before drawing the scene and <see cref="End"/> after, which runs the
    /// outline shader and flushes the result to the backbuffer (or whatever target was active
    /// before Begin).
    /// </summary>
    public class OutlineEffect
    {
        public RenderTarget2D RenderTarget { get; private set; }
        public Effect Shader { get; private set; }

        // Previously bound render target so End() can restore it if the caller wasn't the
        // default backbuffer.
        private RenderTargetBinding[] _previousTargets;

        public void Initialize()
        {
            Shader = GameState.Game.Content.Load<Effect>(ContentPaths.Shaders.ScreenSpaceOutline);
            ValidateBuffer();
            ApplyStaticParameters();
        }

        private void ValidateBuffer()
        {
            var pp = GameState.Game.GraphicsDevice.PresentationParameters;
            int width = Math.Min(pp.BackBufferWidth, 4096);
            int height = Math.Min(pp.BackBufferHeight, 4096);

            if (RenderTarget == null
                || RenderTarget.Width != width
                || RenderTarget.Height != height
                || RenderTarget.IsDisposed
                || RenderTarget.IsContentLost)
            {
                if (RenderTarget != null)
                    RenderTarget.Dispose();

                // Depth buffer attached so scene geometry renders into this target exactly
                // like it would against the backbuffer. We don't sample the depth — the
                // shader does edge detection on color luminance — but the geometry pass
                // still needs a valid depth attachment.
                RenderTarget = new RenderTarget2D(
                    GameState.Game.GraphicsDevice,
                    width, height,
                    mipMap: false,
                    preferredFormat: pp.BackBufferFormat,
                    preferredDepthFormat: DepthFormat.Depth24);
            }
        }

        private void ApplyStaticParameters()
        {
            var pp = GameState.Game.GraphicsDevice.PresentationParameters;
            int width = RenderTarget.Width;
            int height = RenderTarget.Height;

            var projection = Matrix.CreateOrthographicOffCenter(0, width, height, 0, 0, 1);
            var halfPixelOffset = Matrix.CreateTranslation(-0.5f, -0.5f, 0);
            Shader.Parameters["World"].SetValue(Matrix.Identity);
            Shader.Parameters["View"].SetValue(Matrix.Identity);
            Shader.Parameters["Projection"].SetValue(halfPixelOffset * projection);
            Shader.Parameters["InverseViewportSize"].SetValue(new Vector2(1f / width, 1f / height));
        }

        private void UpdateTunables()
        {
            // Tint — "tinted outline" was a specific user request; default is near-black
            // with a faint warm tone so it reads as an ink outline rather than pure black.
            var tint = new Vector3(0.05f, 0.03f, 0.02f);
            Shader.Parameters["OutlineTint"].SetValue(tint);

            var strength = MathHelper.Clamp(GameSettings.Current.OutlineStrength, 0f, 1f);
            Shader.Parameters["OutlineStrength"].SetValue(strength);

            var threshold = MathHelper.Clamp(GameSettings.Current.OutlineEdgeThreshold, 0.01f, 1f);
            Shader.Parameters["EdgeThreshold"].SetValue(threshold);

            var thickness = MathHelper.Clamp(GameSettings.Current.OutlineThickness, 0.5f, 4f);
            Shader.Parameters["OutlineThickness"].SetValue(thickness);
        }

        public void Begin(DwarfTime lastTime)
        {
            ValidateBuffer();
            var gd = GameState.Game.GraphicsDevice;
            _previousTargets = gd.GetRenderTargets();
            gd.SetRenderTarget(RenderTarget);
        }

        public void End(DwarfTime lastTime)
        {
            ValidateBuffer();
            var gd = GameState.Game.GraphicsDevice;

            // Restore the pre-Begin targets (usually the backbuffer).
            if (_previousTargets != null && _previousTargets.Length > 0)
                gd.SetRenderTargets(_previousTargets);
            else
                gd.SetRenderTarget(null);
            _previousTargets = null;

            ApplyStaticParameters();
            UpdateTunables();
            Shader.CurrentTechnique = Shader.Techniques["Outline"];

            try
            {
                DwarfGame.SafeSpriteBatchBegin(
                    SpriteSortMode.Deferred,
                    BlendState.Opaque,
                    SamplerState.LinearClamp,
                    DepthStencilState.None,
                    RasterizerState.CullNone,
                    Shader,
                    Matrix.Identity);
                DwarfGame.SpriteBatch.Draw(RenderTarget, gd.Viewport.Bounds, Color.White);
            }
            finally
            {
                DwarfGame.SpriteBatch.End();
            }
        }
    }
}
