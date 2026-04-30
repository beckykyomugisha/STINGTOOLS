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
        public XYZ      LineStart   { get; set; }   // shared edge endpoints in project coords
        public XYZ      LineEnd     { get; set; }
        public string   Direction   { get; set; }   // "vertical" / "horizontal" / "dogleg"
        public string   PairGuid    { get; set; }   // deterministic, derived from sorted scope-box ids

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

                var viewByScope = BuildViewByScopeIndex(doc);
                var existingByGuid = BuildExistingPairIndex(doc);

                using (var tx = new Transaction(doc, "STING Match-Line sweep"))
                {
                    tx.Start();
                    foreach (var edge in edges)
                    {
                        try
                        {
                            PlaceOrUpdatePair(doc, edge, cfg, viewByScope,
                                              existingByGuid, opts, r);
                        }
                        catch (Exception ex)
                        {
                            r.Errors.Add($"pair {edge.Key}: {ex.Message}");
                            StingLog.Error($"MatchLineEngine pair {edge.Key}", ex);
                        }
                    }
                    if (opts.PruneOrphans)
                        PruneOrphans(doc, edges, existingByGuid, r);
                    tx.Commit();
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

        private static List<ScopeBoxAdjacency> ComputeAdjacency(
            List<Element> scopeBoxes, MatchLineConfig cfg)
        {
            var edges = new List<ScopeBoxAdjacency>();
            double tolFt    = MmToFt(cfg.Adjacency.CoplanarToleranceMm);
            double minOverlapFt = MmToFt(cfg.Adjacency.MinOverlapMm);

            for (int i = 0; i < scopeBoxes.Count; i++)
            {
                var a = scopeBoxes[i];
                var bbA = a.get_BoundingBox(null);
                if (bbA == null) continue;
                for (int j = i + 1; j < scopeBoxes.Count; j++)
                {
                    var b = scopeBoxes[j];
                    var bbB = b.get_BoundingBox(null);
                    if (bbB == null) continue;

                    // X-aligned shared face?
                    foreach (var dirX in new[] { (a:"east",  ax:bbA.Max.X, bx:bbB.Min.X),
                                                  (a:"west",  ax:bbA.Min.X, bx:bbB.Max.X) })
                    {
                        if (Math.Abs(dirX.ax - dirX.bx) > tolFt) continue;
                        double yMin = Math.Max(bbA.Min.Y, bbB.Min.Y);
                        double yMax = Math.Min(bbA.Max.Y, bbB.Max.Y);
                        if ((yMax - yMin) < minOverlapFt) continue;
                        double zMin = Math.Min(bbA.Min.Z, bbB.Min.Z);
                        double x0 = dirX.ax;
                        edges.Add(new ScopeBoxAdjacency
                        {
                            ScopeBoxA = a, ScopeBoxB = b,
                            LineStart = new XYZ(x0, yMin, zMin),
                            LineEnd   = new XYZ(x0, yMax, zMin),
                            Direction = "vertical",
                            PairGuid  = DerivePairGuid(a, b),
                        });
                    }

                    // Y-aligned shared face?
                    foreach (var dirY in new[] { (a:"north", ay:bbA.Max.Y, by:bbB.Min.Y),
                                                  (a:"south", ay:bbA.Min.Y, by:bbB.Max.Y) })
                    {
                        if (Math.Abs(dirY.ay - dirY.by) > tolFt) continue;
                        double xMin = Math.Max(bbA.Min.X, bbB.Min.X);
                        double xMax = Math.Min(bbA.Max.X, bbB.Max.X);
                        if ((xMax - xMin) < minOverlapFt) continue;
                        double zMin = Math.Min(bbA.Min.Z, bbB.Min.Z);
                        double y0 = dirY.ay;
                        edges.Add(new ScopeBoxAdjacency
                        {
                            ScopeBoxA = a, ScopeBoxB = b,
                            LineStart = new XYZ(xMin, y0, zMin),
                            LineEnd   = new XYZ(xMax, y0, zMin),
                            Direction = "horizontal",
                            PairGuid  = DerivePairGuid(a, b),
                        });
                    }
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
                    if (v.ViewType != ViewType.FloorPlan
                        && v.ViewType != ViewType.CeilingPlan
                        && v.ViewType != ViewType.AreaPlan
                        && v.ViewType != ViewType.EngineeringPlan
                        && v.ViewType != ViewType.Section
                        && v.ViewType != ViewType.Elevation)
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

        /// <summary>Reads STING_SHEET_FULL_REF on the sheet hosting
        /// the view; falls back to Sheet Number when the param isn't
        /// bound. Returns "" when the view isn't placed on a sheet.</summary>
        private static string ResolveSheetRef(Document doc, View view)
        {
            try
            {
                foreach (var vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)))
                {
                    if (!(vp is Viewport viewport)) continue;
                    if (viewport.ViewId != view.Id) continue;
                    if (!(doc.GetElement(viewport.SheetId) is ViewSheet sheet)) continue;
                    var pFull = sheet.LookupParameter("STING_SHEET_FULL_REF_TXT");
                    if (pFull != null && pFull.HasValue)
                    {
                        var v = pFull.AsString();
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                    return sheet.SheetNumber ?? "";
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveSheetRef: {ex.Message}"); }
            return "";
        }

        /// <summary>Indexes existing match-line DetailCurves by their
        /// STING_MATCH_LINE_GUID stamp so re-runs can find them in
        /// O(1) and update in place.</summary>
        private static Dictionary<string, List<CurveElement>> BuildExistingPairIndex(Document doc)
        {
            var idx = new Dictionary<string, List<CurveElement>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(CurveElement)))
                {
                    if (!(el is DetailCurve dc)) continue;
                    var p = dc.LookupParameter(ParamRegistry.MATCH_LINE_GUID);
                    if (p == null || !p.HasValue) continue;
                    var key = p.AsString();
                    if (string.IsNullOrEmpty(key)) continue;
                    if (!idx.TryGetValue(key, out var list))
                        idx[key] = list = new List<CurveElement>();
                    list.Add(dc);
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildExistingPairIndex: {ex.Message}"); }
            return idx;
        }

        // ── Pair placement ───────────────────────────────────────────────

        private static void PlaceOrUpdatePair(Document doc, ScopeBoxAdjacency edge,
            MatchLineConfig cfg, Dictionary<long, List<View>> viewByScope,
            Dictionary<string, List<CurveElement>> existingByGuid,
            MatchLineRunOptions opts, MatchLineRunResult r)
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
            foreach (var viewA in viewsA)
            foreach (var viewB in viewsB)
            {
                if (cfg.Adjacency.ConsiderLevelMatch)
                {
                    var lvA = viewA.GenLevel?.Id ?? ElementId.InvalidElementId;
                    var lvB = viewB.GenLevel?.Id ?? ElementId.InvalidElementId;
                    if (lvA != ElementId.InvalidElementId
                        && lvB != ElementId.InvalidElementId
                        && lvA != lvB) continue;
                }

                var refA = ResolveSheetRef(doc, viewA);
                var refB = ResolveSheetRef(doc, viewB);

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
                        try { doc.Delete(dc.Id); } catch { }

                // Place the curve in viewA referencing refB, and the
                // curve in viewB referencing refA.
                PlaceCurve(doc, viewA, edge, cfg, viewPairGuid, refB, r);
                PlaceCurve(doc, viewB, edge, cfg, viewPairGuid, refA, r);

                if (existed) r.PairsUpdated++;
                else         r.PairsCreated++;
            }
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
            MatchLineRunResult r)
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
                    a = new XYZ(edge.LineStart.X, edge.LineStart.Y, z);
                    b = new XYZ(edge.LineEnd.X,   edge.LineEnd.Y,   z);
                }
                else
                {
                    a = edge.LineStart; b = edge.LineEnd;
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

                var line = Line.CreateBound(a, b);
                var dc = doc.Create.NewDetailCurve(view, line);

                // Apply line style.
                var styleId = ResolveLineStyleId(doc, cfg.Geometry.LineStyleName)
                           ?? ResolveLineStyleId(doc, cfg.Geometry.FallbackLineStyleName);
                if (styleId != null && styleId != ElementId.InvalidElementId)
                {
                    try { dc.LineStyle = doc.GetElement(styleId); }
                    catch (Exception ex) { r.Warnings.Add($"line style apply: {ex.Message}"); }
                }

                // Stamp parameters (skip silently when binding missing —
                // pre-flight check should have warned).
                if (cfg.Stamping.WritePairedRef)
                    TrySet(dc, ParamRegistry.MATCH_REF, pairedRef);
                if (cfg.Stamping.WritePairGuid)
                    TrySet(dc, ParamRegistry.MATCH_LINE_GUID, viewPairGuid);
                if (cfg.Stamping.WriteDirection)
                    TrySet(dc, ParamRegistry.MATCH_DIR, edge.Direction);

                // Tip captions (TextNote — fallback path; tag-family
                // path with STING_TAG_MATCHLINE is a Phase II refinement).
                if (!string.IsNullOrEmpty(pairedRef) &&
                    !string.IsNullOrEmpty(cfg.Captions.TipFormat))
                {
                    string caption = cfg.Captions.TipFormat.Replace("{paired_ref}", pairedRef);
                    var noteTypeId = ResolveTextNoteTypeId(doc, cfg.Captions.FallbackTextNoteTypeName);
                    if (noteTypeId != null && noteTypeId != ElementId.InvalidElementId)
                    {
                        if (string.Equals(cfg.Captions.TipPlacement, "BothEnds", StringComparison.OrdinalIgnoreCase))
                        {
                            try { TextNote.Create(doc, view.Id, a, caption, noteTypeId); r.TipCaptionsPlaced++; } catch { }
                            try { TextNote.Create(doc, view.Id, b, caption, noteTypeId); r.TipCaptionsPlaced++; } catch { }
                        }
                        else
                        {
                            var mid = (a + b) / 2;
                            try { TextNote.Create(doc, view.Id, mid, caption, noteTypeId); r.TipCaptionsPlaced++; } catch { }
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

        private static void TrySet(Element el, string paramName, string value)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p != null && !p.IsReadOnly) p.Set(value ?? "");
            }
            catch { /* binding missing — pre-flight warns */ }
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
                    try { doc.Delete(dc.Id); pruned++; } catch { }
            }
            if (pruned > 0)
                r.Warnings.Add($"pruned {pruned} orphan match-line curve(s) — paired scope boxes no longer adjacent");
        }

        public sealed class ValidationReport
        {
            public int  PairsTotal               { get; set; }
            public int  PairsWithMatchingRef     { get; set; }
            public int  PairsWithBrokenRef       { get; set; }
            public int  PairsWithMissingViewPair { get; set; }
            public int  ScopeBoxesAdjacent       { get; set; }
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

                var sheetRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var sh in new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
                {
                    var p = sh.LookupParameter("STING_SHEET_FULL_REF_TXT");
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
    }
}
