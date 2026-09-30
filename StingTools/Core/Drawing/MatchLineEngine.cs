using StingTools.Core;
// StingTools — Drawing Template Manager · Phase 168 — Match-line subsystem
//
// Walks the project's scope-box adjacency graph and emits paired
// match-line annotations on every pair of views that share a scope-box
// face. Each pair gets:
//
//   1. A red dashed-with-tick DetailCurve in each of the two views,
//      drawn along the shared face. Line style: 'STING - Match Line'
//      (falls back to 'Medium Lines' when missing).
//   2. STING_MATCH_REF_TXT on each curve = the paired sheet's
//      STING_SHEET_FULL_REF (or sheet number when the full ref param
//      isn't bound) — re-resolves on sheet renumber.
//   3. STING_MATCH_LINE_GUID_TXT — stable pair identifier so re-runs
//      find the existing pair and update in place rather than
//      duplicating annotations.
//   4. STING_MATCH_DIR_TXT — "vertical" / "horizontal" / "dogleg".
//   5. Tip captions at each end of the line ("see {paired_ref} →") via
//      TextNote (preferred when the STING_TAG_MATCHLINE family is
//      loaded; falls back to project text-note type).
//
// Public surface:
//   * MatchLineEngine.Run(doc, opts)              — full sweep
//   * MatchLineEngine.RunForView(doc, view, opts) — single-view scope
//   * MatchLineEngine.Validate(doc, ...)          — read-only audit
//   * MatchLineEngine.Sync(doc, opts)             — re-apply drifted
//
// All API calls are wrapped in Transaction. Engine never opens its
// own TransactionGroup — callers (commands) do that so cancel rolls
// back the whole sweep. StingLog records every warning + error.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    public sealed class MatchLineRunResult
    {
        public int  ScopeBoxesScanned   { get; set; }
        public int  AdjacencyEdgesFound { get; set; }
        public int  PairsCreated        { get; set; }
        public int  PairsUpdated        { get; set; }
        public int  PairsSkipped        { get; set; }
        public int  TipCaptionsPlaced   { get; set; }
        public List<string> Warnings    { get; set; } = new List<string>();
        public List<string> Errors      { get; set; } = new List<string>();
        /// <summary>DTW-48: parameters already reported unwritable in this run (said once).</summary>
        internal HashSet<string> UnwritableParams { get; } = new HashSet<string>(StringComparer.Ordinal);
    }

    public sealed class MatchLineRunOptions
    {
        /// <summary>When set, restricts the sweep to scope boxes whose
        /// `STING_VIEW_CONTEXT_TAG_TXT` (or scope-box name) matches
        /// the discipline filter. Null = all disciplines.</summary>
        public string DisciplineFilter { get; set; }

        /// <summary>When true (default), removes match lines whose pair
        /// is no longer valid (scope box deleted, view deleted, sheet
        /// removed). When false, leaves dangling annotations alone —
        /// useful for diagnostic runs.</summary>
        public bool PruneOrphans { get; set; } = true;

        /// <summary>When true, even already-current pairs are
        /// re-stamped (forces pair-GUID + ref refresh). Default false.</summary>
        public bool ForceRestamp { get; set; } = false;
    }

    public sealed class ScopeBoxAdjacency
    {
        public Element  ScopeBoxA   { get; set; }
        public Element  ScopeBoxB   { get; set; }
        public XYZ      LineStart   { get; set; }   // shared edge endpoints in project coords (first segment)
        public XYZ      LineEnd     { get; set; }
        public string   Direction   { get; set; }   // "vertical" / "horizontal" / "dogleg"
        public string   PairGuid    { get; set; }   // deterministic, derived from sorted scope-box ids

        /// <summary>Phase 169 — for dog-leg pairs, every segment of the
        /// shared boundary in (start, end) form. Single-face pairs have
        /// exactly one segment matching (LineStart, LineEnd).</summary>
        public List<(XYZ Start, XYZ End)> Segments { get; set; }
            = new List<(XYZ, XYZ)>();

        public string Key => string.Compare(
            ScopeBoxA?.UniqueId ?? "", ScopeBoxB?.UniqueId ?? "",
            StringComparison.Ordinal) < 0
            ? $"{ScopeBoxA?.UniqueId}|{ScopeBoxB?.UniqueId}"
            : $"{ScopeBoxB?.UniqueId}|{ScopeBoxA?.UniqueId}";
    }

    public static class MatchLineEngine
    {
        // Revit internal length unit is feet; the config talks in mm.
        private const double MmPerFoot = 304.8;
        private static double MmToFt(double mm) => mm / MmPerFoot;

        // ── Public entry points ──────────────────────────────────────────

        public static MatchLineRunResult Run(Document doc, MatchLineRunOptions opts = null)
        {
            var r = new MatchLineRunResult();
            if (doc == null) { r.Errors.Add("doc is null"); return r; }
            opts = opts ?? new MatchLineRunOptions();
            var cfg = MatchLineConfigRegistry.Get(doc);

            try
            {
                var scopeBoxes = CollectScopeBoxes(doc, opts.DisciplineFilter);
                r.ScopeBoxesScanned = scopeBoxes.Count;
                if (scopeBoxes.Count < 2) return r;

                var edges = ComputeAdjacency(scopeBoxes, cfg);
                r.AdjacencyEdgesFound = edges.Count;
                if (edges.Count == 0) return r;

                // Phase 169 — group multi-segment edges per scope-box pair
                // so dog-legs (multiple shared faces) collapse to one logical
                // pair with a list of segments. Single-face pairs unaffected.
                var groupedEdges = GroupAdjacenciesByPair(edges);
                r.AdjacencyEdgesFound = groupedEdges.Count;

                var viewByScope = BuildViewByScopeIndex(doc);
                var existingByGuid = BuildExistingPairIndex(doc);
                // One viewport pass, one line-style / note-type resolve and one
                // caption collection per view for the whole sweep -- these were
                // re-collected per view pair and per segment.
                var cache = new SweepCache(doc, cfg);

                using (var tx = new Transaction(doc, "STING Match-Line sweep"))
                {
                    tx.Start();
                    foreach (var edge in groupedEdges)
                    {
                        try
                        {
                            PlaceOrUpdatePair(doc, edge, cfg, viewByScope,
                                              existingByGuid, opts, r, cache);
                        }
                        catch (Exception ex)
                        {
                            r.Errors.Add($"pair {edge.Key}: {ex.Message}");
                            StingLog.Error($"MatchLineEngine pair {edge.Key}", ex);
                        }
                    }
                    if (opts.PruneOrphans)
                        PruneOrphans(doc, groupedEdges, existingByGuid, r);
                    // A commit a failure handler rolls back placed nothing: say so, so the
                    // run's counts are not read as match lines in the model.
                    var status = tx.Commit();
                    if (status != TransactionStatus.Committed)
                        r.Errors.Add($"the match-line transaction did not commit ({status}); nothing placed or updated was kept.");
                }
            }
            catch (Exception ex)
            {
                r.Errors.Add(ex.Message);
                StingLog.Error("MatchLineEngine.Run", ex);
            }
            return r;
        }

        public static MatchLineRunResult Sync(Document doc, MatchLineRunOptions opts = null)
        {
            opts = opts ?? new MatchLineRunOptions();
            opts.ForceRestamp = true;
            return Run(doc, opts);
        }

        // ── Scope-box discovery ──────────────────────────────────────────

        private static List<Element> CollectScopeBoxes(Document doc, string disciplineFilter)
        {
            var list = new List<Element>();
            try
            {
                foreach (var el in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                    .WhereElementIsNotElementType())
                {
                    // STING-LOC:: (a building footprint that sets the LOC token) and
                    // STING-SEED:: (a size to copy) are never drawn on a sheet. Pairing
                    // them put match lines from a building's whole footprint across
                    // every area plan inside it.
                    if (!MatchLineGeometry.IsMatchLineBox(el.Name)) continue;

                    // Optional discipline filter — match against the scope-
                    // box name prefix (e.g. arch- / struct- / mep-) which
                    // is the convention from the Week 5 scope-box auto-binder.
                    if (!string.IsNullOrEmpty(disciplineFilter))
                    {
                        var name = el.Name ?? "";
                        if (name.IndexOf("STING::", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            // Look at the drawing-type id for discipline prefix
                            var parts = name.Split(new[] { "::" }, StringSplitOptions.None);
                            if (parts.Length >= 2 &&
                                !parts[1].StartsWith(disciplineFilter, StringComparison.OrdinalIgnoreCase))
                                continue;
                        }
                    }
                    list.Add(el);
                }
            }
            catch (Exception ex) { StingLog.Warn($"CollectScopeBoxes: {ex.Message}"); }
            return list;
        }

        // ── Adjacency detection ──────────────────────────────────────────
        //
        // For two AABB scope boxes A, B, an adjacency exists when ONE of
        // these holds within tolerance:
        //   A.Max.X ≈ B.Min.X (A east face = B west face)
        //   A.Min.X ≈ B.Max.X (A west = B east)
        //   A.Max.Y ≈ B.Min.Y (A north = B south)
        //   A.Min.Y ≈ B.Max.Y (A south = B north)
        // AND the perpendicular axis spans overlap by at least
        // adjacency.minOverlapMm.
        //
        // Z is ignored (scope boxes typically span all levels). Direction
        // = "vertical" for X-aligned shared face, "horizontal" for
        // Y-aligned. Dog-leg detection (multiple shared faces between the
        // same pair) is reserved for Phase II — for now multiple edges
        // between the same pair are emitted as separate entries.

        //
        // The test above was the original one, on each box's AABB with 1 mm face
        // coincidence. It never matched the Scope Box Planner's area boxes, which
        // overlap by 2 m and may be turned to the grid (a turned box's AABB is not
        // the box). The decision now lives in MatchLineGeometry (Revit-free, tested):
        // each box is measured in its own frame, the pair must share a frame, touch
        // or overlap along one axis and share an edge along the other, and the line
        // sits on the face or down the middle of the overlap strip.

        private static List<ScopeBoxAdjacency> ComputeAdjacency(
            List<Element> scopeBoxes, MatchLineConfig cfg)
        {
            var edges = new List<ScopeBoxAdjacency>();
            double tolFt    = MmToFt(cfg.Adjacency.CoplanarToleranceMm);
            double minOverlapFt = MmToFt(cfg.Adjacency.MinOverlapMm);

            // Measure each box once, in its own frame.
            var measured = new List<(Element Box, MatchLineRect Rect, double ZMin)>();
            foreach (var box in scopeBoxes)
            {
                if (!ScopeBoxRevit.TryMeasure(box, out var m, out var why))
                {
                    StingLog.Warn($"MatchLineEngine: scope box '{box?.Name}' skipped — {why}");
                    continue;
                }
                measured.Add((box, new MatchLineRect(m.Centre.X, m.Centre.Y,
                    m.WidthM * ScopeBoxRevit.FeetPerMetre, m.DepthM * ScopeBoxRevit.FeetPerMetre,
                    m.AngleRad), m.ZMinFt));
            }

            for (int i = 0; i < measured.Count; i++)
            {
                for (int j = i + 1; j < measured.Count; j++)
                {
                    var a = measured[i];
                    var b = measured[j];
                    var seg = MatchLineGeometry.Find(a.Rect, b.Rect, tolFt, minOverlapFt);
                    if (seg == null) continue;
                    double zMin = Math.Min(a.ZMin, b.ZMin);
                    var s = new XYZ(seg.X0, seg.Y0, zMin);
                    var e = new XYZ(seg.X1, seg.Y1, zMin);
                    var adj = new ScopeBoxAdjacency
                    {
                        ScopeBoxA = a.Box, ScopeBoxB = b.Box,
                        LineStart = s, LineEnd = e,
                        Direction = seg.Direction,
                        PairGuid  = DerivePairGuid(a.Box, b.Box),
                    };
                    adj.Segments.Add((s, e));
                    edges.Add(adj);
                }
            }
            return edges;
        }

        private static string DerivePairGuid(Element a, Element b)
        {
            // Deterministic — same pair always yields the same GUID,
            // regardless of which scope box came first in the iteration.
            // Hashing rather than concat keeps the param value short.
            var ua = a?.UniqueId ?? "";
            var ub = b?.UniqueId ?? "";
            var ordered = string.Compare(ua, ub, StringComparison.Ordinal) < 0
                ? ua + "|" + ub
                : ub + "|" + ua;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(ordered));
                // First 16 bytes formatted as a GUID string.
                var guid = new Guid(new ArraySegment<byte>(bytes, 0, 16).ToArray());
                return guid.ToString("D").ToUpperInvariant();
            }
        }

        /// <summary>Phase 169 — groups multiple adjacency records that
        /// share the same scope-box pair into a single record carrying
        /// a `Segments` list. Direction flips to "dogleg" when ≥ 2
        /// segments. Single-face pairs pass through unchanged.</summary>
        private static List<ScopeBoxAdjacency> GroupAdjacenciesByPair(
            List<ScopeBoxAdjacency> raw)
        {
            var byPair = new Dictionary<string, ScopeBoxAdjacency>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var e in raw)
            {
                if (!byPair.TryGetValue(e.PairGuid, out var existing))
                {
                    byPair[e.PairGuid] = e;
                    continue;
                }
                // Merge: append this edge's segments to the existing
                // record. If both directions appear, flag as dogleg.
                existing.Segments.AddRange(e.Segments);
                if (!string.Equals(existing.Direction, e.Direction,
                    StringComparison.OrdinalIgnoreCase))
                    existing.Direction = "dogleg";
                else if (existing.Segments.Count >= 2 &&
                    string.Equals(existing.Direction, e.Direction,
                        StringComparison.OrdinalIgnoreCase))
                    existing.Direction = "dogleg";  // multiple parallel faces — also dogleg
            }
            return byPair.Values.ToList();
        }

        // ── View / sheet resolution ──────────────────────────────────────

        /// <summary>Builds an index from scope-box element id → views
        /// that use that scope box as their crop region. A single
        /// scope box can drive multiple views (one per level).</summary>
        private static Dictionary<long, List<View>> BuildViewByScopeIndex(Document doc)
        {
            var idx = new Dictionary<long, List<View>>();
            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(View)))
                {
                    if (!(el is View v) || v.IsTemplate) continue;
                    // Plans only. DTW-52: a section or 3D view is now produced FROM a scope
                    // box (cut through it / boxed by it); it does not continue onto the next
                    // box's sheet, and a plan-shaped match line drawn in it is nonsense.
                    if (v.ViewType != ViewType.FloorPlan
                        && v.ViewType != ViewType.CeilingPlan
                        && v.ViewType != ViewType.AreaPlan
                        && v.ViewType != ViewType.EngineeringPlan)
                        continue;
                    var p = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                    if (p == null) continue;
                    var sbId = p.AsElementId();
                    if (sbId == null || sbId == ElementId.InvalidElementId) continue;
                    if (!idx.TryGetValue(sbId.Value, out var list))
                        idx[sbId.Value] = list = new List<View>();
                    list.Add(v);
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildViewByScopeIndex: {ex.Message}"); }
            return idx;
        }

        /// <summary>Sheet reference for every placed view, from ONE viewport pass:
        /// view id -> the hosting sheet's number, falling back to
        /// PRJ_SHEET_FULL_REF_TXT when the sheet has no number. A view with no
        /// viewport is absent (callers read that as ""). The first viewport found
        /// for a view wins, as the per-view scan this replaces did.</summary>
        private static Dictionary<long, string> BuildSheetRefIndex(Document doc)
        {
            var idx = new Dictionary<long, string>();
            try
            {
                foreach (var vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)))
                {
                    if (!(vp is Viewport viewport)) continue;
                    long viewId = viewport.ViewId.Value;
                    if (idx.ContainsKey(viewId)) continue;
                    if (!(doc.GetElement(viewport.SheetId) is ViewSheet sheet)) continue;
                    idx[viewId] = SheetRefOf(sheet);
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildSheetRefIndex: {ex.Message}"); }
            return idx;
        }

        /// <summary>The sheet NUMBER first, not the full reference.
        ///
        /// This is an ANNOTATION on a drawing -- "continued on sheet X" --
        /// and the standard this project works to is a full ISO identifier
        /// in the sheet-number field and the SHORTEST usable form in
        /// annotations, because a seven-field identifier inside a match-line
        /// note is unreadable at any sheet scale. It is the same decision
        /// already made for elevation, section and callout tags.
        ///
        /// PRJ_SHEET_FULL_REF_TXT stays as the fallback for a sheet with no
        /// number at all, which is the only case where it is the better of
        /// the two.</summary>
        private static string SheetRefOf(ViewSheet sheet)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(sheet.SheetNumber)) return sheet.SheetNumber;
                var pFull = sheet.LookupParameter("PRJ_SHEET_FULL_REF_TXT");
                if (pFull != null && pFull.HasValue)
                {
                    var v = pFull.AsString();
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
            catch (Exception ex) { StingLog.Warn($"SheetRefOf: {ex.Message}"); }
            return "";
        }

        /// <summary>
        /// A-6: collapse a stamped match-line key to its pair key.
        ///
        /// PlaceCurve stamps dog-leg segments as
        /// "&lt;scopePairGuid&gt;:&lt;viewA&gt;:&lt;viewB&gt;:segN" but
        /// PlaceOrUpdatePair looks the pair up by the bare
        /// "&lt;scopePairGuid&gt;:&lt;viewA&gt;:&lt;viewB&gt;". Indexing the
        /// stamped key verbatim therefore never matched for any multi-segment
        /// boundary: `existed` came back false every sweep, nothing was
        /// deleted, and the curves duplicated — directly contradicting the
        /// file header's "re-runs find the existing pair and update in place".
        /// Grouping every segment under the pair key makes the lookup hit and
        /// the delete-then-replace path remove all segments together.
        ///
        /// Only a trailing ":segN" is stripped — the pair key itself contains
        /// colons, so splitting on the first one would be wrong here.
        /// PruneOrphans keeps working unchanged: it takes the scope-pair GUID
        /// from the first colon-delimited field, which is identical in both
        /// the stamped and the collapsed key.
        /// </summary>
        private static string BasePairKey(string stampedKey)
        {
            if (string.IsNullOrEmpty(stampedKey)) return stampedKey;
            int i = stampedKey.LastIndexOf(":seg", StringComparison.OrdinalIgnoreCase);
            if (i > 0 && int.TryParse(stampedKey.Substring(i + 4), out _))
                return stampedKey.Substring(0, i);
            return stampedKey;
        }

        /// <summary>Indexes existing match-line DetailCurves by their
        /// STING_MATCH_LINE_GUID stamp so re-runs can find them in
        /// O(1) and update in place.</summary>
        private static Dictionary<string, List<CurveElement>> BuildExistingPairIndex(Document doc)
        {
            var idx = new Dictionary<string, List<CurveElement>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // Detail curves live in the Lines category; every other curve
                // element (sketch lines, room/area separation lines, ...) is
                // skipped by the collector instead of paying a LookupParameter
                // each. When the stamp parameter is bound under exactly one shared
                // definition, curves without a value are filtered out natively
                // too. The per-element checks below stay, so the filter only
                // ever narrows the scan, never decides.
                var collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(CurveElement))
                    .OfCategory(BuiltInCategory.OST_Lines);
                var stampFilter = MatchStampHasValueFilter(doc);
                if (stampFilter != null) collector = collector.WherePasses(stampFilter);
                foreach (var el in collector)
                {
                    if (!(el is DetailCurve dc)) continue;
                    var p = dc.LookupParameter(ParamRegistry.MATCH_LINE_GUID);
                    if (p == null || !p.HasValue) continue;
                    var key = BasePairKey(p.AsString());
                    if (string.IsNullOrEmpty(key)) continue;
                    if (!idx.TryGetValue(key, out var list))
                        idx[key] = list = new List<CurveElement>();
                    list.Add(dc);
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildExistingPairIndex: {ex.Message}"); }
            return idx;
        }

        /// <summary>A native "has a value" filter on STING_MATCH_LINE_GUID_TXT, or
        /// null when the parameter is not bound under exactly one shared definition
        /// of that name (then LookupParameter's by-name answer is the only safe
        /// test, and the caller scans the whole category).</summary>
        private static ElementFilter MatchStampHasValueFilter(Document doc)
        {
            try
            {
                ElementId found = null;
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(SharedParameterElement)))
                {
                    if (!(el is SharedParameterElement sp)) continue;
                    if (!string.Equals(sp.Name, ParamRegistry.MATCH_LINE_GUID, StringComparison.Ordinal)) continue;
                    if (found != null) return null;   // two definitions share the name
                    found = sp.Id;
                }
                if (found == null) return null;
                return new ElementParameterFilter(
                    ParameterFilterRuleFactory.CreateHasValueParameterRule(found));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MatchLineEngine stamp filter: {ex.Message}");
                return null;
            }
        }

        // ── Pair placement ───────────────────────────────────────────────

        private static void PlaceOrUpdatePair(Document doc, ScopeBoxAdjacency edge,
            MatchLineConfig cfg, Dictionary<long, List<View>> viewByScope,
            Dictionary<string, List<CurveElement>> existingByGuid,
            MatchLineRunOptions opts, MatchLineRunResult r, SweepCache cache)
        {
            // Resolve a representative view per side (first view bound to
            // the scope box; multi-level pairs get one match line per
            // (level, side) which the drift detector tracks separately).
            if (!viewByScope.TryGetValue(edge.ScopeBoxA.Id.Value, out var viewsA) || viewsA.Count == 0)
            { r.Warnings.Add($"pair {edge.PairGuid}: no view for scope box A '{edge.ScopeBoxA.Name}'"); r.PairsSkipped++; return; }
            if (!viewByScope.TryGetValue(edge.ScopeBoxB.Id.Value, out var viewsB) || viewsB.Count == 0)
            { r.Warnings.Add($"pair {edge.PairGuid}: no view for scope box B '{edge.ScopeBoxB.Name}'"); r.PairsSkipped++; return; }

            // For each (viewA, viewB) pair at the same level, place the
            // match line. When views are at different levels we still
            // pair them — the line geometry is taken from the shared face
            // projected onto the view plane.
            // Only the same drawing on the same level continues across a match line.
            // Every view on box A used to pair with every view on box B, so a box
            // carrying M, E and P plans drew nine cross-discipline match lines per
            // edge — an M plan saying "continued on" an E sheet. The level test is
            // no longer optional (adjacency.considerLevelMatch): a level-1 plan
            // never continues on a level-2 sheet. See MatchLineGeometry.ShouldPairViews.
            foreach (var viewA in viewsA)
            foreach (var viewB in viewsB)
            {
                if (!MatchLineGeometry.ShouldPairViews(
                        ParameterHelpers.GetString(viewA, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID),
                        ParameterHelpers.GetString(viewB, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID),
                        LevelKey(viewA), LevelKey(viewB),
                        FallbackPairKey(viewA), FallbackPairKey(viewB)))
                    continue;

                var refA = cache.SheetRef(viewA);
                var refB = cache.SheetRef(viewB);

                // Per-(view, view) pair guid combines the scope-pair guid
                // with view ids so each level/instance gets its own
                // stamp — drift can flag one view's match line stale
                // without affecting the rest.
                string viewPairGuid = $"{edge.PairGuid}:{viewA.UniqueId}:{viewB.UniqueId}";

                bool existed = existingByGuid.TryGetValue(viewPairGuid, out var existing);
                if (existed && !opts.ForceRestamp)
                {
                    // Verify ref still matches; if it does, no-op.
                    bool refsCurrent = AllRefsMatch(existing, refA, refB);
                    if (refsCurrent) { r.PairsSkipped++; continue; }
                }

                // Strip any prior pair (idempotent re-apply).
                if (existed)
                    foreach (var dc in existing)
                        try { doc.Delete(dc.Id); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

                // Place the curve in viewA referencing refB, and the
                // curve in viewB referencing refA.
                PlaceCurve(doc, viewA, edge, cfg, viewPairGuid, refB, r, cache);
                PlaceCurve(doc, viewB, edge, cfg, viewPairGuid, refA, r, cache);
                if (cache.GuidUnstampable) return;   // DTW-48: nothing was placed; the error says why

                if (existed) r.PairsUpdated++;
                else         r.PairsCreated++;
            }
        }

        private static string LevelKey(View v)
        {
            try
            {
                var id = v?.GenLevel?.Id;
                return id == null || id == ElementId.InvalidElementId ? null : id.Value.ToString();
            }
            catch (Exception ex) { StingLog.Warn($"MatchLineEngine.LevelKey: {ex.Message}"); return null; }
        }

        /// <summary>What stands in for the drawing when a view carries no drawing-type
        /// stamp: its view type and view template.</summary>
        private static string FallbackPairKey(View v)
        {
            try { return $"{v.ViewType}|{v.ViewTemplateId?.Value ?? -1}"; }
            catch (Exception ex) { StingLog.Warn($"MatchLineEngine.FallbackPairKey: {ex.Message}"); return null; }
        }

        private static bool AllRefsMatch(List<CurveElement> existing, string refA, string refB)
        {
            if (existing == null || existing.Count == 0) return false;
            // Two-sided pair — each side's curve carries its OPPOSITE
            // sheet's ref. The set of curve refs must equal {refA, refB}.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dc in existing)
            {
                var p = dc.LookupParameter(ParamRegistry.MATCH_REF);
                if (p != null && p.HasValue) seen.Add(p.AsString() ?? "");
            }
            return seen.Contains(refA ?? "") && seen.Contains(refB ?? "");
        }

        private static void PlaceCurve(Document doc, View view, ScopeBoxAdjacency edge,
            MatchLineConfig cfg, string viewPairGuid, string pairedRef,
            MatchLineRunResult r, SweepCache cache)
        {
            // Phase 169 — for dog-leg pairs, draw every segment of the
            // shared boundary; single-face pairs degenerate to one
            // segment matching the legacy LineStart/LineEnd.
            var segments = (edge.Segments != null && edge.Segments.Count > 0)
                ? edge.Segments
                : new List<(XYZ Start, XYZ End)> { (edge.LineStart, edge.LineEnd) };
            for (int s = 0; s < segments.Count; s++)
            {
                var seg = segments[s];
                // Per-segment GUID suffix so dog-legs get one stamped
                // GUID per segment (drift detection keeps each piece
                // independently up-to-date).
                string segGuid = segments.Count == 1
                    ? viewPairGuid
                    : $"{viewPairGuid}:seg{s + 1}";
                PlaceCurveSegment(doc, view, edge, cfg, segGuid, pairedRef,
                                  seg.Start, seg.End, r, cache);
            }
        }

        private static void PlaceCurveSegment(Document doc, View view, ScopeBoxAdjacency edge,
            MatchLineConfig cfg, string viewPairGuid, string pairedRef,
            XYZ segStart, XYZ segEnd, MatchLineRunResult r, SweepCache cache)
        {
            try
            {
                // Project the line onto the view plane. For plans
                // (FloorPlan / CeilingPlan / AreaPlan) we drop Z.
                XYZ a, b;
                if (view.ViewType == ViewType.FloorPlan
                    || view.ViewType == ViewType.CeilingPlan
                    || view.ViewType == ViewType.AreaPlan
                    || view.ViewType == ViewType.EngineeringPlan)
                {
                    double z = view.GenLevel?.Elevation ?? 0.0;
                    a = new XYZ(segStart.X, segStart.Y, z);
                    b = new XYZ(segEnd.X,   segEnd.Y,   z);
                }
                else
                {
                    a = segStart; b = segEnd;
                }

                // Optional extension beyond the crop edge so the line
                // visually breaks the drawable zone instead of stopping
                // exactly at the boundary.
                double extFt = MmToFt(cfg.Geometry.ExtendBeyondCropMm);
                if (extFt > 1e-6)
                {
                    var dir = (b - a).Normalize();
                    a = a - dir * extFt;
                    b = b + dir * extFt;
                }

                // DTW-48: once a curve has refused the pair stamp, place no more.
                if (cache.GuidUnstampable) return;

                var line = Line.CreateBound(a, b);
                var dc = doc.Create.NewDetailCurve(view, line);

                // DTW-48: the pair GUID is what lets the next run find this curve. A curve
                // that cannot carry it would be re-added on every run, so it is removed and
                // the sweep stops placing, with one error that says why.
                if (cfg.Stamping.WritePairGuid)
                {
                    if (!TrySet(dc, ParamRegistry.MATCH_LINE_GUID, viewPairGuid, r))
                    {
                        try { doc.Delete(dc.Id); }
                        catch (Exception ex) { StingLog.Warn($"MatchLine: removing unstampable curve: {ex.Message}"); }
                        cache.GuidUnstampable = true;
                        r.Errors.Add($"{ParamRegistry.MATCH_LINE_GUID} could not be written on a detail line, so match lines were not placed "
                                   + "(an unstamped line is invisible to the next run, which would add another). "
                                   + "Run Load Shared Params to bind it to Lines, then run match lines again.");
                        return;
                    }
                }
                else if (!cache.GuidOffWarned)
                {
                    cache.GuidOffWarned = true;
                    r.Warnings.Add("Match-line config has stamping.writePairGuid off: the lines placed now cannot be found by the next run, "
                                 + "so every re-run adds another set. Turn it on to make match lines idempotent.");
                }

                // Apply line style.
                var styleId = cache.LineStyleId;
                if (styleId != null && styleId != ElementId.InvalidElementId)
                {
                    try { dc.LineStyle = doc.GetElement(styleId); }
                    catch (Exception ex) { r.Warnings.Add($"line style apply: {ex.Message}"); }
                }

                // Stamp parameters (skip silently when binding missing —
                // pre-flight check should have warned).
                if (cfg.Stamping.WritePairedRef)
                    TrySet(dc, ParamRegistry.MATCH_REF, pairedRef, r);
                if (cfg.Stamping.WriteDirection)
                    TrySet(dc, ParamRegistry.MATCH_DIR, edge.Direction, r);

                // Phase 169 — discipline tint via per-element
                // OverrideGraphicSettings. The view-style-pack default
                // colour is overridden per-curve when the discipline
                // colour map is enabled and the scope-box name encodes a
                // discipline prefix (Week 5 binder convention:
                // STING::<dt-id>::… where the dt-id starts with disc-).
                if (cfg.Discipline?.TintByDiscipline == true)
                    ApplyDisciplineTint(doc, view, dc, edge, cfg);

                // Tip captions (TextNote — fallback path; tag-family
                // path with STING_TAG_MATCHLINE is a Phase II refinement).
                if (!string.IsNullOrEmpty(pairedRef) &&
                    !string.IsNullOrEmpty(cfg.Captions.TipFormat))
                {
                    string caption = cfg.Captions.TipFormat.Replace("{paired_ref}", pairedRef);
                    var noteTypeId = cache.NoteTypeId;
                    if (noteTypeId != null && noteTypeId != ElementId.InvalidElementId)
                    {
                        bool bothEnds = string.Equals(cfg.Captions.TipPlacement, "BothEnds", StringComparison.OrdinalIgnoreCase);
                        var points = bothEnds
                            ? new List<XYZ> { a, b }
                            : new List<XYZ> { (a + b) / 2 };

                        // A-7: the pair's curves are deleted and re-placed on
                        // every update, but captions were only ever created —
                        // so MatchLine_Sync (recommended after every renumber)
                        // stacked another "see XXX" note per end, per run.
                        // Clear the ones this placement is about to replace
                        // first. Matched on view + note type + caption template
                        // shape + proximity to the placement point, so a user's
                        // own annotation and a different pair's caption are
                        // both left alone.
                        // Stamped captions of THIS pair go first, wherever they now sit —
                        // a boundary that moved no longer strands its old caption. The
                        // shape + proximity sweep remains for captions placed before
                        // stamping existed.
                        var captions = cache.CaptionsIn(view, r);
                        RemoveStampedCaptions(doc, view, captions, viewPairGuid, r);
                        RemoveExistingCaptions(doc, view, captions, noteTypeId, cfg.Captions.TipFormat, points, r);

                        for (int i = 0; i < points.Count; i++)
                        {
                            try
                            {
                                var tn = TextNote.Create(doc, view.Id, points[i], caption, noteTypeId);
                                r.TipCaptionsPlaced++;
                                Storage.StingAnnotationProvenanceSchema.Stamp(tn, AnnotationProvenance.MatchCaption,
                                    AnnotationProvenance.Key(viewPairGuid, i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                                // Keep the view's caption snapshot current, so a later
                                // segment in this view sees the note exactly as a fresh
                                // collector would have.
                                captions.Added(tn,
                                    Storage.StingAnnotationProvenanceSchema.Read(tn) != null ? viewPairGuid : null);
                            }
                            catch (Exception ex) { StingLog.Warn($"Caption create: {ex.Message}"); }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                r.Errors.Add($"PlaceCurve: {ex.Message}");
                StingLog.Error("MatchLineEngine.PlaceCurve", ex);
            }
        }

        /// <summary>
        /// DTW-48: write a text stamp and say whether it took. It swallowed every failure in
        /// an empty catch and did nothing when the parameter was unbound, so a missing
        /// binding looked like success. An unbound or read-only parameter is reported once.
        /// </summary>
        private static bool TrySet(Element el, string paramName, string value, MatchLineRunResult r = null)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.String)
                {
                    if (r == null || r.UnwritableParams.Add(paramName))
                    {
                        var why = p == null ? "not bound to Lines" : p.IsReadOnly ? "read-only" : $"a {p.StorageType} parameter, not text";
                        StingLog.Warn($"MatchLineEngine: {paramName} is {why}; match lines cannot carry it.");
                        r?.Warnings.Add($"{paramName} is {why} — match lines cannot carry it. Run Load Shared Params.");
                    }
                    return false;
                }
                return p.Set(value ?? "");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MatchLineEngine: writing {paramName}: {ex.Message}");
                r?.Warnings.Add($"Writing {paramName} on a match line failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Phase 169 — sets a per-element OverrideGraphicSettings
        /// on the placed curve, projection-line colour driven by the
        /// scope-box's discipline prefix and the colour map in
        /// MatchLineConfig.Discipline.ColorMap. Scope-box name encoding:
        /// STING::&lt;disc&gt;-&lt;rest&gt;::… (Week 5 binder convention).</summary>
        private static void ApplyDisciplineTint(Document doc, View view,
            DetailCurve dc, ScopeBoxAdjacency edge, MatchLineConfig cfg)
        {
            try
            {
                if (cfg?.Discipline?.ColorMap == null
                    || cfg.Discipline.ColorMap.Count == 0) return;

                string disc = ExtractDisciplineCode(edge.ScopeBoxA?.Name)
                              ?? ExtractDisciplineCode(edge.ScopeBoxB?.Name);
                if (string.IsNullOrEmpty(disc)) return;
                if (!cfg.Discipline.ColorMap.TryGetValue(disc, out var hex)
                    || string.IsNullOrEmpty(hex)) return;

                if (!TryParseHex(hex, out byte rr, out byte gg, out byte bb)) return;

                var ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(new Color(rr, gg, bb));
                view.SetElementOverrides(dc.Id, ogs);
            }
            catch (Exception ex) { StingLog.Warn($"ApplyDisciplineTint: {ex.Message}"); }
        }

        private static string ExtractDisciplineCode(string scopeBoxName)
        {
            if (string.IsNullOrEmpty(scopeBoxName)) return null;
            // Strip the STING:: prefix if present (Week 5 binder convention).
            var work = scopeBoxName;
            const string p = "STING::";
            if (work.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                work = work.Substring(p.Length);
            // Drawing-type id starts the segment after the prefix.
            int sep = work.IndexOf("::", StringComparison.Ordinal);
            if (sep > 0) work = work.Substring(0, sep);
            // Common drawing-type id prefixes: arch- / struct- / mep- /
            // elec- / plumb- / fp- / pres- / clar- / coord- / fab- ...
            // Map them to ISO 19650 single-letter discipline codes that
            // the colour map expects.
            string lower = work.ToLowerInvariant();
            if (lower.StartsWith("arch")  || lower.StartsWith("a-")) return "A";
            if (lower.StartsWith("struct")|| lower.StartsWith("s-")) return "S";
            if (lower.StartsWith("mep")   || lower.StartsWith("hvac")
                                          || lower.StartsWith("m-"))  return "M";
            if (lower.StartsWith("elec")  || lower.StartsWith("e-")) return "E";
            if (lower.StartsWith("plumb") || lower.StartsWith("p-")) return "P";
            if (lower.StartsWith("fp")    || lower.StartsWith("fire"))return "FP";
            if (lower.StartsWith("comm")  || lower.StartsWith("lv"))  return "LV";
            if (lower.StartsWith("site")  || lower.StartsWith("land"))return "G";
            // Two-character codes already in ISO form (rare).
            if (lower.Length >= 2 && lower[1] == '-')
                return lower.Substring(0, 1).ToUpperInvariant();
            return null;
        }

        private static bool TryParseHex(string hex, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (string.IsNullOrEmpty(hex)) return false;
            var s = hex.TrimStart('#');
            if (s.Length != 6) return false;
            try
            {
                r = Convert.ToByte(s.Substring(0, 2), 16);
                g = Convert.ToByte(s.Substring(2, 2), 16);
                b = Convert.ToByte(s.Substring(4, 2), 16);
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }

        private static ElementId ResolveLineStyleId(Document doc, string styleName)
        {
            if (string.IsNullOrEmpty(styleName)) return null;
            try
            {
                var lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                if (lines == null) return null;
                foreach (Category sub in lines.SubCategories)
                {
                    if (string.Equals(sub.Name, styleName, StringComparison.OrdinalIgnoreCase))
                        return sub.GetGraphicsStyle(GraphicsStyleType.Projection)?.Id ?? sub.Id;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveLineStyleId '{styleName}': {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// A-7 helper: delete the caption notes this placement is replacing.
        ///
        /// Captions cannot carry the pair stamp the curves use —
        /// STING_MATCH_LINE_GUID_TXT is a group-27 parameter bound through the
        /// universal category set, which covers Detail Items but not Text
        /// Notes, so the write would silently no-op. Rather than provision a
        /// new binding, identity here is (view + note type + exact caption text
        /// + proximity to the point being written). That is precise enough to
        /// leave a user's own note and a different pair's caption untouched.
        ///
        /// Matching is on the caption TEMPLATE's shape, not the rendered text.
        /// The caption embeds the target sheet number, so after a renumber the
        /// text this sweep writes differs from the text already on the view —
        /// exact-text matching would miss every stale caption and orphan one
        /// per end, per renumber. That is the flagship case for this code:
        /// MatchLine_Sync is the recommended follow-up to a renumber.
        ///
        /// Residual: if the boundary geometry moved since the last sweep, the
        /// old caption sits outside the tolerance and is left behind as an
        /// orphan rather than deleted. That is strictly better than the
        /// previous behaviour, which orphaned one on EVERY sweep regardless.
        /// </summary>
        /// <summary>Delete the captions this segment stamped last time, by key —
        /// exact, and independent of where the boundary has moved to.</summary>
        private static void RemoveStampedCaptions(Document doc, View view, ViewCaptions captions,
            string viewPairGuid, MatchLineRunResult r)
        {
            try
            {
                foreach (var el in captions.StampedFor(viewPairGuid))
                {
                    try { doc.Delete(el.Id); captions.Deleted(el.Id); }
                    catch (Exception ex) { StingLog.Warn($"Stamped caption prune {el.Id}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { r?.Warnings.Add($"Could not clear stamped captions in '{view.Name}': {ex.Message}"); }
        }

        private static void RemoveExistingCaptions(Document doc, View view, ViewCaptions captions,
            ElementId noteTypeId, string tipFormat, IList<XYZ> points, MatchLineRunResult r)
        {
            if (doc == null || view == null || points == null || points.Count == 0) return;
            if (string.IsNullOrEmpty(tipFormat)) return;
            const double tolFt = 2.0;   // ~600 mm — captions sit at/near the point

            var shape = BuildCaptionMatcher(tipFormat);
            if (shape == null) return;

            try
            {
                var doomed = new List<ElementId>();
                foreach (var tn in captions.Live())
                {
                    if (tn.GetTypeId() != noteTypeId) continue;
                    var text = (tn.Text ?? "").TrimEnd('\r', '\n');
                    if (!shape.IsMatch(text)) continue;
                    XYZ c;
                    try { c = tn.Coord; } catch { continue; }
                    if (c == null) continue;
                    foreach (var p in points)
                    {
                        if (p != null && c.DistanceTo(p) <= tolFt) { doomed.Add(tn.Id); break; }
                    }
                }
                foreach (var id in doomed)
                {
                    try { doc.Delete(id); captions.Deleted(id); }
                    catch (Exception ex) { StingLog.Warn($"Caption prune {id}: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                // Fail open: a failed prune means a possible duplicate caption,
                // not a failed sweep.
                r?.Warnings.Add($"Could not clear prior captions in '{view.Name}': {ex.Message}");
            }
        }

        /// <summary>Everything one sweep resolves once instead of per view pair or
        /// per segment: the sheet reference of every placed view, the line style,
        /// the caption note type and, per view, its text notes.</summary>
        private sealed class SweepCache
        {
            private readonly Document _doc;
            private readonly MatchLineConfig _cfg;
            private Dictionary<long, string> _sheetRefs;
            private bool _lineStyleResolved, _noteTypeResolved;
            private ElementId _lineStyleId, _noteTypeId;
            private readonly Dictionary<long, ViewCaptions> _captions = new Dictionary<long, ViewCaptions>();

            public SweepCache(Document doc, MatchLineConfig cfg) { _doc = doc; _cfg = cfg; }

            /// <summary>DTW-48: set once a placed curve refused the pair-GUID stamp. The
            /// rest of the sweep places nothing — an unstamped curve is invisible to the
            /// next run, which would add another beside it.</summary>
            public bool GuidUnstampable { get; set; }
            /// <summary>DTW-48: "stamping switched off in the config" said once per sweep.</summary>
            public bool GuidOffWarned { get; set; }

            public string SheetRef(View view)
            {
                if (_sheetRefs == null) _sheetRefs = BuildSheetRefIndex(_doc);
                return view != null && _sheetRefs.TryGetValue(view.Id.Value, out var s) ? s : "";
            }

            public ElementId LineStyleId
            {
                get
                {
                    if (!_lineStyleResolved)
                    {
                        _lineStyleId = ResolveLineStyleId(_doc, _cfg.Geometry.LineStyleName)
                                    ?? ResolveLineStyleId(_doc, _cfg.Geometry.FallbackLineStyleName);
                        _lineStyleResolved = true;
                    }
                    return _lineStyleId;
                }
            }

            public ElementId NoteTypeId
            {
                get
                {
                    if (!_noteTypeResolved)
                    {
                        _noteTypeId = ResolveTextNoteTypeId(_doc, _cfg.Captions.FallbackTextNoteTypeName);
                        _noteTypeResolved = true;
                    }
                    return _noteTypeId;
                }
            }

            public ViewCaptions CaptionsIn(View view, MatchLineRunResult r)
            {
                if (!_captions.TryGetValue(view.Id.Value, out var vc))
                    _captions[view.Id.Value] = vc = ViewCaptions.Collect(_doc, view, r);
                return vc;
            }
        }

        /// <summary>One view's text notes, collected once per sweep and kept in step
        /// with what the sweep deletes and creates, so every segment sees the same
        /// set a fresh collector would return.</summary>
        private sealed class ViewCaptions
        {
            private readonly List<TextNote> _notes = new List<TextNote>();
            private readonly Dictionary<string, List<TextNote>> _stampedByHost
                = new Dictionary<string, List<TextNote>>(StringComparer.Ordinal);
            private readonly HashSet<long> _deleted = new HashSet<long>();

            public static ViewCaptions Collect(Document doc, View view, MatchLineRunResult r)
            {
                var vc = new ViewCaptions();
                try
                {
                    foreach (var el in new FilteredElementCollector(doc, view.Id).OfClass(typeof(TextNote)))
                        if (el is TextNote tn) vc._notes.Add(tn);
                }
                catch (Exception ex) { r?.Warnings.Add($"Could not read captions in '{view.Name}': {ex.Message}"); }
                try
                {
                    foreach (var kv in Storage.StingAnnotationProvenanceSchema.Index(doc, view, typeof(TextNote), AnnotationProvenance.MatchCaption))
                    {
                        var host = AnnotationProvenance.HostOf(kv.Key);
                        if (host == null) continue;
                        foreach (var el in kv.Value)
                            if (el is TextNote tn) vc.AddStamped(host, tn);
                    }
                }
                catch (Exception ex) { r?.Warnings.Add($"Could not clear stamped captions in '{view.Name}': {ex.Message}"); }
                return vc;
            }

            private void AddStamped(string host, TextNote tn)
            {
                if (!_stampedByHost.TryGetValue(host, out var list)) _stampedByHost[host] = list = new List<TextNote>();
                list.Add(tn);
            }

            public IEnumerable<TextNote> Live()
            {
                foreach (var tn in _notes)
                    if (!_deleted.Contains(tn.Id.Value)) yield return tn;
            }

            public List<TextNote> StampedFor(string host)
            {
                var result = new List<TextNote>();
                if (host != null && _stampedByHost.TryGetValue(host, out var list))
                    foreach (var tn in list)
                        if (!_deleted.Contains(tn.Id.Value)) result.Add(tn);
                return result;
            }

            public void Deleted(ElementId id) { if (id != null) _deleted.Add(id.Value); }

            public void Added(TextNote tn, string host)
            {
                if (tn == null) return;
                _notes.Add(tn);
                if (host != null) AddStamped(host, tn);
            }
        }

        // Cache: caption template -> compiled shape matcher. One config is
        // shared by an entire sweep, so this is built once per run in practice.
        private static readonly Dictionary<string, System.Text.RegularExpressions.Regex> _captionMatchers
            = new Dictionary<string, System.Text.RegularExpressions.Regex>(StringComparer.Ordinal);
        private static readonly object _captionMatcherLock = new object();

        /// <summary>
        /// Turn a caption template such as "see {paired_ref} →" into a matcher
        /// that recognises any rendered instance of it, whatever sheet number
        /// it carries: everything outside the token is matched literally, the
        /// token itself becomes a non-greedy wildcard.
        ///
        /// Anchored, so a note merely CONTAINING the caption is not swept up,
        /// and the wildcard requires at least one character, so a template
        /// that is nothing but the token cannot degenerate into "match every
        /// note of this type".
        /// </summary>
        private static System.Text.RegularExpressions.Regex BuildCaptionMatcher(string tipFormat)
        {
            if (string.IsNullOrEmpty(tipFormat)) return null;
            lock (_captionMatcherLock)
            {
                if (_captionMatchers.TryGetValue(tipFormat, out var cached)) return cached;
                System.Text.RegularExpressions.Regex rx = null;
                try
                {
                    const string token = "{paired_ref}";
                    var parts = tipFormat.Split(new[] { token }, StringSplitOptions.None);
                    var pattern = string.Join(".+?",
                        parts.Select(System.Text.RegularExpressions.Regex.Escape));
                    rx = new System.Text.RegularExpressions.Regex(
                        "^" + pattern + "$",
                        System.Text.RegularExpressions.RegexOptions.Compiled);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"BuildCaptionMatcher('{tipFormat}'): {ex.Message}");
                }
                _captionMatchers[tipFormat] = rx;
                return rx;
            }
        }

        private static ElementId ResolveTextNoteTypeId(Document doc, string typeName)
        {
            try
            {
                foreach (var el in new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType)))
                {
                    if (!(el is TextNoteType tnt)) continue;
                    if (string.IsNullOrEmpty(typeName)
                        || string.Equals(tnt.Name, typeName, StringComparison.OrdinalIgnoreCase))
                        return tnt.Id;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveTextNoteTypeId: {ex.Message}"); }
            return null;
        }

        // ── Orphan pruning + validation ──────────────────────────────────

        private static void PruneOrphans(Document doc, List<ScopeBoxAdjacency> currentEdges,
            Dictionary<string, List<CurveElement>> existingByGuid, MatchLineRunResult r)
        {
            // An orphan is a stamped match-line whose viewPairGuid prefix
            // (the scope-box pair GUID) doesn't appear in `currentEdges`.
            var liveScopePairs = new HashSet<string>(
                currentEdges.Select(e => e.PairGuid),
                StringComparer.OrdinalIgnoreCase);
            int pruned = 0;
            foreach (var kv in existingByGuid)
            {
                var key = kv.Key ?? "";
                // viewPairGuid format: "<scopePairGuid>:<viewA>:<viewB>"
                var sep = key.IndexOf(':');
                var scopePairGuid = sep > 0 ? key.Substring(0, sep) : key;
                if (liveScopePairs.Contains(scopePairGuid)) continue;
                foreach (var dc in kv.Value)
                    try { doc.Delete(dc.Id); pruned++; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }
            if (pruned > 0)
                r.Warnings.Add($"pruned {pruned} orphan match-line curve(s) — paired scope boxes no longer adjacent");

            // Their captions too. Only STAMPED captions can be tied to a pair, so
            // only those are pruned; an unstamped caption might be a person's note.
            int captions = 0;
            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(TextNote)))
                {
                    var s = Storage.StingAnnotationProvenanceSchema.Read(el);
                    if (s == null || s.Value.Producer != AnnotationProvenance.MatchCaption) continue;
                    var host = AnnotationProvenance.HostOf(s.Value.Key) ?? "";
                    var sep = host.IndexOf(':');
                    var scopePairGuid = sep > 0 ? host.Substring(0, sep) : host;
                    if (liveScopePairs.Contains(scopePairGuid)) continue;
                    try { doc.Delete(el.Id); captions++; }
                    catch (Exception ex) { StingLog.Warn($"Orphan caption prune {el.Id}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { r.Warnings.Add($"Could not prune orphan match-line captions: {ex.Message}"); }
            if (captions > 0)
                r.Warnings.Add($"pruned {captions} orphan match-line caption(s)");
        }

        public sealed class ValidationReport
        {
            public int  PairsTotal               { get; set; }
            public int  PairsWithMatchingRef     { get; set; }
            public int  PairsWithBrokenRef       { get; set; }
            public int  PairsWithMissingViewPair { get; set; }
            public int  ScopeBoxesAdjacent       { get; set; }
            /// <summary>
            /// A-14: distinct scope-box pairs represented among the placed
            /// match lines. PairsTotal counts one entry per (pair x VIEW) —
            /// a scope box driving five level views yields five — so
            /// comparing PairsTotal against ScopeBoxesAdjacent reported
            /// bogus drift on every normal multi-level project.
            /// </summary>
            public int  ScopePairsPlaced         { get; set; }
            public List<string> Warnings         { get; set; } = new List<string>();
        }

        /// <summary>Read-only audit — confirms every placed match line
        /// still points at a live sheet, and every adjacency edge still
        /// has a placed pair. Surfaces drift without modifying the model.</summary>
        public static ValidationReport Validate(Document doc)
        {
            var rep = new ValidationReport();
            if (doc == null) return rep;
            try
            {
                var cfg = MatchLineConfigRegistry.Get(doc);
                var scopeBoxes = CollectScopeBoxes(doc, null);
                var edges = ComputeAdjacency(scopeBoxes, cfg);
                rep.ScopeBoxesAdjacent = edges.Count;

                var existingByGuid = BuildExistingPairIndex(doc);
                rep.PairsTotal = existingByGuid.Count;

                // A-14: collapse per-view keys to their scope-pair GUID (the
                // first colon-delimited field) so the count is comparable
                // with ScopeBoxesAdjacent.
                var scopePairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var k in existingByGuid.Keys)
                {
                    var key = k ?? "";
                    int sep = key.IndexOf(':');
                    scopePairs.Add(sep > 0 ? key.Substring(0, sep) : key);
                }
                rep.ScopePairsPlaced = scopePairs.Count;

                var sheetRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var sh in new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
                {
                    var p = sh.LookupParameter("PRJ_SHEET_FULL_REF_TXT");
                    if (p != null && p.HasValue) sheetRefs.Add(p.AsString() ?? "");
                    sheetRefs.Add(sh.SheetNumber ?? "");
                }

                foreach (var kv in existingByGuid)
                foreach (var dc in kv.Value)
                {
                    var p = dc.LookupParameter(ParamRegistry.MATCH_REF);
                    var refTxt = p?.AsString() ?? "";
                    if (string.IsNullOrEmpty(refTxt))
                    {
                        rep.PairsWithBrokenRef++;
                        rep.Warnings.Add($"curve {dc.Id} has empty MATCH_REF");
                        continue;
                    }
                    if (!sheetRefs.Contains(refTxt))
                    {
                        rep.PairsWithBrokenRef++;
                        rep.Warnings.Add($"curve {dc.Id} → '{refTxt}' (sheet not found in project)");
                    }
                    else
                    {
                        rep.PairsWithMatchingRef++;
                    }
                }
            }
            catch (Exception ex)
            {
                rep.Warnings.Add($"Validate: {ex.Message}");
                StingLog.Error("MatchLineEngine.Validate", ex);
            }
            return rep;
        }

        /// <summary>Read-only — returns the adjacency edges discovered
        /// for a project, suitable for diagnostic display in
        /// MatchLine_Inspect.</summary>
        public static List<ScopeBoxAdjacency> InspectAdjacency(Document doc)
        {
            if (doc == null) return new List<ScopeBoxAdjacency>();
            var cfg = MatchLineConfigRegistry.Get(doc);
            return ComputeAdjacency(CollectScopeBoxes(doc, null), cfg);
        }

        // ── Phase 169 — bundle validator ─────────────────────────────────

        public sealed class BundleReport
        {
            public List<ElementId> BundleSheetIds { get; set; } = new List<ElementId>();
            public int  CurvesScanned { get; set; }
            public int  RefsResolvedInBundle  { get; set; }
            public int  RefsResolvedOutsideBundle { get; set; }
            public int  RefsBroken { get; set; }
            public List<string> Warnings { get; set; } = new List<string>();
            public List<string> OrphanRefs { get; set; } = new List<string>();
        }

        /// <summary>Scans every match-line curve on every sheet in the
        /// supplied bundle. Each STING_MATCH_REF must resolve to a sheet
        /// IN the bundle — refs pointing outside the bundle are
        /// reported as orphans (the most common cause of partial-issue
        /// failures).</summary>
        public static BundleReport ValidateBundle(Document doc,
            IEnumerable<ElementId> bundleSheetIds)
        {
            var rep = new BundleReport();
            if (doc == null || bundleSheetIds == null) return rep;
            var bundle = new HashSet<ElementId>(bundleSheetIds);
            rep.BundleSheetIds = bundle.ToList();
            try
            {
                // Build set of refs the bundle PROVIDES (every sheet's
                // STING_SHEET_FULL_REF + Sheet Number).
                var bundleRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var id in bundle)
                {
                    if (!(doc.GetElement(id) is ViewSheet sh)) continue;
                    var p = sh.LookupParameter("PRJ_SHEET_FULL_REF_TXT");
                    if (p != null && p.HasValue)
                        bundleRefs.Add(p.AsString() ?? "");
                    bundleRefs.Add(sh.SheetNumber ?? "");
                }

                // Build set of view ids placed on bundle sheets.
                var bundleViewIds = new HashSet<ElementId>();
                foreach (var vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)))
                {
                    if (!(vp is Viewport viewport)) continue;
                    if (!bundle.Contains(viewport.SheetId)) continue;
                    bundleViewIds.Add(viewport.ViewId);
                }

                // Walk every match-line curve in those views.
                foreach (var el in new FilteredElementCollector(doc)
                    .OfClass(typeof(CurveElement)))
                {
                    if (!(el is DetailCurve dc)) continue;
                    var pGuid = dc.LookupParameter(ParamRegistry.MATCH_LINE_GUID);
                    if (pGuid == null || !pGuid.HasValue) continue;
                    if (!bundleViewIds.Contains(dc.OwnerViewId)) continue;
                    rep.CurvesScanned++;
                    var pRef = dc.LookupParameter(ParamRegistry.MATCH_REF);
                    var refTxt = pRef?.AsString() ?? "";
                    if (string.IsNullOrEmpty(refTxt))
                    {
                        rep.RefsBroken++;
                        rep.Warnings.Add($"curve {dc.Id} has empty MATCH_REF");
                        continue;
                    }
                    if (bundleRefs.Contains(refTxt))
                        rep.RefsResolvedInBundle++;
                    else
                    {
                        rep.RefsResolvedOutsideBundle++;
                        rep.OrphanRefs.Add(refTxt);
                    }
                }
            }
            catch (Exception ex)
            {
                rep.Warnings.Add($"ValidateBundle: {ex.Message}");
                StingLog.Error("MatchLineEngine.ValidateBundle", ex);
            }
            return rep;
        }
    }
}
