using StingTools.Core;
// StingTools — Drawing Template Manager · Phase 137
//
// DrawingProducer is the engine that turns a (DrawingType, Context)
// pair into one or more views and (optionally) a sheet hosting them.
// Per-type ProductionRules drive multi-view production: one rule per
// produced view, each with optional per-rule overrides.
//
// Caller responsibility: open a Transaction (or TransactionGroup)
// before invoking ProduceAllViews. The producer does not open
// transactions itself.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace StingTools.Core.Drawing
{
    public sealed class DrawingContext
    {
        public Level Level { get; set; }
        public Element Room { get; set; }
        public Element ScopeBox { get; set; }
        public BoundingBoxXYZ CustomBounds { get; set; }
        public string Tag { get; set; }
        public string PackageId { get; set; }
    }

    public sealed class ProduceOptions
    {
        public bool CreateSheet { get; set; } = true;
        public bool PlaceOnSheet { get; set; } = true;
        public bool RunAnnotation { get; set; } = true;
        public bool DuplicateFromTemplate { get; set; } = false;
        public ViewDuplicateOption DuplicateOption { get; set; } = ViewDuplicateOption.Duplicate;
        public bool Idempotent { get; set; } = true;
        public string OverrideSheetNumber { get; set; }
        public string OverrideSheetName { get; set; }
        public DrawingProductionPreset Preset { get; set; }
    }

    public sealed class ProduceResult
    {
        public List<ElementId> ViewIds { get; } = new List<ElementId>();
        public ElementId SheetId { get; set; } = ElementId.InvalidElementId;
        public List<ElementId> ViewportIds { get; } = new List<ElementId>();
        public bool WasIdempotent { get; set; }
        /// <summary>
        /// P-9: views already on the sheet that this run left alone. Counted
        /// separately from ViewportIds so an idempotent re-run reads as
        /// "reused N" instead of emitting one warning per view.
        /// </summary>
        public int ViewportsReused { get; set; }
        /// <summary>P-9: true when the sheet already existed and was reused.</summary>
        public bool SheetReused { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class DrawingProducer
    {
        // GAP-L: per-batch caches for the existing-view + existing-sheet
        // lookups. ProduceAllViews repeatedly hits FilteredElementCollector
        // to find idempotent matches; on a 1000-sheet model this dominates
        // the runtime. The caches are populated lazily on first use within
        // the batch, validated against the active doc on every read, and
        // cleared by Reset() / the IDisposable scope returned by Prime().
        [ThreadStatic] private static Dictionary<string, ElementId> _existingViewCache;
        [ThreadStatic] private static Dictionary<string, ElementId> _existingSheetCache;
        [ThreadStatic] private static Dictionary<string, int>       _packageSheetCount;
        // GAP-L: the set of sheet numbers in use, primed once per batch so
        // EnsureUniqueSheetNumber doesn't re-collect every ViewSheet on each
        // assignment (was O(M²) across an M-sheet batch). Written back as each
        // number is assigned so later sheets in the same batch see it.
        [ThreadStatic] private static HashSet<string>               _sheetNumberCache;
        [ThreadStatic] private static string                        _cacheDocKey;
        // P-12: view names, collected once per batch. NameExists ran a full
        // OfClass(View) collector and MakeUniqueViewName calls it up to 100
        // times per view — O(views^2) on a first run over a large model.
        [ThreadStatic] private static HashSet<string>                _existingViewNames;
        // P-12: category name -> BuiltInCategory, built once per document.
        // Schedule rules resolved their category by iterating ~1,400 enum
        // members and calling Category.GetCategory on each, per rule.
        [ThreadStatic] private static Dictionary<string, BuiltInCategory> _categoryByName;

        private static string CacheDocKey(Document doc)
        {
            if (doc == null) return "__null__";
            try { return string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName; }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return "__unknown__"; }
        }

        private sealed class BatchCacheScope : IDisposable
        {
            public void Dispose() => DrawingProducer.ResetBatchCaches();
        }

        /// <summary>
        /// GAP-L: prime the per-batch lookup caches and return an
        /// IDisposable that resets them when the batch ends. Use with
        /// <c>using (DrawingProducer.PrimeBatchScope(doc)) { ... }</c>
        /// to guarantee the caches don't leak across commands.
        /// </summary>
        public static IDisposable PrimeBatchScope(Document doc)
        {
            PrimeBatchCaches(doc);
            return new BatchCacheScope();
        }

        /// <summary>
        /// GAP-L: prime the per-batch lookup caches. Call this once before
        /// a batch generation so idempotent lookups inside
        /// <see cref="ProduceAllViews"/> are O(1) instead of O(views).
        /// Calling Reset() drops the caches; a fresh prime rebuilds them.
        /// </summary>
        public static void PrimeBatchCaches(Document doc)
        {
            ResetBatchCaches();
            if (doc == null) return;
            _cacheDocKey = CacheDocKey(doc);
            try
            {
                var v = new Dictionary<string, ElementId>(StringComparer.Ordinal);
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(View)))
                {
                    if (!(el is View view) || view.IsTemplate) continue;
                    var dtId = StingTools.Core.ParameterHelpers.GetString(view, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID);
                    if (string.IsNullOrEmpty(dtId)) continue;
                    var ctxTag = StingTools.Core.ParameterHelpers.GetString(view, ParamRegistry.STING_VIEW_CONTEXT_TAG) ?? string.Empty;
                    var ruleIdx = StingTools.Core.ParameterHelpers.GetInt(view, ParamRegistry.STING_PRODUCTION_RULE_IDX, -1);
                    v[ViewKey(dtId, ctxTag, ruleIdx)] = view.Id;
                }
                _existingViewCache = v;

                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(View)))
                    if (el is View vn && !vn.IsTemplate && !string.IsNullOrEmpty(vn.Name)) names.Add(vn.Name);
                _existingViewNames = names;

                var s = new Dictionary<string, ElementId>(StringComparer.Ordinal);
                var pkg = new Dictionary<string, int>(StringComparer.Ordinal);
                var nums = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var sheet in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
                {
                    var dtId = StingTools.Core.ParameterHelpers.GetString(sheet, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID) ?? string.Empty;
                    var pkgId = StingTools.Core.ParameterHelpers.GetString(sheet, DrawingTypeStamper.PARAM_DRAWING_PACKAGE_ID) ?? string.Empty;
                    // null (parameter not bound) indexes as "" — the slow
                    // path in CreateOrFindSheet then distinguishes
                    // not-bound from bound-but-blank.
                    var shtCtx = DrawingTypeStamper.ReadSheetContext(sheet) ?? string.Empty;
                    if (!string.IsNullOrEmpty(dtId)) s[SheetKey(dtId, pkgId, shtCtx)] = sheet.Id;
                    if (pkg.TryGetValue(pkgId, out var n)) pkg[pkgId] = n + 1;
                    else pkg[pkgId] = 1;
                    // Same pass feeds the sheet-number cache — no extra collector.
                    try { if (!string.IsNullOrEmpty(sheet.SheetNumber)) nums.Add(sheet.SheetNumber); }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                }
                _existingSheetCache = s;
                _packageSheetCount  = pkg;
                _sheetNumberCache   = nums;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"DrawingProducer.PrimeBatchCaches: {ex.Message}");
            }
        }

        public static void ResetBatchCaches()
        {
            _existingViewCache  = null;
            _existingViewNames  = null;
            _categoryByName     = null;
            _existingSheetCache = null;
            _packageSheetCount  = null;
            _sheetNumberCache   = null;
            _cacheDocKey        = null;
        }

        // GAP-L: a cache slot only matches the doc it was primed against.
        // Cross-doc consultation returns null so the slow path takes over.
        private static bool CacheMatchesDoc(Document doc)
            => _cacheDocKey != null && string.Equals(_cacheDocKey, CacheDocKey(doc), StringComparison.OrdinalIgnoreCase);

        private static string ViewKey(string dtId, string ctxTag, int ruleIdx)
            => (dtId ?? string.Empty) + "|" + (ctxTag ?? string.Empty) + "|" + ruleIdx;

        // Sheet identity is (drawing type, package, production context).
        // Without the context component a per-level batch resolved every
        // level to the same sheet, so ProduceViewsPerLevelCommand over N
        // levels produced 1 sheet carrying N stacked viewports instead of
        // N sheets. The view key has always carried the context tag, which
        // is why the views were minted correctly and then all placed in
        // the same slot on the same sheet.
        private static string SheetKey(string dtId, string pkgId, string sheetCtx)
            => (dtId ?? string.Empty) + "|" + (pkgId ?? string.Empty) + "|" + (sheetCtx ?? string.Empty);

        public static ProduceResult ProduceView(Document doc, DrawingType dt, DrawingContext ctx, ProduceOptions opts)
            => ProduceAllViews(doc, dt, ctx, opts);

        public static ProduceResult ProduceAllViews(Document doc, DrawingType dt, DrawingContext ctx, ProduceOptions opts)
        {
            var result = new ProduceResult();
            if (doc == null || dt == null || ctx == null) return result;
            opts = opts ?? new ProduceOptions();

            var rules = (dt.ProductionRules != null && dt.ProductionRules.Count > 0)
                ? dt.ProductionRules.OrderBy(r => r.Idx).ToList()
                : new List<ProductionRule> { SynthesizeSingleRule(dt) };

            if (opts.CreateSheet)
                result.SheetId = CreateOrFindSheet(doc, dt, ctx, opts, result);

            // P1 — resolve the title-block family's slot grid once for this
            // sheet (null for norm-only profiles / no sheet) and reuse it across
            // every production rule instead of re-opening the family per view.
            SheetPlacementBridge.FamilySlotContext famCtx = null;
            if (opts.PlaceOnSheet && result.SheetId != ElementId.InvalidElementId)
                famCtx = SheetPlacementBridge.BuildFamilySlotContext(
                    doc, doc.GetElement(result.SheetId) as ViewSheet, dt, result);

            foreach (var rule in rules)
            {
                if (rule == null) continue;
                if (opts.Preset?.General?.GenerateOnlyDefault == true && rule.Idx > 0 && !rule.Required) continue;
                var viewId = ProduceSingleView(doc, dt, rule, ctx, opts, result);
                if (viewId == ElementId.InvalidElementId) continue;
                result.ViewIds.Add(viewId);
                StampViewParameters(doc, viewId, dt, rule, ctx);

                if (opts.PlaceOnSheet && result.SheetId != ElementId.InvalidElementId)
                {
                    var vpId = PlaceViewOnSheet(doc, result.SheetId, viewId, dt, rule, result, famCtx);
                    if (vpId != ElementId.InvalidElementId)
                    {
                        result.ViewportIds.Add(vpId);
                        StampAutoPlaced(doc, vpId);
                    }
                }
            }

            return result;
        }

        private static ProductionRule SynthesizeSingleRule(DrawingType dt)
        {
            string vt;
            switch ((dt.Purpose ?? "").Trim())
            {
                case DrawingPurpose.Plan:         vt = "FloorPlan"; break;
                case DrawingPurpose.Rcp:          vt = "RCP"; break;
                case DrawingPurpose.Section:      vt = "Section"; break;
                case DrawingPurpose.Elevation:    vt = "Elevation"; break;
                case DrawingPurpose.Detail:       vt = "Detail"; break;
                case DrawingPurpose.ThreeD:       vt = "ThreeD"; break;
                case DrawingPurpose.Schedule:     vt = "Schedule"; break;
                default:                          vt = "FloorPlan"; break;
            }
            return new ProductionRule { Idx = 0, ViewType = vt, Required = true, SlotIndex = 0 };
        }

        private static ElementId ProduceSingleView(Document doc, DrawingType dt, ProductionRule rule, DrawingContext ctx, ProduceOptions opts, ProduceResult result)
        {
            try
            {
                if (opts.Idempotent)
                {
                    var existing = FindExistingView(doc, dt.Id, ctx, rule.Idx);
                    if (existing != null)
                    {
                        result.WasIdempotent = true;
                        // GAP-H: re-apply the profile so a re-run after a
                        // profile edit refreshes scale / template / pack /
                        // stamps. SyncStyles flag (annotation off) avoids
                        // re-tagging an already-tagged view. Returning the
                        // raw id without Apply meant idempotent re-runs
                        // were a permanent no-op even after pack edits.
                        try
                        {
                            var refreshOpts = new DrawingTypePresentation.ApplyOptions
                            {
                                AnnotationOptions = new AnnotationRunOptions
                                {
                                    SkipAutoTag = true, SkipAutoDim = true,
                                    SkipDecorative = true, SkipSpots = true
                                },
                                SkipSymbolDriftCheck = true // idempotent refresh — batch path
                            };
                            var refreshed = DrawingTypePresentation.Apply(doc, existing, dt, refreshOpts);
                            result.Warnings.AddRange(refreshed.Warnings);
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Idempotent refresh ({existing.Id}): {ex.Message}");
                        }
                        return existing.Id;
                    }
                }

                var vft = ResolveViewFamilyType(doc, rule, result);
                if (vft == null) return ElementId.InvalidElementId;

                var viewId = CreateViewByType(doc, rule, ctx, dt, vft, result);
                if (viewId == ElementId.InvalidElementId) return viewId;

                var view = doc.GetElement(viewId) as View;
                if (view == null) return ElementId.InvalidElementId;

                try { view.Name = MakeUniqueViewName(doc, BuildViewName(dt, rule, ctx)); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (rule.ScaleOverride.HasValue) try { view.Scale = rule.ScaleOverride.Value; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

                var applyOpts = new DrawingTypePresentation.ApplyOptions
                {
                    AnnotationOptions = opts.RunAnnotation
                        ? new AnnotationRunOptions { ViewScale = view.Scale }
                        : new AnnotationRunOptions { SkipAutoTag = true, SkipAutoDim = true, SkipDecorative = true, SkipSpots = true },
                    SkipSymbolDriftCheck = true, // batch producer — drift via standalone command
                    ContextScopeBox = ctx?.ScopeBox
                };
                var presResult = DrawingTypePresentation.Apply(doc, view, dt, applyOpts);
                result.Warnings.AddRange(presResult.Warnings);

                if (opts.Preset?.VgOverrides != null &&
                    opts.Preset.VgOverrides.TryGetValue(dt.Id, out var presetVg) &&
                    presetVg != null && presetVg.Count > 0)
                {
                    var packResult = new PackApplyResult();
                    ViewStylePackApplier.ApplyPresetOverrides(doc, view, presetVg, packResult);
                    result.Warnings.AddRange(packResult.Warnings);
                }

                return viewId;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"ProduceSingleView({rule?.ViewType}): {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        private static ViewFamilyType ResolveViewFamilyType(Document doc, ProductionRule rule, ProduceResult result)
        {
            ViewFamily targetFamily;
            switch ((rule.ViewType ?? "").Trim())
            {
                case "FloorPlan":    targetFamily = ViewFamily.FloorPlan; break;
                case "RCP":
                case "CeilingPlan":  targetFamily = ViewFamily.CeilingPlan; break;
                case "Section":      targetFamily = ViewFamily.Section; break;
                case "Detail":       targetFamily = ViewFamily.Detail; break;
                case "Elevation":    targetFamily = ViewFamily.Elevation; break;
                case "ThreeD":       targetFamily = ViewFamily.ThreeDimensional; break;
                case "DraftingView": targetFamily = ViewFamily.Drafting; break;
                case "Schedule":     targetFamily = ViewFamily.Schedule; break;
                default:
                    result.Warnings.Add($"Unknown rule.ViewType '{rule.ViewType}'.");
                    return null;
            }
            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == targetFamily);
            if (vft == null)
                result.Warnings.Add($"No ViewFamilyType found for '{rule.ViewType}'.");
            return vft;
        }

        private static ElementId CreateViewByType(Document doc, ProductionRule rule, DrawingContext ctx, DrawingType dt, ViewFamilyType vft, ProduceResult result)
        {
            try
            {
                switch ((rule.ViewType ?? "").Trim())
                {
                    case "FloorPlan":
                    case "RCP":
                    case "CeilingPlan":
                        if (ctx.Level == null)
                        {
                            result.Warnings.Add($"{rule.ViewType} requires a Level — none in context.");
                            return ElementId.InvalidElementId;
                        }
                        return ViewPlan.Create(doc, vft.Id, ctx.Level.Id).Id;

                    case "Section":
                        var sectionBox = ctx.CustomBounds ?? BuildDefaultSectionBbox(ctx);
                        return ViewSection.CreateSection(doc, vft.Id, sectionBox).Id;

                    case "Detail":
                        var detailBox = ctx.CustomBounds ?? BuildDefaultDetailBbox(ctx);
                        return ViewSection.CreateDetail(doc, vft.Id, detailBox).Id;

                    case "Elevation":
                        if (ctx.Level == null && ctx.Room == null)
                        {
                            result.Warnings.Add("Elevation requires Level or Room context.");
                            return ElementId.InvalidElementId;
                        }
                        var origin = ResolveElevationOrigin(ctx);
                        var marker = ElevationMarker.CreateElevationMarker(doc, vft.Id, origin, dt.Scale > 0 ? dt.Scale : 100);
                        var ownerPlan = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewPlan))
                            .Cast<ViewPlan>()
                            .FirstOrDefault(v => !v.IsTemplate);
                        if (ownerPlan == null) { result.Warnings.Add("Elevation requires an owner FloorPlan view."); return ElementId.InvalidElementId; }
                        return marker.CreateElevation(doc, ownerPlan.Id, 0).Id;

                    case "ThreeD":
                        return View3D.CreateIsometric(doc, vft.Id).Id;

                    case "DraftingView":
                        return ViewDrafting.Create(doc, vft.Id).Id;

                    case "Schedule":
                    {
                        var cat = rule.ScheduleCategory;
                        if (string.IsNullOrWhiteSpace(cat))
                        {
                            StingTools.Core.StingLog.Warn($"DrawingProducer: Schedule rule on '{dt.Id}' has no scheduleCategory — skipping.");
                            result.Warnings.Add($"Schedule rule on '{dt.Id}' has no scheduleCategory — skipping.");
                            return ElementId.InvalidElementId;
                        }

                        // Resolve BuiltInCategory from the string name
                        // P-12: was a walk over ~1,400 BuiltInCategory members
                        // calling Category.GetCategory on each, inside a bare
                        // catch, once per schedule rule.
                        BuiltInCategory bic = ResolveCategoryByName(doc, cat);

                        if (bic == BuiltInCategory.INVALID)
                        {
                            StingTools.Core.StingLog.Warn($"DrawingProducer: scheduleCategory '{cat}' not recognised as a Revit category — skipping schedule creation.");
                            result.Warnings.Add($"scheduleCategory '{cat}' not recognised — skipping.");
                            return ElementId.InvalidElementId;
                        }

                        // Idempotent: find existing schedule with same category and STING stamp
                        var bicCatId = Category.GetCategory(doc, bic)?.Id;
                        if (bicCatId == null || bicCatId == ElementId.InvalidElementId)
                        {
                            StingTools.Core.StingLog.Warn($"DrawingProducer: could not resolve CategoryId for '{cat}'.");
                            result.Warnings.Add($"Could not resolve CategoryId for '{cat}'.");
                            return ElementId.InvalidElementId;
                        }

                        var existingSched = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewSchedule))
                            .Cast<ViewSchedule>()
                            .FirstOrDefault(vs =>
                                vs.Definition?.CategoryId?.Value == bicCatId.Value &&
                                string.Equals(
                                    StingTools.Core.ParameterHelpers.GetString(vs, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID),
                                    dt.Id,
                                    StringComparison.OrdinalIgnoreCase));
                        if (existingSched != null)
                            return existingSched.Id;

                        // Create fresh schedule
                        var schedule = ViewSchedule.CreateSchedule(doc, bicCatId);
                        schedule.Name = $"STING - {dt.Name ?? dt.Id}";

                        // Add declared fields if specified
                        if (rule.ScheduleFields?.Count > 0)
                        {
                            var schedDef = schedule.Definition;
                            var availableFields = schedDef.GetSchedulableFields();
                            foreach (var fieldName in rule.ScheduleFields)
                            {
                                var sf = availableFields.FirstOrDefault(f =>
                                    string.Equals(f.GetName(doc), fieldName, StringComparison.OrdinalIgnoreCase));
                                if (sf != null)
                                    schedDef.AddField(sf);
                                else
                                    result.Warnings.Add($"Schedule field '{fieldName}' not found for category '{cat}' — skipped.");
                            }
                        }

                        return schedule.Id;
                    }

                    default:
                        result.Warnings.Add($"CreateViewByType: unsupported '{rule.ViewType}'.");
                        return ElementId.InvalidElementId;
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"CreateViewByType('{rule.ViewType}'): {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        /// <summary>
        /// P-5: build a real section frame. THE single constructor for section
        /// bounding boxes — the producer's default and the "along grid lines"
        /// batch command both come here, because they previously disagreed on
        /// which axis carried height (producer: Y with Transform.Identity;
        /// command: Z, also with an implicit Identity) and neither built the
        /// frame that ViewSection.CreateSection actually consumes.
        ///
        /// CreateSection reads the box's Transform as the section's own
        /// coordinate system and its Min/Max as extents IN THAT FRAME:
        ///   BasisX — along the cut, i.e. the drawing's horizontal
        ///   BasisY — model +Z, i.e. the drawing's vertical
        ///   BasisZ — BasisX × BasisY, the horizontal direction the section
        ///            looks along
        /// With Transform.Identity the frame is the model frame, so BasisY is
        /// model +Y — horizontal — and the result is a downward "plan-section"
        /// rather than a vertical cut.
        ///
        /// <paramref name="bottomZ"/> / <paramref name="topZ"/> are absolute
        /// model elevations and are converted into frame-relative Y here, so
        /// callers never have to think about the frame. Extents are normalised
        /// so Min &lt; Max on every axis: the grid path derived its X extent by
        /// adding a perpendicular vector whose components go negative for
        /// north-south grids, which inverted the box and made CreateSection
        /// throw.
        /// </summary>
        internal static BoundingBoxXYZ BuildSectionBox(
            XYZ origin, XYZ cutDirection, double halfWidthFt,
            double bottomZ, double topZ, double depthFt)
        {
            origin = origin ?? XYZ.Zero;

            // Horizontal component of the cut direction; fall back to model +X
            // for a degenerate (vertical or zero-length) input.
            var flat = new XYZ(cutDirection?.X ?? 0, cutDirection?.Y ?? 0, 0);
            var bx = flat.GetLength() > 1e-9 ? flat.Normalize() : XYZ.BasisX;
            var by = XYZ.BasisZ;
            var bz = bx.CrossProduct(by);

            var t = Transform.Identity;
            t.Origin = origin;
            t.BasisX = bx;
            t.BasisY = by;
            t.BasisZ = bz;

            double halfW = Math.Abs(halfWidthFt);
            double depth = Math.Abs(depthFt);
            double yLo = Math.Min(bottomZ, topZ) - origin.Z;
            double yHi = Math.Max(bottomZ, topZ) - origin.Z;
            if (yHi - yLo < 1e-6) yHi = yLo + 1.0;   // never a zero-height box

            return new BoundingBoxXYZ
            {
                Transform = t,
                Min = new XYZ(-halfW, yLo, -depth),
                Max = new XYZ( halfW, yHi, 0.0),
            };
        }

        private static BoundingBoxXYZ BuildDefaultSectionBbox(DrawingContext ctx)
        {
            // 10 m wide × 5 m tall × 10 m deep default cut at the context
            // level's elevation, looking along model +Y.
            const double mToFt = 1.0 / 0.3048;
            double elevFt = ctx?.Level?.Elevation ?? 0;
            return BuildSectionBox(
                origin:       new XYZ(0, 0, elevFt),
                cutDirection: XYZ.BasisX,
                halfWidthFt:  5.0 * mToFt,
                bottomZ:      elevFt - 1.0 * mToFt,
                topZ:         elevFt + 4.0 * mToFt,
                depthFt:      10.0 * mToFt);
        }

        private static BoundingBoxXYZ BuildDefaultDetailBbox(DrawingContext ctx)
        {
            double elevFt = ctx?.Level?.Elevation ?? 0;
            return new BoundingBoxXYZ
            {
                Transform = Transform.Identity,
                Min = new XYZ(-1.0 * 3.281, elevFt - 0.5 * 3.281, -1.0 * 3.281),
                Max = new XYZ( 1.0 * 3.281, elevFt + 1.5 * 3.281,  1.0 * 3.281)
            };
        }

        private static XYZ ResolveElevationOrigin(DrawingContext ctx)
        {
            try
            {
                if (ctx.Room is FamilyInstance fi)
                {
                    var lp = fi.Location as LocationPoint;
                    if (lp != null) return lp.Point;
                }
                if (ctx.Room?.Location is LocationPoint lpr) return lpr.Point;
                if (ctx.Level != null) return new XYZ(0, 0, ctx.Level.Elevation);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return XYZ.Zero;
        }

        private static ElementId CreateOrFindSheet(Document doc, DrawingType dt, DrawingContext ctx, ProduceOptions opts, ProduceResult result)
        {
            string effectivePackage = ctx.PackageId ?? dt.PackageId ?? "";
            string sheetCtx = BuildContextTag(ctx);
            try
            {
                // GAP-L: per-batch cache hit, fall back to fresh collector.
                if (_existingSheetCache != null
                    && CacheMatchesDoc(doc)
                    && _existingSheetCache.TryGetValue(SheetKey(dt.Id, effectivePackage, sheetCtx), out var cachedSheetId))
                {
                    if (doc.GetElement(cachedSheetId) is ViewSheet vsCached && vsCached.IsValidObject)
                    {
                        result.SheetReused = true;   // P-9: reuse is not production
                        return vsCached.Id;
                    }
                    _existingSheetCache.Remove(SheetKey(dt.Id, effectivePackage, sheetCtx));
                }

                var candidates = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s =>
                        string.Equals(StingTools.Core.ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID), dt.Id, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(StingTools.Core.ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_PACKAGE_ID) ?? "", effectivePackage, StringComparison.Ordinal))
                    .ToList();

                // Same drawing type, same package, same production context.
                var exact = candidates.FirstOrDefault(s =>
                    string.Equals(DrawingTypeStamper.ReadSheetContext(s), sheetCtx, StringComparison.Ordinal));
                if (exact != null) { result.SheetReused = true; return exact.Id; }

                // A sheet produced before the context stamp existed carries
                // no context. Claim it only for an empty-context request —
                // a per-level request must never adopt it, or level 1 would
                // swallow the whole batch exactly as before.
                if (string.IsNullOrEmpty(sheetCtx))
                {
                    var legacyBlank = candidates.FirstOrDefault(s =>
                        string.IsNullOrEmpty(DrawingTypeStamper.ReadSheetContext(s)));
                    if (legacyBlank != null) { result.SheetReused = true; return legacyBlank.Id; }
                }

                // ReadSheetContext returns null when STING_SHEET_CONTEXT_TXT
                // is not bound in this project at all, so contexts cannot be
                // told apart. Fall back to the pre-context (type, package)
                // match: that reproduces the old stacking behaviour, but the
                // alternative is minting a fresh duplicate sheet on every
                // run. Surfaced as a warning so the fix is actionable.
                var unstampable = candidates.FirstOrDefault(s => DrawingTypeStamper.ReadSheetContext(s) == null);
                if (unstampable != null)
                {
                    result.Warnings.Add(
                        $"{DrawingTypeStamper.PARAM_SHEET_CONTEXT} is not bound in this project, so sheets cannot be " +
                        $"matched per level / scope box. Reusing sheet {unstampable.Id} for context '{sheetCtx}'. " +
                        "Run LoadSharedParams to bind it, then re-run production.");
                    result.SheetReused = true;
                    return unstampable.Id;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            ElementId titleBlockId = ElementId.InvalidElementId;
            try
            {
                var (declaredFamily, tbSymbol) = DrawingDispatcher.ResolveTitleBlockVariant(dt);
                // P5 — map the profile's logical / dangling family name to the
                // concrete built family (STING_TB_<size>[_PORT]_<BIM|NONBIM>_v2.0
                // / …_ASSEMBLY_*_v1.0 / …_PRESENT_A1_v1.0) before looking it up.
                // ToConcreteFamily is best-effort: it returns the declared name
                // unchanged for A2/A4 and for unknown vocabulary, and can return
                // blank when the profile declares no family and no size can be
                // derived. So treat its output as possibly-still-logical.
                var tbFamily = TitleBlockResolver.ToConcreteFamily(doc, dt, declaredFamily);

                // A blank family name used to satisfy the match predicate via an
                // `IsNullOrEmpty(tbFamily) ||` clause, so ANY loaded title block
                // matched and the first one won — silently, before any fallback
                // warning could fire. Blank now matches nothing and is reported.
                bool haveName = !string.IsNullOrWhiteSpace(tbFamily);
                if (!haveName)
                    result.Warnings.Add(
                        $"Drawing type '{dt.Id}' resolved no title-block family name " +
                        $"(declared '{declaredFamily ?? ""}'). The sheet will use whatever title block " +
                        "is available — set titleBlockFamily on the profile, or a paper size the " +
                        "resolver can derive from.");

                List<FamilySymbol> CollectMatches() => !haveName
                    ? new List<FamilySymbol>()
                    : new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .Cast<FamilySymbol>()
                        .Where(s => string.Equals(s.FamilyName, tbFamily, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                var familyMatches = CollectMatches();
                // P5 delivery — if the concrete family was built but not yet
                // loaded, load it from Families/TitleBlocks/ on demand.
                if (familyMatches.Count == 0 && haveName
                    && TitleBlockResolver.EnsureFamilyLoaded(doc, tbFamily))
                    familyMatches = CollectMatches();

                FamilySymbol picked = null;
                if (!string.IsNullOrWhiteSpace(tbSymbol))
                {
                    picked = familyMatches.FirstOrDefault(s =>
                        string.Equals(s.Name, tbSymbol, StringComparison.OrdinalIgnoreCase));
                    if (picked == null && familyMatches.Count > 0)
                        result.Warnings.Add(
                            $"Title-block type '{tbSymbol}' not found in family '{tbFamily}' " +
                            $"for drawing type '{dt.Id}'; used '{familyMatches[0].Name}' instead.");
                }
                if (picked == null) picked = familyMatches.FirstOrDefault();
                titleBlockId = picked?.Id ?? ElementId.InvalidElementId;

                if (titleBlockId == ElementId.InvalidElementId)
                {
                    // The comment here used to claim "the producer never falls
                    // back to an arbitrary title block" immediately above code
                    // that did exactly that, with no warning — so a batch could
                    // issue sheets carrying the wrong corporate identity and
                    // report success. Still falls back (a sheet with some title
                    // block beats no sheet), but says so.
                    var any = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .Cast<FamilySymbol>()
                        .FirstOrDefault();
                    titleBlockId = any?.Id ?? ElementId.InvalidElementId;
                    if (any != null && haveName)
                        result.Warnings.Add(
                            $"Title-block family '{tbFamily}' (drawing type '{dt.Id}') is not loaded and " +
                            $"could not be loaded from disk; fell back to '{any.FamilyName}'. " +
                            "The sheet carries the wrong corporate identity until the family is loaded.");
                    else if (any == null)
                        result.Warnings.Add(
                            $"No title-block family is loaded in this project; sheet for drawing type " +
                            $"'{dt.Id}' was created without one.");
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            ViewSheet sheet;
            try { sheet = ViewSheet.Create(doc, titleBlockId); }
            catch (Exception ex) { result.Warnings.Add($"CreateSheet: {ex.Message}"); return ElementId.InvalidElementId; }

            // The sequence has to be resolved BEFORE the number is built —
            // the pattern's {seq} / {seq:Dn} needs it. It used to be consumed
            // further down, after numbering, and only stamped into
            // STING_SHEET_SEQUENCE_INT, so {seq} fell back to parsing
            // ctx.Tag — a level name in every batch command — and every sheet
            // in a package numbered 0001.
            int seq = ResolveSheetSequence(doc, dt, effectivePackage);

            // One token dict for the number, the name and the title-block
            // cells, built with the REAL doc handle so {project} /
            // {originator} resolve from ProjectInformation instead of coming
            // back blank.
            var tokens = BuildTokenDict(doc, dt, ctx, seq);

            try
            {
                var number = opts.OverrideSheetNumber ?? SubstituteTokens(dt.SheetNumberPattern, dt, ctx, seq, tokens);
                // Revit rejects a duplicate sheet number outright, and the
                // catch below would leave the sheet on its default number.
                sheet.SheetNumber = EnsureUniqueSheetNumber(doc, number, sheet.Id, result);
            }
            catch (Exception ex) { result.Warnings.Add($"SheetNumber: {ex.Message}"); }
            try
            {
                sheet.Name = opts.OverrideSheetName ?? SubstituteTokens(dt.SheetNamePattern, dt, ctx, seq, tokens);
            }
            catch (Exception ex) { result.Warnings.Add($"SheetName: {ex.Message}"); }

            // FIX-6: lock check + stale-key clear + stamp + sequence + title-block
            // params all go through the canonical ApplyToSheet plus the
            // producer-specific sequence stamp. Avoids re-implementing the
            // stamper / applier sequence in two places.
            if (DrawingTypeStamper.IsLocked(sheet))
            {
                result.Warnings.Add($"Sheet {sheet.Id} style-locked; producer kept sheet but skipped stamp/apply.");
                return sheet.Id;
            }
            DrawingTypeStamper.Stamp(sheet, dt.Id);
            DrawingTypeStamper.StampPackage(sheet, effectivePackage);
            // Completes the sheet's production identity so the next run
            // finds this exact sheet for this exact context instead of
            // reusing it for every level in the batch.
            if (!DrawingTypeStamper.StampSheetContext(sheet, sheetCtx) && !string.IsNullOrEmpty(sheetCtx))
                result.Warnings.Add(
                    $"Could not stamp {DrawingTypeStamper.PARAM_SHEET_CONTEXT} on sheet {sheet.Id} " +
                    $"(context '{sheetCtx}'); re-running production may not match this sheet. " +
                    "Run LoadSharedParams to bind the parameter.");

            try
            {
                DrawingTypeStamper.StampSheetSequence(sheet, seq);
                // Newly-created sheet should be discoverable next time.
                if (_existingSheetCache != null)
                    _existingSheetCache[SheetKey(dt.Id, effectivePackage, sheetCtx)] = sheet.Id;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            if (dt.TitleBlockParams != null && dt.TitleBlockParams.Count > 0)
            {
                try
                {
                    var tbResult = TitleBlockParamApplier.Apply(doc, sheet, dt, tokens);
                    foreach (var w in tbResult.Warnings)
                        result.Warnings.Add("TitleBlockParams: " + w);
                }
                catch (Exception ex2) { result.Warnings.Add($"TitleBlockParams: {ex2.Message}"); }
            }

            return sheet.Id;
        }

        private static ElementId PlaceViewOnSheet(Document doc, ElementId sheetId, ElementId viewId, DrawingType dt, ProductionRule rule, ProduceResult result, SheetPlacementBridge.FamilySlotContext famCtx = null)
        {
            try
            {
                // P-9: on an idempotent re-run ProduceSingleView returns the
                // EXISTING view, which is already on this sheet. Placing it
                // again threw inside Viewport.Create and surfaced as a warning
                // per view per re-run — noise that made a correct no-op look
                // like a failure. Detect it first and report reuse instead.
                if (IsViewAlreadyOnSheet(doc, sheetId, viewId, out var existingVpId))
                {
                    result.ViewportsReused++;
                    return existingVpId;
                }

                var sp = SheetPlacementBridge.ResolveSlot(doc, sheetId, dt,
                    rule.SlotIndex >= 0 ? rule.SlotIndex : 0, result, famCtx);
                var pt = sp?.Center;
                if (pt == null)
                {
                    var sheet = doc.GetElement(sheetId) as ViewSheet;
                    var bb = sheet?.Outline;
                    pt = bb != null ? new XYZ((bb.Min.U + bb.Max.U) / 2.0, (bb.Min.V + bb.Max.V) / 2.0, 0) : XYZ.Zero;
                }
                // P12.A — fit the view to its slot before placement, unless the
                // production rule pins an explicit scale override.
                if (sp != null && !rule.ScaleOverride.HasValue
                    && doc.GetElement(viewId) is View vFit)
                    SheetPlacementBridge.ApplyFitScale(doc, vFit, sp);

                // SLOT-3: warn on a view/slot type mismatch rather than
                // placing it silently into the wrong slot.
                if (sp?.Slot != null && !string.IsNullOrWhiteSpace(sp.Slot.ViewType)
                    && doc.GetElement(viewId) is View vChk
                    && !string.Equals(vChk.ViewType.ToString(), sp.Slot.ViewType, StringComparison.OrdinalIgnoreCase))
                {
                    result.Warnings.Add(
                        $"View '{vChk.Name}' ({vChk.ViewType}) placed into slot '{sp.Slot.Label}' " +
                        $"which expects '{sp.Slot.ViewType}' — type mismatch.");
                }

                // AUTO-3: a ViewSchedule cannot be placed with Viewport.Create —
                // it throws. The producer called Viewport.Create unconditionally,
                // so every schedule its own ProductionRules created was
                // impossible to place: the rule minted a view that could never
                // reach a sheet. Schedules need ScheduleSheetInstance.
                // These three behaviours existed only in
                // SheetPlacementBridge.PlaceAccordingToSlots, which the producer
                // never calls; ported rather than restructured because the
                // bridge is a batch API and routing through it would also pull
                // in the P-7 slot-origin convention divergence.
                if (doc.GetElement(viewId) is ViewSchedule scheduleView)
                {
                    try
                    {
                        var ssi = ScheduleSheetInstance.Create(doc, sheetId, scheduleView.Id, pt);
                        if (ssi != null)
                        {
                            try { StingTools.Core.ParameterHelpers.SetInt(ssi, ParamRegistry.STING_AUTO_PLACED_BOOL, 1, overwrite: true); }
                            catch (Exception ex) { StingLog.Warn($"AutoPlaced stamp: {ex.Message}"); }
                            return ssi.Id;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"ScheduleSheetInstance.Create('{scheduleView.Name}'): {ex.Message}");
                    }
                    return ElementId.InvalidElementId;
                }

                var vp = Viewport.Create(doc, sheetId, viewId, pt);
                if (vp == null) return ElementId.InvalidElementId;

                try { StingTools.Core.ParameterHelpers.SetInt(vp, ParamRegistry.STING_AUTO_PLACED_BOOL, 1, overwrite: true); }
                catch (Exception ex) { StingLog.Warn($"AutoPlaced stamp: {ex.Message}"); }

                // SLOT-1: per-slot viewport type override.
                if (!string.IsNullOrWhiteSpace(sp?.Slot?.ViewportType))
                {
                    var vpTypeId = SheetPlacementBridge.ResolveViewportTypeId(doc, sp.Slot.ViewportType);
                    if (vpTypeId != null && vpTypeId != ElementId.InvalidElementId)
                    {
                        try { vp.ChangeTypeId(vpTypeId); }
                        catch (Exception ex) { result.Warnings.Add($"Viewport type '{sp.Slot.ViewportType}': {ex.Message}"); }
                    }
                    else
                    {
                        result.Warnings.Add(
                            $"Viewport type '{sp.Slot.ViewportType}' not found — slot '{sp.Slot.Label}' uses the default.");
                    }
                }
                return vp.Id;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"PlaceViewOnSheet: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        /// <summary>
        /// True when this view already has a viewport (or schedule instance)
        /// on this sheet. Viewport.CanAddViewToSheet is the canonical test;
        /// the collector then recovers the existing element's id so callers
        /// can report reuse rather than re-place.
        /// </summary>
        private static bool IsViewAlreadyOnSheet(Document doc, ElementId sheetId, ElementId viewId, out ElementId viewportId)
        {
            viewportId = ElementId.InvalidElementId;
            try
            {
                // Schedules are ScheduleSheetInstance, not Viewport, and
                // CanAddViewToSheet does not describe them.
                if (doc.GetElement(viewId) is ViewSchedule)
                {
                    foreach (var el in new FilteredElementCollector(doc, sheetId)
                        .OfClass(typeof(ScheduleSheetInstance)))
                    {
                        if (el is ScheduleSheetInstance ssi && ssi.ScheduleId == viewId)
                        {
                            viewportId = ssi.Id;
                            return true;
                        }
                    }
                    return false;
                }

                if (Viewport.CanAddViewToSheet(doc, sheetId, viewId)) return false;

                foreach (var el in new FilteredElementCollector(doc, sheetId).OfClass(typeof(Viewport)))
                {
                    if (el is Viewport vp && vp.ViewId == viewId)
                    {
                        viewportId = vp.Id;
                        return true;
                    }
                }
                // CanAddViewToSheet said no but no viewport on THIS sheet owns
                // it — the view is placed on a different sheet. Not reuse;
                // let the normal path run and report the real failure.
                return false;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"IsViewAlreadyOnSheet({viewId}): {ex.Message}");
                return false;   // fail open — attempt the placement
            }
        }

        private static void StampViewParameters(Document doc, ElementId viewId, DrawingType dt, ProductionRule rule, DrawingContext ctx)
        {
            var view = doc.GetElement(viewId);
            if (view == null) return;
            try { StingTools.Core.ParameterHelpers.SetString(view, ParamRegistry.STING_VIEW_CONTEXT_TAG, BuildContextTag(ctx), overwrite: true); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            try { StingTools.Core.ParameterHelpers.SetString(view, ParamRegistry.STING_DRAWING_PACKAGE_ID, ctx.PackageId ?? dt.PackageId ?? "", overwrite: true); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            try { StingTools.Core.ParameterHelpers.SetInt(view, ParamRegistry.STING_PRODUCTION_RULE_IDX, rule.Idx, overwrite: true); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        private static void StampAutoPlaced(Document doc, ElementId vpId)
        {
            var el = doc.GetElement(vpId);
            if (el == null) return;
            try { StingTools.Core.ParameterHelpers.SetInt(el, ParamRegistry.STING_AUTO_PLACED_BOOL, 1, overwrite: true); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        private static string BuildContextTag(DrawingContext ctx)
        {
            string lvl = ctx?.Level?.Name ?? "";
            string room = "";
            try { room = ctx?.Room?.Id?.ToString() ?? ""; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            // P-6: the scope box is part of the context's identity. Without it,
            // two scope boxes on the same level with no ctx.Tag produced the
            // same key, so the second box matched the first box's view and
            // silently produced nothing. Appended rather than inserted so
            // existing per-level stamps (no scope box) keep their current key
            // and stay idempotent across this change.
            string sbox = "";
            try { sbox = ctx?.ScopeBox?.Name ?? ""; } catch (Exception ex) { StingLog.Warn($"BuildContextTag scope box: {ex.Message}"); }

            var tag = $"{lvl}::{room}::{ctx?.Tag ?? ""}";
            return string.IsNullOrEmpty(sbox) ? tag : tag + "::" + sbox;
        }

        private static View FindExistingView(Document doc, string dtId, DrawingContext ctx, int ruleIdx)
        {
            try
            {
                var ctxTag = BuildContextTag(ctx);
                // GAP-L: O(1) hit when the per-batch index is primed for
                // this document. Cross-doc consultation falls through.
                if (_existingViewCache != null
                    && CacheMatchesDoc(doc)
                    && _existingViewCache.TryGetValue(ViewKey(dtId, ctxTag, ruleIdx), out var cachedId))
                {
                    if (doc.GetElement(cachedId) is View vCached
                        && vCached.IsValidObject && !vCached.IsTemplate)
                        return vCached;
                    _existingViewCache.Remove(ViewKey(dtId, ctxTag, ruleIdx));
                }
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(v => !v.IsTemplate &&
                        string.Equals(StingTools.Core.ParameterHelpers.GetString(v, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID), dtId, StringComparison.OrdinalIgnoreCase) &&
                        StingTools.Core.ParameterHelpers.GetInt(v, ParamRegistry.STING_PRODUCTION_RULE_IDX, -1) == ruleIdx &&
                        string.Equals(StingTools.Core.ParameterHelpers.GetString(v, ParamRegistry.STING_VIEW_CONTEXT_TAG), ctxTag, StringComparison.Ordinal));
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        private static string BuildViewName(DrawingType dt, ProductionRule rule, DrawingContext ctx)
        {
            string ctxLabel = ctx?.Level?.Name
                ?? (ctx?.Room != null ? StingTools.Core.ParameterHelpers.GetString(ctx.Room, "Number") : null)
                ?? ctx?.Tag
                ?? "";
            string raw = $"{dt.Name} - {ctxLabel}{rule.NameSuffix ?? ""}".Trim();
            return SanitizeViewName(raw);
        }

        private static string SanitizeViewName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            var bad = new[] { '{', '}', '[', ']', '|', ':', ';', '<', '>', '?', '\\', '/' };
            foreach (var c in bad) raw = raw.Replace(c, '-');
            return raw.Trim();
        }

        private static string MakeUniqueViewName(Document doc, string baseName)
        {
            string name = baseName;
            int n = 2;
            while (NameExists(doc, name) && n < 100) name = $"{baseName}_({n++})";
            // P-12: keep the batch name set current so the next probe in this
            // run sees this name without another collector pass.
            if (_existingViewNames != null && CacheMatchesDoc(doc)) _existingViewNames.Add(name);
            return name;
        }

        private static bool NameExists(Document doc, string name)
        {
            // P-12: O(1) against the batch name set when primed for this doc.
            if (_existingViewNames != null && CacheMatchesDoc(doc))
                return _existingViewNames.Contains(name);
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Any(v => !v.IsTemplate && string.Equals(v.Name, name, StringComparison.Ordinal));
            }
            catch (Exception ex) { StingLog.Warn($"NameExists('{name}'): {ex.Message}"); return false; }
        }

        /// <summary>
        /// P-12: category-name lookup built once per document instead of
        /// iterating every BuiltInCategory member per schedule rule.
        /// </summary>
        private static BuiltInCategory ResolveCategoryByName(Document doc, string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return BuiltInCategory.INVALID;
            if (_categoryByName == null || !CacheMatchesDoc(doc))
            {
                var map = new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (Category c in doc.Settings.Categories)
                    {
                        if (string.IsNullOrEmpty(c?.Name)) continue;
                        try
                        {
                            var bic = (BuiltInCategory)c.Id.Value;
                            if (!map.ContainsKey(c.Name)) map[c.Name] = bic;
                        }
                        catch (Exception ex) { StingLog.Warn($"Category map '{c.Name}': {ex.Message}"); }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"ResolveCategoryByName map: {ex.Message}"); }
                _categoryByName = map;
            }
            return _categoryByName.TryGetValue(categoryName, out var hit) ? hit : BuiltInCategory.INVALID;
        }

        /// <summary>
        /// Resolve the next sheet sequence for this (drawing type, package).
        /// Extracted so numbering can consume it before the sheet number is
        /// built. Behaviour is unchanged: persisted ES counter first, then the
        /// per-batch cache, then a package sheet count.
        /// </summary>
        private static int ResolveSheetSequence(Document doc, DrawingType dt, string effectivePackage)
        {
            // Phase 169 — persisted sequence counter via ExtensibleStorage on
            // ProjectInfo, granular by (DT, package, discipline, vol). Falls
            // back to the per-batch cache (and ultimately a sheet count) when
            // ES is unavailable. Survives Revit restarts and the renumber
            // command's compaction so deleted sheets don't regrow gaps.
            try
            {
                return SheetSequenceStore.Next(doc, dt.Id, effectivePackage,
                    dt.Discipline ?? "", dt.IsoNaming?.Volume ?? "");
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"SheetSequenceStore.Next: {ex.Message}");
            }

            // Legacy fallback path — preserves prior behaviour for documents
            // where ExtensibleStorage isn't writable.
            try
            {
                if (_packageSheetCount != null && CacheMatchesDoc(doc))
                {
                    _packageSheetCount.TryGetValue(effectivePackage, out var n);
                    var next = n + 1;
                    _packageSheetCount[effectivePackage] = next;
                    return next;
                }
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Count(s => string.Equals(StingTools.Core.ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_PACKAGE_ID) ?? "", effectivePackage, StringComparison.Ordinal));
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ResolveSheetSequence fallback: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Revit rejects a duplicate sheet number, so a collision would throw
        /// and leave the sheet on its auto-assigned default. Mirrors
        /// ShopDrawingComposer.EnsureUniqueSheetNumber (-A … -Z, then a short
        /// random suffix) and additionally ignores the sheet being numbered.
        /// </summary>
        private static string EnsureUniqueSheetNumber(Document doc, string baseNumber, ElementId excludeId, ProduceResult result)
        {
            if (string.IsNullOrEmpty(baseNumber)) return baseNumber;

            // GAP-L: reuse the per-batch sheet-number set when it is primed for
            // this doc, so an M-sheet batch no longer re-collects every ViewSheet
            // on each assignment (was O(M²)). The number chosen below is written
            // back into the cache so a later sheet in the same batch sees it —
            // matching the old per-call scan, which saw sheets numbered earlier
            // in the same run. The just-created sheet (excludeId) carries only a
            // default number that was never added to the cache, so excluding it
            // is implicit. Falls back to a fresh scan when no batch cache is live.
            bool useCache = _sheetNumberCache != null && CacheMatchesDoc(doc);
            HashSet<string> existing;
            if (useCache)
            {
                existing = _sheetNumberCache;
            }
            else
            {
                existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)))
                    {
                        if (el is ViewSheet vs && vs.Id != excludeId && !string.IsNullOrEmpty(vs.SheetNumber))
                            existing.Add(vs.SheetNumber);
                    }
                }
                catch (Exception ex)
                {
                    StingTools.Core.StingLog.Warn($"EnsureUniqueSheetNumber: {ex.Message}");
                    return baseNumber;
                }
            }

            string chosen;
            if (!existing.Contains(baseNumber))
            {
                chosen = baseNumber;
            }
            else
            {
                chosen = null;
                for (char c = 'A'; c <= 'Z'; c++)
                {
                    var candidate = baseNumber + "-" + c;
                    if (!existing.Contains(candidate))
                    {
                        result?.Warnings.Add($"Sheet number '{baseNumber}' already exists; used '{candidate}'.");
                        chosen = candidate;
                        break;
                    }
                }
                if (chosen == null)
                {
                    chosen = baseNumber + "-" + Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
                    result?.Warnings.Add($"Sheet number '{baseNumber}' and all -A..-Z variants exist; used '{chosen}'.");
                }
            }

            if (useCache) _sheetNumberCache.Add(chosen);
            return chosen;
        }

        private static readonly System.Text.RegularExpressions.Regex _seqWidthRegex
            = new System.Text.RegularExpressions.Regex(@"\{seq:D(\d+)\}",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string SubstituteTokens(string pattern, DrawingType dt, DrawingContext ctx,
            int seq, IDictionary<string, string> extras)
            => ApplyTokenPattern(
                pattern,
                disc:    dt?.Discipline ?? "",
                lvl:     ctx?.Level?.Name ?? "",
                sys:     dt?.System ?? "",   // P4 — system code into {sys} for number/name patterns
                mark:    ctx?.Tag ?? "",
                spool:   ctx?.Tag ?? "",
                purpose: dt?.Purpose ?? "",
                seq:     seq,
                extras:  extras);

        /// <summary>
        /// The token substitution itself, with no Revit types in its
        /// signature so it can be exercised outside Revit. Callers resolve
        /// the field values; this only does the string work.
        /// </summary>
        internal static string ApplyTokenPattern(string pattern,
            string disc, string lvl, string sys, string mark, string spool, string purpose,
            int seq, IDictionary<string, string> extras)
        {
            if (string.IsNullOrEmpty(pattern)) return pattern;

            var p = pattern;
            // Producer-specific shaping first (SafeShort sanitises and caps at
            // 8 chars). Doing these before the extras sweep keeps the existing
            // behaviour for these six tokens rather than letting the raw
            // dictionary values through.
            p = p.Replace("{disc}", SafeShort(disc));
            p = p.Replace("{discipline}", disc);
            p = p.Replace("{lvl}", SafeShort(lvl));
            p = p.Replace("{sys}", SafeShort(sys));
            p = p.Replace("{mark}", SafeShort(mark));
            p = p.Replace("{spool}", SafeShort(spool));
            p = p.Replace("{purpose}", purpose ?? "");

            // ISO 19650 tokens — {project} {originator} {vol} {type} {role}
            // {suit} {rev} and anything else the canonical builder supplies.
            // 13 corporate drawing types carry these in their
            // sheetNumberPattern; the producer knew none of them, so they
            // survived as literal braces, which are illegal in a Revit sheet
            // number — the assignment threw, was caught, and the sheet kept
            // its default number. Same sweep ShopDrawingComposer already did.
            if (extras != null)
            {
                foreach (var kv in extras)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    p = p.Replace("{" + kv.Key + "}", kv.Value ?? "");
                }
            }

            // {seq:Dn} at whatever width the pattern asks for, then bare {seq}
            // at the historical 4-digit default.
            //
            // Note the extras sweep above may already have consumed a bare
            // {seq}: DrawingTokenContext.Build emits a "seq" key formatted
            // with its seqWidth parameter, which defaults to 4 and which
            // BuildTokenDict does not override — so the two paths agree, and
            // whichever runs first yields the same string. {seq:Dn} is a
            // different literal so the sweep never touches it. If a caller
            // ever passes a non-default seqWidth, format that value the same
            // way here or the two paths will silently disagree.
            p = _seqWidthRegex.Replace(p, m => seq.ToString("D" + m.Groups[1].Value));
            p = p.Replace("{seq}", seq.ToString("D4"));
            return p;
        }

        private static string SafeShort(string s)
        {
            if (string.IsNullOrEmpty(s)) return "XX";
            return new string(s.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').Take(8).ToArray());
        }

        private static Dictionary<string, string> BuildTokenDict(Document doc, DrawingType dt, DrawingContext ctx, int seq)
        {
            // INT-06: route through the canonical builder so SheetManager,
            // ShopDrawingComposer and the production engine all feed the
            // exact same token set into TitleBlockParamApplier.
            //
            // doc was previously passed as null with a comment claiming the
            // producer had no handle — CreateOrFindSheet has had one all
            // along. With null, DrawingTokenContext.ReadProjectInfo returned
            // empty for {project} and {originator}, so producer-path
            // title-block cells came back blank while the fabrication and
            // SheetManager paths filled them from the same profile. seq was
            // likewise never supplied, leaving "{seq:Dn}" literal in cells.
            var d = DrawingTokenContext.Build(
                doc:        doc,
                dt:         dt,
                discCode:   dt?.Discipline,
                discipline: dt?.Discipline,
                levelCode:  ctx?.Level?.Name,
                seq:        seq,
                spool:      ctx?.Tag,
                mark:       ctx?.Tag);
            d["package"] = ctx?.PackageId ?? dt?.PackageId ?? string.Empty;
            return d;
        }
    }
}
