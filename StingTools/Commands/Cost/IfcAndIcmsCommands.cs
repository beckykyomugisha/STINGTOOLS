// ══════════════════════════════════════════════════════════════════════════
//  IfcAndIcmsCommands.cs — P8 user-facing commands.
//
//  Cost_StampIfcQuantities — populate IFC4 Qto_* + Pset_StingCost on
//                             every BOQ element so the next IFC export
//                             carries the cost + quantity data.
//  Cost_ExportIcms3Report  — produce a CSV with cost £ + carbon kgCO₂e
//                             side by side per ICMS3 group code.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.BIMManager;
using StingTools.BOQ;
using StingTools.BOQ.MeasurementStandard;
using StingTools.Core;

namespace StingTools.Commands.Cost
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostStampIfcQuantitiesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var boq = BOQCostManager.BuildBOQDocument(doc);
                if (boq.AllItems.Count == 0)
                {
                    TaskDialog.Show("STING IFC Qto", "BOQ has no items — build the BOQ first.");
                    return Result.Cancelled;
                }

                int stamped;
                using (var t = new Transaction(doc, "STING — stamp IFC Qto + Pset_StingCost"))
                {
                    t.Start();
                    stamped = IfcQuantitySetWriter.StampAllElements(doc, boq);
                    t.Commit();
                }

                TaskDialog.Show("STING — IFC stamping",
                    $"Stamped {stamped} element(s) with IFC4 Qto_* + Pset_StingCost.\n\n" +
                    "The next IFC export will carry quantity + cost data so Cost-X, CostOS, " +
                    "Candy and Bluebeam Revu can ingest without re-measuring.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Cost_StampIfcQuantities", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostExportIcms3ReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var boq = BOQCostManager.BuildBOQDocument(doc);
                if (boq.AllItems.Count == 0)
                {
                    TaskDialog.Show("STING ICMS3", "BOQ has no items.");
                    return Result.Cancelled;
                }

                // Group by ICMS3 group code. The ICMS3 standard handles
                // the classification; we override it later for finer
                // sub-groupings (01.01 land acquisition, 02.05 services,
                // 03.04 maintenance, etc.) when project data supports it.
                var icms = MeasurementStandardRegistry.Get("icms3");
                var grouped = new Dictionary<string, (double cost, double carbon)>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var item in boq.AllItems)
                {
                    Element el = item.RevitElementId > 0
                        ? doc.GetElement(new ElementId(item.RevitElementId)) : null;
                    string group = icms.ClassifyRow(item, el);
                    if (!grouped.TryGetValue(group, out var cur))
                        cur = (0, 0);
                    cur.cost += item.TotalUGX;
                    cur.carbon += item.EmbodiedCarbonKg;
                    grouped[group] = cur;
                }

                string outDir = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "icms3");
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir,
                    $"icms3_report_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                using (var sw = new StreamWriter(outPath))
                {
                    sw.WriteLine("# STING ICMS3 — cost + carbon ledger (Phase 184j / P8)");
                    sw.WriteLine($"# Project:    {boq.ProjectName}");
                    sw.WriteLine($"# Generated:  {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
                    sw.WriteLine($"# Currency:   {boq.Currency}");
                    sw.WriteLine();
                    sw.WriteLine("ICMS3_Group,Cost,Carbon_kgCO2e,Cost_Per_kgCO2e");
                    foreach (var kv in grouped.OrderBy(x => x.Key))
                    {
                        double perKg = kv.Value.carbon > 0
                            ? Math.Round(kv.Value.cost / kv.Value.carbon, 2) : 0;
                        sw.WriteLine(string.Join(",", new[]
                        {
                            kv.Key,
                            kv.Value.cost.ToString("F2", CultureInfo.InvariantCulture),
                            kv.Value.carbon.ToString("F2", CultureInfo.InvariantCulture),
                            perKg.ToString("F2", CultureInfo.InvariantCulture)
                        }));
                    }
                    double totalCost = grouped.Sum(x => x.Value.cost);
                    double totalCarbon = grouped.Sum(x => x.Value.carbon);
                    double overall = totalCarbon > 0
                        ? Math.Round(totalCost / totalCarbon, 2) : 0;
                    sw.WriteLine();
                    sw.WriteLine("TOTAL,"
                        + totalCost.ToString("F2", CultureInfo.InvariantCulture) + ","
                        + totalCarbon.ToString("F2", CultureInfo.InvariantCulture) + ","
                        + overall.ToString("F2", CultureInfo.InvariantCulture));
                }

                TaskDialog.Show("STING — ICMS3 report",
                    $"Cost + carbon ledger exported.\n\n" +
                    $"Groups:     {grouped.Count}\n" +
                    $"Total cost: {boq.Currency} {grouped.Sum(x => x.Value.cost):N0}\n" +
                    $"Total carbon: {grouped.Sum(x => x.Value.carbon):N0} kgCO₂e\n\n" +
                    $"Path: {outPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Cost_ExportIcms3Report", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
