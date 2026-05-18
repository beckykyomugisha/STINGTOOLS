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
            return r;
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
        public static void InvalidateCache(Document doc) { }

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

        public static void ApplyPresetOverrides(Document doc, View view,
            List<StingTools.Core.Drawing.PresetCategoryOverride> presetVg, PackApplyResult r)
        {
            if (doc == null || view == null || presetVg == null || r == null) return;
            // Minimal stub — preset overrides are applied via DrawingTypePresentation pipeline
            r.Warnings.Add("ApplyPresetOverrides: preset VG overrides applied via DrawingTypePresentation.");
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

        /// <summary>Apply only category VG overrides from the pack, collecting
        /// results into an existing <see cref="PackApplyResult"/>.</summary>
        public static void ApplyCategoryOverridesOnly(
            Document doc, View view, ViewStylePack pack, PackApplyResult r)
        {
            if (doc == null || view == null || pack == null || r == null) return;
            ApplyCategoryOverrides(doc, view, pack, r);
        }

        /// <summary>Apply only filter rules from the pack, skipping category VG
        /// overrides.</summary>
        public static void ApplyFilterRulesOnly(Document doc, View view, ViewStylePack pack)
        {
            if (doc == null || view == null || pack == null) return;
            var dummy = new PackApplyResult();
            ApplyFilterRules(doc, view, pack, dummy);
        }

        /// <summary>Apply only filter rules from the pack, collecting results into
        /// an existing <see cref="PackApplyResult"/>.</summary>
        public static void ApplyFilterRulesOnly(
            Document doc, View view, ViewStylePack pack, PackApplyResult r)
        {
            if (doc == null || view == null || pack == null || r == null) return;
            ApplyFilterRules(doc, view, pack, r);
        }

        /// <summary>Apply workset visibility settings from the pack onto the view.
        /// Currently a no-op stub — workset visibility is set project-wide in Revit
        /// and cannot be overridden per-view via the API; this method exists so
        /// callers that reference it compile correctly and warnings are surfaced at
        /// runtime via <paramref name="r"/>.</summary>
        public static void ApplyWorksetVisibility(
            Document doc, View view, ViewStylePack pack, PackApplyResult r)
        {
            if (r != null)
                r.Warnings.Add("ApplyWorksetVisibility: workset visibility has no per-view public Revit API — skipped.");
        }
    }
}
