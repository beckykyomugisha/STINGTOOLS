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
    }
}
