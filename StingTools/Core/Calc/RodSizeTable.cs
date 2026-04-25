// StingTools v4 MVP — MSS SP-58 rod-size table.
//
// Given a point load in kilograms on a single hanger, returns the
// smallest threaded-rod diameter (mm) whose published safe working
// load exceeds the demand, per MSS SP-58-2025 Table 4 (previously
// Table 3 in older editions).
//
// The table values are published safe-working-loads at ambient
// temperature; higher-temperature derates per MSS SP-58 §4.2 are
// applied via the TemperatureDerate() helper. Standard shop rods
// terminate at M20 (3/4") — M24+ demands a calculation per §4.3,
// flagged via Extrapolated=true.
//
// Stock-length handling: when the total calculated rod length
// exceeds StockLengthMm (default 3000), caller should insert a rod
// coupler to avoid specifying a non-stock cut.
//
// Reference: MSS SP-58-2025 "Pipe Hangers and Supports — Materials,
// Design, Manufacture, Selection, Application, and Installation".

using System;
using System.Collections.Generic;

namespace StingTools.Core.Calc
{
    public class RodSizeQuery
    {
        public double PointLoadKg    { get; set; }
        public double TemperatureC   { get; set; } = 20.0;
        public string Material       { get; set; } = "STEEL";   // STEEL / STAINLESS
    }

    public class RodSizeResult
    {
        public double RodDiameterMm   { get; set; }
        public string RodImperial     { get; set; } = "";
        public double SafeWorkingLoadKg { get; set; }
        public double UtilizationPct  { get; set; }
        public bool   Extrapolated    { get; set; }
        public string Basis           { get; set; } = "";
    }

    public static class RodSizeTable
    {
        /// <summary>
        /// Default stock length for threaded rod. Beyond this, a
        /// coupler is inserted. 3000 mm is the standard UK wholesale
        /// cut; US shops typically use 10 ft (3050 mm).
        /// </summary>
        public const double StockLengthMm = 3000.0;

        // MSS SP-58 Table 4 — mild-steel threaded rod safe working
        // loads at 21 °C. Key columns: nominal thread size
        // (imperial + metric) and SWL in kg (converted from lb).
        private static readonly (double dia_mm, string imp, double swl_kg)[] MssTable = new[]
        {
            ( 10.0, "3/8",     331.0 ),   //  730 lb
            ( 12.0, "1/2",     544.0 ),   // 1200 lb
            ( 16.0, "5/8",     884.0 ),   // 1950 lb
            ( 20.0, "3/4",    1315.0 ),   // 2900 lb
            ( 24.0, "7/8",    1837.0 ),   // 4050 lb
            ( 28.0, "1",      2449.0 ),   // 5400 lb
            ( 32.0, "1-1/4",  3651.0 ),
            ( 36.0, "1-3/8",  4263.0 ),
            ( 40.0, "1-1/2",  5195.0 ),
            ( 48.0, "1-3/4",  7008.0 ),
            ( 56.0, "2",      9163.0 ),
        };

        /// <summary>
        /// Pick the smallest rod whose SWL ≥ load × safety factor.
        /// MSS SP-58 embeds a factor of 5 already; caller may add an
        /// extra safety margin via ExtraSafetyFactor (default 1.0).
        /// </summary>
        public static RodSizeResult Select(RodSizeQuery q, double extraSafetyFactor = 1.0)
        {
            var r = new RodSizeResult();
            if (q == null || q.PointLoadKg <= 0)
            {
                r.Basis = "no-load";
                return r;
            }

            double derate = TemperatureDerate(q.TemperatureC, q.Material);
            double demand = q.PointLoadKg * extraSafetyFactor;

            foreach (var (dia, imp, swl) in MssTable)
            {
                double effSwl = swl * derate;
                if (effSwl >= demand)
                {
                    r.RodDiameterMm     = dia;
                    r.RodImperial       = imp;
                    r.SafeWorkingLoadKg = effSwl;
                    r.UtilizationPct    = 100.0 * demand / effSwl;
                    r.Basis             = $"MSS SP-58 Table 4 (derate {derate:F2})";
                    return r;
                }
            }

            var last = MssTable[MssTable.Length - 1];
            r.RodDiameterMm     = last.dia_mm;
            r.RodImperial       = last.imp;
            r.SafeWorkingLoadKg = last.swl_kg * derate;
            r.UtilizationPct    = 100.0 * demand / r.SafeWorkingLoadKg;
            r.Extrapolated      = true;
            r.Basis             = "MSS SP-58 Table 4 (above largest tabulated — verify by calc)";
            return r;
        }

        /// <summary>
        /// Temperature derate per MSS SP-58 §4.2. Mild steel keeps
        /// 100% SWL up to 343 °C, then linear decay to 70% at 427 °C.
        /// Stainless (Type 304/316) has a shallower curve.
        /// </summary>
        private static double TemperatureDerate(double tempC, string material)
        {
            bool stainless = !string.IsNullOrEmpty(material)
                          && material.ToUpperInvariant().Contains("STAIN");
            if (tempC <= 20.0) return 1.0;
            if (!stainless)
            {
                if (tempC <= 343.0) return 1.0;
                if (tempC >= 427.0) return 0.70;
                return 1.0 - 0.30 * (tempC - 343.0) / (427.0 - 343.0);
            }
            else
            {
                if (tempC <= 427.0) return 1.0;
                if (tempC >= 538.0) return 0.80;
                return 1.0 - 0.20 * (tempC - 427.0) / (538.0 - 427.0);
            }
        }
    }
}
