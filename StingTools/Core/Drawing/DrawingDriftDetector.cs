// StingTools — Drawing Template Manager · Week 4
//
// DrawingDriftDetector scans every stamped view and reports where
// the view's current state has drifted from its DrawingType profile.
// Drift kinds:
//
//   SCALE_DRIFT          view.Scale != profile.Scale
//   DETAIL_DRIFT         view.DetailLevel != profile.DetailLevel
//   TEMPLATE_DRIFT       view.ViewTemplateId.Name != profile.ViewTemplateName
//   TOKEN_PROFILE_DRIFT  STING_VIEW_TAG_STYLE / TAG_SEG_MASK_TXT mismatch
//                        (Phase 135)
//
// Consumed by the SyncStyles command (which re-applies the profile
// on drifted views) and surfaced in the Inspect command output so
// users see "12 views have drifted — run Sync Styles" at a glance.

using System;
using System.Collections.Generic;
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
                    AppendTitleBlockParamDrift(doc, sheet, dt, report);

                if (report.Drifts.Count > 0 || report.Suppressed.Count > 0) reports.Add(report);
            }
            return reports;
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

        // Phase 167 — title-block param drift (sheets only). Resolves every
        // TitleBlockParams entry through TitleBlockParamApplier.Peek and
        // compares to the live String value on the sheet's title-block
        // instance(s). Only String storage is checked — Integer/Double
        // params are rare in title blocks and ambiguous to compare.
        private static void AppendTitleBlockParamDrift(Document doc, ViewSheet sheet, DrawingType dt, DriftReport report)
        {
            if (dt?.TitleBlockParams == null || dt.TitleBlockParams.Count == 0) return;
            try
            {
                var expected = TitleBlockParamApplier.Peek(doc, dt, tokens: null);
                if (expected.Count == 0) return;

                var tbs = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .ToList();
                if (tbs.Count == 0) return;

                foreach (var kv in expected)
                {
                    var paramName = kv.Key;
                    var expectedVal = kv.Value ?? "";
                    foreach (var tb in tbs)
                    {
                        Parameter p;
                        try { p = tb.LookupParameter(paramName); } catch { continue; }
                        if (p == null || p.IsReadOnly) continue;
                        if (p.StorageType != StorageType.String) continue;
                        var actual = p.AsString() ?? "";
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
