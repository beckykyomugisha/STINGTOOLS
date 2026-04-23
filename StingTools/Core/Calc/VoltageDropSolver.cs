// StingTools v4 MVP — BS 7671 Appendix 4 voltage drop solver.
//
// For a cable carrying current I_b over length L, voltage drop is
//
//    mV_drop = mV_per_A_per_m × I_b × L / 1000                (single phase)
//    V_drop  = mV_per_A_per_m × I_b × L × √3 / 1000           (three phase)
//    V_drop_percent = V_drop / V_nominal × 100
//
// BS 7671 Appendix 4 Table 4D1A (70 °C thermoplastic two-core cable,
// 30 °C ambient, enclosed installation) tabulates mV/A/m per CSA.
// Phase J ships the 70 °C Cu column — full matrix (Cu/Al × 70/90 °C
// × insulation) is a data-file drop in Phase J.2.
//
// BS 7671 limits:
//   Lighting circuits   ≤ 3 % volt drop
//   Power circuits       ≤ 5 % volt drop
// Lift / life-safety circuits have stricter limits handled separately.

using System;
using System.Collections.Generic;

namespace StingTools.Core.Calc
{
    public class VoltageDropQuery
    {
        public double CsaMm2          { get; set; } = 2.5;
        public double LoadAmps        { get; set; } = 10.0;
        public double LengthM         { get; set; } = 20.0;
        public double NominalVoltageV { get; set; } = 230.0;
        public bool   ThreePhase      { get; set; } = false;
        public string Material        { get; set; } = "CU";
    }

    public class VoltageDropResult
    {
        public double MvPerAPerM   { get; set; }
        public double VoltDropV    { get; set; }
        public double VoltDropPct  { get; set; }
        public bool   LightingPass { get; set; }  // ≤3%
        public bool   PowerPass    { get; set; }  // ≤5%
        public string Basis        { get; set; } = "";
    }

    public static class VoltageDropSolver
    {
        // BS 7671 Appendix 4 Table 4D1A column "Two-core flat cable"
        // (mV/A/m). Values valid for 70 °C thermoplastic copper cable
        // at full rated operating temperature. Aluminium ≈ 1.64×.
        private static readonly Dictionary<double, double> Cu70C = new Dictionary<double, double>
        {
            { 1.0,  44.0 },
            { 1.5,  29.0 },
            { 2.5,  18.0 },
            { 4.0,  11.0 },
            { 6.0,   7.3 },
            { 10.0,  4.4 },
            { 16.0,  2.8 },
            { 25.0,  1.75 },
            { 35.0,  1.25 },
            { 50.0,  0.93 },
            { 70.0,  0.63 },
            { 95.0,  0.46 },
            { 120.0, 0.36 },
            { 150.0, 0.29 },
        };

        public static VoltageDropResult Solve(VoltageDropQuery q)
        {
            var r = new VoltageDropResult();
            if (q == null || q.CsaMm2 <= 0 || q.LoadAmps <= 0 || q.LengthM <= 0 || q.NominalVoltageV <= 0)
                return r;

            if (!Cu70C.TryGetValue(q.CsaMm2, out double mv))
            {
                // Interpolate between the nearest tabulated CSAs.
                var lo = 0.0; var loV = 0.0; var hi = 0.0; var hiV = 0.0;
                foreach (var kv in Cu70C)
                {
                    if (kv.Key <= q.CsaMm2 && kv.Key > lo) { lo = kv.Key; loV = kv.Value; }
                    if (kv.Key >= q.CsaMm2 && (hi == 0 || kv.Key < hi)) { hi = kv.Key; hiV = kv.Value; }
                }
                if (lo == 0 || hi == 0) return r;
                double t = (q.CsaMm2 - lo) / (hi - lo);
                mv = loV + t * (hiV - loV);
            }
            // Aluminium correction per IEC 60364-5-52 Annex D.
            bool aluminium = !string.IsNullOrEmpty(q.Material)
                          && q.Material.ToUpperInvariant().StartsWith("AL");
            if (aluminium) mv *= 1.64;

            r.MvPerAPerM = mv;
            double factor = q.ThreePhase ? Math.Sqrt(3.0) : 1.0;
            r.VoltDropV   = mv * q.LoadAmps * q.LengthM * factor / 1000.0;
            r.VoltDropPct = r.VoltDropV / q.NominalVoltageV * 100.0;
            r.LightingPass = r.VoltDropPct <= 3.0;
            r.PowerPass    = r.VoltDropPct <= 5.0;
            r.Basis = $"BS 7671 Appendix 4 Table 4D1A ({(aluminium ? "Al" : "Cu")} 70 °C, " +
                      $"{(q.ThreePhase ? "3ph" : "1ph")})";
            return r;
        }
    }
}
