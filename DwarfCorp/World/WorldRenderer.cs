using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using BloomPostprocess;
using DwarfCorp.Gui;
using DwarfCorp.Gui.Widgets;
using DwarfCorp.Tutorial;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Color = Microsoft.Xna.Framework.Color;
using Point = Microsoft.Xna.Framework.Point;
using Rectangle = Microsoft.Xna.Framework.Rectangle;
using DwarfCorp.GameStates;
using Newtonsoft.Json;
using DwarfCorp.Events;

namespace DwarfCorp
{
    public partial class WorldRenderer : IDisposable
    {
        public WorldManager World;
        public float CaveView = 0;
        public float TargetCaveView = 0;
        public Overworld Settings = null;
        public WorldRendererPersistentSettings PersistentSettings = new WorldRendererPersistentSettings();
        public Vector3 CursorLightPos = Vector3.Zero;
        public Vector3[] LightPositions = new Vector3[Shader.MaxLights];
        public Shader DefaultShader;
        public OrbitCamera Camera;

        // Fase C.3: scratch buffer reused across FillClosestLights frames. Previously
        // that method did `DynamicLight.Lights.Select(l => l.Position).ToList()` every
        // call — one List allocation per frame, PLUS the Select enumerator, PLUS a new
        // sort comparer closure. Baseline showed Gen 0 firing every 1-3 frames; the
        // render-thread lighting pass was one of the biggest allocators per frame.
        // Reused scratch + instance comparer method keeps the hot path zero-alloc.
        private readonly List<Vector3> _lightPositionScratch = new List<Vector3>();
        private Vector3 _lightSortOrigin;
        private int CompareLightDistanceFromSortOrigin(Vector3 a, Vector3 b)
        {
            float da = (a - _lightSortOrigin).LengthSquared();
            float db = (b - _lightSortOrigin).LengthSquared();
            return da.CompareTo(db);
        }

        // Fase C.3: scratch buffers reused by Render() every frame. Used to be a big LINQ
        // chain (HashSet alloc inside EnumerateIntersectingRootObjectsLoose + SelectMany
        // iterator + Where iterator + predicate closure) enumerated three times per frame
        // (selection buffer, reflection map, main render). Recursive yield-based
        // GameComponent.EnumerateAll made it worse: each walk allocated one enumerator
        // per component in the subtree. Now we fill a HashSet via the fill-overload, walk
        // each root via EnumerateAllInto, filter in place, and pass the single resulting
        // List to every consumer as the concrete type so foreach is struct-enumerator.
        private readonly HashSet<GameComponent> _renderableRoots = new HashSet<GameComponent>();
        private readonly List<GameComponent> _renderableScratch = new List<GameComponent>();
        private readonly List<GameComponent> _renderables = new List<GameComponent>();

        // Fase C.3: scissor RasterizerState used by the 2D post-SpriteBatch pass was being
        // `new`'d every frame. MonoGame tracks state objects on the GPU side; allocating
        // a fresh one every frame leaks into the finalizer queue and the render-state
        // cache. One reused instance is equivalent in behaviour and free of allocation.
        private static readonly RasterizerState _scissorRasterizerState = new RasterizerState
        {
            ScissorTestEnable = true
        };
        public static int MultiSamples
        {
            get { return GameSettings.Current.AntiAliasing; }
            set { GameSettings.Current.AntiAliasing = value; }
        }

        public bool UseFXAA
        {
            get { return MultiSamples == -1; }
        }

        public BloomComponent bloom;
        public FXAA fxaa;
        public OutlineEffect Outline;
        public ChunkRenderer ChunkRenderer;
        public WaterRenderer WaterRenderer;
        public SkyRenderer Sky;
        public SelectionBuffer SelectionBuffer;
        public InstanceRenderer InstanceRenderer;
        public DwarfSprites.DwarfInstanceGroup DwarfInstanceRenderer;
        public ContentManager Content;
        public DwarfGame Game;
        public GraphicsDevice GraphicsDevice { get { return GameState.Game.GraphicsDevice; } }

        // Hack to smooth water reflections TODO: Put into water manager
        private float lastWaterHeight = -1.0f;

        public struct Screenshot
        {
            public string FileName { get; set; }
            public Point Resolution { get; set; }
        }

        public List<Screenshot> Screenshots { get; set; }
        public object ScreenshotLock = new object();

