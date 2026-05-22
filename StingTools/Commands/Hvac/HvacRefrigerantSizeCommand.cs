// StingTools — Refrigerant pipe sizing command.
//
// Pops the RefrigerantSizingDialog, runs RefrigerantPipeSolver,
// reports the chosen size + velocity + ΔP + vendor compliance.
// Unlocks the VRF/VRV market segment — STING previously had no path
// for sizing refrigerant systems (water rules don't apply: two-phase
// flow, oil-return constraints, manufacturer length+lift envelopes).

using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Refrigerant;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacRefrigerantSizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var dlg = new RefrigerantSizingDialog();
                bool? ok = dlg.ShowDialog();
                if (ok != true || dlg.Result == null) return Result.Cancelled;

                var input = dlg.Result;
                var fluid = RefrigerantProperties.Get(input.RefrigerantId);
                // If the user left the dialog's ΔP budget at its default 30 kPa,
                // substitute the vendor-recommended per-leg + per-fluid value
                // (e.g. R134a liquid = 30, CO₂ discharge = 100). The dialog
                // surface stays simple; the registry carries the right number.
                if (Math.Abs(input.MaxPressureDropKpa - 30.0) < 0.01)
                    input.MaxPressureDropKpa = fluid.DefaultBudgetForLeg(input.Leg);
                var result = RefrigerantPipeSolver.Size(input);

                var panel = StingResultPanel.Create($"HVAC — {input.RefrigerantId} {input.Leg}");
                panel.SetSubtitle(
                    $"{fluid.Label} · Tsat={fluid.SuctionSatTempC}/{fluid.CondSatTempC} °C · " +
                    $"capacity {input.CapacityKw:F1} kW · L_eq {input.EquivLengthM:F0} m · lift {input.LiftM:F0} m");

                if (result.Ok)
                {
                    var section = panel.AddSection("SELECTED SIZE")
                         .Metric("OD (ACR copper)",   $"{result.SelectedBoreMm:F2} mm")
                         .Metric("Velocity",          $"{result.VelocityMs:F1} m/s")
                         .Metric("Mass flow",         $"{result.MassFlowKgS * 1000:F2} g/s")
                         .Metric("ΔP",                $"{result.PressureDropKpa:F1} kPa")
                         .Metric("Lift static head",  $"{result.LiftPenaltyKpa:+0.0;-0.0;0.0} kPa " +
                                                       (result.LiftPenaltyKpa < 0 ? "(gravity assist)" : ""))
                         .Metric("Re",                $"{result.ReynoldsNumber:E2}")
                         .Metric("Friction f",        $"{result.FrictionFactor:F4}");
                    if (input.Leg == RefrigerantLeg.Liquid && result.SatTempDropK > 0)
                        section.Metric("Sat-temp drop", $"{result.SatTempDropK:F1} K (reserve {input.SubcoolingReserveK:F1} K)");
                }
                else
                {
                    panel.AddSection("NO MATCH")
                         .Text("No ACR copper size in the catalogue passes both the oil-return " +
                               "velocity floor and the ΔP budget. Lower the capacity, shorten the run, " +
                               "or raise the ΔP budget.");
                }

                if (result.Warnings.Count > 0)
                {
                    panel.AddSection("WARNINGS");
                    foreach (var w in result.Warnings) panel.Text("⚠ " + w);
                }

                panel.AddSection("SIZE SWEEP TRACE");
                foreach (var t in result.Trace.Take(20))
                    panel.Text($"OD {t.DiaMm,5:F2} mm  v {t.VelMs,5:F1} m/s  ΔP {t.DpKpa,6:F1} kPa  {t.Reason}");

                panel.AddSection("VENDOR ENVELOPE (DAIKIN VRV / EQUIV)")
                     .Metric("Max L_eq",          $"{fluid.MaxEquivLengthM:F0} m")
                     .Metric("Max lift (above)",  $"{fluid.MaxLiftAboveIndoorM:F0} m")
                     .Metric("Max drop (below)",  $"{fluid.MaxLiftBelowIndoorM:F0} m")
                     .Text($"Source: {fluid.Source}");

                panel.Text("Method: Darcy-Weisbach with Blasius f (smooth ACR copper). " +
                           "Suction leg includes a 10% two-phase pressure-drop multiplier. " +
                           "Liquid leg static head subtracted from ΔP budget.");
                panel.Show();

                try
                {
                    StingHvacPanel.Instance?.PushRunRow(
                        result.Ok
                            ? $"Refrig {input.RefrigerantId} {input.Leg} → {result.SelectedBoreMm:F1} mm"
                            : $"Refrig {input.RefrigerantId} {input.Leg} — no match",
                        result.Ok ? "⬤" : "⬡");
                }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacRefrigerantSizeCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
