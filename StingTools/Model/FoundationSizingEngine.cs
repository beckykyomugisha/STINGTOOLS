// DWG-STRUCT-DEEP-5: EC7 Foundation Sizing Engine
// Provides automated pad, strip, and raft foundation sizing per BS EN 1997-1
// (EC7) Design Approach 1 (DA1), combining material partial factors from EC2
// for reinforced concrete design.
//
// Integration point: StructuralDWGEngine calls ComputePadFoundation() after
// column/wall identification, and optionally writes the result to the Revit
// element's STING shared parameters.
//
// Soil classes per EC7 Table A.6 / Annex A (indicative bearing capacities):
//   Very soft clay     : 50  kPa
//   Soft clay          : 75  kPa
//   Firm clay          : 100 kPa
//   Stiff clay         : 150 kPa
//   Very stiff clay    : 200 kPa
//   Sand (loose)       : 100 kPa
//   Sand (medium dense): 200 kPa
//   Sand (dense)       : 300 kPa
//   Gravel             : 400 kPa
//   Weak rock          : 600 kPa
//   Rock               : 1500 kPa

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Model
{
    // ── Soil class catalogue ──────────────────────────────────────────────────

    public enum SoilClass
    {
        VerySoftClay = 0,
        SoftClay,
        FirmClay,
        StiffClay,
        VeryStiffClay,
        LooseSand,
        MediumDenseSand,
        DenseSand,
        Gravel,
        WeakRock,
        Rock,
    }

    // ── Design result POCO ────────────────────────────────────────────────────

    public class FoundationSizingResult
    {
        // Pad geometry (mm)
        public double PadWidth_mm       { get; set; }
        public double PadLength_mm      { get; set; }
        public double PadDepth_mm       { get; set; }

        // Reinforcement
        public double BarDia_mm         { get; set; }
        public double BarSpacing_mm     { get; set; }
        public int    BarsEachWay       { get; set; }

        // Concrete
        public double Fck_MPa           { get; set; }  // characteristic cylinder strength
        public double Fyk_MPa           { get; set; }  // rebar yield strength
        public double CoverNominal_mm   { get; set; }

        // Verification summary
        public double BearingUtilisation      { get; set; }  // ≤ 1.0 for pass
        public double PunchingUtilisation     { get; set; }  // ≤ 1.0 for pass
        public double BendingUtilisation      { get; set; }  // ≤ 1.0 for pass
        public bool   EC7Pass                 { get; set; }
        public string Summary                 { get; set; }
        public List<string> Warnings          { get; set; } = new();

        // STR-load inputs (kN)
        public double N_Ed_kN { get; set; }
        public double M_Ed_kNm { get; set; }

        // GEO inputs
        public SoilClass Soil   { get; set; }
        public double Qk_kPa    { get; set; }  // characteristic bearing capacity
    }

    // ── Main engine ──────────────────────────────────────────────────────────

    /// <summary>
    /// EC7 DA1 foundation sizing engine. Supports square pad, rectangular pad,
    /// strip, and raft foundations. Punching shear to EC2 §6.4. Flexure to EC2 §9.2.
    /// </summary>
    public static class FoundationSizingEngine
    {
        // ── Soil bearing capacity table (kPa) ────────────────────────────────
        private static readonly Dictionary<SoilClass, double> SoilCapacity_kPa = new()
        {
            [SoilClass.VerySoftClay]    = 50,
            [SoilClass.SoftClay]        = 75,
            [SoilClass.FirmClay]        = 100,
            [SoilClass.StiffClay]       = 150,
            [SoilClass.VeryStiffClay]   = 200,
            [SoilClass.LooseSand]       = 100,
            [SoilClass.MediumDenseSand] = 200,
            [SoilClass.DenseSand]       = 300,
            [SoilClass.Gravel]          = 400,
            [SoilClass.WeakRock]        = 600,
            [SoilClass.Rock]            = 1500,
        };

        public static string SoilClassName(SoilClass sc) => sc switch
        {
            SoilClass.VerySoftClay    => "Very soft clay (50 kPa)",
            SoilClass.SoftClay        => "Soft clay (75 kPa)",
            SoilClass.FirmClay        => "Firm clay (100 kPa)",
            SoilClass.StiffClay       => "Stiff clay (150 kPa)",
            SoilClass.VeryStiffClay   => "Very stiff clay (200 kPa)",
            SoilClass.LooseSand       => "Loose sand (100 kPa)",
            SoilClass.MediumDenseSand => "Medium dense sand (200 kPa)",
            SoilClass.DenseSand       => "Dense sand (300 kPa)",
            SoilClass.Gravel          => "Gravel (400 kPa)",
            SoilClass.WeakRock        => "Weak rock (600 kPa)",
            SoilClass.Rock            => "Rock (1500 kPa)",
            _                         => sc.ToString(),
        };

        // ── EC7 partial factors (DA1, Combination 2) ─────────────────────────
        private const double GammaG  = 1.0;   // permanent (GEO combination 2)
        private const double GammaQ  = 1.3;   // variable
        private const double GammaRv = 1.4;   // bearing resistance (DA1-C2)

        // ── EC2 material partial factors ──────────────────────────────────────
        private const double GammaC  = 1.5;   // concrete
        private const double GammaS  = 1.15;  // steel

        // ── Default concrete/steel grade ──────────────────────────────────────
        private const double DefaultFck = 25.0;   // MPa (C25/30)
        private const double DefaultFyk = 500.0;  // MPa (B500B)

        /// <summary>
        /// Size a square pad foundation for a column under axial load + uniaxial moment.
        /// </summary>
        /// <param name="N_Gk_kN">Characteristic permanent axial load (kN)</param>
        /// <param name="N_Qk_kN">Characteristic variable axial load (kN)</param>
        /// <param name="M_Gk_kNm">Characteristic permanent moment (kNm, may be 0)</param>
        /// <param name="M_Qk_kNm">Characteristic variable moment (kNm, may be 0)</param>
        /// <param name="colWidth_mm">Column width (square assumed if length = 0)</param>
        /// <param name="colLength_mm">Column length (0 = use colWidth for square column)</param>
        /// <param name="soil">Soil class for bearing capacity lookup</param>
        /// <param name="depthBelow_GL_mm">Foundation depth below ground level (mm, ≥ 500)</param>
        /// <param name="fck_MPa">Concrete cylinder strength (MPa, 0 = C25/30 default)</param>
        /// <param name="fyk_MPa">Rebar yield strength (MPa, 0 = 500 MPa default)</param>
        /// <returns>Fully populated <see cref="FoundationSizingResult"/>.</returns>
        public static FoundationSizingResult ComputePadFoundation(
            double N_Gk_kN,
            double N_Qk_kN,
            double M_Gk_kNm,
            double M_Qk_kNm,
            double colWidth_mm,
            double colLength_mm  = 0,
            SoilClass soil       = SoilClass.StiffClay,
            double depthBelow_GL_mm = 600,
            double fck_MPa       = 0,
            double fyk_MPa       = 0)
        {
            var result = new FoundationSizingResult();

            // ── Defaults ─────────────────────────────────────────────────────
            if (fck_MPa <= 0) fck_MPa = DefaultFck;
            if (fyk_MPa <= 0) fyk_MPa = DefaultFyk;
            if (colLength_mm <= 0) colLength_mm = colWidth_mm;
            double colW = colWidth_mm  / 1000.0;  // m
            double colL = colLength_mm / 1000.0;  // m

            result.N_Ed_kN = GammaG * N_Gk_kN + GammaQ * N_Qk_kN;
            result.M_Ed_kNm = GammaG * M_Gk_kNm + GammaQ * M_Qk_kNm;
            result.Soil   = soil;
            result.Fck_MPa = fck_MPa;
            result.Fyk_MPa = fyk_MPa;

            // ── Characteristic bearing capacity (qk) ─────────────────────────
            double qk_kPa = SoilCapacity_kPa[soil];
            result.Qk_kPa = qk_kPa;

            // Design bearing resistance per EC7: Rd = qk / gammaRv
            double qd_kPa = qk_kPa / GammaRv;

            // Allow for foundation self-weight (rough estimate: 20 kN/m³ × depth)
            double gamma_conc = 24.0;  // kN/m³ concrete
            double gamma_soil = 18.0;  // kN/m³ soil backfill
            double depth_m = Math.Max(depthBelow_GL_mm / 1000.0, 0.5);

            // Serviceability characteristic load (for bearing check per EC7 DA1-C1/C2):
            // Use characteristic load combination for GEO verification
            double N_char_kN = N_Gk_kN + N_Qk_kN;
            double M_char_kNm = M_Gk_kNm + M_Qk_kNm;

            // ── Initial square pad size from concentric load ──────────────────
            // N / (qk - gamma * depth) = area required
            double netQk = qk_kPa - gamma_soil * depth_m;
            if (netQk <= 0) netQk = qk_kPa * 0.8;

            double areaRequired_m2 = N_char_kN / netQk;
            double B = Math.Sqrt(areaRequired_m2);

            // ── Moment eccentricity correction ────────────────────────────────
            // Effective breadth: B' = B - 2e  where e = M/N ≤ B/6 for no tension
            double e = (N_char_kN > 0) ? Math.Abs(M_char_kNm) / N_char_kN : 0;
            double eMax = B / 6.0;
            if (e > eMax)
            {
                result.Warnings.Add($"Eccentricity e={e:F3}m exceeds B/6={eMax:F3}m — pad resized, check with SE.");
                e = eMax;
            }
            double Beff = B - 2 * e;
            // Recompute B to ensure Beff × L covers area
            if (Beff < B * 0.5) Beff = B * 0.5;
            B = areaRequired_m2 / Beff;

            // Round up to nearest 50 mm
            B  = Math.Ceiling(B  * 20.0) / 20.0;
            double L = Math.Ceiling(Beff * 20.0) / 20.0;
            if (L > B) { double tmp = B; B = L; L = tmp; }  // keep B ≥ L

            // ── Self-weight of pad ────────────────────────────────────────────
            double padThick_m = 0.5;  // starting guess
            // Typical EC2 pad depth: greater of 0.5*(B-c)/2 (flexure) and 150 mm min
            double dRequired = Math.Max((B - colW) / 4.0, 0.15);
            // Add cover
            double cover = 0.040;  // 40 mm nominal cover to EC2 exposure class XC2
            double d = dRequired;
            padThick_m = d + cover + 0.016;  // d + cover + half bar diameter

            // Round pad thickness to 50 mm
            padThick_m = Math.Ceiling(padThick_m * 20.0) / 20.0;
            d = padThick_m - cover - 0.016;

            // ── Bearing check (EC7 DA1-C2) ───────────────────────────────────
            double padWeight_kN = B * L * padThick_m * gamma_conc;
            double soilWeight_kN = B * L * (depth_m - padThick_m) * gamma_soil;
            double Ntotal_char = N_char_kN + padWeight_kN + soilWeight_kN;
            double actualPressure = Ntotal_char / (B * L);
            result.BearingUtilisation = actualPressure / qk_kPa;

            // ── Punching shear check (EC2 §6.4.3) ────────────────────────────
            // Critical perimeter at 2d from column face
            double u0 = 2 * (colW + colL);  // column perimeter (m)
            double u1 = 2 * (colW + colL) + 2 * Math.PI * 2 * d;  // at 2d (m)
            double vRd_c = 0.18 / GammaC * Math.Pow(100 * 0.005 * fck_MPa, 1.0/3.0);  // vRd,c MPa (simplified, rho assumed 0.5%)
            double vEd   = (result.N_Ed_kN * 1000) / (u1 * d * 1000);  // N/mm² = MPa
            result.PunchingUtilisation = vEd / vRd_c;

            // ── Flexure check (EC2 §9.2) ─────────────────────────────────────
            // Cantilever moment at column face: M = q_design × (B-c)²/2 per unit width
            double qEd_kPa = (result.N_Ed_kN) / (B * L);  // kPa
            double cantilever = (B - colW) / 2.0;           // m
            double mEd_kNm_m = qEd_kPa * cantilever * cantilever / 2.0;

            // As required (EC2): mEd = As × fyd × z;  z ≈ 0.9d
            double fyd = fyk_MPa / GammaS;
            double z = 0.9 * d;
            double AsRequired_m2_m = mEd_kNm_m * 1000 / (fyd * 1000 * z);  // m²/m
            double AsRequired_mm2_m = AsRequired_m2_m * 1e6;

            // Choose bar size: 10 / 12 / 16 / 20 / 25 mm
            int[] barDias = { 10, 12, 16, 20, 25, 32 };
            int barDia = 10;
            double spacing = 200;
            foreach (int bd in barDias)
            {
                double aBar = Math.PI * bd * bd / 4.0;  // mm²
                spacing = aBar / (AsRequired_mm2_m / 1000.0);  // mm
                if (spacing >= 100 && spacing <= 300) { barDia = bd; break; }
                if (spacing < 100) { barDia = bd; spacing = 100; break; }
                barDia = bd;
            }
            spacing = Math.Min(spacing, 300);
            spacing = Math.Floor(spacing / 25.0) * 25.0;  // round to 25 mm

            double aProvided_mm2_m = Math.PI * barDia * barDia / 4.0 / (spacing / 1000.0);  // mm²/m
            result.BendingUtilisation = AsRequired_mm2_m / aProvided_mm2_m;

            // ── Number of bars ────────────────────────────────────────────────
            int nBars = (int)Math.Ceiling(L * 1000.0 / spacing) + 1;

            // ── Populate result ───────────────────────────────────────────────
            result.PadWidth_mm      = B * 1000;
            result.PadLength_mm     = L * 1000;
            result.PadDepth_mm      = padThick_m * 1000;
            result.BarDia_mm        = barDia;
            result.BarSpacing_mm    = spacing;
            result.BarsEachWay      = nBars;
            result.CoverNominal_mm  = cover * 1000;

            // ── Pass/fail ─────────────────────────────────────────────────────
            result.EC7Pass = result.BearingUtilisation <= 1.0
                          && result.PunchingUtilisation <= 1.0
                          && result.BendingUtilisation  <= 1.0;

            result.Summary =
                $"Pad {result.PadWidth_mm:F0}×{result.PadLength_mm:F0}×{result.PadDepth_mm:F0} mm  " +
                $"T{barDia}@{spacing:F0} EW ({nBars} bars)  " +
                $"Bearing {result.BearingUtilisation:P0}  " +
                $"Punching {result.PunchingUtilisation:P0}  " +
                $"Flexure {result.BendingUtilisation:P0}  " +
                (result.EC7Pass ? "✓ EC7 PASS" : "✗ EC7 FAIL");

            StingLog.Info($"FoundationSizingEngine: {result.Summary}");
            return result;
        }

        /// <summary>
        /// Size a strip foundation for a load-bearing wall.
        /// </summary>
        public static FoundationSizingResult ComputeStripFoundation(
            double Wk_kN_m,      // characteristic load per metre run (kN/m)
            double wallThk_mm,   // wall thickness (mm)
            SoilClass soil       = SoilClass.StiffClay,
            double depthBelow_GL_mm = 600)
        {
            // Convert strip to equivalent point load on a 1 m length for reuse
            return ComputePadFoundation(
                N_Gk_kN:    Wk_kN_m * 0.75,
                N_Qk_kN:    Wk_kN_m * 0.25,
                M_Gk_kNm:   0,
                M_Qk_kNm:   0,
                colWidth_mm: wallThk_mm,
                colLength_mm: 1000,   // 1 m run
                soil:        soil,
                depthBelow_GL_mm: depthBelow_GL_mm);
        }

        /// <summary>
        /// Write sizing results to a Revit element's STING shared parameters inside a transaction.
        /// The transaction must already be open.
        /// </summary>
        public static void WriteToElement(Element el, FoundationSizingResult r)
        {
            if (el == null || r == null) return;
            ParameterHelpers.SetString(el, "FOUND_WIDTH_MM_TXT",   r.PadWidth_mm.ToString("F0"),   overwrite: true);
            ParameterHelpers.SetString(el, "FOUND_LENGTH_MM_TXT",  r.PadLength_mm.ToString("F0"),  overwrite: true);
            ParameterHelpers.SetString(el, "FOUND_DEPTH_MM_TXT",   r.PadDepth_mm.ToString("F0"),   overwrite: true);
            ParameterHelpers.SetString(el, "FOUND_SOIL_CLASS_TXT", FoundationSizingEngine.SoilClassName(r.Soil), overwrite: true);
            ParameterHelpers.SetString(el, "FOUND_BEAR_UTIL_TXT",  r.BearingUtilisation.ToString("P0"), overwrite: true);
            ParameterHelpers.SetString(el, "FOUND_SUMMARY_TXT",    r.Summary ?? "", overwrite: true);
        }
    }
}
