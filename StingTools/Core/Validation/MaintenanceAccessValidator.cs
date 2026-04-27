// Phase 139 H1 — Maintenance access validator.
//
// Validates clearance in front of / above / beside placed equipment per
// rule.MaintenanceClearance class.  Builds an axis-aligned clearance
// volume in front of the element facing direction and queries any
// element intersecting it.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Validation
{
    public class MaintenanceAccessValidator
    {
        public string Name => "MaintenanceAccessValidator";
        private const string ValidatorTag = "MaintenanceAccessValidator";
        private const double MmToFt = 1.0 / 304.8;

        public List<ValidationResult> Validate(Document doc)
        {
            var results = new List<ValidationResult>();
            if (doc == null) return results;
            try
            {
                var cats = new[]
                {
                    BuiltInCategory.OST_ElectricalEquipment,
                    BuiltInCategory.OST_MechanicalEquipment,
                    BuiltInCategory.OST_PlumbingFixtures,
                    BuiltInCategory.OST_LightingFixtures,
                    BuiltInCategory.OST_SpecialityEquipment,
                };
                var filter = new ElementMulticategoryFilter(cats);
                var col = new FilteredElementCollector(doc).WherePasses(filter)
                    .WhereElementIsNotElementType();

                foreach (var el in col)
                {
                    try
                    {
                        string clearClass = ReadString(el, "STING_MAINT_CLEAR_TXT", "");
                        if (string.IsNullOrEmpty(clearClass)) continue;
                        var clearance = ResolveClearance(clearClass);
                        if (clearance == null) continue;
                        var bb = el.get_BoundingBox(null);
                        if (bb == null) continue;

                        // Build clearance AABB in front of the element.
                        var (minPt, maxPt) = BuildClearanceVolume(el, bb, clearance);
                        var outline = new Outline(minPt, maxPt);
                        var bbf = new BoundingBoxIntersectsFilter(outline);
                        var intruders = new FilteredElementCollector(doc)
                            .WhereElementIsNotElementType()
                            .WherePasses(bbf)
                            .Where(e => e.Id != el.Id)
                            .ToList();
                        if (intruders.Count > 0)
                        {
                            results.Add(new ValidationResult(
                                el.Id, ValidationSeverity.Error,
                                "MAINT_ACCESS_BLOCKED",
                                $"{clearClass} clearance blocked by {intruders.Count} element(s)",
                                ValidatorTag));
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"MaintenanceAccessValidator: element {el.Id} failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MaintenanceAccessValidator: scan failed: {ex.Message}");
            }
            return results;
        }

        private class ClearanceSpec
        {
            public string Direction { get; set; } = ""; // FRONT/SIDES/TOP
            public double DepthMm   { get; set; } = 0;
        }

        private static ClearanceSpec ResolveClearance(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            string u = code.ToUpperInvariant();
            if (u == "FRONT_600")  return new ClearanceSpec { Direction = "FRONT", DepthMm = 600  };
            if (u == "FRONT_1000") return new ClearanceSpec { Direction = "FRONT", DepthMm = 1000 };
            if (u == "SIDES_300")  return new ClearanceSpec { Direction = "SIDES", DepthMm = 300  };
            if (u == "TOP_900")    return new ClearanceSpec { Direction = "TOP",   DepthMm = 900  };
            return null;
        }

        private static (XYZ min, XYZ max) BuildClearanceVolume(Element el, BoundingBoxXYZ bb, ClearanceSpec spec)
        {
            double depthFt = spec.DepthMm * MmToFt;
            XYZ min = bb.Min, max = bb.Max;
            switch (spec.Direction)
            {
                case "FRONT":
                    // Build volume on +Y side of element (Revit family default).
                    return (new XYZ(min.X, max.Y, min.Z),
                            new XYZ(max.X, max.Y + depthFt, max.Z));
                case "SIDES":
                    // Pad on both X sides; volume extends along element height.
                    return (new XYZ(min.X - depthFt, min.Y, min.Z),
                            new XYZ(max.X + depthFt, max.Y, max.Z));
                case "TOP":
                    return (new XYZ(min.X, min.Y, max.Z),
                            new XYZ(max.X, max.Y, max.Z + depthFt));
                default:
                    return (min, max);
            }
        }

        private static string ReadString(Element el, string paramName, string fallback)
        {
            try
            {
                var p = el?.LookupParameter(paramName);
                if (p == null || !p.HasValue) return fallback;
                if (p.StorageType == StorageType.String) return p.AsString() ?? fallback;
            }
            catch { }
            return fallback;
        }
    }
}
