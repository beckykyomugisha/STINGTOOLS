using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Commands.Electrical.VoltageDrop
{
    /// <summary>
    /// Pure voltage-drop / breaker-sizing calculation engine. No Revit API
    /// dependency — fully unit-testable. All formulae per BS 7671:2018
    /// Appendix 4 (UK / IEC) and NEC 2023 Chapter 9 / Annex C (US).
    /// </summary>
    public static class VoltageDropEngine
    {
        // mΩ/m at 20°C for copper conductors, indexed by nominal mm² CSA.
        // Values from BS 7671 Appendix 4 Table 4D5B (XLPE single-phase loop).
        private static readonly Dictionary<double, double> CopperResistanceMohmPerM = new()
        {
            { 1.0,   20.0   }, { 1.5,   13.3   }, { 2.5,   8.71   },
            { 4.0,   5.09   }, { 6.0,   3.39   }, { 10.0,  1.83   },
            { 16.0,  1.15   }, { 25.0,  0.727  }, { 35.0,  0.524  },
            { 50.0,  0.387  }, { 70.0,  0.268  }, { 95.0,  0.193  },
            { 120.0, 0.153  }, { 150.0, 0.124  }, { 185.0, 0.101  },
            { 240.0, 0.0778 }, { 300.0, 0.0641 }, { 400.0, 0.0515 }
        };

        /// <summary>
        /// Standard mm² CSA sizes used across BS 7671 / IEC 60364.
        /// Returned smallest-first for stepwise size-up logic.
        /// </summary>
        public static readonly double[] StandardSizesMm2 =
        {
            1.0, 1.5, 2.5, 4.0, 6.0, 10.0, 16.0, 25.0, 35.0, 50.0,
            70.0, 95.0, 120.0, 150.0, 185.0, 240.0, 300.0, 400.0
        };

        public static readonly int[] BreakerSizesBSMCB =
        { 6, 10, 16, 20, 25, 32, 40, 50, 63, 80, 100, 125 };

        public static readonly int[] BreakerSizesBSMCCB =
        { 16, 20, 25, 32, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800, 1000, 1250, 1600 };

        public static readonly int[] BreakerSizesNEC =
        { 15, 20, 25, 30, 35, 40, 45, 50, 60, 70, 80, 90, 100, 110, 125, 150, 175, 200, 225, 250, 300, 350, 400 };

        /// <summary>
        /// Multiplier applied to copper resistance to obtain aluminium
        /// resistance for the same CSA (BS 7671 §523.6 — ratio ~1.61).
        /// </summary>
        public const double AluminiumResistanceFactor = 1.61;

        /// <summary>
        /// Look up baseline resistance at 20°C for a nominal CSA. Returns 0
        /// when the size is not tabulated; callers should treat that as
        /// invalid input.
        /// </summary>
        public static double BaseResistanceMohmPerM(double csaMm2, string material)
        {
            double nearestKey = CopperResistanceMohmPerM.Keys
                .OrderBy(k => Math.Abs(k - csaMm2))
                .FirstOrDefault();
            if (nearestKey <= 0) return 0;
            double baseR = CopperResistanceMohmPerM[nearestKey];
            return string.Equals(material, "Al", StringComparison.OrdinalIgnoreCase)
                ? baseR * AluminiumResistanceFactor
                : baseR;
        }

        /// <summary>
        /// Apply BS 7671 Appendix 4 temperature correction.
        /// R(T) = R(20°C) * (1 + α × (T - 20)) — α = 0.00393/K for copper.
        /// </summary>
        public static double TemperatureCorrection(double baseMohmPerM, double operatingTempC)
        {
            const double alpha = 0.00393;
            return baseMohmPerM * (1.0 + alpha * (operatingTempC - 20.0));
        }

        /// <summary>
        /// Resolve the conductor's operating temperature from the insulation
        /// rating string. Defaults to 70 °C (PVC) when the insulation isn't
        /// recognised. Centralised here so every consumer (VD calc, fault
        /// current, feeder sizer, auto-upsize) gets the same answer.
        /// </summary>
        public static double OperatingTempForInsulation(string insulation)
        {
            string norm = (insulation ?? "").ToUpperInvariant();
            if (norm.Contains("XLPE") || norm.Contains("LSOH") || norm.Contains("EPR")
                || norm.Contains("90")) return 90.0;
            if (norm.Contains("110") || norm.Contains("MICA")) return 110.0;
            if (norm.Contains("60")) return 60.0;
            if (norm.Contains("75") || norm.Contains("THWN")) return 75.0;
            return 70.0;  // PVC default (BS 7671 baseline)
        }

        /// <summary>
        /// Calculate voltage drop as a percentage of nominal system voltage.
        /// 1-phase: VD = 2 × I × L × R / 1000 / V
        /// 3-phase: VD = √3 × I × L × R / 1000 / V (line-to-line)
        /// L is one-way length in metres; R is mΩ/m at operating temperature.
        /// </summary>
        public static double CalculateVoltDropPercent(
            double currentA, double lengthM, double csaMm2,
            string material, double systemVoltageV, int phases,
            double operatingTempC = 70.0)
        {
            if (csaMm2 <= 0 || systemVoltageV <= 0 || lengthM < 0) return 0;
            double r = TemperatureCorrection(BaseResistanceMohmPerM(csaMm2, material), operatingTempC);
            if (r <= 0) return 0;
            double vDrop = phases == 3
                ? Math.Sqrt(3.0) * currentA * lengthM * r / 1000.0
                : 2.0 * currentA * lengthM * r / 1000.0;
            return (vDrop / systemVoltageV) * 100.0;
        }

        /// <summary>
        /// Find the smallest standard CSA whose voltage drop stays within
        /// <paramref name="maxVDPercent"/>. Returns null if no tabulated
        /// size satisfies the constraint at the given length and current.
        /// </summary>
        public static double? MinimumCsaForVDLimit(
            double currentA, double lengthM, string material,
            double systemVoltageV, int phases, double maxVDPercent,
            double operatingTempC = 70.0)
        {
            foreach (double size in StandardSizesMm2)
            {
                double vd = CalculateVoltDropPercent(currentA, lengthM, size,
                    material, systemVoltageV, phases, operatingTempC);
                if (vd > 0 && vd <= maxVDPercent) return size;
            }
            return null;
        }

        /// <summary>
        /// Round up to the next BS EN 60898 MCB rating. Pass continuous=true to
        /// pre-multiply by 1.25 (BS 7671 §433.1.1 / NEC 210.20(A) continuous-load rule).
        /// </summary>
        public static int NextStandardBreakerSizeBS(double minimumA, bool continuous = false, bool useMCCB = false)
        {
            double effective = continuous ? minimumA * 1.25 : minimumA;
            int[] sizes = useMCCB ? BreakerSizesBSMCCB : BreakerSizesBSMCB;
            foreach (int s in sizes) if (s >= effective) return s;
            return sizes[sizes.Length - 1];
        }

        /// <summary>
        /// Round up to the next NEC OCPD standard size. Pass continuous=true to
        /// pre-multiply by 1.25 per NEC 210.20(A).
        /// </summary>
        public static int NextStandardBreakerSizeNEC(double minimumA, bool continuous = false)
        {
            double effective = continuous ? minimumA * 1.25 : minimumA;
            foreach (int s in BreakerSizesNEC) if (s >= effective) return s;
            return BreakerSizesNEC[BreakerSizesNEC.Length - 1];
        }

        /// <summary>
        /// Convert nominal CSA in mm² to a printable label (e.g. "4mm²" or "10AWG").
        /// </summary>
        /// <summary>
        /// Render a metric cross-sectional area.
        ///
        /// <para><b>KUT-7 removed a <c>standard</c> parameter from this method, and the
        /// removal is the point.</b> When it was passed "NEC" it returned the "closest AWG
        /// approximation by CSA" — so a conductor chosen from BS 7671 Appendix 4, on the
        /// standard mm2 series, was PRINTED as an AWG size. That is not a unit conversion;
        /// it is a BS 7671 answer wearing an NEC designation, and nothing downstream could
        /// tell. NEC sizing now happens on NEC Table 310.16 and returns a real AWG / kcmil
        /// trade size from that table, so there is nothing left to approximate.</para>
        ///
        /// <para>The parameter was also never once supplied with a value that reached the
        /// AWG branch: the panel emits "NEC2023", this tested for "NEC". Restoring a
        /// standard argument here would re-open exactly that seam.</para>
        /// </summary>
        public static string FormatCsa(double csaMm2)
        {
            if (csaMm2 <= 0) return "—";
            return csaMm2 < 10 ? $"{csaMm2:0.0}mm²" : $"{(int)csaMm2}mm²";
        }
    }
}
