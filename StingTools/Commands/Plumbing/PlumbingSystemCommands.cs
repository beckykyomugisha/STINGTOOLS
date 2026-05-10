// PlumbingSystemCommands — Phase 179a SYSTEM tab.
//
// Plumb_SaveSystemConfig — modeless dialog → PlumbingSystemConfig.Save.
// Plumb_LoadSystemConfig — read existing config + push to ProjectInformation.

using System;
using System.Linq;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Plumbing;
using StingTools.UI;
using StingTools.UI.Plumbing;

namespace StingTools.Commands.Plumbing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbSaveSystemConfigCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var existing = PlumbingSystemConfig.Load(ctx.Doc);
            var dlg = new PlumbingSystemConfigDialog(ctx.Doc, existing) { Owner = null };
            try { dlg.ShowDialog(); } catch (Exception ex) { TaskDialog.Show("STING Plumbing", "Dialog error: " + ex.Message); return Result.Failed; }
            if (!dlg.Saved) return Result.Cancelled;

            var panel = StingResultPanel.Create("Plumbing System Config Saved");
            panel.SetSubtitle($"Path: {PlumbingSystemConfig.ProjectConfigPath(ctx.Doc) ?? "(project not saved)"}");
            panel.AddSection("ACTIVE")
                 .Metric("Building",         dlg.Result.BuildingType)
                 .Metric("K factor",         dlg.Result.KFactor.ToString("F2"))
                 .Metric("Drainage standard", dlg.Result.DrainStandard)
                 .Metric("Supply standard",   dlg.Result.SupplyStandard)
                 .Metric("DCW material",      dlg.Result.MaterialFor("DCW"))
                 .Metric("DHW material",      dlg.Result.MaterialFor("DHW"))
                 .Metric("Drain material",    dlg.Result.MaterialFor("Drainage"))
                 .Metric("Vent material",     dlg.Result.MaterialFor("Vent"))
                 .Metric("Supply pressure",   dlg.Result.SupplyPressureBarAtEntry.ToString("F2") + " bar");
            panel.Show();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlumbLoadSystemConfigCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var cfg = PlumbingSystemConfig.Load(ctx.Doc);

            var panel = StingResultPanel.Create("Plumbing System Config (current)");
            panel.SetSubtitle($"Path: {PlumbingSystemConfig.ProjectConfigPath(ctx.Doc) ?? "(no project file)"}");
            panel.AddSection("PROJECT")
                 .Metric("Building",            cfg.BuildingType)
                 .Metric("K factor",            cfg.KFactor.ToString("F2"))
                 .Metric("Drainage standard",   cfg.DrainStandard)
                 .Metric("Supply standard",     cfg.SupplyStandard)
                 .Metric("Flush valve majority", cfg.FlushValveMajority ? "yes" : "no")
                 .Metric("Occupancy",           cfg.OccupancyCount.ToString())
                 .Metric("Beds / workstations", cfg.BedsOrWorkstations.ToString())
                 .Metric("Saved (UTC)",         string.IsNullOrEmpty(cfg.LastSavedUtc) ? "(never)" : cfg.LastSavedUtc);

            panel.AddSection("MATERIALS");
            foreach (var kv in cfg.Materials.OrderBy(k => k.Key))
                panel.Metric(kv.Key, kv.Value);

            panel.AddSection("VELOCITY LIMITS (m/s)");
            foreach (var kv in cfg.VelocityMps.OrderBy(k => k.Key))
                panel.Metric(kv.Key, kv.Value.ToString("F2"));

            panel.AddSection("MIN SLOPE (%)");
            foreach (var kv in cfg.SlopePctMin.OrderBy(k => k.Key))
                panel.Metric(kv.Key, kv.Value.ToString("F2"));

            panel.Show();
            return Result.Succeeded;
        }
    }
}
