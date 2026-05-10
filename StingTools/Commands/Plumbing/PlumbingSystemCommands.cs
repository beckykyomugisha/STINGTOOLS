// PlumbingSystemCommands — Phase 179a SYSTEM tab.
//
// Plumb_SaveSystemConfig — read inline panel inputs → PlumbingSystemConfig.Save.
// Plumb_LoadSystemConfig — read existing config + refresh inline panel inputs.

using System;
using System.Linq;
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

            // Inputs come from the SYSTEM tab inline form (Phase 179d). When the
            // dock panel isn't open we fall back to the saved config so the
            // command stays usable from ribbon / NLP entry points.
            var cfg = StingPlumbingPanel.Instance?.ReadSystemConfigFromInputs()
                      ?? PlumbingSystemConfig.Load(ctx.Doc);

            string savedPath;
            using (var tx = new Transaction(ctx.Doc, "STING Plumbing — Save System Config"))
            {
                tx.Start();
                savedPath = PlumbingSystemConfig.Save(ctx.Doc, cfg);
                tx.Commit();
            }

            var panel = StingResultPanel.Create("Plumbing System Config Saved");
            panel.SetSubtitle($"Path: {savedPath ?? "(project not saved — config skipped)"}");
            panel.AddSection("ACTIVE")
                 .Metric("Building",          cfg.BuildingType)
                 .Metric("K factor",          cfg.KFactor.ToString("F2"))
                 .Metric("Drainage standard", cfg.DrainStandard)
                 .Metric("Supply standard",   cfg.SupplyStandard)
                 .Metric("DCW material",      cfg.MaterialFor("DCW"))
                 .Metric("DHW material",      cfg.MaterialFor("DHW"))
                 .Metric("Drain material",    cfg.MaterialFor("Drainage"))
                 .Metric("Vent material",     cfg.MaterialFor("Vent"))
                 .Metric("Supply pressure",   cfg.SupplyPressureBarAtEntry.ToString("F2") + " bar");
            panel.Show();

            StingPlumbingPanel.Instance?.SetStatus(
                $"STING Plumbing — {cfg.BuildingType} · {cfg.DrainStandard} · {cfg.SupplyStandard} · K={cfg.KFactor:F2}");
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

            // Refresh the SYSTEM tab inline form so what's on screen matches
            // the just-loaded config (Phase 179d). No-op when panel is closed.
            StingPlumbingPanel.Instance?.LoadSystemConfigIntoInputs(cfg);
            return Result.Succeeded;
        }
    }
}
