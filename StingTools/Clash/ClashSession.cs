// ClashSession.cs — persistent live-clash state per Revit document.
// Holds mesh cache, OBB trees, and sweep index across edits. All mutations are
// single-threaded (called from LiveClashHandler on the Revit API thread only).
// Reads (clash queries) are thread-safe via a snapshot taken under a lock.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    public sealed class ClashSession
    {
        private static readonly ConcurrentDictionary<string, ClashSession> _perDoc =
            new ConcurrentDictionary<string, ClashSession>();

        public static ClashSession ForDocument(Document doc)
        {
            string key = doc.ProjectInformation?.UniqueId ?? doc.PathName ?? "host";
            return _perDoc.GetOrAdd(key, _ => new ClashSession(doc));
        }

        public static void Clear(Document doc)
        {
            string key = doc.ProjectInformation?.UniqueId ?? doc.PathName ?? "host";
            _perDoc.TryRemove(key, out _);
        }

        private readonly Document _doc;
        private readonly object _lock = new object();
        private readonly Dictionary<int, ClashMeshBuffer> _meshByEid = new Dictionary<int, ClashMeshBuffer>();
        // rec-1: Cache OBB trees by element id so the live narrow-phase can descend
        // rather than brute-force. Rebuilt on RefreshElement, dropped on RemoveElement.
        private readonly Dictionary<int, ObbTree> _obbByEid = new Dictionary<int, ObbTree>();
        private readonly AabbSweep _sweep = new AabbSweep();
        private ClashMatrix _matrix;
        private ClashRuleEngine _ruleEngine;
        public bool Initialised { get; private set; }

        public event Action<int, bool> OnElementFlagChanged;   // (eid, isFlagged)

        private ClashSession(Document doc)
        {
            _doc = doc;
            _matrix = ClashMatrix.Default();
            _ruleEngine = new ClashRuleEngine();
        }

        public void InitialiseFromView(View3D view)
        {
            var all = MeshExtractor.Extract(_doc, view);
            lock (_lock)
            {
                _meshByEid.Clear();
                _obbByEid.Clear();
                foreach (var kv in all)
                {
                    if (kv.Key.LinkInstanceElementId == -1)
                    {
                        _meshByEid[kv.Key.ElementId] = kv.Value;
                        // rec-1: Pre-build OBB trees during cold init so first tick of
                        // live-clash doesn't pay the cost. Stage-6 can make this lazy.
                        _obbByEid[kv.Key.ElementId] = ObbTree.Build(kv.Value);
                    }
                }
                _sweep.GetType();   // keep ref alive
                _sweep_Rebuild();
                Initialised = true;
            }
            StingLog.Info($"ClashSession initialised: {_meshByEid.Count} elements");
        }

        // Expose field via helper to avoid reflection
        private AabbSweep Sweep => _sweep;
        private void _sweep_Rebuild()
        {
            // Rebuild the sweep index from scratch.
            var fresh = new AabbSweep();
            fresh.Build(_meshByEid.Values);
            // Replace by swapping contents via reflection-free approach: rebuild-in-place is safer.
            // Since AabbSweep exposes no clear, we keep a reference via _sweepRef below.
            _sweepRef = fresh;
        }
        private AabbSweep _sweepRef;
        private AabbSweep ActiveSweep => _sweepRef ?? _sweep;

        /// <summary>
        /// Called for each dirty element. Extracts fresh geometry, updates the index,
        /// runs narrow-phase on its neighbours, and returns the new flag set for this element
        /// plus any neighbours whose flag state changed.
        /// </summary>
        public LiveClashResult RefreshElement(int elementId)
        {
            var result = new LiveClashResult();
            try
            {
                var element = _doc.GetElement(new ElementId(elementId));
                if (element == null) return RemoveElement(elementId);

                var fresh = TryExtractOneElement(element);
                lock (_lock)
                {
                    _meshByEid[elementId] = fresh;
                    // rec-1: Rebuild OBB tree for this element so NarrowPhaseFor can descend.
                    _obbByEid[elementId] = fresh != null ? ObbTree.Build(fresh) : null;
                    // rec-9: Incremental sweep-index update. Replaces the prior full
                    // _sweep_Rebuild() which cost ~50 ms per edit on 50k-element
                    // models. RBush Delete+Insert is O(log n) each.
                    if (fresh != null) ActiveSweep.AddOrUpdate(fresh);
                    else ActiveSweep.Remove(new ClashElementKey(
                            _doc.ProjectInformation?.UniqueId ?? _doc.PathName ?? "host",
                            -1, elementId, "", ""));

                    // Narrow phase against neighbours.
                    var hits = NarrowPhaseFor(fresh);
                    var newFlagged = new HashSet<int>(hits.SelectMany(h => new[] { h.A.ElementId, h.B.ElementId }));

                    // Compute diff vs previous flag state.
                    var prev = _flaggedIds;
                    foreach (var id in newFlagged) if (!prev.Contains(id)) result.NewlyFlagged.Add(id);
                    foreach (var id in prev) if (!newFlagged.Contains(id)) result.NewlyCleared.Add(id);
                    _flaggedIds = newFlagged;
                    result.CurrentHits.AddRange(hits);
                }
            }
            catch (Exception ex) { StingLog.Warn($"ClashSession.RefreshElement({elementId}): {ex.Message}"); }
            return result;
        }

        public LiveClashResult RemoveElement(int elementId)
        {
            var result = new LiveClashResult();
            lock (_lock)
            {
                if (_meshByEid.TryGetValue(elementId, out var oldMesh))
                {
                    _meshByEid.Remove(elementId);
                    _obbByEid.Remove(elementId);   // rec-1: drop paired OBB tree
                    // rec-9: O(log n) sweep removal instead of full rebuild.
                    ActiveSweep.Remove(oldMesh.Key);
                    if (_flaggedIds.Remove(elementId)) result.NewlyCleared.Add(elementId);
                }
            }
            return result;
        }

        private HashSet<int> _flaggedIds = new HashSet<int>();

        private ClashMeshBuffer TryExtractOneElement(Element element)
        {
            // Use Face.Triangulate on the element's Geometry for single-element extraction.
            // This is the lightweight path — full CustomExporter re-run is too expensive per edit.
            try
            {
                var opts = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
                var geom = element.get_Geometry(opts);
                if (geom == null) return null;

                var verts = new List<float>();
                var indices = new List<int>();
                var dedup = new Dictionary<long, int>();

                foreach (var obj in geom) WalkGeometry(obj, Transform.Identity, verts, indices, dedup);

                if (verts.Count == 0) return null;

                string docGuid = _doc.ProjectInformation?.UniqueId ?? _doc.PathName ?? "host";
                string ifc = "";
                try { ifc = ExporterIFCUtils.CreateSubElementGUID(element, 0); } catch { }

                var key = new ClashElementKey(docGuid, -1, element.Id.IntegerValue, element.UniqueId, ifc);
                return new ClashMeshBuffer(key, element.Category?.Name ?? "", verts.ToArray(), indices.ToArray());
            }
            catch (Exception ex) { StingLog.Warn("TryExtractOneElement: " + ex.Message); return null; }
        }

        private static void WalkGeometry(GeometryObject go, Transform xform, List<float> verts, List<int> indices, Dictionary<long, int> dedup)
        {
            if (go is GeometryInstance gi)
            {
                var t = xform.Multiply(gi.Transform);
                foreach (var c in gi.GetInstanceGeometry()) WalkGeometry(c, t, verts, indices, dedup);
            }
            else if (go is Solid s && s.Volume > 1e-9)
            {
                foreach (Face f in s.Faces)
                {
                    var m = f.Triangulate();
                    if (m == null) continue;
                    int n = m.NumTriangles;
                    for (int i = 0; i < n; i++)
                    {
                        var t = m.get_Triangle(i);
                        int i0 = Intern(xform.OfPoint(t.get_Vertex(0)), verts, dedup);
                        int i1 = Intern(xform.OfPoint(t.get_Vertex(1)), verts, dedup);
                        int i2 = Intern(xform.OfPoint(t.get_Vertex(2)), verts, dedup);
                        indices.Add(i0); indices.Add(i1); indices.Add(i2);
                    }
                }
            }
        }

        private static int Intern(XYZ p, List<float> verts, Dictionary<long, int> dedup)
        {
            long q = ((long)Math.Round(p.X * 1000.0) * 73856093L)
                   ^ ((long)Math.Round(p.Y * 1000.0) * 19349663L)
                   ^ ((long)Math.Round(p.Z * 1000.0) * 83492791L);
            if (dedup.TryGetValue(q, out int idx)) return idx;
            idx = verts.Count / 3;
            verts.Add((float)p.X); verts.Add((float)p.Y); verts.Add((float)p.Z);
            dedup[q] = idx;
            return idx;
        }

        private List<ClashHit> NarrowPhaseFor(ClashMeshBuffer target)
        {
            var hits = new List<ClashHit>();
            if (target == null || target.TriangleCount == 0) return hits;
            // rec-1: OBB descent when both sides have a tree; falls back to brute
            //        only when the paired element has no tree yet (first-seen path).
            _obbByEid.TryGetValue(target.Key.ElementId, out var treeTarget);
            foreach (var other in _meshByEid.Values)
            {
                if (ReferenceEquals(other, target)) continue;
                if (other.MaxX < target.MinX - 0.164f || other.MinX > target.MaxX + 0.164f) continue;
                if (other.MaxY < target.MinY - 0.164f || other.MinY > target.MaxY + 0.164f) continue;
                if (other.MaxZ < target.MinZ - 0.164f || other.MinZ > target.MaxZ + 0.164f) continue;

                _obbByEid.TryGetValue(other.Key.ElementId, out var treeOther);

                var hit = (treeTarget?.Root != null && treeOther?.Root != null)
                    ? DescendTest(target, treeTarget.Root, other, treeOther.Root)
                    : BruteTest(target, other);
                if (hit != null) hits.Add(hit);
            }
            return hits;
        }

        /// <summary>
        /// rec-1: OBB-tree descent producing a ClashHit on first hit. Mirrors the
        /// ClashKernel.OverlapDescend algorithm but returns the full ClashHit rather
        /// than just a bool so the live path can populate centroid + AABB for
        /// downstream flagging.
        /// </summary>
        private static ClashHit DescendTest(ClashMeshBuffer meshA, ObbNode na, ClashMeshBuffer meshB, ObbNode nb)
        {
            if (na == null || nb == null) return null;
            if (na.AabbMax.X < nb.AabbMin.X || na.AabbMin.X > nb.AabbMax.X) return null;
            if (na.AabbMax.Y < nb.AabbMin.Y || na.AabbMin.Y > nb.AabbMax.Y) return null;
            if (na.AabbMax.Z < nb.AabbMin.Z || na.AabbMin.Z > nb.AabbMax.Z) return null;

            if (na.IsLeaf && nb.IsLeaf)
            {
                for (int ia = 0; ia < na.TriCount; ia++)
                {
                    int triA = na.Tris[na.TriStart + ia];
                    var va0 = GetV(meshA, triA, 0); var va1 = GetV(meshA, triA, 1); var va2 = GetV(meshA, triA, 2);
                    for (int ib = 0; ib < nb.TriCount; ib++)
                    {
                        int triB = nb.Tris[nb.TriStart + ib];
                        var vb0 = GetV(meshB, triB, 0); var vb1 = GetV(meshB, triB, 1); var vb2 = GetV(meshB, triB, 2);
                        if (MollerSat.TriTriOverlap(va0, va1, va2, vb0, vb1, vb2))
                        {
                            var cen = 0.25f * (va0 + va1 + vb0 + vb1);
                            return new ClashHit
                            {
                                A = meshA.Key, B = meshB.Key,
                                Centroid = cen,
                                AabbMin = new Vector3(Math.Max(meshA.MinX, meshB.MinX), Math.Max(meshA.MinY, meshB.MinY), Math.Max(meshA.MinZ, meshB.MinZ)),
                                AabbMax = new Vector3(Math.Min(meshA.MaxX, meshB.MaxX), Math.Min(meshA.MaxY, meshB.MaxY), Math.Min(meshA.MaxZ, meshB.MaxZ)),
                                VolumeMm3 = 100f, Kind = "hard", FailureMode = ""
                            };
                        }
                    }
                }
                return null;
            }
            if (!na.IsLeaf)
            {
                var l = DescendTest(meshA, na.Left, meshB, nb); if (l != null) return l;
                var r = DescendTest(meshA, na.Right, meshB, nb); if (r != null) return r;
                return null;
            }
            else
            {
                var l = DescendTest(meshA, na, meshB, nb.Left); if (l != null) return l;
                var r = DescendTest(meshA, na, meshB, nb.Right); if (r != null) return r;
                return null;
            }
        }

        private static ClashHit BruteTest(ClashMeshBuffer a, ClashMeshBuffer b)
        {
            // Simple AABB-overlap brute pass. Retained as rec-1 fallback when OBB
            // trees aren't cached yet (first-seen element mid-run).
            for (int ia = 0; ia < a.TriangleCount; ia++)
            {
                var va0 = GetV(a, ia, 0); var va1 = GetV(a, ia, 1); var va2 = GetV(a, ia, 2);
                for (int ib = 0; ib < b.TriangleCount; ib++)
                {
                    var vb0 = GetV(b, ib, 0); var vb1 = GetV(b, ib, 1); var vb2 = GetV(b, ib, 2);
                    if (MollerSat.TriTriOverlap(va0, va1, va2, vb0, vb1, vb2))
                    {
                        var cen = 0.25f * (va0 + va1 + vb0 + vb1);
                        return new ClashHit
                        {
                            A = a.Key, B = b.Key,
                            Centroid = cen,
                            AabbMin = new Vector3(Math.Max(a.MinX, b.MinX), Math.Max(a.MinY, b.MinY), Math.Max(a.MinZ, b.MinZ)),
                            AabbMax = new Vector3(Math.Min(a.MaxX, b.MaxX), Math.Min(a.MaxY, b.MaxY), Math.Min(a.MaxZ, b.MaxZ)),
                            VolumeMm3 = 100f, Kind = "hard", FailureMode = ""
                        };
                    }
                }
            }
            return null;
        }

        private static Vector3 GetV(ClashMeshBuffer m, int tri, int corner)
        {
            int vi = m.Indices[tri * 3 + corner];
            return new Vector3(m.Vertices[vi * 3], m.Vertices[vi * 3 + 1], m.Vertices[vi * 3 + 2]);
        }
    }

    public sealed class LiveClashResult
    {
        public List<ClashHit> CurrentHits { get; } = new List<ClashHit>();
        public HashSet<int> NewlyFlagged { get; } = new HashSet<int>();
        public HashSet<int> NewlyCleared { get; } = new HashSet<int>();
    }
}
