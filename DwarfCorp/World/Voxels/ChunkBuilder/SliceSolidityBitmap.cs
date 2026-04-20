using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace DwarfCorp.Voxels
{
    /// <summary>
    /// Fase B.3 — SIMD scan of a chunk slice's voxel-type array to produce a 256-bit
    /// "is non-empty" bitmap (one bit per voxel in the slice). Used as a fast-path by
    /// the greedy meshing passes: <see cref="GeometryBuilder.TryMakeGreedyFaceKey"/>
    /// can reject empty voxels via a single bit test instead of reading a byte +
    /// reaching into <see cref="VoxelHandle"/>.
    ///
    /// Layout. A chunk's voxel-type array has index <c>Y * 256 + Z * 16 + X</c>,
    /// so the 256 bytes of one slice (fixed Y) are laid out contiguously: 16 rows of
    /// 16 columns, row-major by Z. That matches a clean
    /// <c>Vector256&lt;byte&gt;</c>-based compare-against-zero: 256 bytes / 32 =
    /// <b>8 SIMD ops</b> per slice.
    ///
    /// Bitmap. 256 bits are packed into <c>ulong[4]</c>. Bit at index
    /// <c>localZ * 16 + localX</c> is 1 iff the voxel's type byte is non-zero
    /// (i.e. the voxel is not an empty-air voxel).
    ///
    /// Fallback. When <see cref="Avx2.IsSupported"/> is false (old CPUs, ARM), a
    /// scalar loop produces the identical bitmap. Callers see no semantic
    /// difference either way — the SIMD path is a pure performance fast-path.
    ///
    /// ThreadStatic storage. The greedy pass is called from parallel chunk-rebuild
    /// workers; each worker keeps its own bitmap buffer via <c>[ThreadStatic]</c>
    /// in the caller. We don't allocate inside this helper.
    /// </summary>
    public static class SliceSolidityBitmap
    {
        public const int SliceByteCount = VoxelConstants.ChunkSizeX * VoxelConstants.ChunkSizeZ;  // 256
        public const int BitmapUlongCount = SliceByteCount / 64;                                    // 4

        /// <summary>
        /// Fill <paramref name="bitmap"/> with the "is non-zero" bitmap of 256 voxel
        /// types starting at <c>types[sliceStart..sliceStart+256]</c>. Must be
        /// <see cref="BitmapUlongCount"/> entries.
        /// </summary>
        public static void Build(byte[] types, int sliceStart, ulong[] bitmap)
        {
            bitmap[0] = 0; bitmap[1] = 0; bitmap[2] = 0; bitmap[3] = 0;

            if (Avx2.IsSupported)
            {
                BuildAvx2(types, sliceStart, bitmap);
            }
            else
            {
                BuildScalar(types, sliceStart, bitmap);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSolid(ulong[] bitmap, int localX, int localZ)
        {
            int idx = localZ * VoxelConstants.ChunkSizeX + localX;
            return (bitmap[idx >> 6] & (1UL << (idx & 63))) != 0;
        }

        private static unsafe void BuildAvx2(byte[] types, int sliceStart, ulong[] bitmap)
        {
            // Pin types[] once, step 32 bytes at a time. Compare-equal against zero
            // gives 0xFF where the byte IS zero, so we INVERT with MoveMask and a
            // NOT to get "non-zero". MoveMask packs the top bit of each of 32 bytes
            // into a 32-bit integer.
            fixed (byte* basePtr = &types[sliceStart])
            {
                var zero = Vector256<byte>.Zero;

                // 8 ops * 32 bytes = 256 bytes. Each op produces a 32-bit solidity
                // mask covering 32 contiguous byte positions = 32 voxels in
                // row-major (Z, X) order. Every 2 ops fill one 64-bit ulong.
                for (int op = 0; op < 8; op++)
                {
                    int byteOffset = op << 5; // op * 32
                    var v = Avx.LoadVector256(basePtr + byteOffset);
                    var eq = Avx2.CompareEqual(v, zero);
                    uint zeroMask = (uint)Avx2.MoveMask(eq);
                    uint solidMask = ~zeroMask;

                    int ulongIndex = op >> 1;           // op / 2
                    int bitShift = (op & 1) << 5;       // (op % 2) * 32
                    bitmap[ulongIndex] |= ((ulong)solidMask) << bitShift;
                }
            }
        }

        private static void BuildScalar(byte[] types, int sliceStart, ulong[] bitmap)
        {
            // One byte → one bit. Kept deliberately straightforward so the fallback
            // is obvious to review and produces the same bitmap as the SIMD path.
            for (int i = 0; i < SliceByteCount; i++)
            {
                if (types[sliceStart + i] != 0)
                    bitmap[i >> 6] |= 1UL << (i & 63);
            }
        }
    }
}
