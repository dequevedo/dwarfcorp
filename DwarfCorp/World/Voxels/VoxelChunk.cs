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

        public void InvalidateSlice(int LocalY)
        {
            if (LocalY < 0 || LocalY >= VoxelConstants.ChunkSizeY) throw new InvalidOperationException();

            lock (Data.SliceCache)
            {
                Data.SliceCache[LocalY] = null;
                Manager.InvalidateChunk(this);
            }
        }

        public void InvalidateAllSlices()
        {
            lock (Data.SliceCache)
            {
                Data.SliceCache = new RawPrimitive[VoxelConstants.ChunkSizeY];
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
           
            PrimitiveMutex.ReleaseMutex();
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
