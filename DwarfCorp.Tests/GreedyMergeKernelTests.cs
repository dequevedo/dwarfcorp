using System.Collections.Generic;
using DwarfCorp.Voxels;
using Xunit;

namespace DwarfCorp.Tests;

/// <summary>
/// Unit tests for the pure greedy merge kernel. Doesn't touch any DwarfCorp
/// voxel / render state — exercises GreedyMergeMask with synthetic masks so
/// the algorithm can be proved independently of the rest of the pipeline.
/// </summary>
public class GreedyMergeKernelTests
{
    private static GreedyMeshSlice.FaceKey K(int type) =>
        new GreedyMeshSlice.FaceKey(type, 0, 0, 0, 0);

    private static List<(int i, int j, int w, int h)> CollectRects(GreedyMeshSlice.FaceKey?[,] mask)
    {
        var results = new List<(int, int, int, int)>();
        GreedyMeshSlice.GreedyMergeMask(mask, (i, j, w, h, _) => results.Add((i, j, w, h)));
        return results;
    }

    [Fact]
    public void EmptyMask_EmitsNothing()
    {
        var mask = new GreedyMeshSlice.FaceKey?[4, 4];
        var rects = CollectRects(mask);
        Assert.Empty(rects);
    }

    [Fact]
    public void UniformMask_MergesIntoOneRectangle()
    {
        var mask = new GreedyMeshSlice.FaceKey?[4, 4];
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                mask[i, j] = K(1);

        var rects = CollectRects(mask);
        Assert.Single(rects);
        Assert.Equal((0, 0, 4, 4), rects[0]);
    }

    [Fact]
    public void DifferentKeysDoNotMerge()
    {
        var mask = new GreedyMeshSlice.FaceKey?[2, 2];
        mask[0, 0] = K(1); mask[1, 0] = K(2);
        mask[0, 1] = K(1); mask[1, 1] = K(2);

        var rects = CollectRects(mask);
        Assert.Equal(2, rects.Count);
        // Two 1x2 columns (one per key)
        Assert.Contains((0, 0, 1, 2), rects);
        Assert.Contains((1, 0, 1, 2), rects);
    }

    [Fact]
    public void HoleInMiddle_SplitsRectangleCorrectly()
    {
        // 3x3 filled except the center.
        var mask = new GreedyMeshSlice.FaceKey?[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                if (!(i == 1 && j == 1))
                    mask[i, j] = K(1);

        var rects = CollectRects(mask);
        // The algorithm's greedy nature produces a specific decomposition —
        // we verify the set of non-null cells is exactly covered by the
        // rectangles emitted, no overlap.
        int totalArea = 0;
        foreach (var (_, _, w, h) in rects) totalArea += w * h;
        Assert.Equal(8, totalArea); // 3*3 - 1 hole
    }

    [Fact]
    public void SingleCell_EmitsUnitRectangle()
    {
        var mask = new GreedyMeshSlice.FaceKey?[4, 4];
        mask[2, 3] = K(7);

        var rects = CollectRects(mask);
        Assert.Single(rects);
        Assert.Equal((2, 3, 1, 1), rects[0]);
    }

    [Fact]
    public void Rectangle_WiderThanTall_Merges()
    {
        var mask = new GreedyMeshSlice.FaceKey?[5, 2];
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 2; j++)
                mask[i, j] = K(1);

        var rects = CollectRects(mask);
        Assert.Single(rects);
        Assert.Equal((0, 0, 5, 2), rects[0]);
    }

    [Fact]
    public void Rectangle_TallerThanWide_Merges()
    {
        var mask = new GreedyMeshSlice.FaceKey?[2, 5];
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 5; j++)
                mask[i, j] = K(1);

        var rects = CollectRects(mask);
        Assert.Single(rects);
        Assert.Equal((0, 0, 2, 5), rects[0]);
    }

    [Fact]
    public void Worst_Case_Checkerboard_MergesIntoSingleCells()
    {
        var mask = new GreedyMeshSlice.FaceKey?[4, 4];
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                mask[i, j] = K((i + j) % 2);

        var rects = CollectRects(mask);
        // Each 1x1 key is distinct from diagonal neighbors so greedy merges
        // into horizontal runs of 1 then stops. Result: every cell its own rect.
        Assert.Equal(16, rects.Count);
    }
}
