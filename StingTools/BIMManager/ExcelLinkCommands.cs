using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;

namespace StingTools.BIMManager
{
    // ════════════════════════════════════════════════════════════════════════════
    //  STING Excel Link — Bidirectional Excel ↔ Model Data Exchange
    //
    //  Exports element tag/parameter data to Excel for external editing,
    //  then imports changes back with full audit trail and change preview.
    //
    //  Commands:
    //    ExportToExcelCommand      — Export taggable elements to .xlsx
    //    ImportFromExcelCommand    — Import edited .xlsx back into model
    //    ExcelRoundTripCommand     — One-click: export → edit → import
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Internal Helper: ExcelLinkEngine ──

    internal static class ExcelLinkEngine
    {
        // Column definitions in export order
        internal static readonly string[] ColumnHeaders = new[]
        {
            "ElementId", "Category", "Family", "Type", "Level", "Room",
            "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ",
            "TAG1", "STATUS", "Description", "Mark"
        };

        // Parameter names mapped to column headers (for tag/param columns)
        internal static readonly Dictionary<string, Func<string>> ParamColumnMap =
            new Dictionary<string, Func<string>>(StringComparer.Ordinal)
            {
                ["DISC"]        = () => ParamRegistry.DISC,
                ["LOC"]         = () => ParamRegistry.LOC,
                ["ZONE"]        = () => ParamRegistry.ZONE,
                ["LVL"]         = () => ParamRegistry.LVL,
                ["SYS"]         = () => ParamRegistry.SYS,
                ["FUNC"]        = () => ParamRegistry.FUNC,
                ["PROD"]        = () => ParamRegistry.PROD,
                ["SEQ"]         = () => ParamRegistry.SEQ,
                ["TAG1"]        = () => ParamRegistry.TAG1,
                ["STATUS"]      = () => ParamRegistry.STATUS,
                ["Description"] = () => ParamRegistry.DESC,
                ["Mark"]        = () => ParamRegistry.Ext("TYPE_MARK"),
            };

        // Columns that are read-only (derived from model, not editable)
        internal static readonly HashSet<string> ReadOnlyColumns = new HashSet<string>(StringComparer.Ordinal)
        {
            "ElementId", "Category", "Family", "Type", "Level", "Room"
        };

        /// <summary>
        /// Collect taggable elements — either from selection or from entire project.
        /// Returns (elements, scopeDescription).
        /// </summary>
        internal static (List<Element> elements, string scope) CollectElements(
            Document doc, UIDocument uidoc, bool selectionOnly)
        {
            var knownCatNames = new HashSet<string>(TagConfig.DiscMap.Keys, StringComparer.OrdinalIgnoreCase);

            if (selectionOnly)
            {
                var selIds = uidoc.Selection.GetElementIds();
                if (selIds == null || selIds.Count == 0)
                    return (new List<Element>(), "selection (empty)");

                var elements = selIds
                    .Select(id => doc.GetElement(id))
                    .Where(e => e != null && e.Category != null && knownCatNames.Contains(e.Category.Name))
                    .ToList();
                return (elements, $"selection ({elements.Count} of {selIds.Count})");
            }

            // All taggable elements in the project
            var allElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && knownCatNames.Contains(e.Category.Name))
                .ToList();
            return (allElements, $"project ({allElements.Count} elements)");
        }

