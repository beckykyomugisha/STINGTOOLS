// StingTools — IDU selection from the catalogue.
//
// Phase 187h. Walks every Space with a stamped HVC_PEAK_SENS_W (from
// BlockLoad), picks the matching IDU from STING_IDU_CATALOGUE.json
// for the project's active vendor series + refrigerant, and reports
// the per-space pick + capacity ratio + total connected load.
//
// Stamps onto each Space:
//   HVC_SELECTED_IDU_ID_TXT     — the catalogue id of the chosen unit
//   HVC_SELECTED_IDU_LABEL_TXT  — human-readable label

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Refrigerant;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacSelectIdusCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                var pi = doc.ProjectInformation;
                string vendorSeries = pi?.LookupParameter("PRJ_REFRIG_VENDOR_SERIES_TXT")?.AsString();
                string refrigerant  = pi?.LookupParameter("PRJ_REFRIG_FLUID_TXT")?.AsString() ?? "R410A";
                if (string.IsNullOrWhiteSpace(vendorSeries))
                {
                    TaskDialog.Show("STING HVAC — IDU Select",
                        "Set PRJ_REFRIG_VENDOR_SERIES_TXT on Project Information first " +
                        "(e.g. 'Daikin-VRV-5', 'Mitsubishi-CityMulti-Y').");
                    return Result.Cancelled;
                }
                string defaultMounting = pi?.LookupParameter("PRJ_REFRIG_IDU_MOUNTING_TXT")?.AsString()
                    ?? "Ducted";

                var cat = IduCatalogueRegistry.Get(doc);
                if (cat.Units.Count == 0)
                {
                    TaskDialog.Show("STING HVAC — IDU Select",
                        "No units in STING_IDU_CATALOGUE.json or project override.");
                    return Result.Cancelled;
                }

                var spaces = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MEPSpaces)
                    .WhereElementIsNotElementType()
                    .Cast<Space>()
                    .Where(s => s.Area > 0)
                    .ToList();

                int picked = 0, noLoad = 0, noMatch = 0;
                double totalKw = 0, totalDutyKw = 0;
                var perPick = new List<string>();
                using (var tx = new Transaction(doc, "STING IDU Select"))
                {
                    tx.Start();
                    foreach (var sp in spaces)
                    {
                        double dutyKw = ReadDouble(sp, "HVC_PEAK_SENS_W") / 1000.0;
                        if (dutyKw <= 0) { noLoad++; continue; }
                        totalDutyKw += dutyKw;

                        // Per-space mounting override (HVC_IDU_MOUNTING_TXT)
                        // beats the project default.
                        string mounting = sp.LookupParameter("HVC_IDU_MOUNTING_TXT")?.AsString();
                        if (string.IsNullOrWhiteSpace(mounting)) mounting = defaultMounting;

                        var duty = new IduDuty
                        {
                            VendorSeriesId = vendorSeries,
                            RefrigerantId  = refrigerant,
                            MountingType   = mounting,
                            DutyKw         = dutyKw,
                            // OA L/s from BlockLoad as the minimum fan flow.
                            MinFlowLs      = ReadDouble(sp, "HVC_OA_LS"),
                            MaxNc          = 35
                        };
                        var pick = IduSelector.Pick(cat, duty);
                        if (pick.Best == null) { noMatch++; continue; }

                        try
                        {
                            ParameterHelpers.SetString(sp, "HVC_SELECTED_IDU_ID_TXT",
                                pick.Best.Id, overwrite: true);
                            ParameterHelpers.SetString(sp, "HVC_SELECTED_IDU_LABEL_TXT",
                                pick.Best.Label, overwrite: true);
                            picked++;
                            totalKw += pick.Best.NominalCoolingKw;
                            if (perPick.Count < 40)
                                perPick.Add(
                                    $"  {sp.Name} (duty {dutyKw:F1} kW) → {pick.Best.Id} " +
                                    $"({pick.Best.NominalCoolingKw:F1} kW · ratio {pick.CapacityRatio:F2})");
                        }
                        catch (Exception ex) { StingLog.Warn($"IduSelect stamp {sp.Id}: {ex.Message}"); }
                    }
                    tx.Commit();
                }

                var panel = StingResultPanel.Create("HVAC — IDU Select");
                panel.SetSubtitle($"vendor={vendorSeries} · refrigerant={refrigerant} · mounting={defaultMounting}");
                panel.AddSection("SUMMARY")
                     .Metric("Spaces scanned",         spaces.Count.ToString())
                     .Metric("IDUs picked",            picked.ToString())
                     .Metric("Spaces without load",    noLoad.ToString())
                     .Metric("No catalogue match",     noMatch.ToString())
                     .Metric("Σ design duty",          $"{totalDutyKw:F1} kW")
                     .Metric("Σ selected nominal",     $"{totalKw:F1} kW")
                     .Metric("Avg over-sizing ratio",  totalDutyKw > 0
                         ? $"{totalKw / totalDutyKw:F2}"
                         : "n/a");
                if (perPick.Count > 0)
                {
                    panel.AddSection("PER-SPACE PICK (first 40)");
                    foreach (var s in perPick) panel.Text(s);
                }
                panel.Text("Picks smallest-capacity IDU that meets duty + min flow + NC target. " +
                           "Stamps HVC_SELECTED_IDU_ID_TXT + HVC_SELECTED_IDU_LABEL_TXT on Spaces. " +
                           "Set HVC_IDU_MOUNTING_TXT per-space to override the project mounting " +
                           "(Ducted / CeilingCassette / WallMounted).");
                panel.Show();
                try { StingHvacPanel.Instance?.PushRunRow($"IDU select ({picked} picked)", "⬤"); }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacSelectIdusCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static double ReadDouble(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.String &&
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
