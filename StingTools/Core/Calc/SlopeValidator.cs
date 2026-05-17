// Phase 139.28 — BS EN 12056-2 / BS 5572 slope validator.
//
// Confirms that gravity-drainage and vent-pipe runs are laid at or
// above the standards-mandated minimum gradient. Used by routing
// engines after segments are created, both for in-wall chase routes
// (drainage stacks, branch waste) and for surface / suspended runs
// (above-ground sanitary).
//
// Reference values (BS EN 12056-2 §6, BS 5572 §3):
//
//   Soil / waste branch  ≤ 1.5 m   :  1:80   (1.25 %)  System I (UK)
//                                     1:40   (2.50 %)  short branches & sinks
//   Vent pipe                       :  any   (no slope required, but pitch
//                                            toward stack is good practice)
//   Roof drainage                   :  1:80  (1.25 %)
//   Foul drainage stack             :  vertical only
//
// Caller passes the rule's MinSlopePercent (or the result of a default
// derivation). The validator inspects each created segment and reports
// any whose slope is below the threshold.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Calc
{
    public static class SlopeValidator
    {
        /// <summary>
        /// BS EN 12056-2 default minimum slope (%) for branch waste runs
        /// up to 1.5 m. Stacks and main runs use double this (2.5 %).
        /// </summary>
        public const double DefaultBranchSlopePct = 1.25;

        /// <summary>BS EN 12056-2 default for runs &gt; 1.5 m.</summary>
        public const double DefaultMainSlopePct = 2.5;

        public class SlopeReport
        {
            public int SegmentsChecked { get; set; }
            public int SegmentsBelowThreshold { get; set; }
            public int SegmentsAboveThreshold { get; set; }
            public int VerticalSegments { get; set; }
            public List<string> Warnings { get; } = new List<string>();
        }

        /// <summary>
        /// Walk every supplied MEP-curve segment and check the run's
        /// slope (rise/run %). Returns a report; never throws.
        /// </summary>
        public static SlopeReport CheckSegments(
            Document doc,
            IList<ElementId> segmentIds,
            double minSlopePct,
            string ruleId = "")
        {
            var r = new SlopeReport();
            if (doc == null || segmentIds == null || segmentIds.Count == 0) return r;
            if (minSlopePct <= 0) return r; // pressurised — nothing to check
            foreach (var id in segmentIds)
            {
                Element el = null;
                try { el = doc.GetElement(id); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (el == null) continue;
                var curve = (el.Location as LocationCurve)?.Curve;
                if (curve == null) continue;
                XYZ a, b;
                try
                {
                    a = curve.GetEndPoint(0);
                    b = curve.GetEndPoint(1);
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); continue; }
                if (a == null || b == null) continue;
                double dxFt = b.X - a.X;
                double dyFt = b.Y - a.Y;
                double dzFt = b.Z - a.Z;
                double horizFt = Math.Sqrt(dxFt * dxFt + dyFt * dyFt);
                double horizMm = horizFt * 304.8;
                double riseMm  = Math.Abs(dzFt) * 304.8;

                r.SegmentsChecked++;

                double totalLenFt = Math.Sqrt(dxFt * dxFt + dyFt * dyFt + dzFt * dzFt);
                double verticalFraction = totalLenFt > 1e-6 ? Math.Abs(dzFt) / totalLenFt : 0.0;
                if (verticalFraction > 0.8 || (horizMm < 50.0 && riseMm > 100.0))
                {
                    r.VerticalSegments++;
                    continue;
                }
                if (horizMm < 1.0)
                {
                    continue;
                }

                double slopePct = (riseMm / horizMm) * 100.0;
                if (slopePct >= minSlopePct)
                {
                    r.SegmentsAboveThreshold++;
                }
                else
                {
                    r.SegmentsBelowThreshold++;
                    if (r.Warnings.Count < 10)
                    {
                        string ratio = SlopeAsRatio(slopePct);
                        string targetRatio = SlopeAsRatio(minSlopePct);
                        string ridText = string.IsNullOrEmpty(ruleId) ? "" : $" (rule '{ruleId}')";
                        r.Warnings.Add(
                            $"SlopeValidator: segment {id?.Value} slopes at {slopePct:F2}% ({ratio}) " +
                            $"— below minimum {minSlopePct:F2}% ({targetRatio}) per BS EN 12056-2{ridText}.");
                    }
                }
            }

            if (r.SegmentsBelowThreshold > 10)
                r.Warnings.Add($"SlopeValidator: +{r.SegmentsBelowThreshold - 10} more segments below threshold (truncated; see StingLog).");
            return r;
        }

        /// <summary>
        /// Derive a sensible default slope for a route segment category
        /// when the rule didn't set MinSlopePercent. Returns 0 for
        /// pressurised systems (water supply, gas, vent).
        /// </summary>
        public static double DefaultSlopePctFor(string routeSegmentCategory, string systemHint = "")
        {
            string sys = (systemHint ?? "").ToUpperInvariant();
            string cat = (routeSegmentCategory ?? "").ToUpperInvariant();
            if (cat != "PIPE") return 0.0;
            if (sys.Contains("WASTE") || sys.Contains("SOIL") || sys.Contains("DRAIN")
             || sys.Contains("FOUL")  || sys.Contains("STORM") || sys.Contains("RWO"))
                return DefaultBranchSlopePct;
            // Heating return: 0.33 % so condensate finds the system low point.
            if (sys.Contains("HEAT") || sys.Contains("LTHW") || sys.Contains("MTHW"))
                return 0.33;
            return 0.0; // CWS / HWS / GAS / VENT — pressurised, no slope.
        }

        private static string SlopeAsRatio(double pct)
        {
            if (pct <= 0) return "level";
            double run = 100.0 / pct;
            return $"1:{run:F0}";
        }
    }
}
