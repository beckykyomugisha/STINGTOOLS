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
                        try { view.SetCategoryHidden(catId, !src.Visible.Value); } catch { }
                    }

                    var ogs = view.GetCategoryOverrides(catId) ?? new OverrideGraphicSettings();

                    if (src.Halftone.HasValue)             ogs.SetHalftone(src.Halftone.Value);
                    if (src.ProjectionLineWeight.HasValue) ogs.SetProjectionLineWeight(src.ProjectionLineWeight.Value);
                    if (!string.IsNullOrEmpty(src.ProjectionLineColor)) ogs.SetProjectionLineColor(HexColor(src.ProjectionLineColor));
                    if (src.CutLineWeight.HasValue)        ogs.SetCutLineWeight(src.CutLineWeight.Value);
                    if (!string.IsNullOrEmpty(src.CutLineColor))        ogs.SetCutLineColor(HexColor(src.CutLineColor));
                    if (src.Transparency.HasValue)
                    {
                        var t = Clamp(src.Transparency.Value, 0, 100);
                        ogs.SetSurfaceTransparency(t);
                        // 100% transparency on a presentation pack means
                        // "outline only" — hide the surface foreground fill
                        // so only the projection line work renders.
                        if (t >= 100)
                        {
                            try { ogs.SetSurfaceForegroundPatternVisible(false); } catch { }
                            try { ogs.SetSurfaceBackgroundPatternVisible(false); } catch { }
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
