// StingTools — Refrigerant additional-charge calculator (Phase 187g).
//
// Walks per-OD pipe runs in the project (refrigerant pipe segments
// grouped by OD) and computes the additional field-charge against the
// vendor's published kg/m table. Output: kg + per-OD breakdown + the
// vendor short-system offset (if total length is under threshold).
//
// Pipe runs are read from refrigerant pipes in the scope, grouped by
// their actual OD. Falls back to "no pipes selected" when no refrigerant
// pipes are found — user can also supply per-OD entries manually via
// the dialog (future enhancement; v1 ships project-scan only).

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Refrigerant;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacRefrigerantChargeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                // 1. Resolve vendor series + refrigerant from Project Info
                //    parameters (set by user / Project Setup wizard).
                string vendorSeriesId = doc.ProjectInformation?
                    .LookupParameter("PRJ_REFRIG_VENDOR_SERIES_TXT")?.AsString();
                string refrigerantId = doc.ProjectInformation?
                    .LookupParameter("PRJ_REFRIG_FLUID_TXT")?.AsString() ?? "R410A";
                if (string.IsNullOrWhiteSpace(vendorSeriesId))
                {
                    TaskDialog.Show("STING HVAC — Refrigerant Charge",
                        "Set PRJ_REFRIG_VENDOR_SERIES_TXT on Project Information first " +
                        "(e.g. 'Daikin-VRV-5', 'Mitsubishi-CityMulti-R2'). The vendor series " +
                        "drives the per-OD charge-per-metre lookup. PRJ_REFRIG_FLUID_TXT " +
                        "selects the fluid (defaults to R410A).");
                    return Result.Cancelled;
                }

                var chargeTable = RefrigerantChargeRegistry.Get(doc).Find(vendorSeriesId, refrigerantId);
                if (chargeTable == null)
                {
                    TaskDialog.Show("STING HVAC — Refrigerant Charge",
                        $"No charge table found for vendor '{vendorSeriesId}' + refrigerant '{refrigerantId}'.\n\n" +
                        "Add an entry to STING_REFRIG_CHARGE_TABLES.json or its project override.");
                    return Result.Cancelled;
                }

                // 2. Scan refrigerant pipes in scope. Pipes whose system
                //    abbreviation contains "REFRIG" or "RFRG" qualify;
                //    fall back to "all pipes" if the project doesn't use that
                //    convention.
                var pipes = new FilteredElementCollector(doc)
                    .OfClass(typeof(Pipe))
                    .Cast<Pipe>()
                    .Where(IsRefrigerantPipe)
                    .ToList();
                if (pipes.Count == 0)
                {
                    pipes = new FilteredElementCollector(doc)
                        .OfClass(typeof(Pipe)).Cast<Pipe>()
                        .ToList();
                    StingLog.Info($"HvacRefrigCharge: no REFRIG-marked pipes; using all {pipes.Count} pipes");
                }

                // 3. Group by OD (Diameter in mm) + sum lengths.
                var byOd = new Dictionary<double, double>();
                foreach (var p in pipes)
                {
                    try
                    {
                        double odMm = UnitUtils.ConvertFromInternalUnits(p.Diameter, UnitTypeId.Millimeters);
                        double lenM = 0;
                        if (p.Location is LocationCurve lc && lc.Curve != null)
                            lenM = UnitUtils.ConvertFromInternalUnits(lc.Curve.Length, UnitTypeId.Meters);
                        if (odMm <= 0 || lenM <= 0) continue;
                        double rounded = Math.Round(odMm, 2);
                        byOd[rounded] = byOd.TryGetValue(rounded, out var v) ? v + lenM : lenM;
                    }
                    catch (Exception ex) { StingLog.Warn($"RefrigCharge pipe {p.Id}: {ex.Message}"); }
                }

                var runs = byOd.OrderBy(k => k.Key)
                    .Select(kv => new PipeRun { OdMm = kv.Key, LengthM = kv.Value })
                    .ToList();

                // 4. Compute.
                var breakdown = RefrigerantChargeCalculator.Compute(runs, chargeTable);

                // 5. Report.
                var panel = StingResultPanel.Create("HVAC — Refrigerant Additional Charge");
                panel.SetSubtitle($"vendor={vendorSeriesId} · refrigerant={refrigerantId} · {pipes.Count} pipes");
                panel.AddSection("RESULT")
                     .Metric("Vendor table",          chargeTable.Label)
                     .Metric("Source",                chargeTable.Source)
                     .Metric("Additional charge",     $"{breakdown.TotalKg:F2} kg")
                     .Metric("Short-system offset",   breakdown.OffsetApplied
                         ? $"{breakdown.OffsetKg:+0.0;-0.0;0.0} kg applied"
                         : "(not applied)");

                panel.AddSection("PER-OD BREAKDOWN");
                if (breakdown.Lines.Count == 0)
                    panel.Text("No refrigerant pipes in scope.");
                foreach (var (od, lenM, kgPerM, sub, matched) in breakdown.Lines)
                    panel.Text($"  OD {od,6:F2} mm × {lenM,7:F1} m × {kgPerM,6:F3} kg/m = {sub,6:F2} kg" +
                               (matched ? "" : "  (no table match — skipped)"));

                panel.Text("Charge = Σ length_per_OD × kg/m from vendor table. " +
                           "Add factory-charge from outdoor-unit nameplate for total system charge. " +
                           "Set PRJ_REFRIG_VENDOR_SERIES_TXT + PRJ_REFRIG_FLUID_TXT on Project Info " +
                           "to drive the lookup; override per-project at _BIM_COORD/refrig_charge_tables.json.");
                panel.Show();

                try
                {
                    StingHvacPanel.Instance?.PushRunRow(
                        $"Refrig charge ({breakdown.TotalKg:F1} kg @ {vendorSeriesId})", "⬤");
                }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacRefrigerantChargeCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static bool IsRefrigerantPipe(Pipe p)
        {
            try
            {
                var sys = p.MEPSystem;
                if (sys == null) return false;
                string nm = (sys.Name ?? "").ToUpperInvariant();
                return nm.Contains("REFRIG") || nm.Contains("RFRG") || nm.Contains("VRV") || nm.Contains("VRF");
            }
            catch { return false; }
        }
    }
}