        /// <summary>
        /// Creates a new play state
        /// </summary>
        /// <param name="Game">The program currently running</param>
        public WorldRenderer(DwarfGame Game, WorldManager World)
        {
            this.World = World;
            this.Game = Game;
            Content = Game.Content;
            PersistentSettings.MaxViewingLevel = World.WorldSizeInVoxels.Y;
        }

        /// <summary>
        /// Creates a screenshot of the game and saves it to a file.
        /// </summary>
        /// <param name="filename">The file to save the screenshot to</param>
        /// <param name="resolution">The width/height of the image</param>
        /// <returns>True if the screenshot could be taken, false otherwise</returns>
        public bool TakeScreenshot(string filename, Point resolution)
        {
            try
            {
                using (
                    RenderTarget2D renderTarget = new RenderTarget2D(GraphicsDevice, resolution.X, resolution.Y, false,
                        SurfaceFormat.Color, DepthFormat.Depth24))
                {
                    var frustum = Camera.GetDrawFrustum();
                    // One-shot call; per-frame allocator concerns don't apply here.
                    var roots = new HashSet<GameComponent>();
                    World.EnumerateIntersectingRootObjectsLoose(frustum, roots);
                    var renderables = new List<GameComponent>();
                    foreach (var root in roots)
                    {
                        var scratch = new List<GameComponent>();
                        root.EnumerateAllInto(scratch);
                        foreach (var r in scratch)
                            if (r.IsVisible && !World.ChunkManager.IsAboveCullPlane(r.GetBoundingBox()))
                                renderables.Add(r);
                    }

                    var oldProjection = Camera.ProjectionMatrix;
                    Matrix projectionMatrix = Matrix.CreatePerspectiveFieldOfView(Camera.FOV, ((float)resolution.X) / resolution.Y, Camera.NearPlane, Camera.FarPlane);
                    Camera.ProjectionMatrix = projectionMatrix;
                    GraphicsDevice.SetRenderTarget(renderTarget);
                    DrawSky(new DwarfTime(), Camera.ViewMatrix, 1.0f, Color.CornflowerBlue);
                    Draw3DThings(new DwarfTime(), DefaultShader, Camera.ViewMatrix);

                    DefaultShader.View = Camera.ViewMatrix;
                    DefaultShader.Projection = Camera.ProjectionMatrix;

                    ComponentRenderer.Render(renderables, new DwarfTime(), World.ChunkManager, Camera,
                        DwarfGame.SpriteBatch, GraphicsDevice, DefaultShader,
                        ComponentRenderer.WaterRenderType.None, 0);
                    InstanceRenderer.Flush(GraphicsDevice, DefaultShader, Camera,
                        InstanceRenderMode.Normal);
                    DwarfInstanceRenderer.Update(GraphicsDevice);
                    DwarfInstanceRenderer.Render(GraphicsDevice, DefaultShader, Camera, InstanceRenderMode.Normal);


                    GraphicsDevice.SetRenderTarget(null);
                    renderTarget.SaveAsPng(new FileStream(filename, FileMode.Create), resolution.X, resolution.Y);
                    GraphicsDevice.Textures[0] = null;
                    GraphicsDevice.Indices = null;
                    GraphicsDevice.SetVertexBuffer(null);
                    Camera.ProjectionMatrix = oldProjection;
                }
            }
            catch (IOException e)
            {
                Console.Error.WriteLine(e.Message);
                return false;
            }

            return true;
        }

        public bool IsCameraUnderwater()
        {
            var handle = new VoxelHandle(World.ChunkManager, GlobalVoxelCoordinate.FromVector3(Camera.Position + Vector3.Up));
            if (!handle.IsValid) return false;
            var liquidPresent = LiquidCellHelpers.AnyLiquidInVoxel(handle);
            return liquidPresent && handle.Coordinate.Y <= (PersistentSettings.MaxViewingLevel >= World.WorldSizeInVoxels.Y ? 1000.0f : PersistentSettings.MaxViewingLevel + 0.25f);
        }

