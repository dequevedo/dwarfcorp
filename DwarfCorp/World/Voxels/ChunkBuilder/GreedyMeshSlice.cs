using System;
using Microsoft.Xna.Framework;

namespace DwarfCorp.Voxels
{
    /// <summary>
    /// Fase B.2 — greedy meshing for chunk TOP faces. Opt-in via
    /// <c>Debugger.Switches.UseGreedyMeshing</c>; default OFF until the
    /// rendered output is validated interactively against the per-voxel
    /// baseline.
    ///
    /// Why top only to start: top faces are where the uniformity wins are
    /// biggest (grass/stone floors, dungeon ceilings, open-world ground
    /// plane), the geometry is simplest (single axis-aligned plane per
    /// slice), and the risk of visual regression is bounded. Sides + bottom
    /// get their own commits once the top path is proven in-game.
    ///
    /// Algorithm: Mikola/Tantan greedy mesh. For each slice in a chunk
    /// that has visible top faces:
    ///   1. Build a 2D mask (ChunkSizeX × ChunkSizeZ) where each cell is
    ///      either null (no greedy-eligible top face here) or a FaceKey
    ///      describing the face's mergeable attributes.
    ///   2. Scan the mask: for each non-null cell, extend right while the
    ///      key matches, then extend down while the whole row matches.
    ///      Emit one merged quad over the (w × h) rectangle. Clear the
    ///      cells it covered. Continue.
    ///   3. Mask cells that are null (voxel has grass fringe, decal, etc.)
    ///      are NOT handled here — the per-voxel fallback path emits
    ///      those faces untouched. Complex features survive unchanged.
    ///
    /// Merge predicate: strict byte-identical FaceKey equality. Two top
    /// faces merge only if voxel type, tile (atlas coordinate), explored-
    /// neighbor count, and lighting all match. Under-merge is OK; incorrect
    /// merges corrupt the render. Strict beats aggressive here.
    /// </summary>
    public static class GreedyMeshSlice
    {
        /// <summary>
        /// Compact identity for "this top face". Two voxels' top faces merge
        /// iff their FaceKeys compare byte-equal. Non-mergeable cases
        /// (grass fringe, decal, hidden slice) never land in the mask and
        /// skip greedy entirely.
        /// </summary>
        public readonly struct FaceKey : IEquatable<FaceKey>
        {
            public readonly int VoxelType;
            public readonly int TileX;
            public readonly int TileY;
            public readonly int ExploredNeighborBits;
            public readonly int LightHash;

            public FaceKey(int voxelType, int tileX, int tileY, int exploredBits, int lightHash)
            {
                VoxelType = voxelType;
                TileX = tileX;
                TileY = tileY;
                ExploredNeighborBits = exploredBits;
                LightHash = lightHash;
            }

            public bool Equals(FaceKey other) =>
                VoxelType == other.VoxelType
                && TileX == other.TileX
                && TileY == other.TileY
                && ExploredNeighborBits == other.ExploredNeighborBits
                && LightHash == other.LightHash;

            public override bool Equals(object obj) => obj is FaceKey k && Equals(k);
            public override int GetHashCode() =>
                VoxelType * 31 + TileX * 97 + TileY * 103 + ExploredNeighborBits * 109 + LightHash;

            public static bool operator ==(FaceKey a, FaceKey b) => a.Equals(b);
            public static bool operator !=(FaceKey a, FaceKey b) => !a.Equals(b);
        }

        /// <summary>
        /// Pure greedy-merge kernel. Given a 2D grid of nullable face keys,
        /// emit the rectangles that the algorithm would produce via
        /// <paramref name="emit"/>. Exposed as its own method so it can be
        /// unit-tested without touching VoxelChunk / GraphicsDevice.
        ///
        /// Returns the number of merged rectangles emitted (useful as a
        /// diagnostic counter — compare against non-null cell count to see
        /// the merge ratio).
        /// </summary>
        public static int GreedyMergeMask(
            FaceKey?[,] mask,
            Action<int, int, int, int, FaceKey> emit)
        {
            int width = mask.GetLength(0);
            int height = mask.GetLength(1);
            int rectangles = 0;

            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    var cell = mask[i, j];
                    if (cell == null) continue;
                    var key = cell.Value;

                    // Extend right.
                    int w = 1;
                    while (i + w < width)
                    {
                        var n = mask[i + w, j];
                        if (n == null || n.Value != key) break;
                        w++;
                    }

                    // Extend down, row by row, only while the whole row matches.
                    int h = 1;
                    while (j + h < height)
                    {
                        bool rowMatches = true;
                        for (int k = 0; k < w; k++)
                        {
                            var n = mask[i + k, j + h];
                            if (n == null || n.Value != key)
                            {
                                rowMatches = false;
                                break;
                            }
                        }
                        if (!rowMatches) break;
                        h++;
                    }

                    emit(i, j, w, h, key);
                    rectangles++;

                    // Clear covered cells so outer loops skip them.
                    for (int m = 0; m < h; m++)
                        for (int k = 0; k < w; k++)
                            mask[i + k, j + m] = null;
                }
            }

            return rectangles;
        }
    }
}
