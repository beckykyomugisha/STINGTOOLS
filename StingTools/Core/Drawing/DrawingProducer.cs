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
        [ThreadStatic] private static string                        _cacheDocKey;

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

                var s = new Dictionary<string, ElementId>(StringComparer.Ordinal);
                var pkg = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var sheet in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
                {
                    var dtId = StingTools.Core.ParameterHelpers.GetString(sheet, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID) ?? string.Empty;
                    var pkgId = StingTools.Core.ParameterHelpers.GetString(sheet, DrawingTypeStamper.PARAM_DRAWING_PACKAGE_ID) ?? string.Empty;
                    if (!string.IsNullOrEmpty(dtId)) s[SheetKey(dtId, pkgId)] = sheet.Id;
                    if (pkg.TryGetValue(pkgId, out var n)) pkg[pkgId] = n + 1;
                    else pkg[pkgId] = 1;
                }
                _existingSheetCache = s;
                _packageSheetCount  = pkg;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"DrawingProducer.PrimeBatchCaches: {ex.Message}");
            }
        }

        public static void ResetBatchCaches()
        {
            _existingViewCache  = null;
            _existingSheetCache = null;
            _packageSheetCount  = null;
            _cacheDocKey        = null;
        }

        // GAP-L: a cache slot only matches the doc it was primed against.
        // Cross-doc consultation returns null so the slow path takes over.
        private static bool CacheMatchesDoc(Document doc)
            => _cacheDocKey != null && string.Equals(_cacheDocKey, CacheDocKey(doc), StringComparison.OrdinalIgnoreCase);

        private static string ViewKey(string dtId, string ctxTag, int ruleIdx)
            => (dtId ?? string.Empty) + "|" + (ctxTag ?? string.Empty) + "|" + ruleIdx;

        private static string SheetKey(string dtId, string pkgId)
            => (dtId ?? string.Empty) + "|" + (pkgId ?? string.Empty);

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
                    var vpId = PlaceViewOnSheet(doc, result.SheetId, viewId, dt, rule, result);
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
                                }
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
                        : new AnnotationRunOptions { SkipAutoTag = true, SkipAutoDim = true, SkipDecorative = true, SkipSpots = true }
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
                        BuiltInCategory bic = BuiltInCategory.INVALID;
                        foreach (BuiltInCategory b in Enum.GetValues(typeof(BuiltInCategory)))
                        {
                            try
                            {
                                var catEl = Category.GetCategory(doc, b);
                                if (catEl != null && string.Equals(catEl.Name, cat, StringComparison.OrdinalIgnoreCase))
                                {
                                    bic = b;
                                    break;
                                }
                            }
                            catch { }
                        }

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

        private static BoundingBoxXYZ BuildDefaultSectionBbox(DrawingContext ctx)
        {
            // 10m × 5m × 10m default box at level elevation (or origin).
            double elevFt = ctx?.Level?.Elevation ?? 0;
            var bb = new BoundingBoxXYZ
            {
                Transform = Transform.Identity,
                Min = new XYZ(-5.0 * 3.281, elevFt - 1.0 * 3.281, -5.0 * 3.281),
                Max = new XYZ( 5.0 * 3.281, elevFt + 4.0 * 3.281,  5.0 * 3.281)
            };
            return bb;
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
            try
            {
                // GAP-L: per-batch cache hit, fall back to fresh collector.
                if (_existingSheetCache != null
                    && CacheMatchesDoc(doc)
                    && _existingSheetCache.TryGetValue(SheetKey(dt.Id, effectivePackage), out var cachedSheetId))
                {
                    if (doc.GetElement(cachedSheetId) is ViewSheet vsCached && vsCached.IsValidObject)
                        return vsCached.Id;
                    _existingSheetCache.Remove(SheetKey(dt.Id, effectivePackage));
                }
                var existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .FirstOrDefault(s =>
                        string.Equals(StingTools.Core.ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID), dt.Id, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(StingTools.Core.ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_PACKAGE_ID) ?? "", effectivePackage, StringComparison.Ordinal));
                if (existing != null) return existing.Id;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            ElementId titleBlockId = ElementId.InvalidElementId;
            try
            {
                var (tbFamily, tbSymbol) = DrawingDispatcher.ResolveTitleBlockVariant(dt);
                var familyMatches = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .Cast<FamilySymbol>()
                    .Where(s => string.IsNullOrEmpty(tbFamily) ||
                                string.Equals(s.FamilyName, tbFamily, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                FamilySymbol picked = null;
                if (!string.IsNullOrWhiteSpace(tbSymbol))
                    picked = familyMatches.FirstOrDefault(s =>
                        string.Equals(s.Name, tbSymbol, StringComparison.OrdinalIgnoreCase));
                if (picked == null) picked = familyMatches.FirstOrDefault();
                titleBlockId = picked?.Id ?? ElementId.InvalidElementId;
                if (titleBlockId == ElementId.InvalidElementId)
                    titleBlockId = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .Cast<FamilySymbol>()
                        .FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            ViewSheet sheet;
            try { sheet = ViewSheet.Create(doc, titleBlockId); }
            catch (Exception ex) { result.Warnings.Add($"CreateSheet: {ex.Message}"); return ElementId.InvalidElementId; }

            try
            {
                sheet.SheetNumber = opts.OverrideSheetNumber ?? SubstituteTokens(dt.SheetNumberPattern, dt, ctx, doc);
            }
            catch (Exception ex) { result.Warnings.Add($"SheetNumber: {ex.Message}"); }
            try
            {
                sheet.Name = opts.OverrideSheetName ?? SubstituteTokens(dt.SheetNamePattern, dt, ctx, doc);
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

            try
            {
                // Phase 169 — persisted sequence counter via ExtensibleStorage on
                // ProjectInfo, granular by (DT, package, discipline, vol). Falls
                // back to the per-batch cache (and ultimately a sheet count) when
                // ES is unavailable. Survives Revit restarts and the renumber
                // command's compaction so deleted sheets don't regrow gaps.
                int seq;
                bool used = false;
                try
                {
                    seq = SheetSequenceStore.Next(doc, dt.Id, effectivePackage,
                        dt.Discipline ?? "", dt.IsoNaming?.Volume ?? "");
                    used = true;
                }
                catch (Exception ex2)
                {
                    seq = 0;
                    StingTools.Core.StingLog.Warn($"SheetSequenceStore.Next: {ex.Message}");
                }
                if (!used)
                {
                    // Legacy fallback path — preserves prior behaviour for
                    // documents where ExtensibleStorage isn't writable.
                    if (_packageSheetCount != null && CacheMatchesDoc(doc))
                    {
                        _packageSheetCount.TryGetValue(effectivePackage, out var n);
                        seq = n + 1;
                        _packageSheetCount[effectivePackage] = seq;
                    }
                    else
                    {
                        seq = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewSheet))
                            .Cast<ViewSheet>()
                            .Count(s => string.Equals(StingTools.Core.ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_PACKAGE_ID) ?? "", effectivePackage, StringComparison.Ordinal));
                    }
                }
                DrawingTypeStamper.StampSheetSequence(sheet, seq);
                // Newly-created sheet should be discoverable next time.
                if (_existingSheetCache != null)
                    _existingSheetCache[SheetKey(dt.Id, effectivePackage)] = sheet.Id;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            if (dt.TitleBlockParams != null && dt.TitleBlockParams.Count > 0)
            {
                try
                {
                    var tokens = BuildTokenDict(dt, ctx);
                    var tbResult = TitleBlockParamApplier.Apply(doc, sheet, dt, tokens);
                    foreach (var w in tbResult.Warnings)
                        result.Warnings.Add("TitleBlockParams: " + w);
                }
                catch (Exception ex2) { result.Warnings.Add($"TitleBlockParams: {ex2.Message}"); }
            }

            return sheet.Id;
        }

        private static ElementId PlaceViewOnSheet(Document doc, ElementId sheetId, ElementId viewId, DrawingType dt, ProductionRule rule, ProduceResult result)
        {
            try
            {
                var pt = SheetPlacementBridge.GetSlotPosition(doc, sheetId, dt,
                    rule.SlotIndex >= 0 ? rule.SlotIndex : 0, result);
                if (pt == null)
                {
                    var sheet = doc.GetElement(sheetId) as ViewSheet;
                    var bb = sheet?.Outline;
                    pt = bb != null ? new XYZ((bb.Min.U + bb.Max.U) / 2.0, (bb.Min.V + bb.Max.V) / 2.0, 0) : XYZ.Zero;
                }
                var vp = Viewport.Create(doc, sheetId, viewId, pt);
                return vp?.Id ?? ElementId.InvalidElementId;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"PlaceViewOnSheet: {ex.Message}");
                return ElementId.InvalidElementId;
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
            return $"{lvl}::{room}::{ctx?.Tag ?? ""}";
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
            return name;
        }

        private static bool NameExists(Document doc, string name)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Any(v => !v.IsTemplate && string.Equals(v.Name, name, StringComparison.Ordinal));
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }

        private static string SubstituteTokens(string pattern, DrawingType dt, DrawingContext ctx, Document doc)
        {
            if (string.IsNullOrEmpty(pattern)) return pattern;
            string disc  = dt?.Discipline ?? "";
            string lvl   = ctx?.Level?.Name ?? "";
            string sys   = "";
            string mark  = ctx?.Tag ?? "";
            string spool = ctx?.Tag ?? "";

            string Replace(string p)
            {
                p = p.Replace("{disc}", SafeShort(disc));
                p = p.Replace("{discipline}", disc);
                p = p.Replace("{lvl}", SafeShort(lvl));
                p = p.Replace("{sys}", SafeShort(sys));
                p = p.Replace("{mark}", SafeShort(mark));
                p = p.Replace("{spool}", SafeShort(spool));
                p = p.Replace("{purpose}", dt?.Purpose ?? "");
                // {seq:Dn}
                int seq = (ctx?.Tag != null && int.TryParse(ctx.Tag, out var s)) ? s : 1;
                for (int width = 1; width <= 6; width++)
                    p = p.Replace($"{{seq:D{width}}}", seq.ToString("D" + width));
                p = p.Replace("{seq}", seq.ToString("D4"));
                return p;
            }
            return Replace(pattern);
        }

        private static string SafeShort(string s)
        {
            if (string.IsNullOrEmpty(s)) return "XX";
            return new string(s.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').Take(8).ToArray());
        }

        private static Dictionary<string, string> BuildTokenDict(DrawingType dt, DrawingContext ctx)
        {
            // INT-06: route through the canonical builder so SheetManager,
            // ShopDrawingComposer and the production engine all feed the
            // exact same token set into TitleBlockParamApplier.
            var d = DrawingTokenContext.Build(
                doc:        null,        // producer is invoked without a doc handle here
                dt:         dt,
                discCode:   dt?.Discipline,
                discipline: dt?.Discipline,
                levelCode:  ctx?.Level?.Name,
                spool:      ctx?.Tag,
                mark:       ctx?.Tag);
            d["package"] = ctx?.PackageId ?? dt?.PackageId ?? string.Empty;
            return d;
        }
    }
}