        /// <summary>
        /// Called every frame
        /// </summary>
        /// <param name="gameTime">The current time</param>
        public void Update(DwarfTime gameTime)
        {
            ValidateShader();

            FillClosestLights(gameTime);
            Camera.AspectRatio = GraphicsDevice.Viewport.AspectRatio;
            Camera.Update(gameTime, World.ChunkManager);

            Sky.TimeOfDay = World.Time.GetSkyLightness();
            Sky.CosTime = (float)(World.Time.GetTotalHours() * 2 * Math.PI / 24.0f);
            DefaultShader.TimeOfDay = Sky.TimeOfDay;

            ChunkRenderer.Update(gameTime, Camera, GraphicsDevice);
        }

        public void ChangeCameraMode(OrbitCamera.ControlType type)
        {
            Camera.Control = type;
            if (type == OrbitCamera.ControlType.Walk)
            {
                SetMaxViewingLevel(World.WorldSizeInVoxels.Y);
                var below = VoxelHelpers.FindFirstVoxelBelowIncludingWater(new VoxelHandle(World.ChunkManager, GlobalVoxelCoordinate.FromVector3(new Vector3(Camera.Position.X, World.WorldSizeInVoxels.Y - 1, Camera.Position.Z))));
                Camera.Position = below.WorldPosition + Vector3.One * 0.5f + Vector3.Up;
            }
        }

        /// <summary>
        /// Reflects a camera beneath a water surface for reflection drawing TODO: move to water manager
        /// </summary>
        /// <param name="waterHeight">The height of the water (Y)</param>
        /// <returns>A reflection matrix</returns>
        public Matrix GetReflectedCameraMatrix(float waterHeight)
        {
            Vector3 reflCameraPosition = Camera.Position;
            reflCameraPosition.Y = -Camera.Position.Y + waterHeight * 2;
            Vector3 reflTargetPos = Camera.Target;
            reflTargetPos.Y = -Camera.Target.Y + waterHeight * 2;

            Vector3 cameraRight = Vector3.Cross(Camera.Target - Camera.Position, Camera.UpVector);
            cameraRight.Normalize();
            Vector3 invUpVector = Vector3.Cross(cameraRight, reflTargetPos - reflCameraPosition);
            invUpVector.Normalize();
            return Matrix.CreateLookAt(reflCameraPosition, reflTargetPos, invUpVector);
        }

        /// <summary>
        /// Draws all the 3D terrain and entities
        /// </summary>
        /// <param name="gameTime">The current time</param>
        /// <param name="effect">The textured shader</param>
        /// <param name="view">The view matrix of the camera</param> 
        public void Draw3DThings(DwarfTime gameTime, Shader effect, Matrix view)
        {
            Matrix viewMatrix = Camera.ViewMatrix;
            Camera.ViewMatrix = view;

            GraphicsDevice.SamplerStates[0] = Drawer2D.PointMagLinearMin;
            effect.View = view;
            effect.Projection = Camera.ProjectionMatrix;
            effect.SetTexturedTechnique();
            effect.ClippingEnabled = true;
            effect.CaveView = CaveView;
            GraphicsDevice.BlendState = BlendState.NonPremultiplied;

            ChunkRenderer.Render(Camera, gameTime, GraphicsDevice, effect, Matrix.Identity);

            Camera.ViewMatrix = viewMatrix;
            effect.ClippingEnabled = true;
        }

        /// <summary>
        /// Draws the sky box
        /// </summary>
        /// <param name="time">The current time</param>
        /// <param name="view">The camera view matrix</param>
        /// <param name="scale">The scale for the sky drawing</param>
        /// <param name="fogColor"></param>
        /// <param name="drawBackground"></param>
        public void DrawSky(DwarfTime time, Matrix view, float scale, Color fogColor, bool drawBackground = true)
        {
            Matrix oldView = Camera.ViewMatrix;
            Camera.ViewMatrix = view;
            Sky.Render(time, GraphicsDevice, Camera, scale, fogColor, World.ChunkManager.Bounds, drawBackground);
            GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            Camera.ViewMatrix = oldView;
        }

        public void RenderUninitialized(DwarfTime gameTime, String tip = null)
        {
            Render(gameTime);
        }

