using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Core.Electrical;
using StingTools.UI;

namespace StingTools.Commands.Electrical.FaultCurrent
{
    /// <summary>
    /// Maximum fault-level propagation through the SLD hierarchy, IEC 60909-0
    /// equivalent-voltage-source method (see <see cref="Iec60909Lv"/>).
    /// Reads the utility incomer kA from the dock-panel, walks
    /// SLDCircuitTraverser, resolves each panel's voltage / phases / feeder
    /// cable from the model, and writes the calculated fault level to each
    /// downstream panel (parameter ELC_PNL_FAULT_KA → existing MR param
    /// ELC_PNL_SHORT_CIRCUIT_RATING_KA). Every missing input is reported as an
    /// assumption against the panel it affects.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FaultCurrentCommand : IExternalCommand
    {
        public static List<FaultPropagationResult> LastResults { get; private set; }
            = new List<FaultPropagationResult>();

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            double utilityKa = StingElectricalCommandHandler.CurrentUtilityFaultKa;
            bool utilityAssumed = utilityKa <= 0;
            // The dock panel field defaults to 25 kA; 0/blank falls back to the
            // same figure and says so below. It is a placeholder, not a UMEME value.
            if (utilityAssumed) utilityKa = 25.0;

            var root = StingTools.Core.SLD.SLDCircuitTraverser.BuildHierarchy(doc);
            if (root == null)
            {
                TaskDialog.Show("STING Fault Current",
                    "No SLD hierarchy found. Place an electrical incomer panel first.");
                return Result.Cancelled;
            }

            var wireTables = WireTableSet.Load(StingToolsApp.DataPath);
            var aicTiers   = LoadAicTiers();
            var results = FaultCurrentEngine.PropagateAll(root, utilityKa, wireTables,
                ResolveSupply, aicTiers).Values.ToList();
            LastResults = results;

            int written = 0;
            using (var tx = new Transaction(doc, "STING Stamp Fault Levels"))
            {
                tx.Start();
                foreach (var r in results)
                {
                    try
                    {
                        var elId = r.PanelId as ElementId;
                        if (elId == null) continue;
                        var panel = doc.GetElement(elId) as FamilyInstance;
                        if (panel == null) continue;
                        ParameterHelpers.SetString(panel, ParamRegistry.ELC_PNL_FAULT_KA,
                            $"{r.FaultKa:0.00}", overwrite: true);
                        written++;
                    }
                    catch (Exception ex) { StingLog.Warn($"Stamp fault to panel: {ex.Message}"); }
                }
                tx.Commit();
            }

            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            var top = results.OrderByDescending(r => r.FaultKa).FirstOrDefault();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Calculated maximum fault levels (IEC 60909-0, c = {Iec60909Lv.CMaxLv:0.00}) " +
                          $"for {results.Count} panel(s). Stamped {written} ELC_PNL_SHORT_CIRCUIT_RATING_KA values.");
            if (top != null) sb.AppendLine($"Highest: {top.FaultKa:0.0} kA at {top.PanelName}.");
            sb.AppendLine($"Upstream fault level at origin: {utilityKa:0.0} kA" +
                          (utilityAssumed ? " (ASSUMED — no value entered on the Electrical panel)" : " (Electrical panel input)"));
            sb.AppendLine($"Source R/X split: IEC 60909-0 §6.2 default (RQ = {Iec60909Lv.SourceROverX}·XQ) — ASSUMED for an LV source.");
            var withNotes = results.Where(r => r.Assumptions != null && r.Assumptions.Count > 0)
                                   .OrderBy(r => r.PanelName).ToList();
            if (withNotes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"ASSUMPTIONS ({withNotes.Count} panel(s)):");
                foreach (var r in withNotes.Take(15))
                    sb.AppendLine($"  • {r.PanelName}: {string.Join("; ", r.Assumptions)}");
                if (withNotes.Count > 15) sb.AppendLine($"  … and {withNotes.Count - 15} more (see StingTools log).");
                foreach (var r in withNotes)
                    StingLog.Info($"FaultCurrent {r.PanelName}: {r.FaultKa:0.00} kA — {string.Join("; ", r.Assumptions)}");
            }
            TaskDialog.Show("STING Fault Current", sb.ToString());
            return Result.Succeeded;
        }

        // ── Model reads (Revit-bound half) ──────────────────────────────────

        /// <summary>
        /// What the model declares about a panel's supply. Voltage comes from
        /// the panel's Distribution System (L-L, L-G, phase) first, then the
        /// Voltage parameter; feeder CSA and length from the STING parameters on
        /// the panel, then on its feeder circuit, then the circuit's modelled
        /// length. Anything absent is left 0 so the engine records it.
        /// </summary>
        internal static PanelSupplyInfo ResolveSupply(StingTools.Core.SLD.SLDNode node)
        {
            var info = new PanelSupplyInfo();
            var fi = node?.RevitElement;
            if (fi == null) return info;
            var doc = fi.Document;

            try
            {
                var dsId = fi.get_Parameter(BuiltInParameter.RBS_FAMILY_CONTENT_DISTRIBUTION_SYSTEM)?.AsElementId();
                if (dsId != null && dsId != ElementId.InvalidElementId
                    && doc.GetElement(dsId) is DistributionSysType ds)
                {
                    info.Phases = ds.ElectricalPhase == ElectricalPhase.ThreePhase ? 3 : 1;
                    if (ds.VoltageLineToLine != null)
                        info.VoltageLineToLineV = ElecUnits.VoltsFromInternal(ds.VoltageLineToLine.ActualValue);
                    if (ds.VoltageLineToGround != null)
                        info.VoltageLineToNeutralV = ElecUnits.VoltsFromInternal(ds.VoltageLineToGround.ActualValue);
                }
            }
            catch (Exception ex) { StingLog.Warn($"FaultCurrent distribution system '{node.Label}': {ex.Message}"); }

            if (info.VoltageLineToLineV <= 0 && info.VoltageLineToNeutralV <= 0)
            {
                double v = ElecUnits.Volts(fi);
                if (v > 0)
                {
                    // A bare Voltage value does not say whether it is L-L or L-N.
                    // 300 V separates the IEC 60038 LV pairs (230/400, 240/415,
                    // 220/380): at or above it the figure can only be line-to-line.
                    if (v >= 300) info.VoltageLineToLineV = v; else info.VoltageLineToNeutralV = v;
                    info.VoltageNote = $"INFERRED {(v >= 300 ? "L-L" : "L-N")} from panel Voltage {v:0} V (no distribution system)";
                }
            }
            if (info.Phases == 0)
            {
                double ph = ReadNumber(fi, "ELC_CKT_PHASE_COUNT_NR");
                if (ph == 1 || ph == 3) info.Phases = (int)ph;
            }

            ElectricalSystem feeder = null;
            try
            {
                feeder = fi.MEPModel?.GetElectricalSystems()?
                    .FirstOrDefault(s => s.BaseEquipment == null || s.BaseEquipment.Id != fi.Id);
            }
            catch (Exception ex) { StingLog.Warn($"FaultCurrent feeder circuit '{node.Label}': {ex.Message}"); }

            info.FeederCsaMm2 = ReadNumber(fi, "ELC_FEEDER_CSA_MM2");
            if (info.FeederCsaMm2 <= 0) info.FeederCsaMm2 = ReadNumber(fi, "ELC_CBL_SZ_MM");
            if (info.FeederCsaMm2 <= 0 && feeder != null)
            {
                info.FeederCsaMm2 = ReadNumber(feeder, "ELC_FEEDER_CSA_MM2");
                if (info.FeederCsaMm2 <= 0) info.FeederCsaMm2 = ReadNumber(feeder, "ELC_CBL_SZ_MM");
            }

            double len = ReadNumber(fi, "ELC_CBL_LENGTH_M");
            if (len > 0) { info.FeederLengthM = len; info.LengthSource = "ELC_CBL_LENGTH_M on panel"; }
            else if (feeder != null)
            {
                len = ReadNumber(feeder, "ELC_CBL_LENGTH_M");
                if (len > 0) { info.FeederLengthM = len; info.LengthSource = "ELC_CBL_LENGTH_M on feeder circuit"; }
                else
                {
                    try
                    {
                        double ft = feeder.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_LENGTH_PARAM)?.AsDouble() ?? 0;
                        if (ft > 0)
                        {
                            info.FeederLengthM = UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Meters);
                            info.LengthSource = "Revit feeder circuit length";
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"FaultCurrent circuit length '{node.Label}': {ex.Message}"); }
                }
            }
            return info;
        }

        /// <summary>
        /// Leading number of a parameter, whatever its storage. Text is parsed
        /// culture-invariant; for "4x16" / "4C x 16" the figure after the 'x'
        /// (the CSA) is taken, not the core count. Doubles go through ElecUnits
        /// so an electrical spec comes back in SI.
        /// </summary>
        private static double ReadNumber(Element el, string paramName)
        {
            try
            {
                var p = el?.LookupParameter(paramName);
                if (p == null || !p.HasValue) return 0;
                switch (p.StorageType)
                {
                    case StorageType.Double:  return ElecUnits.ToSi(p);
                    case StorageType.Integer: return p.AsInteger();
                    case StorageType.String:
                        string s = p.AsString() ?? "";
                        var cores = System.Text.RegularExpressions.Regex.Match(s,
                            @"\d+\s*[Cc]?\s*[x×X]\s*(\d+(\.\d+)?)");
                        string num = cores.Success ? cores.Groups[1].Value
                            : System.Text.RegularExpressions.Regex.Match(s, @"\d+(\.\d+)?").Value;
                        return num.Length > 0 && double.TryParse(num, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
                }
            }
            catch (Exception ex) { StingLog.Warn($"FaultCurrent read {paramName}: {ex.Message}"); }
            return 0;
        }

        public static double[] LoadAicTiers()
        {
            try
            {
                string path = StingToolsApp.FindDataFile("STING_AIC_TIERS.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new double[0];
                var root = JObject.Parse(File.ReadAllText(path));
                return ((root["tiers_kA"] as JArray) ?? new JArray())
                    .Select(t => t.Value<double>())
                    .OrderBy(x => x)
                    .ToArray();
            }
            catch (Exception ex) { StingLog.Warn($"LoadAicTiers: {ex.Message}"); return new double[0]; }
        }
    }

    /// <summary>
    /// Maps each panel's fault level to the next standard AIC tier and stamps
    /// it to ELC_PNL_AIC_RATING_KA. Requires FaultCurrentCommand to have run
    /// first so LastResults is populated.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AicRatingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var results = FaultCurrentCommand.LastResults;
            if (results == null || results.Count == 0)
            {
                TaskDialog.Show("STING AIC", "Run fault-current calculation first.");
                return Result.Failed;
            }
            var tiers = FaultCurrentCommand.LoadAicTiers();

            int stamped = 0;
            using (var tx = new Transaction(doc, "STING Stamp AIC Tiers"))
            {
                tx.Start();
                foreach (var r in results)
                {
                    try
                    {
                        var elId = r.PanelId as ElementId;
                        if (elId == null) continue;
                        var panel = doc.GetElement(elId) as FamilyInstance;
                        if (panel == null) continue;
                        double aic = FaultCurrentEngine.NextAicTierKa(r.FaultKa, tiers);
                        ParameterHelpers.SetString(panel, ParamRegistry.ELC_PNL_AIC_KA,
                            $"{aic:0.0}", overwrite: true);
                        stamped++;
                    }
                    catch (Exception ex) { StingLog.Warn($"Stamp AIC: {ex.Message}"); }
                }
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            TaskDialog.Show("STING AIC", $"AIC ratings stamped to {stamped} panel(s).");
            return Result.Succeeded;
        }
    }
}
