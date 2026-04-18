// MollerSat.cs — Möller 1997 fast triangle-triangle intersection test.
// Public-domain reference: https://web.stanford.edu/class/cs277/resources/papers/Moller1997b.pdf
// Returns true if two triangles share any point in 3D. No intersection point computed.
using System;
using System.Numerics;

namespace StingTools.Core.Clash
{
    public static class MollerSat
    {
        private const float Epsilon = 1e-6f;

        public static bool TriTriOverlap(
            Vector3 a0, Vector3 a1, Vector3 a2,
            Vector3 b0, Vector3 b1, Vector3 b2)
        {
            // Plane of triangle A.
            var e1 = a1 - a0;
            var e2 = a2 - a0;
            var n1 = Vector3.Cross(e1, e2);
            float d1 = -Vector3.Dot(n1, a0);

            float db0 = Vector3.Dot(n1, b0) + d1;
            float db1 = Vector3.Dot(n1, b1) + d1;
            float db2 = Vector3.Dot(n1, b2) + d1;
            if (Math.Abs(db0) < Epsilon) db0 = 0;
            if (Math.Abs(db1) < Epsilon) db1 = 0;
            if (Math.Abs(db2) < Epsilon) db2 = 0;
            float db0db1 = db0 * db1, db0db2 = db0 * db2;
            if (db0db1 > 0 && db0db2 > 0) return false;   // B fully on one side of A

            // Plane of triangle B.
            var f1 = b1 - b0;
            var f2 = b2 - b0;
            var n2 = Vector3.Cross(f1, f2);
            float d2 = -Vector3.Dot(n2, b0);

            float da0 = Vector3.Dot(n2, a0) + d2;
            float da1 = Vector3.Dot(n2, a1) + d2;
            float da2 = Vector3.Dot(n2, a2) + d2;
            if (Math.Abs(da0) < Epsilon) da0 = 0;
            if (Math.Abs(da1) < Epsilon) da1 = 0;
            if (Math.Abs(da2) < Epsilon) da2 = 0;
            float da0da1 = da0 * da1, da0da2 = da0 * da2;
            if (da0da1 > 0 && da0da2 > 0) return false;   // A fully on one side of B

            // Direction of intersection line.
            var d = Vector3.Cross(n1, n2);
            float maxd = Math.Abs(d.X); int axis = 0;
            if (Math.Abs(d.Y) > maxd) { maxd = Math.Abs(d.Y); axis = 1; }
            if (Math.Abs(d.Z) > maxd) { axis = 2; }

            // Coplanar case: fall back to 2D polygon test on the dominant axis.
            if (maxd < Epsilon) return CoplanarTriTri(n1, a0, a1, a2, b0, b1, b2);

            float pa0 = GetAxis(a0, axis), pa1 = GetAxis(a1, axis), pa2 = GetAxis(a2, axis);
            float pb0 = GetAxis(b0, axis), pb1 = GetAxis(b1, axis), pb2 = GetAxis(b2, axis);

            float isect1_0, isect1_1;
            ComputeIntervals(pa0, pa1, pa2, da0, da1, da2, da0da1, da0da2, out isect1_0, out isect1_1);
            float isect2_0, isect2_1;
            ComputeIntervals(pb0, pb1, pb2, db0, db1, db2, db0db1, db0db2, out isect2_0, out isect2_1);

            if (isect1_0 > isect1_1) { float t = isect1_0; isect1_0 = isect1_1; isect1_1 = t; }
            if (isect2_0 > isect2_1) { float t = isect2_0; isect2_0 = isect2_1; isect2_1 = t; }

            return !(isect1_1 < isect2_0 || isect2_1 < isect1_0);
        }

        private static float GetAxis(Vector3 v, int axis)
        {
            return axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
        }

        private static void ComputeIntervals(
            float v0, float v1, float v2,
            float d0, float d1, float d2,
            float d0d1, float d0d2,
            out float isect0, out float isect1)
        {
            if (d0d1 > 0) { isect0 = v2 + (v0 - v2) * d2 / (d2 - d0); isect1 = v2 + (v1 - v2) * d2 / (d2 - d1); }
            else if (d0d2 > 0) { isect0 = v1 + (v0 - v1) * d1 / (d1 - d0); isect1 = v1 + (v2 - v1) * d1 / (d1 - d2); }
            else if (d1 * d2 > 0 || d0 != 0) { isect0 = v0 + (v1 - v0) * d0 / (d0 - d1); isect1 = v0 + (v2 - v0) * d0 / (d0 - d2); }
            else if (d1 != 0) { isect0 = v1 + (v0 - v1) * d1 / (d1 - d0); isect1 = v1 + (v2 - v1) * d1 / (d1 - d2); }
            else if (d2 != 0) { isect0 = v2 + (v0 - v2) * d2 / (d2 - d0); isect1 = v2 + (v1 - v2) * d2 / (d2 - d1); }
            else { isect0 = 0; isect1 = 0; }
        }

        private static bool CoplanarTriTri(Vector3 n, Vector3 a0, Vector3 a1, Vector3 a2, Vector3 b0, Vector3 b1, Vector3 b2)
        {
            // Project to 2D by dropping the dominant-normal axis, then do edge-edge and point-in-triangle tests.
            float ax = Math.Abs(n.X), ay = Math.Abs(n.Y), az = Math.Abs(n.Z);
            int i0, i1;
            if (ax > ay) { if (ax > az) { i0 = 1; i1 = 2; } else { i0 = 0; i1 = 1; } }
            else { if (az > ay) { i0 = 0; i1 = 1; } else { i0 = 0; i1 = 2; } }
            Vector2 A0 = Proj(a0, i0, i1), A1 = Proj(a1, i0, i1), A2 = Proj(a2, i0, i1);
            Vector2 B0 = Proj(b0, i0, i1), B1 = Proj(b1, i0, i1), B2 = Proj(b2, i0, i1);
            return EdgeTest(A0, A1, B0, B1, B2) || EdgeTest(A1, A2, B0, B1, B2) || EdgeTest(A2, A0, B0, B1, B2)
                || PointInTri(A0, B0, B1, B2) || PointInTri(B0, A0, A1, A2);
        }

        private static Vector2 Proj(Vector3 v, int i0, int i1) =>
            new Vector2(i0 == 0 ? v.X : i0 == 1 ? v.Y : v.Z, i1 == 0 ? v.X : i1 == 1 ? v.Y : v.Z);

        private static bool EdgeTest(Vector2 p, Vector2 q, Vector2 a, Vector2 b, Vector2 c) =>
            SegSeg(p, q, a, b) || SegSeg(p, q, b, c) || SegSeg(p, q, c, a);

        private static bool SegSeg(Vector2 p, Vector2 q, Vector2 a, Vector2 b)
        {
            float d1 = Cross2(b - a, p - a), d2 = Cross2(b - a, q - a);
            float d3 = Cross2(q - p, a - p), d4 = Cross2(q - p, b - p);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        private static float Cross2(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;

        private static bool PointInTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross2(b - a, p - a), d2 = Cross2(c - b, p - b), d3 = Cross2(a - c, p - c);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0;
            bool pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }
}
