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
// Autodesk.Revit.DB.IFC dropped with the ExporterIFCUtils call site.
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
        // A1: Per-element neighbour index — element id → set of element ids it
        // currently clashes with. Lets RefreshElement compute the diff for ONE
        // element without nuking flags on unrelated elements. Maintained
        // symmetrically: when (a,b) clashes, both _clashNeighbours[a] and
        // _clashNeighbours[b] contain each other.
        private readonly Dictionary<int, HashSet<int>> _clashNeighbours = new Dictionary<int, HashSet<int>>();
        // A4: Lazily-built per-element ElementFacts cache so the live path can
        // populate System/Workset alongside Category. Invalidated on
        // RefreshElement / RemoveElement for the affected element id.
        private readonly Dictionary<int, ElementFacts> _factsCache = new Dictionary<int, ElementFacts>();
        // B3: Volatile timestamp of the last dirty mark — read by ClashRunEvent
        // to gate hourly full runs. Defaults to "now" so the very first
        // scheduled run always fires (previous-baseline state).
        private volatile object _lastDirtyAtBox = (object)DateTime.UtcNow;
        public DateTime LastDirtyAtUtc
        {
            get => _lastDirtyAtBox is DateTime dt ? dt : DateTime.UtcNow;
        }
        public void MarkDirty() { _lastDirtyAtBox = (object)DateTime.UtcNow; }

        // F9: Watched-element set. Coordinator marks an element as
        //     "watch this clash" → narrow-phase always re-runs for that
        //     element on every dirty edit, even if it falls out of the
        //     normal _clashNeighbours map (e.g. the matrix doesn't
        //     normally consider its category). Useful when iteratively
        //     fixing a hard-to-pin-down clash.
        private readonly HashSet<int> _watchedElements = new HashSet<int>();
        public void Watch(int elementId)    { lock (_lock) _watchedElements.Add(elementId); }
        public void Unwatch(int elementId)  { lock (_lock) _watchedElements.Remove(elementId); }
        public bool IsWatched(int elementId) { lock (_lock) return _watchedElements.Contains(elementId); }
        public IReadOnlyCollection<int> WatchedSnapshot()
        {
            lock (_lock) return _watchedElements.ToArray();
        }
        // G8: The AabbSweep that the live path actually queries. Replaced
        // wholesale on InitialiseFromView / rebuilt-but-kept-in-place by
        // AddOrUpdate/Remove (rec-9). Prior code had two references — _sweep
        // and _sweepRef — with the former never used after first init. Collapsed
        // to one field; ActiveSweep property kept for readability at call sites.
        private AabbSweep _sweep = new AabbSweep();
        private AabbSweep ActiveSweep => _sweep;
        private ClashMatrix _matrix;
        private ClashRuleEngine _ruleEngine;
        public bool Initialised { get; private set; }

        // Phase-98d: public event — raised by subscribers from the dock-panel
        // live-clash observer; compiler can't see the delegation path so it
        // warns CS0067. Suppress rather than delete — the contract is used.
