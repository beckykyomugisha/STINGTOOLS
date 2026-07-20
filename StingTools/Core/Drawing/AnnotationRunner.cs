// StingTools — Drawing Template Manager · Phase 137
//
// AnnotationRunner consumes an AnnotationRulePack and runs four
// passes against a single View, in order:
//
//   1. Tag rules    — IndependentTag.Create per resolved rule
//   2. Dim rules    — chained dims across grids / levels
//   3. Decorative   — north arrow, scale bar, key plan, matchlines
//   4. Spot rules   — spot elevations / spot coordinates
//
// Caller is responsible for an open Transaction. The runner does not
// open or close transactions.
//
// Phase 137 changes:
//   * Hardcoded BIC list replaced by RevitCategoryTree.TaggableCategories
//   * Generic rule list (AnnotationRulePack.Rules) — one rule per
//     (category, ruleType) pair drives the runner; legacy bool flags
//     fold into rules via MigrateFromLegacy.
//   * New Run(doc, view, pack, options) entry point + AnnotationResult.
//   * Legacy Apply(doc, view, drawingType) retained as a thin shim
//     so DrawingTypePresentation continues to compile.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Drawing
{
    public sealed class AnnotationResult
    {
        public int TagsPlaced      { get; set; }
        public int DimsPlaced      { get; set; }
        public int DecorativePlaced { get; set; }
        public int SpotsPlaced     { get; set; }
        /// <summary>Annotations not re-created because the view already had them (C-4).</summary>
        public int Skipped         { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public sealed class AnnotationRunStats
    {
        public int DimsCreated      { get; set; }
        /// <summary>Alias for DimsCreated used by DrawingTypePresentation.</summary>
        public int DimsPlaced       { get => DimsCreated; set => DimsCreated = value; }
        public int TagsPlaced       { get; set; }
        /// <summary>Decorative elements placed (spots, keynotes, etc.) — reserved for future passes.</summary>
        public int DecorativePlaced { get; set; }
        public int Skipped          { get; set; }
        public List<string> Warnings { get; } = new List<string>();

        public override string ToString()
            => $"AnnotationRun: {DimsCreated} dim(s), {TagsPlaced} tag(s), {Skipped} skipped, {Warnings.Count} warning(s)";
    }

    /// <summary>Runtime switches for the annotation pass.</summary>
    public sealed class AnnotationRunOptions
    {
        /// <summary>Skip auto-dimension steps (grids, levels).</summary>
        public bool SkipDims    { get; set; } = false;
        /// <summary>Skip auto-dim steps — alias matching DrawingTypePresentation usage.</summary>
        public bool SkipAutoDim { get => SkipDims; set => SkipDims = value; }
        /// <summary>Skip auto-tag steps.</summary>
        public bool SkipTags    { get; set; } = false;
        /// <summary>Skip auto-tag steps — alias matching DrawingTypePresentation usage.</summary>
        public bool SkipAutoTag { get => SkipTags; set => SkipTags = value; }
        /// <summary>Skip decorative / spot annotation steps.</summary>
        public bool SkipDecorative { get; set; } = false;
        /// <summary>Skip spot-elevation / spot-coordinate annotation steps.</summary>
        public bool SkipSpots      { get; set; } = false;
        /// <summary>View scale hint (1:N) supplied by the caller for density checks.</summary>
        public int  ViewScale      { get; set; } = 0;
    }

    public static class AnnotationRunner
    {
        // ─── Public entry points ─────────────────────────────────────────

        /// <summary>
        /// Primary overload: accepts a full <see cref="DrawingType"/> and
        /// optional runtime options. Delegates to <see cref="Apply"/>.
        /// </summary>
        public static AnnotationRunStats Run(
            Document doc, View view, DrawingType drawingType, AnnotationRunOptions options = null)
        {
            return Apply(doc, view, drawingType, options);
        }

        /// <summary>
        /// Run the annotation pass defined by drawingType.Annotation
        /// against the given view. The caller is responsible for an
        /// active Transaction — this method performs many Element
        /// creations and expects to be wrapped.
        /// </summary>
        public static AnnotationRunStats Apply(
            Document doc, View view, DrawingType drawingType, AnnotationRunOptions options = null)
        {
            var stats = new AnnotationRunStats();
            if (doc == null || view == null || drawingType?.Annotation == null) return stats;
            var pack = drawingType.Annotation;

            // Scale-aware density — at scales coarser than DenseUntilScale,
            // skip per-element tagging. View.Scale is 1:N so a larger
            // number means a coarser drawing.
            int effectiveScale = (options?.ViewScale > 0) ? options.ViewScale : view.Scale;
            bool dense = !pack.DenseUntilScale.HasValue || effectiveScale <= pack.DenseUntilScale.Value;

            // ── Tagging — Phase 137 Rules path. MigrateFromLegacy folds the
            // legacy per-category bools into pack.Rules at load, so a single
            // rule walk covers both old and new formats; routed through the
            // proven TagCategory helper. Density-gated. (The previous body
            // read the per-category bools directly — but those are zeroed by
            // MigrateFromLegacy, so it was inert, and pack.Rules — used by 48
            // shipped drawing types — was never processed at all.)
            if (options?.SkipTags != true)
            {
                if (dense)
                {
                    try { TagByRules(doc, view, pack, stats); }
                    catch (Exception ex) { stats.Warnings.Add("TagByRules: " + ex.Message); }
                }
                else
                {
                    stats.Skipped++;
                    stats.Warnings.Add($"Per-element tagging skipped — view scale 1:{effectiveScale} exceeds denseUntilScale 1:{pack.DenseUntilScale}.");
                }
            }

            // ── Dimensioning — AutoDim + AutoDim/GridDim/LevelAnnotation rules.
            if (options?.SkipDims != true)
            {
                try { DimByRules(doc, view, pack, stats); }
                catch (Exception ex) { stats.Warnings.Add("DimByRules: " + ex.Message); }
            }

            // ── Decorative (north arrow / scale bar / key plan / matchlines)
            // + spot (elevation / coordinate) — additive Phase 137 passes that
            // had no legacy equivalent and were never wired into Apply.
            if (options?.SkipDecorative != true || options?.SkipSpots != true)
            {
                var aux = new AnnotationResult();
                if (options?.SkipDecorative != true)
                {
                    try { RunDecorativeAnnotation(doc, view, pack, options, aux); }
                    catch (Exception ex) { aux.Warnings.Add("Decorative: " + ex.Message); }
                }
                if (options?.SkipSpots != true)
                {
                    try { RunSpotAnnotation(doc, view, pack, options, aux); }
                    catch (Exception ex) { aux.Warnings.Add("Spot: " + ex.Message); }
                }
                stats.DecorativePlaced += aux.DecorativePlaced + aux.SpotsPlaced;
                stats.Warnings.AddRange(aux.Warnings);
            }

            return stats;
        }

        // ─── Rules-based drivers (wire pack.Rules into the proven helpers) ──

        private static readonly HashSet<string> _tagRuleKinds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "AutoTag", "RoomTag", "SpaceTag", "AreaTag", "MaterialTag", "KeynoteTag", "MultiCategoryTag" };

        /// <summary>
        /// Phase 137 Rules-based tagging. Walks pack.Rules (which, post-
        /// MigrateFromLegacy, also holds the folded legacy per-category bools);
        /// when Rules is empty and the general AutoTag bool is set, synthesises
        /// one AutoTag rule per taggable category. Auto3DTag short-circuits to
        /// Tag3DCommand (IndependentTag is 2D-only). Each resolved built-in
        /// category is tagged at most once via the proven TagCategory helper;
        /// custom (non-built-in) categories are skipped.
        /// </summary>
        private static void TagByRules(Document doc, View view, AnnotationRulePack pack, AnnotationRunStats stats)
        {
            if (pack.Rules != null && pack.Rules.Any(r => r != null && r.Enabled &&
                    string.Equals(r.RuleType, "Auto3DTag", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    if (view is View3D v3d)
                    {
                        bool useNarrative = false;
                        try { useNarrative = ParameterHelpers.GetInt(view, ParamRegistry.DISPLAY_MODE, 0) == 6; }
                        catch { /* defensive */ }
                        var r3d = StingTools.Tags.Tag3DCommand.PlaceTagsInView(doc, v3d, useNarrative);
                        stats.TagsPlaced += r3d.Placed;
                        foreach (var w in r3d.Warnings) stats.Warnings.Add($"Auto3DTag: {w}");
                    }
                    else
                    {
                        stats.Warnings.Add($"Auto3DTag rule skipped — view '{view.Name}' is not a 3D view.");
                    }
                }
                catch (Exception ex) { stats.Warnings.Add("Auto3DTag: " + ex.Message); }
            }

            List<AutoAnnotationRule> effective;
            if (pack.Rules != null && pack.Rules.Count > 0)
                effective = pack.Rules
                    .Where(r => r != null && r.Enabled && _tagRuleKinds.Contains(r.RuleType ?? "AutoTag"))
                    .ToList();
            else if (pack.AutoTag == true)
                effective = SharedParamGuids.AllCategoryEnums
                    .Select(bic => new AutoAnnotationRule { RuleType = "AutoTag", Category = bic.ToString() })
                    .ToList();
            else
                return;

            // C-4: one scan of this view's existing tags, shared by every
            // category below. Lazy so a pack with no taggable category never
            // pays for it. Without this the runner had no idempotency at all:
            // AutoAnnotationRule.SkipIfTagged (default true) was read nowhere,
            // so re-running SyncStyles, a drift heal, or DrawingTypePresentation
            // .Apply doubled every tag on the view.
            var taggedIndex = new Lazy<HashSet<ElementId>>(() => BuildTaggedElementIndex(doc, view, stats));

            var doneCats = new HashSet<long>(); // tag each category at most once
            foreach (var rule in effective)
            {
                if (rule == null) continue;
                try
                {
                    var catId = ResolveCategoryId(doc, rule.Category);
                    if (catId == ElementId.InvalidElementId) continue;
                    long cv = catId.Value;
                    if (!doneCats.Add(cv)) continue;
                    if (!Enum.IsDefined(typeof(BuiltInCategory), unchecked((int)cv))) continue; // skip custom categories
                    TagCategory(doc, view, pack, (BuiltInCategory)cv, rule.Category, stats, rule, taggedIndex.Value);
                }
                catch (Exception ex) { stats.Warnings.Add($"Tag rule '{rule.Category}': {ex.Message}"); }
            }
        }

        /// <summary>
        /// Phase 137 Rules-based dimensioning. Honours AutoDim / GridDim /
        /// LevelAnnotation rules — placing one grid chain and/or one level
        /// chain — plus the general AutoDim bool fallback, via the proven
        /// DimGrids / DimLevels helpers. Each chain is placed at most once.
        /// </summary>
        private static void DimByRules(Document doc, View view, AnnotationRulePack pack, AnnotationRunStats stats)
        {
            bool didGrids = false, didLevels = false;
            if (pack.Rules != null)
            {
                foreach (var r in pack.Rules)
                {
                    if (r == null || !r.Enabled) continue;
                    var rt = r.RuleType ?? "";
                    bool isDim = string.Equals(rt, "AutoDim", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(rt, "GridDim", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(rt, "LevelAnnotation", StringComparison.OrdinalIgnoreCase);
                    if (!isDim) continue;
                    bool isLevels = (r.Category ?? "").IndexOf("Level", StringComparison.OrdinalIgnoreCase) >= 0;
                    try
                    {
                        if (isLevels) { if (!didLevels) { DimLevels(doc, view, pack, stats); didLevels = true; } }
                        else          { if (!didGrids)  { DimGrids(doc, view, pack, stats);  didGrids = true; } }
                    }
                    catch (Exception ex) { stats.Warnings.Add($"Dim rule '{r.Category}': {ex.Message}"); }
                }
            }
            if (pack.AutoDim == true && !didGrids)
            {
                try { DimGrids(doc, view, pack, stats); }
                catch (Exception ex) { stats.Warnings.Add("AutoDim grids: " + ex.Message); }
            }
        }

        // ─── Dimensioning ────────────────────────────────────────────────

        /// <summary>
        /// Drop a single overall dimension chain across all grids
        /// visible in the view. Each grid contributes one reference.
        /// </summary>
        /// <summary>
        /// C-4 idempotency for the two dimension chains.
        ///
        /// Strategy: detect an existing chain by what it REFERENCES rather than
        /// by a stamped marker. A marker would need a new shared parameter bound
        /// to Dimensions — provisioning across MR_PARAMETERS.txt, the .csv
        /// mirror, PARAMETER_REGISTRY.json and a category binding — whereas
        /// DimByRules places at most one grid chain and one level chain per
        /// view, so a coarse "does this view already hold a dimension
        /// referencing Grids / Levels?" question is exactly as precise as the
        /// runner needs and requires nothing new.
        ///
        /// Residual limitation: Revit can report AreReferencesAvailable == false
        /// (references lost, or the dimension is in a linked/【unloaded】 state).
        /// Such a dimension is treated as "unknown" and skipped over rather than
        /// counted as a match, so in the rare case where every dimension in the
        /// view is unreadable AND a prior STING chain exists, a duplicate is
        /// still possible. Failing that way round is deliberate: the alternative
        /// silently refuses to dimension views that merely contain odd geometry.
        /// </summary>
        private static bool ViewHasDimensionReferencing(Document doc, View view, BuiltInCategory targetCat)
        {
            try
            {
                foreach (var el in new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(Dimension))
                    .WhereElementIsNotElementType())
                {
                    if (!(el is Dimension dim)) continue;
                    try
                    {
                        if (!dim.AreReferencesAvailable) continue;   // unknown — not a match
                        var refs = dim.References;
                        if (refs == null) continue;
                        foreach (Reference r in refs)
                        {
                            var host = doc.GetElement(r);
                            if (host?.Category == null) continue;
                            if (host.Category.Id.Value == (long)targetCat) return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"ViewHasDimensionReferencing: dimension {dim.Id} — {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ViewHasDimensionReferencing({view?.Name}): {ex.Message}");
            }
            return false;
        }

        private static void DimGrids(Document doc, View view, AnnotationRulePack pack, AnnotationRunStats stats)
        {
            if (ViewHasDimensionReferencing(doc, view, BuiltInCategory.OST_Grids))
            {
                stats.Skipped++;
                return;
            }

            var grids = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Grids)
                .WhereElementIsNotElementType()
                .Cast<Grid>()
                .ToList();
            if (grids.Count < 2) return;

            // A-4: split by orientation. Every grid used to go into ONE
            // ReferenceArray with a dimension line running between the end
            // points of the first and last grid in COLLECTOR order — arbitrary
            // in both direction and position. A dimension can only measure
            // mutually parallel references, so on any project with orthogonal
            // grids (i.e. essentially all of them) NewDimension threw and the
            // per-view catch swallowed it: grid auto-dimensioning never once
            // succeeded on a real model.
            //
            // Each parallel set now gets its own chain, on a line PERPENDICULAR
            // to that set — which is the only orientation that can measure the
            // spacing between them — placed just outside the grid extent.
            var eastWest = new List<Grid>();   // run along X; spaced along Y
            var northSouth = new List<Grid>(); // run along Y; spaced along X
            double zPlane = 0; bool haveZ = false;
            double xMin = double.MaxValue, xMax = double.MinValue;
            double yMin = double.MaxValue, yMax = double.MinValue;

            foreach (var g in grids)
            {
                var line = g.Curve as Line;
                if (line == null) continue;    // arc grids cannot join a linear chain
                var d = line.Direction;
                var a = line.GetEndPoint(0);
                var b = line.GetEndPoint(1);
                if (!haveZ) { zPlane = a.Z; haveZ = true; }
                xMin = Math.Min(xMin, Math.Min(a.X, b.X)); xMax = Math.Max(xMax, Math.Max(a.X, b.X));
                yMin = Math.Min(yMin, Math.Min(a.Y, b.Y)); yMax = Math.Max(yMax, Math.Max(a.Y, b.Y));
                if (RunsEastWest(d.X, d.Y)) eastWest.Add(g); else northSouth.Add(g);
            }
            if (!haveZ) return;

            var dimStyleId = ResolveDimensionStyleId(doc, pack.DimensionStyle);
            double marginFt = 10.0;   // ~3 m clear of the grid extent

            // East-west grids are stacked along Y, so their chain runs along Y,
            // offset beyond the eastern extent.
            PlaceGridChain(doc, view, eastWest, dimStyleId, stats, "east-west",
                positionOf: g => ((Line)g.Curve).Origin.Y,
                pointAt: (pos, off) => new XYZ(xMax + off, pos, zPlane),
                marginFt: marginFt);

            // North-south grids are stacked along X, so their chain runs along
            // X, offset beyond the northern extent.
            PlaceGridChain(doc, view, northSouth, dimStyleId, stats, "north-south",
                positionOf: g => ((Line)g.Curve).Origin.X,
                pointAt: (pos, off) => new XYZ(pos, yMax + off, zPlane),
                marginFt: marginFt);
        }

        /// <summary>
        /// Orientation test for a grid line: true when the curve runs
        /// predominantly along model X (an "east-west" grid on plan), which
        /// means the set is spaced along Y and must be dimensioned by a chain
        /// running along Y.
        /// Revit-free so the classification is testable; ties (|dx| == |dy|,
        /// a 45-degree grid) resolve to east-west deterministically rather
        /// than by collector order.
        /// </summary>
        internal static bool RunsEastWest(double dirX, double dirY)
            => Math.Abs(dirX) >= Math.Abs(dirY);

        /// <summary>
        /// Span of a parallel grid set along its spacing axis, widened by a
        /// margin. Returns false when there are fewer than two DISTINCT
        /// positions — coincident grids cannot be dimensioned and produced a
        /// zero-length dimension line.
        /// Revit-free so the degenerate cases are testable.
        /// </summary>
        internal static bool TryGridSpan(IReadOnlyList<double> positions, double marginFt,
            out double lo, out double hi)
        {
            lo = hi = 0;
            if (positions == null || positions.Count < 2) return false;
            double mn = double.MaxValue, mx = double.MinValue;
            foreach (var p in positions) { if (p < mn) mn = p; if (p > mx) mx = p; }
            if (mx - mn < 1e-6) return false;
            lo = mn - marginFt;
            hi = mx + marginFt;
            return true;
        }

        private static void PlaceGridChain(Document doc, View view, List<Grid> set,
            ElementId dimStyleId, AnnotationRunStats stats, string label,
            Func<Grid, double> positionOf, Func<double, double, XYZ> pointAt, double marginFt)
        {
            if (set == null || set.Count < 2) return;
            try
            {
                var positions = set.Select(positionOf).ToList();
                if (!TryGridSpan(positions, marginFt, out double lo, out double hi))
                {
                    stats.Warnings.Add($"Grid dim ({label}): grids are coincident — no chain placed.");
                    return;
                }

                var refs = new ReferenceArray();
                foreach (var g in set)
                {
                    try { refs.Append(new Reference(g)); }
                    catch (Exception ex) { StingLog.Warn($"Grid ref {g.Id}: {ex.Message}"); }
                }
                if (refs.Size < 2) return;

                var dimLine = Line.CreateBound(pointAt(lo, marginFt), pointAt(hi, marginFt));
                var dim = (dimStyleId == null || dimStyleId == ElementId.InvalidElementId)
                    ? doc.Create.NewDimension(view, dimLine, refs)
                    : doc.Create.NewDimension(view, dimLine, refs, (DimensionType)doc.GetElement(dimStyleId));
                if (dim != null) stats.DimsCreated++;
            }
            catch (Exception ex) { stats.Warnings.Add($"Grid dim ({label}): {ex.Message}"); }
        }

        /// <summary>
        /// Horizontal anchor for the level chain: a model-space point just
        /// outside the left edge of the view's crop, at mid height. Falls back
        /// to the view origin, then to the project origin, so a view with no
        /// active crop still gets a chain somewhere sensible rather than none.
        /// </summary>
        private static XYZ ResolveLevelChainAnchor(View view)
        {
            const double marginFt = 5.0;
            try
            {
                var cb = view.CropBox;
                if (cb != null)
                {
                    var frame = cb.Transform ?? Transform.Identity;
                    var pf = new XYZ(cb.Min.X - marginFt, (cb.Min.Y + cb.Max.Y) * 0.5, 0);
                    return frame.OfPoint(pf);
                }
            }
            catch (Exception ex) { StingLog.Warn($"ResolveLevelChainAnchor({view?.Name}): {ex.Message}"); }
            try { if (view.Origin != null) return view.Origin; }
            catch (Exception ex) { StingLog.Warn($"ResolveLevelChainAnchor origin: {ex.Message}"); }
            return XYZ.Zero;
        }

        /// <summary>
        /// Drop a vertical dimension chain across all Levels visible in
        /// the section / elevation view.
        /// </summary>
        private static void DimLevels(Document doc, View view, AnnotationRulePack pack, AnnotationRunStats stats)
        {
            if (ViewHasDimensionReferencing(doc, view, BuiltInCategory.OST_Levels))
            {
                stats.Skipped++;
                return;
            }

            var levels = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Levels)
                .WhereElementIsNotElementType()
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();
            if (levels.Count < 2) return;

            var refs = new ReferenceArray();
            foreach (var l in levels)
            {
                try { refs.Append(new Reference(l)); }
                catch (Exception ex) { StingLog.Warn($"Level ref {l.Id}: {ex.Message}"); }
            }
            if (refs.Size < 2) return;

            // A-10: the chain used to be pinned at X=0, Y=0 — the project
            // origin — so on any section not passing through the origin it
            // landed outside the crop and was invisible. Anchor it to the
            // view's own crop box instead, just outside the left edge.
            //
            // The anchor is computed in the crop's FRAME and mapped back to
            // model space (the E-2 lesson: crop Min/Max are frame coords, not
            // model coords), so this works on rotated plans and sections alike.
            double zLo = levels.First().Elevation;
            double zHi = levels.Last().Elevation;
            if (Math.Abs(zHi - zLo) < 1e-6)
            {
                // Every visible level shares an elevation — Line.CreateBound
                // would throw on a zero-length line.
                stats.Warnings.Add("Level dim: all visible levels share one elevation — no chain placed.");
                return;
            }

            var anchor = ResolveLevelChainAnchor(view);
            var dimLine = Line.CreateBound(
                new XYZ(anchor.X, anchor.Y, zLo),
                new XYZ(anchor.X, anchor.Y, zHi));
            try
            {
                var dim = doc.Create.NewDimension(view, dimLine, refs);
                if (dim != null) stats.DimsCreated++;
            }
            catch (Exception ex) { stats.Warnings.Add("Level dim: " + ex.Message); }
        }

        // ─── Tagging ─────────────────────────────────────────────────────

        /// <summary>
        /// Walk every element of the given category visible in the view
        /// and drop an IndependentTag at the element's centre. The tag
        /// family is resolved from AnnotationRulePack.TagFamilies[catKey]
        /// if present, otherwise the first loaded tag family for the
        /// category. After placing each tag, applies CategoryDepths from
        /// the active ViewStylePack if declared.
        /// </summary>
        /// <summary>
        /// Element ids already carrying an IndependentTag in this view.
        /// Built once per view and shared across every tag rule.
        /// </summary>
        private static HashSet<ElementId> BuildTaggedElementIndex(Document doc, View view, AnnotationRunStats stats)
        {
            var set = new HashSet<ElementId>();
            try
            {
                foreach (var el in new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .WhereElementIsNotElementType())
                {
                    if (!(el is IndependentTag tag)) continue;
                    try
                    {
                        foreach (var id in tag.GetTaggedLocalElementIds())
                            if (id != null && id != ElementId.InvalidElementId) set.Add(id);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"BuildTaggedElementIndex: tag {tag.Id} — {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Fail open with a warning: an empty index means "tag
                // everything", i.e. the previous behaviour, rather than
                // silently skipping work the user asked for.
                stats.Warnings.Add($"Could not index existing tags in '{view.Name}' ({ex.Message}); " +
                                   "duplicate tags are possible on this view.");
            }
            return set;
        }

        private static void TagCategory(Document doc, View view, AnnotationRulePack pack,
            BuiltInCategory bic, string catKey, AnnotationRunStats stats,
            AutoAnnotationRule rule = null, HashSet<ElementId> alreadyTagged = null)
        {
            var elements = new FilteredElementCollector(doc, view.Id)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToElements();
            if (elements.Count == 0) return;

            ElementId tagTypeId = ResolveTagTypeId(doc, view, pack, catKey, bic, stats);
            if (tagTypeId == ElementId.InvalidElementId)
            {
                stats.Warnings.Add($"No tag family available for {catKey} — skipped.");
                return;
            }

            // Pre-resolve CategoryDepths pack for this bic (avoid per-element registry hit).
            ViewStylePack depthPack = null;
            string depthCatKey = null;
            int resolvedDepth = 0;
            bool hasDepth = false;
            try
            {
                var dtId = DrawingTypeStamper.Read(view);
                if (!string.IsNullOrEmpty(dtId))
                {
                    depthPack = DrawingTypeRegistry.TryGetPack(doc, dtId);
                    if (depthPack?.CategoryDepths != null)
                    {
                        depthCatKey = Category.GetCategory(doc, bic)?.Name ?? bic.ToString();
                        if (!depthPack.CategoryDepths.TryGetValue(depthCatKey, out resolvedDepth))
                        {
                            depthPack.CategoryDepths.TryGetValue(bic.ToString(), out resolvedDepth);
                        }
                        hasDepth = resolvedDepth > 0;
                    }
                }
            }
            catch { /* resolver must never throw */ }

            // C-4: honour the rule's skipIfTagged (POCO default true). Only the
            // config dialog ever read this field before.
            bool skipIfTagged = rule?.SkipIfTagged ?? true;

            foreach (var el in elements)
            {
                try
                {
                    if (skipIfTagged && alreadyTagged != null && alreadyTagged.Contains(el.Id))
                    {
                        stats.Skipped++;
                        continue;
                    }

                    var pt = GetElementCentre(el);
                    if (pt == null)
                    {
                        // A-9: GetElementCentre's comment promised an XYZ.Zero
                        // sentinel "the caller can detect" but returned null,
                        // and this call site passed it straight into
                        // IndependentTag.Create — one exception per element on
                        // floor / ceiling-heavy categories, flooding the
                        // warning list and tagging nothing. It now falls back
                        // to the bounding-box centre, so those categories tag
                        // properly instead of being skipped; null here means
                        // even that failed.
                        stats.Skipped++;
                        stats.Warnings.Add($"No taggable point for {catKey} element {el.Id} — skipped.");
                        continue;
                    }

                    // Revit 2025 removed IndependentTag.CanTagHost(doc, Reference) +
                    // renamed the Create parameters. The surrounding try/catch
                    // turns any "can't tag this host" failure into a Skipped
                    // count + warning row, replacing the dropped pre-check.
                    var tag = IndependentTag.Create(doc, tagTypeId, view.Id,
                        new Reference(el), false, TagOrientation.Horizontal, pt);
                    if (tag != null)
                    {
                        stats.TagsPlaced++;
                        // Keep the index current so a later rule covering the
                        // same element in this run doesn't tag it twice.
                        alreadyTagged?.Add(el.Id);
                    }

                    // Apply CategoryDepths from the active pack, if declared.
                    // FIX-6: write the tier flags CUMULATIVELY — a depth of N
                    // means tiers 1..N are all visible (higher tiers off), not
                    // tier N alone. Mirrors TokenProfileApplier.WriteCategoryDepths
                    // so the produce path and the per-category depth path agree.
                    if (hasDepth)
                    {
                        try { ParameterHelpers.SetInt(el, "TAG_PARA_DEPTH_INT", resolvedDepth); }
                        catch { }
                        for (int t = 1; t <= 10; t++)
                        {
                            try { ParameterHelpers.SetInt(el, $"TAG_PARA_STATE_{t}_BOOL", t <= resolvedDepth ? 1 : 0); }
                            catch { }
                        }
                    }
                }
                catch (Exception ex) { stats.Warnings.Add($"TagRule create '{el.Id}': {ex.Message}"); }
            }
        }

        private static BuiltInCategory TagCategoryFor(BuiltInCategory host)
        {
            switch (host)
            {
                case BuiltInCategory.OST_Rooms:                return BuiltInCategory.OST_RoomTags;
                case BuiltInCategory.OST_Doors:                return BuiltInCategory.OST_DoorTags;
                case BuiltInCategory.OST_Windows:              return BuiltInCategory.OST_WindowTags;
                case BuiltInCategory.OST_MechanicalEquipment:  return BuiltInCategory.OST_MechanicalEquipmentTags;
                case BuiltInCategory.OST_ElectricalEquipment:  return BuiltInCategory.OST_ElectricalEquipmentTags;
                case BuiltInCategory.OST_PlumbingFixtures:     return BuiltInCategory.OST_PlumbingFixtureTags;
                case BuiltInCategory.OST_LightingFixtures:     return BuiltInCategory.OST_LightingFixtureTags;
                case BuiltInCategory.OST_PipeFitting:          return BuiltInCategory.OST_PipeFittingTags;
                case BuiltInCategory.OST_StructuralFraming:    return BuiltInCategory.OST_StructuralFramingTags;
                default:                                       return host;
            }
        }

        private static ElementId ResolveDimensionStyleId(Document doc, string styleName)
        {
            if (string.IsNullOrWhiteSpace(styleName)) return ElementId.InvalidElementId;
            var dt = new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .FirstOrDefault(d => string.Equals(d.Name, styleName, StringComparison.OrdinalIgnoreCase));
            return dt?.Id ?? ElementId.InvalidElementId;
        }

        private static XYZ GetElementCentre(Element el)
        {
            try
            {
                if (el.Location is LocationPoint lp && lp.Point != null) return lp.Point;
                if (el.Location is LocationCurve lc && lc.Curve != null)
                {
                    var c = lc.Curve;
                    return (c.GetEndPoint(0) + c.GetEndPoint(1)) * 0.5;
                }
                // A-9: floors, ceilings, roofs and most system families have no
                // LocationPoint and no LocationCurve, so both branches above
                // miss and this used to return null — which the caller fed
                // straight to IndependentTag.Create. The bounding-box centre is
                // the natural point for those categories and makes them
                // taggable rather than merely non-crashing. XYZ.Zero would have
                // satisfied the old comment but piled every tag at the project
                // origin, which is worse than skipping.
                var bb = el.get_BoundingBox(null);
                if (bb?.Min != null && bb.Max != null)
                    return (bb.Min + bb.Max) * 0.5;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GetElementCentre({el?.Id}): {ex.Message}");
            }
            // Null means "no usable point" — the caller skips and reports.
            return null;
        }

        // ─── Resolution helpers ──────────────────────────────────────────

        private static ElementId ResolveTagTypeId(Document doc, View view, AnnotationRulePack pack,
            string catKey, BuiltInCategory hostCategory, AnnotationRunStats stats = null)
        {
            ElementId result = ElementId.InvalidElementId;

            // 1. Named tag family from the rule pack
            if (pack.TagFamilies != null && pack.TagFamilies.TryGetValue(catKey, out var famName)
                && !string.IsNullOrWhiteSpace(famName))
            {
                var named = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(fs => string.Equals(fs.FamilyName, famName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // A-8: this matched by family name across EVERY category, so a
                // model family sharing the name could win and then throw once
                // per element inside IndependentTag.Create. Prefer a symbol in
                // the expected tag category. The name-only match is kept as a
                // second choice because TagCategoryFor falls through to the host
                // category for anything not in its switch, so an exact category
                // match is not always available — but a non-annotation match is
                // reported, since that is the case that fails per element.
                var wantCat = (long)TagCategoryFor(hostCategory);
                var byCat = named.FirstOrDefault(fs => fs.Category != null && fs.Category.Id.Value == wantCat);
                var chosen = byCat ?? named.FirstOrDefault();

                if (chosen != null)
                {
                    if (byCat == null && chosen.Category?.CategoryType != CategoryType.Annotation)
                        stats?.Warnings.Add(
                            $"Tag family '{famName}' for {catKey} is not an annotation family " +
                            $"(category '{chosen.Category?.Name ?? "none"}') — tagging will likely fail.");
                    result = chosen.Id;
                }
                else
                {
                    // Previously silent: a rule naming a family that isn't
                    // loaded fell straight through to "first loaded tag of the
                    // category", so the drawing quietly used the wrong tag.
                    // This is the mechanism that hides the known tagFamilies
                    // debt (~87 key mismatches, 19 nonexistent STING_TAG_*
                    // families) at runtime.
                    stats?.Warnings.Add(
                        $"Tag family '{famName}' declared for {catKey} is not loaded; " +
                        "falling back to the first loaded tag of that category.");
                }
            }

            // 2. First loaded tag of the host's tag category (project default)
            if (result == null || result == ElementId.InvalidElementId)
            {
                var fallback = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(fs => fs.Category != null
                        && fs.Category.CategoryType == CategoryType.Annotation
                        && fs.Category.Id.Value == (long)TagCategoryFor(hostCategory));
                if (fallback != null) result = fallback.Id;
            }

            // 3. CategoryTagStyles fallback: check the active view's DrawingType pack.
            if (result == null || result == ElementId.InvalidElementId)
            {
                try
                {
                    var dtId2 = view != null ? DrawingTypeStamper.Read(view) : null;
                    if (!string.IsNullOrEmpty(dtId2))
                    {
                        var pack2 = DrawingTypeRegistry.TryGetPack(doc, dtId2);
                        if (pack2?.CategoryTagStyles != null)
                        {
                            // Try the exact category name first, then BuiltInCategory string.
                            string catKey2 = Category.GetCategory(doc, hostCategory)?.Name ?? hostCategory.ToString();
                            if (!pack2.CategoryTagStyles.TryGetValue(catKey2, out var styleName))
                                pack2.CategoryTagStyles.TryGetValue(hostCategory.ToString(), out styleName);

                            if (!string.IsNullOrEmpty(styleName))
                            {
                                // Find any FamilySymbol whose name contains the style preset name.
                                var match = new FilteredElementCollector(doc)
                                    .OfClass(typeof(FamilySymbol))
                                    .Cast<FamilySymbol>()
                                    .FirstOrDefault(fs =>
                                        fs.Name.IndexOf(styleName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        (fs.Family?.Name ?? "").IndexOf(styleName, StringComparison.OrdinalIgnoreCase) >= 0);
                                if (match != null) result = match.Id;
                            }
                        }
                    }
                }
                catch { /* resolver must never throw */ }
            }

            return result ?? ElementId.InvalidElementId;
        }

        private static FamilySymbol FindFamilySymbolByName(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(s.FamilyName, name, StringComparison.OrdinalIgnoreCase));
        }

        // ── Decorative annotation: north arrow / scale bar / key plan / matchlines ──

        private static void RunDecorativeAnnotation(Document doc, View view, AnnotationRulePack pack, AnnotationRunOptions opts, AnnotationResult result)
        {
            BoundingBoxXYZ outline = null;
            try { outline = view.CropBox; } catch { }
            if (outline == null)
            {
                try
                {
                    var ol = view.Outline;
                    if (ol != null)
                    {
                        outline = new BoundingBoxXYZ
                        {
                            Min = new XYZ(ol.Min.U, ol.Min.V, 0),
                            Max = new XYZ(ol.Max.U, ol.Max.V, 0)
                        };
                    }
                }
                catch { }
            }
            if (outline == null) return;

            PlaceDecorativeIfDeclared(doc, view, pack.NorthArrowFamily, pack.NorthArrowPosition,
                pack.NorthArrowSizeMm, outline, result);
            PlaceDecorativeIfDeclared(doc, view, pack.ScaleBarFamily, pack.ScaleBarPosition,
                null, outline, result);
            PlaceDecorativeIfDeclared(doc, view, pack.KeyPlanFamily, pack.KeyPlanPosition,
                null, outline, result);

            if (pack.MatchlineOffsetMm.HasValue && view is ViewPlan vp && vp.CropBoxActive)
            {
                try
                {
                    var inset = pack.MatchlineOffsetMm.Value / 304.8;
                    var min = outline.Min;
                    var max = outline.Max;
                    var p00 = new XYZ(min.X + inset, min.Y + inset, 0);
                    var p10 = new XYZ(max.X - inset, min.Y + inset, 0);
                    var p11 = new XYZ(max.X - inset, max.Y - inset, 0);
                    var p01 = new XYZ(min.X + inset, max.Y - inset, 0);
                    foreach (var (a, b) in new[] { (p00, p10), (p10, p11), (p11, p01), (p01, p00) })
                    {
                        try { doc.Create.NewDetailCurve(view, Line.CreateBound(a, b)); }
                        catch (Exception ex) { result.Warnings.Add("Matchline detail curve: " + ex.Message); }
                    }
                }
                catch (Exception ex) { result.Warnings.Add("Matchline pass: " + ex.Message); }
            }
        }

        private static void PlaceDecorativeIfDeclared(Document doc, View view, string familyName, string position, double? sizeMm, BoundingBoxXYZ outline, AnnotationResult result)
        {
            if (string.IsNullOrEmpty(familyName)) return;
            try
            {
                var sym = FindFamilySymbolByName(doc, familyName);
                if (sym == null) { result.Warnings.Add($"Decorative family '{familyName}' not found — skipped."); return; }

                // C-4: the decorative pass had no idempotency either, so every
                // re-run stacked another north arrow / scale bar / key plan on
                // the view. One instance of a given family per view is the
                // whole intent, so an existing instance is the guard.
                if (ViewHasInstanceOfFamily(doc, view, sym))
                {
                    result.Skipped++;
                    return;
                }

                if (!sym.IsActive) { try { sym.Activate(); } catch { } }
                var inset = (sizeMm ?? 0) / 304.8;
                var pt = ResolvePositionPoint(outline, position, inset);
                doc.Create.NewFamilyInstance(pt, sym, view);
                result.DecorativePlaced++;
            }
            catch (Exception ex) { result.Warnings.Add($"Decorative '{familyName}': {ex.Message}"); }
        }

        /// <summary>
        /// True when this view already owns an instance of the given family
        /// symbol's family — the guard that keeps the decorative pass from
        /// re-stacking a north arrow / scale bar / key plan on every run.
        /// Matched on family rather than symbol so a type swap still counts.
        /// </summary>
        private static bool ViewHasInstanceOfFamily(Document doc, View view, FamilySymbol sym)
        {
            if (doc == null || view == null || sym == null) return false;
            try
            {
                var famId = sym.Family?.Id;
                if (famId == null) return false;
                foreach (var el in new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(FamilyInstance))
                    .WhereElementIsNotElementType())
                {
                    if (el is FamilyInstance fi && fi.Symbol?.Family?.Id == famId) return true;
                }
            }
            catch (Exception ex)
            {
                // Fail open: place it rather than silently omit a required
                // north arrow because the scan failed.
                StingLog.Warn($"ViewHasInstanceOfFamily('{sym.Name}'): {ex.Message}");
            }
            return false;
        }

        private static XYZ ResolvePositionPoint(BoundingBoxXYZ outline, string position, double inset)
        {
            if (outline == null) return XYZ.Zero;
            var min = outline.Min;
            var max = outline.Max;
            switch ((position ?? "BottomLeft").Trim())
            {
                case "BottomRight": return new XYZ(max.X - inset, min.Y + inset, 0);
                case "TopLeft":     return new XYZ(min.X + inset, max.Y - inset, 0);
                case "TopRight":    return new XYZ(max.X - inset, max.Y - inset, 0);
                default:            return new XYZ(min.X + inset, min.Y + inset, 0);
            }
        }

        // ── Spot annotation rules ──

        private static void RunSpotAnnotation(Document doc, View view, AnnotationRulePack pack, AnnotationRunOptions opts, AnnotationResult result)
        {
            ProcessSpotRules(doc, view, pack.SpotElevationRules, isCoordinate: false, result);
            ProcessSpotRules(doc, view, pack.SpotCoordinateRules, isCoordinate: true, result);
        }

        private static void ProcessSpotRules(Document doc, View view, List<SpotAnnotationRule> rules, bool isCoordinate, AnnotationResult result)
        {
            if (rules == null || rules.Count == 0) return;
            foreach (var r in rules)
            {
                if (r == null || string.IsNullOrEmpty(r.Category)) continue;
                try
                {
                    var catId = ResolveCategoryId(doc, r.Category);
                    if (catId == ElementId.InvalidElementId)
                    {
                        result.Warnings.Add($"Spot rule category '{r.Category}' not found.");
                        continue;
                    }

                    ElementId symbolId = ElementId.InvalidElementId;
                    if (!string.IsNullOrEmpty(r.SymbolFamily))
                    {
                        var s = FindFamilySymbolByName(doc, r.SymbolFamily);
                        if (s != null) symbolId = s.Id;
                    }

                    var elements = new FilteredElementCollector(doc, view.Id)
                        .OfCategoryId(catId)
                        .WhereElementIsNotElementType()
                        .ToList();

                    bool hasLeader = !string.Equals(r.LeaderStyle, "NoLeader", StringComparison.OrdinalIgnoreCase);

                    foreach (var el in elements)
                    {
                        try
                        {
                            var bb = el.get_BoundingBox(view);
                            if (bb == null) continue;
                            var origin = new XYZ((bb.Min.X + bb.Max.X) / 2.0, (bb.Min.Y + bb.Max.Y) / 2.0, bb.Max.Z);
                            var bend = origin + new XYZ(1, 1, 0);
                            var end  = origin + new XYZ(2, 1, 0);
                            var refPt= origin;
                            var faceRef = new Reference(el);
                            SpotDimension sd = isCoordinate
                                ? doc.Create.NewSpotCoordinate(view, faceRef, origin, bend, end, refPt, hasLeader)
                                : doc.Create.NewSpotElevation(view, faceRef, origin, bend, end, refPt, hasLeader);
                            if (symbolId != ElementId.InvalidElementId && sd != null)
                            {
                                try { sd.ChangeTypeId(symbolId); } catch { }
                            }
                            result.SpotsPlaced++;
                        }
                        catch (Exception ex) { result.Warnings.Add($"Spot {(isCoordinate ? "coord" : "elev")} '{r.Category}/{el.Id}': {ex.Message}"); }
                    }
                }
                catch (Exception ex) { result.Warnings.Add($"Spot rules '{r.Category}': {ex.Message}"); }
            }
        }

        // ── Resolve helpers ──

        private static ElementId ResolveCategoryId(Document doc, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return ElementId.InvalidElementId;
            try
            {
                if (Enum.TryParse<BuiltInCategory>(key, true, out var bic))
                {
                    var c = Category.GetCategory(doc, bic);
                    if (c != null) return c.Id;
                }
                foreach (Category c in doc.Settings.Categories)
                    if (string.Equals(c.Name, key, StringComparison.OrdinalIgnoreCase))
                        return c.Id;
            }
            catch { }
            return ElementId.InvalidElementId;
        }
    }
}
