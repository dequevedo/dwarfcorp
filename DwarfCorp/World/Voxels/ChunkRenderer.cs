using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DwarfCorp.GameStates;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Content;
using System.Threading;
using System.Collections.Concurrent;

namespace DwarfCorp
{
    public class ChunkRenderer
    {
        public List<VoxelChunk> RenderList = new List<VoxelChunk>();
        public List<VoxelChunk> LiveVoxelList = new List<VoxelChunk>();
        private int RenderCycle = 1;

        // Reusable scratch buffer for the per-frame visibility pass. Previously Update()
        // allocated a fresh HashSet + List every frame, which showed up as GC pressure
        // while panning the camera.
        private readonly HashSet<VoxelChunk> _visibleScratch = new HashSet<VoxelChunk>();

        // Cache the camera's view*projection matrix so we can skip the expensive
        // triple-nested frustum scan when the camera hasn't moved or rotated. This is
        // the main cause of FPS dropping *only while moving* — a static camera already
        // computes the same visible set every frame.
        private Matrix _lastVisibilityViewProj;
        private bool _hasLastVisibilityViewProj;
        // Force a full visibility recompute every N frames even if the camera is static,
        // so chunks finishing their background load/build join RenderList within a handful
        // of frames instead of being invisible until the player moves.
        private int _framesSinceFullRecompute;
        private const int ForceRecomputeEveryFrames = 15;

        public ChunkManager ChunkData;

        public ChunkRenderer(ChunkManager Data)
        {
            ChunkData = Data;

            GameSettings.Current.VisibilityUpdateTime = 0.05f;
        }

        public void RenderSelectionBuffer(Shader effect, GraphicsDevice graphicsDevice,
            Matrix viewmatrix)
        {
            effect.CurrentTechnique = effect.Techniques[Shader.Technique.SelectionBuffer];
            effect.MainTexture = AssetManager.GetContentTexture(ContentPaths.Terrain.terrain_tiles);
            effect.World = Matrix.Identity;
            effect.View = viewmatrix;
            effect.SelectionBufferColor = Vector4.Zero;

            if (RenderList  != null)
            foreach (VoxelChunk chunk in RenderList)
            {
                foreach (EffectPass pass in effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    chunk.Render(GameState.Game.GraphicsDevice);
                }
            }
        }

        public void Render(Camera renderCamera, DwarfTime gameTime, GraphicsDevice graphicsDevice, Shader effect, Matrix worldMatrix)
        {
            // Fase B.1: drain any fresh chunk primitives produced by background
            // mesh-gen workers since the last frame, and swap them onto their chunks
            // here on the render thread. Budget chosen so a flood of rebuilds can't
            // itself cause a single-frame hitch — the old-primitive Dispose is
            // normally cheap but SetData on the first subsequent Render can bite.
            // Leftover items carry over to next frame; no drops.
            PerformanceMonitor.PushFrame("ChunkMeshUploadDrain");
            const int perFrameUploadBudget = 8;
            int swapped = Voxels.MeshUploadQueue.DrainUpToBudget(perFrameUploadBudget);
            PerformanceMonitor.SetMetric("ChunkMeshUploadsThisFrame", swapped);
            PerformanceMonitor.SetMetric("ChunkMeshUploadsPending", Voxels.MeshUploadQueue.PendingCount);
            PerformanceMonitor.PopFrame();

            if (RenderList != null && !Debugger.Switches.HideTerrain)
            {
                PerformanceMonitor.PushFrame("ChunkRenderer.Render.Terrain");
                PerformanceMonitor.SetMetric("ChunkRenderList", RenderList.Count);
                graphicsDevice.RasterizerState = new RasterizerState { CullMode = CullMode.None };

                foreach (VoxelChunk chunk in RenderList)
                {
                    effect.SetTexturedTechnique();
                    effect.EnableShadows = false;
                    effect.SelfIlluminationTexture = AssetManager.GetContentTexture(ContentPaths.Terrain.terrain_illumination);
                    effect.MainTexture = AssetManager.GetContentTexture(ContentPaths.Terrain.terrain_tiles);
                    effect.SunlightGradient = AssetManager.GetContentTexture(ContentPaths.Gradients.sungradient);
                    effect.AmbientOcclusionGradient = AssetManager.GetContentTexture(ContentPaths.Gradients.ambientgradient);
                    effect.TorchlightGradient = AssetManager.GetContentTexture(ContentPaths.Gradients.torchgradient);
                    effect.LightRamp = Color.White;
                    effect.VertexColorTint = Color.White;
                    effect.SelfIlluminationEnabled = true;
                    effect.World = Matrix.Identity;
                    effect.EnableLighting = true;

                    foreach (EffectPass pass in effect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        chunk.Render(GameState.Game.GraphicsDevice);
                    }

                    chunk.RenderMotes(GameState.Game.GraphicsDevice, effect, renderCamera);
                }
                PerformanceMonitor.PopFrame(); // ChunkRenderer.Render.Terrain
            }

            effect.SelfIlluminationEnabled = false;
            effect.SetTexturedTechnique();
        }

