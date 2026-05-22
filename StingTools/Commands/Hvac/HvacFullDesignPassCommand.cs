// StingTools — HVAC full design pass.
//
// Runs the canonical block-load → propagate → auto-size → balance →
// validate sequence in one transaction-grouped pass. Equivalent to
// running WORKFLOW_HVACDesign.json step-by-step but cheaper to invoke
// from a workflow chip / dock-panel button + collects a single
// summary panel instead of one TaskDialog per step.
//
// Honours the standard HVAC panel scope radio + climate site.
// Cancellable via the StingProgressDialog if running on a large project.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacFullDesignPassCommand : IExternalCommand
    {
        // Steps in the canonical design pass. Each entry is (label, command).
        // Order matters — earlier steps' stamps feed later ones (e.g. BlockLoad
        // writes HVC_PEAK_SENS_W which PropagateLoads consumes).
        private static readonly (string label, Type cmd)[] Steps = new (string, Type)[]
        {
            ("Block load",          typeof(HvacBlockLoadCommand)),
            ("Propagate loads",     typeof(HvacPropagateLoadsCommand)),
            ("Auto-size ducts",     typeof(StingTools.Commands.Mep.MepAutoSizeDuctCommand)),
            ("Hardy-Cross balance", typeof(StingTools.Commands.Routing.HardyCrossCommand)),
            ("NC prediction",       typeof(HvacNcPredictionCommand)),
            ("Pressure-class audit",typeof(HvacPressureClassAuditCommand)),
            ("Stale-size scan",     typeof(HvacDetectStaleSizesCommand))
        };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                int ran = 0, ok = 0, cancelled = 0, failed = 0;
                var rows = new System.Collections.Generic.List<(string Label, string Result, string Note)>();

                foreach (var (label, cmd) in Steps)
                {
                    if (EscapeChecker.IsEscapePressed())
                    {
                        rows.Add((label, "✗ cancelled", "User pressed Escape between steps."));
                        cancelled++;
                        break;
                    }
                    ran++;
                    try
                    {
                        var inst = (IExternalCommand)Activator.CreateInstance(cmd);
                        string subMsg = "";
                        var res = inst.Execute(commandData, ref subMsg, new ElementSet());
                        switch (res)
                        {
                            case Result.Succeeded:
                                rows.Add((label, "⬤ ok", subMsg ?? "")); ok++; break;
                            case Result.Cancelled:
                                rows.Add((label, "⬡ cancelled", subMsg ?? "")); cancelled++; break;
                            default:
                                rows.Add((label, "✗ failed", subMsg ?? "")); failed++; break;
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        rows.Add((label, "⬡ cancelled", "User cancelled inside step.")); cancelled++; break;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"HvacFullDesignPass step '{label}'", ex);
                        rows.Add((label, "✗ failed", ex.Message)); failed++;
                    }
                }

                var panel = StingResultPanel.Create("HVAC — Full Design Pass");
                panel.SetSubtitle($"{ok} ok · {cancelled} cancelled · {failed} failed · {Steps.Length - ran} not run");
                panel.AddSection("STEPS");
                foreach (var (label, result, note) in rows)
                    panel.Text($"{result,-15} {label,-25} {note}");
                panel.Text("Each step opens its own transaction. The full pass writes block-load → " +
                           "HVC_FLOW_LS → sized duct → balanced flow → predicted NC → ΔP-class report. " +
                           "Use Hvac_PropagateLoads / Hvac_AutoSizeDuct individually for partial re-runs.");
                panel.Show();

                try
                {
                    StingHvacPanel.Instance?.PushRunRow(
                        $"Full design pass ({ok} ok / {failed} failed)",
                        failed == 0 ? "⬤" : "⬡");
                }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }

                return failed == 0 ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacFullDesignPassCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