        /// <summary>
        /// Read element data into a row dictionary keyed by column header.
        /// </summary>
        internal static Dictionary<string, string> ReadElementRow(Document doc, Element el)
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal);

            // Identity columns (read-only)
            row["ElementId"] = el.Id.Value.ToString();
            row["Category"] = ParameterHelpers.GetCategoryName(el);
            row["Family"] = ParameterHelpers.GetFamilyName(el);
            row["Type"] = ParameterHelpers.GetFamilySymbolName(el);
            row["Level"] = ParameterHelpers.GetLevelCode(doc, el);

            // Room — try spatial lookup
            string roomName = "";
            try
            {
                Room room = ParameterHelpers.GetRoomAtElement(doc, el);
                if (room != null)
                    roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
            }
            catch { /* element may not have spatial context */ }
            row["Room"] = roomName;

            // Parameter columns
            foreach (var kvp in ParamColumnMap)
            {
                string paramName = kvp.Value();
                row[kvp.Key] = string.IsNullOrEmpty(paramName)
                    ? ""
                    : ParameterHelpers.GetString(el, paramName);
            }

            return row;
        }

        /// <summary>
        /// Build the Excel workbook from element data.
        /// </summary>
        internal static XLWorkbook BuildWorkbook(Document doc, List<Element> elements)
        {
            var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("STING Data");

            // ── Write header row ──
            for (int c = 0; c < ColumnHeaders.Length; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.Value = ColumnHeaders[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0, 51, 102);
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            // ── Write data rows ──
            for (int r = 0; r < elements.Count; r++)
            {
                var rowData = ReadElementRow(doc, elements[r]);
                for (int c = 0; c < ColumnHeaders.Length; c++)
                {
                    string header = ColumnHeaders[c];
                    string value = rowData.TryGetValue(header, out string v) ? v : "";
                    ws.Cell(r + 2, c + 1).Value = value;
                }
            }

            // ── Format read-only columns (light grey background) ──
            int readOnlyColCount = ReadOnlyColumns.Count;
            for (int c = 0; c < ColumnHeaders.Length; c++)
            {
                if (ReadOnlyColumns.Contains(ColumnHeaders[c]))
                {
                    var colRange = ws.Range(2, c + 1, elements.Count + 1, c + 1);
                    colRange.Style.Fill.BackgroundColor = XLColor.FromArgb(230, 230, 230);
                    colRange.Style.Font.FontColor = XLColor.FromArgb(100, 100, 100);

                    // ElementId column gets explicit protection note
                    if (ColumnHeaders[c] == "ElementId")
                        colRange.Style.Protection.Locked = true;
                }
            }

            // ── Auto-fit columns ──
            ws.Columns().AdjustToContents(1, Math.Min(elements.Count + 1, 500));

            // Ensure minimum column widths for readability
            for (int c = 0; c < ColumnHeaders.Length; c++)
            {
                double currentWidth = ws.Column(c + 1).Width;
                if (currentWidth < 10)
                    ws.Column(c + 1).Width = 10;
                if (currentWidth > 50)
                    ws.Column(c + 1).Width = 50;
            }

            // ── Freeze header row ──
            ws.SheetView.FreezeRows(1);

            // ── Add metadata worksheet ──
            var metaWs = wb.AddWorksheet("_STING_Metadata");
            metaWs.Cell(1, 1).Value = "Key";
            metaWs.Cell(1, 2).Value = "Value";
            metaWs.Cell(2, 1).Value = "ExportDate";
            metaWs.Cell(2, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            metaWs.Cell(3, 1).Value = "ProjectName";
            metaWs.Cell(3, 2).Value = doc.Title ?? "";
            metaWs.Cell(4, 1).Value = "ElementCount";
            metaWs.Cell(4, 2).Value = elements.Count;
            metaWs.Cell(5, 1).Value = "Version";
            metaWs.Cell(5, 2).Value = "STING ExcelLink v1.0";
            metaWs.Columns().AdjustToContents();
            metaWs.Hide();

            return wb;
        }

        /// <summary>
        /// Read an Excel file and return rows keyed by ElementId.
        /// Each row is a dictionary of column header → cell value.
        /// </summary>
        internal static Dictionary<long, Dictionary<string, string>> ReadExcelFile(string path)
        {
            var result = new Dictionary<long, Dictionary<string, string>>();

            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet("STING Data");
            if (ws == null)
            {
                // Try the first worksheet as fallback
                ws = wb.Worksheets.FirstOrDefault();
                if (ws == null)
                    throw new InvalidOperationException("No worksheets found in the Excel file.");
            }

            // Read header row to build column index map
            var headerMap = new Dictionary<int, string>();
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int c = 1; c <= lastCol; c++)
            {
                string header = ws.Cell(1, c).GetString().Trim();
                if (!string.IsNullOrEmpty(header))
                    headerMap[c] = header;
            }

            // Verify ElementId column exists
            int? elementIdCol = headerMap.FirstOrDefault(kv => kv.Value == "ElementId").Key;
            if (elementIdCol == null || elementIdCol == 0)
                throw new InvalidOperationException("ElementId column not found in header row.");

            // Read data rows
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 2; r <= lastRow; r++)
            {
                string idStr = ws.Cell(r, elementIdCol.Value).GetString().Trim();
                if (string.IsNullOrEmpty(idStr)) continue;
                if (!long.TryParse(idStr, out long elementId)) continue;

                var rowData = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kvp in headerMap)
                {
                    rowData[kvp.Value] = ws.Cell(r, kvp.Key).GetString();
                }

                result[elementId] = rowData;
            }

            return result;
        }

        /// <summary>
        /// Compare Excel data against current model and return list of changes.
        /// </summary>
        internal static List<ChangeRecord> ComputeChanges(Document doc, Dictionary<long, Dictionary<string, string>> excelData)
        {
            var changes = new List<ChangeRecord>();

            foreach (var kvp in excelData)
            {
                long elementId = kvp.Key;
                var excelRow = kvp.Value;

                Element el = doc.GetElement(new ElementId(elementId));
                if (el == null)
                {
                    changes.Add(new ChangeRecord
                    {
                        ElementId = elementId,
                        Status = ChangeStatus.NotFound,
                        Column = "",
                        OldValue = "",
                        NewValue = "",
                    });
                    continue;
                }

                // Compare each editable parameter column
                foreach (var colKvp in ParamColumnMap)
                {
                    string columnName = colKvp.Key;
                    string paramName = colKvp.Value();
                    if (string.IsNullOrEmpty(paramName)) continue;

                    if (!excelRow.TryGetValue(columnName, out string excelValue))
                        continue;

                    excelValue = excelValue ?? "";
                    string modelValue = ParameterHelpers.GetString(el, paramName) ?? "";

                    if (!string.Equals(excelValue, modelValue, StringComparison.Ordinal))
                    {
                        changes.Add(new ChangeRecord
                        {
                            ElementId = elementId,
                            Status = ChangeStatus.Changed,
                            Column = columnName,
                            ParamName = paramName,
                            OldValue = modelValue,
                            NewValue = excelValue,
                        });
                    }
                }
            }

            return changes;
        }

        /// <summary>
        /// Apply changes to the model within a single transaction.
        /// Returns (applied, skipped, failed) counts.
        /// </summary>
        internal static (int applied, int skipped, int failed) ApplyChanges(
            Document doc, List<ChangeRecord> changes, Transaction trans)
        {
            int applied = 0, skipped = 0, failed = 0;

            var actualChanges = changes.Where(c => c.Status == ChangeStatus.Changed).ToList();
            foreach (var change in actualChanges)
            {
                Element el = doc.GetElement(new ElementId(change.ElementId));
                if (el == null)
                {
                    change.Status = ChangeStatus.NotFound;
                    failed++;
                    StingLog.Warn($"ExcelLink: Element {change.ElementId} not found during apply");
                    continue;
                }

                try
                {
                    bool success = ParameterHelpers.SetString(el, change.ParamName, change.NewValue, overwrite: true);
                    if (success)
                    {
                        change.Status = ChangeStatus.Applied;
                        applied++;
                        StingLog.Info($"ExcelLink: {change.ElementId}.{change.Column}: '{change.OldValue}' → '{change.NewValue}'");
                    }
                    else
                    {
                        change.Status = ChangeStatus.Failed;
                        failed++;
                        StingLog.Warn($"ExcelLink: Failed to write {change.Column} on element {change.ElementId}");
                    }
                }
                catch (Exception ex)
                {
                    change.Status = ChangeStatus.Failed;
                    failed++;
                    StingLog.Error($"ExcelLink: Error writing {change.Column} on element {change.ElementId}", ex);
                }
            }

            skipped = changes.Count(c => c.Status == ChangeStatus.NotFound);

            return (applied, skipped, failed);
        }

        /// <summary>
        /// Find the latest STING Excel export file in the output directory.
        /// </summary>
        internal static string FindLatestExport(Document doc)
        {
            string dir = OutputLocationHelper.GetOutputDirectory(doc);
            if (!Directory.Exists(dir)) return null;

            return Directory.GetFiles(dir, "STING_Excel_Export_*.xlsx")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .FirstOrDefault();
        }

        /// <summary>Build a summary of changes for preview display.</summary>
        internal static string BuildChangeSummary(List<ChangeRecord> changes, int totalExcelRows)
        {
            var actual = changes.Where(c => c.Status == ChangeStatus.Changed).ToList();
            var notFound = changes.Where(c => c.Status == ChangeStatus.NotFound).ToList();

            int elementsAffected = actual.Select(c => c.ElementId).Distinct().Count();
            int paramsChanged = actual.Count;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Excel rows read: {totalExcelRows}");
            sb.AppendLine($"Elements with changes: {elementsAffected}");
            sb.AppendLine($"Parameter values to update: {paramsChanged}");
            if (notFound.Count > 0)
                sb.AppendLine($"Elements not found in model: {notFound.Count}");
            sb.AppendLine();

            // Show per-column change counts
            var byColumn = actual.GroupBy(c => c.Column).OrderByDescending(g => g.Count());
            sb.AppendLine("Changes by column:");
            foreach (var group in byColumn)
            {
                sb.AppendLine($"  {group.Key}: {group.Count()} changes");
            }

            // Show first 10 changes as preview
            if (actual.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Preview (first 10 changes):");
                foreach (var change in actual.Take(10))
                {
                    string oldDisplay = string.IsNullOrEmpty(change.OldValue) ? "<empty>" : change.OldValue;
                    string newDisplay = string.IsNullOrEmpty(change.NewValue) ? "<empty>" : change.NewValue;
                    sb.AppendLine($"  [{change.ElementId}] {change.Column}: {oldDisplay} → {newDisplay}");
                }
                if (actual.Count > 10)
                    sb.AppendLine($"  ... and {actual.Count - 10} more");
            }

            return sb.ToString();
        }

        internal enum ChangeStatus { Changed, Applied, NotFound, Failed }

        internal class ChangeRecord
        {
            public long ElementId { get; set; }
            public ChangeStatus Status { get; set; }
            public string Column { get; set; }
            public string ParamName { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════════
    //  ExportToExcelCommand — Export element data to .xlsx
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Export taggable element data (tags, parameters, spatial info) to an Excel
    /// workbook for external editing. Supports exporting selected elements only
    /// or all taggable elements in the project.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportToExcelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No document open."; return Result.Failed; }

            Document doc = ctx.Doc;
            UIDocument uidoc = ctx.UIDoc;

            try
            {
                // ── Ask scope: selection or all ──
                bool selectionOnly = false;
                var selIds = uidoc.Selection.GetElementIds();
                if (selIds != null && selIds.Count > 0)
                {
                    var scopeDlg = new TaskDialog("STING Excel Export")
                    {
                        MainInstruction = "Export Scope",
                        MainContent = $"You have {selIds.Count} elements selected.\n\n" +
                                      "Export selected elements only, or all taggable elements in the project?",
                        CommonButtons = TaskDialogCommonButtons.Cancel,
                    };
                    scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Export selected elements only");
                    scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Export ALL taggable elements");
                    var scopeResult = scopeDlg.Show();

                    if (scopeResult == TaskDialogResult.Cancel)
                        return Result.Cancelled;
                    selectionOnly = (scopeResult == TaskDialogResult.CommandLink1);
                }

                // ── Collect elements ──
                var (elems, scope) = ExcelLinkEngine.CollectElements(doc, uidoc, selectionOnly);
                if (elems.Count == 0)
                {
                    TaskDialog.Show("STING Excel Export", "No taggable elements found in the selected scope.");
                    return Result.Succeeded;
                }

                StingLog.Info($"ExcelLink: Exporting {elems.Count} elements from {scope}");

                // ── Build workbook ──
                using var wb = ExcelLinkEngine.BuildWorkbook(doc, elems);

                // ── Save to file ──
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"STING_Excel_Export_{timestamp}.xlsx";
                string outputPath = OutputLocationHelper.GetOutputPath(doc, fileName);

                wb.SaveAs(outputPath);

                StingLog.Info($"ExcelLink: Exported to {outputPath}");

                // ── Report success ──
                var resultDlg = new TaskDialog("STING Excel Export")
                {
                    MainInstruction = "Export Complete",
                    MainContent = $"Exported {elems.Count} elements ({scope}) to:\n\n{outputPath}\n\n" +
                                  $"Columns: {ExcelLinkEngine.ColumnHeaders.Length}\n" +
                                  "Grey columns (ElementId, Category, Family, Type, Level, Room) are read-only.\n" +
                                  "Edit the white columns and use Import to update the model.",
                };
                resultDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Open file location");
                var dlgResult = resultDlg.Show();

                if (dlgResult == TaskDialogResult.CommandLink1)
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrEmpty(dir))
                            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"ExcelLink: Could not open directory: {ex.Message}");
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExcelLink Export failed", ex);
                message = ex.Message;
                TaskDialog.Show("STING Excel Export", $"Export failed:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  ImportFromExcelCommand — Import edited Excel back into model
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Import an edited STING Excel export back into the model. Matches rows by
    /// ElementId, compares current model values against Excel values, and updates
    /// only changed cells. Shows a preview summary before applying changes.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ImportFromExcelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No document open."; return Result.Failed; }

            Document doc = ctx.Doc;

            try
            {
                // ── Pick file or auto-detect ──
                string filePath = null;
                string latestExport = ExcelLinkEngine.FindLatestExport(doc);

                if (!string.IsNullOrEmpty(latestExport))
                {
                    var pickDlg = new TaskDialog("STING Excel Import")
                    {
                        MainInstruction = "Select Excel File",
                        MainContent = $"Latest export found:\n{Path.GetFileName(latestExport)}\n" +
                                      $"Modified: {File.GetLastWriteTime(latestExport):yyyy-MM-dd HH:mm}\n\n" +
                                      "Use this file or browse for a different one?",
                    };
                    pickDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Use latest export",
                        Path.GetFileName(latestExport));
                    pickDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Browse for file...");
                    pickDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                    var pickResult = pickDlg.Show();

                    if (pickResult == TaskDialogResult.Cancel)
                        return Result.Cancelled;
                    if (pickResult == TaskDialogResult.CommandLink1)
                        filePath = latestExport;
                }

                if (string.IsNullOrEmpty(filePath))
                {
                    var openDlg = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Select STING Excel Export to Import",
                        Filter = "Excel Files (*.xlsx)|*.xlsx",
                        InitialDirectory = OutputLocationHelper.GetOutputDirectory(doc),
                    };
                    if (openDlg.ShowDialog() != true)
                        return Result.Cancelled;
                    filePath = openDlg.FileName;
                }

                if (!File.Exists(filePath))
                {
                    TaskDialog.Show("STING Excel Import", $"File not found:\n{filePath}");
                    return Result.Failed;
                }

                StingLog.Info($"ExcelLink: Importing from {filePath}");

                // ── Read Excel data ──
                Dictionary<long, Dictionary<string, string>> excelData;
                try
                {
                    excelData = ExcelLinkEngine.ReadExcelFile(filePath);
                }
                catch (IOException ioEx)
                {
                    TaskDialog.Show("STING Excel Import",
                        $"Cannot read file — it may be open in Excel.\n\n" +
                        $"Close the file in Excel and try again.\n\n{ioEx.Message}");
                    return Result.Failed;
                }

                if (excelData.Count == 0)
                {
                    TaskDialog.Show("STING Excel Import", "No data rows found in the Excel file.");
                    return Result.Succeeded;
                }

                // ── Compute changes ──
                var changes = ExcelLinkEngine.ComputeChanges(doc, excelData);
                var actualChanges = changes.Where(c => c.Status == ExcelLinkEngine.ChangeStatus.Changed).ToList();

                if (actualChanges.Count == 0)
                {
                    int notFound = changes.Count(c => c.Status == ExcelLinkEngine.ChangeStatus.NotFound);
                    string msg = "No parameter changes detected — model matches Excel data.";
                    if (notFound > 0)
                        msg += $"\n\n{notFound} element(s) in Excel were not found in the model.";
                    TaskDialog.Show("STING Excel Import", msg);
                    return Result.Succeeded;
                }

                // ── Preview changes and confirm ──
                string summary = ExcelLinkEngine.BuildChangeSummary(changes, excelData.Count);
                var confirmDlg = new TaskDialog("STING Excel Import — Preview Changes")
                {
                    MainInstruction = "Apply Changes?",
                    MainContent = summary,
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Cancel,
                };
                confirmDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"Apply {actualChanges.Count} changes",
                    $"Update {actualChanges.Select(c => c.ElementId).Distinct().Count()} elements");
                var confirmResult = confirmDlg.Show();

                if (confirmResult != TaskDialogResult.CommandLink1)
                    return Result.Cancelled;

                // ── Apply changes in a single transaction ──
                int applied = 0, skipped = 0, failed = 0;
                using (var trans = new Transaction(doc, "STING Excel Import"))
                {
                    trans.Start();
                    try
                    {
                        (applied, skipped, failed) = ExcelLinkEngine.ApplyChanges(doc, changes, trans);
                        trans.Commit();
                    }
                    catch (Exception ex)
                    {
                        if (trans.HasStarted())
                            trans.RollBack();
                        throw new InvalidOperationException($"Transaction failed: {ex.Message}", ex);
                    }
                }

                // ── Report results ──
                StingLog.Info($"ExcelLink Import: applied={applied}, skipped={skipped}, failed={failed}");
                var resultMsg = $"Import Complete\n\n" +
                                $"Parameters updated: {applied}\n" +
                                $"Elements not found: {skipped}\n" +
                                $"Failures: {failed}\n\n" +
                                $"Source: {Path.GetFileName(filePath)}";
                TaskDialog.Show("STING Excel Import", resultMsg);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExcelLink Import failed", ex);
                message = ex.Message;
                TaskDialog.Show("STING Excel Import", $"Import failed:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  ExcelRoundTripCommand — One-click: export → edit → import
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One-click round-trip: exports element data to Excel, opens it in the default
    /// application for editing, then prompts the user to import changes when ready.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExcelRoundTripCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No document open."; return Result.Failed; }

            Document doc = ctx.Doc;
            UIDocument uidoc = ctx.UIDoc;

            try
            {
                // ── Ask scope: selection or all ──
                bool selectionOnly = false;
                var selIds = uidoc.Selection.GetElementIds();
                if (selIds != null && selIds.Count > 0)
                {
                    var scopeDlg = new TaskDialog("STING Excel Round-Trip")
                    {
                        MainInstruction = "Export Scope",
                        MainContent = $"You have {selIds.Count} elements selected.\n\n" +
                                      "Export selected elements only, or all taggable elements?",
                        CommonButtons = TaskDialogCommonButtons.Cancel,
                    };
                    scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Selected elements only");
                    scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "ALL taggable elements");
                    var scopeResult = scopeDlg.Show();

                    if (scopeResult == TaskDialogResult.Cancel)
                        return Result.Cancelled;
                    selectionOnly = (scopeResult == TaskDialogResult.CommandLink1);
                }

                // ── Collect and export ──
                var (elems, scope) = ExcelLinkEngine.CollectElements(doc, uidoc, selectionOnly);
                if (elems.Count == 0)
                {
                    TaskDialog.Show("STING Excel Round-Trip", "No taggable elements found.");
                    return Result.Succeeded;
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"STING_Excel_Export_{timestamp}.xlsx";
                string outputPath = OutputLocationHelper.GetOutputPath(doc, fileName);

                using (var wb = ExcelLinkEngine.BuildWorkbook(doc, elems))
                {
                    wb.SaveAs(outputPath);
                }

                StingLog.Info($"ExcelLink RoundTrip: Exported {elems.Count} elements to {outputPath}");

                // ── Open in default application ──
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = outputPath, UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ExcelLink: Could not open file: {ex.Message}");
                    TaskDialog.Show("STING Excel Round-Trip",
                        $"Exported to:\n{outputPath}\n\nCould not open automatically. Please open the file manually.");
                }

                // ── Wait for user to finish editing ──
                var waitDlg = new TaskDialog("STING Excel Round-Trip")
                {
                    MainInstruction = "Edit in Excel",
                    MainContent = $"The file has been opened:\n{Path.GetFileName(outputPath)}\n\n" +
                                  $"Elements exported: {elems.Count}\n\n" +
                                  "Edit the parameter values in the white columns.\n" +
                                  "When finished, SAVE and CLOSE the Excel file, then click 'Import Changes'.\n\n" +
                                  "Grey columns (ElementId, Category, Family, Type, Level, Room) are read-only " +
                                  "and will be ignored during import.",
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                };
                waitDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Import Changes", "Read the saved Excel file and apply changes to the model");
                var waitResult = waitDlg.Show();

                if (waitResult != TaskDialogResult.CommandLink1)
                {
                    StingLog.Info("ExcelLink RoundTrip: User cancelled import phase");
                    return Result.Cancelled;
                }

                // ── Import phase ──
                Dictionary<long, Dictionary<string, string>> excelData;
                try
                {
                    excelData = ExcelLinkEngine.ReadExcelFile(outputPath);
                }
                catch (IOException ioEx)
                {
                    TaskDialog.Show("STING Excel Round-Trip",
                        $"Cannot read the file — it may still be open in Excel.\n\n" +
                        $"Please close the file in Excel and use 'Import from Excel' to import manually.\n\n{ioEx.Message}");
                    return Result.Failed;
                }

                if (excelData.Count == 0)
                {
                    TaskDialog.Show("STING Excel Round-Trip", "No data rows found in the file.");
                    return Result.Succeeded;
                }

                var changes = ExcelLinkEngine.ComputeChanges(doc, excelData);
                var actualChanges = changes.Where(c => c.Status == ExcelLinkEngine.ChangeStatus.Changed).ToList();

                if (actualChanges.Count == 0)
                {
                    TaskDialog.Show("STING Excel Round-Trip",
                        "No changes detected — the model already matches the Excel data.");
                    return Result.Succeeded;
                }

                // ── Preview and confirm ──
                string summary = ExcelLinkEngine.BuildChangeSummary(changes, excelData.Count);
                var confirmDlg = new TaskDialog("STING Excel Round-Trip — Preview Changes")
                {
                    MainInstruction = "Apply Changes?",
                    MainContent = summary,
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Cancel,
                };
                confirmDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"Apply {actualChanges.Count} changes");
                var confirmResult = confirmDlg.Show();

                if (confirmResult != TaskDialogResult.CommandLink1)
                    return Result.Cancelled;

                // ── Apply ──
                int applied = 0, skipped = 0, failed = 0;
                using (var trans = new Transaction(doc, "STING Excel Round-Trip Import"))
                {
                    trans.Start();
                    try
                    {
                        (applied, skipped, failed) = ExcelLinkEngine.ApplyChanges(doc, changes, trans);
                        trans.Commit();
                    }
                    catch (Exception ex)
                    {
                        if (trans.HasStarted())
                            trans.RollBack();
                        throw new InvalidOperationException($"Transaction failed: {ex.Message}", ex);
                    }
                }

                StingLog.Info($"ExcelLink RoundTrip Import: applied={applied}, skipped={skipped}, failed={failed}");

                TaskDialog.Show("STING Excel Round-Trip",
                    $"Round-trip complete!\n\n" +
                    $"Parameters updated: {applied}\n" +
                    $"Elements not found: {skipped}\n" +
                    $"Failures: {failed}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExcelLink RoundTrip failed", ex);
                message = ex.Message;
                TaskDialog.Show("STING Excel Round-Trip", $"Round-trip failed:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }
}
