using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StingTools.Commands.Electrical.VoltageDrop
{
    /// <summary>
    /// Pure voltage-drop / breaker-sizing calculation engine. No Revit API
    /// dependency — fully unit-testable. All formulae per BS 7671:2018
    /// Appendix 4 (UK / IEC) and NEC 2023 Chapter 9 / Annex C (US).
    /// </summary>
    public static class VoltageDropEngine
    {
        // mΩ/m at 20 °C for plain annealed copper, class 1/2 conductors, indexed by
        // nominal mm² CSA — BS EN 60228:2005 Table 1/2 maximum DC resistance (Ω/km ≡ mΩ/m).
        // ELEC-5: this table previously carried 8.71 for 2.5 mm² (and 13.3 / 5.09 / 3.39
        // for 1.5 / 4 / 6), ~18 % high, and then temperature-corrected it AGAIN in
        // CalculateVoltDropPercent. It is now the 20 °C value with exactly one correction.
        // Cross-check: 2.5 mm² at 70 °C = 7.41 × (1 + 0.00393 × 50) = 8.87 mΩ/m, × 2 (go
        // and return) = 17.7 mV/A/m against 18 mV/A/m in BS 7671 Table 4D2B.
        private static readonly Dictionary<double, double> CopperResistanceMohmPerM = new()
        {
            { 1.0,   18.1   }, { 1.5,   12.1   }, { 2.5,   7.41   },
            { 4.0,   4.61   }, { 6.0,   3.08   }, { 10.0,  1.83   },
            { 16.0,  1.15   }, { 25.0,  0.727  }, { 35.0,  0.524  },
            { 50.0,  0.387  }, { 70.0,  0.268  }, { 95.0,  0.193  },
            { 120.0, 0.153  }, { 150.0, 0.124  }, { 185.0, 0.0991 },
            { 240.0, 0.0754 }, { 300.0, 0.0601 }, { 400.0, 0.0470 }
        };

        /// <summary>BS 7671 Appendix 12 (Table 4Ab) voltage-drop limits from the origin of
        /// a low-voltage installation supplied from a public distribution network.</summary>
        public const double Bs7671LightingLimitPct = 3.0;
        public const double Bs7671OtherUsesLimitPct = 5.0;

        /// <summary>Pick the limit for a circuit: lighting vs other uses (BS 7671 App 12).
        /// Replaces a 3 % / 2 % split keyed on pole count, which is not a BS 7671 rule.</summary>
        public static double LimitFor(bool isLighting, double lightingLimitPct, double otherLimitPct)
            => isLighting
                ? (lightingLimitPct > 0 ? lightingLimitPct : Bs7671LightingLimitPct)
                : (otherLimitPct > 0 ? otherLimitPct : Bs7671OtherUsesLimitPct);

        /// <summary>
        /// Standard mm² CSA sizes used across BS 7671 / IEC 60364.
        /// Returned smallest-first for stepwise size-up logic.
        /// </summary>
        public static readonly double[] StandardSizesMm2 =
        {
            1.0, 1.5, 2.5, 4.0, 6.0, 10.0, 16.0, 25.0, 35.0, 50.0,
            70.0, 95.0, 120.0, 150.0, 185.0, 240.0, 300.0, 400.0
        };

        /// <summary>
        /// Protective-device rating lists. The ONLY copy is STING_WIRE_TABLES.json →
        /// breakerSizes (DSCH-25): there is no built-in fallback. A list that is missing or
        /// invalid is empty and <see cref="LoadError"/> says why, so a sizer refuses
        /// ("no standard rating") instead of sizing against a hidden second copy.
        /// </summary>
        internal sealed class BreakerRatingSet
        {
            public int[] Mcb { get; set; } = new int[0];
            public int[] Mccb { get; set; } = new int[0];
            public int[] Nec { get; set; } = new int[0];
            public int[] NecFuseAdditional { get; set; } = new int[0];
            /// <summary>Null when every list loaded; otherwise every problem, joined.</summary>
            public string LoadError { get; set; }
        }

        public const string WireTablesFile = "STING_WIRE_TABLES.json";

        private static readonly Lazy<BreakerRatingSet> _breakerSizes =
            new Lazy<BreakerRatingSet>(LoadBreakerSizes);

        /// <summary>BS EN 60898 MCB ratings (STING_WIRE_TABLES.json breakerSizes.BS_EN_60898_MCB); empty when not loaded.</summary>
        public static int[] BreakerSizesBSMCB => _breakerSizes.Value.Mcb;

        /// <summary>BS EN 60947-2 MCCB ratings (breakerSizes.BS_EN_60947_MCCB); empty when not loaded.</summary>
        public static int[] BreakerSizesBSMCCB => _breakerSizes.Value.Mccb;

        /// <summary>NEC Table 240.6(A) fuse / inverse-time breaker ratings (breakerSizes.NEC_OCPD); empty when not loaded.</summary>
        public static int[] BreakerSizesNEC => _breakerSizes.Value.Nec;

        /// <summary>Why a rating list is empty, or null when all loaded. Show it to the user.</summary>
        public static string BreakerSizesLoadError => _breakerSizes.Value.LoadError;

        private static BreakerRatingSet LoadBreakerSizes()
        {
            JObject root = null;
            string fileError = null;
            try
            {
                string path = StingTools.Core.StingToolsApp.FindDataFile(WireTablesFile);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    fileError = WireTablesFile + " not found";
                else
                    root = JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                fileError = $"{WireTablesFile} could not be read: {ex.Message}";
            }
            var set = fileError != null
                ? new BreakerRatingSet { LoadError = fileError + " — breaker ratings not loaded; sizing refused." }
                : ResolveBreakerSizes(root);
            if (set.LoadError != null)
                StingTools.Core.StingLog.Error("VoltageDropEngine breaker ratings: " + set.LoadError);
            return set;
        }

        /// <summary>
        /// Rating lists from <paramref name="root"/> → breakerSizes. Each key that is
        /// missing or not a list of positive whole numbers gives an empty list and a line
        /// in <see cref="BreakerRatingSet.LoadError"/>; nothing falls back to a constant.
        /// </summary>
        internal static BreakerRatingSet ResolveBreakerSizes(JObject root)
        {
            var errors = new List<string>();
            var bs = root?["breakerSizes"] as JObject;
            if (bs == null) errors.Add($"{WireTablesFile} has no 'breakerSizes' object");
            int[] Read(string key, bool required)
            {
                if (bs == null) return new int[0];
                var tok = bs[key];
                if (tok == null)
                {
                    if (required) errors.Add($"breakerSizes.{key} is missing");
                    return new int[0];
                }
                var list = ReadRatings(tok);
                if (list == null)
                {
                    errors.Add($"breakerSizes.{key} is not a non-empty list of positive whole amperes");
                    return new int[0];
                }
                return list;
            }
            var set = new BreakerRatingSet
            {
                Mcb = Read("BS_EN_60898_MCB", true),
                Mccb = Read("BS_EN_60947_MCCB", true),
                Nec = Read("NEC_OCPD", true),
                NecFuseAdditional = Read("NEC_FUSE_ADDITIONAL", false)
            };
            if (errors.Count > 0)
                set.LoadError = string.Join("; ", errors) + " — sizing against the affected list is refused.";
            return set;
        }

        private static int[] ReadRatings(JToken tok)
        {
            if (!(tok is JArray arr) || arr.Count == 0) return null;
            var list = new List<int>(arr.Count);
            foreach (var t in arr)
            {
                if (t.Type != JTokenType.Integer && t.Type != JTokenType.Float) return null;
                double v = t.Value<double>();
                if (v <= 0 || v != Math.Floor(v)) return null;
                list.Add((int)v);
            }
            list.Sort();
            return list.ToArray();
        }

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
            // No copper-clad aluminium resistance is shipped: 0 (invalid), never copper's.
            if (StingTools.Standards.NEC2023.ConductorMaterialText.IsCopperClad(material)) return 0;
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
        /// Resistive only (BS EN 60228 R at 20 °C, corrected ONCE to the operating
        /// temperature); reactance is neglected, which understates the drop on large
        /// conductors (≳ 25 mm², where Table 4D2B tabulates x separately).
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
        /// Round up to the next BS EN 60898 MCB rating (In ≥ Ib, BS 7671 Reg 433.1.1(i)).
        /// Returns 0 when no rating fits (see <see cref="NextRating"/>).
        /// <paramref name="continuous"/> pre-multiplies by 1.25; that is an NEC rule
        /// (210.20(A)) with no BS 7671 counterpart, so BS callers should leave it false —
        /// BreakerSizerCommand no longer passes it on the BS path.
        /// </summary>
        public static int NextStandardBreakerSizeBS(double minimumA, bool continuous = false, bool useMCCB = false)
        {
            double effective = continuous ? minimumA * 1.25 : minimumA;
            return NextRating(useMCCB ? BreakerSizesBSMCCB : BreakerSizesBSMCB, effective);
        }

        /// <summary>
        /// Round up to the next NEC Table 240.6(A) standard rating. Pass continuous=true to
        /// pre-multiply by 1.25 per NEC 210.20(A). Returns 0 when no rating fits.
        /// </summary>
        public static int NextStandardBreakerSizeNEC(double minimumA, bool continuous = false)
        {
            double effective = continuous ? minimumA * 1.25 : minimumA;
            return NextRating(BreakerSizesNEC, effective);
        }

        /// <summary>
        /// Smallest rating ≥ <paramref name="minimumA"/>, or 0 when none fits (the load
        /// exceeds the largest rating, or the list did not load). It used to return the
        /// largest rating instead — a 500 A NEC load got a 400 A device, silently.
        /// 0 means "no standard device": the caller must say so, never use it as a rating.
        /// </summary>
        internal static int NextRating(int[] ratings, double minimumA)
        {
            if (ratings == null) return 0;
            foreach (int s in ratings) if (s >= minimumA) return s;
            return 0;
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
