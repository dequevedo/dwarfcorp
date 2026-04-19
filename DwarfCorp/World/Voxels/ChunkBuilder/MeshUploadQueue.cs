using System.Collections.Concurrent;
using System.Threading;

namespace DwarfCorp.Voxels
{
    /// <summary>
    /// Hand-off point between the chunk rebuild worker(s) and the render thread
    /// for freshly-generated <see cref="GeometricPrimitive"/>s.
    ///
    /// Fase B.1 split: background threads produce <see cref="GeometricPrimitive"/>
    /// objects that hold only CPU-side arrays (no GPU buffers yet — that's lazy
    /// in <c>Primitive.Render</c>). They enqueue here. The main thread drains at
    /// the start of each render pass and calls <see cref="VoxelChunk.ApplyFreshPrimitive"/>
    /// to swap the live primitive + dispose the old one. All GPU-touching work —
    /// the swap's <c>Dispose()</c>, and the lazy buffer alloc that happens on
    /// the chunk's next <c>Render</c> — is now guaranteed to run on the render
    /// thread, with no race window between a background disposer and a render
    /// reader.
    ///
    /// That's the whole point of the queue: enable parallel mesh generation
    /// (planned as the next step) without the HEAP_CORRUPTION class of bug that
    /// got the original Fase 1.1 parallelization reverted.
    ///
    /// Backpressure: <see cref="DrainUpToBudget"/> caps how many swaps per frame.
    /// If workers produce faster than the render thread can swap, items sit in
    /// the queue until future frames. Dropping would be wrong — stale chunks
    /// stay visible until their fresh mesh lands.
    /// </summary>
    public static class MeshUploadQueue
    {
        private static readonly ConcurrentQueue<(VoxelChunk Chunk, GeometricPrimitive Fresh)> _pending = new();
        private static long _totalEnqueued;
        private static long _totalDrained;

        public static int PendingCount => _pending.Count;
        public static long TotalEnqueued => Interlocked.Read(ref _totalEnqueued);
        public static long TotalDrained => Interlocked.Read(ref _totalDrained);

        /// <summary>
        /// Enqueue a fresh primitive to be swapped in on the render thread.
        /// <paramref name="fresh"/> must hold only its CPU-side Vertices/Indexes
        /// arrays — its GPU buffers get allocated lazily on the first
        /// <see cref="GeometricPrimitive.Render"/> after the swap.
        /// </summary>
        public static void Enqueue(VoxelChunk chunk, GeometricPrimitive fresh)
        {
            if (chunk == null) return;
            _pending.Enqueue((chunk, fresh));
            Interlocked.Increment(ref _totalEnqueued);
        }

        /// <summary>
        /// Enqueue a DISCARD for <paramref name="chunk"/>. The render thread will
        /// call <see cref="VoxelChunk.DiscardPrimitive"/> — disposing the existing
        /// GPU buffers there, not on the bg thread, stays on the contract
        /// "only the render thread ever disposes GPU resources".
        /// </summary>
        public static void EnqueueDiscard(VoxelChunk chunk)
        {
            if (chunk == null) return;
            _pending.Enqueue((chunk, null));
            Interlocked.Increment(ref _totalEnqueued);
        }

        /// <summary>
        /// Drain up to <paramref name="maxItems"/> pending swaps/discards. Call
        /// from the render thread at the top of the frame. Returns the count
        /// actually performed so callers can feed a profiler counter.
        /// </summary>
        public static int DrainUpToBudget(int maxItems)
        {
            int drained = 0;
            while (drained < maxItems && _pending.TryDequeue(out var item))
            {
                if (item.Fresh != null)
                    item.Chunk.ApplyFreshPrimitive(item.Fresh);
                else
                    item.Chunk.DiscardPrimitive();
                drained++;
            }
            if (drained > 0) Interlocked.Add(ref _totalDrained, drained);
            return drained;
        }

        /// <summary>Drop all pending items (session teardown).</summary>
        public static void Clear()
        {
            while (_pending.TryDequeue(out _)) { }
        }
    }
}
