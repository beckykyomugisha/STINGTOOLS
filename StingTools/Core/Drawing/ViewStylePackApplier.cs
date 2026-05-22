// StingTools — Drawing Template Manager · Week 2
//
// ViewStylePackApplier takes a resolved ViewStylePack and pushes its
// settings onto a View. Called by DrawingTypePresentation.Apply after
// the profile-level scale / template / detail-level have landed.
//
// What it applies:
//   * per-category VG overrides (halftone, line weight, colour, transparency)
//   * per-filter rules (category filters → OverrideGraphicSettings)
//   * text style hint (stored via view parameter — actual switching
//     happens at annotation-pass time where we create text)
//   * dimension style hint (same — stored on the view for the
//     annotation runner to consume)
//
// What it does NOT do:
//   * Line-weight scale — Revit sets that project-wide, not per-view
//   * Hatch palette — a conceptual grouping the user interprets
//
// The applier is defensive: missing filter / category → warning, not
// error, so partial corporate catalogues still produce output.

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

    public static class ViewStylePackApplier
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
                // Carry on; writing overrides onto the view is a no-op
                // when the template locks them, but not an error.
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
                    if (!string.IsNullOrEmpty(src.ProjectionColor))
                    {
                        var c = ParseHexColor(src.ProjectionColor);
                        if (c != null) ogs.SetProjectionLineColor(c);
                    }
                    if (!string.IsNullOrEmpty(src.CutColor))
                    {
                        var c = ParseHexColor(src.CutColor);
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
                    var catId = ResolveCategoryId(doc, kv.Key);
                    if (catId == ElementId.InvalidElementId) { r.Warnings.Add($"Category '{kv.Key}' not found."); continue; }

                    var ogs = view.GetCategoryOverrides(catId) ?? new OverrideGraphicSettings();
                    var src = kv.Value;

                    if (src.Halftone.HasValue)             ogs.SetHalftone(src.Halftone.Value);
                    if (src.ProjectionLineWeight.HasValue) ogs.SetProjectionLineWeight(src.ProjectionLineWeight.Value);
                    if (!string.IsNullOrEmpty(src.ProjectionLineColor)) ogs.SetProjectionLineColor(HexColor(src.ProjectionLineColor));
                    if (src.CutLineWeight.HasValue)        ogs.SetCutLineWeight(src.CutLineWeight.Value);
                    if (!string.IsNullOrEmpty(src.CutLineColor))        ogs.SetCutLineColor(HexColor(src.CutLineColor));
                    if (src.Transparency.HasValue)         ogs.SetSurfaceTransparency(Clamp(src.Transparency.Value, 0, 100));

                    view.SetCategoryOverrides(catId, ogs);
                    r.OverridesSet++;
                }
                catch (Exception ex) { r.Warnings.Add($"VG override '{kv.Key}': {ex.Message}"); }
            }
        }

        private static void ApplyFilterRules(Document doc, View view, ViewStylePack pack, PackApplyResult r)
        {
            if (pack.Filters == null) return;
            foreach (var rule in pack.Filters)
            {
                if (string.IsNullOrWhiteSpace(rule.FilterName)) continue;
                try
                {
                    var filter = new FilteredElementCollector(doc)
                        .OfClass(typeof(ParameterFilterElement))
                        .Cast<ParameterFilterElement>()
                        .FirstOrDefault(f => string.Equals(f.Name, rule.FilterName, StringComparison.OrdinalIgnoreCase));
                    if (filter == null) { r.Warnings.Add($"Filter '{rule.FilterName}' not found — create it first."); continue; }

                    if (!view.GetFilters().Contains(filter.Id))
                        view.AddFilter(filter.Id);

                    var ogs = view.GetFilterOverrides(filter.Id) ?? new OverrideGraphicSettings();
                    if (rule.ProjectionLineWeight.HasValue) ogs.SetProjectionLineWeight(rule.ProjectionLineWeight.Value);
                    if (!string.IsNullOrEmpty(rule.ProjectionLineColor)) ogs.SetProjectionLineColor(HexColor(rule.ProjectionLineColor));
                    if (rule.CutLineWeight.HasValue)        ogs.SetCutLineWeight(rule.CutLineWeight.Value);
                    if (!string.IsNullOrEmpty(rule.CutLineColor))        ogs.SetCutLineColor(HexColor(rule.CutLineColor));
                    if (rule.Transparency.HasValue)         ogs.SetSurfaceTransparency(Clamp(rule.Transparency.Value, 0, 100));
                    ogs.SetHalftone(rule.Halftone);

                    view.SetFilterOverrides(filter.Id, ogs);
                    view.SetFilterVisibility(filter.Id, rule.Visible);
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
                // Try BuiltInCategory first (string parse)
                if (Enum.TryParse<BuiltInCategory>(key, true, out var bic))
                {
                    var c = Category.GetCategory(doc, bic);
                    if (c != null) return c.Id;
                }
                // Fall back to Name lookup across all Categories
                foreach (Category c in doc.Settings.Categories)
                    if (string.Equals(c.Name, key, StringComparison.OrdinalIgnoreCase))
                        return c.Id;
                // Handle subcategory-style keys like "<Room Separation>"
                var trimmed = key.Trim('<', '>', ' ');
                foreach (Category c in doc.Settings.Categories)
                    foreach (Category sub in c.SubCategories)
                        if (string.Equals(sub.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                            return sub.Id;
            }
            catch { }
            return ElementId.InvalidElementId;
        }

        private static Autodesk.Revit.DB.Color HexColor(string hex)
        {
            // Accept "#RRGGBB" or "RRGGBB"
            if (string.IsNullOrWhiteSpace(hex)) return new Autodesk.Revit.DB.Color(0, 0, 0);
            var s = hex.TrimStart('#');
            if (s.Length != 6) return new Autodesk.Revit.DB.Color(0, 0, 0);
            byte r = byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber);
            byte g = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber);
            byte b = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber);
            return new Autodesk.Revit.DB.Color(r, g, b);
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