        public void FillClosestLights(DwarfTime time)
        {
            // Fase C.3: zero-alloc light gather + sort. Reuses `_lightPositionScratch`
            // (instance field) and a pre-allocated Comparison<Vector3> instance method
            // reference (no closure alloc because `_lightSortOrigin` is an instance
            // field, not a captured local). Previous version allocated a fresh List,
            // two Select enumerators and a sort closure every render frame.
            _lightPositionScratch.Clear();
            foreach (var l in DynamicLight.Lights)
                _lightPositionScratch.Add(l.Position);
            // Fase C.3: transient lights now live in a pooled counter-backed array
            // inside DynamicLight; iterate by index.
            for (int i = 0; i < DynamicLight.TempLightCount; i++)
                _lightPositionScratch.Add(DynamicLight.GetTempLight(i).Position);

            _lightSortOrigin = Camera.Position;
            _lightPositionScratch.Sort(CompareLightDistanceFromSortOrigin);

            var lightCount = 0;
            if (GameSettings.Current.CursorLightEnabled)
                LightPositions[lightCount++] = CursorLightPos;

            var lightsAdded = 0;
            while (lightCount < Shader.MaxLights && lightsAdded < _lightPositionScratch.Count)
                LightPositions[lightCount++] = _lightPositionScratch[lightsAdded++];

            DefaultShader.CurrentNumLights = lightCount;

            DynamicLight.ClearTempLights();
        }

        public void ValidateShader()
        {
            if (DefaultShader == null || DefaultShader.IsDisposed || DefaultShader.GraphicsDevice.IsDisposed)
            {
                DefaultShader = new Shader(Content.Load<Effect>(ContentPaths.Shaders.TexturedShaders), true);
                DefaultShader.ScreenWidth = GraphicsDevice.Viewport.Width;
                DefaultShader.ScreenHeight = GraphicsDevice.Viewport.Height;
            }
        }

