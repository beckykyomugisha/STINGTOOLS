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

        // E3: True OBB axes derived from PCA on the node's triangles. Computed
        //      lazily on first access — most nodes never need them because the
        //      AABB-overlap prune kills them first. Sites that DO get past the
        //      AABB test (elongated geometry like ducts, beams) save downstream
        //      triangle-triangle SAT calls by rejecting on OBB-OBB SAT.
        // Centre point in world space.
        public Vector3 ObbCentre;
        // Three orthonormal axes (right-handed). Length-3 (slot 0 = primary).
        public Vector3[] ObbAxes;
        // Half-extents along each axis.
        public Vector3 ObbHalfExtents;
        public bool ObbBuilt;

        /// <summary>
        /// E3: PCA-derived OBB. Computes centre + axes + half-extents from the
        /// triangle vertex set under this node. Call exactly once per node;
        /// callers should check ObbBuilt before reading ObbAxes.
        /// </summary>
        public void EnsureObb(ClashMeshBuffer mesh)
        {
            if (ObbBuilt) return;
            // Walk every vertex of every triangle in this node to compute the
            // 3x3 covariance matrix. PCA yields the OBB axes as eigenvectors.
            // For deeply-nested nodes triangle counts are small (<= LeafThreshold
            // for leaves; 2x for the parent etc.), so this is ~50-200 vertex reads.
            int nVerts = TriCount * 3;
            if (nVerts < 3 || Tris == null)
            {
                // Fallback to AABB axes.
                ObbCentre = 0.5f * (AabbMin + AabbMax);
                ObbAxes = new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
                ObbHalfExtents = 0.5f * (AabbMax - AabbMin);
                ObbBuilt = true;
                return;
            }
            // Mean.
            Vector3 mean = Vector3.Zero;
            for (int i = 0; i < TriCount; i++)
            {
                int t = Tris[TriStart + i];
                for (int v = 0; v < 3; v++)
                {
                    int vi = mesh.Indices[t * 3 + v];
                    mean.X += mesh.Vertices[vi * 3];
                    mean.Y += mesh.Vertices[vi * 3 + 1];
                    mean.Z += mesh.Vertices[vi * 3 + 2];
                }
            }
            mean /= nVerts;

            // Covariance.
            float xx = 0, yy = 0, zz = 0, xy = 0, xz = 0, yz = 0;
            for (int i = 0; i < TriCount; i++)
            {
                int t = Tris[TriStart + i];
                for (int v = 0; v < 3; v++)
                {
                    int vi = mesh.Indices[t * 3 + v];
                    float dx = mesh.Vertices[vi * 3] - mean.X;
                    float dy = mesh.Vertices[vi * 3 + 1] - mean.Y;
                    float dz = mesh.Vertices[vi * 3 + 2] - mean.Z;
                    xx += dx * dx; yy += dy * dy; zz += dz * dz;
                    xy += dx * dy; xz += dx * dz; yz += dy * dz;
                }
            }
            xx /= nVerts; yy /= nVerts; zz /= nVerts;
            xy /= nVerts; xz /= nVerts; yz /= nVerts;

            // Power iteration for the dominant eigenvector — cheap and good
            // enough for OBB selection. Two iterations converge for typical
            // box-like geometry.
            Vector3 e0 = new Vector3(1, 0.5f, 0.25f);
            for (int it = 0; it < 8; it++)
            {
                e0 = new Vector3(
                    xx * e0.X + xy * e0.Y + xz * e0.Z,
                    xy * e0.X + yy * e0.Y + yz * e0.Z,
                    xz * e0.X + yz * e0.Y + zz * e0.Z);
                float len = e0.Length();
                if (len < 1e-9f) { e0 = Vector3.UnitX; break; }
                e0 /= len;
            }
            // Build orthonormal frame from e0.
            Vector3 helper = Math.Abs(e0.X) > 0.9f ? Vector3.UnitY : Vector3.UnitX;
            Vector3 e1 = Vector3.Normalize(Vector3.Cross(e0, helper));
            Vector3 e2 = Vector3.Cross(e0, e1);

            // Project all vertices to find min/max along each axis → half extents.
            float min0 = float.MaxValue, max0 = float.MinValue;
            float min1 = float.MaxValue, max1 = float.MinValue;
            float min2 = float.MaxValue, max2 = float.MinValue;
            for (int i = 0; i < TriCount; i++)
            {
                int t = Tris[TriStart + i];
                for (int v = 0; v < 3; v++)
                {
                    int vi = mesh.Indices[t * 3 + v];
                    var p = new Vector3(mesh.Vertices[vi * 3], mesh.Vertices[vi * 3 + 1], mesh.Vertices[vi * 3 + 2]);
                    var d = p - mean;
                    float p0 = Vector3.Dot(d, e0);
                    float p1 = Vector3.Dot(d, e1);
                    float p2 = Vector3.Dot(d, e2);
                    if (p0 < min0) min0 = p0; if (p0 > max0) max0 = p0;
                    if (p1 < min1) min1 = p1; if (p1 > max1) max1 = p1;
                    if (p2 < min2) min2 = p2; if (p2 > max2) max2 = p2;
                }
            }
            // Centre is the midpoint of the projected bounds, transformed back to world.
            float c0 = 0.5f * (min0 + max0);
            float c1 = 0.5f * (min1 + max1);
            float c2 = 0.5f * (min2 + max2);
            ObbCentre = mean + c0 * e0 + c1 * e1 + c2 * e2;
            ObbAxes = new[] { e0, e1, e2 };
            ObbHalfExtents = new Vector3(
                0.5f * (max0 - min0),
                0.5f * (max1 - min1),
                0.5f * (max2 - min2));
            ObbBuilt = true;
        }
    }

    /// <summary>
    /// E3: OBB-vs-OBB separating-axis test (Gottschalk 1996). Used as an extra
    /// prune AFTER the AABB-overlap test passes — for elongated geometry the
    /// AABB is conservative and many AABB-overlap pairs have no actual OBB
    /// overlap. SAT walks the 15 candidate separating axes (3 axes of each
    /// box + 9 cross-product axes); if any axis separates the projections,
    /// the boxes are disjoint.
    /// </summary>
    internal static class ObbSat
    {
        // Epsilon to handle parallel-axis edge case where cross product
        // collapses to zero — skip those axes (they are redundant with the
        // primary 6).
        private const float Eps = 1e-6f;

        public static bool Overlap(ObbNode a, ObbNode b, ClashMeshBuffer meshA, ClashMeshBuffer meshB)
        {
            a.EnsureObb(meshA);
            b.EnsureObb(meshB);
            if (a.ObbAxes == null || b.ObbAxes == null) return true;   // safety: assume overlap

            Vector3 t = b.ObbCentre - a.ObbCentre;
            // Build R[i,j] = a.axes[i] · b.axes[j], plus absolute value with epsilon.
            Span<float> R  = stackalloc float[9];
            Span<float> Ra = stackalloc float[9];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    float dot = Vector3.Dot(a.ObbAxes[i], b.ObbAxes[j]);
                    R[i * 3 + j] = dot;
                    Ra[i * 3 + j] = Math.Abs(dot) + Eps;
                }
            // Project t onto a's axes.
            Span<float> tA = stackalloc float[3];
            for (int i = 0; i < 3; i++) tA[i] = Vector3.Dot(t, a.ObbAxes[i]);

            float aE0 = a.ObbHalfExtents.X, aE1 = a.ObbHalfExtents.Y, aE2 = a.ObbHalfExtents.Z;
            float bE0 = b.ObbHalfExtents.X, bE1 = b.ObbHalfExtents.Y, bE2 = b.ObbHalfExtents.Z;
            float[] aE = { aE0, aE1, aE2 };
            float[] bE = { bE0, bE1, bE2 };

            // 3 × axes of A.
            for (int i = 0; i < 3; i++)
            {
                float ra = aE[i];
                float rb = bE[0] * Ra[i * 3 + 0] + bE[1] * Ra[i * 3 + 1] + bE[2] * Ra[i * 3 + 2];
                if (Math.Abs(tA[i]) > ra + rb) return false;
            }
            // 3 × axes of B.
            for (int j = 0; j < 3; j++)
            {
                float ra = aE[0] * Ra[0 * 3 + j] + aE[1] * Ra[1 * 3 + j] + aE[2] * Ra[2 * 3 + j];
                float rb = bE[j];
                float tProj = tA[0] * R[0 * 3 + j] + tA[1] * R[1 * 3 + j] + tA[2] * R[2 * 3 + j];
                if (Math.Abs(tProj) > ra + rb) return false;
            }
            // 9 × cross axes a[i] × b[j]. Standard SAT cross-axis tests.
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    int i1 = (i + 1) % 3, i2 = (i + 2) % 3;
                    int j1 = (j + 1) % 3, j2 = (j + 2) % 3;
                    float ra = aE[i1] * Ra[i2 * 3 + j] + aE[i2] * Ra[i1 * 3 + j];
                    float rb = bE[j1] * Ra[i * 3 + j2] + bE[j2] * Ra[i * 3 + j1];
                    float tProj = tA[i2] * R[i1 * 3 + j] - tA[i1] * R[i2 * 3 + j];
                    if (Math.Abs(tProj) > ra + rb) return false;
                }
            }
            return true;
        }
    }
}
