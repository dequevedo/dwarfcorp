using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DwarfCorp.Voxels
{
    /// <summary>
    /// Fase B.2 live integration. Gated by <c>Debugger.Switches.UseGreedyMeshing</c>.
    ///
    /// For each slice of a chunk, we scan the voxels and — for every TOP face that's
    /// safely "uniform" (same voxel type, same atlas tile, fully explored, no grass
    /// fringe, no decal, no slope, no transition textures) — drop it into a 2D
    /// <see cref="GreedyMeshSlice.FaceKey"/> mask. Then <see cref="GreedyMeshSlice.GreedyMergeMask"/>
    /// scans the mask and emits rectangles of contiguous matching cells. Each rectangle
    /// becomes ONE merged quad instead of (w × h) per-voxel quads.
    ///
    /// Voxels whose top face is NOT mask-eligible (grass, decal, slope, fog border,
    /// transition tiles, side-only) fall back to the per-voxel path untouched — we
    /// only skip the TOP face for cells the mask consumed. Everything else (side
    /// faces, bottom faces, designations, decals) still runs per-voxel.
    ///
    /// ## Known limitation: stretched tile UVs
    /// The shader's <c>ClampTexture</c> clamps to atlas tile bounds, so a merged quad
    /// with UVs 0..1 stretches a single tile across the whole (w × h) rectangle rather
    /// than tiling it w times × h times. Visually this produces "big flat tiles" on
    /// merged floors. For DwarfCorp's low-poly pixelated aesthetic this is acceptable
    /// as an initial pass; swapping to genuine tiling is a shader-side follow-up
    /// (either change <c>ClampTexture</c> to wrap via <c>fmod</c>, or route merged
    /// quads through the already-present <c>WrappedTextureSampler</c>).
    /// </summary>
    public static partial class GeometryBuilder
    {
        // Per-slice scratch. GenerateSliceGeometry is called from CreateFromChunk in a
        // loop over localY, sequentially on the geometry-build thread, so a single
        // reused 2D mask is safe without any thread-local tricks. Kept at module scope
        // so consecutive slice builds don't re-allocate.
        [ThreadStatic] private static GreedyMeshSlice.FaceKey?[,] _maskScratch;
        [ThreadStatic] private static bool[,] _consumedTopScratch;

        /// <summary>
        /// Runs the greedy pass for this slice, filling <paramref name="ConsumedTopFace"/>
        /// with true for every (x, z) whose top face was emitted as part of a merged
        /// quad. Caller is expected to skip the TOP face for consumed cells in the
        /// subsequent per-voxel pass so we don't double-emit.
        /// </summary>
        private static void GenerateSliceGeometryGreedy(
            RawPrimitive Into,
            VoxelChunk Chunk,
            int LocalY,
            TerrainTileSheet TileSheet,
            WorldManager World,
            SliceCache Cache,
            bool[,] ConsumedTopFace)
        {
            var mask = _maskScratch ??= new GreedyMeshSlice.FaceKey?[VoxelConstants.ChunkSizeX, VoxelConstants.ChunkSizeZ];
            for (int x = 0; x < VoxelConstants.ChunkSizeX; x++)
                for (int z = 0; z < VoxelConstants.ChunkSizeZ; z++)
                    mask[x, z] = null;

            for (int x = 0; x < VoxelConstants.ChunkSizeX; x++)
                for (int z = 0; z < VoxelConstants.ChunkSizeZ; z++)
                {
                    var v = VoxelHandle.UnsafeCreateLocalHandle(Chunk, new LocalVoxelCoordinate(x, LocalY, z));
                    if (TryMakeGreedyTopFaceKey(v, World, out var key))
                        mask[x, z] = key;
                }

            int maskedCells = 0;
            for (int x = 0; x < VoxelConstants.ChunkSizeX; x++)
                for (int z = 0; z < VoxelConstants.ChunkSizeZ; z++)
                    if (mask[x, z].HasValue) maskedCells++;

            int rectangles = GreedyMeshSlice.GreedyMergeMask(mask, (i, j, w, h, key) =>
            {
                EmitMergedTopQuad(Into, Chunk, LocalY, i, j, w, h, key, TileSheet, World, Cache);
                for (int dx = 0; dx < w; dx++)
                    for (int dz = 0; dz < h; dz++)
                        ConsumedTopFace[i + dx, j + dz] = true;
            });

            // Accumulate session-wide stats so CSV captures the merge effectiveness.
            System.Threading.Interlocked.Add(ref PerfCounters.GreedyCellsMaskedThisFrame, maskedCells);
            System.Threading.Interlocked.Add(ref PerfCounters.GreedyRectanglesEmittedThisFrame, rectangles);
        }

        /// <summary>
        /// Allocation-free scratch for the per-slice "top face already merged" flag.
        /// Returned as-is; caller owns the mutations for the slice.
        /// </summary>
        private static bool[,] GetConsumedTopScratch()
        {
            var s = _consumedTopScratch ??= new bool[VoxelConstants.ChunkSizeX, VoxelConstants.ChunkSizeZ];
            for (int x = 0; x < VoxelConstants.ChunkSizeX; x++)
                for (int z = 0; z < VoxelConstants.ChunkSizeZ; z++)
                    s[x, z] = false;
            return s;
        }

        /// <summary>
        /// Eligibility check + key construction. Returns false if the voxel's top face
        /// shouldn't go through the greedy path for any reason; caller falls back to
        /// per-voxel emit. Reasons to reject:
        /// - Empty voxel (nothing to draw)
        /// - Not fully explored (fog-of-war per-vertex tint varies, can't merge safely)
        /// - Grass type set (fringe decals are per-voxel)
        /// - Decal type set (overlay is per-voxel)
        /// - Ramp (not a flat plane, breaks merge geometry)
        /// - Voxel type uses transition textures (atlas tile changes with neighbors)
        /// - Top face is culled (occluded by another voxel above)
        /// </summary>
        private static bool TryMakeGreedyTopFaceKey(VoxelHandle V, WorldManager World, out GreedyMeshSlice.FaceKey Key)
        {
            Key = default;
            if (V.IsEmpty) return false;
            if (!V.IsExplored) return false;
            if (V.GrassType != 0) return false;
            if (V.DecalType != 0) return false;
            if (V.RampType != RampType.None) return false;
            if (V.Type.HasTransitionTextures) return false;

            var templateSolid = TemplateSolidLibrary.GetTemplateSolid(V.Type.TemplateSolid);
            Geo.TemplateFace topFace = null;
            for (int i = 0; i < templateSolid.Faces.Count; i++)
                if (templateSolid.Faces[i].Orientation == FaceOrientation.Top)
                {
                    topFace = templateSolid.Faces[i];
                    break;
                }
            if (topFace == null) return false;

            if (topFace.CullType == Geo.FaceCullType.Cull && !IsFaceVisible(V, topFace, World.ChunkManager, out _))
                return false;

            var tile = SelectTile(V.Type, FaceOrientation.Top);

            // LightHash: approximate by the voxel's own sunlight bit. Anything more
            // granular (per-vertex lighting) requires a VertexLighting call per voxel
            // here, which doubles the cost and erases half the greedy win. The under-
            // merge on lighting seams is worth the savings; shadows are gentle in this
            // style.
            int lightHash = V.Sunlight ? 1 : 0;

            Key = new GreedyMeshSlice.FaceKey(
                voxelType: V.Type.ID,
                tileX: tile.X,
                tileY: tile.Y,
                exploredBits: 4, // all 4 vertices fully explored (gated by V.IsExplored above)
                lightHash: lightHash);
            return true;
        }

        /// <summary>
        /// Emit one merged quad covering the (w × h) rectangle of voxels starting at
        /// (<paramref name="I"/>, LocalY, <paramref name="J"/>). Four vertices at the
        /// rectangle corners, UVs 0..1 stretched across the tile, uniform lighting
        /// sampled from the anchor voxel. No per-vertex noise (a merged flat region
        /// wants to stay flat).
        /// </summary>
        private static void EmitMergedTopQuad(
            RawPrimitive Into,
            VoxelChunk Chunk,
            int LocalY,
            int I, int J, int W, int H,
            GreedyMeshSlice.FaceKey Key,
            TerrainTileSheet TileSheet,
            WorldManager World,
            SliceCache Cache)
        {
            var anchor = VoxelHandle.UnsafeCreateLocalHandle(Chunk, new LocalVoxelCoordinate(I, LocalY, J));
            var templateSolid = TemplateSolidLibrary.GetTemplateSolid(anchor.Type.TemplateSolid);
            Geo.TemplateFace topFace = null;
            for (int k = 0; k < templateSolid.Faces.Count; k++)
                if (templateSolid.Faces[k].Orientation == FaceOrientation.Top)
                {
                    topFace = templateSolid.Faces[k];
                    break;
                }

            var basePos = anchor.WorldPosition;
            var tile = new Point(Key.TileX, Key.TileY);
            var tileBounds = TileSheet.GetTileBounds(tile);

            // Corners in world space. Top face lies at y = basePos.Y + 1.
            // Vertex ordering follows the cube template (see TemplateSolid.MakeCube): the
            // top-face quad is (FrontTopLeft, BackTopLeft, FrontTopRight, BackTopRight),
            // which in world layout is (X=i, Z=j+h), (X=i, Z=j), (X=i+w, Z=j+h),
            // (X=i+w, Z=j). QuadIndicies = {0,1,2,3,0,2} form two CCW triangles.
            float y = basePos.Y + 1.0f;
            var p0 = new Vector3(basePos.X + 0, y, basePos.Z + H); // FrontTopLeft
            var p1 = new Vector3(basePos.X + 0, y, basePos.Z + 0); // BackTopLeft
            var p2 = new Vector3(basePos.X + W, y, basePos.Z + H); // FrontTopRight
            var p3 = new Vector3(basePos.X + W, y, basePos.Z + 0); // BackTopRight

            // Lighting: sample the anchor voxel's 4 top corners. Same key guarantees
            // the merge predicate held; using the anchor's lighting for all 4 corners
            // is deliberate (avoids per-voxel boundary seams inside a merged region).
            var l0 = VertexLighting.CalculateVertexLight(anchor, topFace.Mesh.Verticies[0].LogicalVertex, World.ChunkManager, Cache);
            var l1 = VertexLighting.CalculateVertexLight(anchor, topFace.Mesh.Verticies[1].LogicalVertex, World.ChunkManager, Cache);
            var l2 = VertexLighting.CalculateVertexLight(anchor, topFace.Mesh.Verticies[2].LogicalVertex, World.ChunkManager, Cache);
            var l3 = VertexLighting.CalculateVertexLight(anchor, topFace.Mesh.Verticies[3].LogicalVertex, World.ChunkManager, Cache);

            // UVs: each corner stretches the single tile across the rectangle. See the
            // class-level comment about the shader ClampTexture limitation.
            var uv0 = TileSheet.MapTileUVs(new Vector2(0, 1), tile);
            var uv1 = TileSheet.MapTileUVs(new Vector2(0, 0), tile);
            var uv2 = TileSheet.MapTileUVs(new Vector2(1, 1), tile);
            var uv3 = TileSheet.MapTileUVs(new Vector2(1, 0), tile);

            var white = new Color(1.0f, 1.0f, 1.0f, 1.0f);
            Cache.FaceGeometry[0] = new ExtendedVertex
            { Position = p0, TextureCoordinate = uv0, TextureBounds = tileBounds, VertColor = white, Color = l0.AsColor() };
            Cache.FaceGeometry[1] = new ExtendedVertex
            { Position = p1, TextureCoordinate = uv1, TextureBounds = tileBounds, VertColor = white, Color = l1.AsColor() };
            Cache.FaceGeometry[2] = new ExtendedVertex
            { Position = p2, TextureCoordinate = uv2, TextureBounds = tileBounds, VertColor = white, Color = l2.AsColor() };
            Cache.FaceGeometry[3] = new ExtendedVertex
            { Position = p3, TextureCoordinate = uv3, TextureBounds = tileBounds, VertColor = white, Color = l3.AsColor() };

            AddQuad(Into, Cache.FaceGeometry, QuadIndicies);
        }
    }
}