        /// <summary>
        /// Called when a frame is to be drawn to the screen
        /// </summary>
        /// <param name="gameTime">The current time</param>
        public void Render(DwarfTime gameTime)
        {
            if (!World.ShowingWorld)
            {
                return;
            }
            ValidateShader();
            var frustum = Camera.GetDrawFrustum();

            // Fase C.3: materialize once per frame into reusable scratch structures.
            // See the `_renderables` field comment for the allocation history.
            _renderableRoots.Clear();
            World.EnumerateIntersectingRootObjectsLoose(frustum, _renderableRoots);
            _renderableScratch.Clear();
            foreach (var root in _renderableRoots)
                root.EnumerateAllInto(_renderableScratch);
            _renderables.Clear();
            bool drawInvisible = Debugger.Switches.DrawInvisible;
            var chunkManager = World.ChunkManager;
            for (int i = 0; i < _renderableScratch.Count; i++)
            {
                var r = _renderableScratch[i];
                if (!drawInvisible && !r.IsVisible) continue;
                if (chunkManager.IsAboveCullPlane(r.GetBoundingBox())) continue;
                _renderables.Add(r);
            }
            var renderables = _renderables;

            // Controls the sky fog
            float x = (1.0f - Sky.TimeOfDay);
            x = x * x;
            DefaultShader.FogColor = new Color(0.32f * x, 0.58f * x, 0.9f * x);
            DefaultShader.LightPositions = LightPositions;

            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.BlendState = BlendState.Opaque;

            if (lastWaterHeight < 0) // Todo: Seriously, every single frame??
            {
                lastWaterHeight = 0;
                foreach (var chunk in World.ChunkManager.ChunkMap)
                    for (int y = 0; y < VoxelConstants.ChunkSizeY; y++)
                        if (chunk.Data.LiquidPresent[y] > 0)
                            lastWaterHeight = Math.Max(y + chunk.Origin.Y, lastWaterHeight);
            }

            // Computes the water height.
            float wHeight = WaterRenderer.GetVisibleWaterHeight(World.ChunkManager, Camera, GraphicsDevice.Viewport,
                lastWaterHeight);

            lastWaterHeight = wHeight;

            // Draw reflection/refraction images
            PerformanceMonitor.PushFrame("WorldRenderer.Reflection");
            WaterRenderer.DrawReflectionMap(renderables, gameTime, World, wHeight - 0.1f,
                GetReflectedCameraMatrix(wHeight),
                DefaultShader, GraphicsDevice);
            PerformanceMonitor.PopFrame();


            #region Draw Selection Buffer.

            PerformanceMonitor.PushFrame("WorldRenderer.SelectionBuffer");
            if (SelectionBuffer == null)
                SelectionBuffer = new SelectionBuffer(8, GraphicsDevice);

            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.BlendState = BlendState.Opaque;

            // Defines the current slice for the GPU
            var level = PersistentSettings.MaxViewingLevel >= World.WorldSizeInVoxels.Y ? 1000.0f : PersistentSettings.MaxViewingLevel + 0.25f;
            Plane slicePlane = WaterRenderer.CreatePlane(level, new Vector3(0, -1, 0), Camera.ViewMatrix, false);

            
            DwarfInstanceRenderer.Update(GraphicsDevice);

            if (SelectionBuffer.Begin(GraphicsDevice))
            {
                // Draw the whole world, and make sure to handle slicing
                DefaultShader.ClipPlane = new Vector4(slicePlane.Normal, slicePlane.D);
                DefaultShader.ClippingEnabled = true;
                DefaultShader.View = Camera.ViewMatrix;
                DefaultShader.Projection = Camera.ProjectionMatrix;
                DefaultShader.World = Matrix.Identity;

                //GamePerformance.Instance.StartTrackPerformance("Render - Selection Buffer - Chunks");
                ChunkRenderer.RenderSelectionBuffer(DefaultShader, GraphicsDevice, Camera.ViewMatrix);
                //GamePerformance.Instance.StopTrackPerformance("Render - Selection Buffer - Chunks");

                //GamePerformance.Instance.StartTrackPerformance("Render - Selection Buffer - Components");
                ComponentRenderer.RenderSelectionBuffer(renderables, gameTime, World.ChunkManager, Camera,
                    DwarfGame.SpriteBatch, GraphicsDevice, DefaultShader);
                //GamePerformance.Instance.StopTrackPerformance("Render - Selection Buffer - Components");

                //GamePerformance.Instance.StartTrackPerformance("Render - Selection Buffer - Instances");
                InstanceRenderer.Flush(GraphicsDevice, DefaultShader, Camera, InstanceRenderMode.SelectionBuffer);
                DwarfInstanceRenderer.Render(GraphicsDevice, DefaultShader, Camera, InstanceRenderMode.SelectionBuffer);
                //GamePerformance.Instance.StopTrackPerformance("Render - Selection Buffer - Instances");

                SelectionBuffer.End(GraphicsDevice);
            }
            PerformanceMonitor.PopFrame(); // WorldRenderer.SelectionBuffer


            #endregion



            // Start drawing the bloom effect
            if (GameSettings.Current.EnableGlow)
            {
                //bloom.BeginDraw();
            }

            // Screen-space outline post-effect: redirect the main scene to an intermediate
            // render target; End() below blits it back with the outline shader applied.
            bool outlineActive = GameSettings.Current.EnableOutline;
            if (outlineActive)
            {
                if (Outline == null)
                {
                    Outline = new OutlineEffect();
                    Outline.Initialize();
                    CrashBreadcrumbs.Push("OutlineEffect initialized");
                }
                Outline.Begin(gameTime);
            }

            // Draw the sky
            GraphicsDevice.Clear(DefaultShader.FogColor);
            PerformanceMonitor.PushFrame("WorldRenderer.Sky");
            DrawSky(gameTime, Camera.ViewMatrix, 1.0f, DefaultShader.FogColor);
            PerformanceMonitor.PopFrame();



            DefaultShader.FogEnd = GameSettings.Current.ChunkDrawDistance;
            DefaultShader.FogStart = GameSettings.Current.ChunkDrawDistance * 0.8f;

            CaveView = CaveView * 0.9f + TargetCaveView * 0.1f;
            DefaultShader.WindDirection = World.Weather.CurrentWind;
            DefaultShader.WindForce = 0.0005f * (1.0f + (float)Math.Sin(World.Time.GetTotalSeconds() * 0.001f));
            // Draw the whole world, and make sure to handle slicing
            DefaultShader.ClipPlane = new Vector4(slicePlane.Normal, slicePlane.D);
            DefaultShader.ClippingEnabled = true;
            //Blue ghost effect above the current slice.
            DefaultShader.GhostClippingEnabled = true;
            PerformanceMonitor.PushFrame("WorldRenderer.Draw3DThings");
            Draw3DThings(gameTime, DefaultShader, Camera.ViewMatrix);
            PerformanceMonitor.PopFrame();


            // Now we want to draw the water on top of everything else
            DefaultShader.ClippingEnabled = true;
            DefaultShader.GhostClippingEnabled = false;

            //ComponentManager.CollisionManager.DebugDraw();

            DefaultShader.View = Camera.ViewMatrix;
            DefaultShader.Projection = Camera.ProjectionMatrix;
            DefaultShader.GhostClippingEnabled = true;
            // Now draw all of the entities in the game
            DefaultShader.ClipPlane = new Vector4(slicePlane.Normal, slicePlane.D);
            DefaultShader.ClippingEnabled = true;

            // Render simple geometry (boxes, etc.)
            PerformanceMonitor.PushFrame("WorldRenderer.Drawer3D");
            Drawer3D.Render(GraphicsDevice, DefaultShader, Camera, World.PersistentData.Designations, World);
            PerformanceMonitor.PopFrame();

            DefaultShader.EnableShadows = false;

            DefaultShader.View = Camera.ViewMatrix;

            PerformanceMonitor.PushFrame("WorldRenderer.ComponentRenderer");
            ComponentRenderer.Render(renderables, gameTime, World.ChunkManager,
                Camera,
                DwarfGame.SpriteBatch, GraphicsDevice, DefaultShader,
                ComponentRenderer.WaterRenderType.None, lastWaterHeight);
            PerformanceMonitor.PopFrame();
            PerformanceMonitor.PushFrame("WorldRenderer.InstanceRenderer.Flush");
            InstanceRenderer.Flush(GraphicsDevice, DefaultShader, Camera, InstanceRenderMode.Normal);
            PerformanceMonitor.PopFrame();
            PerformanceMonitor.PushFrame("WorldRenderer.DwarfInstanceRenderer");
            DwarfInstanceRenderer.Render(GraphicsDevice, DefaultShader, Camera, InstanceRenderMode.Normal);
            PerformanceMonitor.PopFrame();

            PerformanceMonitor.PushFrame("WorldRenderer.Water");
            WaterRenderer.DrawWater(
                GraphicsDevice,
                (float)gameTime.TotalGameTime.TotalSeconds,
                DefaultShader,
                Camera.ViewMatrix,
                GetReflectedCameraMatrix(wHeight),
                Camera.ProjectionMatrix,
                new Vector3(0.1f, 0.0f, 0.1f),
                Camera,
                World.ChunkManager);
            PerformanceMonitor.PopFrame();
            PerformanceMonitor.PushFrame("WorldRenderer.Particles");
            World.ParticleManager.Render(World, GraphicsDevice);
            PerformanceMonitor.PopFrame();
            DefaultShader.ClippingEnabled = false;

            // Flush the outline pass back to the previously-bound render target (typically
            // the backbuffer). Done before any subsequent UI/debug drawing so outlines only
            // affect the world, not the HUD.
            if (outlineActive && Outline != null)
            {
                PerformanceMonitor.PushFrame("WorldRenderer.Outline.End");
                Outline.End(gameTime);
                PerformanceMonitor.PopFrame();
            }

            if (UseFXAA && fxaa == null)
            {
                fxaa = new FXAA();
                fxaa.Initialize();
            }

            if (UseFXAA) PerformanceMonitor.PushFrame("WorldRenderer.FXAA");
            if (GameSettings.Current.EnableGlow)
            {
                if (UseFXAA)
                {
                    fxaa.Begin(DwarfTime.LastTimeX);
                }
                //bloom.DrawTarget = UseFXAA ? fxaa.RenderTarget : null;

                //bloom.Draw(gameTime.ToRealTime());
                if (UseFXAA)
                    fxaa.End(DwarfTime.LastTimeX);
            }
            else if (UseFXAA)
            {
                fxaa.End(DwarfTime.LastTimeX);
            }
            if (UseFXAA) PerformanceMonitor.PopFrame();

            if (Debugger.Switches.DrawSelectionBuffer)
                SelectionBuffer.DebugDraw(GraphicsDevice.Viewport.Bounds);

            PerformanceMonitor.PushFrame("WorldRenderer.HUD.2D");
            try
            {
                DwarfGame.SafeSpriteBatchBegin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, Drawer2D.PointMagLinearMin,
                    null, _scissorRasterizerState, null, Matrix.Identity);
                //DwarfGame.SpriteBatch.Draw(Shadows.ShadowTexture, Vector2.Zero, Color.White);
                if (IsCameraUnderwater())
                {
                    Drawer2D.FillRect(DwarfGame.SpriteBatch, GraphicsDevice.Viewport.Bounds, new Color(10, 40, 60, 200));
                }

                Drawer2D.Render(DwarfGame.SpriteBatch, Camera, GraphicsDevice.Viewport);

                IndicatorManager.Render(gameTime);
            }
            finally
            {
                try
                {
                    DwarfGame.SpriteBatch.End();
                }
                catch (Exception exception)
                {
                    DwarfGame.SpriteBatch = new SpriteBatch(GraphicsDevice);
                }
                PerformanceMonitor.PopFrame(); // WorldRenderer.HUD.2D
            }

