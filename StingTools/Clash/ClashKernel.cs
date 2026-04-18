// ClashKernel.cs — orchestrator. Given extracted meshes, run full clash detection.
// Output is a list of raw hits (ClashHit) that downstream stages filter, group,
// persist, and surface.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    public sealed class ClashHit
    {
        public ClashElementKey A;
        public ClashElementKey B;
        public Vector3 Centroid;
        public Vector3 AabbMin;
        public Vector3 AabbMax;
        public float VolumeMm3;
        public string Kind;            // "hard", "clearance"
        public string FailureMode;     // "", "geometry_error"
    }

    public sealed class ClashKernel
    {
        public Dictionary<ClashElementKey, ObbTree> ObbTrees { get; } = new Dictionary<ClashElementKey, ObbTree>();
        public AabbSweep Sweep { get; } = new AabbSweep();
        public int HardClashCount { get; private set; }
        public long BroadMs { get; private set; }
        public long NarrowMs { get; private set; }

        public void BuildIndexes(IEnumerable<ClashMeshBuffer> meshes)
        {
            var list = meshes.ToList();
            Sweep.Build(list);
            Parallel.ForEach(list, m =>
            {
                var tree = ObbTree.Build(m);
                lock (ObbTrees) ObbTrees[m.Key] = tree;
            });
        }

        public List<ClashHit> Run()
        {
            var sw = Stopwatch.StartNew();
            var pairs = Sweep.CandidatePairs().ToList();
            BroadMs = sw.ElapsedMilliseconds;
            sw.Restart();

            var hits = new ConcurrentBag<ClashHit>();
            Parallel.ForEach(pairs, pair =>
            {
                try
                {
                    var hit = TestPair(pair.A, pair.B);
                    if (hit != null) hits.Add(hit);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"clash TestPair failed {pair.A.Key}x{pair.B.Key}: {ex.Message}");
                }
            });
            NarrowMs = sw.ElapsedMilliseconds;

            var result = hits.ToList();
            HardClashCount = result.Count;
            StingLog.Info($"ClashKernel: {pairs.Count} pairs, {HardClashCount} hits, broad={BroadMs}ms narrow={NarrowMs}ms");
            return result;
        }

        private ClashHit TestPair(ClashMeshBuffer a, ClashMeshBuffer b)
        {
            if (a.TriangleCount == 0 || b.TriangleCount == 0) return null;
            // AABB overlap (fast reject).
            if (a.MaxX < b.MinX || a.MinX > b.MaxX) return null;
            if (a.MaxY < b.MinY || a.MinY > b.MaxY) return null;
            if (a.MaxZ < b.MinZ || a.MinZ > b.MaxZ) return null;

            var aabbMin = new Vector3(Math.Max(a.MinX, b.MinX), Math.Max(a.MinY, b.MinY), Math.Max(a.MinZ, b.MinZ));
            var aabbMax = new Vector3(Math.Min(a.MaxX, b.MaxX), Math.Min(a.MaxY, b.MaxY), Math.Min(a.MaxZ, b.MaxZ));

            // Brute triangle-triangle test inside overlap region — for Stage 1.
            // Stage 1.4+ will descend OBB-trees instead for speed, but brute is correct.
            int hitCount = 0;
            for (int ia = 0; ia < a.TriangleCount && hitCount < 1; ia++)
            {
                var va0 = GetVertex(a, ia, 0); var va1 = GetVertex(a, ia, 1); var va2 = GetVertex(a, ia, 2);
                if (!TriInAabb(va0, va1, va2, aabbMin, aabbMax)) continue;
                for (int ib = 0; ib < b.TriangleCount; ib++)
                {
                    var vb0 = GetVertex(b, ib, 0); var vb1 = GetVertex(b, ib, 1); var vb2 = GetVertex(b, ib, 2);
                    if (!TriInAabb(vb0, vb1, vb2, aabbMin, aabbMax)) continue;
                    if (MollerSat.TriTriOverlap(va0, va1, va2, vb0, vb1, vb2))
                    {
                        hitCount++;
                        break;
                    }
                }
            }
            if (hitCount == 0) return null;

            var centroid = 0.5f * (aabbMin + aabbMax);
            var volExtent = aabbMax - aabbMin;
            float volFt3 = Math.Max(0f, volExtent.X) * Math.Max(0f, volExtent.Y) * Math.Max(0f, volExtent.Z);
            float volMm3 = volFt3 * 28316846.592f;   // ft^3 → mm^3
            return new ClashHit
            {
                A = a.Key, B = b.Key,
                Centroid = centroid,
                AabbMin = aabbMin, AabbMax = aabbMax,
                VolumeMm3 = volMm3, Kind = "hard", FailureMode = ""
            };
        }

        private static Vector3 GetVertex(ClashMeshBuffer m, int tri, int corner)
        {
            int vi = m.Indices[tri * 3 + corner];
            return new Vector3(m.Vertices[vi * 3], m.Vertices[vi * 3 + 1], m.Vertices[vi * 3 + 2]);
        }

        private static bool TriInAabb(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 min, Vector3 max)
        {
            float tMinX = Math.Min(v0.X, Math.Min(v1.X, v2.X));
            float tMaxX = Math.Max(v0.X, Math.Max(v1.X, v2.X));
            if (tMaxX < min.X || tMinX > max.X) return false;
            float tMinY = Math.Min(v0.Y, Math.Min(v1.Y, v2.Y));
            float tMaxY = Math.Max(v0.Y, Math.Max(v1.Y, v2.Y));
            if (tMaxY < min.Y || tMinY > max.Y) return false;
            float tMinZ = Math.Min(v0.Z, Math.Min(v1.Z, v2.Z));
            float tMaxZ = Math.Max(v0.Z, Math.Max(v1.Z, v2.Z));
            if (tMaxZ < min.Z || tMinZ > max.Z) return false;
            return true;
        }
    }
}
