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
        /// <summary>
        /// Drawing-type ids an earlier run may have stamped on this request's sheet and
        /// views — the shipped id, when a project override now routes the key elsewhere.
        /// A sheet found under one of them is re-stamped and reused, never duplicated.
        /// </summary>
        public IReadOnlyCollection<string> FormerDrawingTypeIds { get; set; }

        // ── Elevations (DTW-54 / DTW-27) ─────────────────────────────────────
        /// <summary>Production rules to use instead of the drawing type's (a command that
        /// lays out its own faces, e.g. four exterior elevations on one 1+4 sheet).</summary>
        internal List<ProductionRule> RulesOverride { get; set; }
        /// <summary>Exterior elevations: per rule Idx, where the marker stands and which
        /// way the view must look. Each gets its own marker.</summary>
        internal Dictionary<int, ElevationStation> ElevationStations { get; set; }
        /// <summary>The level whose plan hosts elevation markers when the context itself
        /// carries no level (an exterior elevation is not "on" a level).</summary>
        internal Level OwnerLevel { get; set; }
        /// <summary>Room elevations: marker face (0-3) per rule Idx (ElevationFaces.Plan).</summary>
        internal Dictionary<int, int> ElevationFaceByRule { get; set; }
        /// <summary>The marker this context's faces share (made with the first face).</summary>
        internal ElementId SharedElevationMarkerId { get; set; }
    }

    /// <summary>Where an exterior elevation's marker stands (feet) and the way it must look.</summary>
    internal sealed class ElevationStation
    {
        public double X, Y, LookX, LookY;
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
        // DTW-45: a ledger, not a bare set — a number whose sheet an item's rollback
        // removed is released instead of reading as taken for the rest of the batch.
        [ThreadStatic] private static BatchNameLedger               _sheetNumberCache;
        // STACK-1: sheetId → the production context that claimed it during THIS
        // batch. STING_SHEET_CONTEXT_TXT is what normally tells two per-level
        // sheets apart; when it isn't bound, ReadSheetContext returns null for
        // every sheet and the unstampable fallback below handed the SAME sheet to
        // every level — so a 4-level run stacked all 4 plans on one sheet. The
        // parameter may be unbindable, but within one run we always know which
        // context we just used a sheet for, so claims are tracked here instead.
        [ThreadStatic] private static Dictionary<long, string>      _sheetCtxClaims;
        [ThreadStatic] private static string                        _cacheDocKey;
        // P-12: view names, collected once per batch. NameExists ran a full
        // OfClass(View) collector and MakeUniqueViewName calls it up to 100
        // times per view — O(views^2) on a first run over a large model.
        // DTW-45: likewise for view names.
        [ThreadStatic] private static BatchNameLedger                _existingViewNames;
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
            // P4: the annotation pass's loaded-symbol index + tag-type memo share
            // the batch's lifetime.
            AnnotationRunner.BeginSymbolBatch();
            try
            {
                // One View pass feeds both the stamped-view index and the name set
                // (they were two full collectors).
                var v = new Dictionary<string, ElementId>(StringComparer.Ordinal);
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(View)))
                {
                    if (!(el is View view) || view.IsTemplate) continue;
                    if (!string.IsNullOrEmpty(view.Name)) names.Add(view.Name);
                    var dtId = StingTools.Core.ParameterHelpers.GetString(view, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID);
                    if (string.IsNullOrEmpty(dtId)) continue;
                    var ctxTag = StingTools.Core.ParameterHelpers.GetString(view, ParamRegistry.STING_VIEW_CONTEXT_TAG) ?? string.Empty;
                    var ruleIdx = StingTools.Core.ParameterHelpers.GetInt(view, ParamRegistry.STING_PRODUCTION_RULE_IDX, -1);
                    // DTW-42: indexed by identity (the ids), which for a pre-id stamp is the stamp itself.
                    v[ViewKey(dtId, ProductionContextKey.Identity(ctxTag), ruleIdx)] = view.Id;
                }
                _existingViewCache = v;
                _existingViewNames = new BatchNameLedger(names, StringComparer.Ordinal);

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
                    if (!string.IsNullOrEmpty(dtId)) s[SheetKey(dtId, pkgId, ProductionContextKey.Identity(shtCtx))] = sheet.Id;
                    if (pkg.TryGetValue(pkgId, out var n)) pkg[pkgId] = n + 1;
                    else pkg[pkgId] = 1;
                    // Same pass feeds the sheet-number cache — no extra collector.
                    try { if (!string.IsNullOrEmpty(sheet.SheetNumber)) nums.Add(sheet.SheetNumber); }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                }
                _existingSheetCache = s;
                _packageSheetCount  = pkg;
                _sheetNumberCache   = new BatchNameLedger(nums, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"DrawingProducer.PrimeBatchCaches: {ex.Message}");
            }
        }

        // ── STACK-1 helpers — per-batch sheet↔context claims ────────────────

        /// <summary>True when this batch already used <paramref name="sheetId"/>
        /// for a context other than <paramref name="ctx"/>. Claims only exist for
        /// the current run, so this never blocks legitimate reuse across runs.</summary>
        private static bool ClaimedByOtherContext(ElementId sheetId, string ctx)
        {
            if (sheetId == null || _sheetCtxClaims == null) return false;
            return _sheetCtxClaims.TryGetValue(sheetId.Value, out var owner)
                && !string.Equals(owner, ctx ?? "", StringComparison.Ordinal);
        }

        private static void ClaimSheetForContext(ElementId sheetId, string ctx)
        {
            if (sheetId == null) return;
            if (_sheetCtxClaims == null) _sheetCtxClaims = new Dictionary<long, string>();
            _sheetCtxClaims[sheetId.Value] = ctx ?? "";
        }

        public static void ResetBatchCaches()
        {
            _sheetCtxClaims     = null;   // STACK-1
            _existingViewCache  = null;
            _existingViewNames  = null;
            _categoryByName     = null;
            _existingSheetCache = null;
            _packageSheetCount  = null;
            _sheetNumberCache   = null;
            _cacheDocKey        = null;
            _isoLevelMap        = null;   // DTW-43: levels may be renamed between batches
            _isoLevelMapDocKey  = null;
            // SLOT-5: the title-block slot map memo lives with the slot utils,
            // not here, but it has the same lifetime as a production batch —
            // drop it on the same boundary so an operator who nudged slot
            // reference planes in the Family Editor sees them on the next run.
            try { StingTools.Commands.Drawing.TitleBlockSlotUtils.ClearSlotMapCache(); }
            catch (Exception ex) { StingLog.Warn($"ClearSlotMapCache: {ex.Message}"); }
            AnnotationRunner.EndSymbolBatch();
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

            var rules = (ctx.RulesOverride != null && ctx.RulesOverride.Count > 0)
                ? ctx.RulesOverride.OrderBy(r => r.Idx).ToList()
                : (dt.ProductionRules != null && dt.ProductionRules.Count > 0)
                    ? dt.ProductionRules.OrderBy(r => r.Idx).ToList()
                    : new List<ProductionRule> { SynthesizeSingleRule(dt, result) };

            // DTW-54: a room's elevations are the faces the drawing type asks for —
            // the faces its rules name, else one per Elevation slot — not face 0 alone.
            if (ctx.Room != null && ctx.ElevationStations == null)
                rules = PlanRoomElevationFaces(dt, ctx, rules);

            // D-7 follow-up: a purpose with no producible view (Legend — the API
            // cannot create one — or an unknown purpose) synthesises no rule.
            // Minting the sheet anyway left an empty, correctly-numbered sheet in
            // the set that consumed a sequence number and read as "produced". The
            // warning from SynthesizeSingleRule already says why; stop here.
            if (rules.All(r => r == null))
                return result;

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
                    // DTW-21: a preset scale is the scale asked for — do not fit it away.
                    var vpId = PlaceViewOnSheet(doc, result.SheetId, viewId, dt, rule, result, famCtx,
                        pinScale: opts.Preset?.General?.ScaleOverride > 0);
                    if (vpId != ElementId.InvalidElementId)
                    {
                        result.ViewportIds.Add(vpId);
                        StampAutoPlaced(doc, vpId);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Put a view some other engine made (an SLD or riser drafting view, a panel
        /// schedule) on its drawing type's sheet, through the same path production uses:
        /// the sheet is found by stamp + context (<paramref name="ctx"/>) or created with
        /// the type's title block, number and name, and the view is stamped with the type
        /// and context and placed in slot 0 at its own scale (no fit-to-slot: these views
        /// are drawn at the scale their text is sized for). A re-run reuses the sheet;
        /// another view of the same type already on it — an earlier run's — comes off the
        /// sheet first (ExistingViewPlacement). The view's presentation is not touched.
        /// Caller owns the transaction.
        /// </summary>
        public static ProduceResult PlaceExistingView(Document doc, DrawingType dt, DrawingContext ctx, View view)
        {
            var result = new ProduceResult();
            if (doc == null || dt == null || view == null) return result;
            ctx = ctx ?? new DrawingContext();
            var opts = new ProduceOptions { CreateSheet = true, PlaceOnSheet = true, RunAnnotation = false, Idempotent = true };
            var rule = new ProductionRule
            {
                Idx = 0,
                ViewType = view is ViewSchedule ? "Schedule" : view is ViewDrafting ? "DraftingView" : view.ViewType.ToString(),
                SlotIndex = 0,
                Required = true,
                // Pins the scale: PlaceViewOnSheet fits a view to its slot only when no
                // override is set, and a fit would re-scale a 1:1 diagram whose text is
                // paper-sized. Schedules have no scale to fit; the value only gates the fit.
                ScaleOverride = view is ViewSchedule ? 1 : (SafeScale(view) ?? 1),
            };

            DrawingTypeStamper.Stamp(view, dt.Id);
            StampViewParameters(doc, view.Id, dt, rule, ctx);
            result.ViewIds.Add(view.Id);

            result.SheetId = CreateOrFindSheet(doc, dt, ctx, opts, result);
            if (result.SheetId == ElementId.InvalidElementId)
            {
                result.Warnings.Add($"No sheet could be made for '{dt.Id}'; '{view.Name}' is not on a sheet.");
                return result;
            }

            // Earlier runs' views of this type make way; anything else on the sheet stays.
            try
            {
                var onSheet = new List<(Element vp, Element v)>();
                foreach (var el in new FilteredElementCollector(doc, result.SheetId).WhereElementIsNotElementType())
                {
                    if (el is Viewport vpt) onSheet.Add((vpt, doc.GetElement(vpt.ViewId)));
                    else if (el is ScheduleSheetInstance ssi && !ssi.IsTitleblockRevisionSchedule)
                        onSheet.Add((ssi, doc.GetElement(ssi.ScheduleId)));
                }
                onSheet = onSheet.Where(x => x.v != null).ToList();
                var decision = ExistingViewPlacement.Decide(onSheet.Select(x => new PlacedView
                {
                    ViewId = x.v.Id.Value,
                    DrawingTypeId = StingTools.Core.ParameterHelpers.GetString(x.v, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID),
                }), view.Id.Value, dt.Id, ctx.FormerDrawingTypeIds);
                foreach (var x in onSheet.Where(x => decision.RemoveViewIds.Contains(x.v.Id.Value)))
                {
                    result.Warnings.Add($"'{x.v.Name}' (an earlier {dt.Id} view) was taken off the sheet for '{view.Name}'.");
                    doc.Delete(x.vp.Id);
                }
            }
            catch (Exception ex) { result.Warnings.Add($"Clearing the earlier {dt.Id} view off the sheet: {ex.Message}"); }

            var famCtx = SheetPlacementBridge.BuildFamilySlotContext(doc, doc.GetElement(result.SheetId) as ViewSheet, dt, result);
            var vpId = PlaceViewOnSheet(doc, result.SheetId, view.Id, dt, rule, result, famCtx);
            if (vpId != ElementId.InvalidElementId)
            {
                result.ViewportIds.Add(vpId);
                StampAutoPlaced(doc, vpId);
            }
            return result;
        }

        private static int? SafeScale(View v)
        {
            try { return v.Scale > 0 ? v.Scale : (int?)null; }
            catch (Exception ex) { StingLog.Warn($"Scale of '{v?.Name}': {ex.Message}"); return null; }
        }

        // D-7: the purpose -> view-kind decision lives in DrawingPurposeViewKind
        // (Revit-free, tested). The old switch defaulted every unlisted purpose
        // — Schematic, Clarification, Legend, Spool, Coordination — to
        // "FloorPlan", so a riser schematic was silently produced as a plan.
        // Unknown or unproducible purposes now yield no rule and a warning.
        private static ProductionRule SynthesizeSingleRule(DrawingType dt, ProduceResult result)
        {
            // A Schematic type with no productionRules used to produce an empty drafting
            // view on a numbered sheet, reported as produced. A schematic is drawn by its
            // generator (SLD_Generate, FireAlarm_Schematic …) or by a person; production
            // makes neither an empty view nor a sheet for it, and says which.
            if (string.Equals(dt.Purpose, DrawingPurpose.Schematic, StringComparison.OrdinalIgnoreCase))
            {
                string why = DrawingRouteRequests.SchematicNotProducedReason(dt.Id);
                StingLog.Warn($"DrawingProducer: {why}");
                result.Warnings.Add(why);
                return null;
            }
            var vt =DrawingPurposeViewKind.ResolveForProduction(dt.Id, dt.Purpose, out var problem);
            if (vt == null)
            {
                StingLog.Warn($"DrawingProducer: {problem}");
                result.Warnings.Add(problem);
                return null;
            }
            return new ProductionRule { Idx = 0, ViewType = vt, Required = true, SlotIndex = 0 };
        }

        private static ElementId ProduceSingleView(Document doc, DrawingType dt, ProductionRule rule, DrawingContext ctx, ProduceOptions opts, ProduceResult result)
        {
            try
            {
                // "Duplicate as Dependent": one parent plan per (type, level, rule), each
                // scope box's view a dependent of it. See DependentViewPlanner.
                bool dependent = DependentViewPlanner.UsesDependents(
                    opts.DuplicateOption == ViewDuplicateOption.AsDependent,
                    ctx?.ScopeBox != null, ctx?.Level != null, rule?.ViewType);

                if (opts.Idempotent)
                {
                    var existing = FindExistingView(doc, dt.Id, ctx, rule.Idx);
                    if (existing != null)
                    {
                        result.WasIdempotent = true;
                        if (dependent)
                        {
                            var parent = FindExistingView(doc, dt.Id, ParentContext(ctx), rule.Idx);
                            var action = DependentViewPlanner.ForBoxView(true, true,
                                PrimaryViewIdValue(existing), parent?.Id.Value ?? -1);
                            if (action == DependentViewAction.ReuseDependent)
                            {
                                // A dependent takes template, scale and annotation from its
                                // parent; only its crop is its own.
                                CropToContextBox(doc, existing, dt, ctx, result);
                                return existing.Id;
                            }
                            result.Warnings.Add(DependentViewPlanner.KeptIndependentWarning(
                                existing.Name, ctx.ScopeBox?.Name));
                        }
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
                                SkipSymbolDriftCheck = true, // idempotent refresh — batch path
                                // The box this view is produced for: without it the
                                // refresh re-ran the profile's own crop over the box crop.
                                ContextScopeBox = ctx?.ScopeBox
                            };
                            var refreshed = DrawingTypePresentation.Apply(doc, existing, dt, refreshOpts);
                            result.Warnings.AddRange(refreshed.Warnings);
                            ApplyPresetViewOverrides(existing, opts, result);   // DTW-21: a re-run honours them too
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Idempotent refresh ({existing.Id}): {ex.Message}");
                        }
                        return existing.Id;
                    }
                }

                if (dependent)
                {
                    var depId = ProduceDependentView(doc, dt, rule, ctx, opts, result);
                    if (depId != ElementId.InvalidElementId) return depId;
                    // ProduceDependentView said why; the box still gets its drawing,
                    // as an independent view, rather than nothing.
                }

                return CreatePresentedView(doc, dt, rule, ctx, opts, result, BuildViewName(dt, rule, ctx));
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"ProduceSingleView({rule?.ViewType}): {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        // ── "Duplicate as Dependent" ─────────────────────────────────────────

        /// <summary>The parent's context: same level and package, the parent tag, no box.</summary>
        private static DrawingContext ParentContext(DrawingContext ctx)
            => new DrawingContext { Level = ctx?.Level, PackageId = ctx?.PackageId, Tag = DependentViewPlanner.ParentTag };

        private static long PrimaryViewIdValue(View v)
        {
            try
            {
                var id = v?.GetPrimaryViewId();
                return id == null || id == ElementId.InvalidElementId ? -1 : id.Value;
            }
            catch (Exception ex) { StingLog.Warn($"GetPrimaryViewId({v?.Id}): {ex.Message}"); return -1; }
        }

        /// <summary>
        /// The parent plan for (drawing type, level, rule): found by its stamps, or made
        /// with the drawing type's full presentation — template, pack, annotation. It is
        /// a working view and is never placed on a sheet.
        /// </summary>
        private static View EnsureParentView(Document doc, DrawingType dt, ProductionRule rule,
            DrawingContext ctx, ProduceOptions opts, ProduceResult result)
        {
            var pctx = ParentContext(ctx);
            var found = FindExistingView(doc, dt.Id, pctx, rule.Idx);
            if (found != null) return found;

            var id = CreatePresentedView(doc, dt, rule, pctx, opts, result,
                DependentViewPlanner.ParentViewName(dt.Name ?? dt.Id, ctx.Level?.Name, rule.NameSuffix));
            if (id == ElementId.InvalidElementId) return null;
            StampViewParameters(doc, id, dt, rule, pctx);
            try
            {
                if (_existingViewCache != null && CacheMatchesDoc(doc))
                    _existingViewCache[ViewKey(dt.Id, ProductionContextKey.Identity(BuildContextTag(pctx)), rule.Idx)] = id;
            }
            catch (Exception ex) { StingLog.Warn($"Parent view cache: {ex.Message}"); }
            return doc.GetElement(id) as View;
        }

        /// <summary>
        /// The box's view as a dependent of the level's parent, cropped to the box.
        /// Returns InvalidElementId, with the reason in the warnings, when it cannot be
        /// made — the caller then produces an independent view instead.
        /// </summary>
        private static ElementId ProduceDependentView(Document doc, DrawingType dt, ProductionRule rule,
            DrawingContext ctx, ProduceOptions opts, ProduceResult result)
        {
            string box = ctx.ScopeBox?.Name ?? "";
            var parent = EnsureParentView(doc, dt, rule, ctx, opts, result);
            if (parent == null)
            {
                result.Warnings.Add($"No parent view could be made for '{dt.Id}' on {ctx.Level?.Name}; " +
                                    $"'{box}' was produced as an independent view instead.");
                return ElementId.InvalidElementId;
            }
            if (!parent.CanViewBeDuplicated(ViewDuplicateOption.AsDependent))
            {
                result.Warnings.Add($"Revit will not make a dependent of '{parent.Name}'; " +
                                    $"'{box}' was produced as an independent view instead.");
                return ElementId.InvalidElementId;
            }

            var depId = parent.Duplicate(ViewDuplicateOption.AsDependent);
            if (!(doc.GetElement(depId) is View dep))
            {
                result.Warnings.Add($"Duplicating '{parent.Name}' as a dependent returned no view; " +
                                    $"'{box}' was produced as an independent view instead.");
                return ElementId.InvalidElementId;
            }
            try { dep.Name = MakeUniqueViewName(doc, BuildViewName(dt, rule, ctx), dep.Id, result); }
            catch (Exception ex) { StingLog.Warn($"Dependent view name: {ex.Message}"); }
            // Duplicate copies the parent's stamps; the caller restamps context and rule,
            // and the drawing type is stamped here so the view is found by type even if
            // the copy did not carry it.
            DrawingTypeStamper.Stamp(dep, dt.Id);
            CropToContextBox(doc, dep, dt, ctx, result);
            return depId;
        }

        /// <summary>
        /// Crop a view to the context's scope box: the profile's crop strategy first
        /// (the context box wins there), then a direct bind if the strategy did not take.
        /// A profile with no crop block would otherwise leave a dependent uncropped —
        /// a whole-level copy of its parent.
        /// </summary>
        private static void CropToContextBox(Document doc, View view, DrawingType dt, DrawingContext ctx, ProduceResult result)
        {
            if (view == null || ctx?.ScopeBox == null) return;
            try { result.Warnings.AddRange(DrawingCropApplier.Apply(doc, view, dt, ctx.ScopeBox)); }
            catch (Exception ex) { result.Warnings.Add($"Crop '{view.Name}': {ex.Message}"); }
            try
            {
                var p = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                if (p != null && p.AsElementId() != ctx.ScopeBox.Id)
                {
                    if (!p.IsReadOnly) p.Set(ctx.ScopeBox.Id);
                    if (p.AsElementId() != ctx.ScopeBox.Id)
                        result.Warnings.Add($"'{view.Name}' could not be cropped to scope box '{ctx.ScopeBox.Name}'.");
                }
                if (!view.CropBoxActive) view.CropBoxActive = true;
            }
            catch (Exception ex) { result.Warnings.Add($"Crop '{view.Name}' to '{ctx.ScopeBox.Name}': {ex.Message}"); }
        }

        /// <summary>
        /// Create a view for the rule, name it, and apply the drawing type's presentation
        /// (template, pack, crop to <paramref name="ctx"/>'s scope box, annotation). The one
        /// creation path for both an ordinary view and a dependent's parent.
        /// </summary>
        private static ElementId CreatePresentedView(Document doc, DrawingType dt, ProductionRule rule,
            DrawingContext ctx, ProduceOptions opts, ProduceResult result, string viewName)
        {
            try
            {
                var vft = ResolveViewFamilyType(doc, rule, result, dt?.ViewFamilyTypeName);
                if (vft == null) return ElementId.InvalidElementId;

                var viewId = CreateViewByType(doc, rule, ctx, dt, vft, result);
                if (viewId == ElementId.InvalidElementId) return viewId;

                var view = doc.GetElement(viewId) as View;
                if (view == null) return ElementId.InvalidElementId;

                // DTW-54: a room's faces are told apart by the way each looks, read from
                // the view itself ("Kitchen - North"), unless the rule names its own suffix.
                if (view.ViewType == ViewType.Elevation && ctx?.Room != null && string.IsNullOrEmpty(rule.NameSuffix))
                {
                    try
                    {
                        var look = view.ViewDirection.Negate();
                        var compass = ElevationFaces.Compass(look.X, look.Y);
                        if (!string.IsNullOrEmpty(compass)) viewName = $"{viewName} - {compass}";
                    }
                    catch (Exception ex) { StingLog.Warn($"Elevation direction of {view.Id}: {ex.Message}"); }
                }

                try { view.Name = MakeUniqueViewName(doc, viewName, view.Id, result); }
                catch (Exception ex) { result.Warnings.Add($"Naming view '{viewName}': {ex.Message} — it keeps Revit's default name."); }
                if (rule.ScaleOverride.HasValue) try { view.Scale = rule.ScaleOverride.Value; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

                var applyOpts = new DrawingTypePresentation.ApplyOptions
                {
                    // DTW-28: the dialog's per-part annotation boxes (tags / dims /
                    // decorative / spots) reach the runner; only the master switch did.
                    AnnotationOptions = opts.RunAnnotation
                        ? new AnnotationRunOptions
                        {
                            ViewScale = view.Scale,
                            PackOverride = ComposeAnnotation(dt, rule, opts),
                            SkipAutoTag    = opts.Preset?.General?.RunAutoTag == false,
                            SkipAutoDim    = opts.Preset?.General?.RunAutoDim == false,
                            SkipDecorative = opts.Preset?.General?.RunDecorative == false,
                            SkipSpots      = opts.Preset?.General?.RunSpots == false,
                        }
                        : new AnnotationRunOptions { SkipAutoTag = true, SkipAutoDim = true, SkipDecorative = true, SkipSpots = true },
                    SkipSymbolDriftCheck = true, // batch producer — drift via standalone command
                    ContextScopeBox = ctx?.ScopeBox,
                    // DTW-97: a new view with no depth of its own takes the type's section-marker
                    // far clip. Not on refresh (a depth someone adjusted stays) and not when the
                    // caller built the section box (CustomBounds carries its own depth).
                    ApplyTypeFarClip = ctx?.CustomBounds == null
                };
                var presResult = DrawingTypePresentation.Apply(doc, view, dt, applyOpts);
                result.Warnings.AddRange(presResult.Warnings);

                ApplyPresetVg(doc, view, dt, opts, result);
                ApplyPresetViewOverrides(view, opts, result);

                return viewId;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"ProduceSingleView({rule?.ViewType}): {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        /// <summary>
        /// DTW-21: the Production Config dialog's "Scale override" and "Detail level
        /// override", applied AFTER the drawing type's presentation so they win over the
        /// profile's scale and detail level. They were saved in the preset and never read.
        /// A dependent's scale belongs to its parent and is left alone; a view whose
        /// template controls scale or detail level refuses the write, and that is reported.
        /// </summary>
        private static void ApplyPresetViewOverrides(View view, ProduceOptions opts, ProduceResult result)
        {
            var g = opts?.Preset?.General;
            if (view == null || g == null) return;
            if (g.ScaleOverride is int scale && scale > 0 && PrimaryViewIdValue(view) < 0)
            {
                try
                {
                    if (view.Scale != scale) view.Scale = scale;
                    if (view.Scale != scale)
                        result.Warnings.Add($"'{view.Name}': preset scale 1:{scale} did not take (its view template controls scale).");
                }
                catch (Exception ex) { result.Warnings.Add($"'{view.Name}': preset scale 1:{scale} not applied — {ex.Message}"); }
            }
            if (!string.IsNullOrWhiteSpace(g.DetailLevelOverride))
            {
                if (!Enum.TryParse<ViewDetailLevel>(g.DetailLevelOverride.Trim(), true, out var level) || level == ViewDetailLevel.Undefined)
                    result.Warnings.Add($"Preset detail level '{g.DetailLevelOverride}' is not Coarse, Medium or Fine — not applied.");
                else
                {
                    try
                    {
                        if (view.DetailLevel != level) view.DetailLevel = level;
                        if (view.DetailLevel != level)
                            result.Warnings.Add($"'{view.Name}': preset detail level {level} did not take (its view template controls it).");
                    }
                    catch (Exception ex) { result.Warnings.Add($"'{view.Name}': preset detail level {level} not applied — {ex.Message}"); }
                }
            }
        }

        /// <summary>
        /// DTW-20: the preset's VG overrides, layered like <see cref="ComposeAnnotation"/>:
        /// the "*" entry (what the Production Config dialog saves — it applies to every
        /// drawing type) first, then the drawing type's own entry, which wins where both
        /// set a category. Only the drawing-type entry used to be read, so every VG edit
        /// made in the dialog was saved and never applied.
        /// </summary>
        private static void ApplyPresetVg(Document doc, View view, DrawingType dt, ProduceOptions opts, ProduceResult result)
        {
            var vg = opts?.Preset?.VgOverrides;
            if (vg == null || view == null) return;
            foreach (var key in new[] { "*", dt?.Id })
            {
                if (string.IsNullOrEmpty(key) || !vg.TryGetValue(key, out var list) || list == null || list.Count == 0) continue;
                var packResult = new PackApplyResult();
                try { ViewStylePackApplier.ApplyPresetOverrides(doc, view, list, packResult); }
                catch (Exception ex) { result.Warnings.Add($"Preset VG overrides ('{key}') on '{view.Name}': {ex.Message}"); }
                result.Warnings.AddRange(packResult.Warnings);
            }
        }

        /// <summary>
        /// The annotation pack for this view: the drawing type's, overlaid by the
        /// production rule's annotationOverride, then the preset's "*" and
        /// drawing-type entries (what the Production Config dialog saves). Null
        /// when no override exists, so the runner uses the drawing type's pack
        /// untouched. See AnnotationPackLayering for why these layer rather than
        /// replace.
        /// </summary>
        private static AnnotationRulePack ComposeAnnotation(DrawingType dt, ProductionRule rule, ProduceOptions opts)
        {
            AnnotationRulePack presetAll = null, presetDt = null;
            var po = opts?.Preset?.AnnotationOverrides;
            if (po != null)
            {
                po.TryGetValue("*", out presetAll);
                if (!string.IsNullOrEmpty(dt?.Id)) po.TryGetValue(dt.Id, out presetDt);
            }
            if (rule?.AnnotationOverride == null && presetAll == null && presetDt == null) return null;
            return AnnotationPackLayering.Compose(dt?.Annotation, rule?.AnnotationOverride, presetAll, presetDt);
        }

        private static ViewFamilyType ResolveViewFamilyType(Document doc, ProductionRule rule, ProduceResult result,
            string wantedName = null)
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
            // The drawing type's named view type when it names one of this family,
            // else the first of the family (ViewFamilyTypeChoice) — which used to be
            // the only behaviour, so a section got whichever section type loaded first.
            var vft = ResolveNamedViewFamilyType(doc, targetFamily, wantedName, out var why);
            if (why != null)
                result.Warnings.Add(vft == null ? $"No ViewFamilyType found for '{rule.ViewType}'." : why);
            return vft;
        }

        /// <summary>
        /// The view type named <paramref name="wantedName"/> in <paramref name="family"/>,
        /// else the first of the family (see ViewFamilyTypeChoice). Shared by every
        /// production path so none of them falls back to "first found" on its own.
        /// </summary>
        internal static ViewFamilyType ResolveNamedViewFamilyType(Document doc, ViewFamily family,
            string wantedName, out string warning)
        {
            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .ToList();
            var keys = all.Select(t => (t.Name, t.ViewFamily.ToString())).ToList();
            int pick = ViewFamilyTypeChoice.Pick(keys, family.ToString(), wantedName, out warning);
            return pick < 0 ? null : all[pick];
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
                    {
                        // DTW-52: a section produced for a scope box is cut from the box.
                        // The fixed default below — 10 m wide at the project origin —
                        // is now only for a context that gives no place at all, and says so.
                        var sectionBox = ctx.CustomBounds ?? BuildSectionBoxFromScopeBox(doc, ctx, result);
                        if (sectionBox == null)
                        {
                            sectionBox = BuildDefaultSectionBbox(ctx);
                            result.Warnings.Add($"'{dt.Id}': the section has no grid, box or bounds to cut from — "
                                + "made as a 10 m section at the project origin; move it into place.");
                        }
                        return ViewSection.CreateSection(doc, vft.Id, sectionBox).Id;
                    }

                    case "Detail":
                        var detailBox = ctx.CustomBounds ?? BuildDefaultDetailBbox(ctx);
                        return ViewSection.CreateDetail(doc, vft.Id, detailBox).Id;

                    case "Elevation":
                        return CreateElevation(doc, rule, ctx, dt, vft, result);

                    case "ThreeD":
                    {
                        var v3 = View3D.CreateIsometric(doc, vft.Id);
                        // DTW-52: a 3D view produced for a scope box shows the box, turned
                        // with it — its section box is the box's own frame.
                        if (v3 != null && ctx.ScopeBox != null)
                        {
                            var sb = BuildSectionBoxFor3D(ctx.ScopeBox, result);
                            if (sb != null)
                            {
                                try { v3.SetSectionBox(sb); v3.IsSectionBoxActive = true; }
                                catch (Exception ex) { result.Warnings.Add($"3D section box from '{ctx.ScopeBox.Name}': {ex.Message}"); }
                            }
                        }
                        return v3?.Id ?? ElementId.InvalidElementId;
                    }

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

        /// <summary>
        /// DTW-52: the section a scope box gives — through its centre along its long
        /// side, as deep as the box's far face (SectionFromBox), over the context level's
        /// storey clipped to the box, or the box's full height with no level. Null when
        /// the context has no box or the box cannot be measured (reported).
        /// </summary>
        private static BoundingBoxXYZ BuildSectionBoxFromScopeBox(Document doc, DrawingContext ctx, ProduceResult result)
        {
            if (ctx?.ScopeBox == null) return null;
            return SectionBoxFromScopeBox(doc, ctx.ScopeBox, ctx.Level, cross: false, result?.Warnings);
        }

        /// <summary>
        /// DTW-74: the second of a scope box's two building sections — through its centre,
        /// perpendicular to the long-axis cut the producer takes from the box
        /// (SectionFromBox.CrossFrame), over the box's full height. For a caller that
        /// passes it as <see cref="DrawingContext.CustomBounds"/>. Null (reported) when the
        /// box cannot be measured.
        /// </summary>
        internal static BoundingBoxXYZ BuildCrossSectionBoxFromScopeBox(Document doc, Element box, List<string> warnings)
            => SectionBoxFromScopeBox(doc, box, null, cross: true, warnings);

        /// <summary>DTW-74: the long-axis cut a scope box gives (the one a Section rule
        /// produced for the box takes), over the box's full height, as CustomBounds.</summary>
        internal static BoundingBoxXYZ BuildLongSectionBoxFromScopeBox(Document doc, Element box, List<string> warnings)
            => SectionBoxFromScopeBox(doc, box, null, cross: false, warnings);

        private static BoundingBoxXYZ SectionBoxFromScopeBox(Document doc, Element box, Level level, bool cross, List<string> warnings)
        {
            if (box == null) return null;
            if (!ScopeBoxRevit.TryMeasure(box, out var m, out var why))
            {
                warnings?.Add($"Section from scope box '{box.Name}': the box {why}.");
                return null;
            }
            double wFt = m.WidthM * ScopeBoxRevit.FeetPerMetre, dFt = m.DepthM * ScopeBoxRevit.FeetPerMetre;
            var frame = cross
                ? SectionFromBox.CrossFrame(m.Centre.X, m.Centre.Y, wFt, dFt, m.AngleRad)
                : SectionFromBox.Frame(m.Centre.X, m.Centre.Y, wFt, dFt, m.AngleRad);
            if (frame == null) return null;
            double? lvl = null, next = null;
            if (level != null)
            {
                lvl = level.Elevation;
                try
                {
                    next = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                        .Where(l => l.Elevation > level.Elevation + 1e-6)
                        .OrderBy(l => l.Elevation).Select(l => (double?)l.Elevation).FirstOrDefault();
                }
                catch (Exception ex) { StingLog.Warn($"Section band next level: {ex.Message}"); }
            }
            const double mToFt = 1.0 / 0.3048;
            var (bottom, top) = SectionFromBox.Band(m.ZMinFt, m.ZMaxFt, lvl, next, 1.0 * mToFt, 4.0 * mToFt);
            return BuildSectionBox(
                origin:       new XYZ(frame.OriginX, frame.OriginY, bottom),
                cutDirection: new XYZ(frame.DirX, frame.DirY, 0),
                halfWidthFt:  frame.HalfWidth,
                bottomZ:      bottom,
                topZ:         top,
                depthFt:      frame.Depth);
        }

        /// <summary>DTW-52: a 3D section box that is the scope box, in the box's own frame.</summary>
        private static BoundingBoxXYZ BuildSectionBoxFor3D(Element box, ProduceResult result)
        {
            if (!ScopeBoxRevit.TryMeasure(box, out var m, out var why))
            {
                result.Warnings.Add($"3D view for scope box '{box?.Name}': the box {why}.");
                return null;
            }
            double w = m.WidthM * ScopeBoxRevit.FeetPerMetre, d = m.DepthM * ScopeBoxRevit.FeetPerMetre;
            var t = Transform.Identity;
            t.Origin = m.Centre;
            t.BasisX = new XYZ(Math.Cos(m.AngleRad), Math.Sin(m.AngleRad), 0);
            t.BasisY = new XYZ(-Math.Sin(m.AngleRad), Math.Cos(m.AngleRad), 0);
            t.BasisZ = XYZ.BasisZ;
            double zLo = Math.Min(m.ZMinFt, m.ZMaxFt) - m.Centre.Z, zHi = Math.Max(m.ZMinFt, m.ZMaxFt) - m.Centre.Z;
            if (zHi - zLo < 1e-6) zHi = zLo + 1.0;
            return new BoundingBoxXYZ { Transform = t, Min = new XYZ(-w / 2, -d / 2, zLo), Max = new XYZ(w / 2, d / 2, zHi) };
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

        // ── Elevations ─────────────────────────────────────────────────────────

        /// <summary>
        /// DTW-54: the rules for a room's elevations. With the type's own Elevation rules,
        /// each keeps its Idx and gets the face it names (or its order); with none, one rule
        /// per face ElevationFaces.Plan gives, each into its own slot. The faces share one
        /// marker at the room.
        /// </summary>
        private static List<ProductionRule> PlanRoomElevationFaces(DrawingType dt, DrawingContext ctx, List<ProductionRule> rules)
        {
            bool allElevation = rules.Count > 0 && rules.All(r => r != null
                && string.Equals((r.ViewType ?? "").Trim(), "Elevation", StringComparison.OrdinalIgnoreCase));
            bool hasOwnRules = dt.ProductionRules != null && dt.ProductionRules.Count > 0;
            if (!hasOwnRules && !allElevation) return rules;
            var plan = ElevationFaces.Plan(hasOwnRules ? dt.ProductionRules : null,
                (dt.Slots ?? new List<DrawingSlot>()).Select(s => s?.ViewType).ToList());
            ctx.ElevationFaceByRule = plan.ToDictionary(p => p.RuleIdx, p => p.Face);
            if (hasOwnRules) return rules;
            return plan.Select(p => new ProductionRule
            {
                Idx = p.RuleIdx, ViewType = "Elevation", SlotIndex = p.SlotIndex,
                Required = p.RuleIdx == 0, ElevationFace = p.Face,
            }).ToList();
        }

        /// <summary>
        /// One elevation for <paramref name="rule"/>. An exterior station gets its own marker
        /// and the face that actually looks at the building (read back from the view, not
        /// assumed — the API does not promise which index faces which way); a room or level
        /// context shares one marker across its faces. The marker is hosted on a plan of the
        /// context's own level — the room's level, or the owner level — not the first plan
        /// in the model.
        /// </summary>
        private static ElementId CreateElevation(Document doc, ProductionRule rule, DrawingContext ctx, DrawingType dt,
            ViewFamilyType vft, ProduceResult result)
        {
            var hostLevel = ctx.Level ?? ctx.OwnerLevel ?? RoomLevel(doc, ctx.Room);
            if (hostLevel == null && ctx.Room == null)
            {
                result.Warnings.Add("Elevation requires a level or a room — none in context.");
                return ElementId.InvalidElementId;
            }
            var ownerPlan = ResolveOwnerPlan(doc, hostLevel, result);
            if (ownerPlan == null) return ElementId.InvalidElementId;
            int scale = dt.Scale > 0 ? dt.Scale : 100;

            if (ctx.ElevationStations != null && ctx.ElevationStations.TryGetValue(rule.Idx, out var st))
            {
                var at = new XYZ(st.X, st.Y, hostLevel?.Elevation ?? 0);
                var marker = ElevationMarker.CreateElevationMarker(doc, vft.Id, at, scale);
                for (int i = 0; i < ElevationFaces.MaxFaces; i++)
                {
                    if (!marker.IsAvailableIndex(i)) continue;
                    var v = marker.CreateElevation(doc, ownerPlan.Id, i);
                    if (v == null) continue;
                    var look = v.ViewDirection.Negate();   // ViewDirection points at the viewer
                    if (ElevationFaces.LooksToward(look.X, look.Y, st.LookX, st.LookY)) return v.Id;
                    doc.Delete(v.Id);
                }
                doc.Delete(marker.Id);
                result.Warnings.Add($"'{ctx.Tag}': no face of the elevation marker looks at the building — nothing made.");
                return ElementId.InvalidElementId;
            }

            int face = 0;
            if (rule.ElevationFace.HasValue) face = rule.ElevationFace.Value;
            else if (ctx.ElevationFaceByRule != null && ctx.ElevationFaceByRule.TryGetValue(rule.Idx, out var f)) face = f;
            if (face < 0 || face >= ElevationFaces.MaxFaces)
            {
                result.Warnings.Add($"Rule {rule.Idx} of '{dt.Id}' asks for elevation face {face}; a marker has faces 0-3.");
                return ElementId.InvalidElementId;
            }
            ElevationMarker shared = null;
            if (ctx.SharedElevationMarkerId != null)
                shared = doc.GetElement(ctx.SharedElevationMarkerId) as ElevationMarker;   // null after a rollback
            if (shared == null || !shared.IsAvailableIndex(face))
            {
                shared = ElevationMarker.CreateElevationMarker(doc, vft.Id, ResolveElevationOrigin(ctx), scale);
                ctx.SharedElevationMarkerId = shared.Id;
            }
            return shared.CreateElevation(doc, ownerPlan.Id, face)?.Id ?? ElementId.InvalidElementId;
        }

        private static Level RoomLevel(Document doc, Element room)
        {
            try
            {
                if (room is SpatialElement se && se.Level != null) return se.Level;
                if (room?.LevelId != null && room.LevelId != ElementId.InvalidElementId) return doc.GetElement(room.LevelId) as Level;
            }
            catch (Exception ex) { StingLog.Warn($"RoomLevel({room?.Id}): {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// The floor plan an elevation marker is placed in: an independent floor plan of
        /// <paramref name="level"/>. Any floor plan only when that level has none — said so,
        /// because a marker hosted on another storey's plan is not visible where the room is.
        /// </summary>
        private static ViewPlan ResolveOwnerPlan(Document doc, Level level, ProduceResult result)
        {
            var plans = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan).ToList();
            if (level != null)
            {
                var onLevel = plans.Where(v => v.GenLevel != null && v.GenLevel.Id == level.Id).ToList();
                var own = onLevel.FirstOrDefault(v => PrimaryViewIdValue(v) < 0) ?? onLevel.FirstOrDefault();
                if (own != null) return own;
            }
            var any = plans.FirstOrDefault(v => PrimaryViewIdValue(v) < 0) ?? plans.FirstOrDefault();
            if (any == null)
            {
                result.Warnings.Add("An elevation needs a floor plan to host its marker — the model has none. Produce the plans first.");
                return null;
            }
            result.Warnings.Add(level != null
                ? $"{level.Name} has no floor plan; the elevation marker was placed in '{any.Name}'. Produce that level's plan and re-run to host it there."
                : $"No level in context; the elevation marker was placed in '{any.Name}'.");
            return any;
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
            string legacyCtx = BuildLegacyContextTag(ctx);

            var existing = FindExistingSheet(doc, dt.Id, effectivePackage, sheetCtx, legacyCtx, result);
            if (existing != null) return existing;

            // A sheet stamped with an id this request used to route to (the shipped id,
            // before a project re-routed the key) is the same sheet: adopt and re-stamp it
            // rather than mint a duplicate beside it.
            foreach (var former in (ctx.FormerDrawingTypeIds ?? Array.Empty<string>())
                         .Where(f => !string.IsNullOrWhiteSpace(f) && !string.Equals(f, dt.Id, StringComparison.OrdinalIgnoreCase)))
            {
                existing = FindExistingSheet(doc, former, effectivePackage, sheetCtx, legacyCtx, result);
                if (existing == null) continue;
                try
                {
                    if (doc.GetElement(existing) is ViewSheet adopted && DrawingTypeStamper.Stamp(adopted, dt.Id))
                    {
                        if (_existingSheetCache != null) _existingSheetCache[SheetKey(dt.Id, effectivePackage, ProductionContextKey.Identity(sheetCtx))] = existing;
                        result.Warnings.Add($"Sheet {adopted.SheetNumber} was stamped '{former}'; routing now gives '{dt.Id}', so it was re-stamped and reused.");
                    }
                    else
                        result.Warnings.Add($"Sheet {existing} (stamped '{former}') is reused but could not be re-stamped '{dt.Id}'.");
                }
                catch (Exception ex) { result.Warnings.Add($"Re-stamping sheet {existing} '{dt.Id}': {ex.Message}"); }
                return existing;
            }

            return CreateSheet(doc, dt, ctx, opts, result, effectivePackage, sheetCtx);
        }

        /// <summary>
        /// The existing sheet stamped <paramref name="typeId"/> for this package and
        /// production context, or null. Sets <see cref="ProduceResult.SheetReused"/> on a hit.
        /// </summary>
        private static ElementId FindExistingSheet(Document doc, string typeId, string effectivePackage, string sheetCtx,
            string legacyCtx, ProduceResult result)
        {
            try
            {
                // GAP-L: per-batch cache hit — by identity (DTW-42), then by the pre-id
                // stamp — falling back to a fresh collector.
                if (_existingSheetCache != null && CacheMatchesDoc(doc))
                {
                    foreach (var key in new[] { ProductionContextKey.Identity(sheetCtx), legacyCtx })
                    {
                        if (key == null || !_existingSheetCache.TryGetValue(SheetKey(typeId, effectivePackage, key), out var cachedSheetId)) continue;
                        if (doc.GetElement(cachedSheetId) is ViewSheet vsCached && vsCached.IsValidObject)
                        {
                            result.SheetReused = true;   // P-9: reuse is not production
                            RestampSheetContext(vsCached, sheetCtx, typeId, effectivePackage, result);
                            return vsCached.Id;
                        }
                        _existingSheetCache.Remove(SheetKey(typeId, effectivePackage, key));
                    }
                }

                var candidates = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s =>
                        string.Equals(StingTools.Core.ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID), typeId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(StingTools.Core.ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_PACKAGE_ID) ?? "", effectivePackage, StringComparison.Ordinal))
                    .ToList();

                // Same drawing type, same package, same production context — by id, then
                // by the stamp an earlier build wrote (names only). Either is re-stamped
                // in the current form so the next run matches it by id.
                var exact = candidates.FirstOrDefault(s =>
                                ProductionContextKey.Matches(DrawingTypeStamper.ReadSheetContext(s), sheetCtx, null))
                         ?? (legacyCtx == null ? null : candidates.FirstOrDefault(s =>
                                string.Equals(DrawingTypeStamper.ReadSheetContext(s), legacyCtx, StringComparison.Ordinal)));
                if (exact != null)
                {
                    result.SheetReused = true;
                    RestampSheetContext(exact, sheetCtx, typeId, effectivePackage, result);
                    return exact.Id;
                }

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
                // STACK-1: never hand back a sheet this batch already claimed for a
                // DIFFERENT context — that is what stacked every level onto one
                // sheet. Falling through mints a fresh sheet for this context and
                // records the claim below.
                var unstampable = candidates.FirstOrDefault(s =>
                    DrawingTypeStamper.ReadSheetContext(s) == null
                    && !ClaimedByOtherContext(s.Id, sheetCtx));
                if (unstampable != null)
                {
                    result.Warnings.Add(
                        $"{DrawingTypeStamper.PARAM_SHEET_CONTEXT} is not bound in this project, so sheets cannot be " +
                        $"matched per level / scope box. Reusing sheet {unstampable.Id} for context '{sheetCtx}'. " +
                        "Run LoadSharedParams to bind it, then re-run production.");
                    ClaimSheetForContext(unstampable.Id, sheetCtx);
                    result.SheetReused = true;
                    return unstampable.Id;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// DTW-42: a sheet found by id or by its pre-id stamp is re-stamped with this run's
        /// context, so its stamp names the level / box as they are called now and carries
        /// the ids the next run matches on.
        /// </summary>
        private static void RestampSheetContext(ViewSheet sheet, string sheetCtx, string typeId, string effectivePackage, ProduceResult result)
        {
            if (sheet == null || string.IsNullOrEmpty(sheetCtx)) return;
            try
            {
                var stamped = DrawingTypeStamper.ReadSheetContext(sheet);
                if (stamped == null || string.Equals(stamped, sheetCtx, StringComparison.Ordinal)) return;
                if (DrawingTypeStamper.StampSheetContext(sheet, sheetCtx))
                {
                    if (_existingSheetCache != null)
                        _existingSheetCache[SheetKey(typeId, effectivePackage, ProductionContextKey.Identity(sheetCtx))] = sheet.Id;
                }
                else
                    result?.Warnings.Add($"Sheet {sheet.SheetNumber}: its production context could not be re-stamped to '{sheetCtx}'.");
            }
            catch (Exception ex) { result?.Warnings.Add($"Re-stamping the context of sheet {sheet.Id}: {ex.Message}"); }
        }

        private static ElementId CreateSheet(Document doc, DrawingType dt, DrawingContext ctx, ProduceOptions opts,
            ProduceResult result, string effectivePackage, string sheetCtx)
        {
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
            // PRJ_SHEET_SEQUENCE_INT, so {seq} fell back to parsing
            // ctx.Tag — a level name in every batch command — and every sheet
            // in a package numbered 0001.
            //
            // The project's sheet-number policy decides whether this profile
            // numbers by its own pattern or by the ISO 19650-2 field order
            // derived from its isoNaming block. Default is the profile's own
            // pattern, so this changes nothing until a project opts in by
            // setting PRJ_ORG_SHEET_NUMBER_POLICY_TXT = "iso". See
            // Core/Drawing/SheetNumberPolicy.cs for why the ISO number is
            // derived rather than authored per type. The pattern is resolved
            // FIRST because under ISO it also decides which counter to draw from.
            string numberPattern = dt.SheetNumberPattern;
            var policy = SheetNumberPolicyKind.Profile;
            try
            {
                policy = SheetNumberPolicy.Parse(ReadSheetNumberPolicy(doc, result));
                numberPattern = SheetNumberPolicy.ResolvePattern(dt, policy, out var policyNote);
                if (!string.IsNullOrEmpty(policyNote)) result.Warnings.Add(policyNote);
            }
            catch (Exception ex) { result.Warnings.Add($"Sheet-number policy: {ex.Message}"); }

            int seq = ResolveSheetSequence(doc, dt, ctx, effectivePackage, policy, numberPattern, result);

            // One token dict for the number, the name and the title-block
            // cells, built with the REAL doc handle so {project} /
            // {originator} resolve from ProjectInformation instead of coming
            // back blank.
            var tokens = BuildTokenDict(doc, dt, ctx, seq);
            // DTW-43: an ISO-shaped number carries the ISO level code, so the {lvl} the
            // title block and the K-12 segment stamps show must be the same code, not the
            // level's name.
            if (SheetNumberPolicy.IsAlreadyIso(numberPattern)
                && tokens.TryGetValue(IsoLevelKey, out var isoLvl) && !string.IsNullOrEmpty(isoLvl))
                tokens["lvl"] = isoLvl;

            // K-7: an empty {lvl} (or any other unresolved token) used to reach
            // the sheet number as a dropped segment with no warning. Audit
            // before substituting so the operator sees which token was blank
            // and where to set it, rather than discovering "KBL26-PLN-COT01--DR"
            // on an issued drawing.

            if (opts.OverrideSheetNumber == null)
                result.Warnings.AddRange(
                    DrawingTokenContext.AuditPattern(numberPattern, tokens, "Sheet number"));
            if (opts.OverrideSheetName == null)
                result.Warnings.AddRange(
                    DrawingTokenContext.AuditPattern(dt.SheetNamePattern, tokens, "Sheet name"));

            try
            {
                var number = opts.OverrideSheetNumber ?? SubstituteTokens(numberPattern, dt, ctx, seq, tokens);
                // A known-but-empty token substitutes to "" and leaves both of its
                // separators — "A-{lvl}-{seq:D3}" with no level produces "A--001".
                // Collapse before the uniqueness check, so two sheets differing only
                // by an empty segment are seen as the clash they are.
                number = SheetNumberTidy.Collapse(number);
                // Revit rejects a duplicate sheet number outright, and the
                // catch below would leave the sheet on its default number.
                sheet.SheetNumber = EnsureUniqueSheetNumber(doc, number, sheet.Id, result);
            }
            catch (Exception ex) { result.Warnings.Add($"SheetNumber: {ex.Message}"); }
            try
            {
                var sheetName = opts.OverrideSheetName ?? SubstituteTokens(dt.SheetNamePattern, dt, ctx, seq, tokens);
                // DTW-51: every area box on a level produced a sheet with the same name
                // ("Power Layout - Level 1") unless the pattern names {mark}. Say which
                // area the sheet is when the pattern does not.
                if (opts.OverrideSheetName == null && ctx.ScopeBox != null)
                {
                    var p = dt.SheetNamePattern ?? "";
                    bool namesArea = p.IndexOf("{mark}", StringComparison.OrdinalIgnoreCase) >= 0
                                  || p.IndexOf("{spool}", StringComparison.OrdinalIgnoreCase) >= 0;
                    string area = !string.IsNullOrWhiteSpace(ctx.Tag) ? ctx.Tag : ctx.ScopeBox.Name;
                    if (!namesArea && !string.IsNullOrWhiteSpace(area)
                        && (sheetName ?? "").IndexOf(area, StringComparison.OrdinalIgnoreCase) < 0)
                        sheetName = $"{sheetName} - {area}";
                }
                sheet.Name = sheetName;
            }
            catch (Exception ex) { result.Warnings.Add($"SheetName: {ex.Message}"); }

            // K-12: stamp the seven ISO segments and their join onto the sheet.
            // Nothing new is derived — DrawingTokenContext already resolved every one
            // of these to build the number, and they were discarded after
            // substitution. All twelve PRJ_SHEET_* are bound; eleven had zero writers.
            StampSheetSegments(sheet, tokens, result);

            // DTW-53: there was a style-lock check here, on a sheet created a few lines
            // above — a new sheet carries no lock, so it could never fire. Removed.
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
                    _existingSheetCache[SheetKey(dt.Id, effectivePackage, ProductionContextKey.Identity(sheetCtx))] = sheet.Id;
                ClaimSheetForContext(sheet.Id, sheetCtx);   // STACK-1
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

        private static ElementId PlaceViewOnSheet(Document doc, ElementId sheetId, ElementId viewId, DrawingType dt, ProductionRule rule, ProduceResult result, SheetPlacementBridge.FamilySlotContext famCtx = null,
            bool pinScale = false)
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
                // A dependent's scale belongs to its parent: fitting each dependent
                // would rescale the parent, and so every sibling, once per box.
                if (sp != null && !rule.ScaleOverride.HasValue && !pinScale
                    && doc.GetElement(viewId) is View vFit
                    && PrimaryViewIdValue(vFit) < 0)
                    SheetPlacementBridge.ApplyFitScale(doc, vFit, sp);

                // SLOT-3: warn on a view/slot type mismatch rather than
                // placing it silently into the wrong slot. DTW-63: slot terms
                // are STING vocabulary ("Plan", "3D", "RCP"), not Revit enum
                // names ("FloorPlan", "ThreeD", "CeilingPlan"); compare through
                // the predicate the placement bridge uses.
                if (sp?.Slot != null && !string.IsNullOrWhiteSpace(sp.Slot.ViewType)
                    && doc.GetElement(viewId) is View vChk
                    && !SlotViewTypeCompatibility.IsCompatible(vChk.ViewType.ToString(), sp.Slot.ViewType))
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
                            SheetPlacementBridge.MarkAutoPlaced(ssi); // DTW-98: ES, not an unbindable parameter
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

                SheetPlacementBridge.MarkAutoPlaced(vp); // DTW-98: ES, not an unbindable parameter

                // SLOT-1: the slot's viewport type wins; otherwise the drawing
                // type's own viewportTypeName. All 93 corporate types declare one
                // ("STING - Standard Viewport") and nothing on this path read it,
                // so every produced viewport kept Revit's default type while the
                // catalogue said otherwise.
                var vpTypeName = !string.IsNullOrWhiteSpace(sp?.Slot?.ViewportType)
                    ? sp.Slot.ViewportType
                    : dt?.ViewportTypeName;
                if (!string.IsNullOrWhiteSpace(vpTypeName))
                {
                    var vpTypeId = SheetPlacementBridge.ResolveViewportTypeId(doc, vpTypeName);
                    if (vpTypeId != null && vpTypeId != ElementId.InvalidElementId)
                    {
                        if (vp.GetTypeId() != vpTypeId)
                        {
                            try { vp.ChangeTypeId(vpTypeId); }
                            catch (Exception ex) { result.Warnings.Add($"Viewport type '{vpTypeName}': {ex.Message}"); }
                        }
                    }
                    else
                    {
                        result.Warnings.Add(
                            $"Viewport type '{vpTypeName}' not found — viewport for slot '{sp?.Slot?.Label}' uses the default.");
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
            SheetPlacementBridge.MarkAutoPlaced(el); // DTW-98
        }

        /// <summary>
        /// The context stamp: level / room / tag / scope-box names for display and for the
        /// readers that parse them (Renumber, crop recovery), plus — DTW-42 — the level id,
        /// room id and box UniqueId that identify it. See ProductionContextKey.
        /// </summary>
        private static string BuildContextTag(DrawingContext ctx)
        {
            ReadContextParts(ctx, out var lvl, out var levelId, out var room, out var sbox, out var sboxUid);
            // P-6: the scope box is part of the context's identity, appended last so a
            // per-level stamp keeps its shape. One format, parsed back by
            // ViewContextTag.ScopeBoxName when a re-sync has to recover the box.
            return ProductionContextKey.Compose(lvl, levelId, room, ctx?.Tag, sbox, sboxUid);
        }

        /// <summary>
        /// DTW-27: adopt a view made outside the producer for (drawing type, context, rule)
        /// — e.g. an exterior elevation the Setup Wizard or an earlier build stamped with
        /// the raw tag "exterior::face::North" — by stamping it the way the producer would,
        /// so ProduceAllViews reuses it instead of making a second one. Does nothing, and
        /// returns false, when the producer already has a view for that context and rule.
        /// Caller owns the transaction.
        /// </summary>
        internal static bool AdoptView(Document doc, DrawingType dt, DrawingContext ctx, ProductionRule rule, View view)
        {
            if (doc == null || dt == null || ctx == null || rule == null || view == null) return false;
            if (FindExistingView(doc, dt.Id, ctx, rule.Idx) != null) return false;
            // Adopted only if the type stamp landed: without it the producer cannot find the
            // view again and would make a second one on the next pass.
            bool stamped = DrawingTypeStamper.Stamp(view, dt.Id);
            if (!stamped)
            {
                StingLog.Warn($"AdoptView: could not stamp '{view.Name}' with {dt.Id} — not adopted.");
                return false;
            }
            StampViewParameters(doc, view.Id, dt, rule, ctx);
            try
            {
                if (_existingViewCache != null && CacheMatchesDoc(doc))
                    _existingViewCache[ViewKey(dt.Id, ProductionContextKey.Identity(BuildContextTag(ctx)), rule.Idx)] = view.Id;
            }
            catch (Exception ex)
            {
                // The batch index no longer knows this view; drop it so the next lookup
                // reads the model (where the stamp now is) instead of missing it.
                StingLog.Warn($"AdoptView cache: {ex.Message} — batch view index dropped.");
                _existingViewCache = null;
            }
            return stamped;
        }

        /// <summary>
        /// The stamp the producer wrote before DTW-42 (names only). An element stamped that
        /// way is still found — and re-stamped in the new form — so a model produced by an
        /// earlier build does not get a second set of views and sheets.
        /// </summary>
        private static string BuildLegacyContextTag(DrawingContext ctx)
        {
            ReadContextParts(ctx, out var lvl, out _, out var room, out var sbox, out _);
            return ProductionContextKey.Legacy(lvl, room, ctx?.Tag, sbox);
        }

        private static void ReadContextParts(DrawingContext ctx, out string levelName, out long? levelId,
            out string roomId, out string boxName, out string boxUid)
        {
            levelName = ""; levelId = null; roomId = ""; boxName = ""; boxUid = null;
            try
            {
                levelName = ctx?.Level?.Name ?? "";
                if (ctx?.Level != null) levelId = ctx.Level.Id.Value;
            }
            catch (Exception ex) { StingLog.Warn($"BuildContextTag level: {ex.Message}"); }
            try { roomId = ctx?.Room?.Id?.ToString() ?? ""; } catch (Exception ex) { StingLog.Warn($"BuildContextTag room: {ex.Message}"); }
            try
            {
                boxName = ctx?.ScopeBox?.Name ?? "";
                boxUid = ctx?.ScopeBox?.UniqueId;
            }
            catch (Exception ex) { StingLog.Warn($"BuildContextTag scope box: {ex.Message}"); }
        }

        private static View FindExistingView(Document doc, string dtId, DrawingContext ctx, int ruleIdx)
        {
            try
            {
                var ctxTag = BuildContextTag(ctx);
                var legacyTag = BuildLegacyContextTag(ctx);
                // GAP-L: O(1) hit when the per-batch index is primed for this document —
                // by identity first (DTW-42), then by the pre-id stamp. Cross-doc
                // consultation falls through to the collector.
                if (_existingViewCache != null && CacheMatchesDoc(doc))
                {
                    var keys = new[] { ProductionContextKey.Identity(ctxTag), legacyTag };
                    for (int k = 0; k < keys.Length; k++)
                    {
                        if (!_existingViewCache.TryGetValue(ViewKey(dtId, keys[k], ruleIdx), out var cachedId)) continue;
                        if (doc.GetElement(cachedId) is View vCached
                            && vCached.IsValidObject && !vCached.IsTemplate)
                        {
                            if (k == 0 || SameLevel(vCached)) return vCached;
                            continue;
                        }
                        _existingViewCache.Remove(ViewKey(dtId, keys[k], ruleIdx));
                    }
                }
                var candidates = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate &&
                        string.Equals(StingTools.Core.ParameterHelpers.GetString(v, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID), dtId, StringComparison.OrdinalIgnoreCase) &&
                        StingTools.Core.ParameterHelpers.GetInt(v, ParamRegistry.STING_PRODUCTION_RULE_IDX, -1) == ruleIdx)
                    .ToList();
                // An id match beats a stale-name match: after a rename the legacy string may
                // now describe a DIFFERENT level that took the old name.
                string Stamp(View v) => StingTools.Core.ParameterHelpers.GetString(v, ParamRegistry.STING_VIEW_CONTEXT_TAG);
                return candidates.FirstOrDefault(v => ProductionContextKey.Matches(Stamp(v), ctxTag, null))
                    ?? candidates.FirstOrDefault(v => string.Equals(Stamp(v), legacyTag, StringComparison.Ordinal) && SameLevel(v));
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }

            // A pre-id stamp names its level by name only: accept it only when the view is
            // on this context's level, so a level that has since taken a renamed level's old
            // name does not adopt that level's views.
            bool SameLevel(View v)
            {
                try { return ctx?.Level == null || v.GenLevel == null || v.GenLevel.Id == ctx.Level.Id; }
                catch (Exception ex) { StingLog.Warn($"FindExistingView level of {v?.Id}: {ex.Message}"); return true; }
            }
        }

        private static string BuildViewName(DrawingType dt, ProductionRule rule, DrawingContext ctx)
        {
            string ctxLabel = ctx?.Level?.Name
                ?? (ctx?.Room != null ? StingTools.Core.ParameterHelpers.GetString(ctx.Room, "Number") : null)
                ?? ctx?.Tag
                ?? "";
            // A scope box on a level: the level alone names every box's view alike ("Arch Plan
            // - Level 1", "(2)", "(3)"…), so which area a view covers could not be told. The
            // tag (an area box's code, a STING:: box's tag) says it.
            if (ctx?.Level != null && ctx.ScopeBox != null && !string.IsNullOrWhiteSpace(ctx.Tag))
                ctxLabel = $"{ctx.Level.Name} - {ctx.Tag}";
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

        /// <summary>
        /// K-12 — the seven ISO name segments, in pattern order, and the sheet
        /// parameter each is stamped into. The eighth, PRJ_SHEET_FULL_REF_TXT, is
        /// their join and is written from these.
        /// </summary>
        private static readonly (string Token, string Param)[] SheetSegmentMap =
        {
            ("project",    "PRJ_SHEET_PROJECT_TXT"),
            ("originator", "PRJ_SHEET_ORIG_TXT"),
            ("vol",        "PRJ_SHEET_VOLUME_TXT"),
            ("lvl",        "PRJ_SHEET_LEVEL_TXT"),
            ("type",       "PRJ_SHEET_TYPE_TXT"),
            ("role",       "PRJ_SHEET_ROLE_TXT"),
            ("seq",        "PRJ_SHEET_SEQ_TXT"),
        };

        /// <summary>
        /// Stamp the ISO segments onto the sheet so a title block can display any
        /// subset of them, and so MatchLineEngine's three PRJ_SHEET_FULL_REF_TXT
        /// readers receive the full reference rather than falling back to the native
        /// sheet number.
        /// <para>
        /// K-13 interaction: a token may now be ABSENT rather than blank. An absent
        /// token is written as its LITERAL ("{project}"), not as an empty string.
        /// The tempting argument for empty — "the sheet number was rejected, so no
        /// sheet exists to mislead anyone" — DOES NOT HOLD here: the sheet is created
        /// before the number is assigned, and the assignment sits in a try/catch that
        /// only logs. So a sheet DOES exist carrying Revit's default number, and an
        /// empty segment on it would read as "this project has no volume code"
        /// rather than "this was never resolved". The literal is visibly wrong, which
        /// is the whole point of K-13.
        /// </para>
        /// </summary>
        private static void StampSheetSegments(
            ViewSheet sheet, IDictionary<string, string> tokens, ProduceResult result)
        {
            if (sheet == null || tokens == null) return;

            var parts = new List<string>();
            foreach (var (token, param) in SheetSegmentMap)
            {
                string value = tokens.TryGetValue(token, out var v) && !string.IsNullOrWhiteSpace(v)
                    ? v
                    : "{" + token + "}";   // unresolved: keep it visibly unresolved
                parts.Add(value);

                SafeWrite.Set(sheet, param,
                    () => ParameterHelpers.SetString(sheet, param, value, overwrite: true),
                    "DrawingProducer.SheetSegments", result?.Warnings);
            }

            string fullRef = string.Join("-", parts);
            SafeWrite.Set(sheet, "PRJ_SHEET_FULL_REF_TXT",
                () => ParameterHelpers.SetString(sheet, "PRJ_SHEET_FULL_REF_TXT", fullRef, overwrite: true),
                "DrawingProducer.SheetSegments", result?.Warnings);
        }

        /// <summary>
        /// DTW-53: the first free name of "baseName", "baseName_(2)" … It stopped at _(99)
        /// and returned that name even when taken, so the rename threw and the view kept
        /// Revit's default name while production reported it made. Now uncapped short of
        /// ViewNameUniquifier.Limit, and running out is reported, never a taken name.
        /// </summary>
        private static string MakeUniqueViewName(Document doc, string baseName, ElementId forView = null, ProduceResult result = null)
        {
            // Outside a primed batch, collect the names once rather than once per probe.
            HashSet<string> local = null;
            if (_existingViewNames == null || !CacheMatchesDoc(doc))
            {
                local = new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(View)))
                        if (el is View v && !v.IsTemplate && !string.IsNullOrEmpty(v.Name)) local.Add(v.Name);
                }
                catch (Exception ex) { StingLog.Warn($"MakeUniqueViewName names: {ex.Message}"); local = null; }
            }
            string name = ViewNameUniquifier.Next(baseName, n => local != null ? local.Contains(n) : NameExists(doc, n));
            if (name == null)
            {
                result?.Warnings.Add($"No free view name found for '{baseName}' (tried up to _({ViewNameUniquifier.Limit})); the view keeps Revit's default name.");
                return baseName;
            }
            // P-12: keep the batch name set current so the next probe in this
            // run sees this name without another collector pass. DTW-45: recorded
            // against the view, so a rollback that removes the view frees the name.
            if (_existingViewNames != null && CacheMatchesDoc(doc))
                _existingViewNames.Record(name, forView?.Value ?? -1);
            return name;
        }

        /// <summary>DTW-45: does element <paramref name="id"/> still exist? False once a
        /// rolled-back transaction has taken it away.</summary>
        private static Func<long, bool> Alive(Document doc) => id =>
        {
            if (id <= 0) return true;   // no owner recorded: treat as a real, standing name
            try { var e = doc.GetElement(new ElementId(id)); return e != null && e.IsValidObject; }
            catch (Exception ex) { StingLog.Warn($"DrawingProducer.Alive({id}): {ex.Message}"); return true; }
        };

        private static bool NameExists(Document doc, string name)
        {
            // P-12: O(1) against the batch name set when primed for this doc.
            if (_existingViewNames != null && CacheMatchesDoc(doc))
                return _existingViewNames.Contains(name, Alive(doc));
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
        private static int ResolveSheetSequence(Document doc, DrawingType dt, DrawingContext ctx,
            string effectivePackage, SheetNumberPolicyKind policy, string numberPattern, ProduceResult result)
        {
            // Phase 169 — persisted sequence counter via ExtensibleStorage on
            // ProjectInfo. Falls back to the per-batch cache (and ultimately a
            // sheet count) when ES is unavailable. Survives Revit restarts and
            // the renumber command's compaction so deleted sheets don't regrow gaps.
            //
            // The bucket comes from SheetNumberEngine.CounterBucket: unchanged
            // (type, package, discipline, vol) under the Profile policy; under ISO,
            // the number's own template, because 29 architectural profiles resolve
            // to the same ISO fields and a per-type counter handed them all 0001.
            try
            {
                var template = NumberTemplate(numberPattern, dt, ctx, BuildTokenDict(doc, dt, ctx, 0));
                var bucket = SheetNumberEngine.CounterBucket(policy, template, dt.Id, effectivePackage,
                    dt.Discipline ?? "", dt.IsoNaming?.Volume ?? "");
                if (policy == SheetNumberPolicyKind.Iso && template == null)
                    result?.Warnings.Add(
                        $"DrawingType '{dt.Id}': sheet-number pattern '{numberPattern}' does not carry exactly one " +
                        "{seq} token, so it cannot share an ISO counter; numbered from its own bucket.");
                return SheetSequenceStore.NextForBucket(doc, bucket,
                    () => SeedSequence(doc, dt, effectivePackage, template));
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
        /// The pattern resolved in everything but the sequence — the shape all
        /// numbers in one counter bucket share. Same field values as
        /// <see cref="SubstituteTokens"/>, so the two cannot disagree.
        /// </summary>
        internal static string NumberTemplate(string pattern, DrawingType dt, DrawingContext ctx,
            IDictionary<string, string> extras)
            => NumberTemplate(pattern, dt, ctx?.Level?.Name, ctx?.Tag, extras);

        internal static string NumberTemplate(string pattern, DrawingType dt, string levelName, string tag,
            IDictionary<string, string> extras)
            => SheetNumberEngine.Template(pattern,
                disc:    dt?.Discipline ?? "",
                lvl:     LevelForPattern(pattern, dt, levelName, extras),
                sys:     dt?.System ?? "",
                mark:    tag ?? "",
                spool:   tag ?? "",
                purpose: dt?.Purpose ?? "",
                extras:  extras);

        /// <summary>
        /// First-use seed for a counter bucket: the highest sequence already on a
        /// sheet in it. With a template the sequence is read against the number's
        /// real shape (an ISO number ends in a revision, not the sequence); without
        /// one, the historical per-type seed applies.
        /// </summary>
        private static int SeedSequence(Document doc, DrawingType dt, string effectivePackage, string template)
        {
            if (string.IsNullOrEmpty(template))
                return SheetSequenceStore.Peek(doc, dt.Id, effectivePackage,
                    dt.Discipline ?? "", dt.IsoNaming?.Volume ?? "") - 1;
            int max = 0;
            foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)))
            {
                if (!(el is ViewSheet vs) || vs.IsPlaceholder) continue;
                // DTW-44: a sheet numbered while the ISO pattern still ended
                // "-{suit}-{rev}" ("…-0003-S2-P01") holds the same container number as
                // "…-0003"; read it without the tail, or the new form restarts at 0001.
                var n = SheetNumberEngine.ExtractSequence(vs.SheetNumber, template)
                     ?? SheetNumberEngine.ExtractSequence(SheetNumberPolicy.StripStatusSuffix(vs.SheetNumber), template);
                if (n.HasValue && n.Value > max) max = n.Value;
            }
            return max;
        }

        /// <summary>
        /// Revit rejects a duplicate sheet number, so a collision would throw
        /// and leave the sheet on its auto-assigned default. Uniquifies through
        /// SheetNumberEngine.MakeUnique — the one rule the fabrication composer
        /// uses too — and ignores the sheet being numbered.
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
                // DTW-45: a number an earlier item in this batch took for a sheet its
                // rollback removed is free again — otherwise this sheet got "-A".
                int freed = _sheetNumberCache.Heal(baseNumber, Alive(doc));
                if (freed > 0)
                    StingLog.Info($"EnsureUniqueSheetNumber: {freed} number(s) under '{baseNumber}' freed — their sheets were rolled back.");
                existing = _sheetNumberCache.Names;
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

            // MakeUnique adds the chosen number to the set, so the batch cache
            // sees it for the next sheet in the same run.
            var chosen = SheetNumberEngine.MakeUnique(baseNumber, existing, out var note);
            if (note != null) result?.Warnings.Add(note);
            if (useCache && chosen != null && excludeId != null)
                _sheetNumberCache.Record(chosen, excludeId.Value);   // DTW-45: owned by this sheet
            return chosen ?? baseNumber;
        }

        private static string SubstituteTokens(string pattern, DrawingType dt, DrawingContext ctx,
            int seq, IDictionary<string, string> extras)
            => SubstituteTokens(pattern, dt, ctx?.Level?.Name, ctx?.Tag, seq, extras);

        /// <summary>
        /// Substitution from the plain field values a sheet was produced with.
        /// The renumber command rebuilds a number from a sheet's stamped context
        /// through THIS method, so it cannot drift from what production wrote.
        /// </summary>
        internal static string SubstituteTokens(string pattern, DrawingType dt, string levelName, string tag,
            int seq, IDictionary<string, string> extras)
            => ApplyTokenPattern(
                pattern,
                disc:    dt?.Discipline ?? "",
                // K-7: {lvl} is consumed HERE, before the extras sweep, so it
                // never sees the token dict — adding the IsoNaming fallback to
                // DrawingTokenContext alone would have fixed the title-block
                // cells and left the sheet number still empty, with the two
                // disagreeing about the same drawing. Apply the same fallback
                // at both ends.
                // DTW-43: an ISO-shaped pattern takes the ISO level code (see LevelForPattern).
                lvl:     LevelForPattern(pattern, dt, levelName, extras),
                sys:     dt?.System ?? "",   // P4 — system code into {sys} for number/name patterns
                mark:    tag ?? "",
                spool:   tag ?? "",
                purpose: dt?.Purpose ?? "",
                seq:     seq,
                extras:  extras);

        /// <summary>
        /// The token substitution, delegated to the Revit-free SheetNumberEngine
        /// so production and renumbering build a sheet's number one way.
        /// </summary>
        internal static string ApplyTokenPattern(string pattern,
            string disc, string lvl, string sys, string mark, string spool, string purpose,
            int seq, IDictionary<string, string> extras)
            => SheetNumberEngine.ApplyTokenPattern(pattern, disc, lvl, sys, mark, spool, purpose, seq, extras);

        private static Dictionary<string, string> BuildTokenDict(Document doc, DrawingType dt, DrawingContext ctx, int seq)
            => BuildTokenDict(doc, dt, ctx?.Level?.Name, ctx?.Tag, ctx?.PackageId, seq);

        internal static Dictionary<string, string> BuildTokenDict(Document doc, DrawingType dt,
            string levelName, string tag, string packageId, int seq)
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
                levelCode:  levelName,
                seq:        seq,
                spool:      tag,
                mark:       tag);
            d["package"] = packageId ?? dt?.PackageId ?? string.Empty;
            // DTW-43: the ISO 19650 code of this level, for an ISO-shaped pattern's {lvl}.
            // Carried in the dict (a key no pattern names) so every caller that numbers
            // through BuildTokenDict + SubstituteTokens — production and Renumber — gets
            // the same code without having to look the level up itself.
            if (!string.IsNullOrEmpty(levelName))
            {
                var iso = SheetNumberPolicy.LevelToken(SheetNumberPolicy.IsoPattern, levelName, IsoLevelMap(doc));
                if (!string.IsNullOrEmpty(iso)) d[IsoLevelKey] = iso;
            }
            return d;
        }

        /// <summary>DTW-43: the token-dict key carrying the level's ISO code. Contains a '.'
        /// so no sheet-number pattern token can name it.</summary>
        internal const string IsoLevelKey = "lvl.iso";

        /// <summary>
        /// DTW-43: {lvl} for <paramref name="pattern"/>. An ISO-shaped pattern
        /// (SheetNumberPolicy.IsAlreadyIso) takes the ISO 19650 level code — the one
        /// ParameterHelpers' sheet level stamp derives (IsoLevelCode over every level) —
        /// instead of the level name cut to eight characters ("Level1", "Mezzanin", and
        /// "Level 1" / "Level 1A" colliding). Every other pattern keeps the name.
        /// </summary>
        private static string LevelForPattern(string pattern, DrawingType dt, string levelName, IDictionary<string, string> extras)
        {
            if (levelName != null && SheetNumberPolicy.IsAlreadyIso(pattern)
                && extras != null && extras.TryGetValue(IsoLevelKey, out var iso) && !string.IsNullOrEmpty(iso))
                return iso;
            return levelName ?? dt?.IsoNaming?.Level ?? "";
        }

        [ThreadStatic] private static Dictionary<string, string> _isoLevelMap;
        [ThreadStatic] private static string _isoLevelMapDocKey;

        /// <summary>ISO 19650 level codes for every level, by name — built as
        /// ParameterHelpers.DeriveSheetLevel builds them, so a sheet's number and its level
        /// stamp agree. Cached per document for the batch.</summary>
        private static Dictionary<string, string> IsoLevelMap(Document doc)
        {
            if (doc == null) return null;
            var key = CacheDocKey(doc);
            if (_isoLevelMap != null && string.Equals(_isoLevelMapDocKey, key, StringComparison.OrdinalIgnoreCase)) return _isoLevelMap;
            var map = BuildIsoLevelMap(doc);
            if (map != null) { _isoLevelMap = map; _isoLevelMapDocKey = key; }
            return map;
        }

        /// <summary>The same map, built fresh (no batch cache) — for callers outside a
        /// production batch, such as title-block heal (DTW-79), where a level may have
        /// been renamed since the last batch.</summary>
        internal static Dictionary<string, string> BuildIsoLevelMap(Document doc)
        {
            if (doc == null) return null;
            try
            {
                var storeys = new List<StoreyDatum>();
                foreach (var l in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
                    if (!string.IsNullOrWhiteSpace(l?.Name))
                        storeys.Add(new StoreyDatum
                        {
                            Name = l.Name,
                            ElevationMm = UnitUtils.ConvertFromInternalUnits(l.Elevation, UnitTypeId.Millimeters),
                        });
                return IsoLevelCode.BuildMap(storeys);
            }
            catch (Exception ex) { StingLog.Warn($"DrawingProducer.IsoLevelMap: {ex.Message}"); return null; }
        }
        /// <summary>
        /// Read the project's sheet-number policy from ProjectInformation.
        /// Absent / unreadable ⇒ null ⇒ SheetNumberPolicy.Parse returns
        /// Profile, i.e. existing behaviour.
        /// </summary>
        internal static string ReadSheetNumberPolicy(Document doc, ProduceResult result = null)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                var p = pi?.LookupParameter(SheetNumberPolicy.PolicyParameterName);
                // DRAW-6: an unbound policy parameter used to read as "not set" with
                // no trace, so a project that believed it had opted into ISO
                // numbering got profile numbers. Say so once per sheet produced;
                // the default still applies, so nothing is renumbered by surprise.
                if (p == null)
                {
                    result?.Warnings.Add(
                        $"{SheetNumberPolicy.PolicyParameterName} is not bound to Project Information, so sheets " +
                        "are numbered by each drawing type's own pattern. Run Load Shared Params to bind it " +
                        "if the project uses ISO 19650 numbering.");
                    return null;
                }
                string value = p.StorageType == StorageType.String ? p.AsString() : null;
                // A typo ("iso 19650") parses as Profile by default — the same silent
                // opt-out as an unbound parameter, so it gets the same warning.
                if (!string.IsNullOrWhiteSpace(value) && !SheetNumberPolicy.IsRecognised(value))
                    result?.Warnings.Add(
                        $"{SheetNumberPolicy.PolicyParameterName} holds '{value.Trim()}', which is not a sheet-number " +
                        "policy STING recognises (iso / profile), so sheets are numbered by each drawing type's own " +
                        "pattern. Set it to 'iso' for ISO 19650 numbering.");
                return value;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ReadSheetNumberPolicy: {ex.Message}");
                return null;
            }
        }

    }
}
