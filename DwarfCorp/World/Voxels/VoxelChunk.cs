using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Threading;

namespace DwarfCorp
{
    public partial class VoxelChunk
    {
        public GlobalChunkCoordinate ID;
        public GlobalVoxelCoordinate Origin;
        public ChunkManager Manager { get; set; }
        public VoxelData Data { get; set; }

        private GeometricPrimitive Primitive = null;
        public int RenderCycleWhenLastVisible = 0;
        public int RenderCycleWhenLastLoaded = 0;
        public bool Visible = false;
        public Mutex PrimitiveMutex { get; set; }

        private List<NewInstanceData>[] MoteRecords = new List<NewInstanceData>[VoxelConstants.ChunkSizeY];
        public Dictionary<int, LiquidPrimitive> Liquids { get; set; }
        public bool NewLiquidReceived = false;
                
        public List<DynamicLight> DynamicLights { get; set; }

        public HashSet<GameComponent> RootEntities = new HashSet<GameComponent>();
        public HashSet<GameComponent> EntityAnchors = new HashSet<GameComponent>();

        public int UpdateDitherPattern = 0;
        public int LiquidUpdateDitherPattern = 0;

        /// <summary>
        /// True whenever the chunk has geometry changes pending that a Rebuild hasn't
        /// processed yet. Set by any call that nulls a slice (<see cref="InvalidateSlice"/>,
        /// <see cref="InvalidateAllSlices"/>, <see cref="DiscardPrimitive"/>) and cleared
        /// at the end of <see cref="Rebuild"/>. Defaults true so freshly-created chunks
        /// trigger their initial build the first time they enter the frustum.
        ///
        /// Used by <see cref="ChunkRenderer"/> on the invisible→visible transition to
        /// decide whether an <see cref="ChunkManager.InvalidateChunk"/> call is needed.
        /// Without this flag the renderer unconditionally re-queued every chunk on
        /// frustum entry, triggering a full Concat + GPU re-upload even when the
        /// geometry was identical — very expensive during camera panning.
        /// </summary>
        public volatile bool IsInvalidated = true;

        public void InvalidateSlice(int LocalY)
        {
            if (LocalY < 0 || LocalY >= VoxelConstants.ChunkSizeY) throw new InvalidOperationException();

            lock (Data.SliceCache)
            {
                Data.SliceCache[LocalY] = null;
                IsInvalidated = true;
                Manager.InvalidateChunk(this);
            }
        }

        public void InvalidateAllSlices()
        {
            lock (Data.SliceCache)
            {
                Data.SliceCache = new RawPrimitive[VoxelConstants.ChunkSizeY];
                IsInvalidated = true;
                Manager.InvalidateChunk(this);
            }
        }

        public void DiscardPrimitive()
        {
            PrimitiveMutex.WaitOne();
            if (Primitive != null)
                Primitive.Dispose();
            Primitive = null;
            for (var y = 0; y < VoxelConstants.ChunkSizeY; ++y)
            {
                Data.SliceCache[y] = null;
                MoteRecords[y] = null;
            }
            IsInvalidated = true;

            PrimitiveMutex.ReleaseMutex();

            // Eviction race fix. EnqueueDiscard is called on invisible chunks by the
            // MaxLiveChunks eviction loop, but between enqueue and drain the camera
            // can pan the chunk back into view. ChunkRenderer.Update's visibility
            // transition check runs BEFORE MeshUploadQueue.DrainUpToBudget in the
            // same frame — so the transition sees IsInvalidated=false and skips the
            // rebuild queue, then drain nulls Primitive here, leaving the chunk
            // visible-but-empty with no pending rebuild. Next frames Visible is
            // already true, no transition fires, the chunk stays empty forever.
            //
            // Requeuing here from DiscardPrimitive catches exactly that race: if the
            // chunk is now visible at discard time, the rebuild queue gets the chunk
            // and the next rebuild cycle refills Primitive.
            if (Visible)
                Manager.InvalidateChunk(this);
        }

        public VoxelChunk(ChunkManager manager, GlobalChunkCoordinate id)
        {
            ID = id;
            Origin = new GlobalVoxelCoordinate(id, new LocalVoxelCoordinate(0,0,0));
            Data = VoxelData.Allocate();
            Manager = manager;

            PrimitiveMutex = new Mutex();
            DynamicLights = new List<DynamicLight>();

            Liquids = new Dictionary<int, LiquidPrimitive>();
            foreach (var liquid in Library.EnumerateLiquids())
                Liquids[liquid.ID] = new LiquidPrimitive(liquid.ID);
        }
       
