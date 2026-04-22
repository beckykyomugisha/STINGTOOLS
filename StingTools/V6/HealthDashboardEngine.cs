using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.BIMManager;
using StingTools.Core;

namespace StingTools.V6
{
    /// <summary>N-G4: structured health dashboard over ModelHealthEngine with HTML export + trend.</summary>
    public static class HealthDashboardEngine
    {
        public class DashboardCategory
        {
            public string Name { get; set; }
            public int Score { get; set; }
            public int MaxScore { get; set; }
            public string Detail { get; set; }
            public string Rating =>
                MaxScore == 0 ? "Amber" :
                Score >= MaxScore * 0.8 ? "Green" :
                Score >= MaxScore * 0.5 ? "Amber" : "Red";
        }

        public class Dashboard
        {
            public DateTime GeneratedAt { get; set; } = DateTime.Now;
            public int OverallScore { get; set; }
            public string OverallRating { get; set; }
            public List<DashboardCategory> Categories { get; set; } = new();
            public List<(DateTime When, int Score)> Trend { get; set; } = new();
        }

        public static Dashboard Build(Document doc)
        {
            var report = ModelHealthEngine.RunHealthCheck(doc);
            var d = new Dashboard
            {
                OverallScore = report.OverallScore,
                OverallRating = report.Rating,
            };

            // ModelHealthEngine.Details uses the shape
            //   "<Name>: <score>/<max> - <detail>"  per line.
            foreach (var raw in (report.Details ?? string.Empty)
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = raw.Split(new[] { " - " }, 2, StringSplitOptions.None);
                if (parts.Length < 2) continue;
                var head = parts[0];
                var col = head.IndexOf(':');
                if (col < 0) continue;
                var name = head.Substring(0, col).Trim();
                var slash = head.IndexOf('/', col);
                if (slash < 0) continue;
                if (!int.TryParse(head.Substring(col + 1, slash - col - 1).Trim(), out var sc)) continue;
                if (!int.TryParse(head.Substring(slash + 1).Trim(), out var mx)) continue;
                d.Categories.Add(new DashboardCategory
                {
                    Name = name, Score = sc, MaxScore = mx, Detail = parts[1].Trim()
                });
            }
            d.Trend = ReadTrendLog(doc);
            return d;
        }

        public static string ExportHtml(Document doc, Dashboard d)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'><title>STING Model Health</title><style>"
                + "body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222}h1{margin-bottom:4px}"
                + ".rag-Green{color:#2e7d32}.rag-Amber{color:#ef6c00}.rag-Red{color:#c62828}"
                + "table{border-collapse:collapse;width:100%;margin-top:12px}"
                + "th,td{border:1px solid #ddd;padding:6px 10px;text-align:left}th{background:#f5f5f5}"
                + ".bar{display:inline-block;height:10px;background:#2e7d32;vertical-align:middle;margin-left:6px}"
                + ".overall{font-size:28px;font-weight:600;margin-top:8px}</style></head><body>");
            sb.AppendLine($"<h1>STING Model Health</h1><div>Generated {d.GeneratedAt:yyyy-MM-dd HH:mm}</div>");
            sb.AppendLine($"<div class='overall rag-{d.OverallRating}'>{d.OverallScore}/100 ({HtmlEscape(d.OverallRating ?? "")})</div>");
            sb.AppendLine("<table><thead><tr><th>Category</th><th>Score</th><th>Rating</th><th>Detail</th></tr></thead><tbody>");
            foreach (var c in d.Categories)
            {
                int pct = c.MaxScore > 0 ? (c.Score * 100) / c.MaxScore : 0;
                sb.AppendLine(
                    $"<tr><td>{HtmlEscape(c.Name)}</td>" +
                    $"<td>{c.Score}/{c.MaxScore}<span class='bar' style='width:{pct}px'></span></td>" +
                    $"<td class='rag-{c.Rating}'>{c.Rating}</td>" +
                    $"<td>{HtmlEscape(c.Detail ?? "")}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
            if (d.Trend.Any())
            {
                sb.AppendLine("<h2>Trend (last 20)</h2><table><thead><tr><th>When</th><th>Score</th></tr></thead><tbody>");
                foreach (var (w, s) in d.Trend.OrderByDescending(t => t.When).Take(20))
                    sb.AppendLine($"<tr><td>{w:yyyy-MM-dd HH:mm}</td><td>{s}</td></tr>");
                sb.AppendLine("</tbody></table>");
            }
            sb.AppendLine("</body></html>");
            string path = OutputLocationHelper.GetTimestampedPath(doc, "STING_HealthDashboard", ".html");
            File.WriteAllText(path, sb.ToString());
            StingLog.Info($"HealthDashboard HTML exported to {path}");
            return path;
        }

        static List<(DateTime, int)> ReadTrendLog(Document doc)
        {
            var list = new List<(DateTime, int)>();
            try
            {
                string dir = OutputLocationHelper.GetOutputDirectory(doc);
                string logPath = Path.Combine(dir, "STING_ModelHealth_Log.csv");
                if (!File.Exists(logPath)) return list;
                foreach (var line in File.ReadLines(logPath).Skip(1))
                {
                    var parts = line.Split(',');
                    if (parts.Length < 2) continue;
                    if (!DateTime.TryParse(parts[0], out var when)) continue;
                    if (!int.TryParse(parts[1], out var score)) continue;
                    list.Add((when, score));
                }
            }
            catch (Exception ex) { StingLog.Warn($"HealthDashboard trend read failed: {ex.Message}"); }
            return list;
        }

        static string HtmlEscape(string s) => (s ?? string.Empty)
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class HealthDashboardExportHtmlCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) return Result.Failed;
                var d = HealthDashboardEngine.Build(ctx.Doc);
                var path = HealthDashboardEngine.ExportHtml(ctx.Doc, d);
                TaskDialog.Show("STING",
                    $"Health dashboard exported:\n{path}\n\nOverall: {d.OverallScore}/100 ({d.OverallRating})");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HealthDashboardExportHtmlCommand failed", ex);
                TaskDialog.Show("STING", $"Dashboard export failed: {ex.Message}");
                return Result.Failed;
            }
        }
    }
}
