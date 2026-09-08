using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Commands.Electrical.VoltageDrop;
using StingTools.Core;

namespace StingTools.Commands.Electrical.CableSizer
{
    /// <summary>
    /// Inputs for a single cable-sizing calculation. All fields are required;
    /// the caller is responsible for unit conversions before calling.
    /// </summary>
    public class CableSizeInput
    {
        public double LoadKW { get; set; }
        public double VoltageV { get; set; } = 230.0;
        public double PowerFactor { get; set; } = 0.85;
        public double LengthM { get; set; }
        /// <summary>Install method per BS 7671 Appendix 4 (A1/A2/B1/B2/C/E/F)
        /// or "Conduit" / "DirectBuried" for NEC.</summary>
        public string InstallMethod { get; set; } = "C";
        /// <summary>Conductor material — "Cu" or "Al".</summary>
        public string Material { get; set; } = "Cu";
        /// <summary>"PVC70" | "XLPE90" | "LSOH90" | "THWN90".</summary>
        public string Insulation { get; set; } = "XLPE90";
        public double VDLimitPct { get; set; } = 3.0;
        /// <summary>"BS7671" | "NEC" | "IEC60364".</summary>
        public string Standard { get; set; } = "BS7671";
        public int Phases { get; set; } = 1;
        public double AmbientTempC { get; set; } = 30.0;
        public bool ContinuousLoad { get; set; } = false;
    }

    public class CableSizeResult
    {
        public double DesignCurrentA { get; set; }
        public double RecommendedCsaMm2 { get; set; }
        public string CsaLabel { get; set; } = "—";
        public double ActualVoltDropPct { get; set; }
        public bool VDCompliant { get; set; }
        public int ProposedBreakerA { get; set; }
        public string Warning { get; set; } = "";
        public string DerivationNote { get; set; } = "";

        /// <summary>KUT-7 — the canonical standard this result was calculated UNDER,
        /// not the one that was asked for. They differ only when the request was
        /// refused, in which case this is null and <see cref="Sized"/> is false.</summary>
        public string StandardId { get; set; }

        /// <summary>The clause-level basis of the calculation, for the derivation note
        /// and for anything that has to defend the number later.</summary>
        public string StandardBasis { get; set; } = "";

        /// <summary>False when the engine declined to size — an unsupported standard, or
        /// no tabulated size that satisfies the constraints. <b>A caller must check this
        /// before writing <see cref="RecommendedCsaMm2"/> anywhere</b>: a refusal leaves
        /// it at 0, and 0 written into a parameter reads as "not yet sized" rather than
        /// as "we refused", which is the same silent-zero failure this gap is about.</summary>
        public bool Sized { get; set; }
    }

    /// <summary>
    /// Pure cable-sizing engine. No Revit API; safe to unit-test on Linux.
    /// Loads correction factors lazily from STING_WIRE_TABLES.json — falls back
    /// to embedded defaults when the data file is absent.
    /// </summary>
    public static class CableSizerEngine
    {
        private static JObject _wireTables;
        private static readonly object _loadLock = new object();

        /// <summary>Force the engine to reload the JSON on next use.</summary>
        public static void InvalidateCache() { lock (_loadLock) _wireTables = null; }

        private static JObject LoadWireTables()
        {
            lock (_loadLock)
            {
                if (_wireTables != null) return _wireTables;
                try
                {
                    string path = StingTools.Core.StingToolsApp.FindDataFile("STING_WIRE_TABLES.json");
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        _wireTables = JObject.Parse(File.ReadAllText(path));
                        return _wireTables;
                    }
                }
                catch (Exception ex)
                {
                    StingTools.Core.StingLog.Warn($"CableSizerEngine.LoadWireTables: {ex.Message}");
                }
                _wireTables = new JObject();
                return _wireTables;
            }
        }

