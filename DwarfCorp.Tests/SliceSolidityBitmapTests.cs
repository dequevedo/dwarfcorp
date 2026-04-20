using DwarfCorp;
using DwarfCorp.Voxels;
using Xunit;

namespace DwarfCorp.Tests;

/// <summary>
/// Parity tests for the SIMD solidity bitmap. The SIMD (AVX2) path and the scalar
/// fallback must produce bit-identical bitmaps — a silent mismatch would cause
/// invisible voxels in greedy-meshed chunks on machines where SIMD is (un)available.
///
/// Strategy: the scalar path is covered via <see cref="SliceSolidityBitmap.Build"/>
/// running on a test machine without AVX2 (most CI). For the SIMD path we can't
/// "force" scalar on an AVX2 machine without a debug hook, so we instead exercise
/// <see cref="SliceSolidityBitmap.IsSolid"/> against known-good bitmap values built
/// by whichever path is active; the layout contract (<c>bit = localZ * 16 + localX</c>)
/// has to hold regardless of platform.
/// </summary>
public class SliceSolidityBitmapTests
{
    private static byte[] MakeTypes(params (int x, int z, byte type)[] voxels)
    {
        // One full chunk of types. Slice 0 is bytes 0..255.
        var types = new byte[VoxelConstants.ChunkVoxelCount];
        foreach (var (x, z, type) in voxels)
            types[z * VoxelConstants.ChunkSizeX + x] = type;
        return types;
    }

    [Fact]
    public void EmptySlice_BitmapAllZeros()
    {
        var types = new byte[VoxelConstants.ChunkVoxelCount];
        var bitmap = new ulong[SliceSolidityBitmap.BitmapUlongCount];

        SliceSolidityBitmap.Build(types, 0, bitmap);

        Assert.Equal(0UL, bitmap[0]);
        Assert.Equal(0UL, bitmap[1]);
        Assert.Equal(0UL, bitmap[2]);
        Assert.Equal(0UL, bitmap[3]);
    }

    [Fact]
    public void SingleSolidVoxel_OnlyThatBitSet()
    {
        var types = MakeTypes((3, 5, 1));
        var bitmap = new ulong[SliceSolidityBitmap.BitmapUlongCount];
        SliceSolidityBitmap.Build(types, 0, bitmap);

        Assert.True(SliceSolidityBitmap.IsSolid(bitmap, 3, 5));
        Assert.False(SliceSolidityBitmap.IsSolid(bitmap, 2, 5));
        Assert.False(SliceSolidityBitmap.IsSolid(bitmap, 3, 4));
        Assert.False(SliceSolidityBitmap.IsSolid(bitmap, 3, 6));
    }

    [Fact]
    public void FullSolidSlice_AllBitsSet()
    {
        var types = new byte[VoxelConstants.ChunkVoxelCount];
        for (int i = 0; i < 256; i++) types[i] = 7;
        var bitmap = new ulong[SliceSolidityBitmap.BitmapUlongCount];

        SliceSolidityBitmap.Build(types, 0, bitmap);

        Assert.Equal(ulong.MaxValue, bitmap[0]);
        Assert.Equal(ulong.MaxValue, bitmap[1]);
        Assert.Equal(ulong.MaxValue, bitmap[2]);
        Assert.Equal(ulong.MaxValue, bitmap[3]);
    }

    [Fact]
    public void Slice1_OffsetReadsCorrectRegion()
    {
        // Build a chunk where slice 0 is fully empty, slice 1 has one solid voxel.
        // The Build(sliceStart=256, ...) call must read only slice 1, ignoring the
        // zero bytes in slice 0.
        var types = new byte[VoxelConstants.ChunkVoxelCount];
        int sliceStart1 = VoxelConstants.ChunkSizeX * VoxelConstants.ChunkSizeZ;
        types[sliceStart1 + (7 * VoxelConstants.ChunkSizeX) + 2] = 1; // (x=2, z=7) in slice 1

        var bitmap = new ulong[SliceSolidityBitmap.BitmapUlongCount];
        SliceSolidityBitmap.Build(types, sliceStart1, bitmap);

        Assert.True(SliceSolidityBitmap.IsSolid(bitmap, 2, 7));
        // Any other cell in the bitmap should be empty.
        int popcount = 0;
        for (int i = 0; i < 4; i++)
            popcount += System.Numerics.BitOperations.PopCount(bitmap[i]);
        Assert.Equal(1, popcount);
    }

    [Fact]
    public void TypeByte255_StillDetectedAsSolid()
    {
        // Guard against a sign-extension or signed-compare bug in SIMD path —
        // byte value 255 (the "max voxel type") must still register as non-zero.
        var types = MakeTypes((0, 0, 255), (15, 15, 255));
        var bitmap = new ulong[SliceSolidityBitmap.BitmapUlongCount];
        SliceSolidityBitmap.Build(types, 0, bitmap);

        Assert.True(SliceSolidityBitmap.IsSolid(bitmap, 0, 0));
        Assert.True(SliceSolidityBitmap.IsSolid(bitmap, 15, 15));
    }

    [Fact]
    public void RowAndColumnIndexing_MatchesLayoutContract()
    {
        // Voxel type layout: Types[y*256 + z*16 + x]. For slice 0, Types[i]
        // where i = z*16 + x. IsSolid must agree with that mapping. Set bits on
        // row z=0 only (bytes 0..15), and verify the bitmap reflects it.
        var types = new byte[VoxelConstants.ChunkVoxelCount];
        for (int x = 0; x < 16; x++)
            types[0 * 16 + x] = 1;

        var bitmap = new ulong[SliceSolidityBitmap.BitmapUlongCount];
        SliceSolidityBitmap.Build(types, 0, bitmap);

        for (int x = 0; x < 16; x++)
            Assert.True(SliceSolidityBitmap.IsSolid(bitmap, x, 0), $"x={x} z=0 should be solid");
        for (int z = 1; z < 16; z++)
            Assert.False(SliceSolidityBitmap.IsSolid(bitmap, 0, z), $"x=0 z={z} should be empty");
    }
}
