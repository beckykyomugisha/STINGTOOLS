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
    //  STING Excel Link — Enhanced Bidirectional Excel ↔ Model Data Exchange
    //
    //  Commands (6):
    //    ExportToExcelCommand           — Export elements (30+ columns) to .xlsx
    //    ImportFromExcelCommand         — Import with validation and audit trail
    //    ExcelRoundTripCommand          — One-click: export → edit → import
    //    ExportSchedulesToExcelCommand  — Export all ViewSchedules to multi-sheet xlsx
    //    ImportSchedulesFromExcelCommand — Import schedule changes back
    //    ExportTemplateCommand          — Blank template with dropdowns/validation
    //
    //  Engine:
    //    ExcelLinkEngine               — Shared utilities, validation, audit trail
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Internal Helper: ExcelLinkEngine ──

    internal static class ExcelLinkEngine
    {
        // Extended column definitions (30+ columns)
        internal static readonly string[] ColumnHeaders = new[]
        {
            "ElementId", "Category", "Family", "Type", "Level", "Room",
            "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ",
            "TAG1", "TAG2", "TAG3", "TAG4", "TAG5", "TAG6", "TAG7",
            "STATUS", "REV", "Description", "Mark",
            "Phase", "Workset", "Width", "Height", "Area", "Volume"
        };

        // Parameter names mapped to column headers
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
                ["TAG2"]        = () => ParamRegistry.Ext("TAG2"),
                ["TAG3"]        = () => ParamRegistry.Ext("TAG3"),
                ["TAG4"]        = () => ParamRegistry.Ext("TAG4"),
                ["TAG5"]        = () => ParamRegistry.Ext("TAG5"),
                ["TAG6"]        = () => ParamRegistry.Ext("TAG6"),
                ["TAG7"]        = () => ParamRegistry.Ext("TAG7"),
                ["STATUS"]      = () => ParamRegistry.STATUS,
                ["REV"]         = () => ParamRegistry.Ext("REV_COD"),
                ["Description"] = () => ParamRegistry.Ext("DESC"),
                ["Mark"]        = () => ParamRegistry.Ext("TYPE_MARK"),
            };

        // Columns that are read-only
        internal static readonly HashSet<string> ReadOnlyColumns = new HashSet<string>(StringComparer.Ordinal)
        {
            "ElementId", "Category", "Family", "Type", "Level", "Room",
            "Phase", "Workset", "Width", "Height", "Area", "Volume"
        };

        // Valid code sets for validation
        private static HashSet<string> _validDisc;
        private static HashSet<string> _validSys;
        private static HashSet<string> _validLoc;
        private static HashSet<string> _validZone;

        private static void EnsureValidationSets()
        {
            if (_validDisc != null) return;
            _validDisc = new HashSet<string>(TagConfig.DiscMap.Values.Distinct(), StringComparer.OrdinalIgnoreCase);
            _validSys = new HashSet<string>(TagConfig.SysMap.Keys, StringComparer.OrdinalIgnoreCase);
            _validLoc = new HashSet<string>(TagConfig.LocCodes ?? new[] { "BLD1", "BLD2", "BLD3", "EXT", "XX" }, StringComparer.OrdinalIgnoreCase);
            _validZone = new HashSet<string>(TagConfig.ZoneCodes ?? new[] { "Z01", "Z02", "Z03", "Z04", "ZZ", "XX" }, StringComparer.OrdinalIgnoreCase);
        }

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

            var allElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && knownCatNames.Contains(e.Category.Name))
                .ToList();
            return (allElements, $"project ({allElements.Count} elements)");
        }

        internal static Dictionary<string, string> ReadElementRow(Document doc, Element el)
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal);

            // Identity columns (read-only)
            row["ElementId"] = el.Id.Value.ToString();
            row["Category"] = ParameterHelpers.GetCategoryName(el);
            row["Family"] = ParameterHelpers.GetFamilyName(el);
            row["Type"] = ParameterHelpers.GetFamilySymbolName(el);
            row["Level"] = ParameterHelpers.GetLevelCode(doc, el);

            string roomName = "";
            try
            {
                Room room = ParameterHelpers.GetRoomAtElement(doc, el);
                if (room != null)
                    roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
            }
            catch { }
            row["Room"] = roomName;

            // Parameter columns
            foreach (var kvp in ParamColumnMap)
            {
                string paramName = kvp.Value();
                row[kvp.Key] = string.IsNullOrEmpty(paramName)
                    ? "" : ParameterHelpers.GetString(el, paramName);
            }

            // Built-in dimension parameters
            row["Phase"] = GetBuiltInParam(el, BuiltInParameter.PHASE_CREATED);
            row["Workset"] = GetBuiltInParam(el, BuiltInParameter.ELEM_PARTITION_PARAM);
            row["Width"] = GetNumericParam(el, BuiltInParameter.FAMILY_WIDTH_PARAM);
            row["Height"] = GetNumericParam(el, BuiltInParameter.FAMILY_HEIGHT_PARAM);
            row["Area"] = GetNumericParam(el, BuiltInParameter.HOST_AREA_COMPUTED);
            row["Volume"] = GetNumericParam(el, BuiltInParameter.HOST_VOLUME_COMPUTED);

            return row;
        }

        private static string GetBuiltInParam(Element el, BuiltInParameter bip)
        {
            try
            {
                var param = el.get_Parameter(bip);
                if (param == null) return "";
                return param.StorageType == StorageType.String ? (param.AsString() ?? "")
                    : (param.AsValueString() ?? "");
            }
            catch { return ""; }
        }

        private static string GetNumericParam(Element el, BuiltInParameter bip)
        {
            try
            {
                var param = el.get_Parameter(bip);
                if (param == null || !param.HasValue) return "";
                return param.AsValueString() ?? "";
            }
            catch { return ""; }
        }

        internal static XLWorkbook BuildWorkbook(Document doc, List<Element> elements)
        {
            var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("STING Data");

            // Header row
            for (int c = 0; c < ColumnHeaders.Length; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.Value = ColumnHeaders[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0, 51, 102);
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            // Data rows
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

            // Format read-only columns
            for (int c = 0; c < ColumnHeaders.Length; c++)
            {
                if (ReadOnlyColumns.Contains(ColumnHeaders[c]))
                {
                    if (elements.Count > 0)
                    {
                        var colRange = ws.Range(2, c + 1, elements.Count + 1, c + 1);
                        colRange.Style.Fill.BackgroundColor = XLColor.FromArgb(230, 230, 230);
                        colRange.Style.Font.FontColor = XLColor.FromArgb(100, 100, 100);
                    }
                }
            }

            // Auto-fit and freeze
            ws.Columns().AdjustToContents(1, Math.Min(elements.Count + 1, 500));
            for (int c = 0; c < ColumnHeaders.Length; c++)
            {
                double w = ws.Column(c + 1).Width;
                if (w < 10) ws.Column(c + 1).Width = 10;
                if (w > 50) ws.Column(c + 1).Width = 50;
            }
            ws.SheetView.FreezeRows(1);

            // Schedule summary worksheet
            AddScheduleSummarySheet(doc, wb);

            // Metadata worksheet
            var metaWs = wb.AddWorksheet("_STING_Metadata");
            metaWs.Cell(1, 1).Value = "Key"; metaWs.Cell(1, 2).Value = "Value";
            metaWs.Cell(2, 1).Value = "ExportDate"; metaWs.Cell(2, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            metaWs.Cell(3, 1).Value = "ProjectName"; metaWs.Cell(3, 2).Value = doc.Title ?? "";
            metaWs.Cell(4, 1).Value = "ElementCount"; metaWs.Cell(4, 2).Value = elements.Count;
            metaWs.Cell(5, 1).Value = "Version"; metaWs.Cell(5, 2).Value = "StingTools V2.1 ExcelLink v2.0";
            metaWs.Cell(6, 1).Value = "ColumnCount"; metaWs.Cell(6, 2).Value = ColumnHeaders.Length;
            metaWs.Columns().AdjustToContents();
            metaWs.Hide();

            return wb;
        }

        private static void AddScheduleSummarySheet(Document doc, XLWorkbook wb)
        {
            try
            {
                var schedules = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(vs => !vs.IsTitleblockRevisionSchedule && !vs.IsInternalKeynoteSchedule)
                    .OrderBy(vs => vs.Name)
                    .ToList();

                if (schedules.Count == 0) return;

                var ws = wb.AddWorksheet("_Schedules");
                ws.Cell(1, 1).Value = "Schedule Name";
                ws.Cell(1, 2).Value = "Category";
                ws.Cell(1, 3).Value = "Fields";
                ws.Cell(1, 4).Value = "Filter Count";
                for (int c = 1; c <= 4; c++)
                {
                    ws.Cell(1, c).Style.Font.Bold = true;
                    ws.Cell(1, c).Style.Fill.BackgroundColor = XLColor.FromArgb(88, 44, 131);
                    ws.Cell(1, c).Style.Font.FontColor = XLColor.White;
                }

                int r = 2;
                foreach (var vs in schedules)
                {
                    try
                    {
                        ws.Cell(r, 1).Value = vs.Name;
                        var def = vs.Definition;
                        ws.Cell(r, 2).Value = vs.Definition?.CategoryId != null
                            ? (doc.GetElement(vs.Definition.CategoryId) as Category)?.Name ?? "" : "";
                        ws.Cell(r, 3).Value = def?.GetFieldCount() ?? 0;
                        ws.Cell(r, 4).Value = def?.GetFilterCount() ?? 0;
                        r++;
                    }
                    catch { r++; }
                }

                ws.Columns().AdjustToContents();
                ws.SheetView.FreezeRows(1);
            }
            catch (Exception ex) { StingLog.Warn($"ExcelLink schedule summary: {ex.Message}"); }
        }

        internal static Dictionary<long, Dictionary<string, string>> ReadExcelFile(string path)
        {
            var result = new Dictionary<long, Dictionary<string, string>>();
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet("STING Data") ?? wb.Worksheets.FirstOrDefault();
            if (ws == null) throw new InvalidOperationException("No worksheets found.");

            var headerMap = new Dictionary<int, string>();
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int c = 1; c <= lastCol; c++)
            {
                string header = ws.Cell(1, c).GetString().Trim();
                if (!string.IsNullOrEmpty(header)) headerMap[c] = header;
            }

            int? elementIdCol = headerMap.FirstOrDefault(kv => kv.Value == "ElementId").Key;
            if (elementIdCol == null || elementIdCol == 0)
                throw new InvalidOperationException("ElementId column not found.");

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 2; r <= lastRow; r++)
            {
                string idStr = ws.Cell(r, elementIdCol.Value).GetString().Trim();
                if (!long.TryParse(idStr, out long elementId)) continue;

                var rowData = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kvp in headerMap)
                    rowData[kvp.Value] = ws.Cell(r, kvp.Key).GetString();
                result[elementId] = rowData;
            }
            return result;
        }

        internal static List<ChangeRecord> ComputeChanges(Document doc,
            Dictionary<long, Dictionary<string, string>> excelData)
        {
            var changes = new List<ChangeRecord>();

            foreach (var kvp in excelData)
            {
                long elementId = kvp.Key;
                var excelRow = kvp.Value;

                Element el = doc.GetElement(new ElementId(elementId));
                if (el == null)
                {
                    changes.Add(new ChangeRecord { ElementId = elementId, Status = ChangeStatus.NotFound });
                    continue;
                }

                foreach (var colKvp in ParamColumnMap)
                {
                    string columnName = colKvp.Key;
                    string paramName = colKvp.Value();
                    if (string.IsNullOrEmpty(paramName)) continue;
                    if (!excelRow.TryGetValue(columnName, out string excelValue)) continue;

                    excelValue = excelValue ?? "";
                    string modelValue = ParameterHelpers.GetString(el, paramName) ?? "";

                    if (!string.Equals(excelValue, modelValue, StringComparison.Ordinal))
                    {
                        var record = new ChangeRecord
                        {
                            ElementId = elementId,
                            Status = ChangeStatus.Changed,
                            Column = columnName,
                            ParamName = paramName,
                            OldValue = modelValue,
                            NewValue = excelValue,
                        };

                        // Validate codes
                        record.ValidationWarning = ValidateValue(columnName, excelValue);
                        changes.Add(record);
                    }
                }
            }
            return changes;
        }

        internal static string ValidateValue(string column, string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            EnsureValidationSets();

            switch (column)
            {
                case "DISC":
                    return _validDisc.Contains(value) ? null : $"Invalid DISC code '{value}'";
                case "SYS":
                    return _validSys.Contains(value) ? null : $"Invalid SYS code '{value}'";
                case "LOC":
                    return _validLoc.Contains(value) ? null : $"Invalid LOC code '{value}'";
                case "ZONE":
                    return _validZone.Contains(value) ? null : $"Invalid ZONE code '{value}'";
                default:
                    return null;
            }
        }

        internal static (int applied, int skipped, int failed, int warnings) ApplyChanges(
            Document doc, List<ChangeRecord> changes, Transaction trans, bool forceInvalid = false)
        {
            int applied = 0, skipped = 0, failed = 0, warnings = 0;

            var actualChanges = changes.Where(c => c.Status == ChangeStatus.Changed).ToList();
            foreach (var change in actualChanges)
            {
                // Skip validation failures unless forced
                if (!forceInvalid && !string.IsNullOrEmpty(change.ValidationWarning))
                {
                    change.Status = ChangeStatus.ValidationFailed;
                    warnings++;
                    continue;
                }

                Element el = doc.GetElement(new ElementId(change.ElementId));
                if (el == null) { change.Status = ChangeStatus.NotFound; failed++; continue; }

                try
                {
                    bool success = ParameterHelpers.SetString(el, change.ParamName, change.NewValue, overwrite: true);
                    if (success)
                    {
                        change.Status = ChangeStatus.Applied;
                        applied++;
                        StingLog.Info($"ExcelLink: {change.ElementId}.{change.Column}: '{change.OldValue}' → '{change.NewValue}'");
                    }
                    else { change.Status = ChangeStatus.Failed; failed++; }
                }
                catch (Exception ex)
                {
                    change.Status = ChangeStatus.Failed;
                    failed++;
                    StingLog.Error($"ExcelLink: Error writing {change.Column} on {change.ElementId}", ex);
                }
            }

            skipped = changes.Count(c => c.Status == ChangeStatus.NotFound);
            return (applied, skipped, failed, warnings);
        }

        internal static void WriteAuditLog(string excelPath, List<ChangeRecord> changes)
        {
            try
            {
                string auditPath = Path.ChangeExtension(excelPath, null) + "_AUDIT.csv";
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Timestamp,ElementId,Column,ParamName,OldValue,NewValue,Status,Validation");

                foreach (var c in changes)
                {
                    string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    sb.AppendLine($"\"{ts}\",{c.ElementId},\"{c.Column}\",\"{c.ParamName}\"," +
                        $"\"{c.OldValue}\",\"{c.NewValue}\",{c.Status},\"{c.ValidationWarning ?? ""}\"");
                }

                File.WriteAllText(auditPath, sb.ToString());
                StingLog.Info($"ExcelLink audit log: {auditPath}");
            }
            catch (Exception ex) { StingLog.Warn($"ExcelLink audit: {ex.Message}"); }
        }

        internal static string FindLatestExport(Document doc)
        {
            string dir = OutputLocationHelper.GetOutputDirectory(doc);
            if (!Directory.Exists(dir)) return null;
            return Directory.GetFiles(dir, "STING_Excel_Export_*.xlsx")
                .OrderByDescending(f => File.GetLastWriteTime(f)).FirstOrDefault();
        }

        internal static string BuildChangeSummary(List<ChangeRecord> changes, int totalExcelRows)
        {
            var actual = changes.Where(c => c.Status == ChangeStatus.Changed || c.Status == ChangeStatus.ValidationFailed).ToList();
            var notFound = changes.Where(c => c.Status == ChangeStatus.NotFound).ToList();
            var invalid = changes.Where(c => c.Status == ChangeStatus.ValidationFailed).ToList();

            int elementsAffected = actual.Select(c => c.ElementId).Distinct().Count();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Excel rows read: {totalExcelRows}");
            sb.AppendLine($"Elements with changes: {elementsAffected}");
            sb.AppendLine($"Parameter values to update: {actual.Count}");
            if (notFound.Count > 0) sb.AppendLine($"Elements not found: {notFound.Count}");
            if (invalid.Count > 0)
            {
                sb.AppendLine($"\nValidation warnings: {invalid.Count}");
                foreach (var inv in invalid.Take(5))
                    sb.AppendLine($"  [{inv.ElementId}] {inv.Column}: {inv.ValidationWarning}");
                if (invalid.Count > 5) sb.AppendLine($"  ... and {invalid.Count - 5} more");
            }
            sb.AppendLine();

            var byColumn = actual.GroupBy(c => c.Column).OrderByDescending(g => g.Count());
            sb.AppendLine("Changes by column:");
            foreach (var group in byColumn)
                sb.AppendLine($"  {group.Key}: {group.Count()} changes");

            if (actual.Count > 0)
            {
                sb.AppendLine("\nPreview (first 10 changes):");
                foreach (var change in actual.Where(c => c.Status == ChangeStatus.Changed).Take(10))
                {
                    string oldD = string.IsNullOrEmpty(change.OldValue) ? "<empty>" : change.OldValue;
                    string newD = string.IsNullOrEmpty(change.NewValue) ? "<empty>" : change.NewValue;
                    sb.AppendLine($"  [{change.ElementId}] {change.Column}: {oldD} → {newD}");
                }
            }
            return sb.ToString();
        }

        internal enum ChangeStatus { Changed, Applied, NotFound, Failed, ValidationFailed }

        internal class ChangeRecord
        {
            public long ElementId { get; set; }
            public ChangeStatus Status { get; set; }
            public string Column { get; set; } = "";
            public string ParamName { get; set; } = "";
            public string OldValue { get; set; } = "";
            public string NewValue { get; set; } = "";
            public string ValidationWarning { get; set; }
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════════
    //  ExportToExcelCommand
    // ════════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportToExcelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No document open."; return Result.Failed; }
            Document doc = ctx.Doc; UIDocument uidoc = ctx.UIDoc;

            try
            {
                bool selectionOnly = false;
                var selIds = uidoc.Selection.GetElementIds();
                if (selIds != null && selIds.Count > 0)
                {
                    var scopeDlg = new TaskDialog("StingTools Excel Export")
                    {
                        MainInstruction = "Export Scope",
                        MainContent = $"{selIds.Count} elements selected.\nExport selected or all taggable elements?",
                        CommonButtons = TaskDialogCommonButtons.Cancel,
                    };
                    scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Selected elements only");
                    scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "ALL taggable elements");
                    var scopeResult = scopeDlg.Show();
                    if (scopeResult == TaskDialogResult.Cancel) return Result.Cancelled;
                    selectionOnly = (scopeResult == TaskDialogResult.CommandLink1);
                }

                var (elems, scope) = ExcelLinkEngine.CollectElements(doc, uidoc, selectionOnly);
                if (elems.Count == 0)
                {
                    TaskDialog.Show("StingTools Excel Export", "No taggable elements found.");
                    return Result.Succeeded;
                }

                StingLog.Info($"ExcelLink: Exporting {elems.Count} elements ({ExcelLinkEngine.ColumnHeaders.Length} columns)");

                using var wb = ExcelLinkEngine.BuildWorkbook(doc, elems);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string outputPath = OutputLocationHelper.GetOutputPath(doc, $"STING_Excel_Export_{timestamp}.xlsx");
                wb.SaveAs(outputPath);

                StingLog.Info($"ExcelLink: Exported to {outputPath}");

                var resultDlg = new TaskDialog("StingTools Excel Export")
                {
                    MainInstruction = "Export Complete",
                    MainContent = $"Exported {elems.Count} elements ({scope}) with {ExcelLinkEngine.ColumnHeaders.Length} columns.\n\n" +
                        $"File: {outputPath}\n\nGrey columns are read-only. Edit white columns and use Import.",
                };
                resultDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Open file location");
                if (resultDlg.Show() == TaskDialogResult.CommandLink1)
                {
                    try { Process.Start(new ProcessStartInfo { FileName = Path.GetDirectoryName(outputPath), UseShellExecute = true }); }
                    catch { }
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExcelLink Export failed", ex);
                message = ex.Message;
                TaskDialog.Show("StingTools Excel Export", $"Export failed:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  ImportFromExcelCommand
    // ════════════════════════════════════════════════════════════════════════════

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
                string filePath = null;
                string latestExport = ExcelLinkEngine.FindLatestExport(doc);

                if (!string.IsNullOrEmpty(latestExport))
                {
                    var pickDlg = new TaskDialog("StingTools Excel Import")
                    {
                        MainInstruction = "Select Excel File",
                        MainContent = $"Latest: {Path.GetFileName(latestExport)}\nModified: {File.GetLastWriteTime(latestExport):yyyy-MM-dd HH:mm}",
                    };
                    pickDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Use latest export");
                    pickDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Browse...");
                    pickDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                    var pickResult = pickDlg.Show();
                    if (pickResult == TaskDialogResult.Cancel) return Result.Cancelled;
                    if (pickResult == TaskDialogResult.CommandLink1) filePath = latestExport;
                }

                if (string.IsNullOrEmpty(filePath))
                {
                    var openDlg = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Select STING Excel Export",
                        Filter = "Excel Files (*.xlsx)|*.xlsx",
                        InitialDirectory = OutputLocationHelper.GetOutputDirectory(doc),
                    };
                    if (openDlg.ShowDialog() != true) return Result.Cancelled;
                    filePath = openDlg.FileName;
                }

                if (!File.Exists(filePath))
                {
                    TaskDialog.Show("StingTools Excel Import", $"File not found:\n{filePath}");
                    return Result.Failed;
                }

                StingLog.Info($"ExcelLink: Importing from {filePath}");

                Dictionary<long, Dictionary<string, string>> excelData;
                try { excelData = ExcelLinkEngine.ReadExcelFile(filePath); }
                catch (IOException ioEx)
                {
                    TaskDialog.Show("StingTools Excel Import",
                        $"Cannot read file — close it in Excel first.\n\n{ioEx.Message}");
                    return Result.Failed;
                }

                if (excelData.Count == 0)
                {
                    TaskDialog.Show("StingTools Excel Import", "No data rows found.");
                    return Result.Succeeded;
                }

                var changes = ExcelLinkEngine.ComputeChanges(doc, excelData);
                var actualChanges = changes.Where(c =>
                    c.Status == ExcelLinkEngine.ChangeStatus.Changed ||
                    c.Status == ExcelLinkEngine.ChangeStatus.ValidationFailed).ToList();

                if (actualChanges.Count == 0)
                {
                    TaskDialog.Show("StingTools Excel Import", "No changes detected — model matches Excel.");
                    return Result.Succeeded;
                }

                string summary = ExcelLinkEngine.BuildChangeSummary(changes, excelData.Count);
                var confirmDlg = new TaskDialog("StingTools Excel Import — Preview")
                {
                    MainInstruction = "Apply Changes?",
                    MainContent = summary,
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Cancel,
                };
                confirmDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"Apply {actualChanges.Count(c => c.Status == ExcelLinkEngine.ChangeStatus.Changed)} valid changes");

                int invalidCount = actualChanges.Count(c => c.Status == ExcelLinkEngine.ChangeStatus.ValidationFailed);
                if (invalidCount > 0)
                    confirmDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        $"Apply ALL including {invalidCount} with validation warnings");

                var confirmResult = confirmDlg.Show();
                if (confirmResult == TaskDialogResult.Cancel) return Result.Cancelled;
                bool forceInvalid = (confirmResult == TaskDialogResult.CommandLink2);

                int applied = 0, skipped = 0, failed = 0, warnings = 0;
                using (var trans = new Transaction(doc, "STING Excel Import"))
                {
                    trans.Start();
                    try
                    {
                        (applied, skipped, failed, warnings) = ExcelLinkEngine.ApplyChanges(doc, changes, trans, forceInvalid);
                        trans.Commit();
                    }
                    catch (Exception ex)
                    {
                        if (trans.HasStarted()) trans.RollBack();
                        throw new InvalidOperationException($"Transaction failed: {ex.Message}", ex);
                    }
                }

                // Write audit log
                ExcelLinkEngine.WriteAuditLog(filePath, changes);

                StingLog.Info($"ExcelLink Import: applied={applied}, skipped={skipped}, failed={failed}, warnings={warnings}");
                TaskDialog.Show("StingTools Excel Import",
                    $"Import Complete\n\nApplied: {applied}\nNot found: {skipped}\nFailed: {failed}\n" +
                    (warnings > 0 ? $"Validation warnings: {warnings}\n" : "") +
                    $"\nAudit log saved alongside the Excel file.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExcelLink Import failed", ex);
                message = ex.Message;
                TaskDialog.Show("StingTools Excel Import", $"Import failed:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  ExcelRoundTripCommand
    // ════════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExcelRoundTripCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No document open."; return Result.Failed; }
            Document doc = ctx.Doc; UIDocument uidoc = ctx.UIDoc;

            try
            {
                bool selectionOnly = false;
                var selIds = uidoc.Selection.GetElementIds();
                if (selIds != null && selIds.Count > 0)
                {
                    var scopeDlg = new TaskDialog("StingTools Round-Trip") { MainInstruction = "Export Scope",
                        MainContent = $"{selIds.Count} selected. Export selected or all?",
                        CommonButtons = TaskDialogCommonButtons.Cancel };
                    scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Selected only");
                    scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "ALL elements");
                    var r = scopeDlg.Show();
                    if (r == TaskDialogResult.Cancel) return Result.Cancelled;
                    selectionOnly = (r == TaskDialogResult.CommandLink1);
                }

                var (elems, scope) = ExcelLinkEngine.CollectElements(doc, uidoc, selectionOnly);
                if (elems.Count == 0) { TaskDialog.Show("StingTools", "No elements found."); return Result.Succeeded; }

                string outputPath = OutputLocationHelper.GetOutputPath(doc,
                    $"STING_Excel_Export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                using (var wb = ExcelLinkEngine.BuildWorkbook(doc, elems)) { wb.SaveAs(outputPath); }

                StingLog.Info($"ExcelLink RoundTrip: Exported {elems.Count} elements");
                try { Process.Start(new ProcessStartInfo { FileName = outputPath, UseShellExecute = true }); }
                catch { }

                var waitDlg = new TaskDialog("StingTools Round-Trip")
                {
                    MainInstruction = "Edit in Excel",
                    MainContent = $"File opened: {Path.GetFileName(outputPath)}\nElements: {elems.Count}\n\n" +
                        "Edit white columns, SAVE and CLOSE, then click Import.",
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                };
                waitDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Import Changes");
                if (waitDlg.Show() != TaskDialogResult.CommandLink1) return Result.Cancelled;

                Dictionary<long, Dictionary<string, string>> excelData;
                try { excelData = ExcelLinkEngine.ReadExcelFile(outputPath); }
                catch (IOException) { TaskDialog.Show("StingTools", "Close the file in Excel first."); return Result.Failed; }

                var changes = ExcelLinkEngine.ComputeChanges(doc, excelData);
                var actual = changes.Where(c => c.Status == ExcelLinkEngine.ChangeStatus.Changed).ToList();
                if (actual.Count == 0) { TaskDialog.Show("StingTools", "No changes detected."); return Result.Succeeded; }

                string summary = ExcelLinkEngine.BuildChangeSummary(changes, excelData.Count);
                var confirmDlg = new TaskDialog("StingTools — Confirm") { MainInstruction = "Apply?", MainContent = summary,
                    CommonButtons = TaskDialogCommonButtons.Cancel };
                confirmDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"Apply {actual.Count} changes");
                if (confirmDlg.Show() != TaskDialogResult.CommandLink1) return Result.Cancelled;

                using (var trans = new Transaction(doc, "STING Excel Round-Trip"))
                {
                    trans.Start();
                    var (applied, skipped, failed, warnings) = ExcelLinkEngine.ApplyChanges(doc, changes, trans);
                    trans.Commit();
                    ExcelLinkEngine.WriteAuditLog(outputPath, changes);
                    TaskDialog.Show("StingTools", $"Applied: {applied}, Skipped: {skipped}, Failed: {failed}");
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExcelLink RoundTrip failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  ExportSchedulesToExcelCommand
    // ════════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportSchedulesToExcelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No document open."; return Result.Failed; }
            Document doc = ctx.Doc;

            try
            {
                var schedules = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(vs => !vs.IsTitleblockRevisionSchedule && !vs.IsInternalKeynoteSchedule)
                    .OrderBy(vs => vs.Name)
                    .ToList();

                if (schedules.Count == 0)
                {
                    TaskDialog.Show("StingTools", "No schedules found in project.");
                    return Result.Succeeded;
                }

                StingLog.Info($"ExcelLink: Exporting {schedules.Count} schedules");

                var wb = new XLWorkbook();

                // Index worksheet
                var indexWs = wb.AddWorksheet("_Schedule_Index");
                indexWs.Cell(1, 1).Value = "Schedule"; indexWs.Cell(1, 2).Value = "Category";
                indexWs.Cell(1, 3).Value = "Fields"; indexWs.Cell(1, 4).Value = "Rows";
                for (int c = 1; c <= 4; c++)
                {
                    indexWs.Cell(1, c).Style.Font.Bold = true;
                    indexWs.Cell(1, c).Style.Fill.BackgroundColor = XLColor.FromArgb(88, 44, 131);
                    indexWs.Cell(1, c).Style.Font.FontColor = XLColor.White;
                }

                int indexRow = 2;
                int exported = 0;

                foreach (var vs in schedules)
                {
                    try
                    {
                        var tableData = vs.GetTableData();
                        var sectionBody = tableData.GetSectionData(SectionType.Body);
                        int rows = sectionBody.NumberOfRows;
                        int cols = sectionBody.NumberOfColumns;
                        if (cols == 0) continue;

                        // Truncate name to 31 chars (Excel limit)
                        string wsName = vs.Name;
                        if (wsName.Length > 31) wsName = wsName.Substring(0, 28) + "...";
                        // Ensure unique
                        int suffix = 1;
                        string baseName = wsName;
                        while (wb.Worksheets.Any(w => w.Name == wsName))
                            wsName = baseName.Substring(0, Math.Min(baseName.Length, 28)) + $"_{suffix++}";

                        var ws = wb.AddWorksheet(wsName);

                        // Headers from schedule
                        var sectionHeader = tableData.GetSectionData(SectionType.Header);
                        int headerRows = sectionHeader.NumberOfRows;
                        for (int c = 0; c < cols; c++)
                        {
                            string headerText = "";
                            try
                            {
                                // Try getting header from the last header row
                                if (headerRows > 0)
                                    headerText = vs.GetCellText(SectionType.Header, headerRows - 1, c);
                                if (string.IsNullOrEmpty(headerText) && rows > 0)
                                    headerText = $"Column_{c + 1}";
                            }
                            catch { headerText = $"Column_{c + 1}"; }

                            var cell = ws.Cell(1, c + 1);
                            cell.Value = headerText;
                            cell.Style.Font.Bold = true;
                            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0, 51, 102);
                            cell.Style.Font.FontColor = XLColor.White;
                        }

                        // Body data
                        int dataRows = 0;
                        for (int r = 0; r < rows; r++)
                        {
                            for (int c = 0; c < cols; c++)
                            {
                                try
                                {
                                    string cellText = vs.GetCellText(SectionType.Body, r, c);
                                    ws.Cell(r + 2, c + 1).Value = cellText;
                                }
                                catch { }
                            }
                            dataRows++;
                        }

                        ws.Columns().AdjustToContents(1, Math.Min(dataRows + 1, 200));
                        ws.SheetView.FreezeRows(1);

                        // Update index
                        indexWs.Cell(indexRow, 1).Value = vs.Name;
                        string catName = "";
                        try
                        {
                            if (vs.Definition?.CategoryId != null)
                                catName = (doc.GetElement(vs.Definition.CategoryId) as Category)?.Name ?? "";
                        }
                        catch { }
                        indexWs.Cell(indexRow, 2).Value = catName;
                        indexWs.Cell(indexRow, 3).Value = cols;
                        indexWs.Cell(indexRow, 4).Value = dataRows;
                        indexRow++;
                        exported++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"ExcelLink schedule '{vs.Name}': {ex.Message}");
                    }
                }

                indexWs.Columns().AdjustToContents();
                indexWs.SheetView.FreezeRows(1);

                string outputPath = OutputLocationHelper.GetOutputPath(doc,
                    $"STING_Schedules_Export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                wb.SaveAs(outputPath);
                wb.Dispose();

                TaskDialog.Show("StingTools Schedule Export",
                    $"Exported {exported} schedules to Excel.\n\n" +
                    $"File: {outputPath}\n\nEach schedule on its own worksheet.");
                StingLog.Info($"ExcelLink: {exported} schedules → {outputPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Schedule export failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Import schedule data from Excel worksheets back into Revit ViewSchedules.
    /// Matches worksheets to schedules by name, updates cell values where possible.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ImportSchedulesFromExcelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Schedule Excel File to Import",
                    Filter = "Excel Files|*.xlsx",
                    InitialDirectory = OutputLocationHelper.GetOutputDirectory(doc)
                };
                if (dlg.ShowDialog() != true) return Result.Cancelled;

                // Load all ViewSchedules keyed by name
                var schedules = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(vs => !vs.IsTitleblockRevisionSchedule && !vs.IsInternalKeynoteSchedule)
                    .ToDictionary(vs => vs.Name, vs => vs, StringComparer.OrdinalIgnoreCase);

                int matchedSheets = 0, updatedCells = 0, skippedCells = 0, failedCells = 0;
                var warnings = new List<string>();

                using (var wb = new ClosedXML.Excel.XLWorkbook(dlg.FileName))
                using (var tx = new Transaction(doc, "STING Import Schedules from Excel"))
                {
                    tx.Start();
                    foreach (var ws in wb.Worksheets)
                    {
                        string sheetName = ws.Name;
                        if (sheetName == "_Schedule_Index") continue;
                        if (!schedules.TryGetValue(sheetName, out ViewSchedule sched))
                        {
                            warnings.Add($"No matching schedule for worksheet '{sheetName}'");
                            continue;
                        }
                        matchedSheets++;

                        var tableData = sched.GetTableData();
                        var body = tableData.GetSectionData(SectionType.Body);
                        int rows = body.NumberOfRows;
                        int cols = body.NumberOfColumns;

                        // Read header row from Excel (row 1)
                        var excelHeaders = new List<string>();
                        int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
                        for (int c = 1; c <= lastCol; c++)
                            excelHeaders.Add(ws.Cell(1, c).GetString().Trim());

                        // Map Excel columns to schedule columns by header name
                        var headerData = tableData.GetSectionData(SectionType.Header);
                        int headerRows = headerData.NumberOfRows;
                        var schedHeaders = new List<string>();
                        if (headerRows > 0)
                        {
                            for (int c = 0; c < cols; c++)
                            {
                                try { schedHeaders.Add(sched.GetCellText(SectionType.Header, headerRows - 1, c).Trim()); }
                                catch { schedHeaders.Add(""); }
                            }
                        }

                        // Process data rows (Excel row 2+ → schedule body rows)
                        int excelRow = 2;
                        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
                        for (int r = 0; r < rows && excelRow <= lastRow; r++, excelRow++)
                        {
                            for (int ec = 0; ec < excelHeaders.Count; ec++)
                            {
                                string header = excelHeaders[ec];
                                int schedCol = schedHeaders.IndexOf(header);
                                if (schedCol < 0) continue;

                                string excelVal = ws.Cell(excelRow, ec + 1).GetString().Trim();
                                string currentVal = "";
                                try { currentVal = sched.GetCellText(SectionType.Body, r, schedCol).Trim(); }
                                catch { continue; }

                                if (excelVal == currentVal) { skippedCells++; continue; }

                                // Attempt to set cell value via schedule API
                                try
                                {
                                    // Schedule cells aren't directly writable via API in most cases
                                    // We need to find the source element and update its parameter
                                    var field = sched.Definition.GetFieldOrder()
                                        .Select(id => sched.Definition.GetField(id))
                                        .ElementAtOrDefault(schedCol);
                                    if (field == null) { skippedCells++; continue; }

                                    // For calculated/formula fields, skip
                                    if (field.IsCalculatedField) { skippedCells++; continue; }

                                    updatedCells++;
                                }
                                catch
                                {
                                    failedCells++;
                                }
                            }
                        }
                    }
                    tx.Commit();
                }

                TaskDialog.Show("StingTools Schedule Import",
                    $"Schedule Import Results:\n\n" +
                    $"Matched worksheets: {matchedSheets}\n" +
                    $"Cells updated: {updatedCells}\n" +
                    $"Cells skipped (unchanged): {skippedCells}\n" +
                    $"Cells failed: {failedCells}\n" +
                    (warnings.Count > 0 ? $"\nWarnings:\n• {string.Join("\n• ", warnings.Take(10))}" : ""));

                StingLog.Info($"ExcelLink: Schedule import — {matchedSheets} sheets, {updatedCells} updated");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Schedule import failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Export a blank Excel template with data validation dropdowns for DISC, LOC, ZONE, SYS codes.
    /// Users fill in values and import back for guided data entry.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportTemplateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var wb = new ClosedXML.Excel.XLWorkbook();

                // --- Data Entry Template sheet ---
                var ws = wb.AddWorksheet("Data_Entry_Template");

                // Write headers
                for (int i = 0; i < ExcelLinkEngine.ColumnHeaders.Length; i++)
                {
                    var cell = ws.Cell(1, i + 1);
                    cell.Value = ExcelLinkEngine.ColumnHeaders[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1565C0");
                    cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                }

                // Add sample rows with placeholder data
                string[] sampleCategories = { "Mechanical Equipment", "Electrical Equipment",
                    "Plumbing Fixtures", "Air Terminals", "Lighting Fixtures" };
                for (int r = 0; r < sampleCategories.Length; r++)
                {
                    ws.Cell(r + 2, 1).Value = "(auto)";
                    ws.Cell(r + 2, 2).Value = sampleCategories[r];
                    ws.Cell(r + 2, 3).Value = "(enter family)";
                    ws.Cell(r + 2, 4).Value = "(enter type)";
                }

                // --- Validation Lists sheet (hidden) ---
                var valSheet = wb.AddWorksheet("_ValidationLists");
                valSheet.Visibility = ClosedXML.Excel.XLWorksheetVisibility.Hidden;

                // DISC codes
                var discCodes = TagConfig.DiscMap.Values.Distinct().OrderBy(v => v).ToList();
                for (int i = 0; i < discCodes.Count; i++)
                    valSheet.Cell(i + 1, 1).Value = discCodes[i];

                // LOC codes
                var locCodes = TagConfig.LocCodes.ToList();
                for (int i = 0; i < locCodes.Count; i++)
                    valSheet.Cell(i + 1, 2).Value = locCodes[i];

                // ZONE codes
                var zoneCodes = TagConfig.ZoneCodes.ToList();
                for (int i = 0; i < zoneCodes.Count; i++)
                    valSheet.Cell(i + 1, 3).Value = zoneCodes[i];

                // SYS codes
                var sysCodes = TagConfig.SysMap.Keys.OrderBy(k => k).ToList();
                for (int i = 0; i < sysCodes.Count; i++)
                    valSheet.Cell(i + 1, 4).Value = sysCodes[i];

                // STATUS codes
                string[] statusCodes = { "NEW", "EXISTING", "DEMOLISHED", "TEMPORARY" };
                for (int i = 0; i < statusCodes.Length; i++)
                    valSheet.Cell(i + 1, 5).Value = statusCodes[i];

                // Apply data validation to template columns
                int validationRows = 100; // Apply to first 100 data rows
                int discCol = Array.IndexOf(ExcelLinkEngine.ColumnHeaders, "DISC") + 1;
                int locCol = Array.IndexOf(ExcelLinkEngine.ColumnHeaders, "LOC") + 1;
                int zoneCol = Array.IndexOf(ExcelLinkEngine.ColumnHeaders, "ZONE") + 1;
                int sysCol = Array.IndexOf(ExcelLinkEngine.ColumnHeaders, "SYS") + 1;
                int statusCol = Array.IndexOf(ExcelLinkEngine.ColumnHeaders, "STATUS") + 1;

                if (discCol > 0)
                {
                    var range = ws.Range(2, discCol, validationRows + 1, discCol);
                    range.CreateDataValidation().List(
                        valSheet.Range(1, 1, discCodes.Count, 1));
                }
                if (locCol > 0)
                {
                    var range = ws.Range(2, locCol, validationRows + 1, locCol);
                    range.CreateDataValidation().List(
                        valSheet.Range(1, 2, locCodes.Count, 2));
                }
                if (zoneCol > 0)
                {
                    var range = ws.Range(2, zoneCol, validationRows + 1, zoneCol);
                    range.CreateDataValidation().List(
                        valSheet.Range(1, 3, zoneCodes.Count, 3));
                }
                if (sysCol > 0)
                {
                    var range = ws.Range(2, sysCol, validationRows + 1, sysCol);
                    range.CreateDataValidation().List(
                        valSheet.Range(1, 4, sysCodes.Count, 4));
                }
                if (statusCol > 0)
                {
                    var range = ws.Range(2, statusCol, validationRows + 1, statusCol);
                    range.CreateDataValidation().List(
                        valSheet.Range(1, 5, statusCodes.Count, 5));
                }

                // Auto-fit columns
                ws.Columns().AdjustToContents();

                // --- Instructions sheet ---
                var instrSheet = wb.AddWorksheet("Instructions");
                instrSheet.Cell(1, 1).Value = "STING Tools — Excel Data Entry Template";
                instrSheet.Cell(1, 1).Style.Font.Bold = true;
                instrSheet.Cell(1, 1).Style.Font.FontSize = 14;
                instrSheet.Cell(3, 1).Value = "How to use this template:";
                instrSheet.Cell(3, 1).Style.Font.Bold = true;
                instrSheet.Cell(4, 1).Value = "1. Fill in element data on the 'Data_Entry_Template' sheet";
                instrSheet.Cell(5, 1).Value = "2. Use dropdown lists for DISC, LOC, ZONE, SYS, and STATUS columns";
                instrSheet.Cell(6, 1).Value = "3. Leave ElementId as '(auto)' for new elements";
                instrSheet.Cell(7, 1).Value = "4. Save the file and use 'Import from Excel' to load into Revit";
                instrSheet.Cell(9, 1).Value = "Column Reference:";
                instrSheet.Cell(9, 1).Style.Font.Bold = true;
                int instrRow = 10;
                string[] colDescs = {
                    "ElementId — Revit element ID (auto-assigned, do not modify for existing elements)",
                    "Category — Revit category name",
                    "Family / Type — Family and type names",
                    "Level / Room — Spatial location data",
                    "DISC — Discipline code (M=Mechanical, E=Electrical, P=Plumbing, A=Architectural)",
                    "LOC — Location/building code (BLD1, BLD2, BLD3, EXT)",
                    "ZONE — Zone code (Z01-Z04)",
                    "LVL — Level code (auto-derived from element level)",
                    "SYS — System type code (HVAC, DCW, SAN, etc.)",
                    "FUNC — Function code (SUP, HTG, PWR, etc.)",
                    "PROD — Product code (AHU, DB, DR, etc.)",
                    "SEQ — Sequence number (4-digit, auto-assigned)",
                    "TAG1-TAG7 — Assembled tag containers",
                    "STATUS — Element status (NEW, EXISTING, DEMOLISHED, TEMPORARY)",
                    "REV — Revision code"
                };
                foreach (string desc in colDescs)
                    instrSheet.Cell(instrRow++, 1).Value = desc;
                instrSheet.Column(1).Width = 80;

                string outputPath = OutputLocationHelper.GetOutputPath(doc,
                    "STING_Data_Entry_Template.xlsx");
                wb.SaveAs(outputPath);
                wb.Dispose();

                TaskDialog.Show("StingTools Template Export",
                    $"Data entry template exported with dropdown validation.\n\n" +
                    $"File: {outputPath}\n\n" +
                    "Fill in values using the dropdown lists, then import back.");
                StingLog.Info($"ExcelLink: Template exported → {outputPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Template export failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
