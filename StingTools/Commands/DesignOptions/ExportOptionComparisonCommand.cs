// StingTools — Export Option Comparison.
//
// Phase 175 — runs OptionCostCarbonCalculator across every set/option +
// main model and produces:
//   1. CSV  — option_comparison_<timestamp>.csv under _BIM_COORD/options/
//   2. HTML — option_comparison_<timestamp>.html (stacked bar chart for
//             VE workshops; pure inline SVG, no external dependencies)
//   3. Sidecar update — every per-option CostDelta / CarbonDelta /
//             AreaDelta is recomputed from the row deltas vs each set's
//             primary so the dashboard, the deliverable template, and
//             the BIMCoordinationCenter strip refresh automatically.
//
// Folder structure under _BIM_COORD/options/<set>/<option>/ is created
// on demand by OptionFolderManager so per-option transmittals and
// briefcases land in the right place.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.DesignOptions;

namespace StingTools.Commands.DesignOptions
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportOptionComparisonCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            var doc = ctx.Doc;

            var sets = DesignOptionRegistry.Snapshot(doc);
            if (sets.Count == 0)
            {
                TaskDialog.Show("STING", "No design option sets in this document.");
                return Result.Cancelled;
            }

            // Ensure per-option folder hierarchy exists for downstream output
            OptionFolderManager.EnsureFoldersForAllOptions(doc);

            var rows = OptionCostCarbonCalculator.Build(doc);
            string csvPath = OptionCostCarbonCalculator.ExportCsv(doc, rows);

            // ── Update sidecar deltas vs each set's primary ──────────────
            DesignOptionRegistry.MutateSidecar(doc, sc =>
            {
                foreach (var s in sets)
                {
                    var primaryRow = rows.FirstOrDefault(r =>
                        r.SetName == s.Name && r.IsPrimary);
                    if (primaryRow == null) continue;
                    var meta = sc.Sets.FirstOrDefault(x =>
                        string.Equals(x.SetName, s.Name, StringComparison.OrdinalIgnoreCase));
                    if (meta == null)
                    {
                        meta = new DesignOptionSetMetadata { SetName = s.Name };
                        sc.Sets.Add(meta);
                    }
                    foreach (var o in s.Options)
                    {
                        var optRow = rows.FirstOrDefault(r =>
                            r.SetName == s.Name && r.OptionName == o.Name);
                        if (optRow == null) continue;
                        var optMeta = meta.Options.FirstOrDefault(x =>
                            string.Equals(x.OptionName, o.Name, StringComparison.OrdinalIgnoreCase));
                        if (optMeta == null)
                        {
                            optMeta = new DesignOptionMetadata { OptionName = o.Name };
                            meta.Options.Add(optMeta);
                        }
                        optMeta.CostDelta   = optRow.TotalCost   - primaryRow.TotalCost;
                        optMeta.CarbonDelta = optRow.TotalCarbonKg - primaryRow.TotalCarbonKg;
                        optMeta.AreaDelta   = optRow.TotalAreaM2  - primaryRow.TotalAreaM2;
                    }
                }
            });

            // ── HTML chart ───────────────────────────────────────────────
            string htmlPath = csvPath.Replace(".csv", ".html");
            File.WriteAllText(htmlPath, BuildHtml(rows));

            DesignOptionRegistry.InvalidateCache(doc);

            var sb = new StringBuilder();
            sb.AppendLine($"Sets analysed   : {sets.Count}");
            sb.AppendLine($"Options analysed: {rows.Count - 1}");
            sb.AppendLine();
            sb.AppendLine($"CSV  : {csvPath}");
            sb.AppendLine($"HTML : {htmlPath}");
            sb.AppendLine();
            foreach (var r in rows.OrderBy(x => x.SetName).ThenByDescending(x => x.IsPrimary))
            {
                sb.AppendLine($"  · {r.SetName,-22} {r.OptionName,-18} elems={r.ElementCount,5} cost={r.TotalCost,12:N0} CO₂={r.TotalCarbonKg,10:N0} kg");
            }
            TaskDialog.Show("STING — Option Comparison", sb.ToString());
            return Result.Succeeded;
        }

        private static string BuildHtml(List<OptionRollupRow> rows)
        {
            double maxCost = rows.Count == 0 ? 1 : Math.Max(1, rows.Max(r => r.TotalCost));
            double maxCarb = rows.Count == 0 ? 1 : Math.Max(1, rows.Max(r => r.TotalCarbonKg));
            int width = 900;
            int barH = 22;
            int rowH = 56;
            int top = 60;
            int height = top + rows.Count * rowH + 40;

            var sb = new StringBuilder();
            sb.AppendLine("<!doctype html><html><head><meta charset='utf-8'>");
            sb.AppendLine("<title>STING — Option Comparison</title>");
            sb.AppendLine("<style>body{font:13px/1.4 -apple-system,Segoe UI,sans-serif;margin:24px;color:#222}h1{font-size:18px}.lbl{font-size:11px;fill:#444}.bar-cost{fill:#0066CC}.bar-carb{fill:#F08000}.row-pri{fill:#0a8043;font-weight:bold}</style>");
            sb.AppendLine("</head><body>");
            sb.AppendLine("<h1>STING — Option Comparison</h1>");
            sb.AppendLine($"<p>Generated {DateTime.Now:yyyy-MM-dd HH:mm}. Capex (blue) and embodied carbon (orange) per option, normalised to the largest value.</p>");
            sb.AppendLine($"<svg width='{width}' height='{height}' xmlns='http://www.w3.org/2000/svg'>");
            sb.AppendLine($"<text x='10' y='30' style='font-weight:bold'>Set / Option</text>");
            sb.AppendLine($"<text x='280' y='30' style='font-weight:bold'>Capex (blue) and Carbon (orange)</text>");
            int y = top;
            foreach (var r in rows.OrderBy(x => x.SetName).ThenByDescending(x => x.IsPrimary))
            {
                string cls = r.IsPrimary ? "row-pri" : "lbl";
                string label = $"{r.SetName} · {r.OptionName}{(r.IsPrimary ? " ★" : "")}";
                sb.AppendLine($"<text x='10' y='{y + 14}' class='{cls}'>{System.Net.WebUtility.HtmlEncode(label)}</text>");
                int wCost = (int)Math.Round((width - 300) * (r.TotalCost   / maxCost));
                int wCarb = (int)Math.Round((width - 300) * (r.TotalCarbonKg / maxCarb));
                sb.AppendLine($"<rect x='280' y='{y}' width='{wCost}' height='{barH/2}' class='bar-cost'/>");
                sb.AppendLine($"<rect x='280' y='{y + barH/2 + 2}' width='{wCarb}' height='{barH/2}' class='bar-carb'/>");
                sb.AppendLine($"<text x='285' y='{y + 12}' class='lbl' style='fill:#fff'>{r.TotalCost:N0}</text>");
                sb.AppendLine($"<text x='285' y='{y + barH + 2}' class='lbl' style='fill:#fff'>{r.TotalCarbonKg:N0} kgCO₂e</text>");
                y += rowH;
            }
            sb.AppendLine("</svg></body></html>");
            return sb.ToString();
        }
    }
}
