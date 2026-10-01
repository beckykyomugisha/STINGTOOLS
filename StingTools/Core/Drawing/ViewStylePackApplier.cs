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
        /// <summary>
        /// V-4: this was a no-op with a comment claiming no cache existed —
        /// while ResolveFilterIdCached and ResolveFillPattern ran a full
        /// collector on every call. Now that those are genuinely indexed per
        /// document, this drops the indexes so a filter or pattern created
        /// mid-run is visible to the very next lookup (the lazy-create path
        /// below calls it for exactly that reason).
        /// </summary>
        public static void InvalidateCache(Document doc) => InvalidateResolverCaches(doc);
        public static void ReadCategoryOverrides(Document doc, View view, ViewStylePack pack) { /* No-op stub. */ }

        public static PackApplyResult Apply(Document doc, View view, ViewStylePack pack)
            => Apply(doc, view, pack, extraLineWeightScale: 1.0);

        /// <summary>
        /// <paramref name="extraLineWeightScale"/> multiplies the pack's own
        /// <see cref="ViewStylePack.LineWeightScale"/>. DrawingTypePresentation
        /// passes the profile's <c>print.lineWeightScale</c> here so the two
        /// scales are combined into ONE pass over each category's
        /// OverrideGraphicSettings — two independent passes would compound in
        /// an order-dependent way.
        /// </summary>
        public static PackApplyResult Apply(Document doc, View view, ViewStylePack pack, double extraLineWeightScale)
        {
            var r = new PackApplyResult();
            if (doc == null || view == null || pack == null) return r;
            if (view.IsTemplate) return r;

            // DTW-173: this used to say overrides "will be applied to the template"
            // and then wrote them to the view — where a template that controls V/G
            // masks them, so they never showed. Ask the template what it controls:
            // write each part only where the view, not the template, decides.
            var masked = TemplateControlledVg(doc, view);
            bool vgMasked = masked.Contains(BuiltInParameter.VIS_GRAPHICS_MODEL);
            bool filtersMasked = masked.Contains(BuiltInParameter.VIS_GRAPHICS_FILTERS);
            bool worksetsMasked = masked.Contains(BuiltInParameter.VIS_GRAPHICS_WORKSETS);
            if (masked.Count > 0)
            {
                var parts = new List<string>();
                if (vgMasked) parts.Add("category overrides");
                if (filtersMasked) parts.Add("filters");
                if (worksetsMasked) parts.Add("workset visibility");
                if (parts.Count > 0)
                    r.Warnings.Add($"Pack '{pack.Id}': the view's template controls {string.Join(", ", parts)} — "
                        + "those parts of the pack are masked and were not written. Use a managed pack, or release "
                        + "them in the template, for the pack to show.");
            }

            if (!vgMasked)
            {
                ApplyCategoryOverrides(doc, view, pack, r);
                ApplyLineWeightScale(doc, view, pack, r, extraLineWeightScale);
            }
            if (!filtersMasked)
            {
                ApplyFilterRules(doc, view, pack, r);
                ApplyMaterialClassOverrides(doc, view, pack, r);   // DTW-177: was never called
            }
            if (!worksetsMasked) ApplyWorksetVisibility(doc, view, pack, r);
            ApplyLinkOverrides(doc, view, pack, r);   // element overrides: never template-controlled
            ApplyColorFillSchemes(doc, view, pack, r);
            if (!filtersMasked) ApplyFilterEnabled(doc, view, pack, r);
            return r;
        }

        /// <summary>
        /// DTW-173: the V/G template parameters the view's template controls (empty
        /// when the view has no template). A failed read is treated as "not
        /// controlled", i.e. the pre-DTW-173 behaviour of writing to the view.
        /// </summary>
        private static HashSet<BuiltInParameter> TemplateControlledVg(Document doc, View view)
        {
            var result = new HashSet<BuiltInParameter>();
            if (view?.ViewTemplateId == null || view.ViewTemplateId == ElementId.InvalidElementId) return result;
            try
            {
                if (!(doc.GetElement(view.ViewTemplateId) is View template)) return result;
                var all = new HashSet<ElementId>(template.GetTemplateParameterIds());
                var nonControlled = new HashSet<ElementId>(template.GetNonControlledTemplateParameterIds());
                foreach (var bip in new[] { BuiltInParameter.VIS_GRAPHICS_MODEL, BuiltInParameter.VIS_GRAPHICS_FILTERS,
                                            BuiltInParameter.VIS_GRAPHICS_WORKSETS, BuiltInParameter.VIS_GRAPHICS_ANNOTATION })
                {
                    var id = new ElementId(bip);
                    if (all.Contains(id) && !nonControlled.Contains(id)) result.Add(bip);
                }
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("ViewStylePack.TemplateControl",
                    $"ViewStylePackApplier: reading the template's controlled parameters failed — writing to the view: {ex.Message}");
                result.Clear();
            }
            return result;
        }

        /// <summary>
        /// Scale every line weight this pack states by
        /// <see cref="ViewStylePack.LineWeightScale"/>.
        ///
        /// The field was declared on the pack, promoted from the nested
        /// "appearance" block by the registry, carried through the extends
        /// fold — and then read by NOTHING. All 35 shipped packs carry it and
        /// four author it (0.6 – 1.1 on the presentation packs, whose whole
        /// point is lighter line work), so a presentation drawing rendered at
        /// exactly the same weights as a production one.
        ///
        /// Revit has no view-level line-weight multiplier, so the scale is
        /// applied where the pack's own weights are written: each category
        /// override's projection / cut weight is multiplied and clamped to
        /// Revit's 1..16. Weights the pack does not state are left alone —
        /// scaling Revit's own object-style defaults would silently restyle
        /// categories the pack never mentions.
        ///
        /// A scale of 1.0 (the default) is a no-op and costs one comparison.
        /// </summary>
        internal static void ApplyLineWeightScale(Document doc, View view, ViewStylePack pack, PackApplyResult r,
            double extraScale = 1.0)
        {
            if (doc == null || view == null || pack == null) return;
            if (extraScale <= 0) extraScale = 1.0;
            double scale = (pack.LineWeightScale > 0 ? pack.LineWeightScale : 1.0) * extraScale;
            if (scale <= 0 || Math.Abs(scale - 1.0) < 1e-9) return;
            if (pack.VgOverrides == null || pack.VgOverrides.Count == 0) return;

            int scaled = 0;
            foreach (var kv in pack.VgOverrides)
            {
                var src = kv.Value;
                if (src == null) continue;
                if (!src.ProjectionLineWeight.HasValue && !src.CutLineWeight.HasValue) continue;
                try
                {
                    var catId = ResolveCategoryIdCached(doc, kv.Key);
                    if (catId == ElementId.InvalidElementId) continue;
                    var ogs = view.GetCategoryOverrides(catId) ?? new OverrideGraphicSettings();
                    if (src.ProjectionLineWeight.HasValue)
                        ApplyWeight(ScaleWeight(src.ProjectionLineWeight.Value, scale),
                            w => ogs.SetProjectionLineWeight(w), kv.Key, "scaled projectionLineWeight", r);
                    if (src.CutLineWeight.HasValue)
                        ApplyWeight(ScaleWeight(src.CutLineWeight.Value, scale),
                            w => ogs.SetCutLineWeight(w), kv.Key, "scaled cutLineWeight", r);
                    view.SetCategoryOverrides(catId, ogs);
                    scaled++;
                }
                catch (Exception ex) { r.Warnings.Add($"lineWeightScale on '{kv.Key}': {ex.Message}"); }
            }
            if (scaled > 0)
                r.Warnings.Add($"Pack '{pack.Id}' lineWeightScale {scale:0.##} applied to {scaled} category override(s).");
        }

        /// <summary>
        /// Multiply and clamp to Revit's 1..16. Rounds to nearest, and never
        /// below 1 — a scale of 0.6 on weight 1 must stay visible, not vanish.
        /// Revit-free so the rounding is unit-testable.
        /// </summary>
        internal static int ScaleWeight(int weight, double scale)
        {
            if (weight <= 0) return weight;
            int scaled = (int)Math.Round(weight * scale, MidpointRounding.AwayFromZero);
            if (scaled < MinLineWeight) scaled = MinLineWeight;
            if (scaled > MaxLineWeight) scaled = MaxLineWeight;
            return scaled;
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

                    // DTW-177: the filter used to be added with no override, so it
                    // changed nothing on the drawing. Apply the class's override.
                    var ogs = view.GetFilterOverrides(filter.Id) ?? new OverrideGraphicSettings();
                    if (src.Halftone.HasValue) ogs.SetHalftone(src.Halftone.Value);
                    if (src.Transparency.HasValue) ogs.SetSurfaceTransparency(Clamp(src.Transparency.Value, 0, 100));
                    ApplyWeight(src.ProjectionLineWeight, w => ogs.SetProjectionLineWeight(w), $"byMaterialClass '{className}'", "projectionLineWeight", r);
                    ApplyWeight(src.CutLineWeight, w => ogs.SetCutLineWeight(w), $"byMaterialClass '{className}'", "cutLineWeight", r);
                    if (!string.IsNullOrEmpty(src.ProjectionLineColor)) ogs.SetProjectionLineColor(HexColor(src.ProjectionLineColor));
                    if (!string.IsNullOrEmpty(src.CutLineColor)) ogs.SetCutLineColor(HexColor(src.CutLineColor));
                    view.SetFilterOverrides(filter.Id, ogs);
                    view.SetFilterVisibility(filter.Id, true);
                    r.AppliedByMaterialClass++;
                }
                catch (Exception ex)
                {
                    r.Warnings.Add($"byMaterialClass '{className}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Revit line weights are 1..16; anything else throws. Apply the
        /// weight when it is in range, treat 0 / null as "not stated", and
        /// report anything else by name instead of letting the throw abandon
        /// the surrounding override block.
        /// </summary>
        internal const int MinLineWeight = 1;
        internal const int MaxLineWeight = 16;

        /// <summary>Revit-free range test, so the rule is unit-testable.</summary>
        internal static bool IsValidLineWeight(int w) => w >= MinLineWeight && w <= MaxLineWeight;

        private static void ApplyWeight(int? weight, Action<int> setter, string subject, string field, PackApplyResult r)
        {
            if (!weight.HasValue) return;
            int w = weight.Value;
            if (w == 0) return;            // serialiser default ⇒ "not stated"
            if (!IsValidLineWeight(w))
            {
                r?.Warnings.Add(
                    $"{subject}: {field} {w} is outside Revit's 1..16 range — that one value is ignored; " +
                    "the rest of the override was applied.");
                return;
            }
            try { setter(w); }
            catch (Exception ex)
            {
                r?.Warnings.Add($"{subject}: {field} {w} rejected by Revit ({ex.Message}); the rest of the override was applied.");
            }
        }

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
                        // H-4 — was a silent catch. SetCategoryHidden THROWS when the
                        // category cannot be controlled in this view, so here the
                        // exception is the signal (unlike the parameter writes in
                        // this batch, where the return value is). A swallowed
                        // failure means the drawing renders with categories the
                        // style pack says are hidden — visible on the sheet, and
                        // attributed to the pack being wrong rather than unapplied.
                        SafeWrite.Try(() => view.SetCategoryHidden(catId, !src.Visible.Value),
                            "ViewStylePack.Visibility",
                            $"category '{kv.Key}' in view '{view.Name}'",
                            r?.Warnings);
                    }

                    var ogs = view.GetCategoryOverrides(catId) ?? new OverrideGraphicSettings();

                    if (src.Halftone.HasValue)             ogs.SetHalftone(src.Halftone.Value);
                    // Revit's valid line-weight range is 1..16 and
                    // SetProjectionLineWeight THROWS outside it. Because the
                    // throw happens before SetCategoryOverrides at the end of
                    // this block, one out-of-range weight used to discard the
                    // category's ENTIRE override — colour, halftone and
                    // transparency with it — leaving only a warning. A single
                    // `projWeight: 0` written by a round-trip through a
                    // non-nullable int (which is exactly what the pack editor
                    // used to produce) was enough to do it.
                    //
                    // ApplyWeight reports the bad value and carries on, so the
                    // rest of the override still lands. 0 is treated as "not
                    // stated" rather than as an error, since that is what a
                    // serialiser default means.
                    ApplyWeight(src.ProjectionLineWeight, w => ogs.SetProjectionLineWeight(w),
                        kv.Key, "projectionLineWeight", r);
                    if (!string.IsNullOrEmpty(src.ProjectionLineColor)) ogs.SetProjectionLineColor(HexColor(src.ProjectionLineColor));
                    ApplyWeight(src.CutLineWeight, w => ogs.SetCutLineWeight(w),
                        kv.Key, "cutLineWeight", r);
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
                            SafeWrite.Try(() => ogs.SetSurfaceForegroundPatternVisible(false), "ViewStylePack.Pattern", "surface fg pattern off", r?.Warnings);
                            SafeWrite.Try(() => ogs.SetSurfaceBackgroundPatternVisible(false), "ViewStylePack.Pattern", "surface bg pattern off", r?.Warnings);
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
                    else
                    {
                        // DTW-167: an existing registry filter is brought up to its
                        // current definition once per document per session, so a
                        // data correction reaches projects that minted it earlier.
                        RefreshRegistryFilterOnce(doc, rule.FilterName, r);
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
                    ApplyWeight(projWeight, w => ogs.SetProjectionLineWeight(w),
                        rule.FilterName, "projectionLineWeight", r);
                    ApplyLinePattern(doc, rule.ProjectionLinePattern ?? defaults?.ProjLinePattern,
                        id => ogs.SetProjectionLinePatternId(id), rule.FilterName, r);

                    // Cut line
                    var cutColor = rule.CutLineColor ?? defaults?.CutColor;
                    if (!string.IsNullOrEmpty(cutColor)) ogs.SetCutLineColor(HexColor(cutColor));
                    var cutWeight = rule.CutLineWeight ?? defaults?.CutWeight;
                    ApplyWeight(cutWeight, w => ogs.SetCutLineWeight(w),
                        rule.FilterName, "cutLineWeight", r);
                    ApplyLinePattern(doc, rule.CutLinePattern ?? defaults?.CutLinePattern,
                        id => ogs.SetCutLinePatternId(id), rule.FilterName, r);

                    // Surface / cut, foreground / background fills. DTW-165: a colour
                    // with no pattern draws nothing in Revit, so a stated colour with
                    // no stated pattern means solid fill; a pattern that does not
                    // resolve is reported once per name.
                    ApplyFill(doc, rule.SurfaceFgColor ?? defaults?.SurfFgColor, rule.SurfaceFgPattern ?? defaults?.SurfFgPattern,
                        c => ogs.SetSurfaceForegroundPatternColor(c), id => ogs.SetSurfaceForegroundPatternId(id),
                        () => ogs.SetSurfaceForegroundPatternVisible(true), rule.FilterName, "surface foreground", r);
                    ApplyFill(doc, rule.SurfaceBgColor ?? defaults?.SurfBgColor, rule.SurfaceBgPattern ?? defaults?.SurfBgPattern,
                        c => ogs.SetSurfaceBackgroundPatternColor(c), id => ogs.SetSurfaceBackgroundPatternId(id),
                        () => ogs.SetSurfaceBackgroundPatternVisible(true), rule.FilterName, "surface background", r);
                    ApplyFill(doc, rule.CutFgColor ?? defaults?.CutFgColor, rule.CutFgPattern ?? defaults?.CutFgPattern,
                        c => ogs.SetCutForegroundPatternColor(c), id => ogs.SetCutForegroundPatternId(id),
                        () => ogs.SetCutForegroundPatternVisible(true), rule.FilterName, "cut foreground", r);
                    ApplyFill(doc, rule.CutBgColor ?? defaults?.CutBgColor, rule.CutBgPattern ?? defaults?.CutBgPattern,
                        c => ogs.SetCutBackgroundPatternColor(c), id => ogs.SetCutBackgroundPatternId(id),
                        () => ogs.SetCutBackgroundPatternVisible(true), rule.FilterName, "cut background", r);

                    // Transparency
                    var transp = rule.Transparency ?? defaults?.Transparency;
                    if (transp.HasValue) ogs.SetSurfaceTransparency(Clamp(transp.Value, 0, 100));

                    // Halftone — pack wins when it states a value, else the
                    // registry default, else off. The old `rule.Halftone ||
                    // defaults?.Halftone == true` could never express "pack
                    // says OFF" against a default of ON.
                    bool halftone = rule.Halftone ?? (defaults?.Halftone ?? false);
                    ogs.SetHalftone(halftone);

                    // Detail level — Revit 2023+ override.
                    var dlStr = rule.DetailLevel ?? defaults?.DetailLevel;
                    if (!string.IsNullOrEmpty(dlStr) &&
                        Enum.TryParse<ViewDetailLevel>(dlStr, true, out var dl))
                    {
                        // V-10: was a silent catch "for < 2023"; the plugin targets
                        // 2025+, so a throw here is a real failure to honour the
                        // pack's detail level and must reach the result.
                        SafeWrite.Try(() => ogs.SetDetailLevel(dl), "ViewStylePack.Filter",
                            $"detail level '{dlStr}' on filter '{rule.FilterName}'", r?.Warnings);
                    }

                    view.SetFilterOverrides(filterId, ogs);

                    // Visibility — pack wins when stated, else the registry
                    // default, else visible. The old form read the DEFAULT
                    // whenever the rule was true, inverting its own comment.
                    bool visible = rule.Visible ?? (defaults?.Visible ?? true);
                    view.SetFilterVisibility(filterId, visible);
                    r.FiltersApplied++;
                }
                catch (Exception ex) { r.Warnings.Add($"Filter '{rule.FilterName}': {ex.Message}"); }
            }
        }

        private static readonly HashSet<string> _refreshedFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static void RefreshRegistryFilterOnce(Document doc, string filterName, PackApplyResult r)
        {
            string key = (doc?.PathName ?? doc?.Title ?? "_") + "|" + filterName;
            lock (_refreshedFilters) { if (!_refreshedFilters.Add(key)) return; }
            var def = AecFilterRegistry.GetByName(doc, filterName);
            if (def == null) return;
            var f = AecFilterFactory.FindOrCreate(doc, def);
            foreach (var w in f.Warnings) r.Warnings.Add($"Filter '{filterName}': {w}");
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
            try { ApplyMaterialClassOverrides(doc, view, pack, r); }
            catch (Exception ex) { r.Warnings.Add($"ApplyFilterRulesOnly (byMaterialClass): {ex.Message}"); }
        }

        /// <summary>
        /// Applies workset visibility settings from <paramref name="pack"/> to
        /// <paramref name="view"/>. A pack that states no mode is skipped silently; one that states a mode on a
        /// non-workshared document warns (see <see cref="WorksetVisibilityPlan"/>).
        /// The pack's WorksetVisibility string is a mode keyword: "ShowAll" / "HideAll" / null (skip).
        /// </summary>
        public static void ApplyWorksetVisibility(Document doc, View view, ViewStylePack pack, PackApplyResult r)
        {
            if (doc == null || view == null || pack == null || r == null) return;
            try
            {
                // V-11: the pack's intent decides first. The workshare check
                // used to run before this, so every apply on every
                // non-workshared project warned "skipped" although no shipped
                // pack sets worksetVisibility -- noise that trains users to
                // ignore the warnings list.
                switch (WorksetVisibilityPlan.Decide(pack.WorksetVisibility, doc.IsWorkshared))
                {
                    case WorksetVisibilityAction.None: return;
                    case WorksetVisibilityAction.WarnNotWorkshared:
                        r.Warnings.Add(WorksetVisibilityPlan.NotWorksharedWarning(pack.Id, pack.WorksetVisibility));
                        return;
                }
                var visibility = WorksetVisibilityPlan.Hides(pack.WorksetVisibility)
                    ? WorksetVisibility.Hidden
                    : WorksetVisibility.Visible;
                // CA2021: Workset is not an Element, use GetWorksets() instead
                var wkCol = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset);
                foreach (var wk in wkCol)
                    view.SetWorksetVisibility(wk.Id, visibility);
            }
            catch (Exception ex) { r.Warnings.Add($"ApplyWorksetVisibility: {ex.Message}"); }
        }

        // ── Phase 137 — Revit-link graphic overrides ──
        // The pack's LinkOverrides is a raw JSON token (merge-stub model).
        // Shape: { "<link name>": { "hidden": true, "halftone": true } }.
        internal static void ApplyLinkOverrides(Document doc, View view, ViewStylePack pack, PackApplyResult r)
        {
            if (pack?.LinkOverrides == null) return;
            Dictionary<string, Newtonsoft.Json.Linq.JObject> spec = null;
            try
            {
                var tok = pack.LinkOverrides as Newtonsoft.Json.Linq.JToken
                          ?? Newtonsoft.Json.Linq.JToken.FromObject(pack.LinkOverrides);
                if (tok is Newtonsoft.Json.Linq.JObject jo)
                    spec = jo.ToObject<Dictionary<string, Newtonsoft.Json.Linq.JObject>>();
            }
            catch (Exception ex) { r.Warnings.Add($"ApplyLinkOverrides parse: {ex.Message}"); return; }
            if (spec == null || spec.Count == 0) return;

            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().ToList();
            foreach (var kv in spec)
            {
                try
                {
                    // DTW-127: an instance's Name is "<file>.rvt : 1 : <location>", so a pack
                    // keyed by the link's name ("Structure" / "Structure.rvt") never matched.
                    // Match the instance name, then its type name, then the file name with
                    // and without extension — every instance of a matched link is overridden.
                    var matched = links.Where(l => LinkMatches(doc, l, kv.Key)).ToList();
                    if (matched.Count == 0) { r.Warnings.Add($"Revit link '{kv.Key}' not found — skipped."); continue; }
                    bool hidden = (bool?)(kv.Value?["hidden"]) ?? false;
                    bool halftone = (bool?)(kv.Value?["halftone"]) ?? false;
                    foreach (var link in matched)
                    {
                        if (hidden && view.CanCategoryBeHidden(new ElementId(BuiltInCategory.OST_RvtLinks)))
                        {
                            try { view.HideElements(new List<ElementId> { link.Id }); }
                            catch (Exception ex) { r.Warnings.Add($"Link hide '{kv.Key}': {ex.Message}"); }
                        }
                        if (halftone)
                        {
                            var ogs = new OverrideGraphicSettings();
                            ogs.SetHalftone(true);
                            view.SetElementOverrides(link.Id, ogs);
                        }
                    }
                }
                catch (Exception ex) { r.Warnings.Add($"Link override '{kv.Key}': {ex.Message}"); }
            }
        }

        /// <summary>DTW-127 — does pack key <paramref name="key"/> name this link instance?
        /// Compared against the instance name, the RevitLinkType name, and the linked file
        /// name with and without its extension (and with the ".rvt : n : location" suffix
        /// an instance name carries cut off).</summary>
        private static bool LinkMatches(Document doc, RevitLinkInstance link, string key)
        {
            if (link == null || string.IsNullOrWhiteSpace(key)) return false;
            string k = key.Trim();
            var names = new List<string> { link.Name };
            int colon = (link.Name ?? "").IndexOf(" : ", StringComparison.Ordinal);
            if (colon > 0) names.Add(link.Name.Substring(0, colon));
            try
            {
                if (doc.GetElement(link.GetTypeId()) is RevitLinkType lt)
                {
                    names.Add(lt.Name);
                    try
                    {
                        var ext = lt.GetExternalFileReference();
                        var path = ext == null ? null : ModelPathUtils.ConvertModelPathToUserVisiblePath(ext.GetAbsolutePath());
                        if (!string.IsNullOrEmpty(path)) names.Add(System.IO.Path.GetFileName(path));
                    }
                    catch (Exception ex) { StingLog.Warn($"ApplyLinkOverrides file name of '{lt.Name}': {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ApplyLinkOverrides type of link {link.Id}: {ex.Message}"); }

            foreach (var n in names)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                string t = n.Trim();
                if (string.Equals(t, k, StringComparison.OrdinalIgnoreCase)) return true;
                if (t.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(t.Substring(0, t.Length - 4), k, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // ── Phase 137 — Color-fill schemes ──
        // Shape: { "<category name>": "<color fill scheme name>" }.
        internal static void ApplyColorFillSchemes(Document doc, View view, ViewStylePack pack, PackApplyResult r)
        {
            if (pack?.ColorFillSchemes == null) return;
            Dictionary<string, string> spec = null;
            try
            {
                var tok = pack.ColorFillSchemes as Newtonsoft.Json.Linq.JToken
                          ?? Newtonsoft.Json.Linq.JToken.FromObject(pack.ColorFillSchemes);
                if (tok is Newtonsoft.Json.Linq.JObject jo)
                    spec = jo.ToObject<Dictionary<string, string>>();
            }
            catch (Exception ex) { r.Warnings.Add($"ApplyColorFillSchemes parse: {ex.Message}"); return; }
            if (spec == null || spec.Count == 0) return;
            if (!(view is ViewPlan vp))
            {
                r.Warnings.Add("Pack declares colorFillSchemes but view is not a plan — skipped.");
                return;
            }
            var schemes = new FilteredElementCollector(doc)
                .OfClass(typeof(ColorFillScheme)).Cast<ColorFillScheme>().ToList();
            foreach (var kv in spec)
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

        // ── Phase 137 — Pack-level filter enable flag ──
        // FilterEnabled defaults true; when explicitly false the pack
        // disables every filter already attached to the view.
        internal static void ApplyFilterEnabled(Document doc, View view, ViewStylePack pack, PackApplyResult r)
        {
            if (pack == null || view == null) return;
            if (pack.FilterEnabled) return; // enabled — nothing to do
            try
            {
                foreach (var fid in view.GetFilters())
                {
                    try { view.SetFilterVisibility(fid, true); view.SetIsFilterEnabled(fid, false); }
                    catch (Exception ex) { r.Warnings.Add($"FilterEnabled disable {fid}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { r.Warnings.Add($"ApplyFilterEnabled: {ex.Message}"); }
        }

        // ── C4 — material-class filter cache + factory ──
        // DTW-125: each entry remembers the material ids its rules were built from. A hit
        // whose class has since gained or lost a material is rebuilt, not returned as is —
        // the cached filter used to keep matching the class as it was at first use for
        // the rest of the session. AecFilters_Reload and Sync Styles also clear it.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (ElementId Id, string Materials)> _matClassFilterCache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, (ElementId Id, string Materials)>(StringComparer.OrdinalIgnoreCase);

        public static void InvalidateMaterialClassFilterCache() => _matClassFilterCache.Clear();

        private static ParameterFilterElement EnsureMaterialClassFilter(Document doc, string className)
        {
            try
            {
                string filterName = ProductionEdgeDecisions.MaterialClassFilterName(className);   // DTW-227: one name rule
                string cacheKey = (doc?.PathName ?? doc?.Title ?? "_") + "|" + className;

                var matIds = new FilteredElementCollector(doc).OfClass(typeof(Material))
                    .Cast<Material>()
                    .Where(m => string.Equals(m.MaterialClass ?? "", className, StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.Id)
                    .ToList();
                string signature = string.Join(",", matIds.Select(id => id.Value).OrderBy(v => v));

                if (_matClassFilterCache.TryGetValue(cacheKey, out var cached) &&
                    cached.Id != null && cached.Id.Value > 0 &&
                    string.Equals(cached.Materials, signature, StringComparison.Ordinal) &&
                    doc.GetElement(cached.Id) is ParameterFilterElement cachedPfe &&
                    string.Equals(cachedPfe.Name, filterName, StringComparison.OrdinalIgnoreCase))
                {
                    return cachedPfe;
                }

                var existing = new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>()
                    .FirstOrDefault(f => string.Equals(f.Name, filterName, StringComparison.OrdinalIgnoreCase));

                if (matIds.Count == 0) { if (existing != null) _matClassFilterCache[cacheKey] = (existing.Id, signature); return existing; }

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

                var rules = new List<FilterRule>();
                ElementId matParam = new ElementId(BuiltInParameter.MATERIAL_ID_PARAM);
                foreach (var mid in matIds)
                {
                    try { rules.Add(ParameterFilterRuleFactory.CreateEqualsRule(matParam, mid)); }
                    catch (Exception ex) { StingTools.Core.StingLog.WarnRateLimited("MatClassFilter.Rule", $"Rule build: {ex.Message}"); }
                }
                if (rules.Count == 0) return existing;

                // One ElementParameterFilter per rule, OR-ed together.
                // ElementParameterFilter's second argument is `inverted`,
                // not "use OR" — multiple FilterRules handed to a single
                // ElementParameterFilter are AND-ed. This filter asks
                // "material is any of these N materials in the class", so
                // the AND form (material == A AND material == B) could
                // never match anything once a class had more than one
                // material. Mirrors AecFilterFactory.BuildFilter.
                ElementFilter elemFilter = rules.Count == 1
                    ? (ElementFilter)new ElementParameterFilter(rules[0])
                    : new LogicalOrFilter(rules.Select(r => (ElementFilter)new ElementParameterFilter(r)).ToList());

                ParameterFilterElement built;
                if (existing == null)
                {
                    built = ParameterFilterElement.Create(doc, filterName, cats, elemFilter);
                }
                else
                {
                    try { existing.SetCategories(cats); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    try { existing.SetElementFilter(elemFilter); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    built = existing;
                }
                if (built != null) _matClassFilterCache[cacheKey] = (built.Id, signature);
                return built;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"EnsureMaterialClassFilter '{className}': {ex.Message}");
                return null;
            }
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
            catch (Exception ex)
            {
                // V-10: callers turn InvalidElementId into "category not found",
                // which is wrong when the lookup THREW. Log the real cause
                // (rate-limited: this runs per pack key per view).
                StingTools.Core.StingLog.WarnRateLimited("ViewStylePack.ResolveCategoryId",
                    $"ViewStylePackApplier: category lookup for '{key}' threw -- reported as not found: {ex.Message}");
            }
            return ElementId.InvalidElementId;
        }

        /// <summary>
        /// DTW-169: a subcategory by name, under <paramref name="parent"/> when given
        /// (BuiltInCategory or name), else under any category.
        /// </summary>
        private static ElementId ResolveSubCategoryId(Document doc, string parent, string sub)
        {
            if (doc == null || string.IsNullOrWhiteSpace(sub)) return ElementId.InvalidElementId;
            var subName = sub.Trim().Trim('<', '>').Trim();
            try
            {
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    var parentId = ResolveCategoryId(doc, parent);
                    var parentCat = parentId == ElementId.InvalidElementId ? null : Category.GetCategory(doc, parentId);
                    if (parentCat == null) return ElementId.InvalidElementId;
                    foreach (Category s in parentCat.SubCategories)
                        if (string.Equals(s.Name, subName, StringComparison.OrdinalIgnoreCase)) return s.Id;
                    return ElementId.InvalidElementId;
                }
                foreach (Category c in doc.Settings.Categories)
                    foreach (Category s in c.SubCategories)
                        if (string.Equals(s.Name, subName, StringComparison.OrdinalIgnoreCase)) return s.Id;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.WarnRateLimited("ViewStylePack.ResolveSubCategoryId",
                    $"ViewStylePackApplier: subcategory lookup '{parent}' / '{sub}' threw — reported as not found: {ex.Message}");
            }
            return ElementId.InvalidElementId;
        }

        private static ElementId ResolveLinePattern(Document doc, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ElementId.InvalidElementId;
            var trimmed = name.Trim().Trim('<', '>').Trim();
            if (trimmed.Equals("Solid", StringComparison.OrdinalIgnoreCase))
                return LinePatternElement.GetSolidPatternId();
            return new FilteredElementCollector(doc)
                .OfClass(typeof(LinePatternElement))
                .Cast<LinePatternElement>()
                .FirstOrDefault(lp => string.Equals(lp.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                ?.Id ?? ElementId.InvalidElementId;
        }

        // DTW-165: pattern misses used to be skipped without a word. Each missing
        // name is reported once per document per session — once is enough to act
        // on, and a batch would otherwise repeat it per filter per view.
        private static readonly HashSet<string> _patternMissReported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static void ReportPatternMissOnce(Document doc, string kind, string name, string subject, PackApplyResult r)
        {
            string key = (doc?.PathName ?? doc?.Title ?? "_") + "|" + kind + "|" + name;
            lock (_patternMissReported) { if (!_patternMissReported.Add(key)) return; }
            string hint = kind == "fill"
                ? " Run Create Fill Patterns (STING - … patterns) or load a pattern of that name."
                : " Load a line pattern of that name.";
            r?.Warnings.Add($"{subject}: {kind} pattern '{name}' is not in this project — not applied (reported once).{hint}");
            StingLog.Warn($"ViewStylePackApplier: {kind} pattern '{name}' not found ({subject}).");
        }

        /// <summary>Resolve and set a line pattern; a miss is reported once.</summary>
        internal static void ApplyLinePattern(Document doc, string name, Action<ElementId> setter, string subject, PackApplyResult r)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var pid = ResolveLinePattern(doc, name);
            if (pid == ElementId.InvalidElementId) { ReportPatternMissOnce(doc, "line", name, subject, r); return; }
            try { setter(pid); }
            catch (Exception ex) { r?.Warnings.Add($"{subject}: line pattern '{name}' rejected by Revit ({ex.Message})."); }
        }

        /// <summary>
        /// Set one fill slot (colour + pattern + visible). A colour with no pattern
        /// means solid (<see cref="FillPatternNames.EffectivePattern"/>).
        /// </summary>
        internal static void ApplyFill(Document doc, string color, string pattern,
            Action<Autodesk.Revit.DB.Color> setColor, Action<ElementId> setPattern, Action setVisible,
            string subject, string slot, PackApplyResult r)
        {
            if (!string.IsNullOrEmpty(color))
            {
                try { setColor(HexColor(color)); }
                catch (Exception ex) { r?.Warnings.Add($"{subject}: {slot} colour '{color}' rejected ({ex.Message})."); }
            }
            var effective = FillPatternNames.EffectivePattern(pattern, color);
            if (string.IsNullOrEmpty(effective)) return;
            var fid = ResolveFillPattern(doc, effective);
            if (fid == ElementId.InvalidElementId) { ReportPatternMissOnce(doc, "fill", effective, subject, r); return; }
            try
            {
                setPattern(fid);
                SafeWrite.Try(setVisible, "ViewStylePack.Pattern", $"{slot} pattern on", r?.Warnings);
            }
            catch (Exception ex) { r?.Warnings.Add($"{subject}: {slot} pattern '{effective}' rejected by Revit ({ex.Message})."); }
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

        /// <summary>DTW-10: drop the filter / fill-pattern resolver indexes for every
        /// document. This was an empty stub claiming no static state existed, so
        /// AecFilters_Create and AecFilters_Reload — its only callers — left the
        /// per-document index serving ids from before their run.</summary>
        public static void InvalidateCache() => InvalidateAllResolverCaches();

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
                    catch (Exception exCat)
                    {
                        // V-10: an inaccessible category is skipped, but a snapshot
                        // that silently drops categories reads as "no override" --
                        // say which (rate-limited: one line per category at most).
                        StingTools.Core.StingLog.WarnRateLimited("ViewStylePack.ReadCategoryOverrides",
                            $"ReadCategoryOverrides: category '{cat?.Name}' skipped -- not in snapshot: {exCat.Message}");
                    }
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
            // DTW-218: as DTW-173 for Apply - a template that controls the view's model
            // (or annotation) category V/G masks an override written to the view, so it
            // never shows. Such rows are not written and not counted; one warning says so.
            var masked = TemplateControlledVg(doc, view);
            int maskedRows = 0;
            foreach (var o in overrides)
            {
                if (o == null) continue;
                // DTW-169: a subcategory row (subCategory set, category optional)
                // used to be dropped because only Category was read.
                string label = string.IsNullOrWhiteSpace(o.SubCategory)
                    ? o.Category
                    : (string.IsNullOrWhiteSpace(o.Category) ? o.SubCategory : $"{o.Category} : {o.SubCategory}");
                if (string.IsNullOrWhiteSpace(label)) continue;
                try
                {
                    var catId = string.IsNullOrWhiteSpace(o.SubCategory)
                        ? ResolveCategoryId(doc, o.Category)
                        : ResolveSubCategoryId(doc, o.Category, o.SubCategory);
                    if (catId == ElementId.InvalidElementId)
                    {
                        r.Warnings.Add($"PresetOverride: category '{label}' not found.");
                        continue;
                    }
                    if (masked.Count > 0 && IsMaskedCategory(doc, catId, masked)) { maskedRows++; continue; }
                    if (o.Visible.HasValue)
                        SafeWrite.Try(() => view.SetCategoryHidden(catId, !o.Visible.Value),
                            "PresetOverride.Visibility", $"'{label}' in view '{view.Name}'", r.Warnings);

                    var ogs = view.GetCategoryOverrides(catId) ?? new OverrideGraphicSettings();
                    if (o.Halftone.HasValue) ogs.SetHalftone(o.Halftone.Value);

                    // Weights go through ApplyWeight: 0 is "not stated", out-of-range
                    // is reported, and neither throws away the rest of the override.
                    ApplyWeight(o.ProjLineWeight, w => ogs.SetProjectionLineWeight(w), label, "projLineWeight", r);
                    if (!string.IsNullOrEmpty(o.ProjLineColor)) ogs.SetProjectionLineColor(HexColor(o.ProjLineColor));
                    ApplyLinePattern(doc, o.ProjLinePattern, id => ogs.SetProjectionLinePatternId(id), label, r);
                    ApplyWeight(o.CutLineWeight, w => ogs.SetCutLineWeight(w), label, "cutLineWeight", r);
                    if (!string.IsNullOrEmpty(o.CutLineColor)) ogs.SetCutLineColor(HexColor(o.CutLineColor));
                    ApplyLinePattern(doc, o.CutLinePattern, id => ogs.SetCutLinePatternId(id), label, r);

                    ApplyFill(doc, o.SurfFgColor, o.SurfFgPattern,
                        c => ogs.SetSurfaceForegroundPatternColor(c), id => ogs.SetSurfaceForegroundPatternId(id),
                        () => ogs.SetSurfaceForegroundPatternVisible(true), label, "surface foreground", r);
                    ApplyFill(doc, o.SurfBgColor, o.SurfBgPattern,
                        c => ogs.SetSurfaceBackgroundPatternColor(c), id => ogs.SetSurfaceBackgroundPatternId(id),
                        () => ogs.SetSurfaceBackgroundPatternVisible(true), label, "surface background", r);
                    ApplyFill(doc, o.CutFgColor, o.CutFgPattern,
                        c => ogs.SetCutForegroundPatternColor(c), id => ogs.SetCutForegroundPatternId(id),
                        () => ogs.SetCutForegroundPatternVisible(true), label, "cut foreground", r);
                    ApplyFill(doc, o.CutBgColor, o.CutBgPattern,
                        c => ogs.SetCutBackgroundPatternColor(c), id => ogs.SetCutBackgroundPatternId(id),
                        () => ogs.SetCutBackgroundPatternVisible(true), label, "cut background", r);
                    // Explicit pattern-visibility flags win over the "on" the fills set.
                    if (o.SurfFgVisible.HasValue)
                        SafeWrite.Try(() => ogs.SetSurfaceForegroundPatternVisible(o.SurfFgVisible.Value), "PresetOverride.Pattern", $"'{label}' surface fg visible", r.Warnings);
                    if (o.SurfBgVisible.HasValue)
                        SafeWrite.Try(() => ogs.SetSurfaceBackgroundPatternVisible(o.SurfBgVisible.Value), "PresetOverride.Pattern", $"'{label}' surface bg visible", r.Warnings);
                    if (o.CutFgVisible.HasValue)
                        SafeWrite.Try(() => ogs.SetCutForegroundPatternVisible(o.CutFgVisible.Value), "PresetOverride.Pattern", $"'{label}' cut fg visible", r.Warnings);

                    if (o.Transparency.HasValue) ogs.SetSurfaceTransparency(Clamp(o.Transparency.Value, 0, 100));
                    if (!string.IsNullOrEmpty(o.DetailLevel))
                    {
                        if (Enum.TryParse<ViewDetailLevel>(o.DetailLevel, true, out var dl))
                            SafeWrite.Try(() => ogs.SetDetailLevel(dl), "PresetOverride.DetailLevel", $"'{label}' detail level", r.Warnings);
                        else
                            r.Warnings.Add($"PresetOverride '{label}': detail level '{o.DetailLevel}' is not Coarse / Medium / Fine — ignored.");
                    }
                    view.SetCategoryOverrides(catId, ogs);
                    r.OverridesSet++;
                }
                catch (Exception ex) { r.Warnings.Add($"PresetOverride '{label}': {ex.Message}"); }
            }
            if (maskedRows > 0)
                r.Warnings.Add($"Preset VG overrides on '{view.Name}': the view's template controls category "
                    + $"overrides, so {maskedRows} preset override(s) would be masked and were not written. "
                    + "Put them in the drawing type's style pack, or release V/G in the template, for them to show.");
        }

        /// <summary>DTW-218: is this category's V/G (model or annotation, by the category
        /// or its parent) controlled by the view's template?</summary>
        private static bool IsMaskedCategory(Document doc, ElementId catId, HashSet<BuiltInParameter> masked)
        {
            try
            {
                var cat = Category.GetCategory(doc, catId);
                var root = cat?.Parent ?? cat;
                bool annotation = root != null && root.CategoryType == CategoryType.Annotation;
                return masked.Contains(annotation ? BuiltInParameter.VIS_GRAPHICS_ANNOTATION
                                                  : BuiltInParameter.VIS_GRAPHICS_MODEL);
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("ViewStylePack.PresetMask",
                    $"ViewStylePackApplier: category type read failed - writing the preset override: {ex.Message}");
                return false;
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
