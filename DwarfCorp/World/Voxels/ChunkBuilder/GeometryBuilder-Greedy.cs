using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DwarfCorp.Voxels
{
    /// <summary>
    /// Fase B.2 live integration. Gated by <c>Debugger.Switches.UseGreedyMeshing</c>.
    ///
    /// For each horizontal plane that a chunk slice exposes (the TOP face of every voxel
    /// at Y, and the BOTTOM face of every voxel at Y) we scan the slice, build a 2D
    /// <see cref="GreedyMeshSlice.FaceKey"/> mask over (X, Z), and run
    /// <see cref="GreedyMeshSlice.GreedyMergeMask"/> to collapse contiguous matching
    /// cells into a single merged quad. The per-voxel pass that follows skips the
    /// faces already consumed by the greedy pass — tracked via a per-slice byte-mask
    /// with one bit per <see cref="FaceOrientation"/>.
    ///
    /// ### Scope limitation (side faces deferred)
    /// Top and Bottom live cleanly inside the per-slice architecture: both lie on the
    /// XZ plane at a fixed Y and a slice sees every cell of the plane. Side faces
    /// (North/South on Z, East/West on X) sit on planes that extend across multiple
    /// Y slices — to greedy-merge them in 2D we'd need a cross-slice pass, which is a
    /// bigger architectural move. The 1D-per-slice variant (horizontal runs only) is
    /// possible here but the ROI is much smaller than Top/Bottom 2D, so it's parked
    /// until the payoff of Top+Bottom is measured.
    ///
    /// ### Eligibility (any cell rejected falls back to per-voxel emit)
    /// - Empty or not fully explored (fog-of-war per-vertex tint varies).
    /// - Grass type set (fringe decals are per-voxel).
    /// - Decal type set (overlay is per-voxel).
    /// - Ramp (breaks the flat-plane assumption).
    /// - Voxel type uses transition textures (atlas tile changes with neighbors).
    /// - Face is culled (occluded by adjacent voxel).
    ///
    /// ### Known limitation: stretched tile UVs
    /// <c>TexturedShaders.fx :: ClampTexture</c> clamps UVs to atlas tile bounds, so
    /// a merged quad with UVs 0..1 stretches a single tile across the whole rectangle
    /// rather than tiling it <c>w × h</c> times. Acceptable for DwarfCorp's low-poly
    /// pixelated look as an initial pass; swapping to real tiling is TODO 33 in
    /// <c>TODO_LIST.md</c> (shader <c>fmod</c> wrap or route merged quads through the
    /// <c>WrappedTextureSampler</c> already present).
    /// </summary>
    public static partial class GeometryBuilder
    {
        // Face bit flags — one bit per FaceOrientation value (Top=0, Bottom=1, etc.).
        // Used in the per-slice `consumedFaces` byte-mask so the per-voxel fallback
        // pass can skip each face that the greedy pass already emitted without
        // needing a separate bool[,] per face.
        private const byte FaceBit_Top    = 1 << (int)FaceOrientation.Top;
        private const byte FaceBit_Bottom = 1 << (int)FaceOrientation.Bottom;

        // Per-thread scratch. `CreateFromChunk` runs on the parallel chunk-rebuild
        // workers, so these are ThreadStatic. Reused across slices of the same chunk
        // and across chunks on the same worker — the clear loop below (O(ChunkSizeX *
        // ChunkSizeZ)) is trivial compared to the meshgen itself.
        [ThreadStatic] private static GreedyMeshSlice.FaceKey?[,] _maskScratch;
        [ThreadStatic] private static byte[,] _consumedFacesScratch;

        /// <summary>Scratch for the per-slice "face already emitted" bitmask.
        /// Bit at <c>1 &lt;&lt; (int)FaceOrientation</c> is set when the greedy
        /// pass for that orientation emitted a merged quad covering this (x, z)
        /// cell. Caller owns the mutations for the slice.</summary>
        private static byte[,] GetConsumedFacesScratch()
        {
            var s = _consumedFacesScratch ??= new byte[VoxelConstants.ChunkSizeX, VoxelConstants.ChunkSizeZ];
            for (int x = 0; x < VoxelConstants.ChunkSizeX; x++)
                for (int z = 0; z < VoxelConstants.ChunkSizeZ; z++)
                    s[x, z] = 0;
            return s;
        }

        /// <summary>
        /// Runs greedy passes for every face orientation that supports 2D merging on
        /// a horizontal slice (currently Top and Bottom). Marks consumed cells in
        /// <paramref name="ConsumedFaces"/>. The per-voxel pass that runs afterward
        /// should skip each face whose bit is set.
        /// </summary>
        private static void GenerateSliceGeometryGreedy(
            RawPrimitive Into,
            VoxelChunk Chunk,
            int LocalY,
            TerrainTileSheet TileSheet,
            WorldManager World,
            SliceCache Cache,
            byte[,] ConsumedFaces)
        {
            RunGreedyPass(FaceOrientation.Top, FaceBit_Top, Into, Chunk, LocalY, TileSheet, World, Cache, ConsumedFaces);
            RunGreedyPass(FaceOrientation.Bottom, FaceBit_Bottom, Into, Chunk, LocalY, TileSheet, World, Cache, ConsumedFaces);
        }

        /// <summary>
        /// One pass of the greedy merge for a single face orientation on the current
        /// horizontal slice. Builds the FaceKey mask, runs <see cref="GreedyMeshSlice.GreedyMergeMask"/>,
        /// emits each merged rectangle as one quad, and ORs the face bit into the
        /// consumed-faces array for every covered cell.
        /// </summary>
        private static void RunGreedyPass(
            FaceOrientation Orientation,
            byte FaceBit,
            RawPrimitive Into,
            VoxelChunk Chunk,
            int LocalY,
            TerrainTileSheet TileSheet,
            WorldManager World,
            SliceCache Cache,
            byte[,] ConsumedFaces)
        {
            var mask = _maskScratch ??= new GreedyMeshSlice.FaceKey?[VoxelConstants.ChunkSizeX, VoxelConstants.ChunkSizeZ];
            for (int x = 0; x < VoxelConstants.ChunkSizeX; x++)
                for (int z = 0; z < VoxelConstants.ChunkSizeZ; z++)
                    mask[x, z] = null;

            for (int x = 0; x < VoxelConstants.ChunkSizeX; x++)
                for (int z = 0; z < VoxelConstants.ChunkSizeZ; z++)
                {
                    var v = VoxelHandle.UnsafeCreateLocalHandle(Chunk, new LocalVoxelCoordinate(x, LocalY, z));
                    if (TryMakeGreedyFaceKey(v, Orientation, World, out var key))
                        mask[x, z] = key;
                }

            int maskedCells = 0;
            for (int x = 0; x < VoxelConstants.ChunkSizeX; x++)
                for (int z = 0; z < VoxelConstants.ChunkSizeZ; z++)
                    if (mask[x, z].HasValue) maskedCells++;

            int rectangles = GreedyMeshSlice.GreedyMergeMask(mask, (i, j, w, h, key) =>
            {
                EmitMergedHorizontalQuad(Orientation, Into, Chunk, LocalY, i, j, w, h, key, TileSheet, World, Cache);
                for (int dx = 0; dx < w; dx++)
                    for (int dz = 0; dz < h; dz++)
                        ConsumedFaces[i + dx, j + dz] |= FaceBit;
            });

            System.Threading.Interlocked.Add(ref PerfCounters.GreedyCellsMaskedThisFrame, maskedCells);
            System.Threading.Interlocked.Add(ref PerfCounters.GreedyRectanglesEmittedThisFrame, rectangles);
        }

        /// <summary>
        /// Eligibility check + key construction for a given face orientation. Returns
        /// false and the caller falls back to the per-voxel emit path. See the class
        /// doc-comment for the exclusion list.
        /// </summary>
        private static bool TryMakeGreedyFaceKey(VoxelHandle V, FaceOrientation Orientation, WorldManager World, out GreedyMeshSlice.FaceKey Key)
        {
            Key = default;
            if (V.IsEmpty) return false;
            if (!V.IsExplored) return false;
            if (V.GrassType != 0) return false;
            if (V.DecalType != 0) return false;
            if (V.RampType != RampType.None) return false;
            if (V.Type.HasTransitionTextures) return false;

            var templateSolid = TemplateSolidLibrary.GetTemplateSolid(V.Type.TemplateSolid);
            Geo.TemplateFace face = null;
            for (int i = 0; i < templateSolid.Faces.Count; i++)
                if (templateSolid.Faces[i].Orientation == Orientation)
                {
                    face = templateSolid.Faces[i];
                    break;
                }
            if (face == null) return false;

            if (face.CullType == Geo.FaceCullType.Cull && !IsFaceVisible(V, face, World.ChunkManager, out _))
                return false;

            var tile = SelectTile(V.Type, Orientation);

            // LightHash: approximate by the voxel's own sunlight bit. Anything more
            // granular (per-vertex lighting) requires a VertexLighting call per voxel
            // here, which doubles the cost and erases half the greedy win. The
            // under-merge on lighting seams is worth the savings; shadows are gentle
            // in this style.
            int lightHash = V.Sunlight ? 1 : 0;

            Key = new GreedyMeshSlice.FaceKey(
                voxelType: V.Type.ID,
                tileX: tile.X,
                tileY: tile.Y,
                exploredBits: 4,
                lightHash: lightHash);
            return true;
        }

        /// <summary>
        /// Emit one merged quad covering the (w × h) rectangle of voxels starting at
        /// (<paramref name="I"/>, LocalY, <paramref name="J"/>). Dispatches to the
        /// correct vertex layout based on orientation (Top or Bottom).
        /// </summary>
        private static void EmitMergedHorizontalQuad(
            FaceOrientation Orientation,
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
            Geo.TemplateFace face = null;
            for (int k = 0; k < templateSolid.Faces.Count; k++)
                if (templateSolid.Faces[k].Orientation == Orientation)
                {
                    face = templateSolid.Faces[k];
                    break;
                }

            var basePos = anchor.WorldPosition;
            var tile = new Point(Key.TileX, Key.TileY);
            var tileBounds = TileSheet.GetTileBounds(tile);

            // Vertex slots must match the ordering that TemplateMesh.QuadPart writes
            // into Verticies[0..3]: slot 0 = "topLeft" param, slot 1 = "topRight",
            // slot 2 = "bottomRight", slot 3 = "bottomLeft".
            //
            // For the TOP face the template passes (FrontTopLeft, BackTopLeft,
            // FrontTopRight, BackTopRight) as (bottomLeft, topLeft, bottomRight,
            // topRight), so the stored slots are BackTopLeft, BackTopRight,
            // FrontTopRight, FrontTopLeft — that's the layout we reproduce for the
            // merged quad.
            //
            // For the BOTTOM face the template passes (BackBottomRight,
            // FrontBottomRight, BackBottomLeft, FrontBottomLeft), so the stored
            // slots are FrontBottomRight, FrontBottomLeft, BackBottomLeft,
            // BackBottomRight.
            //
            // QuadIndicies = {0,1,2, 3,0,2} then forms two triangles sharing the
            // [0]-[2] diagonal that cover the entire quad — works for either
            // winding since terrain renders with CullMode.None.
            Vector3 p0, p1, p2, p3;
            if (Orientation == FaceOrientation.Top)
            {
                float y = basePos.Y + 1.0f;
                p0 = new Vector3(basePos.X + 0, y, basePos.Z + 0); // BackTopLeft
                p1 = new Vector3(basePos.X + W, y, basePos.Z + 0); // BackTopRight
                p2 = new Vector3(basePos.X + W, y, basePos.Z + H); // FrontTopRight
                p3 = new Vector3(basePos.X + 0, y, basePos.Z + H); // FrontTopLeft
            }
            else // Bottom
            {
                float y = basePos.Y;
                p0 = new Vector3(basePos.X + W, y, basePos.Z + H); // FrontBottomRight
                p1 = new Vector3(basePos.X + 0, y, basePos.Z + H); // FrontBottomLeft
                p2 = new Vector3(basePos.X + 0, y, basePos.Z + 0); // BackBottomLeft
                p3 = new Vector3(basePos.X + W, y, basePos.Z + 0); // BackBottomRight
            }

            // Lighting: sample anchor's 4 face corners. Same FaceKey across the
            // merged region guarantees the merge predicate held — using the anchor's
            // lighting for every merged corner avoids per-voxel seams inside the
            // merged region. Verticies[i].LogicalVertex is already in QuadPart-
            // reordered slots, so these line up 1:1 with p0..p3 above.
            var l0 = VertexLighting.CalculateVertexLight(anchor, face.Mesh.Verticies[0].LogicalVertex, World.ChunkManager, Cache);
            var l1 = VertexLighting.CalculateVertexLight(anchor, face.Mesh.Verticies[1].LogicalVertex, World.ChunkManager, Cache);
            var l2 = VertexLighting.CalculateVertexLight(anchor, face.Mesh.Verticies[2].LogicalVertex, World.ChunkManager, Cache);
            var l3 = VertexLighting.CalculateVertexLight(anchor, face.Mesh.Verticies[3].LogicalVertex, World.ChunkManager, Cache);

            // UVs follow QuadPart's pattern: [0]=(0,0), [1]=(1,0), [2]=(1,1), [3]=(0,1).
            // Stretched across the merged rect — see class-header note about the
            // shader ClampTexture limitation.
            var uv0 = TileSheet.MapTileUVs(new Vector2(0, 0), tile);
            var uv1 = TileSheet.MapTileUVs(new Vector2(1, 0), tile);
            var uv2 = TileSheet.MapTileUVs(new Vector2(1, 1), tile);
            var uv3 = TileSheet.MapTileUVs(new Vector2(0, 1), tile);

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
