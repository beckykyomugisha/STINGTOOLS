using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Panels
{
    // ════════════════════════════════════════════════════════════════════════════
    //  Panel Schedule Excel Round-Trip
    //
    //  Export reads every PanelScheduleView's Body section cells to an .xlsx
    //  workbook (one worksheet per panel). Import reads cells back from the
    //  workbook and writes via TableSectionData.SetCellText.
    //
    //  Read-only Revit-managed cells (loads computed from circuits, totals,
    //  utility-tracked fields) reject SetCellText silently or throw — those
    //  are caught and reported as "rejected" rather than failing the whole
    //  import. This matches the DiRoots PanelLink behaviour.
    // ════════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportPanelSchedulesToExcelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(PanelScheduleView))
                .Cast<PanelScheduleView>()
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (schedules.Count == 0)
            {
                TaskDialog.Show("STING Panel Schedule Export",
                    "No PanelScheduleView objects in the project.\n\n" +
                    "Run 'Batch Panel Schedules' first to generate schedules per panel.");
                return Result.Succeeded;
            }

            string defaultName = $"STING_PanelSchedules_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            string outPath = OutputLocationHelper.PromptForSaveLocation(
                doc, "Export Panel Schedules to Excel", defaultName,
                "Excel Workbook (*.xlsx)|*.xlsx");
            if (string.IsNullOrEmpty(outPath)) return Result.Cancelled;

            int sheetsWritten = 0, totalRows = 0, errors = 0;
            var failures = new List<string>();

            try
            {
                using var wb = new XLWorkbook();
                var index = wb.Worksheets.Add("INDEX");
                index.Cell(1, 1).Value = "STING Panel Schedule Export";
                index.Cell(1, 1).Style.Font.Bold = true;
                index.Cell(2, 1).Value = $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}";
                index.Cell(3, 1).Value = $"Project: {doc.Title}";
                index.Cell(5, 1).Value = "Sheet";
                index.Cell(5, 2).Value = "Panel name";
                index.Cell(5, 3).Value = "Template";
                index.Cell(5, 4).Value = "Body rows × cols";
                index.Range(5, 1, 5, 4).Style.Font.Bold = true;
                int indexRow = 6;

                foreach (var psv in schedules)
                {
                    string sheetName = SafeWorksheetName(psv.Name, sheetsWritten + 1);
                    string panelName = "(unknown)";
                    string templateName = "(unknown)";
                    try
                    {
                        var pid = psv.GetPanel();
                        if (pid != null && pid != ElementId.InvalidElementId)
                        {
                            var panel = doc.GetElement(pid);
                            if (panel != null) panelName = panel.Name;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"GetPanel for {psv.Name}: {ex.Message}"); }
                    try
                    {
                        var tid = psv.GetTemplate();
                        if (tid != null && tid != ElementId.InvalidElementId)
                        {
                            var t = doc.GetElement(tid) as PanelScheduleTemplate;
                            if (t != null) templateName = t.Name;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"GetTemplate for {psv.Name}: {ex.Message}"); }

                    int rows = 0, cols = 0;
                    try
                    {
                        var ws = wb.Worksheets.Add(sheetName);
                        ws.Cell(1, 1).Value = "Panel";
                        ws.Cell(1, 2).Value = panelName;
                        ws.Cell(2, 1).Value = "Schedule";
                        ws.Cell(2, 2).Value = psv.Name;
                        ws.Cell(3, 1).Value = "Template";
                        ws.Cell(3, 2).Value = templateName;
                        ws.Cell(4, 1).Value = "PanelScheduleView ElementId";
                        ws.Cell(4, 2).Value = psv.Id.Value.ToString();
                        ws.Range(1, 1, 4, 1).Style.Font.Bold = true;

                        WriteSection(ws, psv, SectionType.Header, "HEADER", 6, ref rows, ref cols);
                        int afterHeader = 6 + Math.Max(rows, 0) + 3;
                        WriteSection(ws, psv, SectionType.Body, "BODY (editable circuit rows)", afterHeader, ref rows, ref cols);
                        int afterBody = afterHeader + Math.Max(rows, 0) + 3;
                        WriteSection(ws, psv, SectionType.Summary, "SUMMARY", afterBody, ref rows, ref cols);

                        ws.Columns().AdjustToContents(1, 60);
                        sheetsWritten++;
                        totalRows += rows;

                        index.Cell(indexRow, 1).Value = sheetName;
                        index.Cell(indexRow, 2).Value = panelName;
                        index.Cell(indexRow, 3).Value = templateName;
                        index.Cell(indexRow, 4).Value = $"{rows}×{cols}";
                        indexRow++;
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        failures.Add($"{psv.Name}: {ex.Message}");
                        StingLog.Warn($"Export {psv.Name}: {ex.Message}");
                    }
                }

                index.Columns().AdjustToContents();
                wb.SaveAs(outPath);
            }
            catch (Exception ex)
            {
                StingLog.Error("ExportPanelSchedulesToExcel", ex);
                message = ex.Message;
                return Result.Failed;
            }

            var panel = StingResultPanel.Create("Export Panel Schedules → Excel");
            panel.SetSubtitle($"{sheetsWritten} schedules · {totalRows} rows · {errors} error(s)");
            panel.AddSection("OUTPUT").Text(outPath);
            panel.AddSection("NEXT STEPS")
                 .Text("Edit the BODY section (circuit description, breaker rating, notes).")
                 .Text("HEADER and SUMMARY are read-only on import — they are exported for reference only.")
                 .Text("Re-import with 'Import Panel Schedules from Excel'. Cells that Revit considers read-only (computed loads, totals) will be reported as 'rejected'.");
            if (failures.Count > 0)
            {
                panel.AddSection("FAILURES");
                foreach (string f in failures.Take(20)) panel.Text(f);
            }
            panel.Show();

            StingLog.Info($"PanelSchedule Excel export: {sheetsWritten} sheets, {totalRows} rows, {errors} errors → {outPath}");
            return Result.Succeeded;
        }

        private static void WriteSection(IXLWorksheet ws, PanelScheduleView psv, SectionType section,
            string label, int startRow, ref int outRows, ref int outCols)
        {
            outRows = 0; outCols = 0;
            TableSectionData data;
            try
            {
                var td = psv.GetTableData();
                if (td == null) return;
                data = td.GetSectionData(section);
            }
            catch (Exception ex) { StingLog.Warn($"GetSectionData {section}: {ex.Message}"); return; }
            if (data == null) return;

            int nRows = data.NumberOfRows;
            int nCols = data.NumberOfColumns;
            ws.Cell(startRow, 1).Value = label;
            ws.Cell(startRow, 1).Style.Font.Bold = true;
            ws.Range(startRow, 1, startRow, Math.Max(1, nCols)).Style.Fill.BackgroundColor = XLColor.LightGray;

            for (int r = 0; r < nRows; r++)
            {
                for (int c = 0; c < nCols; c++)
                {
                    string txt = "";
                    try { txt = psv.GetCellText(section, r, c) ?? ""; }
                    catch (Exception ex) { StingLog.Warn($"GetCellText {section}[{r},{c}]: {ex.Message}"); }
                    ws.Cell(startRow + 1 + r, c + 1).Value = txt;
                }
            }
            outRows = nRows; outCols = nCols;
        }

        private static string SafeWorksheetName(string raw, int fallbackOrdinal)
        {
            if (string.IsNullOrEmpty(raw)) return $"Panel_{fallbackOrdinal:D3}";
            var bad = new[] { '\\', '/', '?', '*', ':', '[', ']' };
            foreach (char c in bad) raw = raw.Replace(c, '_');
            if (raw.Length > 31) raw = raw.Substring(0, 31);
            return raw;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ImportPanelSchedulesFromExcelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Panel Schedules from Excel",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                InitialDirectory = OutputLocationHelper.GetOutputDirectory(doc)
            };
            if (dlg.ShowDialog() != true) return Result.Cancelled;
            string inPath = dlg.FileName;

            var byId = new FilteredElementCollector(doc)
                .OfClass(typeof(PanelScheduleView))
                .Cast<PanelScheduleView>()
                .ToDictionary(p => p.Id.Value, p => p);
            var byName = new Dictionary<string, PanelScheduleView>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in byId.Values)
                if (!byName.ContainsKey(p.Name)) byName[p.Name] = p;

            int sheetsProcessed = 0, cellsWritten = 0, cellsRejected = 0, cellsSkipped = 0, schedulesNotFound = 0;
            var failures = new List<string>();

            using (var tx = new Transaction(doc, "STING Import Panel Schedules"))
            {
                tx.Start();
                XLWorkbook wb;
                try { wb = new XLWorkbook(inPath); }
                catch (Exception ex)
                {
                    StingLog.Error("ImportPanelSchedules: open workbook", ex);
                    message = ex.Message;
                    return Result.Failed;
                }

                using (wb)
                {
                    foreach (var ws in wb.Worksheets)
                    {
                        if (string.Equals(ws.Name, "INDEX", StringComparison.OrdinalIgnoreCase)) continue;

                        long elemId = 0;
                        try
                        {
                            string idStr = (ws.Cell(4, 2).GetString() ?? "").Trim();
                            long.TryParse(idStr, out elemId);
                        }
                        catch (Exception ex) { StingLog.Warn($"Sheet '{ws.Name}' missing ElementId in B4: {ex.Message}"); }

                        string scheduleName = "";
                        try { scheduleName = ws.Cell(2, 2).GetString(); } catch (Exception ex) { StingLog.Warn($"Sheet '{ws.Name}' missing schedule name: {ex.Message}"); }

                        PanelScheduleView psv = null;
                        if (elemId > 0 && byId.TryGetValue(elemId, out var p1)) psv = p1;
                        else if (!string.IsNullOrEmpty(scheduleName) && byName.TryGetValue(scheduleName, out var p2)) psv = p2;
                        else if (byName.TryGetValue(ws.Name, out var p3)) psv = p3;

                        if (psv == null)
                        {
                            schedulesNotFound++;
                            failures.Add($"Worksheet '{ws.Name}' → no matching PanelScheduleView");
                            continue;
                        }

                        int bodyHeaderRow = FindLabelRow(ws, "BODY");
                        if (bodyHeaderRow <= 0)
                        {
                            failures.Add($"Worksheet '{ws.Name}' → BODY label row not found, skipped");
                            continue;
                        }

                        TableSectionData body;
                        try { body = psv.GetTableData()?.GetSectionData(SectionType.Body); }
                        catch (Exception ex) { failures.Add($"{psv.Name}: GetSectionData failed: {ex.Message}"); continue; }
                        if (body == null) { failures.Add($"{psv.Name}: Body section unavailable"); continue; }

                        int nRows = body.NumberOfRows;
                        int nCols = body.NumberOfColumns;
                        int written = 0, rejected = 0, skipped = 0;

                        for (int r = 0; r < nRows; r++)
                        {
                            int xlRow = bodyHeaderRow + 1 + r;
                            for (int c = 0; c < nCols; c++)
                            {
                                string newVal;
                                try { newVal = ws.Cell(xlRow, c + 1).GetString() ?? ""; }
                                catch (Exception ex) { StingLog.Warn($"{psv.Name}[{r},{c}] read xlsx: {ex.Message}"); continue; }

                                string oldVal = "";
                                try { oldVal = psv.GetCellText(SectionType.Body, r, c) ?? ""; }
                                catch (Exception ex) { StingLog.Warn($"{psv.Name}[{r},{c}] read revit: {ex.Message}"); }

                                if (string.Equals(newVal, oldVal, StringComparison.Ordinal))
                                {
                                    skipped++;
                                    continue;
                                }

                                try
                                {
                                    body.SetCellText(r, c, newVal);
                                    written++;
                                }
                                catch (Exception ex)
                                {
                                    rejected++;
                                    StingLog.Warn($"{psv.Name}[{r},{c}] SetCellText '{newVal}': {ex.Message}");
                                }
                            }
                        }

                        sheetsProcessed++;
                        cellsWritten += written;
                        cellsRejected += rejected;
                        cellsSkipped += skipped;
                    }
                }
                tx.Commit();
            }

            var panel = StingResultPanel.Create("Import Panel Schedules ← Excel");
            panel.SetSubtitle($"{sheetsProcessed} schedules · {cellsWritten} cells written · {cellsRejected} rejected");
            panel.AddSection("SUMMARY")
                 .Metric("Worksheets processed", sheetsProcessed.ToString())
                 .MetricHighlight("Cells written", cellsWritten.ToString())
                 .MetricWarn("Cells rejected (read-only)", cellsRejected.ToString())
                 .Metric("Cells unchanged", cellsSkipped.ToString())
                 .MetricError("Schedules not found", schedulesNotFound.ToString());
            if (failures.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (string f in failures.Take(25)) panel.Text(f);
                if (failures.Count > 25) panel.Text($"… {failures.Count - 25} more (see STING log).");
            }
            panel.AddSection("NOTES")
                 .Text("Rejected cells are typically Revit-managed: computed loads, totals, breaker ratings driven by circuit data, or cells outside the editable schema for the active template.")
                 .Text("Only the BODY section is imported. HEADER and SUMMARY edits in Excel are ignored.");
            panel.Show();

            StingLog.Info($"PanelSchedule Excel import: sheets={sheetsProcessed} written={cellsWritten} rejected={cellsRejected} skipped={cellsSkipped}");
            return Result.Succeeded;
        }

        private static int FindLabelRow(IXLWorksheet ws, string label)
        {
            int last = ws.LastRowUsed()?.RowNumber() ?? 0;
            for (int r = 1; r <= last; r++)
            {
                string v = "";
                try { v = ws.Cell(r, 1).GetString(); } catch (Exception ex) { StingLog.Warn($"FindLabelRow row {r}: {ex.Message}"); }
                if (!string.IsNullOrEmpty(v) && v.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                    return r;
            }
            return -1;
        }
    }
}
