// PlumbingSystemCommands — Phase 179a / 187 SYSTEM tab.
//
// Plumb_SaveSystemConfig — reads the inline form on the SYSTEM tab of
//   StingPlumbingPanel and persists it via PlumbingSystemConfig.Save. Falls
//   back to the modeless dialog when the panel isn't constructed (e.g. the
//   command was invoked from a workflow before the panel was opened).
// Plumb_LoadSystemConfig — reads the on-disk config, pushes the values back
//   into the inline form (when the panel is open), and shows a summary panel.

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

            // Prefer the inline form on the SYSTEM tab — no modal dialog. Fall
            // back to the legacy modeless dialog only when the panel isn't
            // present (e.g. headless workflow invocation).
            PlumbingSystemConfig cfg;
            var panel = StingPlumbingPanel.Instance;
            if (panel != null)
            {
                var existing = PlumbingSystemConfig.Load(ctx.Doc);
                try
                {
                    cfg = panel.Dispatcher.Invoke(() => panel.ReadSystemForm(existing));
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("STING Plumbing",
                        "Could not read inline SYSTEM form: " + ex.Message);
                    return Result.Failed;
                }
            }
            else
            {
                var existing = PlumbingSystemConfig.Load(ctx.Doc);
                var dlg = new PlumbingSystemConfigDialog(ctx.Doc, existing) { Owner = null };
                try { dlg.ShowDialog(); }
                catch (Exception ex)
                {
                    TaskDialog.Show("STING Plumbing", "Dialog error: " + ex.Message);
                    return Result.Failed;
                }
                if (!dlg.Saved) return Result.Cancelled;
                cfg = dlg.Result;
            }

            try
            {
                using (var tx = new Transaction(ctx.Doc, "STING Plumbing — Save System Config"))
                {
                    tx.Start();
                    PlumbingSystemConfig.Save(ctx.Doc, cfg);
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("Plumb_SaveSystemConfig persist", ex);
                message = "Could not save plumbing system config: " + ex.Message;
                return Result.Failed;
            }

            var result = StingResultPanel.Create("Plumbing System Config Saved");
            result.SetSubtitle($"Path: {PlumbingSystemConfig.ProjectConfigPath(ctx.Doc) ?? "(project not saved)"}");
            result.AddSection("ACTIVE")
                 .Metric("Building",         cfg.BuildingType)
                 .Metric("K factor",         cfg.KFactor.ToString("F2"))
                 .Metric("Drainage standard", cfg.DrainStandard)
                 .Metric("Supply standard",   cfg.SupplyStandard)
                 .Metric("Head-loss method",  cfg.HeadLossMethod ?? "—")
                 .Metric("Sizing strategy",   cfg.SupplySizingStrategy ?? "—")
                 .Metric("DCW material",      cfg.MaterialFor("DCW"))
                 .Metric("DHW material",      cfg.MaterialFor("DHW"))
                 .Metric("Drain material",    cfg.MaterialFor("Drainage"))
                 .Metric("Storm material",    cfg.MaterialFor("Storm"))
                 .Metric("Vent material",     cfg.MaterialFor("Vent"))
                 .Metric("Supply pressure",   cfg.SupplyPressureBarAtEntry.ToString("F2") + " bar")
                 .Metric("Max Δp",            cfg.MaxPressureDropPaPerM.ToString("F0") + " Pa/m");
            result.Show();
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

            // Push the loaded config into the SYSTEM-tab inline form (if the
            // panel is open) so the user can see + edit the on-disk values.
            try
            {
                var panel = StingPlumbingPanel.Instance;
                panel?.Dispatcher.Invoke(() => panel.LoadSystemForm(cfg));
            }
            catch (Exception ex) { StingLog.Warn($"Plumb_LoadSystemConfig form push: {ex.Message}"); }

            var panelResult = StingResultPanel.Create("Plumbing System Config (current)");
            panelResult.SetSubtitle($"Path: {PlumbingSystemConfig.ProjectConfigPath(ctx.Doc) ?? "(no project file)"}");
            panelResult.AddSection("PROJECT")
                 .Metric("Building",            cfg.BuildingType)
                 .Metric("K factor",            cfg.KFactor.ToString("F2"))
                 .Metric("Drainage standard",   cfg.DrainStandard)
                 .Metric("Supply standard",     cfg.SupplyStandard)
                 .Metric("Head-loss method",    cfg.HeadLossMethod ?? "—")
                 .Metric("Sizing strategy",     cfg.SupplySizingStrategy ?? "—")
                 .Metric("Flush valve majority", cfg.FlushValveMajority ? "yes" : "no")
                 .Metric("Occupancy",           cfg.OccupancyCount.ToString())
                 .Metric("Beds / workstations", cfg.BedsOrWorkstations.ToString())
                 .Metric("Saved (UTC)",         string.IsNullOrEmpty(cfg.LastSavedUtc) ? "(never)" : cfg.LastSavedUtc);

            panelResult.AddSection("MATERIALS");
            foreach (var kv in cfg.Materials.OrderBy(k => k.Key))
                panelResult.Metric(kv.Key, kv.Value);

            panelResult.AddSection("VELOCITY LIMITS (m/s)");
            foreach (var kv in cfg.VelocityMps.OrderBy(k => k.Key))
                panelResult.Metric(kv.Key, kv.Value.ToString("F2"));

            panelResult.AddSection("MIN SLOPE (%)");
            foreach (var kv in cfg.SlopePctMin.OrderBy(k => k.Key))
                panelResult.Metric(kv.Key, kv.Value.ToString("F2"));

            panelResult.Show();
            return Result.Succeeded;
        }
    }
}
