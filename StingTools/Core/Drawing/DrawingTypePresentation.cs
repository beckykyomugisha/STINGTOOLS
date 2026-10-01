using StingTools.Core;
// StingTools — Drawing Template Manager
//
// DrawingTypePresentation is the shared application step for batch
// generators (BatchSections, BatchElevations, BatchSheets, fabrication
// composer). Given a freshly-created View and a resolved DrawingType,
// it applies scale / detail level / view template, runs the annotation
// pass from the rule pack, and (optionally) sets the view's crop
// margin per the crop strategy.
//
// Batch commands should call Apply(...) inside their active Transaction
// after the view has been created but before it is placed on a sheet.
// Null drawingType is a no-op so adding the call is zero-regression.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    public static class DrawingTypePresentation
    {
        // ── C-1 view-template cache ────────────────────────────────────────
        // Per-document map of (templateName → ElementId) so batch producers
        // (BatchSections, BatchElevations, BatchSheets) skip the
        // FilteredElementCollector<View> scan for every view they create.
        // Invalidated on document close and on DrawingTypeRegistry.Reload(doc).
        private static readonly object _viewTemplateCacheLock = new object();
        private static readonly Dictionary<string, Dictionary<string, ElementId>> _viewTemplateCache
            = new Dictionary<string, Dictionary<string, ElementId>>(StringComparer.OrdinalIgnoreCase);

        // ── C-2 pack-resolution cache ─────────────────────────────────────
        // Per-document map of (packId → ViewStylePack) so batch producers
        // resolve each pack only once. Invalidated by the same triggers as
        // the view-template cache.
        private static readonly object _packCacheLock = new object();
        private static readonly Dictionary<string, Dictionary<string, ViewStylePack>> _packCache
            = new Dictionary<string, Dictionary<string, ViewStylePack>>(StringComparer.OrdinalIgnoreCase);

        private static string DocKey(Document doc)
        {
            if (doc == null) return "__null__";
            try { return string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName; }
            catch { return "__unknown__"; }
        }

        /// <summary>
        /// DT-R11: true when the view's template controls any of <paramref name="bips"/>,
        /// so a direct write would be refused ("Detail Level cannot be modified.").
        /// The decision itself is <see cref="TemplateOwnedSetting.OwnedByTemplate"/>.
        /// </summary>
        private static bool TemplateOwns(Document doc, View view, out string templateName, params BuiltInParameter[] bips)
        {
            templateName = null;
            if (doc == null || view == null) return false;
            try
            {
                var tid = view.ViewTemplateId;
                if (!ManagedTemplateSyncer.IsUsable(tid)) return false;
                var tpl = doc.GetElement(tid) as View;
                if (tpl == null) return false;
                templateName = tpl.Name;
                var all = new HashSet<long>(tpl.GetTemplateParameterIds().Select(i => i.Value));
                var free = new HashSet<long>(tpl.GetNonControlledTemplateParameterIds().Select(i => i.Value));
                bool controls = bips.Any(b => all.Contains((long)b) && !free.Contains((long)b));
                bool readOnly = bips.Any(b => view.get_Parameter(b)?.IsReadOnly == true);
                return TemplateOwnedSetting.OwnedByTemplate(true, controls, readOnly);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DrawingTypePresentation: template control check on '{view.Name}': {ex.Message}");
                return false;
            }
        }

        private static readonly BuiltInParameter[] ScaleParams =
            { BuiltInParameter.VIEW_SCALE_PULLDOWN_METRIC, BuiltInParameter.VIEW_SCALE_PULLDOWN_IMPERIAL, BuiltInParameter.VIEW_SCALE };

        private static ElementId ResolveViewTemplate(Document doc, string name)
        {
            if (doc == null || string.IsNullOrWhiteSpace(name)) return ElementId.InvalidElementId;
            string docKey = DocKey(doc);
            Dictionary<string, ElementId> docMap;
            lock (_viewTemplateCacheLock)
            {
                if (!_viewTemplateCache.TryGetValue(docKey, out docMap))
                {
                    docMap = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
                    _viewTemplateCache[docKey] = docMap;
                }
                if (docMap.TryGetValue(name, out ElementId cached))
                {
                    if (ManagedTemplateSyncer.IsUsable(cached))
                    {
                        var elem = doc.GetElement(cached);
                        if (elem is View vTpl && vTpl.IsValidObject && vTpl.IsTemplate)
                            return cached;
                        // Stale positive entry — the template was deleted or
                        // renamed. Drop it and re-resolve.
                        docMap.Remove(name);
                    }
                    else
                    {
                        // E-9: a NEGATIVE entry ("this name resolves to nothing")
                        // used to be evicted on every read, so a template name
                        // that does not exist paid a full OfClass(View) scan per
                        // view in a batch — the cache actively defeated itself
                        // for exactly the case it was meant to make cheap.
                        // Negative entries now persist until Reload/Prewarm.
                        return ElementId.InvalidElementId;
                    }
                }
            }

            var tpl = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v => v.IsTemplate
                    && ManagedTemplateNames.Matches(v.Name, name));   // DT-R11-F: a managed name in either form
            ElementId resolved = tpl?.Id ?? ElementId.InvalidElementId;

            lock (_viewTemplateCacheLock)
            {
                if (_viewTemplateCache.TryGetValue(docKey, out var live))
                    live[name] = resolved;
            }
            return resolved;
        }

        private static ViewStylePack ResolvePackCached(Document doc, string packId)
        {
            if (doc == null || string.IsNullOrWhiteSpace(packId)) return null;
            string docKey = DocKey(doc);
            lock (_packCacheLock)
            {
                if (_packCache.TryGetValue(docKey, out var docMap)
                    && docMap.TryGetValue(packId, out var cached))
                    return cached;
            }

            ViewStylePack resolved = null;
            try { resolved = ViewStylePackRegistry.Get(doc, packId); }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ResolvePackCached '{packId}': {ex.Message}");
            }

            lock (_packCacheLock)
            {
                if (!_packCache.TryGetValue(docKey, out var docMap))
                {
                    docMap = new Dictionary<string, ViewStylePack>(StringComparer.OrdinalIgnoreCase);
                    _packCache[docKey] = docMap;
                }
                docMap[packId] = resolved;
            }
            return resolved;
        }

        /// <summary>
        /// C-1 / D-3: clear the cached view-template ElementIds for a given
        /// document. Wired to <see cref="DrawingTypeRegistry.Reload"/> and the
        /// document-closed handler so a stale ElementId from a previous
        /// session never resolves to a deleted template.
        /// </summary>
        public static void InvalidateViewTemplateCache(Document doc)
        {
            string docKey = DocKey(doc);
            lock (_viewTemplateCacheLock)
            {
                if (_viewTemplateCache.ContainsKey(docKey))
                    _viewTemplateCache.Remove(docKey);
            }
        }

        /// <summary>
        /// DTW-8: forget cached "no such template" answers for a document,
        /// keeping the positive ids (each is re-validated when hit).
        /// </summary>
        private static void DropNegativeViewTemplateEntries(Document doc)
        {
            string docKey = DocKey(doc);
            lock (_viewTemplateCacheLock)
            {
                if (!_viewTemplateCache.TryGetValue(docKey, out var docMap)) return;
                foreach (var name in docMap.Where(kv => kv.Value == ElementId.InvalidElementId)
                                           .Select(kv => kv.Key).ToList())
                    docMap.Remove(name);
            }
        }

        /// <summary>
        /// C-2 / D-3: clear the cached <see cref="ViewStylePack"/> entries for a
        /// given document. Same triggers as
        /// <see cref="InvalidateViewTemplateCache"/>.
        /// </summary>
        public static void InvalidatePackCache(Document doc)
        {
            string docKey = DocKey(doc);
            lock (_packCacheLock)
            {
                if (_packCache.ContainsKey(docKey))
                    _packCache.Remove(docKey);
            }
        }

        /// <summary>
        /// Phase 183 — parses a pack <c>ScaleHint</c> string ("1:50", "50",
        /// or "1 : 100" with whitespace) into a positive integer ratio.
        /// Returns false on empty / non-numeric input so the caller can
        /// silently skip the fallback. Mirrors the editor's parser so the
        /// runtime accepts every form the editor writes.
        /// </summary>
        private static bool TryParseScaleHint(string hint, out int scale)
        {
            scale = 0;
            if (string.IsNullOrWhiteSpace(hint)) return false;
            var s = hint.Trim();
            int colon = s.IndexOf(':');
            if (colon >= 0) s = s.Substring(colon + 1).Trim();
            return int.TryParse(s, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out scale) && scale > 0;
        }

        /// <summary>
        /// D-1: pre-warm the per-document caches for a batch run.
        /// Builds the (template name → ElementId) and (packId → pack) maps in
        /// one pass so subsequent <see cref="Apply"/> calls all hit the cache
        /// regardless of which DrawingType they request. Safe to call outside
        /// any Transaction. Idempotent — re-runs are a no-op once warm.
        /// </summary>
        public static void Prewarm(Document doc)
        {
            if (doc == null) return;
            // DTW-8: a negative entry ("no template of that name") persisted for
            // the life of the document, so a template created or loaded after
            // the first miss was never found. A batch starts from the truth:
            // drop the negatives (positives are re-validated on every hit).
            DropNegativeViewTemplateEntries(doc);
            try
            {
                var lib = DrawingTypeRegistry.GetLibrary(doc);
                if (lib?.DrawingTypes == null) return;

                // Pre-resolve every drawing type once so the C-5 memo is hot
                // (which in turn means every (template name, pack id) referenced
                // by the catalogue gets warmed below).
                foreach (var raw in lib.DrawingTypes)
                {
                    if (string.IsNullOrWhiteSpace(raw.Id)) continue;
                    var dt = DrawingTypeRegistry.Get(doc, raw.Id);
                    if (dt == null) continue;
                    if (!string.IsNullOrWhiteSpace(dt.ViewTemplateName))
                        ResolveViewTemplate(doc, dt.ViewTemplateName);
                    if (!string.IsNullOrWhiteSpace(dt.ViewStylePackId))
                        ResolvePackCached(doc, dt.ViewStylePackId);
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"DrawingTypePresentation.Prewarm: {ex.Message}"); }
        }

        public sealed class ApplyOptions
        {
            /// <summary>
            /// When non-null the supplied <see cref="AnnotationRunOptions"/>
            /// passes through to the runner verbatim — callers use this to
            /// skip individual annotation passes (e.g. SyncStyles wants
            /// VG/template re-apply but no auto-dim/auto-tag).
            /// </summary>
            public AnnotationRunOptions AnnotationOptions { get; set; }
            /// <summary>When true, no writes are made; only validation is run.</summary>
            public bool DryRun { get; set; }

            /// <summary>
            /// P-6: the scope box this view is being produced FOR, when the
            /// caller has one. Passed through to DrawingCropApplier, where it
            /// wins over the profile's static Crop.ScopeBoxName.
            /// </summary>
            public Element ContextScopeBox { get; set; }

            /// <summary>
            /// Skip the per-view symbol-standard drift scan (step 8.5). That
            /// scan runs a FilteredElementCollector over the view's symbols on
            /// every Apply; in a batch of N views it fires N times and emits
            /// N near-identical warnings. Batch producers and heal passes set
            /// this true and rely on the standalone Fix-Symbol-Drift command
            /// instead. Default false preserves the single-view diagnostic.
            /// </summary>
            public bool SkipSymbolDriftCheck { get; set; }

            /// <summary>
            /// DTW-65: apply the type's <c>sectionMarker.farClipMm</c> to a section,
            /// elevation or detail view. Off by default because most callers have
            /// already chosen the depth: a section box from a production preset, or an
            /// elevation preset's far clip, and re-applying a profile (Sync Styles,
            /// heal) must not reset a depth someone adjusted. Set it where the view was
            /// made with no depth of its own (the producer's default section box).
            /// </summary>
            public bool ApplyTypeFarClip { get; set; }

            /// <summary>
            /// DTW-196: the scale to keep instead of the type's — the scale production
            /// fitted this view to its sheet slot (ProducedViewState). 0 = apply the type's.
            /// A re-run reset an auto-fitted view to the type scale, and the view, already on
            /// its sheet, was never fitted again.
            /// </summary>
            public int KeepScale { get; set; }

            /// <summary>
            /// DTW-196: report when the view's template is replaced. Set on a production
            /// re-run, where the view's current template may be one someone chose; it is
            /// still replaced by the type's (lock the view's style to keep a hand-picked
            /// one), but no longer silently. Off for new views, whose template is Revit's
            /// view-type default.
            /// </summary>
            public bool ReportTemplateReplacement { get; set; }
        }

        public sealed class ApplyResult
        {
            public bool ScaleApplied       { get; set; }
            public bool DetailLevelApplied { get; set; }
            /// <summary>DT-R11: the view's template controls the setting, so it was left to the template.</summary>
            public bool ScaleOwnedByTemplate       { get; set; }
            public bool DetailLevelOwnedByTemplate { get; set; }
            public bool TemplateApplied    { get; set; }
            public bool PackApplied        { get; set; }
            public bool CropApplied        { get; set; }
            public bool TokenProfileApplied { get; set; }   // Phase 135 — Step 7.5
            /// <summary>Title-block cells actually changed by <see cref="ApplyToSheet"/>
            /// (DTW-4) — lets a heal pass report sheets it really re-synced.</summary>
            public int TitleBlockParamsWritten { get; set; }
            public AnnotationRunStats Annotation { get; set; }

            // Phase 137 — managed-template routing
            public ElementId ManagedTemplateId      { get; set; } = ElementId.InvalidElementId;
            public bool      ManagedTemplateCreated { get; set; }
            public bool      ManagedTemplateUpdated { get; set; }
            public int       AnnotationTagsPlaced   { get; set; }
            public int       AnnotationDimsPlaced   { get; set; }
            public int       AnnotationDecPlaced    { get; set; }

            public System.Collections.Generic.List<string> Warnings { get; } = new System.Collections.Generic.List<string>();
        }

        /// <summary>
        /// INT-05: sheet-aware apply. Sheets have no scale / detail-level /
        /// view-template / crop / view-style-pack — running the full view
        /// pipeline on a sheet would no-op those steps but still spend time
        /// looking up missing assets. Instead this routes the steps that
        /// matter for sheets:
        ///   1. style-lock check
        ///   2. DrawingType id stamp
        ///   3. PackageId stamp (if profile carries one)
        ///   4. title-block parameter binding
        /// Returns the same <see cref="ApplyResult"/> shape so SheetManager
        /// / ShopDrawingComposer / ProductionRule consume one type.
        /// </summary>
        public static ApplyResult ApplyToSheet(
            Document doc, ViewSheet sheet, DrawingType dt,
            IDictionary<string, string> tokens = null)
        {
            var r = new ApplyResult();
            if (doc == null || sheet == null || dt == null) return r;

            if (DrawingTypeStamper.IsLocked(sheet))
            {
                r.Warnings.Add($"Sheet {sheet.Id} is style-locked; ApplyToSheet skipped.");
                return r;
            }

            // FIX-7: clear keys declared by the *previous* profile on this
            // sheet before stamping the new id. Handles cloned sheets and
            // profile re-assignment so stale title-block cells from the
            // prior profile don't survive.
            try
            {
                var priorStampedId = DrawingTypeStamper.Read(sheet);
                if (!string.IsNullOrEmpty(priorStampedId)
                    && !string.Equals(priorStampedId, dt.Id, StringComparison.OrdinalIgnoreCase))
                {
                    TitleBlockParamApplier.ClearStaleKeysFromPriorProfile(doc, sheet, priorStampedId);
                }
            }
            catch (Exception ex) { r.Warnings.Add($"ApplyToSheet ClearStale: {ex.Message}"); }

            DrawingTypeStamper.Stamp(sheet, dt.Id);
            if (!string.IsNullOrEmpty(dt.PackageId))
                DrawingTypeStamper.StampPackage(sheet, dt.PackageId);

            try
            {
                var effectiveTokens = tokens ?? DrawingTokenContext.BuildForExistingSheet(doc, sheet, dt);
                var tbResult = TitleBlockParamApplier.Apply(doc, sheet, dt, effectiveTokens);
                r.TitleBlockParamsWritten = tbResult.ParamsWritten;
                r.Warnings.AddRange(tbResult.Warnings);
            }
            catch (Exception ex) { r.Warnings.Add($"ApplyToSheet TitleBlockParams: {ex.Message}"); }
            return r;
        }

        /// <summary>
        /// INT-05: sheet-aware apply. Sheets have no scale / detail-level /
        /// view-template / crop / view-style-pack — running the full view
        /// pipeline on a sheet would no-op those steps but still spend time
        /// looking up missing assets. Instead this routes the steps that
        /// matter for sheets:
        ///   1. style-lock check
        ///   2. DrawingType id stamp
        ///   3. PackageId stamp (if profile carries one)
        ///   4. title-block parameter binding
        /// Returns the same <see cref="ApplyResult"/> shape so SheetManager
        /// / ShopDrawingComposer / ProductionRule consume one type.
        /// </summary>

        public static ApplyResult Apply(Document doc, View view, DrawingType dt, bool runAnnotation = true)
            => Apply(doc, view, dt, runAnnotation ? null : new ApplyOptions {
                AnnotationOptions = new AnnotationRunOptions {
                    SkipAutoTag = true, SkipAutoDim = true, SkipDecorative = true, SkipSpots = true
                },
                // A no-annotation apply is a bulk/refresh/heal pass — skip the
                // per-view drift scan too (covers the MEP producers, scope-box
                // refresh, and MEP coordination callers with no per-call edit).
                SkipSymbolDriftCheck = true
            });

        /// <summary>
        /// Apply per-slot overrides on top of the DrawingType defaults that
        /// already landed via <see cref="Apply(Document,View,DrawingType,ApplyOptions)"/>.
        /// Slots can specify a different scale, detail level or view template
        /// — used by fabrication so a 1:20 detail callout slot can sit next
        /// to a 1:50 spool overview on the same sheet.
        /// </summary>
        public static void ApplySlotOverrides(Document doc, View view, DrawingSlot slot, ApplyResult result)
        {
            if (doc == null || view == null || slot == null) return;
            if (view.IsTemplate) return;
            if (DrawingTypeStamper.IsLocked(view)) return;

            // Correct order: template first, then per-slot scale/level overrides (slot wins).
            // 1. Apply slot view template (if set)
            // 2. Apply slot.Scale (override whatever the template set)
            // 3. Apply slot.DetailLevel (override whatever the template set)

            // 1. View template — applied first so steps 2 & 3 can override it.
            if (!string.IsNullOrWhiteSpace(slot.ViewTemplate))
            {
                try
                {
                    var tplId = ResolveViewTemplate(doc, slot.ViewTemplate);
                    if (ManagedTemplateSyncer.IsUsable(tplId))
                    {
                        view.ViewTemplateId = tplId;
                        if (result != null) result.TemplateApplied = true;
                    }
                    else
                    {
                        result?.Warnings.Add($"Slot ViewTemplate '{slot.ViewTemplate}' not found.");
                    }
                }
                catch (Exception ex)
                {
                    result?.Warnings.Add($"Slot ViewTemplate: {ex.Message}");
                }
            }

            // 2. Per-slot scale is applied AFTER template so it wins over template-controlled scale.
            // DT-R11: a template that CONTROLS scale makes Revit refuse the write; the
            // template owns it then, and that is logged rather than reported as a failure.
            if (slot.Scale.HasValue && slot.Scale.Value > 0
                && TemplateOwns(doc, view, out var slotScaleTpl, ScaleParams))
            {
                if (result != null) result.ScaleOwnedByTemplate = true;
                StingLog.Info("DrawingTypePresentation: " + TemplateOwnedSetting.Note("Slot scale", "1:" + slot.Scale.Value, view.Name, slotScaleTpl));
            }
            else if (slot.Scale.HasValue && slot.Scale.Value > 0)
            {
                try
                {
                    view.Scale = slot.Scale.Value;
                    if (result != null) result.ScaleApplied = true;
                }
                catch (Exception ex)
                {
                    result?.Warnings.Add($"Slot scale 1:{slot.Scale.Value} on {view.Id}: {ex.Message}");
                }
            }

            // 3. Per-slot detail level — applied after template for the same reason as scale.
            if (!string.IsNullOrWhiteSpace(slot.DetailLevel)
                && TemplateOwns(doc, view, out var slotDetailTpl, BuiltInParameter.VIEW_DETAIL_LEVEL))
            {
                if (result != null) result.DetailLevelOwnedByTemplate = true;
                StingLog.Info("DrawingTypePresentation: " + TemplateOwnedSetting.Note("Slot DetailLevel", slot.DetailLevel, view.Name, slotDetailTpl));
            }
            else if (!string.IsNullOrWhiteSpace(slot.DetailLevel))
            {
                try
                {
                    ViewDetailLevel parsed;
                    switch (slot.DetailLevel.Trim().ToLowerInvariant())
                    {
                        case "coarse": parsed = ViewDetailLevel.Coarse; break;
                        case "fine":   parsed = ViewDetailLevel.Fine;   break;
                        default:       parsed = ViewDetailLevel.Medium; break;
                    }
                    view.DetailLevel = parsed;
                    if (result != null) result.DetailLevelApplied = true;
                }
                catch (Exception ex)
                {
                    result?.Warnings.Add($"Slot DetailLevel {slot.DetailLevel}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// The scope box a view was produced for, when the caller did not pass one: the
        /// box it is cropped to now, or — if that was lost — the box its context tag
        /// names. Null for a view produced without a box (the profile's crop applies).
        /// <paramref name="leaveCrop"/> is set when the tag names a box that no longer
        /// exists and the view has none: re-cropping by the profile would then replace a
        /// scope-box crop with something else, so the crop is left as it is.
        /// </summary>
        private static Element RecoverContextScopeBox(Document doc, View view, ApplyResult r, out bool leaveCrop)
        {
            leaveCrop = false;
            string tagBox;
            try { tagBox = ViewContextTag.ScopeBoxName(StingTools.Core.ParameterHelpers.GetString(view, ParamRegistry.STING_VIEW_CONTEXT_TAG)); }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Context tag of '{view?.Name}': {ex.Message}"); return null; }
            if (string.IsNullOrEmpty(tagBox)) return null;

            Element assigned = null;
            try
            {
                var id = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP)?.AsElementId();
                if (id != null && id != ElementId.InvalidElementId) assigned = doc.GetElement(id);
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"Scope box of '{view?.Name}': {ex.Message}"); }

            Element named = null;
            if (assigned == null)
            {
                try
                {
                    named = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                        .WhereElementIsNotElementType()
                        .FirstOrDefault(e => string.Equals(e.Name, tagBox, StringComparison.OrdinalIgnoreCase));
                }
                catch (Exception ex) { StingTools.Core.StingLog.Warn($"Find scope box '{tagBox}': {ex.Message}"); }
            }

            switch (ViewContextTag.Decide(tagBox, assigned != null, named != null))
            {
                case CropRecovery.KeepAssigned:
                    return assigned;
                case CropRecovery.RestoreFromTag:
                    r.Warnings.Add($"'{view.Name}' had lost its scope box; re-cropped to '{tagBox}', the box it was produced for.");
                    return named;
                case CropRecovery.LeaveAlone:
                    leaveCrop = true;
                    r.Warnings.Add($"'{view.Name}' was produced for scope box '{tagBox}', which is gone; its crop was left as it is.");
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// DTW-65: set a section / elevation / detail view's far clip to the type's
        /// <c>sectionMarker.farClipMm</c>. Turns far clipping on ("clip without
        /// line") when it is off, because the offset does nothing otherwise. A
        /// template that controls far clipping makes the parameters read-only; that
        /// is logged and left to the template. Returns true when the depth was set.
        /// </summary>
        public static bool ApplySectionMarkerFarClip(View view, DrawingType dt, ApplyResult r = null)
        {
            var spec = dt?.SectionMarker;
            if (view == null || spec == null || string.IsNullOrWhiteSpace(spec.Family) || spec.FarClipMm <= 0) return false;
            if (view.ViewType != ViewType.Section && view.ViewType != ViewType.Elevation && view.ViewType != ViewType.Detail)
                return false;
            try
            {
                var offset = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
                if (offset == null || offset.IsReadOnly)
                {
                    StingTools.Core.StingLog.Info($"'{view.Name}': far clip is controlled elsewhere (template or view type); {dt.Id} farClipMm not applied.");
                    return false;
                }
                var clipping = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_FAR_CLIPPING);
                if (clipping != null && !clipping.IsReadOnly && clipping.StorageType == StorageType.Integer && clipping.AsInteger() == 0)
                    clipping.Set(2); // 0 no clip · 1 clip with line · 2 clip without line
                bool ok = offset.Set(spec.FarClipMm / 304.8);
                if (!ok) r?.Warnings.Add($"'{view.Name}': Revit refused far clip {spec.FarClipMm:0} mm.");
                return ok;
            }
            catch (Exception ex)
            {
                r?.Warnings.Add($"'{view.Name}': far clip {spec.FarClipMm:0} mm: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// DTW-62: the explicit template name for <paramref name="view"/>: the
        /// <c>viewTemplateOverride</c> of the first production rule that makes this
        /// kind of view, else the type's <c>viewTemplateName</c>.
        /// </summary>
        internal static string ExplicitTemplateNameFor(DrawingType dt, View view)
        {
            if (dt == null) return null;
            if (dt.ProductionRules != null && view != null)
            {
                string kind = DrawingTemplateCatalogue.ViewKindOf(view.ViewType.ToString());
                if (kind != null)
                {
                    var rule = dt.ProductionRules.FirstOrDefault(pr =>
                        !string.IsNullOrWhiteSpace(pr?.ViewTemplateOverride)
                        && string.Equals(DrawingTemplateCatalogue.ViewKindOf(pr.ViewType), kind, StringComparison.OrdinalIgnoreCase));
                    if (rule != null) return rule.ViewTemplateOverride.Trim();
                }
            }
            return dt.ViewTemplateName;
        }

        public static ApplyResult Apply(Document doc, View view, DrawingType dt, ApplyOptions options)
        {
            var r = new ApplyResult();
            if (doc == null || view == null || dt == null) return r;
            if (view.IsTemplate) return r;

            // Week 3 — stamp the DrawingType id so the Project Browser
            // organizer, the style-propagation IUpdater, and downstream
            // audits all know which profile produced this view. No-op
            // on projects where the shared param has not been bound;
            // no-op when user has locked the view's style.
            if (DrawingTypeStamper.IsLocked(view))
            {
                r.Warnings.Add($"View {view.Id} is style-locked; presentation skipped.");
                return r;
            }
            DrawingTypeStamper.Stamp(view, dt.Id);

            // DTW-196: the template the view had before this apply, so a replacement on a
            // re-run is reported rather than silent.
            ElementId priorTemplateId = ElementId.InvalidElementId;
            if (options?.ReportTemplateReplacement == true)
            {
                try { priorTemplateId = view.ViewTemplateId ?? ElementId.InvalidElementId; }
                catch (Exception ex) { StingLog.Warn($"Apply prior template of '{view.Name}': {ex.Message}"); }
            }

            // Phase 183 — pack-fallback resolution. Resolve the bound pack
            // up-front so Scale / DetailLevel / ViewTemplate steps below can
            // fall back to pack defaults whenever the DrawingType leaves the
            // slot empty. Phase 136 introduced the pack fields but the
            // fallback chain was never wired into Apply — only managed mode
            // (Phase 137) consumed the pack template. Profiles that opt out
            // of templating ended up with no template at all.
            //
            // DrawingType always wins when both set the same field. Managed
            // packs still mint their own template after this block, so the
            // managed-mode override below takes precedence over the external
            // fallback.
            // Resolve through ViewStylePackRegistry.ResolveForDrawingType so a
            // profile that names no pack still picks one up from the library's
            // routing table. Before this, the four structural profiles, the
            // three schedule profiles and pres-narrative-A1 — 8 of 93 — got no
            // VG overrides and no filters whatsoever, silently, because
            // fallbackPack stayed null and every downstream step is guarded on
            // it. A routed pack is logged, because "where did this styling come
            // from" should never need inferring.
            ViewStylePack fallbackPack = null;
            string packSource = "none";
            try
            {
                fallbackPack = ViewStylePackRegistry.ResolveForDrawingType(doc, dt, out packSource);
                if (fallbackPack != null && !string.Equals(packSource, "profile", StringComparison.OrdinalIgnoreCase))
                    StingTools.Core.StingLog.Info(
                        $"DrawingType '{dt.Id}' names no view style pack; resolved '{fallbackPack.Id}' via {packSource}.");
                else if (fallbackPack == null)
                    r.Warnings.Add(
                        $"DrawingType '{dt.Id}' resolves to no view style pack (no viewStylePackId and no matching routing rule) — "
                        + "no category overrides or filters will be applied to this view.");
            }
            catch (Exception ex) { r.Warnings.Add($"ViewStylePack resolve: {ex.Message}"); }
            int effectiveScale = dt.Scale;
            string effectiveDetailLevel = dt.DetailLevel;
            bool scaleFromPack = false, detailFromPack = false;
            if (fallbackPack != null && !fallbackPack.IsManaged)
            {
                if (effectiveScale <= 0 && !string.IsNullOrWhiteSpace(fallbackPack.ScaleHint)
                    && TryParseScaleHint(fallbackPack.ScaleHint, out var packScale))
                { effectiveScale = packScale; scaleFromPack = true; }
                if (string.IsNullOrWhiteSpace(effectiveDetailLevel) && !string.IsNullOrWhiteSpace(fallbackPack.DetailLevel))
                { effectiveDetailLevel = fallbackPack.DetailLevel; detailFromPack = true; }
            }
            // DTW-196: a view production fitted to its slot keeps that scale on a re-run.
            if (options != null && options.KeepScale > 0)
            {
                if (effectiveScale != options.KeepScale)
                    StingLog.Info($"DrawingType '{dt.Id}': '{view.Name}' keeps its fitted scale 1:{options.KeepScale} (type 1:{effectiveScale}).");
                effectiveScale = options.KeepScale;
                scaleFromPack = false;
            }

            // Scale -------------------------------------------------------
            // DT-R11: when the view's template controls scale Revit refuses the write;
            // the template owns it, which is logged, not reported as a failure.
            if (effectiveScale > 0 && TemplateOwns(doc, view, out var scaleTpl, ScaleParams))
            {
                r.ScaleOwnedByTemplate = true;
                StingLog.Info("DrawingTypePresentation: " + TemplateOwnedSetting.Note("Scale", "1:" + effectiveScale, view.Name, scaleTpl));
            }
            else if (effectiveScale > 0)
            {
                try
                {
                    // Only views that expose Scale (plans, sections,
                    // elevations, drafting, 3D) accept an int; schedules
                    // and legends throw.
                    view.Scale = effectiveScale;
                    r.ScaleApplied = true;
                    if (scaleFromPack)
                        StingTools.Core.StingLog.Info(
                            $"DrawingType '{dt.Id}' inherited scale 1:{effectiveScale} from pack '{fallbackPack.Id}'.");
                }
                catch (Exception ex) { r.Warnings.Add($"Scale 1:{effectiveScale}: {ex.Message}"); }
            }
            else
            {
                // 3D / perspective profiles legitimately ship without a fixed
                // scale; assigning view.Scale = 0 throws InvalidOperationException.
                StingTools.Core.StingLog.Info(
                    $"DrawingType '{dt.Id}' has scale <= 0 and no pack fallback; skipping view scale assignment.");
            }

            // Detail level -----------------------------------------------
            // DT-R11: "Detail Level cannot be modified." was the view's template doing its
            // job — it controls VIEW_DETAIL_LEVEL. Leave it to the template and say so.
            if (!string.IsNullOrWhiteSpace(effectiveDetailLevel)
                && TemplateOwns(doc, view, out var detailTpl, BuiltInParameter.VIEW_DETAIL_LEVEL))
            {
                r.DetailLevelOwnedByTemplate = true;
                StingLog.Info("DrawingTypePresentation: " + TemplateOwnedSetting.Note("DetailLevel", effectiveDetailLevel, view.Name, detailTpl));
            }
            else if (!string.IsNullOrWhiteSpace(effectiveDetailLevel))
            {
                try
                {
                    ViewDetailLevel parsed;
                    switch (effectiveDetailLevel.Trim().ToLowerInvariant())
                    {
                        case "coarse": parsed = ViewDetailLevel.Coarse; break;
                        case "fine":   parsed = ViewDetailLevel.Fine;   break;
                        default:       parsed = ViewDetailLevel.Medium; break;
                    }
                    view.DetailLevel = parsed;
                    r.DetailLevelApplied = true;
                    if (detailFromPack)
                        StingTools.Core.StingLog.Info(
                            $"DrawingType '{dt.Id}' inherited DetailLevel '{effectiveDetailLevel}' from pack '{fallbackPack.Id}'.");
                }
                catch (Exception ex) { r.Warnings.Add($"DetailLevel {effectiveDetailLevel}: {ex.Message}"); }
            }

            // Template Priority (highest to lowest):
            //   1. The explicit template for THIS view: the viewTemplateOverride of the
            //      production rule that makes this kind of view, else dt.ViewTemplateName.
            //   2. Managed pack template (STING MANAGED - {packId} - {ViewType}) — applied when there is
            //      no explicit template, it is not in the project, or it is for another
            //      kind of view.
            // Rationale: named templates carry user customisations that should not be silently
            // discarded; managed templates are the fallback for new projects without existing templates.
            //
            // DTW-62: a mixed-kind type (spool plan + ISO, coordination plan + ISO +
            // section, 3D axon + key plan) names one template, which fits only one of
            // its views. Assigning it to the others threw, and every sheet reported a
            // warning for what is the expected case. A template of another kind is now
            // skipped with a log line and the pack's kind-appropriate template applies.
            //
            // C-1: cached lookup; FilteredElementCollector<View> only runs
            // on first miss per (docKey, templateName).
            bool explicitTemplateApplied = false;
            string explicitTemplateName = ExplicitTemplateNameFor(dt, view);
            if (!string.IsNullOrWhiteSpace(explicitTemplateName))
            {
                try
                {
                    ElementId tplId = ResolveViewTemplate(doc, explicitTemplateName);
                    if (tplId != null && tplId != ElementId.InvalidElementId)
                    {
                        if (view.IsValidViewTemplate(tplId))
                        {
                            view.ViewTemplateId = tplId;
                            r.TemplateApplied = true;
                            explicitTemplateApplied = true;
                        }
                        else
                        {
                            StingTools.Core.StingLog.Info(
                                $"DrawingTypePresentation.Apply: '{explicitTemplateName}' ({dt.Id}) is not a {view.ViewType} template; " +
                                $"'{view.Name}' takes the view style pack's template instead.");
                        }
                    }
                    else
                    {
                        StingTools.Core.StingLog.Warn($"DrawingTypePresentation.Apply: viewTemplateName '{explicitTemplateName}' not found in project — falling back to managed pack template.");
                        r.Warnings.Add($"View template '{explicitTemplateName}' not found in project; falling back to managed pack template.");
                    }
                }
                catch (Exception ex)
                {
                    StingTools.Core.StingLog.Warn($"DrawingTypePresentation.Apply: could not apply view template '{explicitTemplateName}' — {ex.Message}");
                    r.Warnings.Add($"ViewTemplate: {ex.Message}");
                }
            }

            // Crop strategy (bonus) -------------------------------------
            if (dt.Crop != null)
            {
                try
                {
                    // A caller that re-applies the profile (Sync Styles, Produce &
                    // Export's style phase, a production re-run's refresh, drift heal)
                    // has no production context. Without the box the view was produced
                    // for, the profile's own crop kind ran and replaced it. Recover it
                    // from the view (ViewContextTag.Decide).
                    var contextBox = options?.ContextScopeBox;
                    bool leaveCrop = false;
                    if (contextBox == null) contextBox = RecoverContextScopeBox(doc, view, r, out leaveCrop);
                    if (!leaveCrop)
                    {
                        var cropWarns = DrawingCropApplier.Apply(doc, view, dt, contextBox);
                        r.Warnings.AddRange(cropWarns);
                        r.CropApplied = true;
                    }
                }
                catch (Exception ex) { r.Warnings.Add($"CropApplier: {ex.Message}"); }
            }

            // Section-marker far clip (DTW-65, opt-in) ----------------
            if (options?.ApplyTypeFarClip == true)
                ApplySectionMarkerFarClip(view, dt, r);

            // View Style Pack (shared graphic overrides) ---------------
            // Phase 137 — managed packs route through ManagedTemplateSyncer
            // which mints (or updates) a "STING MANAGED - {packId} - {ViewType}"
            // template and assigns it to the view; non-managed packs apply
            // their VG / filter / etc. payload directly to the view.
            // Gate on the RESOLVED pack, not on dt.ViewStylePackId. Gating on
            // the id meant a pack that arrived from the routing table was
            // resolved up-front and then never applied.
            ViewStylePack resolvedPack = fallbackPack;
            if (resolvedPack != null || !string.IsNullOrWhiteSpace(dt.ViewStylePackId))
            {
                try
                {
                    // C-2: cached lookup so batch producers resolve a given
                    // pack only once per session. Reuses the fallback resolution
                    // performed up-front when it succeeded.
                    if (resolvedPack == null && !string.IsNullOrWhiteSpace(dt.ViewStylePackId))
                        resolvedPack = ResolvePackCached(doc, dt.ViewStylePackId);
                    if (resolvedPack == null)
                    {
                        r.Warnings.Add($"ViewStylePack '{dt.ViewStylePackId}' not found.");
                    }
                    else if (resolvedPack.IsManaged)
                    {
                        var syncResult = new PackApplyResult();
                        var templateId = ManagedTemplateSyncer.EnsureTemplate(doc, resolvedPack, view.ViewType, syncResult);
                        r.Warnings.AddRange(syncResult.Warnings);
                        if (ManagedTemplateSyncer.IsUsable(templateId))
                        {
                            r.ManagedTemplateId = templateId;
                            r.ManagedTemplateCreated = !r.TemplateApplied;
                            r.ManagedTemplateUpdated = true;
                            r.PackApplied = true;

                            // Only assign the managed template to the view when no explicit
                            // dt.ViewTemplateName was resolved — explicit template wins (PACK-1).
                            if (!explicitTemplateApplied)
                            {
                                bool valid = true;
                                try { valid = view.IsValidViewTemplate(templateId); }
                                catch (Exception ex) { r.Warnings.Add($"Managed template validity check: {ex.Message}"); }
                                if (!valid)
                                {
                                    // DTW-176: e.g. a schedule template minted from a
                                    // schedule of another category. Say so and style the
                                    // view directly rather than throw on assignment.
                                    r.Warnings.Add($"Managed template for pack '{resolvedPack.Id}' is not valid for "
                                        + $"view '{view.Name}' ({view.ViewType}) — pack applied to the view directly.");
                                    var directStats = ViewStylePackApplier.Apply(doc, view, resolvedPack,
                                        DrawingPrintApplier.EffectiveLineWeightScale(resolvedPack, dt));
                                    DrawingPrintApplier.ApplyHalftoneLinks(doc, view, dt, directStats);
                                    r.Warnings.AddRange(directStats.Warnings);
                                }
                                else
                                {
                                    try
                                    {
                                        view.ViewTemplateId = templateId;
                                        r.TemplateApplied = true;
                                    }
                                    catch (Exception ex) { r.Warnings.Add($"Assign managed template: {ex.Message}"); }
                                }
                            }

                            // DTW-163: the template carries the pack's V/G and
                            // filters and now controls them. What a template can
                            // never carry — per-link element overrides — goes on
                            // the view itself.
                            if (resolvedPack.LinkOverrides != null)
                            {
                                var linkRes = new PackApplyResult();
                                ViewStylePackApplier.ApplyLinkOverrides(doc, view, resolvedPack, linkRes);
                                r.Warnings.AddRange(linkRes.Warnings);
                            }
                        }
                        else
                        {
                            r.Warnings.Add($"ViewStylePack '{dt.ViewStylePackId}' is managed but no template could be minted — falling back to external apply.");
                            var packStats = ViewStylePackApplier.Apply(doc, view, resolvedPack,
                                DrawingPrintApplier.EffectiveLineWeightScale(resolvedPack, dt));
                            DrawingPrintApplier.ApplyHalftoneLinks(doc, view, dt, packStats);
                            r.PackApplied = true;
                            r.Warnings.AddRange(packStats.Warnings);
                        }
                    }
                    else
                    {
                        // print.lineWeightScale folds into the pack scale, and
                        // print.halftoneLinks finally acts. Both were declared on
                        // 90 profiles and read by nothing but the Excel round-trip.
                        var packStats = ViewStylePackApplier.Apply(doc, view, resolvedPack,
                            DrawingPrintApplier.EffectiveLineWeightScale(resolvedPack, dt));
                        DrawingPrintApplier.ApplyHalftoneLinks(doc, view, dt, packStats);
                        r.PackApplied = true;
                        r.Warnings.AddRange(packStats.Warnings);
                    }
                }
                catch (Exception ex) { r.Warnings.Add($"ViewStylePack: {ex.Message}"); }
            }
            // E-9b: an `else if (!string.IsNullOrWhiteSpace(dt.ViewStylePackId))`
            // stood here — the else branch of that very condition, so provably
            // unreachable. Its "pack not found" warning is already emitted
            // inside the if, where resolvedPack is actually tested.

            // DTW-196: a re-run replacing the view's template says so.
            if (options?.ReportTemplateReplacement == true && ManagedTemplateSyncer.IsUsable(priorTemplateId))
            {
                try
                {
                    var now = view.ViewTemplateId ?? ElementId.InvalidElementId;
                    if (now != priorTemplateId)
                    {
                        var line = ProductionEdgeDecisions.TemplateReplacedLine(view.Name,
                            (doc.GetElement(priorTemplateId) as View)?.Name,
                            now == ElementId.InvalidElementId ? null : (doc.GetElement(now) as View)?.Name, dt.Id);
                        r.Warnings.Add(line);
                        StingLog.Info("DrawingTypePresentation: " + line);
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Apply template report for '{view.Name}': {ex.Message}"); }
            }

            // Token Profile (Phase 135) — Step 7.5 -----------------------
            // Runs between the pack apply and the annotation pass so any
            // auto-tags AnnotationRunner emits inherit the active style
            // preset, paragraph depth, section visibility, and segment
            // mask. No-op when neither the profile nor the pack supplies
            // any tag-appearance value.
            if (dt.TokenProfile != null
                || resolvedPack?.TagColorScheme != null
                || resolvedPack?.DefaultTagStyle != null
                || (resolvedPack?.CategoryTagStyles != null && resolvedPack.CategoryTagStyles.Count > 0))
            {
                try
                {
                    var tpRes = TokenProfileApplier.Apply(doc, view, dt, resolvedPack);
                    r.TokenProfileApplied = tpRes.ViewParamWrites + tpRes.ElementWrites
                                          + tpRes.TypeWrites > 0 || tpRes.PresentationApplied;
                    r.Warnings.AddRange(tpRes.Warnings);
                }
                catch (Exception ex) { r.Warnings.Add($"TokenProfileApplier: {ex.Message}"); }
            }

            // Step 7.6 (FIX-3b) — refresh ASS_DISPLAY_TXT so the DrawingType's
            // display mode + segment mask actually render on produce. The
            // annotation pass drops IndependentTags whose label reads the host's
            // ASS_DISPLAY_TXT; TokenProfileApplier just wrote STING_DISPLAY_MODE +
            // TAG_SEG_MASK on those hosts, but only the interactive
            // BuildAndWriteTag path recomputes ASS_DISPLAY_TXT — the produce path
            // never did, so a dropped-in view showed the stale display string.
            // Recompute it here (mask now applies in every mode — see
            // TagConfig.BuildDisplayTag D5) so no separate tag pass is needed.
            if (dt.TokenProfile != null
                && (dt.TokenProfile.DisplayMode.HasValue
                    || !string.IsNullOrWhiteSpace(dt.TokenProfile.SegmentMask)))
            {
                try
                {
                    int refreshed = 0;
                    var ids = new FilteredElementCollector(doc, view.Id)
                        .WhereElementIsNotElementType()
                        .ToElementIds();
                    foreach (var id in ids)
                    {
                        var el = doc.GetElement(id);
                        if (el == null) continue;
                        try
                        {
                            // BuildDisplayTag self-skips elements with no tokens
                            // (returns "" without writing), so this is safe to
                            // run across the whole view.
                            if (!string.IsNullOrEmpty(TagConfig.BuildDisplayTag(el))) refreshed++;
                        }
                        catch { /* per-element — keep going */ }
                    }
                    StingLog.Info($"DrawingTypePresentation: refreshed ASS_DISPLAY_TXT on {refreshed} element(s) for mask/mode.");
                }
                catch (Exception ex) { r.Warnings.Add($"Display refresh: {ex.Message}"); }
            }

            // Phase 175 — Step 7.7 design-option scope. Resolves the
            // profile's OptionScope to a concrete option ElementId and
            // writes VIEWER_OPTION_VISIBILITY on the view. Runs after
            // TokenProfile so annotations inherit the option-aware tag
            // suffix when the profile sets one.
            if (dt.OptionScope != null)
            {
                try
                {
                    var optRes = DrawingOptionApplier.Apply(doc, view, dt);
                    if (!string.IsNullOrEmpty(optRes.Warning))
                        r.Warnings.Add($"DrawingOptionApplier: {optRes.Warning}");
                }
                catch (Exception ex) { r.Warnings.Add($"DrawingOptionApplier: {ex.Message}"); }
            }

            // Annotation pass --------------------------------------------
            // Phase 137 — explicit AnnotationRunOptions plumbing so callers
            // (SyncStyles, batch producers) can skip individual passes.
            if (dt.Annotation != null || options?.AnnotationOptions?.PackOverride != null)
            {
                try
                {
                    var annOpts = options?.AnnotationOptions ?? new AnnotationRunOptions
                    {
                        ViewScale = dt.Scale > 0 ? dt.Scale : view.Scale
                    };
                    var annResult = AnnotationRunner.Run(doc, view, dt, annOpts);
                    r.AnnotationTagsPlaced = annResult.TagsPlaced;
                    r.AnnotationDimsPlaced = annResult.DimsPlaced;
                    r.AnnotationDecPlaced  = annResult.DecorativePlaced;
                    r.Warnings.AddRange(annResult.Warnings);

                    // Populate legacy AnnotationRunStats so callers reading
                    // the old field (e.g. existing tests / UI) keep working.
                    r.Annotation = new AnnotationRunStats
                    {
                        TagsPlaced  = annResult.TagsPlaced,
                        DimsCreated = annResult.DimsPlaced
                    };
                    foreach (var w in annResult.Warnings) r.Annotation.Warnings.Add(w);
                }
                catch (Exception ex) { r.Warnings.Add($"AnnotationRunner: {ex.Message}"); }
            }

            // Step 8.5 — Phase 175 symbol-standard drift gate.
            // Read-only: surfaces drift as a warning so SyncStyles or
            // FixSymbolDriftCommand can heal it. Doesn't auto-apply to
            // avoid surprising the user mid-Apply. Skipped in batch/heal
            // passes (per-view scan; see ApplyOptions.SkipSymbolDriftCheck).
            if (!(options?.SkipSymbolDriftCheck ?? false))
            {
                try
                {
                    string activeStd = StingTools.Core.Symbols.SymbolStandardResolver
                        .ResolveStandard(doc, view, null);
                    var driftReport = StingTools.Core.Symbols.SymbolDriftDetector
                        .DetectDrift(doc, view);
                    if (driftReport.DriftedSymbols > 0)
                    {
                        r.Warnings.Add(
                            $"Symbol-standard drift in view: {driftReport.DriftedSymbols} symbol(s) "
                          + $"don't match resolved standard ({activeStd}). "
                          + "Run 'Fix Symbol Drift' to heal.");
                    }
                }
                catch (Exception ex) { r.Warnings.Add($"Symbol drift check: {ex.Message}"); }
            }

            return r;
        }
    }
}
