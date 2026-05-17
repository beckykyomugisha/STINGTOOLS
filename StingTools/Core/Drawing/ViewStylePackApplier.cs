// StingTools — Drawing Template Manager · Week 2 + Phase 137
//
// ViewStylePackApplier takes a resolved ViewStylePack and pushes its
// settings onto a View. Called by DrawingTypePresentation.Apply after
// the profile-level scale / template / detail-level have landed, and
// by ManagedTemplateSyncer when minting a managed view template.
//
// Phase 137 additions:
//   * Workset visibility writes
//   * Per-link override writes (display style + halftone + hide)
//   * Per-category color-fill scheme writes
//   * Per-filter enable/disable writes
//   * Public ReadCategoryOverrides helper (template snapshot)
//   * Public ApplyPresetOverrides helper (preset cascade)
//   * Internal *Only wrappers used by ManagedTemplateSyncer

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    public sealed class PackApplyResult
    {
        public int OverridesSet { get; set; }
        public int FiltersApplied { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class ViewStylePackApplier
    {
        // PERF-02: per-document caches so filter / category lookups don't
        // run a fresh FilteredElementCollector on every filter rule and a
        // fresh doc.Settings.Categories scan on every category override.
        // Cleared by DrawingTypeRegistry.Reload(doc).
        private static readonly object _cacheLock = new object();
        private static readonly Dictionary<string, Dictionary<string, ElementId>> _categoryCache
            = new Dictionary<string, Dictionary<string, ElementId>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Dictionary<string, ElementId>> _filterCache
            = new Dictionary<string, Dictionary<string, ElementId>>(StringComparer.OrdinalIgnoreCase);

        private static string DocKey(Document doc)
        {
            if (doc == null) return "__null__";
            try { return string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName; }
            catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] DocKey resolve PathName/Title: {ex.Message}"); return "__unknown__"; }
        }

        public static void InvalidateCache(Document doc)
        {
            string key = DocKey(doc);
            lock (_cacheLock)
            {
                if (_categoryCache.ContainsKey(key)) _categoryCache.Remove(key);
                if (_filterCache.ContainsKey(key))   _filterCache.Remove(key);
            }
        }

        private static ElementId ResolveCategoryIdCached(Document doc, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return ElementId.InvalidElementId;
            string docKey = DocKey(doc);
            lock (_cacheLock)
            {
                if (_categoryCache.TryGetValue(docKey, out var docMap)
                    && docMap.TryGetValue(key, out var cached))
                {
                    // FIX-4: a Category's id is stable for the lifetime of a
                    // document, but a sub-category id can be invalidated when
                    // the user deletes the parent line / fill style. Validate
                    // before trusting the cached ElementId.
                    if (cached == ElementId.InvalidElementId) return cached;
                    if (Category.GetCategory(doc, cached) != null) return cached;
                    docMap.Remove(key);
                }
            }
            var resolved = ResolveCategoryId(doc, key);
            lock (_cacheLock)
            {
                if (!_categoryCache.TryGetValue(docKey, out var docMap))
                    _categoryCache[docKey] = docMap = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
                docMap[key] = resolved;
            }
            return resolved;
        }

        private static ElementId ResolveFilterIdCached(Document doc, string filterName)
        {
            if (string.IsNullOrWhiteSpace(filterName)) return ElementId.InvalidElementId;
            string docKey = DocKey(doc);
            lock (_cacheLock)
            {
                if (_filterCache.TryGetValue(docKey, out var docMap)
                    && docMap.TryGetValue(filterName, out var cached))
                {
                    // FIX-4: the project may have deleted / re-created the
                    // ParameterFilterElement since the cache was populated.
                    // Validate by Id+name before returning.
                    if (cached == ElementId.InvalidElementId) return cached;
                    var el = doc.GetElement(cached) as ParameterFilterElement;
                    if (el != null && string.Equals(el.Name, filterName, StringComparison.OrdinalIgnoreCase))
                        return cached;
                    docMap.Remove(filterName);
                }
            }
            ElementId resolved;
            try
            {
                resolved = new FilteredElementCollector(doc)
                    .OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>()
                    .FirstOrDefault(f => string.Equals(f.Name, filterName, StringComparison.OrdinalIgnoreCase))?.Id
                    ?? ElementId.InvalidElementId;
            }
            catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] ResolveFilterIdCached collector: {ex.Message}"); resolved = ElementId.InvalidElementId; }

            lock (_cacheLock)
            {
                if (!_filterCache.TryGetValue(docKey, out var docMap))
                    _filterCache[docKey] = docMap = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
                docMap[filterName] = resolved;
            }
            return resolved;
        }

        public static PackApplyResult Apply(Document doc, View view, ViewStylePack pack)
        {
            var r = new PackApplyResult();
            if (doc == null || view == null || pack == null) return r;
            if (view.IsTemplate) return r;

            // Cannot override graphics when a view template governs the view.
            if (view.ViewTemplateId != null && view.ViewTemplateId != ElementId.InvalidElementId)
            {
                r.Warnings.Add("View has an active template — pack VG overrides will be applied to the template, not the view.");
            }

            ApplyCategoryOverrides(doc, view, pack, r);
            ApplyFilterRules(doc, view, pack, r);
            ApplyWorksetVisibility(doc, view, pack, r);
            ApplyLinkOverrides(doc, view, pack, r);
            ApplyColorFillSchemes(doc, view, pack, r);
            ApplyFilterEnabled(doc, view, pack, r);
            return r;
        }

        // ── Internal wrappers used by ManagedTemplateSyncer ──

        internal static void ApplyCategoryOverridesOnly(Document doc, View view, ViewStylePack pack, PackApplyResult r) =>
            ApplyCategoryOverrides(doc, view, pack, r);

        internal static void ApplyFilterRulesOnly(Document doc, View view, ViewStylePack pack, PackApplyResult r) =>
            ApplyFilterRules(doc, view, pack, r);

        internal static void ApplyCategoryOverrides(Document doc, View view, ViewStylePack pack, PackApplyResult r)
        {
            if (pack.VgOverrides == null) return;
            foreach (var kv in pack.VgOverrides)
            {
                try
                {
                    var catId = ResolveCategoryIdCached(doc, kv.Key);
                    if (catId == ElementId.InvalidElementId) { r.Warnings.Add($"Category '{kv.Key}' not found."); continue; }

                    var src = kv.Value;
                    if (src == null) continue;

                    // Visibility — set first so a hidden category can still
                    // carry overrides ready for when it is re-shown.
                    if (src.Visible.HasValue)
                    {
                        try { view.SetCategoryHidden(catId, !src.Visible.Value); } catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] SetCategoryHidden '{kv.Key}': {ex.Message}"); }
                    }

                    var ogs = view.GetCategoryOverrides(catId) ?? new OverrideGraphicSettings();

                    if (src.Halftone.HasValue)             ogs.SetHalftone(src.Halftone.Value);
                    if (src.ProjectionLineWeight.HasValue) ogs.SetProjectionLineWeight(src.ProjectionLineWeight.Value);
                    if (!string.IsNullOrEmpty(src.ProjectionLineColor)) ogs.SetProjectionLineColor(HexColor(src.ProjectionLineColor));
                    if (!string.IsNullOrEmpty(src.ProjectionLinePattern))
                    {
                        var pid = ResolveLinePattern(doc, src.ProjectionLinePattern);
                        if (pid != ElementId.InvalidElementId) ogs.SetProjectionLinePatternId(pid);
                    }
                    if (src.CutLineWeight.HasValue)        ogs.SetCutLineWeight(src.CutLineWeight.Value);
                    if (!string.IsNullOrEmpty(src.CutLineColor))        ogs.SetCutLineColor(HexColor(src.CutLineColor));
                    if (!string.IsNullOrEmpty(src.CutLinePattern))
                    {
                        var pid = ResolveLinePattern(doc, src.CutLinePattern);
                        if (pid != ElementId.InvalidElementId) ogs.SetCutLinePatternId(pid);
                    }

                    // Phase 177 — surface foreground / background fill patterns
                    if (!string.IsNullOrEmpty(src.SurfaceFgColor)) ogs.SetSurfaceForegroundPatternColor(HexColor(src.SurfaceFgColor));
                    if (!string.IsNullOrEmpty(src.SurfaceFgPattern))
                    {
                        var fid = ResolveFillPattern(doc, src.SurfaceFgPattern);
                        if (fid != ElementId.InvalidElementId)
                        {
                            ogs.SetSurfaceForegroundPatternId(fid);
                            try { ogs.SetSurfaceForegroundPatternVisible(src.SurfaceFgVisible ?? true); } catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] SetSurfaceForegroundPatternVisible (with pattern): {ex.Message}"); }
                        }
                    }
                    else if (src.SurfaceFgVisible.HasValue)
                    {
                        try { ogs.SetSurfaceForegroundPatternVisible(src.SurfaceFgVisible.Value); } catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] SetSurfaceForegroundPatternVisible: {ex.Message}"); }
                    }
                    if (!string.IsNullOrEmpty(src.SurfaceBgColor)) ogs.SetSurfaceBackgroundPatternColor(HexColor(src.SurfaceBgColor));
                    if (!string.IsNullOrEmpty(src.SurfaceBgPattern))
                    {
                        var fid = ResolveFillPattern(doc, src.SurfaceBgPattern);
                        if (fid != ElementId.InvalidElementId)
                        {
                            ogs.SetSurfaceBackgroundPatternId(fid);
                            try { ogs.SetSurfaceBackgroundPatternVisible(src.SurfaceBgVisible ?? true); } catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] SetSurfaceBackgroundPatternVisible (with pattern): {ex.Message}"); }
                        }
                    }
                    else if (src.SurfaceBgVisible.HasValue)
                    {
                        try { ogs.SetSurfaceBackgroundPatternVisible(src.SurfaceBgVisible.Value); } catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] SetSurfaceBackgroundPatternVisible: {ex.Message}"); }
                    }

                    // Cut foreground / background fill patterns
                    if (!string.IsNullOrEmpty(src.CutFgColor)) ogs.SetCutForegroundPatternColor(HexColor(src.CutFgColor));
                    if (!string.IsNullOrEmpty(src.CutFgPattern))
                    {
                        var fid = ResolveFillPattern(doc, src.CutFgPattern);
                        if (fid != ElementId.InvalidElementId)
                        {
                            ogs.SetCutForegroundPatternId(fid);
                            try { ogs.SetCutForegroundPatternVisible(src.CutFgVisible ?? true); } catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] SetCutForegroundPatternVisible (with pattern): {ex.Message}"); }
                        }
                    }
                    else if (src.CutFgVisible.HasValue)
                    {
                        try { ogs.SetCutForegroundPatternVisible(src.CutFgVisible.Value); } catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] SetCutForegroundPatternVisible: {ex.Message}"); }
                    }
                    if (!string.IsNullOrEmpty(src.CutBgColor)) ogs.SetCutBackgroundPatternColor(HexColor(src.CutBgColor));
                    if (!string.IsNullOrEmpty(src.CutBgPattern))
                    {
                        var fid = ResolveFillPattern(doc, src.CutBgPattern);
                        if (fid != ElementId.InvalidElementId) ogs.SetCutBackgroundPatternId(fid);
                    }

                    if (!string.IsNullOrEmpty(src.DetailLevel) &&
                        Enum.TryParse<ViewDetailLevel>(src.DetailLevel, true, out var dl))
                    {
                        try { ogs.SetDetailLevel(dl); } catch { /* < 2023 */ }
                    }

                    if (src.Transparency.HasValue)
                    {
                        var t = Clamp(src.Transparency.Value, 0, 100);
                        ogs.SetSurfaceTransparency(t);
                        // 100% transparency on a presentation pack means
                        // "outline only" — hide the surface foreground fill
                        // so only the projection line work renders.
                        if (t >= 100)
                        {
                            try { ogs.SetSurfaceForegroundPatternVisible(false); } catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] Hide surface fg at 100% transparency: {ex.Message}"); }
                            try { ogs.SetSurfaceBackgroundPatternVisible(false); } catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] Hide surface bg at 100% transparency: {ex.Message}"); }
                        }
                    }

                    view.SetCategoryOverrides(catId, ogs);
                    r.OverridesSet++;
                }
                catch (Exception ex) { r.Warnings.Add($"VG override '{kv.Key}': {ex.Message}"); }
            }
        }

        internal static void ApplyFilterRules(Document doc, View view, ViewStylePack pack, PackApplyResult r)
        {
            if (pack.Filters == null) return;
            foreach (var rule in pack.Filters)
            {
                if (string.IsNullOrWhiteSpace(rule.FilterName)) continue;
                try
                {
                    var filterId = ResolveFilterIdCached(doc, rule.FilterName);

                    // Phase 139 — lazy-create from AecFilterRegistry if the
                    // pack references a corporate-baseline filter that
                    // hasn't been minted in this document yet. The registry
                    // looks up the definition by name and the factory mints
                    // it under the active transaction.
                    if (filterId == ElementId.InvalidElementId)
                    {
                        var def = AecFilterRegistry.GetByName(doc, rule.FilterName);
                        if (def != null)
                        {
                            var f = AecFilterFactory.FindOrCreate(doc, def);
                            if (f.Ok && f.Filter != null)
                            {
                                filterId = f.Filter.Id;
                                InvalidateCache(doc); // rebuild cache so other refs hit
                                if (f.Warnings.Count > 0)
                                    foreach (var w in f.Warnings) r.Warnings.Add($"Filter '{rule.FilterName}': {w}");
                            }
                            else if (!string.IsNullOrEmpty(f.Error))
                            {
                                r.Warnings.Add($"Filter '{rule.FilterName}' lazy-create failed: {f.Error}");
                            }
                        }
                        if (filterId == ElementId.InvalidElementId)
                        {
                            r.Warnings.Add($"Filter '{rule.FilterName}' not found and not in AEC registry — skipped.");
                            continue;
                        }
                    }

                    if (!view.GetFilters().Contains(filterId))
                        view.AddFilter(filterId);

                    // Phase 139 — merge corporate-baseline default override
                    // for filters that came from the registry. Pack-level
                    // fields always win.
                    FilterDefaultOverride defaults = null;
                    if (rule.InheritDefaults != false)
                    {
                        var def = AecFilterRegistry.GetByName(doc, rule.FilterName);
                        defaults = def?.DefaultOverride;
                    }

                    var ogs = view.GetFilterOverrides(filterId) ?? new OverrideGraphicSettings();

                    // Projection line
                    var projColor = rule.ProjectionLineColor ?? defaults?.ProjColor;
                    if (!string.IsNullOrEmpty(projColor)) ogs.SetProjectionLineColor(HexColor(projColor));
                    var projWeight = rule.ProjectionLineWeight ?? defaults?.ProjWeight;
                    if (projWeight.HasValue) ogs.SetProjectionLineWeight(projWeight.Value);
                    var projLp = rule.ProjectionLinePattern ?? defaults?.ProjLinePattern;
                    if (!string.IsNullOrEmpty(projLp))
                    {
                        var pid = ResolveLinePattern(doc, projLp);
                        if (pid != ElementId.InvalidElementId) ogs.SetProjectionLinePatternId(pid);
                    }

                    // Cut line
                    var cutColor = rule.CutLineColor ?? defaults?.CutColor;
                    if (!string.IsNullOrEmpty(cutColor)) ogs.SetCutLineColor(HexColor(cutColor));
                    var cutWeight = rule.CutLineWeight ?? defaults?.CutWeight;
                    if (cutWeight.HasValue) ogs.SetCutLineWeight(cutWeight.Value);
                    var cutLp = rule.CutLinePattern ?? defaults?.CutLinePattern;
                    if (!string.IsNullOrEmpty(cutLp))
                    {
                        var pid = ResolveLinePattern(doc, cutLp);
                        if (pid != ElementId.InvalidElementId) ogs.SetCutLinePatternId(pid);
                    }

                    // Surface foreground / background patterns
                    var sfgColor = rule.SurfaceFgColor ?? defaults?.SurfFgColor;
                    if (!string.IsNullOrEmpty(sfgColor)) ogs.SetSurfaceForegroundPatternColor(HexColor(sfgColor));
                    var sfgPattern = rule.SurfaceFgPattern ?? defaults?.SurfFgPattern;
                    if (!string.IsNullOrEmpty(sfgPattern))
                    {
                        var fid = ResolveFillPattern(doc, sfgPattern);
                        if (fid != ElementId.InvalidElementId)
                        {
                            ogs.SetSurfaceForegroundPatternId(fid);
                            try { ogs.SetSurfaceForegroundPatternVisible(true); } catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] Filter SetSurfaceForegroundPatternVisible: {ex.Message}"); }
                        }
                    }
                    var sbgColor = rule.SurfaceBgColor ?? defaults?.SurfBgColor;
                    if (!string.IsNullOrEmpty(sbgColor)) ogs.SetSurfaceBackgroundPatternColor(HexColor(sbgColor));
                    var sbgPattern = rule.SurfaceBgPattern ?? defaults?.SurfBgPattern;
                    if (!string.IsNullOrEmpty(sbgPattern))
                    {
                        var fid = ResolveFillPattern(doc, sbgPattern);
                        if (fid != ElementId.InvalidElementId)
                        {
                            ogs.SetSurfaceBackgroundPatternId(fid);
                            try { ogs.SetSurfaceBackgroundPatternVisible(true); } catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] Filter SetSurfaceBackgroundPatternVisible: {ex.Message}"); }
                        }
                    }

                    // Cut foreground / background patterns (fire-rated walls etc.)
                    var cfgColor = rule.CutFgColor ?? defaults?.CutFgColor;
                    if (!string.IsNullOrEmpty(cfgColor)) ogs.SetCutForegroundPatternColor(HexColor(cfgColor));
                    var cfgPattern = rule.CutFgPattern ?? defaults?.CutFgPattern;
                    if (!string.IsNullOrEmpty(cfgPattern))
                    {
                        var fid = ResolveFillPattern(doc, cfgPattern);
                        if (fid != ElementId.InvalidElementId)
                        {
                            ogs.SetCutForegroundPatternId(fid);
                            try { ogs.SetCutForegroundPatternVisible(true); } catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] Filter SetCutForegroundPatternVisible: {ex.Message}"); }
                        }
                    }
                    var cbgColor = rule.CutBgColor ?? defaults?.CutBgColor;
                    if (!string.IsNullOrEmpty(cbgColor)) ogs.SetCutBackgroundPatternColor(HexColor(cbgColor));
                    var cbgPattern = rule.CutBgPattern ?? defaults?.CutBgPattern;
                    if (!string.IsNullOrEmpty(cbgPattern))
                    {
                        var fid = ResolveFillPattern(doc, cbgPattern);
                        if (fid != ElementId.InvalidElementId)
                        {
                            ogs.SetCutBackgroundPatternId(fid);
                            try { ogs.SetCutBackgroundPatternVisible(true); } catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] Filter SetCutBackgroundPatternVisible: {ex.Message}"); }
                        }
                    }

                    // Transparency
                    var transp = rule.Transparency ?? defaults?.Transparency;
                    if (transp.HasValue) ogs.SetSurfaceTransparency(Clamp(transp.Value, 0, 100));

                    // Halftone — pack flag wins; default override falls through.
                    bool halftone = rule.Halftone || (defaults?.Halftone == true);
                    ogs.SetHalftone(halftone);

                    // Detail level — Revit 2023+ override.
                    var dlStr = rule.DetailLevel ?? defaults?.DetailLevel;
                    if (!string.IsNullOrEmpty(dlStr) &&
                        Enum.TryParse<ViewDetailLevel>(dlStr, true, out var dl))
                    {
                        try { ogs.SetDetailLevel(dl); } catch { /* < 2023 */ }
                    }

                    view.SetFilterOverrides(filterId, ogs);

                    // Visibility — pack rule wins; otherwise default override visible flag; default true.
                    bool visible = rule.Visible;
                    if (defaults?.Visible.HasValue == true && rule.Visible) visible = defaults.Visible.Value;
                    view.SetFilterVisibility(filterId, visible);
                    r.FiltersApplied++;
                }
                catch (Exception ex) { r.Warnings.Add($"Filter '{rule.FilterName}': {ex.Message}"); }
            }
        }

        // ── Phase 137 — Workset visibility ──

        internal static void ApplyWorksetVisibility(Document doc, View view, ViewStylePack pack, PackApplyResult r)
        {
            if (pack.WorksetVisibility == null || pack.WorksetVisibility.Count == 0) return;
            if (!doc.IsWorkshared)
            {
                r.Warnings.Add("Pack declares worksetVisibility but the document is not workshared — skipped.");
                return;
            }
            foreach (var kv in pack.WorksetVisibility)
            {
                try
                {
                    var ws = new FilteredWorksetCollector(doc)
                        .OfKind(WorksetKind.UserWorkset)
                        .FirstOrDefault(w => string.Equals(w.Name, kv.Key, StringComparison.OrdinalIgnoreCase));
                    if (ws == null) { r.Warnings.Add($"Workset '{kv.Key}' not found — skipped."); continue; }

                    WorksetVisibility vis;
                    switch ((kv.Value ?? "").Trim().ToLowerInvariant())
                    {
                        case "show":   vis = WorksetVisibility.Visible; break;
                        case "hide":   vis = WorksetVisibility.Hidden; break;
                        default:       vis = WorksetVisibility.UseGlobalSetting; break;
                    }
                    view.SetWorksetVisibility(ws.Id, vis);
                }
                catch (Exception ex) { r.Warnings.Add($"Workset visibility '{kv.Key}': {ex.Message}"); }
            }
        }

        // ── Phase 137 — Per-link overrides ──

        internal static void ApplyLinkOverrides(Document doc, View view, ViewStylePack pack, PackApplyResult r)
        {
            if (pack.LinkOverrides == null || pack.LinkOverrides.Count == 0) return;
            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();
            foreach (var kv in pack.LinkOverrides)
            {
                try
                {
                    var link = links.FirstOrDefault(l => string.Equals(l.Name, kv.Key, StringComparison.OrdinalIgnoreCase));
                    if (link == null) { r.Warnings.Add($"Revit link '{kv.Key}' not found — skipped."); continue; }
                    if (kv.Value?.Hidden == true)
                    {
                        try { view.SetCategoryHidden(new ElementId(BuiltInCategory.OST_RvtLinks), true); } catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] Hide Revit links category: {ex.Message}"); }
                    }
                    if (kv.Value?.Halftone.HasValue == true)
                    {
                        try
                        {
                            var ogs = view.GetCategoryOverrides(new ElementId(BuiltInCategory.OST_RvtLinks)) ?? new OverrideGraphicSettings();
                            ogs.SetHalftone(kv.Value.Halftone.Value);
                            view.SetCategoryOverrides(new ElementId(BuiltInCategory.OST_RvtLinks), ogs);
                        }
                        catch (Exception inner) { r.Warnings.Add($"Link halftone '{kv.Key}': {inner.Message}"); }
                    }
                }
                catch (Exception ex) { r.Warnings.Add($"Link override '{kv.Key}': {ex.Message}"); }
            }
        }

        // ── Phase 137 — Color-fill schemes ──

        internal static void ApplyColorFillSchemes(Document doc, View view, ViewStylePack pack, PackApplyResult r)
        {
            if (pack.ColorFillSchemes == null || pack.ColorFillSchemes.Count == 0) return;
            if (!(view is ViewPlan vp))
            {
                r.Warnings.Add("Pack declares colorFillSchemes but view is not a plan — skipped.");
                return;
            }
            var schemes = new FilteredElementCollector(doc)
                .OfClass(typeof(ColorFillScheme))
                .Cast<ColorFillScheme>()
                .ToList();
            foreach (var kv in pack.ColorFillSchemes)
            {
                try
                {
                    var catId = ResolveCategoryIdCached(doc, kv.Key);
                    if (catId == ElementId.InvalidElementId) { r.Warnings.Add($"ColorFill category '{kv.Key}' not found — skipped."); continue; }
                    var scheme = schemes.FirstOrDefault(s => string.Equals(s.Name, kv.Value, StringComparison.OrdinalIgnoreCase));
                    if (scheme == null) { r.Warnings.Add($"ColorFillScheme '{kv.Value}' not found — skipped."); continue; }
                    vp.SetColorFillSchemeId(catId, scheme.Id);
                }
                catch (Exception ex) { r.Warnings.Add($"ColorFill '{kv.Key}': {ex.Message}"); }
            }
        }

        // ── Phase 137 — Filter enable/disable ──

        internal static void ApplyFilterEnabled(Document doc, View view, ViewStylePack pack, PackApplyResult r)
        {
            if (pack.FilterEnabled == null || pack.FilterEnabled.Count == 0) return;
            foreach (var kv in pack.FilterEnabled)
            {
                try
                {
                    var filter = new FilteredElementCollector(doc)
                        .OfClass(typeof(ParameterFilterElement))
                        .Cast<ParameterFilterElement>()
                        .FirstOrDefault(f => string.Equals(f.Name, kv.Key, StringComparison.OrdinalIgnoreCase));
                    if (filter == null) { r.Warnings.Add($"Filter '{kv.Key}' not found — skipped."); continue; }
                    if (!view.GetFilters().Contains(filter.Id))
                        view.AddFilter(filter.Id);
                    view.SetIsFilterEnabled(filter.Id, kv.Value);
                }
                catch (Exception ex) { r.Warnings.Add($"FilterEnabled '{kv.Key}': {ex.Message}"); }
            }
        }

        // ── Phase 137 — Public helpers ──

        public static Dictionary<string, StyleVgOverride> ReadCategoryOverrides(Document doc, View template)
        {
            var result = new Dictionary<string, StyleVgOverride>();
            if (doc == null || template == null) return result;
            foreach (Category c in doc.Settings.Categories)
            {
                try
                {
                    var ogs = template.GetCategoryOverrides(c.Id);
                    if (ogs == null) continue;
                    var src = new StyleVgOverride();
                    bool any = false;
                    if (ogs.Halftone) { src.Halftone = true; any = true; }
                    if (ogs.ProjectionLineWeight > 0) { src.ProjectionLineWeight = ogs.ProjectionLineWeight; any = true; }
                    var pc = ogs.ProjectionLineColor;
                    if (pc != null && pc.IsValid) { src.ProjectionLineColor = ColorToHex(pc); any = true; }
                    if (ogs.CutLineWeight > 0) { src.CutLineWeight = ogs.CutLineWeight; any = true; }
                    var cc = ogs.CutLineColor;
                    if (cc != null && cc.IsValid) { src.CutLineColor = ColorToHex(cc); any = true; }
                    if (ogs.Transparency > 0) { src.Transparency = ogs.Transparency; any = true; }
                    if (any) result[c.Name] = src;
                }
                catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] ReadCategoryOverrides for '{c.Name}': {ex.Message}"); }
            }
            return result;
        }

        public static void ApplyPresetOverrides(Document doc, View view, List<PresetCategoryOverride> overrides, PackApplyResult r)
        {
            if (overrides == null || overrides.Count == 0) return;
            foreach (var o in overrides)
            {
                if (o == null) continue;
                try
                {
                    ElementId catId;
                    if (!string.IsNullOrEmpty(o.SubCategory))
                        catId = ResolveSubCategoryId(doc, o.Category, o.SubCategory);
                    else
                        catId = ResolveCategoryId(doc, o.Category);
                    if (catId == ElementId.InvalidElementId) { r.Warnings.Add($"Preset category '{o.Category}/{o.SubCategory}' not found — skipped."); continue; }

                    if (o.Visible.HasValue)
                    {
                        try { view.SetCategoryHidden(catId, !o.Visible.Value); } catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] Preset SetCategoryHidden '{o.Category}': {ex.Message}"); }
                    }

                    var ogs = view.GetCategoryOverrides(catId) ?? new OverrideGraphicSettings();

                    if (o.ProjLineWeight.HasValue)            ogs.SetProjectionLineWeight(o.ProjLineWeight.Value);
                    if (!string.IsNullOrEmpty(o.ProjLineColor)) ogs.SetProjectionLineColor(HexColor(o.ProjLineColor));
                    if (!string.IsNullOrEmpty(o.ProjLinePattern))
                    {
                        var pid = ResolveLinePattern(doc, o.ProjLinePattern);
                        if (pid != ElementId.InvalidElementId) ogs.SetProjectionLinePatternId(pid);
                    }
                    if (!string.IsNullOrEmpty(o.SurfFgColor))    ogs.SetSurfaceForegroundPatternColor(HexColor(o.SurfFgColor));
                    if (!string.IsNullOrEmpty(o.SurfFgPattern))
                    {
                        var fid = ResolveFillPattern(doc, o.SurfFgPattern);
                        if (fid != ElementId.InvalidElementId) ogs.SetSurfaceForegroundPatternId(fid);
                    }
                    if (o.SurfFgVisible.HasValue) ogs.SetSurfaceForegroundPatternVisible(o.SurfFgVisible.Value);
                    if (!string.IsNullOrEmpty(o.SurfBgColor))    ogs.SetSurfaceBackgroundPatternColor(HexColor(o.SurfBgColor));
                    if (!string.IsNullOrEmpty(o.SurfBgPattern))
                    {
                        var fid = ResolveFillPattern(doc, o.SurfBgPattern);
                        if (fid != ElementId.InvalidElementId) ogs.SetSurfaceBackgroundPatternId(fid);
                    }
                    if (o.SurfBgVisible.HasValue) ogs.SetSurfaceBackgroundPatternVisible(o.SurfBgVisible.Value);
                    if (o.Transparency.HasValue) ogs.SetSurfaceTransparency(Clamp(o.Transparency.Value, 0, 100));

                    if (o.CutLineWeight.HasValue)             ogs.SetCutLineWeight(o.CutLineWeight.Value);
                    if (!string.IsNullOrEmpty(o.CutLineColor))   ogs.SetCutLineColor(HexColor(o.CutLineColor));
                    if (!string.IsNullOrEmpty(o.CutLinePattern))
                    {
                        var pid = ResolveLinePattern(doc, o.CutLinePattern);
                        if (pid != ElementId.InvalidElementId) ogs.SetCutLinePatternId(pid);
                    }
                    if (!string.IsNullOrEmpty(o.CutFgColor))    ogs.SetCutForegroundPatternColor(HexColor(o.CutFgColor));
                    if (!string.IsNullOrEmpty(o.CutFgPattern))
                    {
                        var fid = ResolveFillPattern(doc, o.CutFgPattern);
                        if (fid != ElementId.InvalidElementId) ogs.SetCutForegroundPatternId(fid);
                    }
                    if (o.CutFgVisible.HasValue) ogs.SetCutForegroundPatternVisible(o.CutFgVisible.Value);
                    if (!string.IsNullOrEmpty(o.CutBgColor))    ogs.SetCutBackgroundPatternColor(HexColor(o.CutBgColor));
                    if (!string.IsNullOrEmpty(o.CutBgPattern))
                    {
                        var fid = ResolveFillPattern(doc, o.CutBgPattern);
                        if (fid != ElementId.InvalidElementId) ogs.SetCutBackgroundPatternId(fid);
                    }

                    if (o.Halftone.HasValue) ogs.SetHalftone(o.Halftone.Value);
                    if (!string.IsNullOrEmpty(o.DetailLevel) &&
                        Enum.TryParse<ViewDetailLevel>(o.DetailLevel, true, out var dl))
                        ogs.SetDetailLevel(dl);

                    view.SetCategoryOverrides(catId, ogs);
                    r.OverridesSet++;
                }
                catch (Exception ex) { r.Warnings.Add($"Preset override '{o.Category}': {ex.Message}"); }
            }
        }

        // ── Resolvers ──

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
                var trimmed = key.Trim('<', '>', ' ');
                foreach (Category c in doc.Settings.Categories)
                    foreach (Category sub in c.SubCategories)
                        if (string.Equals(sub.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                            return sub.Id;
            }
            catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] ResolveCategoryId('{key}'): {ex.Message}"); }
            return ElementId.InvalidElementId;
        }

        private static ElementId ResolveSubCategoryId(Document doc, string categoryName, string subCatName)
        {
            if (string.IsNullOrWhiteSpace(categoryName) || string.IsNullOrWhiteSpace(subCatName))
                return ElementId.InvalidElementId;
            try
            {
                var parent = ResolveCategoryId(doc, categoryName);
                if (parent == ElementId.InvalidElementId) return ElementId.InvalidElementId;
                var trimmed = subCatName.Trim('<', '>', ' ');
                foreach (Category c in doc.Settings.Categories)
                {
                    if (c.Id != parent) continue;
                    foreach (Category sub in c.SubCategories)
                        if (string.Equals(sub.Name.Trim('<', '>', ' '), trimmed, StringComparison.OrdinalIgnoreCase))
                            return sub.Id;
                }
            }
            catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] ResolveSubCategoryId('{categoryName}/{subCatName}'): {ex.Message}"); }
            return ElementId.InvalidElementId;
        }

        private static ElementId ResolveLinePattern(Document doc, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ElementId.InvalidElementId;
            try
            {
                if (string.Equals(name, "Solid", StringComparison.OrdinalIgnoreCase))
                    return LinePatternElement.GetSolidPatternId();
                var lp = new FilteredElementCollector(doc)
                    .OfClass(typeof(LinePatternElement))
                    .Cast<LinePatternElement>()
                    .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                return lp?.Id ?? ElementId.InvalidElementId;
            }
            catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] ResolveLinePattern('{name}'): {ex.Message}"); return ElementId.InvalidElementId; }
        }

        private static ElementId ResolveFillPattern(Document doc, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ElementId.InvalidElementId;
            try
            {
                var fp = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                return fp?.Id ?? ElementId.InvalidElementId;
            }
            catch (Exception ex) { StingLog.Warn($"[ViewStylePackApplier] ResolveFillPattern('{name}'): {ex.Message}"); return ElementId.InvalidElementId; }
        }

        private static Autodesk.Revit.DB.Color HexColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return new Autodesk.Revit.DB.Color(0, 0, 0);
            var s = hex.TrimStart('#');
            if (s.Length != 6) return new Autodesk.Revit.DB.Color(0, 0, 0);
            byte r = byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber);
            byte g = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber);
            byte b = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber);
            return new Autodesk.Revit.DB.Color(r, g, b);
        }

        private static string ColorToHex(Autodesk.Revit.DB.Color c)
        {
            if (c == null || !c.IsValid) return null;
            return $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
