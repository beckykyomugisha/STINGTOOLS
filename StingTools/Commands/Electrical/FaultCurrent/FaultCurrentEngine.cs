using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Core.Electrical;

namespace StingTools.Commands.Electrical.FaultCurrent
{
    /// <summary>
    /// Fault-level propagation down the SLD hierarchy. No Revit API calls -
    /// the command resolves each panel's voltage, phase count and feeder
    /// cable and hands them in through <see cref="PanelSupplyInfo"/>.
    ///
    /// The arithmetic is IEC 60909-0:2016's equivalent-voltage-source method
    /// in <see cref="Iec60909Lv"/>: voltage factor cmax, a source impedance
    /// recovered from the upstream fault level with an R/X split, and the
    /// feeder's R (20 °C, for maximum fault current) and X added as complex
    /// impedances. What it is NOT: see the header of Iec60909Lv.cs.
    ///
    /// Every input the model did not supply is recorded per panel in
    /// <see cref="FaultPropagationResult.Assumptions"/> - nothing is defaulted
    /// silently.
    /// </summary>
    public static class FaultCurrentEngine
    {
        public const double Sqrt3 = Iec60909Lv.Sqrt3;

        /// <summary>
        /// ASSUMPTION used only when no panel in the chain declares a voltage:
        /// 400 V line-to-line, 3-phase (IEC 60038 / BS EN 60038 nominal LV).
        /// Surfaced per panel in the result, never applied silently.
        /// </summary>
        public const double AssumedLineToLineV = 400.0;

        /// <summary>
        /// Resistance of one cable run in milliohms at the conductor operating
        /// temperature. Used by the BS 7671 Zs check (R1 + R2 at 70/90 °C) -
        /// NOT by the maximum-fault calculation, which uses 20 °C resistance
        /// per IEC 60909-0.
        /// </summary>
        public static double CableImpedanceMohm(WireTableSet wireTables,
            double csaMm2, string material, double lengthM,
            double operatingTempC = 70.0,
            string insulation = null)
            // Pure arithmetic lives in WireTableSet.cs (CableResistance) so it is testable.
            => CableResistance.RunMohm(wireTables, csaMm2, material, lengthM, operatingTempC, insulation);

        /// <summary>
        /// Fault level at the downstream end of a feeder, kA, IEC 60909-0 cmax.
        /// <paramref name="systemVoltageV"/> is the LINE-TO-LINE voltage for
        /// <paramref name="phases"/> = 3 (e.g. 400) and the LINE-TO-NEUTRAL
        /// voltage for a single-phase system (e.g. 230). The feeder impedance
        /// is one conductor, taken as purely resistive here - callers that know
        /// the cable should use <see cref="Iec60909Lv"/> directly with R and X.
        /// </summary>
        public static double DownstreamFaultKa(double upstreamFaultKa, double feederZMohm,
            double systemVoltageV, int phases = 3)
        {
            var feeder = new ImpedanceMohm(Math.Max(0, feederZMohm), 0);
            return phases == 3
                ? Iec60909Lv.Downstream3PhKa(upstreamFaultKa, systemVoltageV, feeder)
                : Iec60909Lv.Downstream1PhKa(upstreamFaultKa, systemVoltageV, feeder);
        }

        /// <summary>
        /// Walk the SLD hierarchy depth-first, propagating the maximum fault
        /// level downward. <paramref name="utilityFaultKa"/> is the fault level
        /// at the root board: 3-phase I"k for a 3-phase root, the line-to-neutral
        /// prospective fault current for a single-phase root.
        /// <paramref name="supplyOf"/> returns what the model knows about a node;
        /// it may return null.
        /// </summary>
        public static Dictionary<long, FaultPropagationResult> PropagateAll(
            StingTools.Core.SLD.SLDNode root, double utilityFaultKa, WireTableSet wireTables,
            Func<StingTools.Core.SLD.SLDNode, PanelSupplyInfo> supplyOf, double[] aicTiers = null)
        {
            var results = new Dictionary<long, FaultPropagationResult>();
            if (root == null) return results;
            PropagateNode(root, utilityFaultKa, 0, 0, false, wireTables, supplyOf,
                aicTiers ?? new double[0], results);
            return results;
        }

