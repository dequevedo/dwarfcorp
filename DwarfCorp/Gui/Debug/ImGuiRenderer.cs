using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using XnaKeys = Microsoft.Xna.Framework.Input.Keys;

namespace DwarfCorp.Gui.Debug
{
    /// <summary>
    /// Minimal MonoGame ↔ ImGui.NET renderer. Wraps the IO/input glue and
    /// the draw-data → MonoGame primitives translation in one file so we
    /// don't depend on third-party NuGet bridges that ship broken.
    ///
    /// Based on the standard ImGui renderer pattern used across the
    /// ImGui.NET sample projects — translated to XNA4/MonoGame primitives
    /// (VertexBuffer, IndexBuffer, BasicEffect, Texture2D). Keeps the API
    /// surface tiny: <see cref="RebuildFontAtlas"/>, <see cref="BeforeLayout"/>,
    /// <see cref="AfterLayout"/>. Nothing exotic.
    /// </summary>
    public class ImGuiRenderer
    {
        private readonly Game _game;
        private readonly GraphicsDevice _device;
        private BasicEffect _effect;
        private RasterizerState _rasterizer;

        private VertexDeclaration _vertexDecl;
        private byte[] _vertexBytes;
        private VertexBuffer _vertexBuffer;
        private int _vertexCapacity;

        private byte[] _indexBytes;
        private IndexBuffer _indexBuffer;
        private int _indexCapacity;

        private readonly Dictionary<IntPtr, Texture2D> _textures = new();
        private int _nextTextureId = 1;
        private IntPtr _fontTextureId;

        // Input edge detection
        private int _scrollWheelAccum;
        private readonly List<int> _keysToProcess = new();

        public ImGuiRenderer(Game game)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
            _device = game.GraphicsDevice ?? throw new InvalidOperationException("GraphicsDevice not ready");

            IntPtr context = ImGui.CreateContext();
            ImGui.SetCurrentContext(context);

            _effect = new BasicEffect(_device);
            _rasterizer = new RasterizerState
            {
                CullMode = CullMode.None,
                DepthBias = 0,
                FillMode = FillMode.Solid,
                MultiSampleAntiAlias = false,
                ScissorTestEnable = true,
                SlopeScaleDepthBias = 0
            };

            // ImDrawVert layout: pos(float2) uv(float2) col(byte4 normalized)
            _vertexDecl = new VertexDeclaration(
                new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
                new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
                new VertexElement(16, VertexElementFormat.Color, VertexElementUsage.Color, 0));

            WireUpInput();
        }

        public void RebuildFontAtlas()
        {
            var io = ImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr pixelData, out int width, out int height, out int bytesPerPixel);

            int pixelByteCount = width * height * bytesPerPixel;
            var pixels = new byte[pixelByteCount];
            Marshal.Copy(pixelData, pixels, 0, pixelByteCount);

            var tex = new Texture2D(_device, width, height, false, SurfaceFormat.Color);
            tex.SetData(pixels);

