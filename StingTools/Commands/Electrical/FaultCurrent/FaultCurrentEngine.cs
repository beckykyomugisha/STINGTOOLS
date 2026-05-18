using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace StingTools.Commands.Electrical.FaultCurrent
{
    /// <summary>
    /// Pure resistive-impedance fault-current engine. No Revit API. Implements
    /// the IEC 60909 simplified resistive method:
    ///   Z_source = V_LN / (sqrt(3) * I_fault_kA * 1000)   [3-phase, ohms]
    ///   Z_total  = Z_source + Z_feeder
    ///   I_fault_downstream = V_LN / (sqrt(3) * Z_total)    [kA]
    /// Cable resistance is read from the wire-tables JSON the Phase 177
    /// CableSizerEngine already consumes, with linear T-coefficient correction
    /// per BS 7671 Appx 4 and IEC 60228.
    /// </summary>
    public static class FaultCurrentEngine
    {
        public const double Sqrt3 = 1.7320508075688772;

        /// <summary>
        /// Total impedance of one cable run in milliohms. Single-phase callers
        /// must double the result to account for the return loop; three-phase
        /// callers use it as-is (line-to-line uses the same per-conductor R).
        /// </summary>
        public static double CableImpedanceMohm(WireTableSet wireTables,
            double csaMm2, string material, double lengthM,
            double operatingTempC = 70.0)
        {
            if (wireTables == null || csaMm2 <= 0 || lengthM <= 0) return 0;
            double r = wireTables.GetMohmPerMetre(csaMm2, material);
            if (r <= 0) return 0;
            // Linear copper temperature coefficient α = 0.00393/K; aluminium 0.00403/K.
            double alpha = string.Equals(material, "Al", StringComparison.OrdinalIgnoreCase) ? 0.00403 : 0.00393;
            double rT = r * (1.0 + alpha * (operatingTempC - 20.0));
            return rT * lengthM;
        }

        /// <summary>
        /// Available short-circuit current at the downstream end of a feeder
        /// given the upstream bus fault level + feeder loop impedance.
        /// systemVoltageV = phase-to-neutral voltage (e.g. 240 for 415V 3Ph TN-S
        /// / 277 for 480V 3Ph US / 240 for 240V 1Ph).
        /// </summary>
        public static double DownstreamFaultKa(double upstreamFaultKa, double feederZMohm,
            double systemVoltageV, int phases = 3)
        {
            if (upstreamFaultKa <= 0 || systemVoltageV <= 0) return 0;
            double zSource = phases == 3
                ? systemVoltageV / (Sqrt3 * upstreamFaultKa * 1000.0)
                : systemVoltageV / (upstreamFaultKa * 1000.0);
            double zTotalOhm = zSource + feederZMohm / 1000.0;
            if (zTotalOhm <= 0) return upstreamFaultKa;
            double iFaultA = phases == 3
                ? systemVoltageV / (Sqrt3 * zTotalOhm)
                : systemVoltageV / zTotalOhm;
            return iFaultA / 1000.0;
        }

        /// <summary>
        /// Walk the SLD hierarchy depth-first, propagating the fault level
        /// downward. The returned dictionary maps each panel ElementId to the
        /// computed fault level + AIC requirement.
        /// Caller supplies the SLD root from
        /// <c>StingTools.Core.SLD.SLDCircuitTraverser.BuildHierarchy(doc)</c>.
        /// </summary>
        public static Dictionary<long, FaultPropagationResult> PropagateAll(
            StingTools.Core.SLD.SLDNode root, double utilityFaultKa, WireTableSet wireTables,
            double systemVoltageV = 240.0, int phases = 3, double[] aicTiers = null)
        {
            var results = new Dictionary<long, FaultPropagationResult>();
            if (root == null) return results;
            PropagateNode(root, utilityFaultKa, wireTables, systemVoltageV, phases, aicTiers ?? new double[0], results);
            return results;
        }

        private static void PropagateNode(StingTools.Core.SLD.SLDNode node,
            double parentFaultKa, WireTableSet wireTables,
            double systemVoltageV, int phases, double[] aicTiers,
            Dictionary<long, FaultPropagationResult> results)
        {
            if (node == null) return;

            // For non-root panel nodes, compute downstream fault from feeder cable.
            double feederZMohm = 0;
            double feederCsa   = 0;
            if (node.HierarchyLevel > 0 && node.RevitElement != null)
            {
                feederCsa = ReadDoubleParameter(node.RevitElement, "ELC_FEEDER_CSA_MM2");
                if (feederCsa <= 0) feederCsa = ReadDoubleParameter(node.RevitElement, "ELC_CBL_SZ_MM");
                double feederLenM = 5.0;  // conservative default when no length is recorded
                feederZMohm = CableImpedanceMohm(wireTables, feederCsa, "Cu", feederLenM);
            }

            double thisFaultKa = node.HierarchyLevel == 0
                ? parentFaultKa
                : DownstreamFaultKa(parentFaultKa, feederZMohm, systemVoltageV, phases);

            if (node.IsPanel && node.ElementId != null)
            {
                results[node.ElementId.Value] = new FaultPropagationResult
                {
                    PanelId       = node.ElementId,
                    PanelName     = node.Label,
                    FaultKa       = thisFaultKa,
                    ZtotalMohm    = feederZMohm,
                    AicRequiredKa = NextAicTierKa(thisFaultKa, aicTiers),
                    Voltage       = $"{systemVoltageV:0}V",
                    FeederCsaMm2  = feederCsa
                };
            }

            foreach (var child in node.Children ?? Enumerable.Empty<StingTools.Core.SLD.SLDNode>())
                PropagateNode(child, thisFaultKa, wireTables, systemVoltageV, phases, aicTiers, results);
        }

        /// <summary>Look up the next AIC tier ≥ <paramref name="faultKa"/> × (1 + safetyMargin).</summary>
        public static double NextAicTierKa(double faultKa, double[] tiers, double safetyMarginPct = 10.0)
        {
            if (tiers == null || tiers.Length == 0) return faultKa;
            double target = faultKa * (1.0 + safetyMarginPct / 100.0);
            foreach (double t in tiers.OrderBy(x => x))
                if (t >= target) return t;
            return tiers.Last();
        }

        /// <summary>
        /// Soft reflection-style read of a string-valued parameter; callers
        /// that don't reference Revit API can pass null and get 0 back.
        /// We use Object so the engine assembly stays Revit-API-free.
        /// </summary>
        private static double ReadDoubleParameter(object revitElement, string paramName)
        {
            if (revitElement == null) return 0;
            try
            {
                var t = revitElement.GetType();
                var lookup = t.GetMethod("LookupParameter", new Type[] { typeof(string) });
                var p = lookup?.Invoke(revitElement, new object[] { paramName });
                if (p == null) return 0;
                var asString = p.GetType().GetMethod("AsString", Type.EmptyTypes);
                var s = asString?.Invoke(p, null) as string;
                return double.TryParse(s, out double v) ? v : 0;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; }
        }
    }

    public class FaultPropagationResult
    {
        public object PanelId       { get; set; }   // ElementId boxed
        public string PanelName     { get; set; }
        public double FaultKa       { get; set; }
        public double ZtotalMohm    { get; set; }
        public double AicRequiredKa { get; set; }
        public string Voltage       { get; set; }
        public double FeederCsaMm2  { get; set; }
    }

    /// <summary>
    /// Loader for STING_WIRE_TABLES.json. Held by FaultCurrentEngine /
    /// FeederSizerEngine. Aluminium = copper × 1.61 if no Al table is shipped.
    /// </summary>
    public class WireTableSet
    {
        private readonly List<(double csaMm2, double mohmPerM)> _copper = new();
        private const double AluminiumFactor = 1.61;

        public static WireTableSet Load(string dataPath)
        {
            var ws = new WireTableSet();
            try
            {
                string path = string.IsNullOrEmpty(dataPath)
                    ? null
                    : Path.Combine(dataPath, "STING_WIRE_TABLES.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    path = StingTools.Core.StingToolsApp.FindDataFile("STING_WIRE_TABLES.json");
                }
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return ws;
                var root = JObject.Parse(File.ReadAllText(path));
                var table = (root["copperTables"] as JArray)?.OfType<JObject>().FirstOrDefault();
                if (table == null) return ws;
                foreach (var sz in table["sizes"] as JArray ?? new JArray())
                {
                    double csa = sz["csaMm2"]?.Value<double>() ?? 0;
                    double r   = sz["mohm_per_m"]?.Value<double>() ?? 0;
                    if (csa > 0 && r > 0) ws._copper.Add((csa, r));
                }
                ws._copper.Sort((a, b) => a.csaMm2.CompareTo(b.csaMm2));
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"WireTableSet.Load: {ex.Message}");
            }
            return ws;
        }

        /// <summary>
        /// Return mΩ/m for the nominal CSA (closest tabulated size, or
        /// linearly interpolated when between two entries).
        /// </summary>
        public double GetMohmPerMetre(double csaMm2, string material)
        {
            if (csaMm2 <= 0 || _copper.Count == 0) return 0;
            double r;
            if (csaMm2 <= _copper[0].csaMm2) r = _copper[0].mohmPerM;
            else if (csaMm2 >= _copper[^1].csaMm2) r = _copper[^1].mohmPerM;
            else
            {
                int idx = _copper.FindLastIndex(p => p.csaMm2 <= csaMm2);
                var lo = _copper[idx];
                var hi = _copper[idx + 1];
                double t = (csaMm2 - lo.csaMm2) / (hi.csaMm2 - lo.csaMm2);
                r = lo.mohmPerM + t * (hi.mohmPerM - lo.mohmPerM);
            }
            return string.Equals(material, "Al", StringComparison.OrdinalIgnoreCase) ? r * AluminiumFactor : r;
        }
    }
}
