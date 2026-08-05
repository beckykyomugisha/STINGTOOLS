// ClashXlsxExportCommand.cs — F7. Coordinator-friendly Excel export of the
// current clashes.json. Most coordinators ask for the BCC clash list as
// XLSX with filters for project meetings — BCF is for cross-platform
// round-trip, not for spreadsheet review. ClosedXML is already a project
// dependency.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ClashXlsxExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                string outDir = OutputLocationHelper.GetOutputDirectory(doc) ?? Path.GetTempPath();
                string clashesJson = Path.Combine(outDir, "clashes.json");
                var run = ClashPersistence.Load(clashesJson);
                if (run == null || run.Clashes == null || run.Clashes.Count == 0)
                {
                    TaskDialog.Show("STING Clash XLSX",
                        $"No clashes to export.\n\nExpected: {clashesJson}\n\nRun clash detection first.");
                    return Result.Cancelled;
                }

                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string xlsxPath = Path.Combine(outDir, $"clashes_{stamp}.xlsx");
                ExportToXlsx(run, xlsxPath, includeArchiveTrend: true, archiveDir: Path.Combine(outDir, "archive"));

                TaskDialog.Show("STING Clash XLSX",
                    $"Exported {run.Clashes.Count} clashes ({run.Groups?.Count ?? 0} groups) to:\n\n{xlsxPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ClashXlsxExportCommand.Execute", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// F7: Reusable export entry point. Sheets:
        ///   1. Summary    — run stats, severity bucket counts.
        ///   2. Clashes    — one row per ClashRecord, AutoFilter enabled.
        ///   3. Groups     — one row per ClashGroupRecord.
        ///   4. Trend      — F3 archive series (when archiveDir provided).
        /// </summary>
        public static void ExportToXlsx(ClashRunRecord run, string xlsxPath,
            bool includeArchiveTrend = false, string archiveDir = null)
        {
            if (run == null || string.IsNullOrEmpty(xlsxPath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(xlsxPath));

            using var wb = new XLWorkbook();

            // ── Summary ────────────────────────────────────────────────────
            var summary = wb.Worksheets.Add("Summary");
            int row = 1;
            summary.Cell(row++, 1).Value = "STING Clash Run Summary";
            summary.Cell(row++, 1).Value = "Run Id:";        summary.Cell(row - 1, 2).Value = run.RunId ?? "";
            summary.Cell(row++, 1).Value = "Previous Run:";  summary.Cell(row - 1, 2).Value = run.PreviousRunId ?? "";
            summary.Cell(row++, 1).Value = "Duration (ms):"; summary.Cell(row - 1, 2).Value = run.DurationMs;
            summary.Cell(row++, 1).Value = "Matrix:";        summary.Cell(row - 1, 2).Value = run.MatrixFile ?? "";
            summary.Cell(row++, 1).Value = "Rules:";         summary.Cell(row - 1, 2).Value = run.RulesFile ?? "";
            row++;
            summary.Cell(row++, 1).Value = "Stats";
            summary.Cell(row, 1).Value = "Raw hits:";          summary.Cell(row++, 2).Value = run.Stats.Raw;
            summary.Cell(row, 1).Value = "Matrix/rule filtered:"; summary.Cell(row++, 2).Value = run.Stats.Tier1Filtered;
            summary.Cell(row, 1).Value = "Excluded:";          summary.Cell(row++, 2).Value = run.Stats.Excluded;
            summary.Cell(row, 1).Value = "Kept:";              summary.Cell(row++, 2).Value = run.Clashes?.Count ?? 0;
            summary.Cell(row, 1).Value = "Groups:";            summary.Cell(row++, 2).Value = run.Stats.Groups;
            summary.Cell(row, 1).Value = "New:";               summary.Cell(row++, 2).Value = run.Stats.New;
            summary.Cell(row, 1).Value = "Active:";            summary.Cell(row++, 2).Value = run.Stats.Active;
            summary.Cell(row, 1).Value = "Resolved:";          summary.Cell(row++, 2).Value = run.Stats.Resolved;
            summary.Cell(row, 1).Value = "Reintroduced:";      summary.Cell(row++, 2).Value = run.Stats.Reintroduced;
            row++;
            summary.Cell(row++, 1).Value = "Severity bucket";
            int crit = 0, hi = 0, med = 0, lo = 0;
            foreach (var c in run.Clashes ?? new System.Collections.Generic.List<ClashRecord>())
            {
                switch ((c.Severity ?? "").ToUpperInvariant())
                {
                    case "CRITICAL": crit++; break;
                    case "HIGH": hi++; break;
                    case "MED": case "MEDIUM": med++; break;
                    case "LOW": lo++; break;
                }
            }
            summary.Cell(row, 1).Value = "CRITICAL:"; summary.Cell(row++, 2).Value = crit;
            summary.Cell(row, 1).Value = "HIGH:";     summary.Cell(row++, 2).Value = hi;
            summary.Cell(row, 1).Value = "MED:";      summary.Cell(row++, 2).Value = med;
            summary.Cell(row, 1).Value = "LOW:";      summary.Cell(row++, 2).Value = lo;
            summary.Columns().AdjustToContents();

            // ── Clashes (autofilter, one row per clash) ────────────────────
            var sheet = wb.Worksheets.Add("Clashes");
            string[] headers = {
                "Clash Id", "State", "Severity", "Tolerance", "Kind",
                "Matrix Pair", "Group Id",
                "Triage Score", "Recurrence",
                "Element A Id", "Element A Cat", "Element A System",
                "Element B Id", "Element B Cat", "Element B System",
                "First Seen UTC", "Last Seen UTC",
                "Volume mm³",
                "Resolution Hint", "Linked Issue Guid",
            };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = sheet.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
            }
            int r = 2;
            foreach (var c in (run.Clashes ?? new System.Collections.Generic.List<ClashRecord>())
                              .OrderByDescending(x => x.TriageScore))
            {
                int col = 1;
                sheet.Cell(r, col++).Value = c.Id ?? "";
                sheet.Cell(r, col++).Value = c.State ?? "";
                sheet.Cell(r, col++).Value = c.Severity ?? "";
                sheet.Cell(r, col++).Value = c.Tolerance ?? "";
                sheet.Cell(r, col++).Value = c.Kind ?? "";
                sheet.Cell(r, col++).Value = c.MatrixPairId ?? "";
                sheet.Cell(r, col++).Value = c.GroupId ?? "";
                sheet.Cell(r, col++).Value = c.TriageScore;
                sheet.Cell(r, col++).Value = c.RecurrenceCount;
                sheet.Cell(r, col++).Value = c.ElementA?.ElementId ?? 0;
                sheet.Cell(r, col++).Value = c.ElementA?.Category ?? "";
                sheet.Cell(r, col++).Value = c.ElementA?.System ?? "";
                sheet.Cell(r, col++).Value = c.ElementB?.ElementId ?? 0;
                sheet.Cell(r, col++).Value = c.ElementB?.Category ?? "";
                sheet.Cell(r, col++).Value = c.ElementB?.System ?? "";
                sheet.Cell(r, col++).Value = c.FirstSeenUtc;
                sheet.Cell(r, col++).Value = c.LastSeenUtc;
                sheet.Cell(r, col++).Value = c.VolumeMm3;
                sheet.Cell(r, col++).Value = c.ResolutionHint ?? "";
                sheet.Cell(r, col++).Value = c.IssueGuid ?? c.LinkedIssueGuid ?? "";
                // Severity colour cue.
                var sevCell = sheet.Cell(r, 3);
                sevCell.Style.Fill.BackgroundColor = SeverityColour(c.Severity);
                r++;
            }
            sheet.RangeUsed().SetAutoFilter();
            sheet.Columns().AdjustToContents();

            // ── Groups ─────────────────────────────────────────────────────
            var groups = wb.Worksheets.Add("Groups");
            string[] gh = { "Group Id", "Kind", "Anchor", "Size", "Status", "Assignee", "Due Date UTC" };
            for (int i = 0; i < gh.Length; i++)
            {
                var cell = groups.Cell(1, i + 1);
                cell.Value = gh[i];
                cell.Style.Font.Bold = true;
            }
            int gr = 2;
            foreach (var g in run.Groups ?? new System.Collections.Generic.List<ClashGroupRecord>())
            {
                groups.Cell(gr, 1).Value = g.Id ?? "";
                groups.Cell(gr, 2).Value = g.Kind ?? "";
                groups.Cell(gr, 3).Value = g.Anchor ?? "";
                groups.Cell(gr, 4).Value = g.Size;
                groups.Cell(gr, 5).Value = g.Status ?? "";
                groups.Cell(gr, 6).Value = g.Assignee ?? "";
                if (g.DueDateUtc.HasValue) groups.Cell(gr, 7).Value = g.DueDateUtc.Value;
                gr++;
            }
            groups.RangeUsed()?.SetAutoFilter();
            groups.Columns().AdjustToContents();

            // ── Trend (F3 archive) ─────────────────────────────────────────
            if (includeArchiveTrend && !string.IsNullOrEmpty(archiveDir))
            {
                try
                {
                    var trend = wb.Worksheets.Add("Trend");
                    string[] th = { "Run Id", "Duration ms", "Total", "New", "Active", "Resolved", "Reintroduced", "Critical", "High" };
                    for (int i = 0; i < th.Length; i++)
                    {
                        var cell = trend.Cell(1, i + 1);
                        cell.Value = th[i];
                        cell.Style.Font.Bold = true;
                    }
                    var archived = ClashPersistence.LoadArchive(archiveDir, ClashPersistence.ArchiveCap);
                    int tr = 2;
                    foreach (var ar in archived)
                    {
                        if (ar?.Stats == null) continue;
                        int aCrit = 0, aHi = 0;
                        foreach (var c in ar.Clashes ?? new System.Collections.Generic.List<ClashRecord>())
                        {
                            if (c.Severity == "CRITICAL") aCrit++;
                            else if (c.Severity == "HIGH") aHi++;
                        }
                        trend.Cell(tr, 1).Value = ar.RunId ?? "";
                        trend.Cell(tr, 2).Value = ar.DurationMs;
                        trend.Cell(tr, 3).Value = ar.Clashes?.Count ?? 0;
                        trend.Cell(tr, 4).Value = ar.Stats.New;
                        trend.Cell(tr, 5).Value = ar.Stats.Active;
                        trend.Cell(tr, 6).Value = ar.Stats.Resolved;
                        trend.Cell(tr, 7).Value = ar.Stats.Reintroduced;
                        trend.Cell(tr, 8).Value = aCrit;
                        trend.Cell(tr, 9).Value = aHi;
                        tr++;
                    }
                    trend.RangeUsed()?.SetAutoFilter();
                    trend.Columns().AdjustToContents();
                }
                catch (Exception ex) { StingLog.Warn($"ClashXlsxExportCommand trend sheet: {ex.Message}"); }
            }

            wb.SaveAs(xlsxPath);
            StingLog.Info($"ClashXlsxExport: {run.Clashes?.Count ?? 0} clashes → {xlsxPath}");
        }

        private static XLColor SeverityColour(string sev)
        {
            switch ((sev ?? "").ToUpperInvariant())
            {
                case "CRITICAL": return XLColor.FromArgb(255, 99, 71);    // tomato
                case "HIGH":     return XLColor.FromArgb(255, 165, 0);    // orange
                case "MED":
                case "MEDIUM":   return XLColor.FromArgb(255, 215, 0);    // gold
                case "LOW":      return XLColor.FromArgb(173, 216, 230);  // light blue
                default:         return XLColor.NoColor;
            }
        }
    }
}