            if (_fontTextureId != IntPtr.Zero)
                UnbindTexture(_fontTextureId);
            _fontTextureId = BindTexture(tex);
            io.Fonts.SetTexID(_fontTextureId);
            io.Fonts.ClearTexData();
        }

        public IntPtr BindTexture(Texture2D tex)
        {
            IntPtr id = new(_nextTextureId++);
            _textures.Add(id, tex);
            return id;
        }

        public void UnbindTexture(IntPtr id)
        {
            _textures.Remove(id);
        }

        public void BeforeLayout(GameTime gameTime)
        {
            var io = ImGui.GetIO();
            io.DeltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
            UpdateInput();
            ImGui.NewFrame();
        }

        public void AfterLayout()
        {
            ImGui.Render();
            RenderDrawData(ImGui.GetDrawData());
        }

        private void WireUpInput()
        {
            var io = ImGui.GetIO();
            io.Fonts.AddFontDefault();

            _game.Window.TextInput += (s, e) =>
            {
                if (e.Character != '\t') ImGui.GetIO().AddInputCharacter(e.Character);
            };
        }

        private void UpdateInput()
        {
            var io = ImGui.GetIO();
            var mouse = Mouse.GetState();
            var kb = Keyboard.GetState();

            io.AddMousePosEvent(mouse.X, mouse.Y);
            io.AddMouseButtonEvent(0, mouse.LeftButton == ButtonState.Pressed);
            io.AddMouseButtonEvent(1, mouse.RightButton == ButtonState.Pressed);
            io.AddMouseButtonEvent(2, mouse.MiddleButton == ButtonState.Pressed);

            int wheel = mouse.ScrollWheelValue;
            io.AddMouseWheelEvent(0, (wheel - _scrollWheelAccum) / 120f);
            _scrollWheelAccum = wheel;

            // Map the handful of keys ImGui actually uses for widget navigation.
            io.AddKeyEvent(ImGuiKey.Tab, kb.IsKeyDown(XnaKeys.Tab));
            io.AddKeyEvent(ImGuiKey.LeftArrow, kb.IsKeyDown(XnaKeys.Left));
            io.AddKeyEvent(ImGuiKey.RightArrow, kb.IsKeyDown(XnaKeys.Right));
            io.AddKeyEvent(ImGuiKey.UpArrow, kb.IsKeyDown(XnaKeys.Up));
            io.AddKeyEvent(ImGuiKey.DownArrow, kb.IsKeyDown(XnaKeys.Down));
            io.AddKeyEvent(ImGuiKey.PageUp, kb.IsKeyDown(XnaKeys.PageUp));
            io.AddKeyEvent(ImGuiKey.PageDown, kb.IsKeyDown(XnaKeys.PageDown));
            io.AddKeyEvent(ImGuiKey.Home, kb.IsKeyDown(XnaKeys.Home));
            io.AddKeyEvent(ImGuiKey.End, kb.IsKeyDown(XnaKeys.End));
            io.AddKeyEvent(ImGuiKey.Delete, kb.IsKeyDown(XnaKeys.Delete));
            io.AddKeyEvent(ImGuiKey.Backspace, kb.IsKeyDown(XnaKeys.Back));
            io.AddKeyEvent(ImGuiKey.Enter, kb.IsKeyDown(XnaKeys.Enter));
            io.AddKeyEvent(ImGuiKey.Escape, kb.IsKeyDown(XnaKeys.Escape));
            io.AddKeyEvent(ImGuiKey.Space, kb.IsKeyDown(XnaKeys.Space));
            io.AddKeyEvent(ImGuiKey.ModCtrl, kb.IsKeyDown(XnaKeys.LeftControl) || kb.IsKeyDown(XnaKeys.RightControl));
            io.AddKeyEvent(ImGuiKey.ModShift, kb.IsKeyDown(XnaKeys.LeftShift) || kb.IsKeyDown(XnaKeys.RightShift));
            io.AddKeyEvent(ImGuiKey.ModAlt, kb.IsKeyDown(XnaKeys.LeftAlt) || kb.IsKeyDown(XnaKeys.RightAlt));

            io.DisplaySize = new System.Numerics.Vector2(_device.Viewport.Width, _device.Viewport.Height);
            io.DisplayFramebufferScale = System.Numerics.Vector2.One;
        }

        private unsafe void RenderDrawData(ImDrawDataPtr drawData)
        {
            // Copy the render state so we can restore it — MonoGame and ImGui
            // both trash each other's scissor/rasterizer/etc.
            var prevViewport = _device.Viewport;
            var prevScissor = _device.ScissorRectangle;
            var prevBlend = _device.BlendState;
            var prevDepth = _device.DepthStencilState;
            var prevRaster = _device.RasterizerState;
            var prevSampler = _device.SamplerStates[0];

            _device.BlendFactor = Color.White;
            _device.BlendState = BlendState.NonPremultiplied;
            _device.DepthStencilState = DepthStencilState.None;
            _device.RasterizerState = _rasterizer;
            _device.SamplerStates[0] = SamplerState.LinearClamp;

            drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);
            _device.Viewport = new Viewport(0, 0, _device.PresentationParameters.BackBufferWidth, _device.PresentationParameters.BackBufferHeight);

            UpdateBuffers(drawData);

            _effect.World = Matrix.Identity;
            _effect.View = Matrix.Identity;
            _effect.Projection = Matrix.CreateOrthographicOffCenter(0f, _device.Viewport.Width, _device.Viewport.Height, 0f, -1f, 1f);
            _effect.TextureEnabled = true;
            _effect.VertexColorEnabled = true;

            _device.SetVertexBuffer(_vertexBuffer);
            _device.Indices = _indexBuffer;

            int vtxOffset = 0;
            int idxOffset = 0;
            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                var cmdList = drawData.CmdLists[n];

                for (int cmdIdx = 0; cmdIdx < cmdList.CmdBuffer.Size; cmdIdx++)
                {
                    ImDrawCmdPtr cmd = cmdList.CmdBuffer[cmdIdx];
                    if (!_textures.TryGetValue(cmd.TextureId, out var tex))
                        throw new InvalidOperationException("ImGui tried to bind an unknown texture id: " + cmd.TextureId);

                    _device.ScissorRectangle = new Rectangle(
                        (int)cmd.ClipRect.X,
                        (int)cmd.ClipRect.Y,
                        (int)(cmd.ClipRect.Z - cmd.ClipRect.X),
                        (int)(cmd.ClipRect.W - cmd.ClipRect.Y));

                    _effect.Texture = tex;
                    foreach (var pass in _effect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        _device.DrawIndexedPrimitives(
                            PrimitiveType.TriangleList,
                            baseVertex: vtxOffset,
                            startIndex: idxOffset + (int)cmd.IdxOffset,
                            primitiveCount: (int)cmd.ElemCount / 3);
                    }
                }

                vtxOffset += cmdList.VtxBuffer.Size;
                idxOffset += cmdList.IdxBuffer.Size;
            }

            // Restore prior state
            _device.Viewport = prevViewport;
            _device.ScissorRectangle = prevScissor;
            _device.BlendState = prevBlend;
            _device.DepthStencilState = prevDepth;
            _device.RasterizerState = prevRaster;
            _device.SamplerStates[0] = prevSampler;
        }

        private unsafe void UpdateBuffers(ImDrawDataPtr drawData)
        {
            if (drawData.TotalVtxCount == 0) return;

            int vtxSize = sizeof(ImDrawVert);
            int idxSize = sizeof(ushort);

            if (drawData.TotalVtxCount > _vertexCapacity)
            {
                _vertexCapacity = (int)(drawData.TotalVtxCount * 1.5f);
                _vertexBuffer?.Dispose();
                _vertexBuffer = new VertexBuffer(_device, _vertexDecl, _vertexCapacity, BufferUsage.None);
                _vertexBytes = new byte[_vertexCapacity * vtxSize];
            }
            if (drawData.TotalIdxCount > _indexCapacity)
            {
                _indexCapacity = (int)(drawData.TotalIdxCount * 1.5f);
                _indexBuffer?.Dispose();
                _indexBuffer = new IndexBuffer(_device, IndexElementSize.SixteenBits, _indexCapacity, BufferUsage.None);
                _indexBytes = new byte[_indexCapacity * idxSize];
            }

            int vtxByteOffset = 0;
            int idxByteOffset = 0;
            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                var cmdList = drawData.CmdLists[n];

                int vtxBytes = cmdList.VtxBuffer.Size * vtxSize;
                fixed (byte* dst = &_vertexBytes[vtxByteOffset])
                    Buffer.MemoryCopy((void*)cmdList.VtxBuffer.Data, dst, vtxBytes, vtxBytes);

                int idxBytes = cmdList.IdxBuffer.Size * idxSize;
                fixed (byte* dst = &_indexBytes[idxByteOffset])
                    Buffer.MemoryCopy((void*)cmdList.IdxBuffer.Data, dst, idxBytes, idxBytes);

                vtxByteOffset += vtxBytes;
                idxByteOffset += idxBytes;
            }

            _vertexBuffer.SetData(_vertexBytes, 0, drawData.TotalVtxCount * vtxSize);
            _indexBuffer.SetData(_indexBytes, 0, drawData.TotalIdxCount * idxSize);
        }
    }
}