        private static void PropagateNode(StingTools.Core.SLD.SLDNode node,
            double parentFaultKa, double parentVoltageLL, int parentPhases, bool parentVoltageAssumed,
            WireTableSet wireTables, Func<StingTools.Core.SLD.SLDNode, PanelSupplyInfo> supplyOf,
            double[] aicTiers, Dictionary<long, FaultPropagationResult> results)
        {
            if (node == null) return;
            var info = supplyOf?.Invoke(node) ?? new PanelSupplyInfo();
            var notes = new List<string>();
            bool voltageAssumed = false;

            // ── Voltage + phases: model, else inherit from the parent, else assume ──
            int phases = info.Phases == 1 || info.Phases == 3 ? info.Phases : 0;
            double vLL = info.VoltageLineToLineV;
            double vLN = info.VoltageLineToNeutralV;
            if (phases == 0 && parentPhases != 0)
            {
                phases = parentPhases;
                notes.Add($"phase count not declared — inherited {phases}-phase from upstream");
            }
            if (vLL <= 0 && vLN > 0) vLL = vLN * Sqrt3;
            if (vLN <= 0 && vLL > 0) vLN = vLL / Sqrt3;
            if (vLL <= 0 && parentVoltageLL > 0)
            {
                vLL = parentVoltageLL; vLN = vLL / Sqrt3;
                voltageAssumed = parentVoltageAssumed;
                notes.Add($"voltage not declared — inherited {vLL:0} V L-L from upstream" +
                          (parentVoltageAssumed ? " (which was itself ASSUMED)" : ""));
            }
            if (vLL <= 0)
            {
                vLL = AssumedLineToLineV; vLN = vLL / Sqrt3;
                voltageAssumed = true;
                notes.Add($"ASSUMED {AssumedLineToLineV:0} V L-L (no voltage on this panel or any upstream)");
            }
            if (phases == 0)
            {
                phases = 3;
                notes.Add("ASSUMED 3-phase (phase count not declared)");
            }
            if (!string.IsNullOrEmpty(info.VoltageNote)) notes.Add(info.VoltageNote);

            // ── Feeder cable (non-root panels only) ──
            double rPerM = 0, lengthM = 0, csa = info.FeederCsaMm2;
            var conductor = new ImpedanceMohm(0, 0);
            if (node.HierarchyLevel > 0)
            {
                string material = string.IsNullOrEmpty(info.Material) ? "Cu" : info.Material;
                lengthM = info.FeederLengthM;
                rPerM = csa > 0 && wireTables != null ? wireTables.GetMohmPerMetre(csa, material) : 0;
                if (csa <= 0)
                    notes.Add("feeder CSA unknown — cable impedance ignored, fault level taken as upstream " +
                              "(conservative for breaking capacity only)");
                else if (rPerM <= 0)
                    notes.Add($"no resistance data for {csa:0.#} mm² — cable impedance ignored");
                if (lengthM <= 0)
                    notes.Add("feeder length unknown — cable impedance ignored, fault level taken as upstream " +
                              "(conservative for breaking capacity only)");
                if (rPerM > 0 && lengthM > 0)
                {
                    conductor = Iec60909Lv.CableConductor(rPerM, lengthM);
                    notes.Add($"cable X ASSUMED {Iec60909Lv.AssumedCableReactanceMohmPerM:0.00} mΩ/m");
                    if (!string.IsNullOrEmpty(info.LengthSource))
                        notes.Add($"length {lengthM:0.#} m from {info.LengthSource}");
                }
            }

            double thisFaultKa;
            if (node.HierarchyLevel == 0)
                thisFaultKa = parentFaultKa;
            else if (phases == 3)
                thisFaultKa = Iec60909Lv.Downstream3PhKa(parentFaultKa, vLL, conductor);
            else
            {
                if (parentPhases == 3)
                    notes.Add("upstream L-N fault level ASSUMED equal to its 3-phase level " +
                              "(true at Dyn transformer terminals, high further downstream — conservative for breaking capacity)");
                thisFaultKa = Iec60909Lv.Downstream1PhKa(parentFaultKa, vLN, conductor);
            }

            if (node.IsPanel && node.ElementId != null)
            {
                results[node.ElementId.Value] = new FaultPropagationResult
                {
                    PanelId       = node.ElementId,
                    PanelName     = node.Label,
                    FaultKa       = thisFaultKa,
                    ZtotalMohm    = conductor.Magnitude,
                    AicRequiredKa = NextAicTierKa(thisFaultKa, aicTiers),
                    Voltage       = phases == 3 ? $"{vLL:0}V 3ph" : $"{vLN:0}V 1ph",
                    FeederCsaMm2  = csa,
                    FeederLengthM = lengthM,
                    Phases        = phases,
                    VoltageFactorC = Iec60909Lv.CMaxLv,
                    Assumptions   = notes
                };
            }

            foreach (var child in node.Children ?? Enumerable.Empty<StingTools.Core.SLD.SLDNode>())
                PropagateNode(child, thisFaultKa, vLL, phases, voltageAssumed, wireTables, supplyOf, aicTiers, results);
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
    }

