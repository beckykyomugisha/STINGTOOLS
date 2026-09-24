// StingTools v4 MVP — BS 7671 Appendix 4 voltage drop solver.
//
// For a cable carrying current I_b over length L, voltage drop is
//
//    V_drop  = (mV/A/m)_1ph × I_b × L / 1000     (single phase, 2-core column, V L-N loop)
//    V_drop  = (mV/A/m)_3ph × I_b × L / 1000     (three phase, 3/4-core column, V L-L)
//    V_drop_percent = V_drop / V_nominal × 100
//
// NominalVoltageV must therefore be L-N (230) for single phase and L-L (400)
// for three phase.
//
// ONE SOURCE OF mV/A/m. The figures are those of the Appendix 4 capacity table
// the BS 7671 cable sizer uses — STING_WIRE_TABLES.json → bs7671Appendix4, table
// 4D2A (method C) with its voltage-drop companion 4D2B. Callers that can load the
// data file pass the table in; without one, the built-in copy below is used, and
// a test pins it equal to the shipped file so the two cannot drift.
//
// Until 2026-09 this solver disagreed with the sizer twice over:
//   * 50–150 mm² held the 4D2B r (resistive) column (0.93 / 0.63 / 0.46 / 0.36 /
//     0.29). BS 7671 voltage drop uses z where r / x / z are tabulated
//     (0.94 / 0.65 / 0.49 / 0.39 / 0.33);
//   * three-phase was 1-ph × √3/2 rather than the tabulated 3/4-core column,
//     which differs by up to ~5 % at the large sizes.
// Aluminium is REFUSED: no aluminium table is shipped, and the old flat ×1.64
// multiplier was not a BS 7671 figure.
//
// BS 7671 Appendix 12 limits:
//   Lighting circuits   ≤ 3 % volt drop
//   Other circuits      ≤ 5 % volt drop

using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Electrical;

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
        /// <summary>Why no drop was computed (no table for the material, CSA outside
        /// the table). Empty when the result is a real calculation.</summary>
        public string Refusal      { get; set; } = "";
        public bool   Computed     => string.IsNullOrEmpty(Refusal) && MvPerAPerM > 0;
    }

    public static class VoltageDropSolver
    {
        /// <summary>
        /// Built-in copy of STING_WIRE_TABLES.json bs7671Appendix4 table 4D2A/4D2B
        /// (Cu, 70 °C thermoplastic, multicore, method C): csa, mV/A/m 2-core 1-ph,
        /// mV/A/m 3/4-core 3-ph (z where r/x/z are tabulated). Used only when the
        /// caller has no loaded table. Pinned to the data file by a test.
        /// </summary>
        public static readonly IReadOnlyList<(double Csa, double Mv1ph, double Mv3ph)> BuiltIn4D2B = new[]
        {
            (1.0,   44.0,  38.0),
            (1.5,   29.0,  25.0),
            (2.5,   18.0,  15.0),
            (4.0,   11.0,   9.5),
            (6.0,    7.3,   6.4),
            (10.0,   4.4,   3.8),
            (16.0,   2.8,   2.4),
            (25.0,   1.75,  1.5),
            (35.0,   1.25,  1.1),
            (50.0,   0.94,  0.81),
            (70.0,   0.65,  0.57),
            (95.0,   0.49,  0.42),
            (120.0,  0.39,  0.35),
            (150.0,  0.33,  0.29),
            (185.0,  0.28,  0.25),
            (240.0,  0.23,  0.21),
        };

        /// <summary>
        /// Solve with the given Appendix 4 table (from Bs7671Data.FindTable), or the
        /// built-in copy of the same table when <paramref name="table"/> is null.
        /// </summary>
        public static VoltageDropResult Solve(VoltageDropQuery q, Bs7671CapacityTable table = null)
        {
            var r = new VoltageDropResult();
            if (q == null || q.CsaMm2 <= 0 || q.LoadAmps <= 0 || q.LengthM <= 0 || q.NominalVoltageV <= 0)
            {
                r.Refusal = "Missing input (CSA, current, length or voltage is zero).";
                return r;
            }

            bool aluminium = !string.IsNullOrEmpty(q.Material)
                          && q.Material.Trim().ToUpperInvariant().StartsWith("AL");
            if (aluminium)
            {
                r.Refusal = "No BS 7671 Appendix 4 aluminium voltage-drop table is shipped — not approximated.";
                r.Basis = r.Refusal;
                return r;
            }

            string tableId;
            List<(double Csa, double Mv)> col;
            if (table != null && table.Rows.Count > 0)
            {
                col = table.Rows.Select(x => (x.CsaMm2, q.ThreePhase ? x.MvAm3ph : x.MvAm1ph))
                                .Where(x => x.Item2 > 0).OrderBy(x => x.Item1).ToList();
                tableId = string.IsNullOrEmpty(table.VoltDropTable) ? (table.Id ?? "Appendix 4") : table.VoltDropTable;
            }
            else
            {
                col = BuiltIn4D2B.Select(x => (x.Csa, q.ThreePhase ? x.Mv3ph : x.Mv1ph)).ToList();
                tableId = "4D2B";
            }

            double mv = Lookup(col, q.CsaMm2);
            if (mv <= 0)
            {
                r.Refusal = $"{q.CsaMm2:0.##} mm² is outside Table {tableId} " +
                            $"({col.First().Csa:0.##}–{col.Last().Csa:0.##} mm²).";
                r.Basis = r.Refusal;
                return r;
            }

            r.MvPerAPerM = mv;
            r.VoltDropV   = mv * q.LoadAmps * q.LengthM / 1000.0;
            r.VoltDropPct = r.VoltDropV / q.NominalVoltageV * 100.0;
            r.LightingPass = r.VoltDropPct <= 3.0;
            r.PowerPass    = r.VoltDropPct <= 5.0;
            r.Basis = $"BS 7671 Appendix 4 Table {tableId} (Cu 70 °C, " +
                      $"{(q.ThreePhase ? "3/4-core 3-ph column" : "2-core 1-ph column")}, " +
                      $"{mv:0.###} mV/A/m)";
            return r;
        }

        /// <summary>Exact row, else linear interpolation between the neighbours; 0 outside.</summary>
        private static double Lookup(List<(double Csa, double Mv)> col, double csa)
        {
            if (col == null || col.Count == 0) return 0;
            foreach (var x in col)
                if (Math.Abs(x.Csa - csa) < 1e-6) return x.Mv;
            for (int i = 0; i + 1 < col.Count; i++)
            {
                var lo = col[i]; var hi = col[i + 1];
                if (csa > lo.Csa && csa < hi.Csa)
                {
                    double t = (csa - lo.Csa) / (hi.Csa - lo.Csa);
                    return lo.Mv + t * (hi.Mv - lo.Mv);
                }
            }
            return 0;
        }
    }
}
