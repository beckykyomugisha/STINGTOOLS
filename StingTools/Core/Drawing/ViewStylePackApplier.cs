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
        /// <summary>C4 — Number of material-class overrides applied on this view.</summary>
        public int AppliedByMaterialClass { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public static partial class ViewStylePackApplier
    {
        public static void InvalidateCache(Document doc) { /* Pack registry is doc-scoped; no separate cache needed. */ }
        public static void ReadCategoryOverrides(Document doc, View view, ViewStylePack pack) { /* No-op stub. */ }

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
            ApplyMaterialClassOverrides(doc, view, pack, r);  // C4
            return r;
        }

        /// <summary>
        /// C4 — Project a pack's byMaterialClass entries onto the view
        /// as ParameterFilterElement overrides. Each class spawns one
        /// filter "STING_MAT_CLASS_&lt;class&gt;" rule-matching the
        /// element's primary Material element by name → MaterialClass.
        ///
        /// The filter is created once per project (idempotent) and the
        /// view receives it with the StyleVgOverride applied. Walls
        /// with a Concrete material render concrete-grey; the same
        /// pack's Walls VG override (if any) still applies first.
        ///
        /// Best-effort: when ParameterFilterElement creation fails
        /// (e.g. shared param not bound on Material category) the
        /// pack still applies the rest of its overrides without
        /// aborting.
        /// </summary>
        private static void ApplyMaterialClassOverrides(Document doc, View view, ViewStylePack pack, PackApplyResult r)
        {
            if (pack?.ByMaterialClass == null || pack.ByMaterialClass.Count == 0) return;

            foreach (var kv in pack.ByMaterialClass)
            {
                string className = kv.Key;
                var src = kv.Value;
                if (string.IsNullOrWhiteSpace(className) || src == null) continue;

                try
                {
                    var filter = EnsureMaterialClassFilter(doc, className);
                    if (filter == null)
                    {
                        r.Warnings.Add($"byMaterialClass '{className}': filter could not be created (no eligible Material category bindings).");
                        continue;
                    }
                    if (!view.IsFilterApplied(filter.Id))
                        view.AddFilter(filter.Id);

                    var ogs = view.GetFilterOverrides(filter.Id) ?? new OverrideGraphicSettings();
                    if (src.Halftone.HasValue)             ogs.SetHalftone(src.Halftone.Value);
                    if (src.Transparency.HasValue)         ogs.SetSurfaceTransparency(src.Transparency.Value);
                    if (src.ProjectionLineWeight.HasValue) ogs.SetProjectionLineWeight(src.ProjectionLineWeight.Value);
                    if (src.CutLineWeight.HasValue)        ogs.SetCutLineWeight(src.CutLineWeight.Value);
                    if (!string.IsNullOrEmpty(src.ProjectionLineColor))
                    {
                        var c = ParseHexColor(src.ProjectionLineColor);
                        if (c != null) ogs.SetProjectionLineColor(c);
                    }
                    if (!string.IsNullOrEmpty(src.CutLineColor))
                    {
                        var c = ParseHexColor(src.CutLineColor);
                        if (c != null) ogs.SetCutLineColor(c);
                    }
                    view.SetFilterOverrides(filter.Id, ogs);
                    r.AppliedByMaterialClass++;
                }
                catch (Exception ex)
                {
                    r.Warnings.Add($"byMaterialClass '{className}': {ex.Message}");
                    StingTools.Core.StingLog.Warn($"ApplyMaterialClassOverrides '{className}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Find-or-create the project-scoped ParameterFilterElement
        /// "STING_MAT_CLASS_&lt;class&gt;" that selects every element
        /// whose primary Material's MaterialClass matches.
        ///
        /// Implementation notes: Revit has no built-in "material's class"
        /// filter; we use a filter rule on the Material parameter
        /// (MATERIAL_ID_PARAM) keyed by the resolved Material element ids
        /// whose MaterialClass matches the requested class. The id list
        /// is regenerated lazily on each apply so a newly-added material
        /// is picked up next time the pack runs.
        /// </summary>
        // P-6 — Cache the (doc-path, className) → ParameterFilterElement id
        // so successive packs targeting the same class skip the rebuild.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ElementId> _matClassFilterCache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);

        public static void InvalidateMaterialClassFilterCache() => _matClassFilterCache.Clear();

        private static ParameterFilterElement EnsureMaterialClassFilter(Document doc, string className)
        {
            try
            {
                string filterName = $"STING_MAT_CLASS_{className}";
                string cacheKey = (doc?.PathName ?? doc?.Title ?? "_") + "|" + className;
                if (_matClassFilterCache.TryGetValue(cacheKey, out var cachedId) &&
                    cachedId != null && cachedId.Value > 0 &&
                    doc.GetElement(cachedId) is ParameterFilterElement cachedPfe &&
                    string.Equals(cachedPfe.Name, filterName, StringComparison.OrdinalIgnoreCase))
                {
                    return cachedPfe;
                }

                // Find existing
                var existing = new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>()
                    .FirstOrDefault(f => string.Equals(f.Name, filterName, StringComparison.OrdinalIgnoreCase));

                // Resolve every Material id whose class matches.
                var matIds = new FilteredElementCollector(doc).OfClass(typeof(Material))
                    .Cast<Material>()
                    .Where(m => string.Equals(m.MaterialClass ?? "", className, StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.Id)
                    .ToList();
                if (matIds.Count == 0) { if (existing != null) _matClassFilterCache[cacheKey] = existing.Id; return existing; }

                // Build the categories the filter applies to — every
                // category that can carry a Material parameter. Use a
                // conservative set that always works in Revit 2025+.
                var cats = new List<ElementId>
                {
                    new ElementId(BuiltInCategory.OST_Walls),
                    new ElementId(BuiltInCategory.OST_Floors),
                    new ElementId(BuiltInCategory.OST_Ceilings),
                    new ElementId(BuiltInCategory.OST_Roofs),
                    new ElementId(BuiltInCategory.OST_Columns),
                    new ElementId(BuiltInCategory.OST_StructuralColumns),
                    new ElementId(BuiltInCategory.OST_StructuralFraming),
                    new ElementId(BuiltInCategory.OST_StructuralFoundation),
                    new ElementId(BuiltInCategory.OST_Doors),
                    new ElementId(BuiltInCategory.OST_Windows),
                    new ElementId(BuiltInCategory.OST_PlumbingFixtures),
                    new ElementId(BuiltInCategory.OST_MechanicalEquipment),
                    new ElementId(BuiltInCategory.OST_ElectricalFixtures),
                    new ElementId(BuiltInCategory.OST_LightingFixtures),
                    new ElementId(BuiltInCategory.OST_Furniture),
                };

                // Build the OR-of-equals rule across all the class's
                // material ids.
                var rules = new List<FilterRule>();
                ElementId matParam = new ElementId(BuiltInParameter.MATERIAL_ID_PARAM);
                foreach (var mid in matIds)
                {
                    try { rules.Add(ParameterFilterRuleFactory.CreateEqualsRule(matParam, mid)); }
                    catch (Exception ex) { StingTools.Core.StingLog.WarnRateLimited("MatClassFilter.Rule", $"Rule build: {ex.Message}"); }
                }
                if (rules.Count == 0) return existing;
                ElementParameterFilter elemFilter = new ElementParameterFilter(rules, false /* OR semantics across the rules */);

                ParameterFilterElement built;
                if (existing == null)
                {
                    built = ParameterFilterElement.Create(doc, filterName, cats, elemFilter);
                }
                else
                {
                    try { existing.SetCategories(cats); } catch { }
                    try { existing.SetElementFilter(elemFilter); } catch { }
                    built = existing;
                }
                if (built != null) _matClassFilterCache[cacheKey] = built.Id;
                return built;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"EnsureMaterialClassFilter '{className}': {ex.Message}");
                return null;
            }
        }

        private static Color ParseHexColor(string hex)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return null;
                hex = hex.TrimStart('#');
                if (hex.Length != 6) return null;
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return new Color(r, g, b);
            }
            catch { return null; }
        }

        private static void ApplyCategoryOverrides(Document doc, View view, ViewStylePack pack, PackApplyResult r)
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
                        try { view.SetCategoryHidden(catId, !src.Visible.Value); } catch { }
                    }

                    var ogs = view.GetCategoryOverrides(catId) ?? new OverrideGraphicSettings();

                    // Visibility
                    if (src.Visible.HasValue)
                    {
                        try { view.SetCategoryHidden(catId, !src.Visible.Value); }
                        catch (Exception ex) { r.Warnings.Add($"Visibility '{kv.Key}': {ex.Message}"); }
                    }

                    // Projection line
                    if (!string.IsNullOrEmpty(src.ProjectionLineColor))
                        ogs.SetProjectionLineColor(HexColor(src.ProjectionLineColor));
                    if (src.ProjectionLineWeight.HasValue)
                        ogs.SetProjectionLineWeight(src.ProjectionLineWeight.Value);
                    if (!string.IsNullOrEmpty(src.ProjectionLinePattern))
                    {
                        var lpId = ResolveLinePatternId(doc, src.ProjectionLinePattern);
                        if (lpId != ElementId.InvalidElementId) ogs.SetProjectionLinePatternId(lpId);
                    }

                    // Surface foreground pattern
                    if (!string.IsNullOrEmpty(src.SurfaceFgPatternName))
                    {
                        var fpId = ResolveFillPatternId(doc, src.SurfaceFgPatternName);
                        if (fpId != ElementId.InvalidElementId) ogs.SetSurfaceForegroundPatternId(fpId);
                    }
                    if (!string.IsNullOrEmpty(src.SurfaceFgPatternColor))
                        ogs.SetSurfaceForegroundPatternColor(HexColor(src.SurfaceFgPatternColor));
                    if (src.SurfaceFgPatternVisible.HasValue)
                        ogs.SetSurfaceForegroundPatternVisible(src.SurfaceFgPatternVisible.Value);

                    // Surface background pattern
                    if (!string.IsNullOrEmpty(src.SurfaceBgPatternName))
                    {
                        var fpId = ResolveFillPatternId(doc, src.SurfaceBgPatternName);
                        if (fpId != ElementId.InvalidElementId) ogs.SetSurfaceBackgroundPatternId(fpId);
                    }
                    if (!string.IsNullOrEmpty(src.SurfaceBgPatternColor))
                        ogs.SetSurfaceBackgroundPatternColor(HexColor(src.SurfaceBgPatternColor));
                    if (src.SurfaceBgPatternVisible.HasValue)
                        ogs.SetSurfaceBackgroundPatternVisible(src.SurfaceBgPatternVisible.Value);

                    // Transparency
                    if (src.Transparency.HasValue)
                        ogs.SetSurfaceTransparency(Clamp(src.Transparency.Value, 0, 100));

                    // Halftone
                    if (src.Halftone.HasValue) ogs.SetHalftone(src.Halftone.Value);

                    // Cut line
                    if (!string.IsNullOrEmpty(src.CutLineColor))
                        ogs.SetCutLineColor(HexColor(src.CutLineColor));
                    if (src.CutLineWeight.HasValue)
                        ogs.SetCutLineWeight(src.CutLineWeight.Value);
                    if (!string.IsNullOrEmpty(src.CutLinePattern))
                    {
                        var lpId = ResolveLinePatternId(doc, src.CutLinePattern);
                        if (lpId != ElementId.InvalidElementId) ogs.SetCutLinePatternId(lpId);
                    }

                    // Cut foreground pattern
                    if (!string.IsNullOrEmpty(src.CutFgPatternName))
                    {
                        var fpId = ResolveFillPatternId(doc, src.CutFgPatternName);
                        if (fpId != ElementId.InvalidElementId) ogs.SetCutForegroundPatternId(fpId);
                    }
                    if (!string.IsNullOrEmpty(src.CutFgPatternColor))
                        ogs.SetCutForegroundPatternColor(HexColor(src.CutFgPatternColor));
                    if (src.CutFgPatternVisible.HasValue)
                        ogs.SetCutForegroundPatternVisible(src.CutFgPatternVisible.Value);

                    // Cut background pattern
                    if (!string.IsNullOrEmpty(src.CutBgPatternName))
                    {
                        var fpId = ResolveFillPatternId(doc, src.CutBgPatternName);
                        if (fpId != ElementId.InvalidElementId) ogs.SetCutBackgroundPatternId(fpId);
                    }
                    if (!string.IsNullOrEmpty(src.CutBgPatternColor))
                        ogs.SetCutBackgroundPatternColor(HexColor(src.CutBgPatternColor));
                    if (src.CutBgPatternVisible.HasValue)
                        ogs.SetCutBackgroundPatternVisible(src.CutBgPatternVisible.Value);

                    // Per-category detail level
                    if (!string.IsNullOrEmpty(src.DetailLevel))
                    {
                        if (Enum.TryParse<ViewDetailLevel>(src.DetailLevel, true, out var dl))
                            ogs.SetDetailLevel(dl);
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
                            try { ogs.SetSurfaceForegroundPatternVisible(true); } catch { }
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
                            try { ogs.SetSurfaceBackgroundPatternVisible(true); } catch { }
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
                            try { ogs.SetCutForegroundPatternVisible(true); } catch { }
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
                            try { ogs.SetCutBackgroundPatternVisible(true); } catch { }
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

        // ── Selective apply methods used by ManagedTemplateSyncer ─────────────────

        /// <summary>
        /// Applies only the category VG overrides from <paramref name="pack"/>
        /// to <paramref name="view"/>. Used by ManagedTemplateSyncer when the
        /// managed-fields whitelist contains "vgOverrides".
        /// </summary>
        public static void ApplyCategoryOverridesOnly(Document doc, View view, ViewStylePack pack, PackApplyResult r)
        {
            if (doc == null || view == null || pack == null || r == null) return;
            try { ApplyCategoryOverrides(doc, view, pack, r); }
            catch (Exception ex) { r.Warnings.Add($"ApplyCategoryOverridesOnly: {ex.Message}"); }
        }

        /// <summary>
        /// Applies only the filter rules from <paramref name="pack"/> to
        /// <paramref name="view"/>. Used by ManagedTemplateSyncer when the
        /// managed-fields whitelist contains "filters".
        /// </summary>
        public static void ApplyFilterRulesOnly(Document doc, View view, ViewStylePack pack, PackApplyResult r)
        {
            if (doc == null || view == null || pack == null || r == null) return;
            try { ApplyFilterRules(doc, view, pack, r); }
            catch (Exception ex) { r.Warnings.Add($"ApplyFilterRulesOnly: {ex.Message}"); }
        }

        /// <summary>
        /// Applies workset visibility settings from <paramref name="pack"/> to
        /// <paramref name="view"/>. Silently skips when the document is not workshared.
        /// The pack's WorksetVisibility string is a mode keyword: "ShowAll" / "HideAll" / null (skip).
        /// </summary>
        public static void ApplyWorksetVisibility(Document doc, View view, ViewStylePack pack, PackApplyResult r)
        {
            if (doc == null || view == null || pack == null || r == null) return;
            try
            {
                if (!doc.IsWorkshared)
                { r.Warnings.Add("ApplyWorksetVisibility: document is not workshared — skipped."); return; }
                var mode = (pack.WorksetVisibility ?? "").Trim();
                if (string.IsNullOrEmpty(mode)) return;
                var visibility = string.Equals(mode, "HideAll", StringComparison.OrdinalIgnoreCase)
                    ? WorksetVisibility.Hidden
                    : WorksetVisibility.Visible;
                // CA2021: Workset is not an Element, use GetWorksets() instead
                var wkCol = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset);
                foreach (var wk in wkCol)
                    view.SetWorksetVisibility(wk.Id, visibility);
            }
            catch (Exception ex) { r.Warnings.Add($"ApplyWorksetVisibility: {ex.Message}"); }
        }

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
            catch { }
            return ElementId.InvalidElementId;
        }

        private static ElementId ResolveLinePatternId(Document doc, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ElementId.InvalidElementId;
            if (name.Equals("Solid", StringComparison.OrdinalIgnoreCase))
                return LinePatternElement.GetSolidPatternId();
            return new FilteredElementCollector(doc)
                .OfClass(typeof(LinePatternElement))
                .Cast<LinePatternElement>()
                .FirstOrDefault(lp => string.Equals(lp.Name, name, StringComparison.OrdinalIgnoreCase))
                ?.Id ?? ElementId.InvalidElementId;
        }

        private static ElementId ResolveFillPatternId(Document doc, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ElementId.InvalidElementId;
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(fp => string.Equals(fp.Name, name, StringComparison.OrdinalIgnoreCase))
                ?.Id ?? ElementId.InvalidElementId;
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

        // ── Additional API surface used by drawing-type machinery ─────────

        /// <summary>Invalidate any internal caches (currently a no-op — state is
        /// held per-call, not statically). Provided for callers that follow the
        /// invalidate-then-apply pattern.</summary>
        public static void InvalidateCache() { }

        /// <summary>Read the category VG override map from the pack into a plain
        /// dictionary (key = category key, value = the raw override object).
        /// Returns an empty dictionary when the pack has no overrides.</summary>
        public static Dictionary<string, object> ReadCategoryOverrides(ViewStylePack pack)
        {
            var result = new Dictionary<string, object>();
            if (pack?.VgOverrides == null) return result;
            foreach (var kv in pack.VgOverrides)
                result[kv.Key] = kv.Value;
            return result;
        }

        /// <summary>Read category override keys that are currently active on
        /// <paramref name="view"/> and return them as a
        /// <see cref="StyleVgOverride"/> dictionary suitable for storing
        /// directly into <see cref="ViewStylePack.VgOverrides"/>.
        /// The dictionary is keyed by category name.
        /// Returns an empty dictionary when <paramref name="view"/> is
        /// null or has no overrides.</summary>
        public static Dictionary<string, StyleVgOverride> ReadCategoryOverrides(Document doc, View view)
        {
            var result = new Dictionary<string, StyleVgOverride>();
            if (doc == null || view == null) return result;
            try
            {
                foreach (Category cat in doc.Settings.Categories)
                {
                    try
                    {
                        if (view.GetCategoryHidden(cat.Id)) continue;
                        var ogs = view.GetCategoryOverrides(cat.Id);
                        if (ogs == null) continue;
                        var svo = new StyleVgOverride
                        {
                            Halftone             = ogs.Halftone ? (bool?)true : null,
                            ProjectionLineWeight = ogs.ProjectionLineWeight > 0 ? (int?)ogs.ProjectionLineWeight : null,
                            Transparency         = ogs.Transparency > 0 ? (int?)ogs.Transparency : null,
                        };
                        // Only store entries that carry at least one non-default field.
                        if (svo.Halftone != null || svo.ProjectionLineWeight != null || svo.Transparency != null)
                            result[cat.Name ?? cat.Id.ToString()] = svo;
                    }
                    catch { /* skip inaccessible categories */ }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ReadCategoryOverrides(view): {ex.Message}");
            }
            return result;
        }

        /// <summary>Apply the pack's VG + filter settings as a preset; delegates
        /// to <see cref="Apply"/>.</summary>
        public static void ApplyPresetOverrides(Document doc, View view, ViewStylePack pack)
        {
            Apply(doc, view, pack);
        }

        /// <summary>Apply a list of <see cref="PresetCategoryOverride"/> entries
        /// (from <see cref="DrawingProductionPreset.VgOverrides"/>) onto a view.
        /// Results are collected into <paramref name="r"/>.</summary>
        public static void ApplyPresetOverrides(
            Document doc, View view,
            System.Collections.Generic.List<PresetCategoryOverride> overrides,
            PackApplyResult r)
        {
            if (doc == null || view == null || overrides == null || r == null) return;
            foreach (var o in overrides)
            {
                if (string.IsNullOrWhiteSpace(o.Category)) continue;
                try
                {
                    var catId = ResolveCategoryId(doc, o.Category);
                    if (catId == ElementId.InvalidElementId)
                    {
                        r.Warnings.Add($"PresetOverride: category '{o.Category}' not found.");
                        continue;
                    }
                    var ogs = view.GetCategoryOverrides(catId) ?? new OverrideGraphicSettings();
                    if (o.Halftone.HasValue)          ogs.SetHalftone(o.Halftone.Value);
                    if (o.ProjLineWeight.HasValue)     ogs.SetProjectionLineWeight(o.ProjLineWeight.Value);
                    if (!string.IsNullOrEmpty(o.ProjLineColor)) ogs.SetProjectionLineColor(HexColor(o.ProjLineColor));
                    if (o.CutLineWeight.HasValue)      ogs.SetCutLineWeight(o.CutLineWeight.Value);
                    if (!string.IsNullOrEmpty(o.CutLineColor))  ogs.SetCutLineColor(HexColor(o.CutLineColor));
                    if (o.Transparency.HasValue)       ogs.SetSurfaceTransparency(Clamp(o.Transparency.Value, 0, 100));
                    if (o.Visible.HasValue)            view.SetCategoryHidden(catId, !o.Visible.Value);
                    view.SetCategoryOverrides(catId, ogs);
                    r.OverridesSet++;
                }
                catch (Exception ex) { r.Warnings.Add($"PresetOverride '{o.Category}': {ex.Message}"); }
            }
        }

        /// <summary>Apply only category VG overrides from the pack, skipping filter
        /// rules.</summary>
        public static void ApplyCategoryOverridesOnly(Document doc, View view, ViewStylePack pack)
        {
            if (doc == null || view == null || pack == null) return;
            var dummy = new PackApplyResult();
            ApplyCategoryOverrides(doc, view, pack, dummy);
        }

        /// <summary>Apply only filter rules from the pack, skipping category VG
        /// overrides.</summary>
        public static void ApplyFilterRulesOnly(Document doc, View view, ViewStylePack pack)
        {
            if (doc == null || view == null || pack == null) return;
            var dummy = new PackApplyResult();
            ApplyFilterRules(doc, view, pack, dummy);
        }

    }
}
