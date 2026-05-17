using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical.FaultCurrent
{
    /// <summary>
    /// Resistive fault-current propagation through the SLD hierarchy.
    /// Reads the utility incomer kA from the dock-panel, walks
    /// SLDCircuitTraverser, and writes the calculated fault level to each
    /// downstream panel (parameter ELC_PNL_FAULT_KA → existing MR param
    /// ELC_PNL_SHORT_CIRCUIT_RATING_KA).
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
            if (utilityKa <= 0) utilityKa = 25.0;

            var root = StingTools.Core.SLD.SLDCircuitTraverser.BuildHierarchy(doc);
            if (root == null)
            {
                TaskDialog.Show("STING Fault Current",
                    "No SLD hierarchy found. Place an electrical incomer panel first.");
                return Result.Cancelled;
            }

            var wireTables = WireTableSet.Load(StingToolsApp.DataPath);
            var aicTiers   = LoadAicTiers();
            double sysV    = 240.0;
            int phases     = 3;

            var results = FaultCurrentEngine.PropagateAll(root, utilityKa, wireTables,
                sysV, phases, aicTiers).Values.ToList();
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
            TaskDialog.Show("STING Fault Current",
                $"Calculated fault levels for {results.Count} panel(s). " +
                $"Stamped {written} ELC_PNL_SHORT_CIRCUIT_RATING_KA values. " +
                (top == null ? "" : $"Highest: {top.FaultKa:0.0} kA at {top.PanelName}."));
            return Result.Succeeded;
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
