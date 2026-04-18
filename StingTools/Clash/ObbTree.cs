// ObbTree.cs — per-element OBB-tree BVH for the narrow-phase descent.
// Build-time: O(n log n) top-down partition by largest-extent axis.
// Query: OBB/OBB overlap test (Gottschalk 1996) prunes before triangle-triangle SAT.
using System;
using System.Numerics;

namespace StingTools.Core.Clash
{
    public sealed class ObbTree
    {
        public ObbNode Root { get; }
        public ClashMeshBuffer Mesh { get; }

        private ObbTree(ObbNode root, ClashMeshBuffer mesh)
        {
            Root = root;
            Mesh = mesh;
        }

        public static ObbTree Build(ClashMeshBuffer mesh)
        {
            int triCount = mesh.TriangleCount;
            if (triCount == 0) return new ObbTree(null, mesh);
            var triIndices = new int[triCount];
            for (int i = 0; i < triCount; i++) triIndices[i] = i;
            var root = BuildRecursive(mesh, triIndices, 0, triCount, 0);
            return new ObbTree(root, mesh);
        }

        private const int LeafThreshold = 8;
        private const int MaxDepth = 24;

        private static ObbNode BuildRecursive(ClashMeshBuffer mesh, int[] tris, int start, int count, int depth)
        {
            var node = new ObbNode();
            ComputeAabbOverTriangles(mesh, tris, start, count, out node.AabbMin, out node.AabbMax);

            if (count <= LeafThreshold || depth >= MaxDepth)
            {
                node.IsLeaf = true;
                node.TriStart = start;
                node.TriCount = count;
                node.Tris = tris;
                return node;
            }

            var extent = node.AabbMax - node.AabbMin;
            int axis = 0;
            if (extent.Y > extent.X) axis = 1;
            if (extent.Z > (axis == 0 ? extent.X : extent.Y)) axis = 2;
            float split = 0.5f * (node.AabbMin[axis] + node.AabbMax[axis]);

            int lo = start, hi = start + count - 1;
            while (lo <= hi)
            {
                float c = TriCentroidComponent(mesh, tris[lo], axis);
                if (c < split) lo++;
                else
                {
                    int tmp = tris[lo]; tris[lo] = tris[hi]; tris[hi] = tmp; hi--;
                }
            }
            int leftCount = lo - start;
            if (leftCount == 0 || leftCount == count) leftCount = count / 2;

            node.IsLeaf = false;
            node.Left = BuildRecursive(mesh, tris, start, leftCount, depth + 1);
            node.Right = BuildRecursive(mesh, tris, start + leftCount, count - leftCount, depth + 1);
            return node;
        }

        private static void ComputeAabbOverTriangles(ClashMeshBuffer m, int[] tris, int start, int count, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);
            for (int i = 0; i < count; i++)
            {
                int t = tris[start + i];
                for (int v = 0; v < 3; v++)
                {
                    int vi = m.Indices[t * 3 + v];
                    float x = m.Vertices[vi * 3], y = m.Vertices[vi * 3 + 1], z = m.Vertices[vi * 3 + 2];
                    if (x < min.X) min.X = x; if (x > max.X) max.X = x;
                    if (y < min.Y) min.Y = y; if (y > max.Y) max.Y = y;
                    if (z < min.Z) min.Z = z; if (z > max.Z) max.Z = z;
                }
            }
        }

        private static float TriCentroidComponent(ClashMeshBuffer m, int tri, int axis)
        {
            int i0 = m.Indices[tri * 3];
            int i1 = m.Indices[tri * 3 + 1];
            int i2 = m.Indices[tri * 3 + 2];
            return (m.Vertices[i0 * 3 + axis] + m.Vertices[i1 * 3 + axis] + m.Vertices[i2 * 3 + axis]) / 3f;
        }
    }

    public sealed class ObbNode
    {
        public Vector3 AabbMin;
        public Vector3 AabbMax;
        public bool IsLeaf;
        public ObbNode Left;
        public ObbNode Right;
        public int[] Tris;
        public int TriStart;
        public int TriCount;
    }
}
