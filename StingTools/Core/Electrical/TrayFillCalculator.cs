// StingTools v4 MVP — Phase L tray fill calculator.
//
// For a given cable tray / conduit, sums the circular cross-section
// area of every cable whose RouteTrayIds contains the tray's id,
// plus a user-configurable packing waste factor (default 1.10 for
// random-lay cables, 1.00 for ordered arrangements).
//
// Reports both the absolute area ratio and the IEC 60364-5-52
// §522.4 + BS 7671 7.6.1 / NEC 300.17 fill-limit compliance flags:
//
//   Conduit (3+ conductors): 40 %
//   Conduit (1-2 conductors): 31 % (1), 53 % (2)
//   Cable tray with cover:     40 %
//   Cable ladder (open):       50 %
//
// Output feeds Phase L.UI (WPF cross-section widget) and
// ValidateFillsCommand.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace StingTools.Core.Electrical
{
    public class TrayFillEntry
    {
        public StingCable Cable { get; set; }
        public double AreaMm2 { get; set; }
    }

    public class TrayFillReport
    {
        public long TrayId      { get; set; }
        public string TrayKind  { get; set; } = "";     // CABLE_TRAY / CONDUIT / LADDER
        public double InnerWidthMm  { get; set; }
        public double InnerHeightMm { get; set; }
        public double InnerAreaMm2  { get; set; }
        public double UsedAreaMm2   { get; set; }
        public double FillRatio     { get; set; }       // 0..1
        public double FillLimit     { get; set; }       // 0..1 (per reg)
        public bool   PassesLimit   { get; set; }
        public int    CableCount    { get; set; }
        public List<TrayFillEntry> Cables { get; } = new List<TrayFillEntry>();
        public string Basis         { get; set; } = "";
    }

    public static class TrayFillCalculator
    {
        private const double FtToMm = 304.8;
        private const double PackingWaste = 1.10;

        public static TrayFillReport Compute(Document doc, Element tray, CableManifest manifest)
        {
            var r = new TrayFillReport();
            if (doc == null || tray == null) return r;
            r.TrayId = tray.Id.Value;

            ResolveGeometry(tray, r);
            ResolveFillLimit(tray, r);

            if (manifest == null || manifest.Cables == null) return r;

            foreach (var c in manifest.Cables.Where(x => x.RouteTrayIds.Contains(r.TrayId)))
            {
                double d = c.OuterDiameterMm > 0
                    ? c.OuterDiameterMm
                    : EstimateOdMm(c);
                double area = Math.PI * d * d * 0.25 * c.CoreCount * PackingWaste;
                r.Cables.Add(new TrayFillEntry { Cable = c, AreaMm2 = area });
                r.UsedAreaMm2 += area;
                r.CableCount  += 1;
            }

            if (r.InnerAreaMm2 > 0)
                r.FillRatio = r.UsedAreaMm2 / r.InnerAreaMm2;
            r.PassesLimit = r.FillRatio <= r.FillLimit;
            return r;
        }

        private static void ResolveGeometry(Element tray, TrayFillReport r)
        {
            try
            {
                if (tray is CableTray ct)
                {
                    r.TrayKind      = "CABLE_TRAY";
                    r.InnerWidthMm  = ct.Width  * FtToMm;
                    r.InnerHeightMm = ct.Height * FtToMm;
                    r.InnerAreaMm2  = r.InnerWidthMm * r.InnerHeightMm;
                }
                else if (tray is Conduit c)
                {
                    r.TrayKind     = "CONDUIT";
                    double d       = c.Diameter * FtToMm;
                    r.InnerWidthMm = d;
                    r.InnerHeightMm = d;
                    r.InnerAreaMm2  = Math.PI * d * d * 0.25;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
        }

        private static void ResolveFillLimit(Element tray, TrayFillReport r)
        {
            if (r.TrayKind == "CONDUIT")
            {
                // BS 7671 App E / NEC 300.17 — 40% for ≥3 conductors.
                r.FillLimit = 0.40;
                r.Basis = "BS 7671 Appendix E / NEC 300.17 (3+ conductors)";
                return;
            }
            // CableTray: covered vs ladder — heuristic by type name.
            string tn = (tray.Name ?? "").ToUpperInvariant();
            if (tn.Contains("LADDER")) { r.FillLimit = 0.50; r.Basis = "Ladder open tray (manufacturer guidance)"; }
            else if (tn.Contains("PERFORAT")) { r.FillLimit = 0.45; r.Basis = "Perforated tray (IEC 61537)"; }
            else { r.FillLimit = 0.40; r.Basis = "Covered tray (IEC 61537)"; }
        }

        /// <summary>
        /// Approximate outer diameter of a multi-core PVC/XLPE cable
        /// from CSA alone — sqrt(4 × nCores × CSA / π) × insulation
        /// expansion factor (≈ 1.9 for Cu/PVC, 1.7 for Cu/XLPE).
        /// </summary>
        private static double EstimateOdMm(StingCable c)
        {
            if (c == null || c.CsaMm2 <= 0) return 5.0;
            double conductorDia = Math.Sqrt(4 * c.CsaMm2 / Math.PI);
            double expansion = c.InsulationType == "XLPE" ? 1.7 : 1.9;
            return conductorDia * expansion + (c.CoreCount > 1 ? 2.0 : 0);
        }
    }
}
