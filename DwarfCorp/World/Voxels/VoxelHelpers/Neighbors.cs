using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DwarfCorp
{
    public partial class VoxelHelpers
    {
        #region Offset Lists
        private static GlobalVoxelOffset[] ManhattanNeighbors = new GlobalVoxelOffset[]
        {
            new GlobalVoxelOffset(1,0,0),
            new GlobalVoxelOffset(-1,0,0),
            new GlobalVoxelOffset(0,1,0),
            new GlobalVoxelOffset(0,-1,0),
            new GlobalVoxelOffset(0,0,1),
            new GlobalVoxelOffset(0,0,-1)
        };

        private static GlobalVoxelOffset[] ManhattanCubeNeighbors = new GlobalVoxelOffset[]
        {
            new GlobalVoxelOffset(0,0,0),
            new GlobalVoxelOffset(1,0,0),
            new GlobalVoxelOffset(-1,0,0),
            new GlobalVoxelOffset(0,1,0),
            new GlobalVoxelOffset(0,-1,0),
            new GlobalVoxelOffset(0,0,1),
            new GlobalVoxelOffset(0,0,-1)
        };

        public static GlobalVoxelOffset[] ManhattanNeighbors2D = new GlobalVoxelOffset[]
        {
            new GlobalVoxelOffset(0,0,-1),
            new GlobalVoxelOffset(1,0,0),
            new GlobalVoxelOffset(0,0,1),
            new GlobalVoxelOffset(-1,0,0)
        };

        public static GlobalVoxelOffset[] DiagonalNeighbors2D = new GlobalVoxelOffset[]
        {
            new GlobalVoxelOffset(-1,0,-1),
            new GlobalVoxelOffset(1,0,-1),
            new GlobalVoxelOffset(1,0,1),
            new GlobalVoxelOffset(-1,0,1)
        };

        private static GlobalVoxelOffset[] AllNeighbors = new GlobalVoxelOffset[]
        {
            new GlobalVoxelOffset(-1,-1,-1),
            new GlobalVoxelOffset(-1,-1,0),
            new GlobalVoxelOffset(-1,-1,1),
            new GlobalVoxelOffset(-1,0,-1),
            new GlobalVoxelOffset(-1,0,0),
            new GlobalVoxelOffset(-1,0,1),
            new GlobalVoxelOffset(-1,1,-1),
            new GlobalVoxelOffset(-1,1,0),
            new GlobalVoxelOffset(-1,1,1),

            new GlobalVoxelOffset(0,-1,-1),
            new GlobalVoxelOffset(0,-1,0),
            new GlobalVoxelOffset(0,-1,1),
            new GlobalVoxelOffset(0,0,-1),
            //new GlobalVoxelOffset(0,0,0),
            new GlobalVoxelOffset(0,0,1),
            new GlobalVoxelOffset(0,1,-1),
            new GlobalVoxelOffset(0,1,0),
            new GlobalVoxelOffset(0,1,1),

            new GlobalVoxelOffset(1,-1,-1),
            new GlobalVoxelOffset(1,-1,0),
            new GlobalVoxelOffset(1,-1,1),
            new GlobalVoxelOffset(1,0,-1),
            new GlobalVoxelOffset(1,0,0),
            new GlobalVoxelOffset(1,0,1),
            new GlobalVoxelOffset(1,1,-1),
            new GlobalVoxelOffset(1,1,0),
            new GlobalVoxelOffset(1,1,1),
        };

        private static GlobalVoxelOffset[] Cube = new GlobalVoxelOffset[]
        {
            new GlobalVoxelOffset(-1,-1,-1),
            new GlobalVoxelOffset(-1,-1,0),
            new GlobalVoxelOffset(-1,-1,1),
            new GlobalVoxelOffset(-1,0,-1),
            new GlobalVoxelOffset(-1,0,0),
            new GlobalVoxelOffset(-1,0,1),
            new GlobalVoxelOffset(-1,1,-1),
            new GlobalVoxelOffset(-1,1,0),
            new GlobalVoxelOffset(-1,1,1),

            new GlobalVoxelOffset(0,-1,-1),
            new GlobalVoxelOffset(0,-1,0),
            new GlobalVoxelOffset(0,-1,1),
            new GlobalVoxelOffset(0,0,-1),
            new GlobalVoxelOffset(0,0,0),
            new GlobalVoxelOffset(0,0,1),
            new GlobalVoxelOffset(0,1,-1),
            new GlobalVoxelOffset(0,1,0),
            new GlobalVoxelOffset(0,1,1),

            new GlobalVoxelOffset(1,-1,-1),
            new GlobalVoxelOffset(1,-1,0),
            new GlobalVoxelOffset(1,-1,1),
            new GlobalVoxelOffset(1,0,-1),
            new GlobalVoxelOffset(1,0,0),
            new GlobalVoxelOffset(1,0,1),
            new GlobalVoxelOffset(1,1,-1),
            new GlobalVoxelOffset(1,1,0),
            new GlobalVoxelOffset(1,1,1),
        };

        #region VertexNeighbors
        public static readonly GlobalVoxelOffset[][] VertexNeighbors = new GlobalVoxelOffset[][]
        {
            // This MUST be in the same order as the VoxelVertex enum!

            new GlobalVoxelOffset[] // Back Bottom Left (-1, -1, -1)
            {
                new GlobalVoxelOffset(-1,-1,-1),
                new GlobalVoxelOffset(-1,-1,0),
                new GlobalVoxelOffset(-1,0,-1),
                new GlobalVoxelOffset(-1,0,0),
                new GlobalVoxelOffset(0,-1,-1),
                new GlobalVoxelOffset(0,-1,0),
                new GlobalVoxelOffset(0,0,-1),
                new GlobalVoxelOffset(0,0,0)
            },

            new GlobalVoxelOffset[] // Front Bottom Left (-1, -1, 1)
            {
                new GlobalVoxelOffset(-1,-1,0),
                new GlobalVoxelOffset(-1,-1,1),
                new GlobalVoxelOffset(-1,0,0),
                new GlobalVoxelOffset(-1,0,1),
                new GlobalVoxelOffset(0,-1,0),
                new GlobalVoxelOffset(0,-1,1),
                new GlobalVoxelOffset(0,0,0),
                new GlobalVoxelOffset(0,0,1)
            },

            new GlobalVoxelOffset[] // Back Top Left (-1, 1, -1)
            {
                new GlobalVoxelOffset(-1,0,-1),
                new GlobalVoxelOffset(-1,0,0),
                new GlobalVoxelOffset(-1,1,-1),
                new GlobalVoxelOffset(-1,1,0),
                new GlobalVoxelOffset(0,0,-1),
                new GlobalVoxelOffset(0,0,0),
                new GlobalVoxelOffset(0,1,-1),
                new GlobalVoxelOffset(0,1,0)
            },

            new GlobalVoxelOffset[] // Front Top Left (-1, 1, 1)
            {
                new GlobalVoxelOffset(-1,0,0),
                new GlobalVoxelOffset(-1,0,1),
                new GlobalVoxelOffset(-1,1,0),
                new GlobalVoxelOffset(-1,1,1),
                new GlobalVoxelOffset(0,0,0),
                new GlobalVoxelOffset(0,0,1),
                new GlobalVoxelOffset(0,1,0),
                new GlobalVoxelOffset(0,1,1)
            },

            new GlobalVoxelOffset[] // Back Bottom Right (1, -1, -1)
            {
                new GlobalVoxelOffset(0,-1,-1),
                new GlobalVoxelOffset(0,-1,0),
                new GlobalVoxelOffset(0,0,-1),
                new GlobalVoxelOffset(0,0,0),
                new GlobalVoxelOffset(1,-1,-1),
                new GlobalVoxelOffset(1,-1,0),
                new GlobalVoxelOffset(1,0,-1),
                new GlobalVoxelOffset(1,0,0)
            },

            new GlobalVoxelOffset[] // Front Bottom Right (1, -1, 1)
            {
                new GlobalVoxelOffset(0,-1,0),
                new GlobalVoxelOffset(0,-1,1),
                new GlobalVoxelOffset(0,0,0),
                new GlobalVoxelOffset(0,0,1),
                new GlobalVoxelOffset(1,-1,0),
                new GlobalVoxelOffset(1,-1,1),
                new GlobalVoxelOffset(1,0,0),
                new GlobalVoxelOffset(1,0,1)
            },

            new GlobalVoxelOffset[] // Back Top Right (1, 1, -1)
            {
                new GlobalVoxelOffset(0,0,-1),
                new GlobalVoxelOffset(0,0,0),
                new GlobalVoxelOffset(0,1,-1),
                new GlobalVoxelOffset(0,1,0),
                new GlobalVoxelOffset(1,0,-1),
                new GlobalVoxelOffset(1,0,0),
                new GlobalVoxelOffset(1,1,-1),
                new GlobalVoxelOffset(1,1,0)
            },

            new GlobalVoxelOffset[] // Front Top Right (1, 1, 1)
            {
                new GlobalVoxelOffset(0,0,0),
                new GlobalVoxelOffset(0,0,1),
                new GlobalVoxelOffset(0,1,0),
                new GlobalVoxelOffset(0,1,1),
                new GlobalVoxelOffset(1,0,0),
                new GlobalVoxelOffset(1,0,1),
                new GlobalVoxelOffset(1,1,0),
                new GlobalVoxelOffset(1,1,1)
            }
        };
        #endregion

        #region Vertex Neighbors 2D
        private static GlobalVoxelOffset[][] VertexNeighbors2D = new GlobalVoxelOffset[][]
        {
            // Back Bottom Left
            new GlobalVoxelOffset[] { },

            // Front Bottom Left
            new GlobalVoxelOffset[] { },

            // Back Top Left
            new GlobalVoxelOffset[]
            {
                new GlobalVoxelOffset(-1, 0, 0),
                new GlobalVoxelOffset(-1, 0, -1),
                new GlobalVoxelOffset(0, 0, -1),
                new GlobalVoxelOffset(0,0,0)
            },
            
            // Front Top Left
            new GlobalVoxelOffset[]
            {
                new GlobalVoxelOffset(-1, 0, 0),
                new GlobalVoxelOffset(-1, 0, 1),
                new GlobalVoxelOffset(0, 0, 1),
                new GlobalVoxelOffset(0,0,0)
            },
            
            // Back Bottom Right
            new GlobalVoxelOffset[] { },
            
            // Front Bottom Right
            new GlobalVoxelOffset[] { },
            
            // Back Top Right
            new GlobalVoxelOffset[]
            {
                new GlobalVoxelOffset(0, 0, -1),
                new GlobalVoxelOffset(1, 0, -1),
                new GlobalVoxelOffset(1, 0, 0),
                new GlobalVoxelOffset(0,0,0)
            },

            // Front Top Right
            new GlobalVoxelOffset[]
            {
                new GlobalVoxelOffset(0, 0, 1),
                new GlobalVoxelOffset(1, 0, 1),
                new GlobalVoxelOffset(1, 0, 0),
                new GlobalVoxelOffset(0,0,0)
            },
        };
        #endregion

        #endregion

        public static IEnumerable<GlobalVoxelCoordinate> EnumerateNeighbors(
            IEnumerable<GlobalVoxelOffset> Neighbors,
            GlobalVoxelCoordinate Coordinate)
        {
            foreach (var offset in Neighbors)
                yield return Coordinate + offset;
        }

        /// <summary>
        /// Zero-allocation enumerator for voxel neighbor arrays. The yield-return
        /// IEnumerable versions allocate an iterator state machine on every call; this
        /// struct avoids that for the chunk-mesh hot path (VertexLighting,
        /// face-exploration checks) where the cost adds up over thousands of voxels per
        /// chunk rebuild. Usable directly in foreach — `foreach (var c in NewStructEnumerator)`
        /// compiles to the struct's `MoveNext` / `Current` without IEnumerator boxing.
        /// </summary>
        public struct NeighborArrayEnumerator
        {
            private readonly GlobalVoxelOffset[] _offsets;
            private readonly GlobalVoxelCoordinate _origin;
            private int _index;

            internal NeighborArrayEnumerator(GlobalVoxelOffset[] offsets, GlobalVoxelCoordinate origin)
            {
                _offsets = offsets;
                _origin = origin;
                _index = -1;
            }

            public bool MoveNext()
            {
                _index++;
                return _offsets != null && _index < _offsets.Length;
            }

            public GlobalVoxelCoordinate Current
            {
                get
                {
                    var off = _offsets[_index];
                    return _origin + off;
                }
            }

            public NeighborArrayEnumerator GetEnumerator() => this;
        }

        public static NeighborArrayEnumerator EnumerateVertexNeighborsFast(
            GlobalVoxelCoordinate Coordinate, VoxelVertex Vertex)
        {
            return new NeighborArrayEnumerator(VertexNeighbors[(int)Vertex], Coordinate);
        }

        public static NeighborArrayEnumerator EnumerateVertexNeighbors2DFast(
            GlobalVoxelCoordinate Coordinate, VoxelVertex Vertex)
        {
            return new NeighborArrayEnumerator(VertexNeighbors2D[(int)Vertex], Coordinate);
        }

        public static NeighborArrayEnumerator EnumerateManhattanNeighborsFast(GlobalVoxelCoordinate Coordinate)
        {
            return new NeighborArrayEnumerator(ManhattanNeighbors, Coordinate);
        }

        // Fase C.3: struct-enumerator variant of EnumerateManhattanCube. Physics.cs
        // called the yield-return version every frame for every moving entity and then
        // chained a `.Select(...)` on top, allocating two iterator state machines per
        // entity per frame. This one returns a plain struct (zero-alloc).
        public static NeighborArrayEnumerator EnumerateManhattanCubeFast(GlobalVoxelCoordinate Coordinate)
        {
            return new NeighborArrayEnumerator(ManhattanCubeNeighbors, Coordinate);
        }

        public static IEnumerable<GlobalVoxelCoordinate> EnumerateManhattanNeighbors(
            GlobalVoxelCoordinate Coordinate)
        {
            return EnumerateNeighbors(ManhattanNeighbors, Coordinate);
        }

        public static IEnumerable<GlobalVoxelCoordinate> EnumerateManhattanNeighbors2D(
            GlobalVoxelCoordinate Coordinate)
        {
            return EnumerateNeighbors(ManhattanNeighbors2D, Coordinate);
        }

        public static IEnumerable<GlobalVoxelCoordinate> EnumerateManhattanNeighbors2D_Y(GlobalVoxelCoordinate Coordinate)
        {
            return EnumerateNeighbors(ManhattanNeighbors2D, Coordinate);
        }

        public static IEnumerable<GlobalVoxelCoordinate> EnumerateManhattanNeighbors2D_X(GlobalVoxelCoordinate Coordinate)
        {
            return EnumerateNeighbors(ManhattanNeighbors2D.Select(n => new GlobalVoxelOffset(0, -n.Z, n.X)), Coordinate);
        }

        public static IEnumerable<GlobalVoxelCoordinate> EnumerateManhattanNeighbors2D_Z(GlobalVoxelCoordinate Coordinate)
        {
            return EnumerateNeighbors(ManhattanNeighbors2D.Select(n => new GlobalVoxelOffset(n.X, -n.Z, 0)), Coordinate);
        }

        public static IEnumerable<GlobalVoxelCoordinate> EnumerateAllNeighbors(
            GlobalVoxelCoordinate Coordinate)
        {
            return EnumerateNeighbors(AllNeighbors, Coordinate);
        }

        public static IEnumerable<GlobalVoxelCoordinate> EnumerateCube(
            GlobalVoxelCoordinate Coordinate)
        {
            return EnumerateNeighbors(Cube, Coordinate);
        }

        public static IEnumerable<GlobalVoxelCoordinate> EnumerateManhattanCube(
           GlobalVoxelCoordinate Coordinate)
        {
            return EnumerateNeighbors(ManhattanCubeNeighbors, Coordinate);
        }

        public static IEnumerable<GlobalVoxelCoordinate> EnumerateVertexNeighbors(
            GlobalVoxelCoordinate Coordinate, VoxelVertex Vertex)
        {
            return EnumerateNeighbors(VertexNeighbors[(int)Vertex], Coordinate);
        }

        public static IEnumerable<GlobalVoxelCoordinate> EnumerateVertexNeighbors2D(
            GlobalVoxelCoordinate Coordinate, VoxelVertex Vertex)
        {
            return EnumerateNeighbors(VertexNeighbors2D[(int)Vertex], Coordinate);
        }

        public static VoxelHandle GetNeighbor(VoxelHandle Of, GlobalVoxelOffset Offset)
        {
            if (!Of.IsValid) return VoxelHandle.InvalidHandle;
            return new VoxelHandle(Of.Chunk.Manager, Of.Coordinate + Offset);
        }
    }
}
