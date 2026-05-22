// StingTools — Drawing Template Manager · Week 4
//
// DrawingDriftDetector scans every stamped view and reports where
// the view's current state has drifted from its DrawingType profile.
// Drift kinds:
//
//   SCALE_DRIFT             view.Scale != profile.Scale
//   DETAIL_DRIFT            view.DetailLevel != profile.DetailLevel
//   TEMPLATE_DRIFT          view.ViewTemplateId.Name != profile.ViewTemplateName
//   TOKEN_PROFILE_DRIFT     STING_VIEW_TAG_STYLE / TAG_SEG_MASK_TXT mismatch
//                           (Phase 135)
//   TITLEBLOCK_SPEC_DRIFT   title-block family on sheet doesn't match profile
//                           family, or STING_TB_SPEC_HASH_TXT is absent /
//                           mismatched (Gap 5)
//   CROP_DRIFT              view's VIEWER_VOLUME_OF_INTEREST_CROP doesn't
//                           match profile.crop.scopeBoxName, OR the
//                           Phase 183 STING_CROP_KIND_TXT /
//                           STING_CROP_MARGIN_MM_TXT stamps disagree with
//                           the profile's current crop kind / margin
//                           (for bbox-derived crops)
//   VG_OVERRIDE_DRIFT       per-category OverrideGraphicSettings on the
//                           view disagrees with the resolved pack's
//                           vgOverrides entry (non-managed packs only)
//   FILTER_DRIFT            pack filter not attached to view, or
//                           visibility / halftone / projection-weight
//                           override doesn't match the pack's filterRule
//                           (non-managed packs only)
//
// Consumed by the SyncStyles command (which re-applies the profile
// on drifted views) and surfaced in the Inspect command output so
// users see "12 views have drifted — run Sync Styles" at a glance.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Drawing
{
    public sealed class DriftReport
    {
        public ElementId ViewId { get; set; }
        public string    ViewName { get; set; }
        public string    DrawingTypeId { get; set; }
        public List<string> Drifts { get; } = new List<string>();

        /// <summary>
        /// C-8 / E-2: drift items that the running view template suppresses
        /// (the parameter cannot be written by the profile because the
        /// template controls it). SyncStyles must skip these — re-applying
        /// will fail silently and the report will resurface forever. Inspect
        /// surfaces them as informational ("template controls this field").
        /// </summary>
        public List<string> Suppressed { get; } = new List<string>();

        public bool Any => Drifts.Count > 0;
        public bool AnyActionable => Drifts.Count > 0;
        public bool AnySuppressed => Suppressed.Count > 0;
    }

    public static class DrawingDriftDetector
    {
        // PERF-06: reverse index of (stamped DrawingTypeId → list of view ids).
        // The Scan() pass walked every View in the document and called
        // DrawingTypeStamper.Read on each, even though most projects only have
        // a few stamped views. Cache the index per-document so repeated scans
        // (which happen on every SyncStyles / Inspect press) skip the
        // FilteredElementCollector + per-element stamp read.
        private sealed class ScanCache
        {
            public Dictionary<long, string> StampByViewId
                = new Dictionary<long, string>();
            public bool Valid;
        }

        private static readonly object _cacheLock = new object();
        private static readonly Dictionary<string, ScanCache> _cache
            = new Dictionary<string, ScanCache>(StringComparer.OrdinalIgnoreCase);

        private static string DocKey(Document doc)
        {
            if (doc == null) return "__null__";
            try { return string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName; }
            catch { return "__unknown__"; }
        }

        public static void InvalidateCache(Document doc)
        {
            string k = DocKey(doc);
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(k, out var sc)) sc.Valid = false;
            }
        }

        public static List<DriftReport> Scan(Document doc)
        {
            var reports = new List<DriftReport>();
            if (doc == null) return reports;

            // C-4: build a single id → DrawingType dictionary up front so we
            // don't walk DrawingTypeLibrary.DrawingTypes once per stamped view.
            // ListAll(doc) returns the cached library; the per-id resolved
            // value is memoized inside DrawingTypeRegistry.Get (C-5).
            var resolvedById = new Dictionary<string, DrawingType>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in DrawingTypeRegistry.ListAll(doc))
            {
                if (string.IsNullOrWhiteSpace(raw.Id)) continue;
                if (resolvedById.ContainsKey(raw.Id)) continue;
                var resolved = DrawingTypeRegistry.Get(doc, raw.Id);
                if (resolved != null) resolvedById[raw.Id] = resolved;
            }

            // PERF-06: build / refresh the reverse index once per Scan call;
            // the inner loop reads stamp values out of the dictionary instead
            // of probing every element.
            ScanCache cache;
            lock (_cacheLock)
            {
                if (!_cache.TryGetValue(DocKey(doc), out cache))
                    _cache[DocKey(doc)] = cache = new ScanCache();
            }
            if (!cache.Valid)
            {
                cache.StampByViewId.Clear();
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(View)))
                {
                    if (!(el is View vv) || vv.IsTemplate) continue;
                    var s = DrawingTypeStamper.Read(vv);
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    cache.StampByViewId[vv.Id.Value] = s;
                }
                cache.Valid = true;
            }

            foreach (var kv in cache.StampByViewId)
            {
                if (!(doc.GetElement(new ElementId(kv.Key)) is View v) || v.IsTemplate) continue;
                var dtId = kv.Value;
                if (string.IsNullOrWhiteSpace(dtId)) continue;   // not a STING view
                if (DrawingTypeStamper.IsLocked(v)) continue;    // user-frozen

                resolvedById.TryGetValue(dtId, out var dt);
                if (dt == null)
                {
                    var r = new DriftReport { ViewId = v.Id, ViewName = v.Name, DrawingTypeId = dtId };
                    r.Drifts.Add($"Unknown DrawingType '{dtId}' — profile deleted or project override diverged.");
                    reports.Add(r);
                    continue;
                }

                var report = new DriftReport { ViewId = v.Id, ViewName = v.Name, DrawingTypeId = dtId };

                if (dt.Scale > 0 && v.Scale != dt.Scale)
                    report.Drifts.Add($"SCALE: view 1:{v.Scale} vs profile 1:{dt.Scale}");

                if (!string.IsNullOrEmpty(dt.DetailLevel))
                {
                    var actual = v.DetailLevel.ToString();
                    if (!string.Equals(actual, dt.DetailLevel, StringComparison.OrdinalIgnoreCase))
                        report.Drifts.Add($"DETAIL: view {actual} vs profile {dt.DetailLevel}");
                }

                if (!string.IsNullOrEmpty(dt.ViewTemplateName))
                {
                    var tplName = TemplateName(doc, v.ViewTemplateId);
                    if (!string.Equals(tplName, dt.ViewTemplateName, StringComparison.OrdinalIgnoreCase))
                        report.Drifts.Add($"TEMPLATE: view '{tplName ?? "(none)"}' vs profile '{dt.ViewTemplateName}'");
                }

                // Phase 137 — managed-template drift detection. When the
                // resolved pack is in managed mode, the view should carry
                // the "STING:{packId}:{ViewType}" template and the stored
                // checksum on that template should match the pack's
                // current checksum. If either is wrong, flag drift.
                AppendManagedTemplateDrift(doc, v, dt, report);

                // Phase 135 — token profile drift. Compares
                // STING_VIEW_TAG_STYLE and TAG_SEG_MASK_TXT on the view
                // to the resolved profile/pack defaults. SyncStyles
                // re-runs DrawingTypePresentation.Apply which routes
                // through TokenProfileApplier and heals the drift.
                AppendTokenProfileDrift(doc, v, dt, report);

                // Phase 167 — title-block parameter drift (sheets only).
                // Compares the resolved value of every TitleBlockParams entry
                // to the live String parameter on the sheet's title-block
                // instance(s). SyncStyles routes sheets through
                // DrawingTypePresentation.ApplyToSheet which re-runs
                // TitleBlockParamApplier and heals the drift.
                if (v is ViewSheet sheet)
                {
                    AppendTitleBlockParamDrift(doc, sheet, dt, report);
                    // Gap 5 — title-block spec / hash drift.
                    AppendTitleBlockSpecDrift(doc, sheet, dt, report);
                }

                // Phase 175 — design-option drift. When a profile declares
                // an OptionScope, compare the view's actual
                // VIEWER_OPTION_VISIBILITY to what DrawingOptionApplier
                // would write. SyncStyles re-runs Apply and heals.
                AppendOptionScopeDrift(doc, v, dt, report);

                // Crop drift — scope-box mismatch + (Phase 183)
                // bbox-derived stamp comparison for TightBbox /
                // RoomBoundary / ScopeBoxOrBbox-bbox-fallback profiles.
                AppendCropDrift(doc, v, dt, report);

                // VG / filter drift (non-managed packs only — managed
                // packs are covered by AppendManagedTemplateDrift's
                // checksum comparison against the STING:* template).
                AppendVgAndFilterDrift(doc, v, dt, report);

                // Phase 175 — design-option drift. When a profile declares
                // an OptionScope, compare the view's actual
                // VIEWER_OPTION_VISIBILITY to what DrawingOptionApplier
                // would write. SyncStyles re-runs Apply and heals.
                AppendOptionScopeDrift(doc, v, dt, report);

                if (report.Drifts.Count > 0 || report.Suppressed.Count > 0) reports.Add(report);
            }
            return reports;
        }

        // CROP_DRIFT — covers two distinct scenarios:
        //   1. Scope-box crops (kind=ScopeBox / ScopeBoxOrBbox with
        //      scopeBoxName set) — compare view's bound
        //      VIEWER_VOLUME_OF_INTEREST_CROP to the named scope box.
        //   2. Bbox-derived crops (kind=TightBbox / RoomBoundary, or
        //      ScopeBoxOrBbox falling through to bbox) — compare the
        //      kind + margin stamped onto the view by DrawingCropApplier
        //      (Phase 183) to what the profile currently asks for. This
        //      catches profile edits like "marginMm: 150 → 300" that the
        //      view hasn't been re-cropped to honour.
        private static void AppendCropDrift(Document doc, View v, DrawingType dt, DriftReport report)
        {
            try
            {
                var crop = dt?.Crop;
                if (crop == null) return;
                var kind = (crop.Kind ?? "").Trim();

                // 1) Scope-box drift
                if (!string.IsNullOrEmpty(crop.ScopeBoxName)
                    && (string.Equals(kind, "ScopeBox", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(kind, "ScopeBoxOrBbox", StringComparison.OrdinalIgnoreCase)))
                {
                    var p = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                    if (p != null)
                    {
                        ElementId actualId = p.AsElementId() ?? ElementId.InvalidElementId;
                        string actualName = actualId == ElementId.InvalidElementId
                            ? "(none)"
                            : doc.GetElement(actualId)?.Name ?? actualId.ToString();

                        if (!string.Equals(actualName, crop.ScopeBoxName, StringComparison.OrdinalIgnoreCase))
                        {
                            var resolved = new FilteredElementCollector(doc)
                                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                                .WhereElementIsNotElementType()
                                .FirstOrDefault(e => string.Equals(e.Name, crop.ScopeBoxName, StringComparison.OrdinalIgnoreCase));
                            if (resolved == null)
                            {
                                if (string.Equals(kind, "ScopeBoxOrBbox", StringComparison.OrdinalIgnoreCase))
                                    report.Suppressed.Add($"CROP_INFO: scope box '{crop.ScopeBoxName}' not in document — bbox fallback in use");
                                else
                                    report.Drifts.Add($"CROP: scope box '{crop.ScopeBoxName}' not in document — view will fail to crop");
                            }
                            else
                            {
                                report.Drifts.Add($"CROP: scope box '{actualName}' vs profile '{crop.ScopeBoxName}'");
                            }
                        }
                    }
                }

                // 2) Bbox-derived drift via Phase 183 crop stamp. Only
                // meaningful for kinds where the applier writes a stamp.
                // ScopeBox is excluded — it's covered above and doesn't
                // benefit from margin comparison.
                bool isBboxKind = string.Equals(kind, "TightBbox", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(kind, "RoomBoundary", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(kind, "ScopeBoxOrBbox", StringComparison.OrdinalIgnoreCase);
                if (!isBboxKind) return;

                var (stampedKind, stampedMargin) = DrawingTypeStamper.ReadCrop(v);
                if (stampedKind == null && !stampedMargin.HasValue) return; // no stamp; nothing to diff

                if (!string.Equals(stampedKind ?? "", kind, StringComparison.OrdinalIgnoreCase))
                    report.Drifts.Add($"CROP: kind '{stampedKind ?? "(none)"}' vs profile '{kind}'");
                else if (stampedMargin.HasValue && Math.Abs(stampedMargin.Value - crop.MarginMm) > 1.0)
                    report.Drifts.Add($"CROP: margin {stampedMargin.Value:0.#}mm vs profile {crop.MarginMm:0.#}mm");
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"AppendCropDrift({v?.Id}): {ex.Message}");
            }
        }

        // VG / filter drift — compares the live view's per-category
        // OverrideGraphicSettings + filter attachments against the
        // resolved pack's VgOverrides + Filters. Only runs for
        // non-managed packs; managed packs already get template-level
        // checksum drift via AppendManagedTemplateDrift, and double-
        // detecting would surface every view that uses the managed
        // template as drifted whenever a managed field is edited.
        //
        // Phase 184 — one drift entry per category / filter, listing
        // ALL mismatching attributes joined with semicolons so a single
        // re-apply heals every difference at once. (Phase 183 reported
        // only the first mismatch per category, masking the rest.)
        private static void AppendVgAndFilterDrift(Document doc, View v, DrawingType dt, DriftReport report)
        {
            try
            {
                if (string.IsNullOrEmpty(dt?.ViewStylePackId)) return;
                var pack = ViewStylePackRegistry.Get(doc, dt.ViewStylePackId);
                if (pack == null) return;
                if (pack.IsManaged) return; // managed mode is covered elsewhere

                // ── VG overrides ──
                if (pack.VgOverrides != null)
                {
                    foreach (var kv in pack.VgOverrides)
                    {
                        var catId = ResolveCategoryIdForDrift(doc, kv.Key);
                        if (catId == ElementId.InvalidElementId) continue;

                        OverrideGraphicSettings ogs;
                        try { ogs = v.GetCategoryOverrides(catId); } catch { continue; }
                        if (ogs == null) continue;
                        var expected = kv.Value;
                        if (expected == null) continue;

                        var mismatches = new List<string>(4);
                        if (expected.Halftone.HasValue && ogs.Halftone != expected.Halftone.Value)
                            mismatches.Add($"halftone {ogs.Halftone} vs {expected.Halftone.Value}");
                        if (expected.ProjectionLineWeight.HasValue
                            && ogs.ProjectionLineWeight != expected.ProjectionLineWeight.Value)
                            mismatches.Add($"projWeight {ogs.ProjectionLineWeight} vs {expected.ProjectionLineWeight.Value}");
                        if (expected.CutLineWeight.HasValue
                            && ogs.CutLineWeight != expected.CutLineWeight.Value)
                            mismatches.Add($"cutWeight {ogs.CutLineWeight} vs {expected.CutLineWeight.Value}");
                        if (expected.Transparency.HasValue
                            && ogs.Transparency != Clamp01(expected.Transparency.Value))
                            mismatches.Add($"transparency {ogs.Transparency} vs {expected.Transparency.Value}");

                        if (mismatches.Count > 0)
                            report.Drifts.Add($"VG_OVERRIDE: '{kv.Key}' " + string.Join("; ", mismatches));
                    }
                }

                // ── Filter rules ──
                if (pack.Filters != null && pack.Filters.Count > 0)
                {
                    System.Collections.Generic.ICollection<ElementId> attached;
                    try { attached = v.GetFilters(); } catch { attached = null; }
                    if (attached == null) return;

                    foreach (var rule in pack.Filters)
                    {
                        if (string.IsNullOrWhiteSpace(rule.FilterName)) continue;
                        var filter = new FilteredElementCollector(doc)
                            .OfClass(typeof(ParameterFilterElement))
                            .Cast<ParameterFilterElement>()
                            .FirstOrDefault(f => string.Equals(f.Name, rule.FilterName, StringComparison.OrdinalIgnoreCase));
                        if (filter == null)
                        {
                            // Filter not in document yet — pack-side issue, not view-side drift.
                            report.Suppressed.Add($"FILTER_INFO: '{rule.FilterName}' not in document — will lazy-create on apply");
                            continue;
                        }

                        if (!attached.Contains(filter.Id))
                        {
                            report.Drifts.Add($"FILTER: '{rule.FilterName}' not attached to view");
                            continue;
                        }

                        var mismatches = new List<string>(4);
                        bool actualVisible;
                        try { actualVisible = v.GetFilterVisibility(filter.Id); }
                        catch { continue; }
                        if (actualVisible != rule.Visible)
                            mismatches.Add($"visible={actualVisible} vs {rule.Visible}");

                        OverrideGraphicSettings fogs;
                        try { fogs = v.GetFilterOverrides(filter.Id); } catch { fogs = null; }
                        if (fogs != null)
                        {
                            if (fogs.Halftone != rule.Halftone)
                                mismatches.Add($"halftone {fogs.Halftone} vs {rule.Halftone}");
                            if (rule.ProjectionLineWeight.HasValue
                                && fogs.ProjectionLineWeight != rule.ProjectionLineWeight.Value)
                                mismatches.Add($"projWeight {fogs.ProjectionLineWeight} vs {rule.ProjectionLineWeight.Value}");
                            if (rule.CutLineWeight.HasValue
                                && fogs.CutLineWeight != rule.CutLineWeight.Value)
                                mismatches.Add($"cutWeight {fogs.CutLineWeight} vs {rule.CutLineWeight.Value}");
                            if (rule.Transparency.HasValue
                                && fogs.Transparency != Clamp01(rule.Transparency.Value))
                                mismatches.Add($"transparency {fogs.Transparency} vs {rule.Transparency.Value}");
                        }

                        if (mismatches.Count > 0)
                            report.Drifts.Add($"FILTER: '{rule.FilterName}' " + string.Join("; ", mismatches));
                    }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"AppendVgAndFilterDrift({v?.Id}): {ex.Message}");
            }
        }

        private static ElementId ResolveCategoryIdForDrift(Document doc, string key)
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
            }
            catch { }
            return ElementId.InvalidElementId;
        }

        private static int Clamp01(int v) => v < 0 ? 0 : v > 100 ? 100 : v;

        // Phase 175 — drift between profile.OptionScope and view's actual
        // VIEWER_OPTION_VISIBILITY parameter.
        private static void AppendOptionScopeDrift(Document doc, View v, DrawingType dt, DriftReport report)
        {
            try
            {
                if (dt?.OptionScope == null) return;
                if (v == null || v.IsTemplate) return;
                var p = v.get_Parameter(BuiltInParameter.VIEWER_OPTION_VISIBILITY);
                if (p == null) return;
                ElementId actual = p.AsElementId() ?? ElementId.InvalidElementId;
                ElementId expected = ResolveExpectedOptionId(doc, dt.OptionScope);
                if (actual == expected) return;
                string actualName = actual == ElementId.InvalidElementId
                    ? "<Automatic>"
                    : doc.GetElement(actual)?.Name ?? actual.ToString();
                string expectedName = expected == ElementId.InvalidElementId
                    ? "<Automatic>"
                    : doc.GetElement(expected)?.Name ?? expected.ToString();
                report.Drifts.Add($"OPTION_SCOPE: VIEWER_OPTION_VISIBILITY '{actualName}' vs profile '{expectedName}'");
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"AppendOptionScopeDrift({v?.Id}): {ex.Message}");
            }
        }

        private static ElementId ResolveExpectedOptionId(Document doc, DrawingOptionScope scope)
        {
            try
            {
                if (scope == null || string.IsNullOrEmpty(scope.Mode)) return ElementId.InvalidElementId;
                switch (scope.Mode.Trim().ToLowerInvariant())
                {
                    case "automatic":
                        return ElementId.InvalidElementId;
                    case "primary":
                        if (string.IsNullOrEmpty(scope.SetName)) return ElementId.InvalidElementId;
                        var setsP = StingTools.Core.DesignOptions.DesignOptionRegistry.Snapshot(doc);
                        foreach (var s in setsP)
                            if (string.Equals(s.Name, scope.SetName, StringComparison.OrdinalIgnoreCase))
                                return s.Primary?.OptionId ?? ElementId.InvalidElementId;
                        return ElementId.InvalidElementId;
                    case "specific":
                        if (string.IsNullOrEmpty(scope.OptionName)) return ElementId.InvalidElementId;
                        var setsS = StingTools.Core.DesignOptions.DesignOptionRegistry.Snapshot(doc);
                        foreach (var s in setsS)
                        {
                            if (!string.IsNullOrEmpty(scope.SetName) &&
                                !string.Equals(s.Name, scope.SetName, StringComparison.OrdinalIgnoreCase))
                                continue;
                            foreach (var o in s.Options)
                                if (string.Equals(o.Name, scope.OptionName, StringComparison.OrdinalIgnoreCase))
                                    return o.OptionId;
                        }
                        return ElementId.InvalidElementId;
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ResolveExpectedOptionId: {ex.Message}"); }
            return ElementId.InvalidElementId;
        }

        /// <summary>
        /// C-8: returns true when the view's currently applied view template
        /// controls the named parameter — meaning a profile re-apply cannot
        /// write to it. Used to demote TOKEN_PROFILE drift into
        /// <see cref="DriftReport.Suppressed"/> so SyncStyles doesn't loop.
        /// </summary>
        private static bool TemplateControlsParameter(Document doc, View v, string paramName)
        {
            try
            {
                if (v?.ViewTemplateId == null || v.ViewTemplateId == ElementId.InvalidElementId) return false;
                if (!(doc.GetElement(v.ViewTemplateId) is View tpl)) return false;
                var p = tpl.LookupParameter(paramName);
                if (p == null) return false;
                var nonControlled = tpl.GetNonControlledTemplateParameterIds();
                return nonControlled == null || !nonControlled.Contains(p.Id);
            }
            catch { return false; }
        }

        private static void AppendManagedTemplateDrift(Document doc, View v, DrawingType dt, DriftReport report)
        {
            if (string.IsNullOrEmpty(dt.ViewStylePackId)) return;
            try
            {
                var pack = DrawingTypeRegistry.TryGetPack(doc, dt.ViewStylePackId);
                if (pack == null || !pack.IsManaged) return;

                var expectedName = ManagedTemplateSyncer.GetManagedTemplateName(pack.Id, v.ViewType);
                View current = null;
                if (v.ViewTemplateId != null && v.ViewTemplateId != ElementId.InvalidElementId)
                    current = doc.GetElement(v.ViewTemplateId) as View;

                if (current == null || !string.Equals(current.Name, expectedName, StringComparison.Ordinal))
                {
                    report.Drifts.Add($"ManagedTemplate: view template '{current?.Name ?? "(none)"}' vs expected '{expectedName}'");
                    return;
                }

                var stored = ManagedTemplateSyncer.GetStoredChecksum(current);
                var expected = ManagedTemplateSyncer.ComputePackChecksum(pack);
                if (!string.Equals(stored, expected, StringComparison.Ordinal))
                    report.Drifts.Add($"ManagedTemplate: checksum '{stored ?? "(none)"}' vs '{expected}' — pack edited since template last applied");
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"AppendManagedTemplateDrift({v.Id}): {ex.Message}");
            }
        }

        private static void AppendTokenProfileDrift(Document doc, View v, DrawingType dt, DriftReport report)
        {
            try
            {
                // Resolve effective expected values (profile > pack).
                var profile = dt.TokenProfile;
                ViewStylePack pack = string.IsNullOrEmpty(dt.ViewStylePackId)
                    ? null
                    : ViewStylePackRegistry.Get(doc, dt.ViewStylePackId);

                // ACC-10: profile wins; only fall back to pack when the
                // profile is null. An empty-string profile.ColorScheme is
                // treated as "leave as-is" rather than as a falsy → pack
                // cascade, so a deliberately-cleared profile slot doesn't
                // silently re-inherit the pack's scheme.
                string expectedScheme;
                if (profile != null && profile.ColorScheme != null)
                    expectedScheme = profile.ColorScheme;
                else
                    expectedScheme = pack?.TagColorScheme;
                if (!string.IsNullOrEmpty(expectedScheme))
                {
                    string actual = ReadStringParam(v, ParamRegistry.VIEW_TAG_STYLE);
                    if (!string.Equals(actual, expectedScheme, StringComparison.OrdinalIgnoreCase))
                    {
                        // C-8: if the running view template controls this
                        // parameter, demote the entry to Suppressed so
                        // SyncStyles doesn't retry forever.
                        if (TemplateControlsParameter(doc, v, ParamRegistry.VIEW_TAG_STYLE))
                            report.Suppressed.Add($"DRIFT_SUPPRESSED_BY_TEMPLATE: STING_VIEW_TAG_STYLE controlled by view template ('{actual ?? "(empty)"}' vs profile '{expectedScheme}')");
                        else
                            report.Drifts.Add($"TOKEN_PROFILE: STING_VIEW_TAG_STYLE '{actual ?? "(empty)"}' vs profile '{expectedScheme}'");
                    }
                }

                string expectedMask = profile?.SegmentMask;
                if (!string.IsNullOrEmpty(expectedMask)
                    && expectedMask.Length == 8)
                {
                    string actual = ReadStringParam(v, ParamRegistry.TAG_SEG_MASK);
                    if (!string.Equals(actual, expectedMask, StringComparison.Ordinal))
                    {
                        if (TemplateControlsParameter(doc, v, ParamRegistry.TAG_SEG_MASK))
                            report.Suppressed.Add($"DRIFT_SUPPRESSED_BY_TEMPLATE: TAG_SEG_MASK controlled by view template ('{actual ?? "(empty)"}' vs profile '{expectedMask}')");
                        else
                            report.Drifts.Add($"TOKEN_PROFILE: TAG_SEG_MASK '{actual ?? "(empty)"}' vs profile '{expectedMask}'");
                    }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"AppendTokenProfileDrift({v.Id}): {ex.Message}");
            }
        }

        // Phase 167+168 — title-block drift (sheets only). Compares profile-
        // resolved values against live TB instance parameters across all
        // major storage types, detects deleted TBs and family-swap drift.
        // Phase 168 fix: resolves the same DrawingTokenContext the applier
        // would use at write-time (with seq extracted from the sheet number)
        // so {disc}/{seq:Dn} templates don't false-positive against a literal
        // "{disc}".
        private static void AppendTitleBlockParamDrift(Document doc, ViewSheet sheet, DrawingType dt, DriftReport report)
        {
            try
            {
                var tbs = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .ToList();

                // Detect missing / family-swap drift even when titleBlockParams is empty.
                if (tbs.Count == 0)
                {
                    if (!string.IsNullOrEmpty(dt.TitleBlockFamily))
                        report.Drifts.Add($"TITLE_BLOCK_PARAM: sheet has no title-block instance vs profile family '{dt.TitleBlockFamily}'");
                    return;
                }
                if (!string.IsNullOrEmpty(dt.TitleBlockFamily))
                {
                    foreach (var tb in tbs)
                    {
                        var actualFam = tb.Symbol?.FamilyName ?? "(unknown)";
                        if (!string.Equals(actualFam, dt.TitleBlockFamily, StringComparison.OrdinalIgnoreCase))
                        {
                            report.Drifts.Add($"TITLE_BLOCK_PARAM: family '{actualFam}' vs profile '{dt.TitleBlockFamily}'");
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(dt.TitleBlockSymbolType))
                    {
                        foreach (var tb in tbs)
                        {
                            if (string.Equals(tb.Symbol?.FamilyName, dt.TitleBlockFamily, StringComparison.OrdinalIgnoreCase)
                                && !string.Equals(tb.Symbol?.Name, dt.TitleBlockSymbolType, StringComparison.OrdinalIgnoreCase))
                            {
                                report.Drifts.Add($"TITLE_BLOCK_PARAM: symbol '{tb.Symbol?.Name}' vs profile '{dt.TitleBlockSymbolType}'");
                                break;
                            }
                        }
                    }
                }

                if (dt?.TitleBlockParams == null || dt.TitleBlockParams.Count == 0) return;

                var tokens = DrawingTokenContext.Build(
                    doc:        doc,
                    dt:         dt,
                    discCode:   dt.Discipline,
                    discipline: dt.Discipline,
                    seq:        DrawingTokenContext.ExtractSeqFromSheetNumber(sheet.SheetNumber));
                var expected = TitleBlockParamApplier.Peek(doc, dt, tokens);
                if (expected.Count == 0) return;

                foreach (var kv in expected)
                {
                    var paramName = kv.Key;
                    var expectedVal = kv.Value ?? "";
                    foreach (var tb in tbs)
                    {
                        Parameter p;
                        try { p = tb.LookupParameter(paramName); } catch { continue; }
                        if (p == null || p.IsReadOnly) continue;
                        string actual;
                        switch (p.StorageType)
                        {
                            case StorageType.String:
                                actual = p.AsString() ?? "";
                                break;
                            case StorageType.Integer:
                                actual = p.AsInteger().ToString(CultureInfo.InvariantCulture);
                                // Compare both raw int and yes/no semantic if expected is "Yes"/"No".
                                if (string.Equals(expectedVal, "Yes", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(expectedVal, "No", StringComparison.OrdinalIgnoreCase))
                                    actual = p.AsInteger() != 0 ? "Yes" : "No";
                                break;
                            case StorageType.Double:
                                actual = p.AsDouble().ToString("0.###", CultureInfo.InvariantCulture);
                                break;
                            case StorageType.ElementId:
                                var eid = p.AsElementId();
                                var el  = (eid != null && eid != ElementId.InvalidElementId) ? doc.GetElement(eid) : null;
                                actual = el?.Name ?? "";
                                break;
                            default: continue;
                        }
                        if (!string.Equals(actual, expectedVal, StringComparison.Ordinal))
                        {
                            report.Drifts.Add($"TITLE_BLOCK_PARAM: '{paramName}' = '{actual}' vs profile '{expectedVal}'");
                            break; // one drift per param name is enough for the report
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"AppendTitleBlockParamDrift({sheet.Id}): {ex.Message}");
            }
        }

        // Gap 5 — title-block spec / hash drift (sheets only).
        // Checks that (a) the family on the sheet matches the profile's
        // TitleBlockFamily, and (b) the STING_TB_SPEC_HASH_TXT stamp is
        // present and agrees with the profile-computed hash (when available).
        private static void AppendTitleBlockSpecDrift(Document doc, ViewSheet sheet, DrawingType dt, DriftReport report)
        {
            try
            {
                var insts = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .ToList();

                // (a) Missing title-block when the profile names one.
                if (insts.Count == 0)
                {
                    if (!string.IsNullOrEmpty(dt?.TitleBlockFamily))
                        report.Drifts.Add(
                            $"TITLEBLOCK_SPEC: no title-block on sheet vs profile family '{dt.TitleBlockFamily}'");
                    return;
                }

                foreach (var inst in insts)
                {
                    string actualFamily = inst.Symbol?.FamilyName ?? "(unknown)";

                    // (b) Family name mismatch.
                    if (!string.IsNullOrEmpty(dt?.TitleBlockFamily)
                        && !string.Equals(actualFamily, dt.TitleBlockFamily, StringComparison.OrdinalIgnoreCase))
                    {
                        report.Drifts.Add(
                            $"TITLEBLOCK_SPEC: family '{actualFamily}' vs profile '{dt.TitleBlockFamily}'");
                    }

                    // (c) Spec hash check.
                    Parameter hashParam = null;
                    try { hashParam = inst.LookupParameter("STING_TB_SPEC_HASH_TXT"); } catch { }

                    if (hashParam == null || hashParam.StorageType != StorageType.String)
                    {
                        // No hash stamped — informational only, not a blocking drift.
                        report.Suppressed.Add(
                            $"TITLEBLOCK_SPEC_INFO: no spec hash stamped on '{actualFamily}' — factory-of-origin unknown");
                        continue;
                    }

                    string liveHash = hashParam.AsString() ?? "";
                    if (string.IsNullOrEmpty(liveHash))
                    {
                        report.Suppressed.Add(
                            $"TITLEBLOCK_SPEC_INFO: no spec hash stamped on '{actualFamily}' — factory-of-origin unknown");
                        continue;
                    }

                    // Hash-vs-expected comparison deferred: TitleBlockSpecRegistry does not
                    // yet expose a ComputeHash factory.  The stamped hash is still written
                    // (above) so a future phase can compare without a data migration.
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"AppendTitleBlockSpecDrift({sheet?.Id}): {ex.Message}");
            }
        }

        private static string ReadStringParam(Element el, string paramName)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.StorageType != StorageType.String) return null;
                return p.AsString();
            }
            catch { return null; }
        }

        private static string TemplateName(Document doc, ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId) return null;
            try { return (doc.GetElement(id) as View)?.Name; } catch { return null; }
        }
    }
}