        private void GetChunksIntersecting(BoundingFrustum Frustum, HashSet<VoxelChunk> chunks)
        {
            chunks.Clear();
            var frustumBox = MathFunctions.GetBoundingBox(Frustum.GetCorners());
            var minChunk = ChunkData.ConfineToBounds(GlobalVoxelCoordinate.FromVector3(frustumBox.Min).GetGlobalChunkCoordinate());
            var maxChunk = ChunkData.ConfineToBounds(GlobalVoxelCoordinate.FromVector3(frustumBox.Max).GetGlobalChunkCoordinate());


            for (var x = minChunk.X; x <= maxChunk.X; ++x)
                for (var y = minChunk.Y; y <= maxChunk.Y; ++y)
                    for (var z = minChunk.Z; z <= maxChunk.Z; ++z)
                    {
                        var chunkCoord = new GlobalChunkCoordinate(x, y, z);
                        var min = new GlobalVoxelCoordinate(chunkCoord, new LocalVoxelCoordinate(0, 0, 0));
                        var box = new BoundingBox(min.ToVector3(), min.ToVector3() + new Vector3(VoxelConstants.ChunkSizeX, VoxelConstants.ChunkSizeY, VoxelConstants.ChunkSizeZ));
                        if (Frustum.Contains(box) != ContainmentType.Disjoint)
                            chunks.Add(ChunkData.GetChunk(chunkCoord));
                    }
        }

        private void GetChunksInRadius(Vector3 Point, float Radius, HashSet<VoxelChunk> Chunks)
        {
            Chunks.Clear();
            var minChunk = ChunkData.ConfineToBounds(GlobalVoxelCoordinate.FromVector3(Point - new Vector3(Radius, Radius, Radius)).GetGlobalChunkCoordinate());
            var maxChunk = ChunkData.ConfineToBounds(GlobalVoxelCoordinate.FromVector3(Point + new Vector3(Radius, Radius, Radius)).GetGlobalChunkCoordinate());

            for (var x = minChunk.X; x <= maxChunk.X; ++x)
                for (var y = minChunk.Y; y <= maxChunk.Y; ++y)
                    for (var z = minChunk.Z; z <= maxChunk.Z; ++z)
                    {
                        var chunkCoord = new GlobalChunkCoordinate(x, y, z);
                        Chunks.Add(ChunkData.GetChunk(chunkCoord));
                    }
        }

        public void Update(DwarfTime gameTime, Camera camera, GraphicsDevice g)
        {
            PerformanceMonitor.PushFrame("ChunkRenderer.Update");

            var frustum = camera.GetDrawFrustum();
            var viewProj = frustum.Matrix;

            // Fast path: camera is static AND we've refreshed recently — the visible set
            // cannot have changed geometrically, so reuse the previous RenderList instead of
            // doing the triple-nested frustum scan. Even with a static camera we still fall
            // through to the slow path every ForceRecomputeEveryFrames frames to catch chunks
            // that finish their background load/build (crucial during map startup, where a
            // pure skip would leave RenderList missing freshly-loaded terrain until the
            // player moves).
            _framesSinceFullRecompute += 1;
            if (_hasLastVisibilityViewProj
                && viewProj == _lastVisibilityViewProj
                && _framesSinceFullRecompute < ForceRecomputeEveryFrames)
            {
                foreach (var chunk in RenderList)
                    chunk.RenderCycleWhenLastVisible = RenderCycle;
                RenderCycle += 1;
                PerformanceMonitor.PopFrame();
                return;
            }

            _lastVisibilityViewProj = viewProj;
            _hasLastVisibilityViewProj = true;
            _framesSinceFullRecompute = 0;

            // Reuse the scratch set instead of allocating a new one each frame.
            GetChunksIntersecting(frustum, _visibleScratch);

            foreach (var chunk in _visibleScratch)
            {
                if (chunk.Visible == false)
                {
                    chunk.Visible = true;
                    // Only re-queue if there's a real pending invalidation. Previously
                    // this was unconditional, which meant every camera pan that brought
                    // a chunk back into the frustum triggered a full Rebuild (Concat +
                    // GPU upload) even with no geometry change. The IsInvalidated flag
                    // persists across visibility transitions: real slice/primitive
                    // invalidations set it, Rebuild consumes it, and an unchanged chunk
                    // returning to view now costs zero.
                    if (chunk.IsInvalidated)
                        chunk.Manager.InvalidateChunk(chunk);
                }

                chunk.RenderCycleWhenLastVisible = RenderCycle;
            }

            foreach (var chunk in RenderList)
                if (chunk.RenderCycleWhenLastVisible != RenderCycle)
                    chunk.Visible = false;

            // Rebuild RenderList in place so callers holding a reference still see it, and
            // we avoid allocating a new List every frame.
            RenderList.Clear();
            foreach (var chunk in _visibleScratch)
                RenderList.Add(chunk);

            //var loadedSet = new HashSet<VoxelChunk>();
            //GetChunksInRadius(camera.Position, GameSettings.Current.ChunkLoadDistance, loadedSet);
            //foreach (var chunk in loadedSet)
            //{
            //    if (chunk.Data == null)
            //        chunk.Manager.QueueChunkLoad(chunk);
            //    chunk.RenderCycleWhenLastLoaded = RenderCycle;
            //}

            //foreach (var chunk in LiveVoxelList)
            //    if (chunk.RenderCycleWhenLastLoaded != RenderCycle)
            //        chunk.Manager.QueueChunkSave(chunk);

            //LiveVoxelList = loadedSet.ToList();

            RenderCycle += 1;
            PerformanceMonitor.PopFrame();
        }
    }
}
