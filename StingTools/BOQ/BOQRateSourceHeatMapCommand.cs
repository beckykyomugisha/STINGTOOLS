// Phase 108m — BOQ rate-source heat map. Visualises per-category rate
// provenance (Override / CSV / COBie / Default / Missing) as a table
// + coloured HTML side-car. QS sees at a glance which categories need
// a rate review.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.BOQ
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BOQRateSourceHeatMapCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var doc = ctx.Doc;

                var boq = BOQCostManager.BuildBOQDocument(doc);
                if (boq == null || boq.AllItems.Count == 0)
                {
                    TaskDialog.Show("Rate-source heat map", "No BOQ items to analyse.");
                    return Result.Cancelled;
                }

                var byCat = boq.AllItems
                    .Where(i => i.Source == BOQRowSource.Model)
                    .GroupBy(i => i.Category ?? "(uncategorised)")
                    .OrderByDescending(g => g.Sum(i => i.TotalUGX))
                    .ToList();

                var rp = StingResultPanel.Create("BOQ Rate-Source Heat Map")
                    .SetSubtitle("Per-category provenance of the BOQ rates — Override > CSV > COBie > Default > None")
                    .AddSection("SUMMARY");
                int cats = byCat.Count;
                int overrideCats = byCat.Count(g => g.Any(i => i.RateSource == "Override"));
                int csvCats      = byCat.Count(g => g.Any(i => i.RateSource == "CSV"));
                int cobieCats    = byCat.Count(g => g.Any(i => i.RateSource == "COBie"));
                int defaultCats  = byCat.Count(g => g.Any(i => i.RateSource == "Default"));
                int noneCats     = byCat.Count(g => g.All(i => i.RateUGX <= 0));
                rp.Metric("Categories in BOQ",       cats.ToString());
                rp.Metric("Override-priced",         overrideCats.ToString());
                rp.Metric("CSV-priced (catalogue)",  csvCats.ToString());
                rp.Metric("COBie-priced",            cobieCats.ToString());
                rp.Metric("Default-priced",          defaultCats.ToString());
                rp.Metric("Zero-rate (needs review)", noneCats.ToString());

                // Build HTML heat-map
                var html = new StringBuilder();
                html.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'><style>");
                html.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:16px;background:#f5f6fa}");
                html.AppendLine("h1{color:#1a3a5c}h2{color:#1a3a5c;font-size:14px;margin-top:24px}");
                html.AppendLine("table{border-collapse:collapse;width:100%;margin-top:12px;font-size:12px}");
                html.AppendLine("th{background:#1a3a5c;color:#fff;padding:6px 8px;text-align:left}");
                html.AppendLine("td{padding:6px 8px;border-bottom:1px solid #d1d5db}");
                html.AppendLine(".override{background:#c8f0dc;color:#147040;font-weight:600}");
                html.AppendLine(".csv{background:#cfe2f3;color:#104b6c}");
                html.AppendLine(".cobie{background:#fce5cd;color:#7a4418}");
                html.AppendLine(".default{background:#eeeeee;color:#555}");
                html.AppendLine(".none{background:#f4cccc;color:#7a1818;font-weight:600}");
                html.AppendLine("</style></head><body>");
                html.AppendLine($"<h1>BOQ Rate-Source Heat Map</h1>");
                html.AppendLine($"<div style='color:#666;font-size:11px'>Project: {boq.ProjectName} · Generated {DateTime.Now:yyyy-MM-dd HH:mm}</div>");
                html.AppendLine("<h2>Categories by rate-source dominance</h2>");
                html.AppendLine("<table><tr><th>Category</th><th>Items</th><th>Dominant source</th><th>Total UGX</th><th>Avg confidence</th></tr>");
                var csv = new StringBuilder();
                csv.AppendLine("Category,Items,DominantSource,TotalUGX,AvgConfidence,Override,CSV,COBie,Default,None");
                foreach (var g in byCat)
                {
                    var sources = g.GroupBy(i => string.IsNullOrEmpty(i.RateSource) ? (i.RateUGX > 0 ? "Default" : "None") : i.RateSource)
                                   .ToDictionary(x => x.Key, x => x.Count());
                    string dom = sources.OrderByDescending(kv => kv.Value).First().Key;
                    string cls = dom.ToLowerInvariant();
                    double total = g.Sum(i => i.TotalUGX);
                    double avgConf = g.Average(i => i.RateConfidence);
                    html.AppendLine($"<tr><td>{WebEscape(g.Key)}</td><td>{g.Count()}</td><td class='{cls}'>{dom}</td><td>UGX {total:N0}</td><td>{avgConf:F0}</td></tr>");
                    csv.AppendLine($"\"{g.Key}\",{g.Count()},{dom},{total:F0},{avgConf:F0}," +
                                   $"{sources.GetValueOrDefault("Override", 0)},{sources.GetValueOrDefault("CSV", 0)}," +
                                   $"{sources.GetValueOrDefault("COBie", 0)},{sources.GetValueOrDefault("Default", 0)}," +
                                   $"{sources.GetValueOrDefault("None", 0)}");
                }
                html.AppendLine("</table></body></html>");

                // Folder consolidation: nest BOQ heatmap exports inside the
                // unified project root's 16_COMPLIANCE_<code>/RateHeatMap/ folder.
                string compRoot = StingTools.Core.ProjectFolderEngine.GetFolderPath(doc, "COMPLIANCE");
                string outDir = string.IsNullOrEmpty(compRoot)
                    ? Path.Combine(Path.GetDirectoryName(doc.PathName ?? "") ?? "", "STING_BOQ_RateHeatMap")
                    : Path.Combine(compRoot, "RateHeatMap");
                Directory.CreateDirectory(outDir);
                string ts = DateTime.Now.ToString("yyyyMMdd_HHmm");
                string htmlPath = Path.Combine(outDir, $"rate_heatmap_{ts}.html");
                string csvPath  = Path.Combine(outDir, $"rate_heatmap_{ts}.csv");
                File.WriteAllText(htmlPath, html.ToString());
                File.WriteAllText(csvPath, csv.ToString());
                try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{htmlPath}\""); } catch (Exception ex) { StingLog.Warn($"Explorer: {ex.Message}"); }

                rp.AddSection("FILES")
                  .Text(htmlPath)
                  .Text(csvPath)
                  .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("BOQRateSourceHeatMapCommand", ex); message = ex.Message; return Result.Failed; }
        }

        private static string WebEscape(string s)
            => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
