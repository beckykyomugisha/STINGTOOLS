using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.V6
{
    /// <summary>N-G12: command surface for LabourHoursEngine.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ApplyLabourHoursCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                var doc = ctx.Doc;

                // Scope: active selection if any, else every element whose category appears in the rate table.
                var selIds = ctx.UIDoc?.Selection?.GetElementIds();
                List<Element> targets;
                if (selIds != null && selIds.Count > 0)
                {
                    targets = selIds.Select(id => doc.GetElement(id)).Where(e => e != null).ToList();
                }
                else
                {
                    var rates = LabourHoursEngine.LoadRates();
                    var allowed = new HashSet<string>(
                        rates.Select(r => r.Category), StringComparer.OrdinalIgnoreCase);
                    targets = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .Where(e => e.Category != null && allowed.Contains(e.Category.Name))
                        .ToList();
                }

                LabourHoursEngine.ApplyResult r;
                using (var t = new Transaction(doc, "STING Apply Labour Hours"))
                {
                    t.Start();
                    r = LabourHoursEngine.Apply(doc, targets);
                    t.Commit();
                }

                string crewSummary = string.Join("\n", r.ByCrew
                    .OrderByDescending(kv => kv.Value.hours)
                    .Select(kv => $"  {kv.Key}: {kv.Value.count} items, {kv.Value.hours:0.0} hrs"));
                TaskDialog.Show("STING",
                    $"Labour hours applied to {r.ElementsTouched} elements.\n" +
                    $"Total: {r.TotalHours:0.0} hrs  |  £{r.TotalCostGbp:0.00}\n\n{crewSummary}");
                StingLog.Info($"LabourHours: {r.ElementsTouched} elements, {r.TotalHours:0.0} hrs, £{r.TotalCostGbp:0.00}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ApplyLabourHoursCommand failed", ex);
                TaskDialog.Show("STING", $"Labour hours failed: {ex.Message}");
                return Result.Failed;
            }
        }
    }

    /// <summary>Exports labour hours summary to CSV (per-crew + per-category breakdown).</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportLabourHoursCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                var doc = ctx.Doc;

                // Aggregate existing CST_INSTALL_HRS across the model — no writes.
                var perCrew = new Dictionary<string, (int count, double hrs, double cost)>();
                var perCat = new Dictionary<string, (int count, double hrs)>();
                foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    string hrsStr = ParameterHelpers.GetString(el, ParamRegistry.CST_INSTALL_HRS);
                    if (string.IsNullOrEmpty(hrsStr)) continue;
                    if (!double.TryParse(hrsStr, out var hrs)) continue;
                    string crew = ParameterHelpers.GetString(el, ParamRegistry.CST_LABOUR_CREW_TXT) ?? "";
                    double rate = 0; double.TryParse(
                        ParameterHelpers.GetString(el, ParamRegistry.CST_LABOUR_RATE_GBP) ?? "0", out rate);
                    double cost = hrs * rate;
                    if (!perCrew.TryGetValue(crew, out var c)) c = (0, 0, 0);
                    perCrew[crew] = (c.count + 1, c.hrs + hrs, c.cost + cost);
                    string cat = el.Category?.Name ?? "(uncategorised)";
                    if (!perCat.TryGetValue(cat, out var k)) k = (0, 0);
                    perCat[cat] = (k.count + 1, k.hrs + hrs);
                }

                string path = OutputLocationHelper.GetTimestampedPath(doc, "STING_LabourHours", ".csv");
                using (var w = new System.IO.StreamWriter(path))
                {
                    w.WriteLine("Section,Key,Count,Hours,Cost_GBP");
                    foreach (var kv in perCrew.OrderByDescending(x => x.Value.hrs))
                        w.WriteLine($"Crew,{kv.Key},{kv.Value.count},{kv.Value.hrs:0.00},{kv.Value.cost:0.00}");
                    foreach (var kv in perCat.OrderByDescending(x => x.Value.hrs))
                        w.WriteLine($"Category,\"{kv.Key}\",{kv.Value.count},{kv.Value.hrs:0.00},");
                }
                TaskDialog.Show("STING", $"Labour hours exported:\n{path}");
                StingLog.Info($"LabourHours exported: {path}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportLabourHoursCommand failed", ex);
                TaskDialog.Show("STING", $"Export failed: {ex.Message}");
                return Result.Failed;
            }
        }
    }
}