    /// <summary>
    /// What the model says about one panel's supply. Filled by the command
    /// from Revit; 0 / null means "not known" and the engine records the
    /// resulting assumption rather than inventing a value.
    /// </summary>
    public class PanelSupplyInfo
    {
        public double VoltageLineToLineV   { get; set; }
        public double VoltageLineToNeutralV { get; set; }
        /// <summary>1 or 3; 0 = unknown.</summary>
        public int    Phases               { get; set; }
        /// <summary>Where the voltage came from, or an inference note. Optional.</summary>
        public string VoltageNote          { get; set; }
        public double FeederCsaMm2         { get; set; }
        public double FeederLengthM        { get; set; }
        public string LengthSource         { get; set; }
        public string Material             { get; set; } = "Cu";
    }

    public class FaultPropagationResult
    {
        public object PanelId       { get; set; }   // ElementId boxed
        public string PanelName     { get; set; }
        /// <summary>Maximum initial symmetrical fault current I"k at the panel, kA (IEC 60909-0, cmax).</summary>
        public double FaultKa       { get; set; }
        /// <summary>|Z| of ONE conductor of the feeder into this panel, mΩ (20 °C R + assumed X).</summary>
        public double ZtotalMohm    { get; set; }
        public double AicRequiredKa { get; set; }
        /// <summary>"400V 3ph" (line-to-line) or "230V 1ph" (line-to-neutral).</summary>
        public string Voltage       { get; set; }
        public double FeederCsaMm2  { get; set; }
        public double FeederLengthM { get; set; }
        public int    Phases        { get; set; }
        public double VoltageFactorC { get; set; }
        /// <summary>Every input this panel's result rests on that the model did not supply.</summary>
        public List<string> Assumptions { get; set; } = new List<string>();
    }

    /// <summary>
    /// Loader half of <see cref="WireTableSet"/> (file lookup). The table itself —
    /// parsing and interpolation — is Revit-free in WireTableSet.cs so it can be
    /// tested against the shipped data file.
    /// </summary>
    public partial class WireTableSet
    {
        public static WireTableSet Load(string dataPath)
        {
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
                    return new WireTableSet();
                return FromJson(JObject.Parse(File.ReadAllText(path)));
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"WireTableSet.Load: {ex.Message}");
                return new WireTableSet();
            }
        }
    }
}
