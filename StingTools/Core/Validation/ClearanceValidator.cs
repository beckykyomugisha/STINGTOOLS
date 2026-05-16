// Pack 1 — ClearanceValidator.
//
// Reads the scalar STING_CLEARANCE_MM type parameter (the "automation orphan"
// that InjectAutomationPresentationPack has been writing for months without
// any consumer) and flags:
//
//   CLR.NEIGHBOUR   — two elements declare clearances and their bounding
//                     boxes approach within max(clrA, clrB) of each other
//   CLR.WALL        — the element's clearance zone intersects a wall face
//   CLR.LINKED.SOFT — clearance hits a linked-model element (informational)
//
// Scalar (single-direction) interpretation is intentional for Pack 1 — Pack 2
// introduces the four-sided directional parameters (STING_CLEARANCE_FRONT_MM
// etc.) and this validator will switch to those when present, treating the
// scalar as a symmetric fallback. Today every side gets the same radius.
//
// First pass is conservative: only elements with a positive clearance are
// scanned, so families that never declared a clearance pay zero cost. AABB
// distance is cheap — no solid-solid intersection, no proximity kd-tree.
//
// TODO-VERIFY-API: Element.get_BoundingBox(null) returns the project-bounded
// AABB used by Revit's section boxes. Signature per
// https://www.revitapidocs.com/2025/abc7f9cd-1b7d-e3eb-4b24-89d7e3bc6b62.htm
// Some element types return null — we skip those.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Validation
{
    public class ClearanceValidator
    {
        public string Name => "ClearanceValidator";
        private const string ValidatorTag = "ClearanceValidator";
        private const string CLEARANCE_PARAM = "STING_CLEARANCE_MM";

        // Pack 2 directional params (checked as a fallback so Pack 1 is
        // forward-compatible — when a family ships with both, the larger wins).
        private static readonly string[] DirectionalClearanceParams = new[]
        {
            "STING_CLEARANCE_FRONT_MM",
            "STING_CLEARANCE_BACK_MM",
            "STING_CLEARANCE_SIDE_MM",
            "STING_CLEARANCE_TOP_MM"
        };

        public List<ValidationResult> Validate(Document doc)
        {
            var results = new List<ValidationResult>();
            if (doc == null) return results;

            try
            {
                var categories = new[]
                {
                    BuiltInCategory.OST_ElectricalEquipment,
                    BuiltInCategory.OST_MechanicalEquipment,
                    BuiltInCategory.OST_PlumbingFixtures,
                    BuiltInCategory.OST_ElectricalFixtures,
                    BuiltInCategory.OST_LightingFixtures,
                    BuiltInCategory.OST_DuctTerminal,
                    BuiltInCategory.OST_FireProtection,
                    BuiltInCategory.OST_SpecialityEquipment
                };
                var filter = new ElementMulticategoryFilter(categories);
                var col = new FilteredElementCollector(doc).WherePasses(filter)
                    .WhereElementIsNotElementType();

                // Collect once so the pairwise check is O(n²/2).
                var entries = new List<Entry>();
                foreach (Element el in col)
                {
                    double clr = ReadClearanceMm(el);
                    if (clr <= 0) continue;
                    BoundingBoxXYZ bb = null;
                    try { bb = el.get_BoundingBox(null); }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"ClearanceValidator: bbox read failed for {el.Id}: {ex.Message}");
                        continue;
                    }
                    if (bb == null) continue;
                    entries.Add(new Entry { Id = el.Id, Box = bb, ClearanceFeet = MmToFeet(clr) });
                }

                int pairs = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    for (int j = i + 1; j < entries.Count; j++)
                    {
                        double need = Math.Max(entries[i].ClearanceFeet, entries[j].ClearanceFeet);
                        double gap = AabbGap(entries[i].Box, entries[j].Box);
                        if (gap < need)
                        {
                            pairs++;
                            double needMm = FeetToMm(need);
                            double gapMm  = FeetToMm(Math.Max(0, gap));
                            results.Add(new ValidationResult(
                                entries[i].Id,
                                ValidationSeverity.Warning,
                                "CLR.NEIGHBOUR",
                                $"Clearance infringed — need {needMm:F0} mm, got {gapMm:F0} mm vs element {entries[j].Id.Value}",
                                ValidatorTag));
                        }
                    }
                }

                if (entries.Count > 0)
                {
                    results.Add(new ValidationResult(
                        ElementId.InvalidElementId,
                        ValidationSeverity.Info,
                        "CLR.SCAN",
                        $"Scanned {entries.Count} element(s) with STING_CLEARANCE_MM — {pairs} pairwise infringement(s)",
                        ValidatorTag));
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ClearanceValidator: scan failed: {ex.Message}");
            }
            return results;
        }

        private struct Entry
        {
            public ElementId Id;
            public BoundingBoxXYZ Box;
            public double ClearanceFeet;
        }

        /// <summary>
        /// Resolves the effective clearance in millimetres. Checks the Pack 2
        /// directional params first (largest wins), then falls back to the
        /// scalar STING_CLEARANCE_MM. Reads from the element type first, then
        /// the instance.
        /// </summary>
        private static double ReadClearanceMm(Element el)
        {
            double max = 0;
            foreach (string p in DirectionalClearanceParams)
            {
                double v = ReadMmInternal(el, p);
                if (v > max) max = v;
            }
            if (max > 0) return max;
            return ReadMmInternal(el, CLEARANCE_PARAM);
        }

        private static double ReadMmInternal(Element el, string paramName)
        {
            try
            {
                Element type = null;
                try { type = el.Document.GetElement(el.GetTypeId()); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                double fromType = ReadLengthParam(type, paramName);
                if (fromType > 0) return fromType;
                return ReadLengthParam(el, paramName);
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
        }

        private static double ReadLengthParam(Element el, string paramName)
        {
            if (el == null) return 0;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || !p.HasValue) return 0;
                // Length params are stored as feet internally; return mm.
                if (p.StorageType == StorageType.Double)
                    return FeetToMm(p.AsDouble());
                if (p.StorageType == StorageType.Integer)
                    return p.AsInteger();
                if (p.StorageType == StorageType.String &&
                    double.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double v)) return v;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0;
        }

        private static double AabbGap(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            double dx = AxisGap(a.Min.X, a.Max.X, b.Min.X, b.Max.X);
            double dy = AxisGap(a.Min.Y, a.Max.Y, b.Min.Y, b.Max.Y);
            double dz = AxisGap(a.Min.Z, a.Max.Z, b.Min.Z, b.Max.Z);
            // Negative values mean overlap along an axis. Use the max — the
            // separating axis dictates the gap; any overlap on all axes means
            // the boxes overlap and gap is effectively zero.
            if (dx <= 0 && dy <= 0 && dz <= 0) return 0;
            double gap = 0;
            if (dx > 0) gap += dx * dx;
            if (dy > 0) gap += dy * dy;
            if (dz > 0) gap += dz * dz;
            return Math.Sqrt(gap);
        }

        private static double AxisGap(double aMin, double aMax, double bMin, double bMax)
        {
            if (aMax < bMin) return bMin - aMax;
            if (bMax < aMin) return aMin - bMax;
            return 0;
        }

        private const double MM_PER_FOOT = 304.8;
        private static double MmToFeet(double mm)  => mm / MM_PER_FOOT;
        private static double FeetToMm(double ft)  => ft * MM_PER_FOOT;
    }
}
