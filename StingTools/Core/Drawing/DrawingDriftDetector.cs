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

            foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(View)))
            {
                if (!(el is View v) || v.IsTemplate) continue;
                var dtId = DrawingTypeStamper.Read(v);
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

                string expectedScheme = profile?.ColorScheme ?? pack?.TagColorScheme;
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
