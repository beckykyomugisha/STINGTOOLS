// PlumbingPumpCommands — Phase 179d pump selection + booster set commands.
//
// Plumb_PumpSelect    — duty point calculation + catalogue match + write-back.
// Plumb_BoosterSet    — break tank volume + booster set sizing per BS EN 806-3.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Plumbing;
using StingTools.UI;

namespace StingTools.Commands.Plumbing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbPumpSelectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            var pumps = new FilteredElementCollector(ctx.Doc)
                .WhereElementIsNotElementType()
                .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                .Cast<Element>()
                .Where(el =>
                {
                    var name    = (el.Name ?? "").ToUpperInvariant();
                    var famName = ((el as FamilyInstance)?.Symbol?.Family?.Name ?? "").ToUpperInvariant();
                    return name.Contains("PUMP") || famName.Contains("PUMP") ||
                           name.Contains("BOOSTER") || famName.Contains("BOOSTER");
                })
                .ToList();

            if (pumps.Count == 0)
            {
                TaskDialog.Show("STING Plumbing — Pump Select",
                    "No pump families found.\n\nLoad a pump family (OST_MechanicalEquipment) whose name contains " +
                    "'PUMP' or 'BOOSTER', then re-run Pump Select.");
                return Result.Cancelled;
            }

            PipeNetwork network = null;
            try { network = PipeNetworkBuilder.Build(ctx.Doc, null); } catch { }

            var td = new TaskDialog("Plumb_PumpSelect")
            {
                MainInstruction = $"Pump sizing mode ({pumps.Count} pump(s) found)",
                MainContent     = "Choose how to derive the duty point.",
                CommonButtons   = TaskDialogCommonButtons.Cancel,
                DefaultButton   = TaskDialogResult.Cancel
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Auto (derive from pipe network pressure + flow)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Simple (use defaults: 20 m static + 5 m friction + 1 l/s)");
            var pick = td.Show();
            if (pick != TaskDialogResult.CommandLink1 && pick != TaskDialogResult.CommandLink2)
                return Result.Cancelled;

            bool autoMode = pick == TaskDialogResult.CommandLink1;
            string cataloguePath = StingToolsApp.FindDataFile("STING_PUMP_CATALOGUE.json");
            var report = new List<string>();
            int written = 0, failed = 0;

            using (var tx = new Transaction(ctx.Doc, "STING Plumbing Pump Select"))
            {
                tx.Start();
                foreach (var pumpEl in pumps)
                {
                    try
                    {
                        string sysName = (pumpEl as FamilyInstance)?.MEPModel?.ConnectorManager
                            ?.Connectors?.Cast<Connector>()
                            .Select(c => c.MEPSystem?.Name).FirstOrDefault(n => n != null) ?? "";
                        PumpDutyPoint duty = autoMode && network != null
                            ? PumpSelector.CalculateDutyPoint(ctx.Doc, network, sysName)
                            : PumpSelector.CalculateDutyPointSimple(20.0, 5.0, 1.0);

                        var result = PumpSelector.SelectPump(duty, cataloguePath);
                        if (result.BestMatch != null)
                        {
                            PumpSelector.WritePumpData(ctx.Doc, pumpEl.Id, result.BestMatch, duty);
                            report.Add($"OK  {pumpEl.Name,-30} => {result.BestMatch.Manufacturer} {result.BestMatch.Model} " +
                                       $"({result.BestMatch.RatedFlowLps:F2} l/s @ {result.BestMatch.RatedHeadM:F1} m, " +
                                       $"{result.BestMatch.EfficiencyPct:F0}% eff)");
                            written++;
                        }
                        else
                        {
                            report.Add($"--- {pumpEl.Name,-30} => no match for {duty.FlowLps:F2} l/s @ {duty.HeadM:F1} m");
                            failed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        report.Add($"ERR {pumpEl.Name}: {ex.Message}");
                        failed++;
                    }
                }
                tx.Commit();
            }

            var panel = StingResultPanel.Create("Pump Selection");
            panel.SetSubtitle($"{pumps.Count} pump(s) scanned · BS EN 806-3");
            panel.AddSection("SUMMARY")
                 .Metric("Pumps found",          pumps.Count.ToString())
                 .Metric("Sized + written",       written.ToString())
                 .Metric("No catalogue match",    failed.ToString());
            if (report.Any())
            {
                panel.AddSection("RESULTS (first 40)");
                foreach (var line in report.Take(40)) panel.Text(line);
            }
            if (string.IsNullOrEmpty(cataloguePath))
                panel.AddSection("NOTE").Text("STING_PUMP_CATALOGUE.json not found — built-in fallback catalogue used.");
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbBoosterSetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            // Accumulate total cold-water loading units for peak demand
            double totalLu = 0;
            foreach (var fi in new FilteredElementCollector(ctx.Doc)
                .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                .WhereElementIsNotElementType().Cast<Element>())
            {
                try { totalLu += fi.LookupParameter(ParamRegistry.PLM_SUP_LU_CW)?.AsDouble() ?? 0; } catch { }
            }

            double qdLps = PlumbingTables.LuToQdLps(totalLu);

            // Break tank: 1 hour peak storage (BS 6700 / WRAS guidance)
            double breakTankL = qdLps * 3600.0;

            // Booster duty head from highest level
            var levels = new FilteredElementCollector(ctx.Doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();
            double topLevelM  = (levels.LastOrDefault()?.Elevation ?? 0) * 0.3048;
            double staticHead = topLevelM + 5.0;          // +5 m residual at top fixture
            double frictionHead = staticHead * 0.15;       // 15% friction estimate

            string cataloguePath = StingToolsApp.FindDataFile("STING_PUMP_CATALOGUE.json");
            var duty     = PumpSelector.CalculateDutyPointSimple(staticHead, frictionHead, qdLps);
            var selection = PumpSelector.SelectPump(duty, cataloguePath);

            var panel = StingResultPanel.Create("Booster Set Sizing (BS EN 806-3)");
            panel.SetSubtitle($"Sigma LU = {totalLu:F1}  Qd = {qdLps:F2} l/s  {levels.Count} levels");
            panel.AddSection("BREAK TANK")
                 .Metric("Peak demand",        $"{qdLps:F2} l/s")
                 .Metric("Storage period",     "1.0 h (BS 6700)")
                 .Metric("Recommended volume", $"{breakTankL / 1000.0:F1} m³  ({breakTankL:F0} L)");
            panel.AddSection("BOOSTER PUMP DUTY")
                 .Metric("Static head",          $"{staticHead:F1} m")
                 .Metric("Friction allowance",   $"{frictionHead:F1} m (15%)")
                 .Metric("Design head (+20%)",   $"{duty.HeadM:F1} m")
                 .Metric("Design flow",          $"{duty.FlowLps:F2} l/s")
                 .Metric("Configuration",        "1D+1S (duty/standby)");
            if (selection.BestMatch != null)
            {
                var m = selection.BestMatch;
                panel.AddSection("RECOMMENDED PUMP")
                     .Metric("Manufacturer", m.Manufacturer)
                     .Metric("Model",        m.Model)
                     .Metric("Rated flow",   $"{m.RatedFlowLps:F2} l/s")
                     .Metric("Rated head",   $"{m.RatedHeadM:F1} m")
                     .Metric("Power",        $"{m.PowerKw:F2} kW")
                     .Metric("Efficiency",   $"{m.EfficiencyPct:F0}%");
            }
            panel.AddSection("STANDARDS")
                 .Text("BS EN 806-3:2006 ss5.5 — Booster systems")
                 .Text("BS 6700 ss4.7 — 1-hour peak storage minimum")
                 .Text("WRAS guidance — lid, overflow, screened vent required on tank");
            panel.Show();
            return Result.Succeeded;
        }
    }
}
