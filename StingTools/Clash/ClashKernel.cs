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
        // G3: ConcurrentDictionary for parallel read-heavy access. BuildIndexes
        // writes once (in parallel); Run() reads many times (in parallel).
        // Prior Dictionary-with-lock serialised every TestPair through a single
        // mutex — on a 10k-pair run that was 20k lock acquisitions contending
        // on one mutex, dragging parallel throughput below single-thread.
        public ConcurrentDictionary<ClashElementKey, ObbTree> ObbTrees { get; } =
            new ConcurrentDictionary<ClashElementKey, ObbTree>();
        public AabbSweep Sweep { get; } = new AabbSweep();
        public int HardClashCount { get; private set; }
        public long BroadMs { get; private set; }
        public long NarrowMs { get; private set; }

        public void BuildIndexes(IEnumerable<ClashMeshBuffer> meshes)
        {
            var list = meshes.ToList();
            Sweep.Build(list);
            // D5: Bound parallelism. Unbounded Parallel.ForEach competes with
            //     Revit's own worker threads (CustomExporter, view regen) and
            //     causes UI judder during runs. Cap at ProcessorCount-1 so the
            //     UI thread keeps a free core, with a floor of 1.
            int workers = Math.Max(1, Environment.ProcessorCount - 1);
            var po = new ParallelOptions { MaxDegreeOfParallelism = workers };
            Parallel.ForEach(list, po, m =>
            {
                // G3: lock-free insert via ConcurrentDictionary.
                ObbTrees[m.Key] = ObbTree.Build(m);
            });
        }

        public List<ClashHit> Run()
        {
            var sw = Stopwatch.StartNew();
            var pairs = Sweep.CandidatePairs().ToList();
            BroadMs = sw.ElapsedMilliseconds;
            sw.Restart();

            var hits = new ConcurrentBag<ClashHit>();
            // D5: Bound narrow-phase parallelism for the same reason as
            //     BuildIndexes — keep one free core for the UI thread.
            int workers = Math.Max(1, Environment.ProcessorCount - 1);
            var po = new ParallelOptions { MaxDegreeOfParallelism = workers };
            Parallel.ForEach(pairs, po, pair =>
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
            //        G3: ConcurrentDictionary reads are lock-free — no need to
            //        lock(ObbTrees) here, which was serialising every parallel
            //        worker through a single mutex.
            ObbTrees.TryGetValue(a.Key, out var ta);
            ObbTrees.TryGetValue(b.Key, out var tb);

            bool overlap = (ta?.Root != null && tb?.Root != null)
                ? OverlapDescend(a, ta.Root, b, tb.Root)
                : BruteFallback(a, b, aabbMin, aabbMax);

            if (!overlap) return null;

            var centroid = 0.5f * (aabbMin + aabbMax);
            // D3: Better volume estimate. Prior code used the AABB-intersection
            //     box volume (extent.X × extent.Y × extent.Z) which is grossly
            //     inflated for oblique intersections — a long pipe crossing a
            //     beam reports the entire intersection-AABB volume even though
            //     the actual contact volume is tiny. This skewed R001's 100mm³
            //     threshold (slivers escape) and TriageScore (everything looks
            //     like a major overlap).
            //
            //     New estimator: min-extent depth × intersection AREA on the
            //     two largest axes. For a duct piercing a wall this is wall
            //     thickness × pipe diameter ≈ true contact volume. For
            //     parallel-stacked elements with clipping volume the answer
            //     is correct. Worst case for thin slivers (extent.minor → 0)
            //     it still reports near-zero, which is what we want.
            var volExtent = aabbMax - aabbMin;
            float ex = Math.Max(0f, volExtent.X);
            float ey = Math.Max(0f, volExtent.Y);
            float ez = Math.Max(0f, volExtent.Z);
            float minExt = Math.Min(ex, Math.Min(ey, ez));
            // Sum of pairwise products – min² gives footprint of the larger two
            // axes; multiplied by min depth approximates a slab of overlap.
            float footprintFt2 = (ex * ey + ey * ez + ex * ez) - 3f * minExt * minExt;
            footprintFt2 = Math.Max(0f, footprintFt2 * 0.5f);
            float volFt3 = footprintFt2 * minExt;
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
        /// rec-1: OBB-tree descent. AABB-overlap prune at every node,
        /// triangle-triangle SAT only at the leaf × leaf level. Returns on the
        /// first hit — callers only need boolean overlap.
        ///
        /// H7: Iterative (explicit Stack) instead of recursive. Eliminates any
        /// stack-overflow risk from pathologically deep trees and avoids per-
        /// frame method-call overhead. For typical ObbTree depths (MaxDepth=24
        /// both sides → max 48 frames) the overflow risk was already low, but
        /// an iterative version is equally fast, defensively robust, and works
        /// inside TestPair's Parallel.ForEach workers where stack size is the
        /// default 1 MiB (not the 4 MiB of the main thread).
        /// </summary>
        private static bool OverlapDescend(ClashMeshBuffer meshA, ObbNode rootA, ClashMeshBuffer meshB, ObbNode rootB)
        {
            if (rootA == null || rootB == null) return false;
            var stack = new Stack<(ObbNode, ObbNode)>();
            stack.Push((rootA, rootB));
            while (stack.Count > 0)
            {
                var (na, nb) = stack.Pop();
                if (na == null || nb == null) continue;
                if (!AabbsOverlap(na.AabbMin, na.AabbMax, nb.AabbMin, nb.AabbMax)) continue;

                // E3: Real OBB-OBB SAT prune. Only run when AABB extent
                //     ratio (longest / shortest) > 3 — for box-like geometry
                //     (chunky structural beams, square AHUs) the AABB and
                //     OBB are essentially the same shape, and computing the
                //     PCA axes is wasted work. For elongated geometry
                //     (long ducts, cable trays, beams along a raked angle)
                //     OBB pruning rejects many AABB-overlap pairs before
                //     reaching triangle-triangle SAT.
                if (IsElongated(na) || IsElongated(nb))
                {
                    if (!ObbSat.Overlap(na, nb, meshA, meshB)) continue;
                }

                if (na.IsLeaf && nb.IsLeaf)
                {
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
                }
                else if (na.IsLeaf)
                {
                    // A is a leaf, B internal — must descend B.
                    stack.Push((na, nb.Right));
                    stack.Push((na, nb.Left));
                }
                else if (nb.IsLeaf)
                {
                    // B is a leaf, A internal — must descend A.
                    stack.Push((na.Right, nb));
                    stack.Push((na.Left, nb));
                }
                else
                {
                    // E2: Both internal — descend the side with the larger AABB
                    //     volume. Standard BVH practice: splitting the larger
                    //     side first keeps subtree pairs balanced and prunes
                    //     deeper trees more aggressively. Prior code arbitrarily
                    //     picked A which left tall, narrow trees (typical for
                    //     long elements like ducts) un-pruned.
                    float volA = AabbVolume(na);
                    float volB = AabbVolume(nb);
                    if (volA >= volB)
                    {
                        stack.Push((na.Right, nb));
                        stack.Push((na.Left, nb));
                    }
                    else
                    {
                        stack.Push((na, nb.Right));
                        stack.Push((na, nb.Left));
                    }
                }
            }
            return false;
        }

        private static bool AabbsOverlap(Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax)
        {
            if (aMax.X < bMin.X || aMin.X > bMax.X) return false;
            if (aMax.Y < bMin.Y || aMin.Y > bMax.Y) return false;
            if (aMax.Z < bMin.Z || aMin.Z > bMax.Z) return false;
            return true;
        }

        /// <summary>E2: AABB volume for descend-order selection.</summary>
        private static float AabbVolume(ObbNode n)
        {
            var ext = n.AabbMax - n.AabbMin;
            return Math.Max(0f, ext.X) * Math.Max(0f, ext.Y) * Math.Max(0f, ext.Z);
        }

        /// <summary>
        /// E3: Heuristic — node is elongated if longest extent is > 3x the
        /// shortest. Only elongated nodes pay the OBB SAT cost; box-like
        /// nodes use the AABB-only path.
        /// </summary>
        private static bool IsElongated(ObbNode n)
        {
            var ext = n.AabbMax - n.AabbMin;
            float ex = Math.Max(0.001f, ext.X);
            float ey = Math.Max(0.001f, ext.Y);
            float ez = Math.Max(0.001f, ext.Z);
            float mx = Math.Max(ex, Math.Max(ey, ez));
            float mn = Math.Min(ex, Math.Min(ey, ez));
            return mx / mn > 3f;
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
