// Phase 139 F3 — Accessibility (BS8300 / Approved Doc M / Part M)
// post-placement auditor.  Uses HeightStandardsTable to validate
// placed element MountingHeight against the Min/Max range for its
// rule.HeightStandard key.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core.Placement;

namespace StingTools.Core.Validation
{
    public class AccessibilityAuditor
    {
        public string Name => "AccessibilityAuditor";
        private const string ValidatorTag = "AccessibilityAuditor";
        private const double FtToMm = 304.8;

        public List<ValidationResult> Validate(Document doc)
        {
            var results = new List<ValidationResult>();
            if (doc == null) return results;

            try
            {
                var col = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType();

                foreach (var el in col)
                {
                    try
                    {
                        var p = el.LookupParameter("STING_HEIGHT_STANDARD_TXT");
                        if (p == null || !p.HasValue) continue;
                        string key = p.AsString();
                        if (string.IsNullOrEmpty(key)) continue;

                        var entry = HeightStandardsTable.Get(key);
                        if (entry == null) continue;

                        var pt = (el.Location as LocationPoint)?.Point;
                        if (pt == null) continue;
                        double levelZ = 0.0;
                        try
                        {
                            var lvlId = el.LevelId;
                            if (lvlId != null && lvlId != ElementId.InvalidElementId)
                            {
                                var lvl = doc.GetElement(lvlId) as Level;
                                if (lvl != null) levelZ = lvl.Elevation;
                            }
                        }
                        catch { }
                        double heightMm = (pt.Z - levelZ) * FtToMm;

                        if (heightMm + 5 < entry.MinMm || heightMm - 5 > entry.MaxMm)
                        {
                            results.Add(new ValidationResult(
                                el.Id, ValidationSeverity.Warning,
                                "ACCESS_HEIGHT_OUT_OF_RANGE",
                                $"Placed at {heightMm:F0}mm; standard '{key}' requires {entry.MinMm:F0}-{entry.MaxMm:F0}mm ({entry.Standard})",
                                ValidatorTag));
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"AccessibilityAuditor element {el.Id}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AccessibilityAuditor scan failed: {ex.Message}");
            }
            return results;
        }
    }
}
