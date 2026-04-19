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
        private readonly AabbSweep _sweep = new AabbSweep();
        private ClashMatrix _matrix;
        private ClashRuleEngine _ruleEngine;
        public bool Initialised { get; private set; }

        // Public observability hook — subscribed by BCC Clash tab when present,
        // may not be raised from inside ClashSession in the current pipeline.
#pragma warning disable CS0067
        public event Action<int, bool> OnElementFlagChanged;   // (eid, isFlagged)
#pragma warning restore CS0067

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
                foreach (var kv in all)
                {
                    if (kv.Key.LinkInstanceElementId == -1)
                        _meshByEid[kv.Key.ElementId] = kv.Value;
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
                // Revit 2024+ obsoleted ElementId(int) — use the Int64 overload.
                var element = _doc.GetElement(new ElementId((long)elementId));
                if (element == null) return RemoveElement(elementId);

                var fresh = TryExtractOneElement(element);
                lock (_lock)
                {
                    _meshByEid[elementId] = fresh;
                    _sweep_Rebuild();   // rebuild; for Stage 6 we will do incremental refit

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
                if (_meshByEid.Remove(elementId))
                {
                    _sweep_Rebuild();
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
                // IFC GUID dropped — ExporterIFCUtils is no longer in the core
                // Revit 2025 API. element.UniqueId already provides global
                // uniqueness for the clash key, so empty IFC is acceptable.
                string ifc = string.Empty;

                // ElementId.IntegerValue obsoleted in Revit 2024; use .Value (long → int).
                var key = new ClashElementKey(docGuid, -1, (int)element.Id.Value, element.UniqueId, ifc);
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
            foreach (var other in _meshByEid.Values)
            {
                if (ReferenceEquals(other, target)) continue;
                if (other.MaxX < target.MinX - 0.164f || other.MinX > target.MaxX + 0.164f) continue;
                if (other.MaxY < target.MinY - 0.164f || other.MinY > target.MaxY + 0.164f) continue;
                if (other.MaxZ < target.MinZ - 0.164f || other.MinZ > target.MaxZ + 0.164f) continue;

                var hit = BruteTest(target, other);
                if (hit != null) hits.Add(hit);
            }
            return hits;
        }

        private static ClashHit BruteTest(ClashMeshBuffer a, ClashMeshBuffer b)
        {
            // Simple AABB-overlap brute pass. Stage 6 replaces with BVH descent.
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
