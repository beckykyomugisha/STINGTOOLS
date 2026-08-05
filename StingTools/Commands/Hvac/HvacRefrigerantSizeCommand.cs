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
                if (Math.Abs(input.MaxPressureDropKpa - 30.0) < 0.01)
                    input.MaxPressureDropKpa = fluid.DefaultBudgetForLeg(input.Leg);

                // Phase 187f — pass the active document so RefrigerantPipeSolver
                // can resolve vendor-series length tables from the registry.
                try
                {
                    var ctx = ParameterHelpers.GetContext(commandData);
                    if (ctx != null) input.Document = ctx.Doc;
                }
                catch { }

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

                panel.AddSection("GENERIC FLUID ENVELOPE")
                     .Metric("Max L_eq",          $"{fluid.MaxEquivLengthM:F0} m")
                     .Metric("Max lift (above)",  $"{fluid.MaxLiftAboveIndoorM:F0} m")
                     .Metric("Max drop (below)",  $"{fluid.MaxLiftBelowIndoorM:F0} m")
                     .Text($"Source: {fluid.Source}");

                // Vendor envelope if a series was picked.
                if (!string.IsNullOrEmpty(result.VendorSeriesId))
                {
                    var vendor = StingTools.Core.Refrigerant.RefrigerantVendorRegistry.Get(input.Document)
                        .Get(result.VendorSeriesId);
                    if (vendor != null)
                    {
                        panel.AddSection($"VENDOR ENVELOPE — {vendor.Label}")
                             .Metric("Max actual oneway L",   $"{vendor.ActualOnewayMaxM:F0} m")
                             .Metric("Max equiv oneway L",    $"{vendor.EquivalentOnewayMaxM:F0} m")
                             .Metric("Max total system L",    $"{vendor.TotalPipeLengthM:F0} m")
                             .Metric("First-branch → far IDU",$"{vendor.FirstBranchToFarIduActualM:F0} m actual / {vendor.FirstBranchToFarIduEquivM:F0} m equiv")
                             .Metric("ODU↔IDU vertical max",  $"{vendor.VerticalHighLowOduIduM:F0} m")
                             .Metric("IDU↔IDU vertical max",  $"{vendor.VerticalHighLowIduIduM:F0} m")
                             .Text($"Source: {vendor.Source}");
                    }
                }

                // Phase 187d — refrigerant ↔ duct linkage. Ducted indoor units
                // (ceiling-concealed VRF) have BOTH refrigerant pipes AND a
                // supply duct connector. If the active document has any such
                // IDU, surface it so the user knows to size both.
                try
                {
                    var ctx = ParameterHelpers.GetContext(commandData);
                    var ducted = FindDuctedIndoorUnits(ctx?.Doc, input.CapacityKw);
                    if (ducted.Count > 0)
                    {
                        panel.AddSection("LINKED DUCTED IDUs");
                        foreach (var idu in ducted)
                            panel.Text($"  {idu}");
                        panel.Text("Each ducted IDU also needs its supply / return duct sized. " +
                                   "Run Hvac_AutoSizeDuct on the connected duct system once flow is stamped " +
                                   "(via Hvac_PropagateLoads or manually).");
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Refrig IDU link scan: {ex.Message}"); }

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

        /// <summary>
        /// Scan the project for mechanical equipment whose family/type name
        /// suggests it's a ducted refrigerant indoor unit (FCU, ducted VRF
        /// IDU, fan-coil with supply ductwork) and capacity within ±50 %
        /// of the supplied design capacity. Returns label strings for the
        /// result panel; primary purpose is to nudge the user to size the
        /// supply ducts too.
        /// </summary>
        private static System.Collections.Generic.List<string> FindDuctedIndoorUnits(
            Autodesk.Revit.DB.Document doc, double designKw)
        {
            var list = new System.Collections.Generic.List<string>();
            if (doc == null) return list;
            try
            {
                var equipment = new Autodesk.Revit.DB.FilteredElementCollector(doc)
                    .OfCategory(Autodesk.Revit.DB.BuiltInCategory.OST_MechanicalEquipment)
                    .WhereElementIsNotElementType()
                    .OfClass(typeof(Autodesk.Revit.DB.FamilyInstance))
                    .Cast<Autodesk.Revit.DB.FamilyInstance>()
                    .ToList();
                foreach (var fi in equipment)
                {
                    string fam = (fi.Symbol?.Family?.Name ?? "").ToLowerInvariant();
                    string typ = (fi.Symbol?.Name ?? "").ToLowerInvariant();
                    bool ducted =
                        fam.Contains("ducted") || typ.Contains("ducted") ||
                        fam.Contains("fcu")    || typ.Contains("fcu") ||
                        fam.Contains("ceiling concealed") || typ.Contains("ceiling concealed") ||
                        fam.Contains("ahu");
                    if (!ducted) continue;
                    if (!HasDuctConnector(fi)) continue;
                    double cap = ReadDouble(fi, "HVC_CAPACITY_KW");
                    if (cap > 0 && designKw > 0 &&
                        (cap < designKw * 0.5 || cap > designKw * 1.5)) continue;
                    string tag = fi.LookupParameter("ASS_TAG_1")?.AsString() ?? $"#{fi.Id.Value}";
                    list.Add($"{tag} · {fi.Symbol?.Family?.Name} / {fi.Symbol?.Name} · capacity {cap:F1} kW");
                    if (list.Count >= 20) break;
                }
            }
            catch (System.Exception ex) { StingTools.Core.StingLog.Warn($"FindDuctedIndoorUnits: {ex.Message}"); }
            return list;
        }

        private static bool HasDuctConnector(Autodesk.Revit.DB.FamilyInstance fi)
        {
            try
            {
                var conns = fi.MEPModel?.ConnectorManager?.Connectors;
                if (conns == null) return false;
                foreach (Autodesk.Revit.DB.Connector c in conns)
                    if (c.Domain == Autodesk.Revit.DB.Domain.DomainHvac) return true;
            }
            catch { }
            return false;
        }

        private static double ReadDouble(Autodesk.Revit.DB.Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null) return 0;
                if (p.StorageType == Autodesk.Revit.DB.StorageType.Double) return p.AsDouble();
                if (p.StorageType == Autodesk.Revit.DB.StorageType.String &&
                    double.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double v)) return v;
            }
            catch { }
            return 0;
        }
    }
}