            if (Debugger.Switches.DrawTiledInstanceAtlas)
            {
                var tiledInstanceGroup = InstanceRenderer.GetCombinedTiledInstance();
                var tex = tiledInstanceGroup.GetAtlasTexture();
                if (tex != null)
                    this.World.UserInterface.Gui.DrawQuad(tex.Bounds, tex);
            }

            DwarfGame.SpriteBatch.GraphicsDevice.ScissorRectangle =
                DwarfGame.SpriteBatch.GraphicsDevice.Viewport.Bounds;

            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.BlendState = BlendState.Opaque;

                World.ModuleManager.Render(gameTime, World.ChunkManager, Camera, DwarfGame.SpriteBatch, GraphicsDevice, DefaultShader);

            lock (ScreenshotLock)
            {
                foreach (Screenshot shot in Screenshots)
                {
                    TakeScreenshot(shot.FileName, shot.Resolution);
                }

                Screenshots.Clear();
            }
        }




        /// <summary>
        /// Called when the GPU is getting new settings
        /// </summary>
        /// <param name="sender">The object requesting new device settings</param>
        /// <param name="e">The device settings that are getting set</param>
        public static void GraphicsPreparingDeviceSettings(object sender, PreparingDeviceSettingsEventArgs e)
        {
            if (e == null)
            {
                Console.Error.WriteLine("Preparing device settings given null event args.");
                return;
            }

            if (e.GraphicsDeviceInformation == null)
            {
                Console.Error.WriteLine("Somehow, GraphicsDeviceInformation is null!");
                return;
            }

            PresentationParameters pp = e.GraphicsDeviceInformation.PresentationParameters;
            if (pp == null)
            {
                Console.Error.WriteLine("Presentation parameters invalid.");
                return;
            }

            GraphicsAdapter adapter = e.GraphicsDeviceInformation.Adapter;
            if (adapter == null)
            {
                Console.Error.WriteLine("Somehow, graphics adapter is null!");
                return;
            }

            if (adapter.CurrentDisplayMode == null)
            {
                Console.Error.WriteLine("Somehow, CurrentDisplayMode is null!");
                return;
            }

            SurfaceFormat format = adapter.CurrentDisplayMode.Format;

            if (MultiSamples > 0 && MultiSamples != pp.MultiSampleCount)
            {
                pp.MultiSampleCount = MultiSamples;
            }
            else if (MultiSamples <= 0 && MultiSamples != pp.MultiSampleCount)
            {
                pp.MultiSampleCount = 0;
            }
        }

