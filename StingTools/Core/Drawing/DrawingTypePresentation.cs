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
                    if (cached != ElementId.InvalidElementId)
                    {
                        var elem = doc.GetElement(cached);
                        if (elem is View vTpl && vTpl.IsValidObject && vTpl.IsTemplate)
                            return cached;
                    }
                    docMap.Remove(name);
                }
            }

            var tpl = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v => v.IsTemplate
                    && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
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
        }

        public sealed class ApplyResult
        {
            public bool ScaleApplied       { get; set; }
            public bool DetailLevelApplied { get; set; }
            public bool TemplateApplied    { get; set; }
            public bool PackApplied        { get; set; }
            public bool CropApplied        { get; set; }
            public bool TokenProfileApplied { get; set; }   // Phase 135 — Step 7.5
            public AnnotationRunStats Annotation { get; set; }

            // Phase 137 — managed-template routing
            public ElementId ManagedTemplateId      { get; set; } = ElementId.InvalidElementId;
            public bool      ManagedTemplateCreated { get; set; }
            public bool      ManagedTemplateUpdated { get; set; }
            public int       AnnotationTagsPlaced   { get; set; }
            public int       AnnotationDimsPlaced   { get; set; }
            public int       AnnotationDecPlaced    { get; set; }

            public System.Collections.Generic.List<string> Warnings { get; } = new System.Collections.Generic.List<string>();

            // Phase 137 — STING-Managed View Templates
            public ElementId ManagedTemplateId { get; set; } = ElementId.InvalidElementId;
            public bool ManagedTemplateCreated { get; set; }
            public bool ManagedTemplateUpdated { get; set; }
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
                var effectiveTokens = tokens ?? DrawingTokenContext.Build(
                    doc:        doc,
                    dt:         dt,
                    discCode:   dt.Discipline,
                    discipline: dt.Discipline,
                    seq:        DrawingTokenContext.ExtractSeqFromSheetNumber(sheet.SheetNumber));
                var tbResult = TitleBlockParamApplier.Apply(doc, sheet, dt, effectiveTokens);
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
                var effectiveTokens = tokens ?? DrawingTokenContext.Build(
                    doc:        doc,
                    dt:         dt,
                    discCode:   dt.Discipline,
                    discipline: dt.Discipline,
                    seq:        DrawingTokenContext.ExtractSeqFromSheetNumber(sheet.SheetNumber));
                var tbResult = TitleBlockParamApplier.Apply(doc, sheet, dt, effectiveTokens);
                r.Warnings.AddRange(tbResult.Warnings);
            }
            catch (Exception ex) { r.Warnings.Add($"ApplyToSheet TitleBlockParams: {ex.Message}"); }
            return r;
        }

        public static ApplyResult Apply(Document doc, View view, DrawingType dt, bool runAnnotation = true)
            => Apply(doc, view, dt, runAnnotation ? null : new ApplyOptions {
                AnnotationOptions = new AnnotationRunOptions {
                    SkipAutoTag = true, SkipAutoDim = true, SkipDecorative = true, SkipSpots = true
                }
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
                    if (tplId != ElementId.InvalidElementId)
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
            // If the template locks scale (IsTemplateParameterDisplayed = false for View.Scale),
            // Revit will silently ignore this write — acceptable trade-off.
            if (slot.Scale.HasValue && slot.Scale.Value > 0)
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
            if (!string.IsNullOrWhiteSpace(slot.DetailLevel))
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
            ViewStylePack fallbackPack = null;
            if (!string.IsNullOrWhiteSpace(dt.ViewStylePackId))
            {
                fallbackPack = ResolvePackCached(doc, dt.ViewStylePackId);
            }
            int effectiveScale = dt.Scale;
            string effectiveDetailLevel = dt.DetailLevel;
            string effectiveTemplateName = dt.ViewTemplateName;
            bool scaleFromPack = false, detailFromPack = false, templateFromPack = false;
            if (fallbackPack != null && !fallbackPack.IsManaged)
            {
                if (effectiveScale <= 0 && !string.IsNullOrWhiteSpace(fallbackPack.ScaleHint)
                    && TryParseScaleHint(fallbackPack.ScaleHint, out var packScale))
                { effectiveScale = packScale; scaleFromPack = true; }
                if (string.IsNullOrWhiteSpace(effectiveDetailLevel) && !string.IsNullOrWhiteSpace(fallbackPack.DetailLevel))
                { effectiveDetailLevel = fallbackPack.DetailLevel; detailFromPack = true; }
                if (string.IsNullOrWhiteSpace(effectiveTemplateName) && !string.IsNullOrWhiteSpace(fallbackPack.ViewTemplate))
                { effectiveTemplateName = fallbackPack.ViewTemplate; templateFromPack = true; }
            }

            // Scale -------------------------------------------------------
            if (effectiveScale > 0)
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
            else
            {
                // 3D / perspective profiles legitimately ship without a fixed
                // scale; assigning view.Scale = 0 throws InvalidOperationException.
                StingTools.Core.StingLog.Info(
                    $"DrawingType '{dt.Id}' has scale <= 0; skipping view scale assignment.");
            }

            // Detail level -----------------------------------------------
            if (!string.IsNullOrWhiteSpace(effectiveDetailLevel))
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
            //   1. dt.ViewTemplateName — explicit user/corporate named template; applied if found.
            //   2. Managed pack template (STING:{packId}:{ViewType}) — applied if dt.ViewTemplateName
            //      is absent or not found in the project.
            // Rationale: named templates carry user customisations that should not be silently
            // discarded; managed templates are the fallback for new projects without existing templates.
            //
            // C-1: cached lookup; FilteredElementCollector<View> only runs
            // on first miss per (docKey, templateName).
            bool explicitTemplateApplied = false;
            if (!string.IsNullOrWhiteSpace(dt.ViewTemplateName))
            {
                try
                {
                    ElementId tplId = ResolveViewTemplate(doc, dt.ViewTemplateName);
                    if (tplId != null && tplId != ElementId.InvalidElementId)
                    {
                        view.ViewTemplateId = tplId;
                        r.TemplateApplied = true;
                        explicitTemplateApplied = true;
                    }
                    else
                    {
                        StingTools.Core.StingLog.Warn($"DrawingTypePresentation.Apply: viewTemplateName '{dt.ViewTemplateName}' not found in project — falling back to managed pack template.");
                        r.Warnings.Add($"View template '{dt.ViewTemplateName}' not found in project; falling back to managed pack template.");
                    }
                }
                catch (Exception ex)
                {
                    StingTools.Core.StingLog.Warn($"DrawingTypePresentation.Apply: could not apply view template '{dt.ViewTemplateName}' — {ex.Message}");
                    r.Warnings.Add($"ViewTemplate: {ex.Message}");
                }
            }

            // Crop strategy (bonus) -------------------------------------
            if (dt.Crop != null)
            {
                try
                {
                    var cropWarns = DrawingCropApplier.Apply(doc, view, dt);
                    r.Warnings.AddRange(cropWarns);
                    r.CropApplied = true;
                }
                catch (Exception ex) { r.Warnings.Add($"CropApplier: {ex.Message}"); }
            }

            // View Style Pack (shared graphic overrides) ---------------
            // Phase 137 — managed packs route through ManagedTemplateSyncer
            // which mints (or updates) a "STING:{packId}:{ViewType}"
            // template and assigns it to the view; non-managed packs apply
            // their VG / filter / etc. payload directly to the view.
            ViewStylePack resolvedPack = fallbackPack;
            if (!string.IsNullOrWhiteSpace(dt.ViewStylePackId))
            {
                try
                {
                    // C-2: cached lookup so batch producers resolve a given
                    // pack only once per session. Reuses the fallback resolution
                    // performed up-front when it succeeded.
                    if (resolvedPack == null)
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
                        if (templateId != ElementId.InvalidElementId)
                        {
                            r.ManagedTemplateId = templateId;
                            r.ManagedTemplateCreated = !r.TemplateApplied;
                            r.ManagedTemplateUpdated = true;
                            r.PackApplied = true;

                            // Only assign the managed template to the view when no explicit
                            // dt.ViewTemplateName was resolved — explicit template wins (PACK-1).
                            if (!explicitTemplateApplied)
                            {
                                try
                                {
                                    view.ViewTemplateId = templateId;
                                    r.TemplateApplied = true;
                                }
                                catch (Exception ex) { r.Warnings.Add($"Assign managed template: {ex.Message}"); }
                            }
                        }
                        else
                        {
                            r.Warnings.Add($"ViewStylePack '{dt.ViewStylePackId}' is managed but no template could be minted — falling back to external apply.");
                            var packStats = ViewStylePackApplier.Apply(doc, view, resolvedPack);
                            r.PackApplied = true;
                            r.Warnings.AddRange(packStats.Warnings);
                        }
                    }
                    else
                    {
                        var packStats = ViewStylePackApplier.Apply(doc, view, resolvedPack);
                        r.PackApplied = true;
                        r.Warnings.AddRange(packStats.Warnings);
                    }
                }
                catch (Exception ex) { r.Warnings.Add($"ViewStylePack: {ex.Message}"); }
            }
            else if (!string.IsNullOrWhiteSpace(dt.ViewStylePackId))
            {
                r.Warnings.Add($"ViewStylePack '{dt.ViewStylePackId}' not found.");
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
            if (dt.Annotation != null)
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
            // avoid surprising the user mid-Apply.
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

            return r;
        }
    }
}
