using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using StingTools.Core;

namespace StingTools.Core
{
    // ══════════════════════════════════════════════════════════════════════
    //  StingExcelExporter — shared XLSX export helper
    //  Phase 76 Item 3
    //
    //  Corporate theme:
    //    Header row  : #1E3A5F (navy) / white bold text
    //    Alt rows    : #F5F8FF / white
    //    Freeze row  : yes (row 1)
    //    Auto widths : yes
    // ══════════════════════════════════════════════════════════════════════

    public static class StingExcelExporter
    {
        private static readonly XLColor HeaderBg  = XLColor.FromHtml("#1E3A5F");
        private static readonly XLColor AltRowBg  = XLColor.FromHtml("#F5F8FF");
        private static readonly XLColor AccentBg  = XLColor.FromHtml("#E8A020");

        /// <summary>
        /// Export a flat table to an XLSX file with corporate STING styling.
        /// </summary>
        /// <param name="path">Full output file path.</param>
        /// <param name="sheetName">Sheet name (max 31 chars).</param>
        /// <param name="headers">Column headers.</param>
        /// <param name="rows">Data rows — each inner list must match headers.Count.</param>
        /// <param name="colColours">Optional: column-index → HTML hex colour for header override.</param>
        /// <param name="openFolder">If true, opens the output folder in Explorer after save.</param>
        /// <returns>Saved file path.</returns>
        public static string ExportTable(
            string path,
            string sheetName,
            List<string> headers,
            List<List<string>> rows,
            Dictionary<int, string> colColours = null,
            bool openFolder = true)
        {
            if (string.IsNullOrEmpty(path))   throw new ArgumentNullException(nameof(path));
            if (headers == null || headers.Count == 0) throw new ArgumentNullException(nameof(headers));

            // Sanitise sheet name
            string safeName = (sheetName ?? "Sheet1").Length > 31
                ? sheetName.Substring(0, 31) : (sheetName ?? "Sheet1");

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add(safeName);

            // ── Header row ──
            for (int c = 0; c < headers.Count; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold  = true;
                cell.Style.Font.FontColor = XLColor.White;

                XLColor bgColor = (colColours != null && colColours.TryGetValue(c, out string hex))
                    ? XLColor.FromHtml(hex) : HeaderBg;
                cell.Style.Fill.BackgroundColor = bgColor;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
            }

            // ── Data rows ──
            for (int r = 0; r < (rows?.Count ?? 0); r++)
            {
                var row = rows[r];
                bool isAlt = (r % 2 == 1);
                for (int c = 0; c < headers.Count; c++)
                {
                    var cell = ws.Cell(r + 2, c + 1);
                    cell.Value = (row != null && c < row.Count) ? (row[c] ?? "") : "";
                    if (isAlt)
                        cell.Style.Fill.BackgroundColor = AltRowBg;
                }
            }

            // ── Auto column widths ──
            ws.Columns().AdjustToContents(8.0, 60.0);

            // ── Freeze header row ──
            ws.SheetView.FreezeRows(1);

            // ── Timestamp + file metadata ──
            wb.Properties.Title   = safeName;
            wb.Properties.Author  = "STING BIM Tools";
            wb.Properties.Created = DateTime.Now;

            wb.SaveAs(path);
            StingLog.Info($"StingExcelExporter: saved {rows?.Count ?? 0} rows → {path}");

            if (openFolder)
            {
                string folder = Path.GetDirectoryName(path);
                if (Directory.Exists(folder))
                {
                    try { Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true })?.Dispose(); }
                    catch (Exception ex) { StingLog.Warn($"Open folder: {ex.Message}"); }
                }
            }

            return path;
        }

        /// <summary>
        /// Export multiple named tables as separate sheets in one XLSX.
        /// </summary>
        public static string ExportMultiSheet(
            string path,
            List<(string SheetName, List<string> Headers, List<List<string>> Rows)> sheets,
            bool openFolder = true)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

            using var wb = new XLWorkbook();

            foreach (var (sheetName, headers, rows) in sheets)
            {
                string safeName = (sheetName ?? "Sheet").Length > 31
                    ? sheetName.Substring(0, 31) : (sheetName ?? "Sheet");
                var ws = wb.Worksheets.Add(safeName);

                for (int c = 0; c < headers.Count; c++)
                {
                    var cell = ws.Cell(1, c + 1);
                    cell.Value = headers[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Fill.BackgroundColor = HeaderBg;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                for (int r = 0; r < (rows?.Count ?? 0); r++)
                {
                    var row = rows[r];
                    for (int c = 0; c < headers.Count; c++)
                    {
                        ws.Cell(r + 2, c + 1).Value = (row != null && c < row.Count) ? (row[c] ?? "") : "";
                        if (r % 2 == 1)
                            ws.Cell(r + 2, c + 1).Style.Fill.BackgroundColor = AltRowBg;
                    }
                }

                ws.Columns().AdjustToContents(8.0, 60.0);
                ws.SheetView.FreezeRows(1);
            }

            wb.Properties.Author  = "STING BIM Tools";
            wb.Properties.Created = DateTime.Now;
            wb.SaveAs(path);

            if (openFolder)
            {
                string folder = Path.GetDirectoryName(path);
                if (Directory.Exists(folder))
                {
                    try { Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true })?.Dispose(); }
                    catch (Exception ex) { StingLog.Warn($"Open folder: {ex.Message}"); }
                }
            }

            return path;
        }

        // ── Convenience factory for permission matrix ──────────────────────

        /// <summary>Export the permission/role matrix to XLSX.</summary>
        public static string ExportPermissionMatrix(
            string outputDir,
            List<(string Code, string Name, string Discipline, string CDEAccess, string CanApprove, string CanIssue)> roles,
            List<(string Folder, string CDEState, string ReadRoles, string WriteRoles, string ApproveRoles)> folders)
        {
            string path = Path.Combine(outputDir, $"permissions_matrix_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");

            var sheets = new List<(string, List<string>, List<List<string>>)>
            {
                ("Roles", new List<string> { "Code", "Name", "Discipline", "CDE Access", "Can Approve", "Can Issue" },
                    roles.Select(r => new List<string> { r.Code, r.Name, r.Discipline, r.CDEAccess, r.CanApprove, r.CanIssue }).ToList()),

                ("Folder Permissions", new List<string> { "Folder", "CDE State", "Read Roles", "Write Roles", "Approve Roles" },
                    folders.Select(f => new List<string> { f.Folder, f.CDEState, f.ReadRoles, f.WriteRoles, f.ApproveRoles }).ToList())
            };

            return ExportMultiSheet(path, sheets);
        }
    }
}
