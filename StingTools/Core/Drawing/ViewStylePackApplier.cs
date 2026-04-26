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

        internal static void ApplyFilterRules(Document doc, View view, ViewStylePack pack, PackApplyResult r)
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
                    var rgs = new RevitLinkGraphicsSettings();
                    if (kv.Value?.Hidden == true)
                        rgs.LinkVisibilityType = LinkVisibility.Invisible;
                    if (kv.Value?.Halftone.HasValue == true)
                    {
                        var ogs = new OverrideGraphicSettings();
                        ogs.SetHalftone(kv.Value.Halftone.Value);
                        rgs.OverrideGraphicSettings = ogs;
                    }
                    view.SetLinkOverrides(link.Id, rgs);
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
                    var catId = ResolveCategoryId(doc, kv.Key);
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
                    view.SetFilterEnabled(filter.Id, kv.Value);
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
                catch { }
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
                        try { view.SetCategoryHidden(catId, !o.Visible.Value); } catch { }
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
            catch { }
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
            catch { }
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
            catch { return ElementId.InvalidElementId; }
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
            catch { return ElementId.InvalidElementId; }
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
