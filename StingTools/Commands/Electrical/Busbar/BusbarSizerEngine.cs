using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Commands.Electrical.Busbar
{
    /// <summary>
    /// Pure-math busbar sizer per BS EN 60439-1 / IEC 61439-1 indicative
    /// table values. CSA / rating pairs are conservative copper flat-bar
    /// values at 35°C ambient; ambient-temperature derate is applied
    /// linearly above 35°C (-0.5 % per °C, floored at 50 %). Production
    /// hardening would extend the table per manufacturer (e.g. Schneider
    /// Canalis / GE Spectra) and add aluminium variants.
    /// </summary>
    public class BusbarSizeResult
    {
        public double CsaMm2     { get; set; }
        public double RatingA    { get; set; }
        public string Label      { get; set; } = "";
        public bool   Compliant  { get; set; }
        public string Warning    { get; set; } = "";
    }

    public static class BusbarSizerEngine
    {
        private static readonly (double CsaMm2, double RatingA, string Label)[] BusbarTable =
        {
            (100,  250,  "25×4"),
            (160,  350,  "40×4"),
            (200,  400,  "50×4"),
            (300,  530,  "60×5"),
            (400,  650,  "80×5"),
            (500,  750,  "100×5"),
            (600,  880,  "100×6"),
            (800,  1050, "100×8"),
            (1000, 1220, "100×10"),
            (1200, 1400, "120×10"),
            (1600, 1700, "160×10"),
            (2000, 2000, "200×10"),
        };

        public static BusbarSizeResult Size(double demandA, string standard = "BS7671", double ambientC = 35)
        {
            double designA = string.Equals(standard, "NEC", StringComparison.OrdinalIgnoreCase)
                ? demandA * 1.25
                : demandA * 1.00;
            double tempFactor = ambientC > 35 ? Math.Max(0.5, 1.0 - 0.005 * (ambientC - 35)) : 1.0;
            foreach (var (csa, rating, lbl) in BusbarTable)
            {
                double deRated = rating * tempFactor;
                if (deRated >= designA)
                    return new BusbarSizeResult
                    { CsaMm2 = csa, RatingA = deRated, Label = lbl, Compliant = true };
            }
            var last = BusbarTable.Last();
            return new BusbarSizeResult
            {
                CsaMm2 = last.CsaMm2, RatingA = last.RatingA * tempFactor, Label = last.Label,
                Compliant = false,
                Warning = $"Demand {designA:0}A exceeds tabulated maximum — consider parallel busbars."
            };
        }

        /// <summary>
        /// Trunking duct fill % = (effective conductor area × phase count) /
        /// duct internal area × 100. Effective area applies a 1.3 insulation
        /// factor over raw CSA per BS EN 60439-1 Annex C.
        /// </summary>
        public static double FillPercent(double ductInternalAreaMm2, double totalCsaMm2, int phases)
        {
            if (ductInternalAreaMm2 <= 0) return 0;
            int p = Math.Max(1, phases);
            double effective = totalCsaMm2 * p * 1.3;
            return effective / ductInternalAreaMm2 * 100.0;
        }
    }
}