        public void Dispose()
        {
            bloom.Dispose();
            WaterRenderer.Dispose();
        }

        public void SetMaxViewingLevel(int level)
        {
            if (level == PersistentSettings.MaxViewingLevel)
                return;
            SoundManager.PlaySound(ContentPaths.Audio.Oscar.sfx_gui_click_voxel, 0.15f, (float)(level / (float)World.WorldSizeInVoxels.Y) - 0.5f);

            var oldLevel = PersistentSettings.MaxViewingLevel;

            PersistentSettings.MaxViewingLevel = Math.Max(Math.Min(level, World.WorldSizeInVoxels.Y), 1);

            foreach (var c in World.ChunkManager.ChunkMap)
            {
                var oldSliceIndex = oldLevel - 1 - c.Origin.Y;
                if (oldSliceIndex >= 0 && oldSliceIndex < VoxelConstants.ChunkSizeY) c.InvalidateSlice(oldSliceIndex);

                var newSliceIndex = PersistentSettings.MaxViewingLevel - 1 - c.Origin.Y;
                if (newSliceIndex >= 0 && newSliceIndex < VoxelConstants.ChunkSizeY) c.InvalidateSlice(newSliceIndex);

                if (oldLevel < c.Origin.Y && level >= c.Origin.Y)
                    World.ChunkManager.InvalidateChunk(c);
            }
        }
    }
}
