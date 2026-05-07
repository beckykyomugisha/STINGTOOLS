using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Commands.Electrical.VoltageDrop;

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
            catch { }
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
            catch { }
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

        public static CableSizeResult Calculate(CableSizeInput input)
        {
            var result = new CableSizeResult();
            if (input == null) { result.Warning = "Null input"; return result; }

            double iB = DesignCurrent(input.LoadKW, input.VoltageV, input.PowerFactor, input.Phases);
            result.DesignCurrentA = iB;
            if (iB <= 0)
            {
                result.Warning = "Invalid load / voltage / PF — cannot compute design current.";
                return result;
            }

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
                result.Warning = "No tabulated size satisfies the voltage-drop limit at this length / current.";
                return result;
            }

            result.RecommendedCsaMm2 = winner.Value;
            result.CsaLabel = $"{VoltageDropEngine.FormatCsa(winner.Value, input.Standard)} {input.Material}/{input.Insulation}";
            result.ActualVoltDropPct = winnerVd;
            result.VDCompliant = winnerVd <= maxVD;

            result.ProposedBreakerA = string.Equals(input.Standard, "NEC", StringComparison.OrdinalIgnoreCase)
                ? VoltageDropEngine.NextStandardBreakerSizeNEC(iB, input.ContinuousLoad)
                : VoltageDropEngine.NextStandardBreakerSizeBS(iB, input.ContinuousLoad);

            result.DerivationNote =
                $"Ib={iB:0.0}A, CF={cf:0.00} (method={input.InstallMethod} ins={input.Insulation} ta={input.AmbientTempC}°C), " +
                $"opT={opTemp:0}°C, target VD ≤ {maxVD:0.0}%";
            return result;
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
            catch { }
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
            catch { }
            // Fallback: rough geometric approximation including insulation.
            return csaMm2 * 1.6 + 6.0;
        }
    }
}