        private BoundingBox m_boundingBox;
        private bool m_boundingBoxCreated = false;

        public BoundingBox GetBoundingBox()
        {
            if (!m_boundingBoxCreated)
            {
                Vector3 max = new Vector3(VoxelConstants.ChunkSizeX, VoxelConstants.ChunkSizeY, VoxelConstants.ChunkSizeZ) + Origin.ToVector3();
                m_boundingBox = new BoundingBox(Origin.ToVector3(), max);
                m_boundingBoxCreated = true;
            }

            return m_boundingBox;
        }
        
        public void Render(GraphicsDevice device)
        {
            PrimitiveMutex.WaitOne();
            if (Primitive != null) Primitive.Render(device);
            PrimitiveMutex.ReleaseMutex();
        }

        public void RebuildLiquidGeometry()
        {
            System.Threading.Interlocked.Increment(ref PerfCounters.LiquidGeomRebuilds);
            var toInit = new List<LiquidPrimitive>();
            foreach (var primitive in Liquids)
                toInit.Add(primitive.Value);
            LiquidPrimitive.InitializePrimativesFromChunk(this, toInit);
        }

        /// <summary>
        /// Runs on a background mesh-gen thread. Produces a fresh
        /// <see cref="GeometricPrimitive"/> from the chunk's voxel data and
        /// hands it to <see cref="Voxels.MeshUploadQueue"/> for the render
        /// thread to swap in via <see cref="ApplyFreshPrimitive"/>. The
        /// old primitive is NOT disposed here — that crosses a thread
        /// boundary (GPU buffer Dispose), so it moves to the render thread.
        ///
        /// Fase B.1 split: before this refactor, Rebuild both produced and
        /// swapped in one step. Running two of those in parallel races the
        /// old-primitive Dispose against ChunkRenderer reading the primitive,
        /// which is the exact bug that got the original Fase 1.1 reverted.
        /// Now: producers do pure CPU work + Enqueue; consumer (one, on the
        /// render thread) swaps.
        /// </summary>
        public void Rebuild(GraphicsDevice g)
        {
            if (g == null || g.IsDisposed)
                return;

            // Mark that we're consuming the current invalidation up-front. Any
            // InvalidateSlice that races this method after this point will set the
            // flag back to true and re-queue the chunk, guaranteeing a subsequent
            // rebuild catches the mid-flight change. If we cleared at the end we'd
            // lose that race.
            IsInvalidated = false;

            GeometricPrimitive primitive;

            // Fase B.2 live: greedy meshing only exists on the new geometry builder
            // path. Flip UseNewVoxelGeoGen implicitly when greedy is requested so the
            // user has a single switch to toggle in the F12 Debugger panel.
            if (Debugger.Switches.UseNewVoxelGeoGen || Debugger.Switches.UseGreedyMeshing)
            {
                primitive = Voxels.GeometryBuilder.CreateFromChunk(this, Manager.World);
            }
            else
            {
                var vlp = new VoxelListPrimitive();
                vlp.InitializeFromChunk(this, Manager.World);
                primitive = vlp;
            }

            Voxels.MeshUploadQueue.Enqueue(this, primitive);
        }

        /// <summary>
        /// Render-thread swap of the live primitive. Called by
        /// <see cref="Voxels.MeshUploadQueue.DrainUpToBudget"/> after a
        /// background worker hands off a freshly-generated primitive.
        /// Disposing the previous primitive (which releases GPU buffers)
        /// is safe here because ChunkRenderer — the other reader of
        /// <see cref="Primitive"/> — also runs on the render thread.
        /// </summary>
        public void ApplyFreshPrimitive(GeometricPrimitive fresh)
        {
            PrimitiveMutex.WaitOne();
            try
            {
                if (Primitive != null)
                    Primitive.Dispose();
                Primitive = fresh;
            }
            finally
            {
                PrimitiveMutex.ReleaseMutex();
            }
        }

        public void Destroy()
        {
            if (Primitive != null)
                Primitive.Dispose();
        }
    }
}
