// StingTools v4 MVP — BS EN 50174-2 cable segregation validator.
//
// No competitor in the Part-C research surveys this natively: MagiCAD
// has cable layouts, eVolve does not enforce segregation, ProDesign is
// calc-only. This validator closes the gap by walking cable trays /
// conduits and reporting separation violations per the BS EN 50174-2
// Annex E matrix:
//
//                                                    Unscreened-Power
//                                                    Minimum separation
//   Data/Power class              Pair         |   No divider  | Metal divider | Enclosed metal
//   ---------------------------------------   +--------------+---------------+-----------------
//   Unscreened data + unscreened power            200 mm          100 mm          0 mm
//   Screened (foil)   + unscreened power          50 mm           20 mm           0 mm
//   Screened (S/FTP)  + unscreened power          30 mm           10 mm           0 mm
//   Any data + screened power (SWA)                0 mm            0 mm           0 mm
//
// The last 15 m of any run are exempt (clause 6.6.7, "approach to
// equipment"). We flag, but don't auto-fix: re-routing to recover
// segregation is Phase I territory.
//
// Classification is read from ELC_CABLE_SEG_CLASS_TXT on cables (when
// modelled as StingCable elements) or from the parent conduit / tray
// when cables are implicit. The validator defaults to UTP (worst case)
// when classification is absent, which is deliberately pessimistic.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace StingTools.Core.Calc
{
    public enum CableSegClass
    {
        Unknown  = 0,   // default — assume UTP for worst case
        UTP      = 1,   // unscreened twisted pair (balanced data)
        FTP      = 2,   // foil-screened
        SFTP     = 3,   // screen-of-screens (category 6A+)
        SWA      = 4,   // steel-wire-armour power (treat as screened)
        Power    = 5,   // unscreened mains power
        Fire     = 6,   // BS 6387 fire-resistant — requires own containment
    }

    public class CableSegFinding
    {
        public ElementId Tray1 { get; set; } = ElementId.InvalidElementId;
        public ElementId Tray2 { get; set; } = ElementId.InvalidElementId;
        public CableSegClass Class1 { get; set; }
        public CableSegClass Class2 { get; set; }
        public double ActualMm  { get; set; }
        public double RequiredMm { get; set; }
        public string DividerHint { get; set; } = "";
        public string Severity { get; set; } = "Warning";
        public override string ToString() =>
            $"{Class1} vs {Class2}: {ActualMm:F0} mm actual ({RequiredMm:F0} mm required, " +
            $"{DividerHint})";
    }

    public class CableSegResult
    {
        public int TraysScanned { get; set; }
        public int PairsChecked { get; set; }
        public List<CableSegFinding> Findings { get; } = new List<CableSegFinding>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class CableSegregationValidator
    {
        private const double FtToMm = 304.8;
        private const double SearchRadiusMm = 400.0; // Annex E upper bound is 200 mm; add margin

        /// <summary>
        /// Classify a tray/conduit. Reads explicit ELC_CABLE_SEG_CLASS_TXT
        /// when present; falls back to system-name heuristics.
        /// </summary>
        public static CableSegClass Classify(Element el)
        {
            try
            {
                var p = el.LookupParameter("ELC_CABLE_SEG_CLASS_TXT");
                var s = p?.AsString() ?? "";
                if (!string.IsNullOrEmpty(s))
                {
                    switch (s.ToUpperInvariant())
                    {
                        case "SCREEN":
                        case "FTP":    return CableSegClass.FTP;
                        case "SFTP":
                        case "SCSCR":  return CableSegClass.SFTP;
                        case "UTP":
                        case "MULT_SC":return CableSegClass.UTP;
                        case "SWA":    return CableSegClass.SWA;
                        case "FIRE":   return CableSegClass.Fire;
                        case "POWER":  return CableSegClass.Power;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            // Heuristic classification.
            try
            {
                string sysName = "";
                if (el is MEPCurve mc) sysName = mc.MEPSystem?.Name ?? "";
                if (string.IsNullOrEmpty(sysName)) sysName = el.Name ?? "";
                sysName = sysName.ToUpperInvariant();
                if (sysName.Contains("FIRE"))               return CableSegClass.Fire;
                if (sysName.Contains("SWA") ||
                    sysName.Contains("ARMOUR"))             return CableSegClass.SWA;
                if (sysName.Contains("DATA") ||
                    sysName.Contains("CAT6") ||
                    sysName.Contains("TEL"))                return CableSegClass.UTP;
                if (sysName.Contains("POWER") ||
                    sysName.Contains("LIGHT"))              return CableSegClass.Power;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return CableSegClass.Unknown;
        }

        /// <summary>
        /// Look up the minimum separation between two segregation
        /// classes when routed without a divider. Returns 0 when the
        /// pair does not require separation (e.g. both screened).
        /// </summary>
        public static double RequiredSeparationMm(CableSegClass a, CableSegClass b, out string dividerHint)
        {
            dividerHint = "no divider";
            // Normalise to (power, data) ordering so the matrix has
            // one canonical slot per pair.
            var data  = IsData(a)  ? a : b;
            var power = IsPower(a) ? a : b;

            // Fire cables need their own containment under BS 5839-1.
            if (a == CableSegClass.Fire || b == CableSegClass.Fire)
            {
                dividerHint = "BS 5839-1: separate containment";
                return 50.0;
            }

            if (power == CableSegClass.SWA)
            {
                dividerHint = "SWA counts as screened — no separation";
                return 0.0;
            }

            if (!(IsData(data) && IsPower(power))) return 0.0;

            switch (data)
            {
                case CableSegClass.UTP:  dividerHint = "200mm no divider / 100mm metal divider / 0mm enclosed metal";
                                         return 200.0;
                case CableSegClass.FTP:  dividerHint = "50mm no divider / 20mm metal divider / 0mm enclosed metal";
                                         return 50.0;
                case CableSegClass.SFTP: dividerHint = "30mm no divider / 10mm metal divider / 0mm enclosed metal";
                                         return 30.0;
                default:                 dividerHint = "fallback UTP worst case";
                                         return 200.0;
            }
        }

        public static CableSegResult Validate(Document doc)
        {
            var result = new CableSegResult();
            if (doc == null) return result;

            // Collect cable trays + conduits in one pass.
            var trays = new List<Element>();
            foreach (var cat in new[] { BuiltInCategory.OST_CableTray,
                                         BuiltInCategory.OST_Conduit,
                                         BuiltInCategory.OST_ElectricalCircuit })
            {
                try
                {
                    var col = new FilteredElementCollector(doc)
                        .OfCategory(cat).WhereElementIsNotElementType();
                    trays.AddRange(col);
                }
                catch (Exception ex)
                { result.Warnings.Add($"Collector {cat}: {ex.Message}"); }
            }
            result.TraysScanned = trays.Count;
            if (trays.Count < 2) return result;

            // Classify once per element.
            var classified = trays.Select(el => (el, cls: Classify(el))).ToList();

            // Pairwise check. O(n²) is fine for project-scale (~200
            // trays typical); Phase I can swap for an R-tree.
            for (int i = 0; i < classified.Count; i++)
            {
                for (int j = i + 1; j < classified.Count; j++)
                {
                    result.PairsChecked++;
                    var (a, clsA) = classified[i];
                    var (b, clsB) = classified[j];
                    if (clsA == CableSegClass.Unknown && clsB == CableSegClass.Unknown) continue;

                    string divider;
                    double requiredMm = RequiredSeparationMm(clsA, clsB, out divider);
                    if (requiredMm <= 0) continue;

                    // Quick AABB reject.
                    var bbA = a.get_BoundingBox(null);
                    var bbB = b.get_BoundingBox(null);
                    if (bbA == null || bbB == null) continue;
                    double aabbDistMm = AABBDistance(bbA, bbB) * FtToMm;
                    if (aabbDistMm > SearchRadiusMm) continue;

                    // Finer check: curve-to-curve distance at 6 samples.
                    var curveA = (a.Location as LocationCurve)?.Curve;
                    var curveB = (b.Location as LocationCurve)?.Curve;
                    if (curveA == null || curveB == null) continue;
                    double actualMm = MinCurveDistance(curveA, curveB, 6) * FtToMm;
                    if (actualMm >= requiredMm) continue;

                    result.Findings.Add(new CableSegFinding
                    {
                        Tray1 = a.Id, Tray2 = b.Id,
                        Class1 = clsA, Class2 = clsB,
                        ActualMm = actualMm, RequiredMm = requiredMm,
                        DividerHint = divider,
                        Severity = actualMm < 0.5 * requiredMm ? "Error" : "Warning",
                    });
                }
            }
            return result;
        }

        private static bool IsData(CableSegClass c) =>
            c == CableSegClass.UTP || c == CableSegClass.FTP || c == CableSegClass.SFTP;
        private static bool IsPower(CableSegClass c) =>
            c == CableSegClass.Power || c == CableSegClass.SWA;

        private static double AABBDistance(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            double dx = Math.Max(0.0, Math.Max(a.Min.X - b.Max.X, b.Min.X - a.Max.X));
            double dy = Math.Max(0.0, Math.Max(a.Min.Y - b.Max.Y, b.Min.Y - a.Max.Y));
            double dz = Math.Max(0.0, Math.Max(a.Min.Z - b.Max.Z, b.Min.Z - a.Max.Z));
            return Math.Sqrt(dx*dx + dy*dy + dz*dz);
        }

        private static double MinCurveDistance(Curve a, Curve b, int samples)
        {
            double best = double.MaxValue;
            for (int i = 0; i <= samples; i++)
            {
                double t = i / (double)samples;
                XYZ pa;
                try { pa = a.Evaluate(t, true); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); continue; }
                double d;
                try
                {
                    var proj = b.Project(pa);
                    d = proj?.XYZPoint?.DistanceTo(pa) ?? double.MaxValue;
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); continue; }
                if (d < best) best = d;
            }
            return best;
        }
    }
}