#pragma warning disable CS0067
        public event Action<int, bool> OnElementFlagChanged;   // (eid, isFlagged)
        public event Action<ClashRunRecord> OnRunCompleted;    // raised by SeedFromRun
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
                _obbByEid.Clear();
                _clashNeighbours.Clear();   // A1: reset neighbour index on cold init
                _factsCache.Clear();         // A4: reset facts cache on cold init
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
                _sweep_Rebuild();
                Initialised = true;
            }
            StingLog.Info($"ClashSession initialised: {_meshByEid.Count} elements");
        }

        /// <summary>
        /// Rebuild the sweep index from scratch. Used on cold init only
        /// (InitialiseFromView); per-edit updates go through
        /// ActiveSweep.AddOrUpdate/Remove (rec-9).
        /// </summary>
        private void _sweep_Rebuild()
        {
            var fresh = new AabbSweep();
            fresh.Build(_meshByEid.Values);
            _sweep = fresh;
        }

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
                // B3: dirty-model tracking for the scheduler.
                MarkDirty();
                // ElementId(int) ctor is obsolete in Revit 2024+; use Int64 overload.
                var element = _doc.GetElement(new ElementId((long)elementId));
                if (element == null) return RemoveElement(elementId);

                var fresh = TryExtractOneElement(element);
                lock (_lock)
                {
                    _meshByEid[elementId] = fresh;
                    // rec-1: Rebuild OBB tree for this element so NarrowPhaseFor can descend.
                    _obbByEid[elementId] = fresh != null ? ObbTree.Build(fresh) : null;
                    // A4: invalidate cached facts so System/Workset re-resolve on next use.
                    _factsCache.Remove(elementId);
                    // rec-9: Incremental sweep-index update. Replaces the prior full
                    // _sweep_Rebuild() which cost ~50 ms per edit on 50k-element
                    // models. RBush Delete+Insert is O(log n) each.
                    if (fresh != null) ActiveSweep.AddOrUpdate(fresh);
                    else ActiveSweep.Remove(new ClashElementKey(
                            _doc.ProjectInformation?.UniqueId ?? _doc.PathName ?? "host",
                            -1, elementId, "", ""));

                    // Narrow phase against neighbours.
                    var hits = NarrowPhaseFor(fresh);

                    // A1: Build the new neighbour set for THIS element only.
                    //
                    // Each hit pair contains target (this elementId) and other.
                    // Skip any "other" hit that is a self-clash (shouldn't
                    // happen — guarded by ReferenceEquals — but be defensive).
                    var newNeighbours = new HashSet<int>();
                    foreach (var h in hits)
                    {
                        int otherId = h.A.ElementId == elementId ? h.B.ElementId : h.A.ElementId;
                        if (otherId == elementId) continue;
                        newNeighbours.Add(otherId);
                    }

                    // A1: Compute the prior neighbour set so we can diff. Then
                    // update the symmetric neighbour map: for any old neighbour
                    // that is no longer a neighbour, remove the back-edge in
                    // _clashNeighbours[other]; for any new neighbour, add the
                    // back-edge. This keeps the map consistent and lets us
                    // ask "does element X still clash with anything?" in O(1).
                    _clashNeighbours.TryGetValue(elementId, out var oldNeighbours);
                    oldNeighbours = oldNeighbours ?? new HashSet<int>();

                    foreach (var oldId in oldNeighbours)
                    {
                        if (newNeighbours.Contains(oldId)) continue;
                        // edge (elementId,oldId) gone — remove back-edge.
                        if (_clashNeighbours.TryGetValue(oldId, out var otherSet))
                        {
                            otherSet.Remove(elementId);
                            if (otherSet.Count == 0) _clashNeighbours.Remove(oldId);
                        }
                    }
                    foreach (var newId in newNeighbours)
                    {
                        if (oldNeighbours.Contains(newId)) continue;
                        if (!_clashNeighbours.TryGetValue(newId, out var otherSet))
                        {
                            otherSet = new HashSet<int>();
                            _clashNeighbours[newId] = otherSet;
                        }
                        otherSet.Add(elementId);
                    }
                    if (newNeighbours.Count == 0) _clashNeighbours.Remove(elementId);
                    else _clashNeighbours[elementId] = newNeighbours;

                    // A1: Compute flag-state diff narrowed to THIS element and
                    // its prior + current neighbours. Other elements' flag
                    // state is untouched — RefreshElement(X) must not silently
                    // clear flags on Y when X and Y are unrelated.
                    var prev = _flaggedIds;

                    bool wasFlagged = prev.Contains(elementId);
                    bool isFlagged = newNeighbours.Count > 0;
                    if (isFlagged && !wasFlagged)
                    {
                        result.NewlyFlagged.Add(elementId);
                        prev.Add(elementId);
                    }
                    else if (!isFlagged && wasFlagged)
                    {
                        result.NewlyCleared.Add(elementId);
                        prev.Remove(elementId);
                    }

                    // For each previously-connected neighbour that is no longer
                    // a neighbour: it may now have zero clashes, in which case
                    // it should clear. Check via _clashNeighbours so we respect
                    // its OTHER edges.
                    foreach (var oldId in oldNeighbours)
                    {
                        if (newNeighbours.Contains(oldId)) continue;
                        bool stillFlagged = _clashNeighbours.ContainsKey(oldId)
                            && _clashNeighbours[oldId].Count > 0;
                        if (!stillFlagged && prev.Remove(oldId))
                            result.NewlyCleared.Add(oldId);
                    }
                    // For each newly-connected neighbour: it has at least one
                    // clash now (with elementId) so it must be flagged.
                    foreach (var newId in newNeighbours)
                    {
                        if (oldNeighbours.Contains(newId)) continue;
                        if (prev.Add(newId)) result.NewlyFlagged.Add(newId);
                    }

                    result.CurrentHits.AddRange(hits);
                }
            }
            catch (Exception ex) { StingLog.Warn($"ClashSession.RefreshElement({elementId}): {ex.Message}"); }
            // H6: Fire outside the lock.
            RaiseFlagChanges(result);
            return result;
        }

        public LiveClashResult RemoveElement(int elementId)
        {
            var result = new LiveClashResult();
            // B3: dirty-model tracking for the scheduler.
            MarkDirty();
            lock (_lock)
            {
                if (_meshByEid.TryGetValue(elementId, out var oldMesh))
                {
                    _meshByEid.Remove(elementId);
                    _obbByEid.Remove(elementId);   // rec-1: drop paired OBB tree
                    _factsCache.Remove(elementId);   // A4: drop cached facts
                    // rec-9: O(log n) sweep removal instead of full rebuild.
                    ActiveSweep.Remove(oldMesh.Key);
                    if (_flaggedIds.Remove(elementId)) result.NewlyCleared.Add(elementId);

                    // A1: Drop this element from the neighbour map and clean up
                    // every back-edge. Any previous neighbour that loses its
                    // last clash should be cleared.
                    if (_clashNeighbours.TryGetValue(elementId, out var oldNeighbours))
                    {
                        _clashNeighbours.Remove(elementId);
                        foreach (var otherId in oldNeighbours)
                        {
                            if (_clashNeighbours.TryGetValue(otherId, out var otherSet))
                            {
                                otherSet.Remove(elementId);
                                if (otherSet.Count == 0)
                                {
                                    _clashNeighbours.Remove(otherId);
                                    if (_flaggedIds.Remove(otherId)) result.NewlyCleared.Add(otherId);
                                }
                            }
                        }
                    }
                }
            }
            // H6: Fire outside the lock so a slow subscriber doesn't block the
            //     Revit API thread. Swallow subscriber exceptions — UI bugs
            //     shouldn't crash the live-clash loop.
            RaiseFlagChanges(result);
            return result;
        }

        /// <summary>
        /// H2: Seed the live session's flag set from a freshly computed
        /// ClashRunRecord. Called by ClashRunCommand after persistence so the
        /// in-authoring flag state and the persisted clashes.json agree.
        /// Also raises OnRunCompleted so subscribed UI can refresh.
        /// </summary>
        public void SeedFromRun(ClashRunRecord run)
        {
            if (run?.Clashes == null) return;
            var result = new LiveClashResult();
            lock (_lock)
            {
                var newFlagged = new HashSet<int>();
                foreach (var c in run.Clashes)
                {
                    if (c.State == "Resolved" || c.State == "Void") continue;
                    if (c.ElementA != null && c.ElementA.LinkInstanceId == -1)
                        newFlagged.Add(c.ElementA.ElementId);
                    if (c.ElementB != null && c.ElementB.LinkInstanceId == -1)
                        newFlagged.Add(c.ElementB.ElementId);
                }
                var prev = _flaggedIds;
                foreach (var id in newFlagged) if (!prev.Contains(id)) result.NewlyFlagged.Add(id);
                foreach (var id in prev) if (!newFlagged.Contains(id)) result.NewlyCleared.Add(id);
                _flaggedIds = newFlagged;
            }
            RaiseFlagChanges(result);
            try { OnRunCompleted?.Invoke(run); }
            catch (Exception ex) { StingLog.Warn($"OnRunCompleted subscriber threw: {ex.Message}"); }
        }

        /// <summary>
        /// H6: Centralised event dispatcher. Swallow subscriber exceptions so
        /// a buggy WPF handler never kills the live-clash pipeline.
        /// </summary>
        private void RaiseFlagChanges(LiveClashResult r)
        {
            var handler = OnElementFlagChanged;
            if (handler == null || r == null) return;
            foreach (var id in r.NewlyFlagged)
            {
                try { handler(id, true); }
                catch (Exception ex) { StingLog.Warn($"OnElementFlagChanged(flag) subscriber: {ex.Message}"); }
            }
            foreach (var id in r.NewlyCleared)
            {
                try { handler(id, false); }
                catch (Exception ex) { StingLog.Warn($"OnElementFlagChanged(clear) subscriber: {ex.Message}"); }
            }
        }

        private HashSet<int> _flaggedIds = new HashSet<int>();

        // D10: Thread-static reusable buffers for the per-element extractor.
        //      LiveClashHandler can fire many times per second during dragging
        //      and each call previously allocated a fresh List<float> + List<int>
        //      + Dictionary. Pooling on a thread-static slot avoids the GC churn
        //      without sharing state across threads (the IUpdater path is on
        //      the Revit API thread; live-clash sessions are single-threaded).
        [ThreadStatic] private static List<float> _tlsVerts;
        [ThreadStatic] private static List<int>   _tlsIndices;
        [ThreadStatic] private static Dictionary<long, int> _tlsDedup;

        private ClashMeshBuffer TryExtractOneElement(Element element)
        {
            // Use Face.Triangulate on the element's Geometry for single-element extraction.
            // This is the lightweight path — full CustomExporter re-run is too expensive per edit.
            try
            {
                var opts = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
                var geom = element.get_Geometry(opts);
                if (geom == null) return null;

                var verts = _tlsVerts ?? (_tlsVerts = new List<float>(1024));
                var indices = _tlsIndices ?? (_tlsIndices = new List<int>(512));
                var dedup = _tlsDedup ?? (_tlsDedup = new Dictionary<long, int>(512));
                verts.Clear(); indices.Clear(); dedup.Clear();

                foreach (var obj in geom) WalkGeometry(obj, Transform.Identity, verts, indices, dedup);

                if (verts.Count == 0) return null;

                string docGuid = _doc.ProjectInformation?.UniqueId ?? _doc.PathName ?? "host";
                // IFC GUID: we'd normally call ExporterIFCUtils.CreateSubElementGUID,
                // but that type lives in RevitAPIIFC.dll (not referenced by this
                // project). Element.UniqueId is the same string Revit feeds into
                // its IFC export pipeline, so it's a sound substitute for the
                // clash-kernel's per-element identity hash.
                string ifc = element.UniqueId ?? "";

                var key = new ClashElementKey(docGuid, -1, (int)element.Id.Value, element.UniqueId, ifc);
                // ToArray copies — necessary because the mesh buffer outlives the
                // pooled lists (next extraction reuses the same backing storage).
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

            // G2: Query the sweep index for broad-phase candidates instead of
            // iterating every mesh in the session.
            // D6: Snapshot the candidate enumeration into a local list before
            //     the loop so we don't hold the lock across triangle-triangle
            //     SAT work. The lock-holding caller (RefreshElement) uses the
            //     same _lock, but materialising the snapshot here means a
            //     future read-side query path can iterate without blocking.
            var sweep = ActiveSweep;
            var raw = sweep?.QueryCandidatesFor(target) ?? (IEnumerable<ClashMeshBuffer>)_meshByEid.Values;
            var candidates = raw is List<ClashMeshBuffer> ? raw : raw.ToList();

            // H1 + A4: Pre-build ElementFacts for target once (constant across
            //     the candidate loop). A4 — resolve System / Workset from the
            //     real Revit Element (cached per-eid) so the live path matches
            //     matrix cells that filter on System=... rather than always
            //     missing them. Without this, ducts/pipes whose matrix rule
            //     keys on System name silently fail to flag in-authoring.
            var targetFacts = ResolveFacts(target);

            foreach (var other in candidates)
            {
                if (ReferenceEquals(other, target)) continue;
                // E8: Defence-in-depth self-clash filter. ReferenceEquals is
                //     correct when the same ClashMeshBuffer instance comes
                //     back via the sweep cache, but two distinct Solids of
                //     the same FamilyInstance can also surface (e.g. a
                //     curtain wall with mullion + panel modelled on the same
                //     ElementId). ElementId-equality short-circuits both.
                if (other.Key != null && target.Key != null &&
                    other.Key.ElementId == target.Key.ElementId &&
                    other.Key.LinkInstanceElementId == target.Key.LinkInstanceElementId) continue;
                if (other.MaxX < target.MinX - 0.164f || other.MinX > target.MaxX + 0.164f) continue;
                if (other.MaxY < target.MinY - 0.164f || other.MinY > target.MaxY + 0.164f) continue;
                if (other.MaxZ < target.MinZ - 0.164f || other.MinZ > target.MaxZ + 0.164f) continue;

                // H1: Matrix + rule filter BEFORE the expensive narrow phase.
                //     Without this, the live path flagged any overlap — including
                //     "Intentional" pairs (duct insulation vs its own duct, wall-
                //     floor joins, curtain mullion-panel) that the headless run
                //     drops via ClashRule. Users saw warning triangles on every
                //     hosted ceiling fixture and lost trust in the flagging.
                var otherFacts = ResolveFacts(other);
                var cell = _matrix?.Match(targetFacts, otherFacts);
                if (cell == null) continue;   // not in coordination scope — skip

                ClashHit hit = (treeTarget?.Root != null && _obbByEid.TryGetValue(other.Key.ElementId, out var treeOther) && treeOther?.Root != null)
                    ? DescendTest(target, treeTarget.Root, other, treeOther.Root)
                    : BruteTest(target, other);
                if (hit == null) continue;

                // H1: Run the rule engine (tessellation artifact drop, intentional
                //     hosted-element exclusions, volume thresholds) on the tentative
                //     hit. Only "Keep" verdicts surface as live flags.
                var classified = _ruleEngine?.Classify(hit, targetFacts, otherFacts, cell);
                if (classified == null || classified.Verdict == ClashVerdict.Keep)
                    hits.Add(hit);
            }
            return hits;
        }

        /// <summary>
        /// A4: Build ElementFacts including System and Workset for the live
        /// matrix/rule path. Cached per-eid in _factsCache so the loop in
        /// NarrowPhaseFor pays one Revit Element lookup per element per edit
        /// (not per candidate pair). Cache is invalidated by RefreshElement
        /// and RemoveElement for the affected element id.
        ///
        /// Falls back to category-only when the element is no longer in the
        /// document (deleted mid-tick) or any of the parameter reads throw.
        /// Live-path was previously category-only — H1's existing matrix
        /// pre-filter was correct on Category but missed any cell keyed on
        /// System=... so duct-vs-duct rules etc. were silently dropped.
        /// </summary>
        private ElementFacts ResolveFacts(ClashMeshBuffer m)
        {
            if (m == null) return new ElementFacts();
            int eid = m.Key?.ElementId ?? 0;
            if (_factsCache.TryGetValue(eid, out var cached)) return cached;
            var facts = new ElementFacts { Category = m.Category ?? "" };
            try
            {
                if (eid != 0 && _doc != null)
                {
                    var el = _doc.GetElement(new ElementId((long)eid));
                    if (el != null)
                    {
                        facts.System = ReadElementSystem(el);
                        facts.Workset = ReadElementWorkset(_doc, el);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ClashSession.ResolveFacts({eid}): {ex.Message}"); }
            _factsCache[eid] = facts;
            return facts;
        }

        /// <summary>
        /// A4: Live-path mirror of ClashRunCommand.ReadSystem. Kept private and
        /// duplicated rather than reaching across the namespace boundary into
        /// the headless command — the helpers are tiny and the dependency
        /// direction would otherwise force ClashRunCommand to carry a public
        /// surface for ClashSession to consume.
        /// </summary>
        private static string ReadElementSystem(Element el)
        {
            if (el == null) return "";
            try
            {
                if (el is MEPCurve mc && mc.MEPSystem != null) return mc.MEPSystem.Name ?? "";
                var p = el.LookupParameter("System Name");
                if (p != null) return p.AsString() ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"ResolveFacts.ReadElementSystem({el?.Id}): {ex.Message}"); }
            return "";
        }

        private static string ReadElementWorkset(Document doc, Element el)
        {
            try
            {
                if (doc == null || el == null || !doc.IsWorkshared) return "";
                var ws = doc.GetWorksetTable().GetWorkset(el.WorksetId);
                return ws?.Name ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; }
        }

        /// <summary>
        /// rec-1: OBB-tree descent producing a ClashHit on first hit.
        ///
        /// H7: Iterative (explicit Stack) instead of recursive. Same motivation
        /// as ClashKernel.OverlapDescend — stack safety + ThreadPool-friendly
        /// (live-clash runs on Revit API thread whose WPF-derived stack can be
        /// smaller than a typical worker).
        /// </summary>
        private static ClashHit DescendTest(ClashMeshBuffer meshA, ObbNode rootA, ClashMeshBuffer meshB, ObbNode rootB)
        {
            if (rootA == null || rootB == null) return null;
            var stack = new Stack<(ObbNode, ObbNode)>();
            stack.Push((rootA, rootB));
            while (stack.Count > 0)
            {
                var (na, nb) = stack.Pop();
                if (na == null || nb == null) continue;
                if (na.AabbMax.X < nb.AabbMin.X || na.AabbMin.X > nb.AabbMax.X) continue;
                if (na.AabbMax.Y < nb.AabbMin.Y || na.AabbMin.Y > nb.AabbMax.Y) continue;
                if (na.AabbMax.Z < nb.AabbMin.Z || na.AabbMin.Z > nb.AabbMax.Z) continue;

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
                }
                else if (!na.IsLeaf)
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
            return null;
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
