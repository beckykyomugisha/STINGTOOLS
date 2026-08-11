// ══════════════════════════════════════════════════════════════════════════
//  BOQBelowProductAuditCommand.cs — how much of the bill is an average?
//
//  Every rate provider sets RateResolutionLevel on its lookup, and until now
//  nothing read it: the field was computed and discarded one line later. So a
//  category average and a product-specific quotation reached the page looking
//  identical, and nobody could say what fraction of a tender was assumed.
//
//  This counts them. REPORT ONLY — it changes nothing, prices nothing, and
//  makes no recommendation about which rates to chase. It answers one question:
//  of the money in this bill, how much is priced at the product level, and how
//  much is an average over a category?
//
//  Levels, most specific first:
//    Product   the actual product — the only level that prices a fire door
//              differently from a cupboard door
//    System    category refined by MEP system
//    Material  material-level (the price book resolves here)
//    Category  one rate for everything in the category
//    None      not priced at all
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.BOQ.Rates;
using StingTools.Core;

namespace StingTools.BOQ
{
    [Transaction(TransactionMode.ReadOnly)]
    public class BOQBelowProductAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                var boq = BOQCostManager.BuildBOQDocument(doc, null, BoqGroupingMode.WorkSection);
                var lines = boq?.Sections?.SelectMany(s => s.Items).Where(i => i != null).ToList()
                            ?? new List<BOQLineItem>();
                if (lines.Count == 0)
                {
                    TaskDialog.Show("Below-product audit", "The bill is empty — nothing to audit.");
                    return Result.Succeeded;
                }

                var byLevel = new SortedDictionary<int, (int rows, double value)>();
                foreach (var li in lines)
                {
                    double amount = li.Quantity * li.RateUGX;
                    int k = (int)li.RateResolution;
                    byLevel.TryGetValue(k, out var cur);
                    byLevel[k] = (cur.rows + 1, cur.value + amount);
                }

                double totalValue = lines.Sum(l => l.Quantity * l.RateUGX);
                int totalRows = lines.Count;

                int productRows = byLevel.TryGetValue((int)RateResolutionLevel.Product, out var pr) ? pr.rows : 0;
                double productValue = byLevel.TryGetValue((int)RateResolutionLevel.Product, out var pv) ? pv.value : 0;
                double belowValue = totalValue - productValue;

                var sb = new StringBuilder();
                sb.AppendLine("How specifically is this bill priced?");
                sb.AppendLine(new string('=', 62));
                sb.AppendLine($"  {"level",-10} {"rows",7} {"% rows",8} {"value (UGX)",18} {"% value",9}");
                foreach (RateResolutionLevel lvl in new[]
                {
                    RateResolutionLevel.Product, RateResolutionLevel.System,
                    RateResolutionLevel.Material, RateResolutionLevel.Category,
                    RateResolutionLevel.None,
                })
                {
                    byLevel.TryGetValue((int)lvl, out var c);
                    double pctRows = totalRows > 0 ? 100.0 * c.rows / totalRows : 0;
                    double pctVal = totalValue > 0 ? 100.0 * c.value / totalValue : 0;
                    sb.AppendLine($"  {lvl,-10} {c.rows,7:N0} {pctRows,7:F1}% {c.value,18:N0} {pctVal,8:F1}%");
                }
                sb.AppendLine(new string('-', 62));
                sb.AppendLine($"  {"TOTAL",-10} {totalRows,7:N0} {"100.0%",8} {totalValue,18:N0} {"100.0%",9}");
                sb.AppendLine();
                sb.AppendLine($"  BELOW PRODUCT LEVEL: {(totalValue > 0 ? 100.0 * belowValue / totalValue : 0):F1}% "
                            + $"of value ({belowValue:N0} UGX) is priced by an average, not by the product.");
                sb.AppendLine();

                // The worst offenders by value, because that is where chasing a real
                // rate actually moves the tender. Ranking by row count would put a
                // thousand cheap fixings above one mispriced roof.
                var worst = lines
                    .Where(l => l.RateResolution != RateResolutionLevel.Product)
                    .GroupBy(l => $"{l.Discipline}/{l.Category}")
                    .Select(g => new
                    {
                        Key = g.Key,
                        Rows = g.Count(),
                        Value = g.Sum(l => l.Quantity * l.RateUGX),
                        Level = g.GroupBy(l => l.RateResolution).OrderByDescending(x => x.Count()).First().Key,
                        Why = g.Select(l => l.RateProvenance).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? "",
                    })
                    .OrderByDescending(x => x.Value)
                    .Take(15)
                    .ToList();

                if (worst.Count > 0)
                {
                    sb.AppendLine("  Largest below-product groups, by value:");
                    foreach (var w in worst)
                        sb.AppendLine($"    {w.Value,16:N0}  {w.Level,-9} {w.Rows,5:N0} row(s)  {w.Key}"
                                    + (string.IsNullOrWhiteSpace(w.Why) ? "" : $"  — {Short(w.Why)}"));
                }

                int unresolvedQty = lines.Count(l => !l.QuantityResolved);
                if (unresolvedQty > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"  Separately: {unresolvedQty:N0} row(s) carry an UNRESOLVED QUANTITY. Those");
                    sb.AppendLine("  read as 0 and price as cheap items; the rate level says nothing about them.");
                }

                StingLog.Info($"BOQ below-product audit: rows={totalRows}, product={productRows}, "
                            + $"belowValue={belowValue:F0} of {totalValue:F0} UGX");

                UI.StingResultPanel.Create("Below-product rate audit")
                    .SetSubtitle($"{(totalValue > 0 ? 100.0 * belowValue / totalValue : 0):F1}% of value priced by average")
                    .AddSection("RESOLUTION")
                    .Text(sb.ToString())
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BOQBelowProductAuditCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string Short(string s)
            => s != null && s.Length > 58 ? s.Substring(0, 58) + "…" : s;
    }
}
