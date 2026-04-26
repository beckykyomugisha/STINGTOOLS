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

        public bool Any => Drifts.Count > 0;
    }

    public static class DrawingDriftDetector
    {
        public static List<DriftReport> Scan(Document doc)
        {
            var reports = new List<DriftReport>();
            if (doc == null) return reports;

            foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(View)))
            {
                if (!(el is View v) || v.IsTemplate) continue;
                var dtId = DrawingTypeStamper.Read(v);
                if (string.IsNullOrWhiteSpace(dtId)) continue;   // not a STING view
                if (DrawingTypeStamper.IsLocked(v)) continue;    // user-frozen

                var dt = DrawingTypeRegistry.Get(doc, dtId);
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

                // Phase 135 — token profile drift. Compares
                // STING_VIEW_TAG_STYLE and TAG_SEG_MASK_TXT on the view
                // to the resolved profile/pack defaults. SyncStyles
                // re-runs DrawingTypePresentation.Apply which routes
                // through TokenProfileApplier and heals the drift.
                AppendTokenProfileDrift(doc, v, dt, report);

                // Phase 137 — managed-template drift. Compares the
                // STING-managed Revit template's STING_PACK_CHECKSUM_TXT
                // against the live pack checksum. SyncStyles re-runs
                // DrawingTypePresentation.Apply which routes through
                // ManagedTemplateSyncer and heals the drift.
                AppendManagedTemplateDrift(doc, v, dt, report);

                if (report.Any) reports.Add(report);
            }
            return reports;
        }

        private static void AppendManagedTemplateDrift(Document doc, View v, DrawingType dt, DriftReport report)
        {
            try
            {
                if (string.IsNullOrEmpty(dt.ViewStylePackId)) return;
                var pack = ViewStylePackRegistry.Get(doc, dt.ViewStylePackId);
                if (pack == null || !pack.IsManaged) return;

                var templateName = $"STING:{pack.Id}:{v.ViewType}";
                var template = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(t => t.IsTemplate
                                      && t.ViewType == v.ViewType
                                      && string.Equals(t.Name, templateName, StringComparison.Ordinal));

                if (template == null)
                {
                    report.Drifts.Add(
                        $"MANAGED_TEMPLATE: '{templateName}' missing — pack is managed but template not generated.");
                    return;
                }

                var stored = ReadStringParam(template, "STING_PACK_CHECKSUM_TXT");
                var expected = ManagedTemplateSyncer.ComputePackChecksum(pack);
                if (!string.Equals(stored, expected, StringComparison.Ordinal))
                {
                    report.Drifts.Add(
                        $"MANAGED_TEMPLATE: checksum drift on '{templateName}' " +
                        $"(stored {Truncate(stored, 8)} vs current {Truncate(expected, 8)})");
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"AppendManagedTemplateDrift({v.Id}): {ex.Message}");
            }
        }

        private static string Truncate(string s, int n)
            => string.IsNullOrEmpty(s) ? "(empty)" : (s.Length <= n ? s : s.Substring(0, n));

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
                        report.Drifts.Add($"TOKEN_PROFILE: STING_VIEW_TAG_STYLE '{actual ?? "(empty)"}' vs profile '{expectedScheme}'");
                }

                string expectedMask = profile?.SegmentMask;
                if (!string.IsNullOrEmpty(expectedMask)
                    && expectedMask.Length == 8)
                {
                    string actual = ReadStringParam(v, ParamRegistry.TAG_SEG_MASK);
                    if (!string.Equals(actual, expectedMask, StringComparison.Ordinal))
                        report.Drifts.Add($"TOKEN_PROFILE: TAG_SEG_MASK '{actual ?? "(empty)"}' vs profile '{expectedMask}'");
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
