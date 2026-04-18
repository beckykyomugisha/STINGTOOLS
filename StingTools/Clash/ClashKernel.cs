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

            // rec-1: OBB-tree descent. Short-circuits on first triangle-triangle hit.
            //        Falls back to brute-force only if a mesh has no OBB-tree built
            //        (shouldn't happen because BuildIndexes runs over every mesh, but
            //        a defensive fallback keeps correctness if ObbTrees is cleared
            //        mid-run, e.g. by an invalidation in Stage 5).
            ObbTree ta = null, tb = null;
            lock (ObbTrees)
            {
                ObbTrees.TryGetValue(a.Key, out ta);
                ObbTrees.TryGetValue(b.Key, out tb);
            }

            bool overlap = (ta?.Root != null && tb?.Root != null)
                ? OverlapDescend(a, ta.Root, b, tb.Root)
                : BruteFallback(a, b, aabbMin, aabbMax);

            if (!overlap) return null;

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

        /// <summary>
        /// rec-1: Recursive OBB-tree descent. AABB-overlap prune at every node,
        /// triangle-triangle SAT only at the leaf × leaf level. Returns on the
        /// first hit — callers only need boolean overlap.
        /// </summary>
        private static bool OverlapDescend(ClashMeshBuffer meshA, ObbNode na, ClashMeshBuffer meshB, ObbNode nb)
        {
            if (na == null || nb == null) return false;
            if (!AabbsOverlap(na.AabbMin, na.AabbMax, nb.AabbMin, nb.AabbMax)) return false;

            if (na.IsLeaf && nb.IsLeaf)
            {
                // Leaf × leaf: triangle-triangle SAT over the triangle subsets.
                for (int ia = 0; ia < na.TriCount; ia++)
                {
                    int triA = na.Tris[na.TriStart + ia];
                    var va0 = GetVertex(meshA, triA, 0);
                    var va1 = GetVertex(meshA, triA, 1);
                    var va2 = GetVertex(meshA, triA, 2);
                    for (int ib = 0; ib < nb.TriCount; ib++)
                    {
                        int triB = nb.Tris[nb.TriStart + ib];
                        var vb0 = GetVertex(meshB, triB, 0);
                        var vb1 = GetVertex(meshB, triB, 1);
                        var vb2 = GetVertex(meshB, triB, 2);
                        if (MollerSat.TriTriOverlap(va0, va1, va2, vb0, vb1, vb2))
                            return true;
                    }
                }
                return false;
            }

            // Descend the larger volume node first for better pruning (SAH-lite heuristic).
            // Here we simply alternate: if A is internal, recurse into its children
            // against B; otherwise recurse into B's children against A.
            if (!na.IsLeaf)
            {
                if (OverlapDescend(meshA, na.Left, meshB, nb)) return true;
                if (OverlapDescend(meshA, na.Right, meshB, nb)) return true;
                return false;
            }
            else // nb is internal
            {
                if (OverlapDescend(meshA, na, meshB, nb.Left)) return true;
                if (OverlapDescend(meshA, na, meshB, nb.Right)) return true;
                return false;
            }
        }

        private static bool AabbsOverlap(Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax)
        {
            if (aMax.X < bMin.X || aMin.X > bMax.X) return false;
            if (aMax.Y < bMin.Y || aMin.Y > bMax.Y) return false;
            if (aMax.Z < bMin.Z || aMin.Z > bMax.Z) return false;
            return true;
        }

        /// <summary>
        /// rec-1: Brute-force fallback retained for the rare case where the OBB tree
        /// is unavailable (ObbTrees cleared mid-run). Short-circuits on first hit.
        /// </summary>
        private static bool BruteFallback(ClashMeshBuffer a, ClashMeshBuffer b, Vector3 aabbMin, Vector3 aabbMax)
        {
            for (int ia = 0; ia < a.TriangleCount; ia++)
            {
                var va0 = GetVertex(a, ia, 0); var va1 = GetVertex(a, ia, 1); var va2 = GetVertex(a, ia, 2);
                if (!TriInAabb(va0, va1, va2, aabbMin, aabbMax)) continue;
                for (int ib = 0; ib < b.TriangleCount; ib++)
                {
                    var vb0 = GetVertex(b, ib, 0); var vb1 = GetVertex(b, ib, 1); var vb2 = GetVertex(b, ib, 2);
                    if (!TriInAabb(vb0, vb1, vb2, aabbMin, aabbMax)) continue;
                    if (MollerSat.TriTriOverlap(va0, va1, va2, vb0, vb1, vb2))
                        return true;
                }
            }
            return false;
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