        public static double InstallMethodFactor(string method)
        {
            var tables = LoadWireTables();
            try
            {
                var v = tables["correctionFactors"]?["installMethods"]?[method];
                if (v != null) return v.Value<double>();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return method switch
            {
                "A1" => 0.77, "A2" => 0.77, "B1" => 0.88, "B2" => 0.88,
                "C" => 1.00, "E" => 1.17, "F" => 1.21,
                "Conduit" => 1.00, "DirectBuried" => 0.93,
                _ => 1.0
            };
        }

        public static double InsulationFactor(string insulation)
        {
            var tables = LoadWireTables();
            try
            {
                var v = tables["correctionFactors"]?["insulation"]?[insulation];
                if (v != null) return v.Value<double>();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return insulation switch
            {
                "PVC70" => 1.0, "XLPE90" => 1.18, "LSOH90" => 1.18,
                "THWN90" => 1.0, _ => 1.0
            };
        }

        public static double AmbientTemperatureFactor(double ambientTempC)
        {
            // Interpolated linearly from BS 7671 Appendix 4 Table 4B1 (PVC 70°C).
            if (ambientTempC <= 25) return 1.05;
            if (ambientTempC <= 30) return 1.00;
            if (ambientTempC <= 35) return 0.94;
            if (ambientTempC <= 40) return 0.87;
            if (ambientTempC <= 45) return 0.79;
            return 0.71;
        }

        /// <summary>
        /// Operating temperature used for resistance correction. Pulled from
        /// the insulation rating; defaults to 70°C (PVC).
        /// </summary>
        public static double OperatingTemperature(string insulation)
        {
            return insulation switch
            {
                "PVC70" => 70.0, "XLPE90" => 90.0,
                "LSOH90" => 90.0, "THWN90" => 75.0,
                _ => 70.0
            };
        }

        /// <summary>
        /// Compute design current from kW / V / PF / phase count.
        /// 3-phase: I = kW × 1000 / (√3 × V × PF)
        /// 1-phase: I = kW × 1000 / (V × PF)
        /// </summary>
        public static double DesignCurrent(double loadKW, double voltageV, double pf, int phases)
        {
            if (voltageV <= 0 || pf <= 0) return 0;
            double watts = loadKW * 1000.0;
            return phases == 3
                ? watts / (Math.Sqrt(3.0) * voltageV * pf)
                : watts / (voltageV * pf);
        }

        /// <summary>
        /// Size a conductor under the standard named by <see cref="CableSizeInput.Standard"/>.
        ///
        /// <para>KUT-7. This used to be one BS 7671 Appendix 4 calculation for every
        /// standard, with the standard consulted in exactly two places afterwards: the
        /// breaker lookup, and a routine that renamed the resulting mm2 to the nearest
        /// AWG. NEC 2023 now routes to <c>NECStandards</c> — Table 310.16 and the 310.15
        /// corrections — so an AWG answer comes from the AWG table. AS/NZS 3000 is
        /// REFUSED, because its tables are not in this tree and a cable size carrying a
        /// standard's name onto a drawing must come from that standard.</para>
        /// </summary>
        public static CableSizeResult Calculate(CableSizeInput input)
        {
            var result = new CableSizeResult();
            if (input == null) { result.Warning = "Null input"; return result; }

            string standardId = StingTools.Standards.ElectricalStandardId.Normalise(input.Standard);
            result.StandardId = standardId;
            if (!StingTools.Standards.ElectricalStandardId
                    .SupportsConductorSizing(standardId, out string basis, out string refusal))
            {
                // Refuse rather than hand back a BS 7671 size wearing another standard's
                // name. Downstream cannot tell the two apart, and a cable size is written
                // to a drawing.
                result.Sized = false;
                result.Warning = refusal;
                result.DerivationNote = "No calculation performed.";
                StingLog.Warn($"CableSizerEngine: refused to size under " +
                              $"{StingTools.Standards.ElectricalStandardId.Label(standardId)} — tables not shipped.");
                return result;
            }
            result.StandardBasis = basis;

            double iB = DesignCurrent(input.LoadKW, input.VoltageV, input.PowerFactor, input.Phases);
            result.DesignCurrentA = iB;
            if (iB <= 0)
            {
                result.Warning = "Invalid load / voltage / PF — cannot compute design current.";
                return result;
            }

            if (standardId == StingTools.Standards.ElectricalStandardId.Nec2023)
                return CalculateNec(input, result, iB);

            double cf = InstallMethodFactor(input.InstallMethod)
                      * InsulationFactor(input.Insulation)
                      * AmbientTemperatureFactor(input.AmbientTempC);
            if (cf <= 0) cf = 1.0;

            double effectiveCurrent = iB / cf;
            double opTemp = OperatingTemperature(input.Insulation);
            double maxVD = input.VDLimitPct > 0 ? input.VDLimitPct : 3.0;

            // Iterate up the standard sizes; pick the first CSA whose VD is OK.
            double? winner = null;
            double winnerVd = 0;
            foreach (double csa in VoltageDropEngine.StandardSizesMm2)
            {
                if (csa < CrossSectionForCurrent(effectiveCurrent, input.Material))
                    continue;
                double vd = VoltageDropEngine.CalculateVoltDropPercent(
                    iB, input.LengthM, csa, input.Material,
                    input.VoltageV, input.Phases, opTemp);
                if (vd > 0 && vd <= maxVD)
                {
                    winner = csa;
                    winnerVd = vd;
                    break;
                }
            }

            if (winner == null)
            {
                result.Sized = false;
                result.Warning = "No tabulated size satisfies the voltage-drop limit at this length / current.";
                return result;
            }

            result.RecommendedCsaMm2 = winner.Value;
            // The mm2 series IS this standard's series, so the label needs no translation.
            result.CsaLabel = $"{VoltageDropEngine.FormatCsa(winner.Value)} {input.Material}/{input.Insulation}";
            result.ActualVoltDropPct = winnerVd;
            result.VDCompliant = winnerVd <= maxVD;
            result.Sized = true;

            // BS 7671 §433.1.1 / IEC 60364-4-43: In >= Ib, and 1.45 x In <= Iz.
            result.ProposedBreakerA = VoltageDropEngine.NextStandardBreakerSizeBS(iB, input.ContinuousLoad);

            result.DerivationNote =
                $"Ib={iB:0.0}A, CF={cf:0.00} (method={input.InstallMethod} ins={input.Insulation} ta={input.AmbientTempC}°C), " +
                $"opT={opTemp:0}°C, target VD ≤ {maxVD:0.0}% — {result.StandardBasis}";
            return result;
        }

        /// <summary>
        /// NEC 2023 conductor sizing, on the NEC's own tables.
        ///
        /// <para>Clause by clause: ampacity from <b>Table 310.16</b> at the 75 °C column
        /// (the termination limit for equipment rated over 100 A, and the column
        /// 110.14(C)(1) drives most designs to); ambient correction from <b>Table
        /// 310.15(B)(1)</b>; more than three current-carrying conductors adjusted per
        /// <b>310.15(C)(1)</b>; overcurrent device from the standard ratings in
        /// <b>240.6(A)</b>; a continuous load carried at 125% per <b>210.19(A)(1)</b> and
        /// <b>215.2(A)(1)</b>; and the small-conductor limit of <b>240.4(D)</b> applied to
        /// 14, 12 and 10 AWG.</para>
        ///
        /// <para>Voltage drop is NOT a NEC requirement. 210.19(A) Informational Note 4 and
        /// 215.2(A) Informational Note 2 RECOMMEND 3% on a branch circuit and 5% overall;
        /// the engine reports the figure and flags it against the caller's limit, but a
        /// conductor is not upsized for it here, because doing so would enforce as a rule
        /// something the code offers as advice.</para>
        /// </summary>
        private static CableSizeResult CalculateNec(CableSizeInput input, CableSizeResult result, double iB)
        {
            try
            {
                // 210.19(A)(1) / 215.2(A)(1) - a continuous load is carried at 125%.
                double sizingCurrent = input.ContinuousLoad ? iB * 1.25 : iB;

                var material = string.Equals(input.Material, "Al", StringComparison.OrdinalIgnoreCase)
                    ? StingTools.Standards.NEC2023.ConductorMaterial.Aluminum
                    : StingTools.Standards.NEC2023.ConductorMaterial.Copper;

                // 3 current-carrying conductors on a single-phase circuit (L+N counts 2,
                // but the adjustment threshold is >3, so both 1ph and 3ph sit at or below
                // it unless the caller says otherwise). Bundling beyond that is a routing
                // fact this engine is not given.
                int ccc = input.Phases == 3 ? 3 : 2;

                string awg = null;
                double ampacity = 0;
                foreach (string size in NecSizeLadder)
                {
                    double a;
                    try { a = StingTools.Standards.NEC2023.NECStandards.GetConductorAmpacity(size, material, 75); }
                    catch (ArgumentException) { continue; }   // size absent from the table for this material
                    a = StingTools.Standards.NEC2023.NECStandards.ApplyTemperatureCorrection(a, input.AmbientTempC);
                    a = StingTools.Standards.NEC2023.NECStandards.ApplyBundlingAdjustment(a, ccc);
                    if (a >= sizingCurrent) { awg = size; ampacity = a; break; }
                }

                if (awg == null)
                {
                    result.Sized = false;
                    result.Warning =
                        $"No single conductor in NEC Table 310.16 carries {sizingCurrent:0.0} A after " +
                        $"310.15(B)(1) ambient and 310.15(C)(1) adjustment. Parallel conductors " +
                        $"(310.10(G)) are required and are not sized here.";
                    return result;
                }

                double csaMm2 = NecCircularMilsToMm2(awg);
                result.RecommendedCsaMm2 = csaMm2;
                result.CsaLabel = $"{NecSizeLabel(awg)} {input.Material}/{input.Insulation}";
                result.Sized = true;

                // 240.6(A) standard rating, then the 240.4(D) small-conductor ceiling.
                int breaker = StingTools.Standards.NEC2023.NECStandards.GetStandardBreakerSize(sizingCurrent);
                int maxForSize = StingTools.Standards.NEC2023.NECStandards.GetMaximumBreakerSize(awg);
                if (maxForSize > 0 && breaker > maxForSize) breaker = maxForSize;
                result.ProposedBreakerA = breaker;

                // Informational only - see the summary above.
                double maxVD = input.VDLimitPct > 0 ? input.VDLimitPct : 3.0;
                double opTemp = OperatingTemperature(input.Insulation);
                result.ActualVoltDropPct = VoltageDropEngine.CalculateVoltDropPercent(
                    iB, input.LengthM, csaMm2, input.Material, input.VoltageV, input.Phases, opTemp);
                result.VDCompliant = result.ActualVoltDropPct <= maxVD;
                if (!result.VDCompliant)
                    result.Warning =
                        $"Voltage drop {result.ActualVoltDropPct:0.00}% exceeds the {maxVD:0.0}% target. " +
                        "NEC 210.19(A) Informational Note 4 RECOMMENDS 3% (5% overall) but does not " +
                        "require it, so the conductor was not upsized. Upsize deliberately if the " +
                        "project specification makes the limit binding.";

                result.DerivationNote =
                    $"Ib={iB:0.0}A" + (input.ContinuousLoad ? $", x1.25 continuous = {sizingCurrent:0.0}A [210.19(A)(1)]" : "") +
                    $", Table 310.16 @75°C corrected to {ampacity:0.0}A " +
                    $"(ta={input.AmbientTempC:0}°C [310.15(B)(1)], {ccc} CCC [310.15(C)(1)]), " +
                    $"OCPD {breaker}A [240.6(A)" + (maxForSize > 0 ? " capped by 240.4(D)" : "") + "] — " +
                    result.StandardBasis;
                return result;
            }
            catch (Exception ex)
            {
                // A throw here means the NEC path itself failed. Refuse - do NOT fall
                // through to the BS path, which is exactly the substitution this gap
                // exists to stop.
                StingLog.Error("CableSizerEngine.CalculateNec", ex);
                result.Sized = false;
                result.Warning = $"NEC 2023 sizing failed: {ex.Message}. No size was produced; " +
                                 "the BS 7671 path was NOT used as a substitute.";
                return result;
            }
        }

        /// <summary>NEC conductor series, smallest first. Trade sizes as
        /// <c>NECStandards</c> keys them: AWG below 250, then kcmil.</summary>
        private static readonly string[] NecSizeLadder =
        {
            "14", "12", "10", "8", "6", "4", "3", "2", "1",
            "1/0", "2/0", "3/0", "4/0",
            "250", "300", "350", "400", "500", "600", "700", "750",
        };

        private static readonly Dictionary<string, double> NecCircularMils = new Dictionary<string, double>
        {
            ["14"] = 4110, ["12"] = 6530, ["10"] = 10380, ["8"] = 16510,
            ["6"] = 26240, ["4"] = 41740, ["3"] = 52620, ["2"] = 66360,
            ["1"] = 83690, ["1/0"] = 105600, ["2/0"] = 133100, ["3/0"] = 167800,
            ["4/0"] = 211600, ["250"] = 250000, ["300"] = 300000, ["350"] = 350000,
            ["400"] = 400000, ["500"] = 500000, ["600"] = 600000, ["700"] = 700000,
            ["750"] = 750000,
        };

        /// <summary>The TRUE mm2 area of an AWG / kcmil size, so a downstream numeric
        /// parameter carries the real cross-section rather than a nearest-metric guess.
        /// 1 circular mil = pi/4 x (0.001 in)^2 = 5.067075e-4 mm2.</summary>
        internal static double NecCircularMilsToMm2(string size)
            => NecCircularMils.TryGetValue(size ?? "", out double cm)
                ? Math.Round(cm * 5.067074790e-4, 2)
                : 0.0;

        /// <summary>"12" -> "12AWG"; "250" -> "250kcmil". The table keys both as bare
        /// numbers, and printing "250AWG" would name a conductor that does not exist.</summary>
        internal static string NecSizeLabel(string size)
        {
            if (string.IsNullOrEmpty(size)) return "—";
            return size.Length >= 3 && !size.Contains("/") ? $"{size}kcmil" : $"{size}AWG";
        }

        /// <summary>
        /// Rough first-pass CSA from current alone, ignoring voltage drop.
        /// Empirical fallback used when correction factors push effective
        /// current above the smallest sizes' rating.
        /// </summary>
        private static double CrossSectionForCurrent(double currentA, string material)
        {
            // Conservative copper amp/mm² ratings for 70°C PVC, Method C.
            // Prefer voltage-drop-driven sizing — this is just to skip clearly
            // undersized iterations.
            double[] ampThresholds = { 13, 17, 23, 31, 40, 56, 75, 100, 125, 150, 192, 232, 269, 309, 353, 415 };
            for (int i = 0; i < ampThresholds.Length; i++)
                if (currentA <= ampThresholds[i])
                    return VoltageDropEngine.StandardSizesMm2[i + 1]; // skip the 1.0 mm² entry

            double last = VoltageDropEngine.StandardSizesMm2[VoltageDropEngine.StandardSizesMm2.Length - 1];
            return string.Equals(material, "Al", StringComparison.OrdinalIgnoreCase) ? last * 1.6 : last;
        }

        /// <summary>
        /// Conduit-fill calculator. Accepts wire entries and returns the
        /// resulting fill percentage and a recommendation if the fill exceeds
        /// the BS 7671 limit (typically 45%).
        /// </summary>
        public class ConduitFillResult
        {
            public double TotalWireAreaMm2 { get; set; }
            public double ConduitInternalAreaMm2 { get; set; }
            public double FillPct { get; set; }
            public bool Exceeds { get; set; }
            public string Recommendation { get; set; } = "";
        }

        public static ConduitFillResult CalculateConduitFill(
            string conduitKey, double maxFillPct,
            IEnumerable<(double csaMm2, int qty)> wires)
        {
            var tables = LoadWireTables();
            double conduitArea = 0;
            try
            {
                var v = tables["conduitInternalArea_mm2"]?[conduitKey];
                if (v != null) conduitArea = v.Value<double>();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            double total = 0;
            foreach (var (csa, qty) in wires)
            {
                double outer = WireOuterArea(csa, tables);
                total += outer * qty;
            }
            var result = new ConduitFillResult
            {
                TotalWireAreaMm2 = total,
                ConduitInternalAreaMm2 = conduitArea,
                FillPct = conduitArea > 0 ? total / conduitArea * 100.0 : 0,
            };
            result.Exceeds = result.FillPct > maxFillPct;
            if (result.Exceeds)
            {
                // Suggest the next conduit size.
                var areas = tables["conduitInternalArea_mm2"] as JObject;
                if (areas != null)
                {
                    var next = areas.Properties()
                        .Select(p => new { p.Name, Area = p.Value.Value<double>() })
                        .Where(x => x.Area > conduitArea && total / x.Area * 100.0 <= maxFillPct)
                        .OrderBy(x => x.Area)
                        .FirstOrDefault();
                    if (next != null)
                        result.Recommendation = $"Use {next.Name} ({total / next.Area * 100.0:0}% fill)";
                    else
                        result.Recommendation = "No standard conduit size satisfies the fill limit; review cable selection.";
                }
            }
            return result;
        }

        private static double WireOuterArea(double csaMm2, JObject tables)
        {
            string key = csaMm2 < 10 ? $"{csaMm2:0.0}" : ((int)csaMm2).ToString();
            try
            {
                var v = tables?["wireOuterArea_mm2"]?[key];
                if (v != null) return v.Value<double>();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            // Fallback: rough geometric approximation including insulation.
            return csaMm2 * 1.6 + 6.0;
        }
    }
}
