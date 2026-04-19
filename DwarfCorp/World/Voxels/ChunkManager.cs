using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <summary>
    /// Responsible for keeping track of and accessing large collections of
    /// voxels. There is intended to be only one chunk manager. Essentially,
    /// it is a virtual memory lookup table for the world's voxels. It imitates
    /// a gigantic 3D array.
    /// </summary>
    public partial class ChunkManager
    {
        private Queue<VoxelChunk> RebuildQueue = new Queue<VoxelChunk>();
        private HashSet<VoxelChunk> RebuildQueueMembers = new HashSet<VoxelChunk>();
        private readonly object RebuildQueueLock = new object();
        private AutoResetEvent RebuildEvent = new AutoResetEvent(true);
        public bool NeedsMinimapUpdate = true;

        private Queue<GlobalChunkCoordinate> InvalidColumns = new Queue<GlobalChunkCoordinate>();
        private HashSet<GlobalChunkCoordinate> InvalidColumnMembers = new HashSet<GlobalChunkCoordinate>();
        private readonly object InvalidColumnLock = new object();

        public void InvalidateChunk(VoxelChunk Chunk)
        {
            lock (RebuildQueueLock)
            {
                RebuildEvent.Set();
                if (RebuildQueueMembers.Add(Chunk))
                    RebuildQueue.Enqueue(Chunk);
            }

            EnqueueInvalidColumn(Chunk.ID.X, Chunk.ID.Z);
        }

        public VoxelChunk PopInvalidChunk()
        {
            lock (RebuildQueueLock)
            {
                if (RebuildQueue.Count == 0) return null;
                var result = RebuildQueue.Dequeue();
                RebuildQueueMembers.Remove(result);
                return result;
            }
        }

        public void EnqueueInvalidColumn(int X, int Z)
        {
            var columnCoordinate = new GlobalChunkCoordinate(X, 0, Z);
            lock (InvalidColumnLock)
            {
                if (InvalidColumnMembers.Add(columnCoordinate))
                    InvalidColumns.Enqueue(columnCoordinate);
            }
        }

        public GlobalChunkCoordinate? PopInvalidColumn()
        {
            lock (InvalidColumnLock)
            {
                if (InvalidColumns.Count == 0) return null;
                var result = InvalidColumns.Dequeue();
                InvalidColumnMembers.Remove(result);
                return result;
            }
        }

        private Thread RebuildThread { get; set; }
        private Thread ChunkUpdateThread { get; set; }
        private AutoScaleThread WaterUpdateThread;

        public BoundingBox Bounds { get; set; }

        public bool PauseThreads { get; set; }

        public bool ExitThreads { get; set; }

        public WorldManager World { get; set; }
        public ContentManager Content { get; set; }

        public WaterManager Water { get; set; }

        public Timer ChunkUpdateTimer = new Timer(0.1f, false, Timer.TimerMode.Game);

        // Todo: Move this.
        public bool IsAboveCullPlane(BoundingBox Box)
        {
            return Box.Min.Y > (World.Renderer.PersistentSettings.MaxViewingLevel + 5);
        }

        public VoxelHandle CreateVoxelHandle(GlobalVoxelCoordinate Coordinate)
        {
            return new VoxelHandle(this, Coordinate);
        }

        public LiquidCellHandle CreateLiquidCellHandle(GlobalLiquidCoordinate Coordinate)
        {
            return new LiquidCellHandle(this, Coordinate);
        }

        public ChunkManager(ContentManager Content, WorldManager World)
        {
            this.Content = Content;
            this.World = World;

            ExitThreads = false;

            InitializeChunkMap(Point3.Zero, World.WorldSizeInChunks);             

            RebuildThread = new Thread(RebuildVoxelsThread) { IsBackground = true };
            RebuildThread.Name = "RebuildVoxels";

            WaterUpdateThread = new AutoScaleThread(this, (f) => Water.UpdateWater(), "WaterUpdate");
            this.ChunkUpdateThread = new Thread(UpdateChunksThread) { IsBackground = true, Name = "Update Chunks" };

            GameSettings.Current.VisibilityUpdateTime = 0.05f;

            Water = new WaterManager(this);

            PauseThreads = false;

            Vector3 maxBounds = new Vector3(
                World.WorldSizeInChunks.X * VoxelConstants.ChunkSizeX / 2.0f,
                World.WorldSizeInChunks.Y * VoxelConstants.ChunkSizeY / 2.0f,
                World.WorldSizeInChunks.Z * VoxelConstants.ChunkSizeZ / 2.0f);
            Vector3 minBounds = -maxBounds; // Todo: Can this just be 0,0,0?
            Bounds = new BoundingBox(minBounds, maxBounds);
        }

        public void StartThreads()
        {
            RebuildThread.Start();
            WaterUpdateThread.Start();
            ChunkUpdateThread.Start();
        }

        public void RebuildVoxelsThread()
        {
            Console.Out.WriteLine("Starting chunk regeneration thread.");
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

            var liveChunks = new List<VoxelChunk>();

#if !DEBUG
            try
#endif
            {
                while (!DwarfGame.ExitGame && !ExitThreads)
                {
                    try
                    {
                        RebuildEvent.WaitOne();
                    }
                    catch (ThreadAbortException exception)
                    {
                        continue;
                    }

                    // Fase B.1 (parallelize): drain all invalidated + visible chunks into a
                    // batch, then Parallel.ForEach over them. After the B.1 split every
                    // worker does pure CPU mesh-gen + a lock-free Enqueue — zero GPU writes
                    // on this thread. The render thread picks up the swaps via
                    // MeshUploadQueue.DrainUpToBudget at the top of its frame.
                    //
                    // Bookkeeping (liveChunks add + MaxLiveChunks eviction) still runs
                    // serially on this thread after the parallel phase. Evictions also go
                    // through MeshUploadQueue (EnqueueDiscard) so GPU dispose stays on
                    // the render thread — same contract as rebuilds.
                    PerformanceMonitor.PushFrame("ChunkMeshGenBatch");
                    var batch = new List<VoxelChunk>();
                    VoxelChunk popped;
                    do
                    {
                        popped = PopInvalidChunk();
                        if (popped != null && popped.Visible)
                            batch.Add(popped);
                    } while (popped != null);

                    if (batch.Count > 0)
                    {
                        int workers = System.Math.Max(1, System.Environment.ProcessorCount - 1);
                        PerformanceMonitor.SetMetric("ChunkMeshGenBatchSize", batch.Count);
                        PerformanceMonitor.SetMetric("ChunkMeshGenWorkers", workers);

                        var gd = GameState.Game.GraphicsDevice;
                        System.Threading.Tasks.Parallel.ForEach(
                            batch,
                            new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = workers },
                            chunk => chunk.Rebuild(gd));

                        foreach (var chunk in batch)
                        {
                            liveChunks.Add(chunk);
                            if (liveChunks.Count > GameSettings.Current.MaxLiveChunks)
                            {
                                liveChunks.Sort((a, b) => a.RenderCycleWhenLastVisible - b.RenderCycleWhenLastVisible);
                                while (liveChunks.Count > GameSettings.Current.MaxLiveChunks)
                                {
                                    if (liveChunks[0].Visible) break;
                                    Voxels.MeshUploadQueue.EnqueueDiscard(liveChunks[0]);
                                    liveChunks.RemoveAt(0);
                                }
                            }
                        }
                        NeedsMinimapUpdate = true;
                    }
                    PerformanceMonitor.PopFrame();

                    PerformanceMonitor.SetMetric("VISIBLE CHUNKS", liveChunks.Count);
                }
            }
#if !DEBUG
            catch (Exception exception)
            {
                Console.Out.WriteLine("Chunk regeneration thread encountered an exception.");
                ProgramData.WriteExceptionLog(exception);
                //throw;
            }
#endif       
            Console.Out.WriteLine(String.Format("Chunk regeneration thread exited cleanly Exit Game: {0} Exit Thread: {1}.", DwarfGame.ExitGame, ExitThreads));
        }

        public void RecalculateBounds()
        {
            List<BoundingBox> boxes = GetChunkEnumerator().Select(c => c.GetBoundingBox()).ToList();
            Bounds = MathFunctions.GetBoundingBox(boxes);
        }

        private IEnumerable<VoxelChunk> EnumerateAdjacentChunks(VoxelChunk Chunk)
        {
            for (int dx = -1; dx < 2; dx++)
                for (int dz = -1; dz < 2; dz++)
                    if (dx != 0 || dz != 0)
                    {
                        var adjacentCoord = new GlobalChunkCoordinate(
                            Chunk.ID.X + dx, 0, Chunk.ID.Z + dz);
                        if (CheckBounds(adjacentCoord))
                            yield return GetChunk(adjacentCoord);
                    }
        }

        public void UpdateChunksThread()
        {
            ChunkUpdate.ChunkUpdateThread(this);
        }

        public void UpdateBounds()
        {
            var boundingBoxes = GetChunkEnumerator().Select(c => c.GetBoundingBox());
            Bounds = MathFunctions.GetBoundingBox(boundingBoxes);
        }

        public void Destroy()
        {
            PauseThreads = true;
            ExitThreads = true;
            RebuildEvent.Set();
            RebuildThread.Join();
            WaterUpdateThread.Join();
            ChunkUpdateThread.Join();
            foreach (var item in ChunkMap)
                item.Destroy();
        }
    }
}
