// StingTools — Climate-registry diagnostic commands.
//
// Inspect: show the active climate site for the open document plus
// the corporate site catalogue.
// Reload: force the cache to drop so an edit to the corporate baseline
// or a project override is picked up without restarting Revit.

using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Climate;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacClimateInspectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                var doc = ctx?.Doc;
                var data = ClimateRegistry.Get(doc);
                var active = ClimateRegistry.ActiveSite(doc);

                var panel = StingResultPanel.Create("HVAC — Climate Registry");
                panel.SetSubtitle(
                    $"{data.Sites.Count} sites loaded · active: {active.Label} ({active.Source})");

                panel.AddSection("ACTIVE SITE")
                     .Metric("Id",                 active.Id)
                     .Metric("Country",            active.Country)
                     .Metric("Lat / Lon",          $"{active.Lat:F3} / {active.Lon:F3}")
                     .Metric("Elevation",          $"{active.ElevationM:F0} m")
                     .Metric("Cooling 0.4 % DB",   $"{active.Cooling996DbC:F1} °C")
                     .Metric("Cooling MCWB",       $"{active.Cooling996McwbC:F1} °C")
                     .Metric("Heating 99.6 % DB",  $"{active.Heating996DbC:F1} °C")
                     .Metric("HDD (base 18 °C)",   $"{active.Hdd18:F0}")
                     .Metric("CDD (base 10 °C)",   $"{active.Cdd10:F0}")
                     .Metric("ρ (cooling design)", $"{active.AirDensityCoolingKgM3():F3} kg/m³")
                     .Metric("ρ (heating design)", $"{active.AirDensityHeatingKgM3():F3} kg/m³");

                panel.AddSection("CATALOGUE");
                foreach (var s in data.Sites.OrderBy(x => x.Country).ThenBy(x => x.Label))
                    panel.Text($"{s.Id,-16} {s.Label,-32} {s.Country}  cool {s.Cooling996DbC,5:F1} °C  heat {s.Heating996DbC,5:F1} °C  ρ={s.AirDensityCoolingKgM3():F3}");

                panel.Text("Set the active site via Project Info > 'PRJ_CLIMATE_SITE_ID' " +
                           "(use the id column above), or via the Address field which is fuzzy-matched.");
                panel.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacClimateInspectCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacClimateReloadCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                ClimateRegistry.Reload();
                TaskDialog.Show("STING HVAC", "Climate registry reloaded — corporate baseline + project override re-read on next use.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacClimateReloadCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
