// PlumbingSystemCommands — Phase 179a / 187 SYSTEM tab.
//
// Plumb_SaveSystemConfig — reads the inline form on the SYSTEM tab of
//   StingPlumbingPanel and persists it via PlumbingSystemConfig.Save.
//   When the panel isn't open the command refuses (no modal dialog
//   fallback — the legacy PlumbingSystemConfigDialog was retired in
//   Phase 187 because the inline form replaces it).
// Plumb_LoadSystemConfig — reads the on-disk config, pushes the values
//   back into the inline form (when the panel is open).

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

            var panel = StingPlumbingPanel.Instance;
            if (panel == null)
            {
                TaskDialog.Show("STING Plumbing — Save System Config",
                    "Open the STING Plumbing panel and edit the SYSTEM tab before saving.");
                return Result.Cancelled;
            }

            PlumbingSystemConfig cfg;
            try
            {
                var existing = PlumbingSystemConfig.Load(ctx.Doc);
                cfg = panel.Dispatcher.Invoke(() => panel.ReadSystemForm(existing));
            }
            catch (Exception ex)
            {
                StingLog.Error("Plumb_SaveSystemConfig ReadSystemForm", ex);
                message = "Could not read inline SYSTEM form: " + ex.Message;
                panel?.ShowInlineResult("Save System Config: read failed — " + ex.Message);
                return Result.Failed;
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
                panel?.ShowInlineResult("Save System Config: persist failed — " + ex.Message);
                return Result.Failed;
            }

            // Inline summary — no popup window. The activity-log surface in
            // the SYSTEM tab captures the full single-line confirmation.
            string summary =
                $"Saved · Building={cfg.BuildingType} · K={cfg.KFactor:F2} · " +
                $"Drain={cfg.DrainStandard} · Supply={cfg.SupplyStandard} · " +
                $"DCW={cfg.MaterialFor("DCW")} · DHW={cfg.MaterialFor("DHW")} · " +
                $"HeadLoss={cfg.HeadLossMethod} · Sizing={cfg.SupplySizingStrategy} · " +
                $"InletBar={cfg.SupplyPressureBarAtEntry:F2}";
            panel.ShowInlineResult("Save System Config: " + summary);
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

            // Push the loaded config into the inline form (if the panel is
            // open) and log a one-line summary inline. No modal popup.
            var panel = StingPlumbingPanel.Instance;
            try { panel?.Dispatcher.Invoke(() => panel.LoadSystemForm(cfg)); }
            catch (Exception ex) { StingLog.Warn($"Plumb_LoadSystemConfig push: {ex.Message}"); }

            string summary =
                $"Loaded · Building={cfg.BuildingType} · K={cfg.KFactor:F2} · " +
                $"Drain={cfg.DrainStandard} · Supply={cfg.SupplyStandard} · " +
                $"DCW={cfg.MaterialFor("DCW")} · DHW={cfg.MaterialFor("DHW")} · " +
                $"Saved={(string.IsNullOrEmpty(cfg.LastSavedUtc) ? "(never)" : cfg.LastSavedUtc)}";
            panel?.ShowInlineResult("Load System Config: " + summary);

            // For headless / no-panel paths still emit a TaskDialog so the
            // user sees something.
            if (panel == null)
            {
                TaskDialog.Show("STING Plumbing — System Config",
                    summary.Replace(" · ", "\n"));
            }
            return Result.Succeeded;
        }
    }
}

