using StingTools.Core;
// StingTools v4 MVP — BS EN 50174-2 service separation checker.
//
// For a candidate drop (from→to) on a given service (ELC_PWR,
// COM_DATA, PLM_CWS, HVC_SA, …), scans nearby MEPCurves, classifies
// each by service, and flags min-separation violations via the
// RoutingRules table.
//
// Phase A: reports violations as DropResult warnings; does not reject
// the drop. Phase C will fold the results into the A* voxel cost
// function so the path-finder routes around crowded zones.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Routing
{
    public class SeparationViolation
    {
        public string Rule         { get; set; } = "";
        public string OtherService { get; set; } = "";
        public ElementId OtherId   { get; set; } = ElementId.InvalidElementId;
        public double ActualMm     { get; set; }
        public double RequiredMm   { get; set; }
        public string Rationale    { get; set; } = "";

        public override string ToString() =>
            $"{Rule}: {OtherService} {ActualMm:F0}mm (need {RequiredMm:F0}mm) — {Rationale}";
    }

    public static class SeparationChecker
    {
        private const double MmToFt = 1.0 / 304.8;
        private const double FtToMm = 304.8;

        // Revit MEPCurve category → service id string used in rules.
        // Power vs data can't be distinguished by category alone — the
        // caller passes the source service, and we classify neighbours
        // by their system name when possible, falling back to category.
        private static readonly Dictionary<int, string> CategoryServiceMap
            = new Dictionary<int, string>
            {
                { (int)BuiltInCategory.OST_Conduit,         "ELC_PWR"    },
                { (int)BuiltInCategory.OST_CableTray,       "ELC_PWR"    },
                { (int)BuiltInCategory.OST_DuctCurves,      "HVC_SA"     },
                { (int)BuiltInCategory.OST_FlexDuctCurves,  "HVC_SA"     },
                { (int)BuiltInCategory.OST_PipeCurves,      "PLM_CWS"    },
                { (int)BuiltInCategory.OST_FlexPipeCurves,  "PLM_CWS"    },
            };

        /// <summary>
        /// Scan all MEPCurves within searchRadiusMm of the drop line.
        /// For each neighbour, look up the required separation and flag
        /// anything whose actual centreline distance is below it.
        /// </summary>
        public static List<SeparationViolation> Check(
            Document doc,
            XYZ from,
            XYZ to,
            string sourceService,
            double searchRadiusMm = 1500.0)
        {
            var findings = new List<SeparationViolation>();
            if (doc == null || from == null || to == null) return findings;

            var dropCurve = Line.CreateBound(from, to);
            var searchRadiusFt = searchRadiusMm * MmToFt;

            var scope = new List<Element>();
            foreach (var cat in CategoryServiceMap.Keys)
            {
                try
                {
                    var col = new FilteredElementCollector(doc)
                        .OfCategory((BuiltInCategory)cat)
                        .WhereElementIsNotElementType();
                    scope.AddRange(col);
                }
                catch (Exception ex)
                { StingLog.Warn($"SeparationChecker: collector for cat {cat} failed: {ex.Message}"); }
            }

            foreach (var other in scope)
            {
                var otherCurve = (other?.Location as LocationCurve)?.Curve;
                if (otherCurve == null) continue;

                // Cheap bounding-box prefilter before the full distance calc.
                double approxDist;
                try
                {
                    var otherMid = otherCurve.Evaluate(0.5, true);
                    approxDist = dropCurve.Project(otherMid)?.XYZPoint?.DistanceTo(otherMid)
                                 ?? double.MaxValue;
                }
                catch { approxDist = double.MaxValue; }
                if (approxDist > searchRadiusFt) continue;

                string otherService = InferService(other);
                double requiredMm = RoutingRules.RequiredSeparationMm(sourceService, otherService);
                if (requiredMm <= 0) continue; // no rule applies

                // Actual minimum distance between the two line segments —
                // sample both curves at 10 points, take min pair-distance.
                double actualFt = MinCurveDistance(dropCurve, otherCurve);
                double actualMm = actualFt * FtToMm;
                if (actualMm >= requiredMm) continue;

                var rule = RoutingRules.SeparationRules
                    .Where(r => r.AppliesTo(sourceService, otherService))
                    .OrderByDescending(r => r.MinSeparationMm)
                    .FirstOrDefault();

                findings.Add(new SeparationViolation
                {
                    Rule         = rule?.Id ?? "UNKNOWN",
                    OtherService = otherService,
                    OtherId      = other.Id,
                    ActualMm     = actualMm,
                    RequiredMm   = requiredMm,
                    Rationale    = rule?.Rationale ?? ""
                });
            }

            return findings;
        }

        private static string InferService(Element el)
        {
            // Try to read a system name first — most projects use names
            // like "Hot Water — Domestic" or "Fire Alarm" that contain
            // the service classifier.
            try
            {
                if (el is MEPCurve mc)
                {
                    var sysName = mc.MEPSystem?.Name ?? "";
                    var mapped = MapSystemNameToService(sysName);
                    if (!string.IsNullOrEmpty(mapped)) return mapped;
                }
            }
            catch { /* some curves have no system */ }
            // Fallback: category → default service.
            try
            {
                int catId = (int)(el.Category?.Id?.Value ?? -1);
                if (CategoryServiceMap.TryGetValue(catId, out var svc)) return svc;
            }
            catch { }
            return "UNKNOWN";
        }

        private static string MapSystemNameToService(string systemName)
        {
            if (string.IsNullOrEmpty(systemName)) return null;
            var n = systemName.ToUpperInvariant();
            if (n.Contains("FIRE"))        return "COM_FIRE";
            if (n.Contains("DATA") || n.Contains("TEL")) return "COM_DATA";
            if (n.Contains("SECURITY"))    return "COM_SEC";
            if (n.Contains("EMERGENCY"))   return "LTG_EMERGENCY";
            if (n.Contains("HV") || n.Contains("HIGH VOLT")) return "ELC_HV";
            if (n.Contains("POWER") || n.Contains("LIGHTING")) return "ELC_PWR";
            if (n.Contains("MED"))         return "PLM_MED_GAS";
            if (n.Contains("GAS"))         return "PLM_GAS";
            if (n.Contains("FOUL") || n.Contains("SOIL")) return "PLM_FOUL";
            if (n.Contains("SAN"))         return "PLM_SANITARY";
            if (n.Contains("STORM") || n.Contains("RWP") || n.Contains("RAIN"))
                                           return "PLM_STORM";
            if (n.Contains("HOT") || n.Contains("DHW") || n.Contains("HWS"))
                                           return "PLM_HWS";
            if (n.Contains("COLD") || n.Contains("DCW") || n.Contains("CWS"))
                                           return "PLM_CWS";
            if (n.Contains("CHW") || n.Contains("CHILL")) return "HVC_CHW";
            if (n.Contains("LTHW") || n.Contains("HEAT")) return "HVC_LTHW";
            if (n.Contains("SUPPLY"))      return "HVC_SA";
            if (n.Contains("RETURN"))      return "HVC_RA";
            if (n.Contains("EXHAUST"))     return "HVC_EX";
            if (n.Contains("SPRINK"))      return "FLS_SPK";
            return null;
        }

        private static double MinCurveDistance(Curve a, Curve b, int samples = 10)
        {
            double best = double.MaxValue;
            for (int i = 0; i <= samples; i++)
            {
                double t = i / (double)samples;
                XYZ pa;
                try { pa = a.Evaluate(t, true); } catch { continue; }
                double d;
                try
                {
                    var proj = b.Project(pa);
                    d = proj?.XYZPoint?.DistanceTo(pa) ?? double.MaxValue;
                }
                catch { continue; }
                if (d < best) best = d;
            }
            return best;
        }
    }
}
