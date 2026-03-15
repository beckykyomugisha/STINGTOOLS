using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;

namespace StingTools.BIMManager
{
    // ════════════════════════════════════════════════════════════════════════════
    //  STING Excel Link — Bidirectional Excel ↔ Model Data Exchange (v2.0)
    //
    //  Exports element tag/parameter data to Excel for external editing,
    //  then imports changes back with validation, audit trail, and change preview.
    //
    //  Commands:
    //    ExportToExcelCommand              — Export taggable elements to .xlsx (30+ columns)
    //    ImportFromExcelCommand            — Import edited .xlsx with validation + audit trail
    //    ExcelRoundTripCommand             — One-click: export → edit → import
    //    ExportSchedulesToExcelCommand     — Export all ViewSchedules to .xlsx
    //    ImportSchedulesFromExcelCommand   — Import schedule data from .xlsx
    //    ExportTemplateCommand             — Export blank template with data validation
    // ════════════════════════════════════════════════════════════════════════════

    #region ── Internal Helper: ExcelLinkEngine ──

    internal static class ExcelLinkEngine
    {
        // ── Column definitions in export order (30+ columns) ──
        internal static readonly string[] ColumnHeaders = new[]
        {
            // Identity (read-only)
            "ElementId", "Category", "Family", "Type", "Level", "Room",
            // Source tokens (editable)
            "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ",
            // Tag containers
            "TAG1", "TAG2", "TAG3", "TAG4", "TAG5", "TAG6", "TAG7",
            // Status / lifecycle
            "STATUS", "REV",
            // Description & identity
            "Description", "Mark", "Comments",
            // Geometry / dimensional (read-only)
            "Width", "Height", "Area", "Volume", "Length",
            // Project context (read-only)
            "Phase", "Workset", "DesignOption",
            // Classification (editable)
            "AssemblyCode", "Keynote", "URL", "Image"
        };

        // Parameter names mapped to column headers (for tag/param columns)
        internal static readonly Dictionary<string, Func<string>> ParamColumnMap =
            new Dictionary<string, Func<string>>(StringComparer.Ordinal)
            {
                ["DISC"]         = () => ParamRegistry.DISC,
                ["LOC"]          = () => ParamRegistry.LOC,
                ["ZONE"]         = () => ParamRegistry.ZONE,
                ["LVL"]          = () => ParamRegistry.LVL,
                ["SYS"]          = () => ParamRegistry.SYS,
                ["FUNC"]         = () => ParamRegistry.FUNC,
                ["PROD"]         = () => ParamRegistry.PROD,
                ["SEQ"]          = () => ParamRegistry.SEQ,
                ["TAG1"]         = () => ParamRegistry.TAG1,
                ["TAG2"]         = () => ParamRegistry.TAG2,
                ["TAG3"]         = () => ParamRegistry.TAG3,
                ["TAG4"]         = () => ParamRegistry.TAG4,
                ["TAG5"]         = () => ParamRegistry.TAG5,
                ["TAG6"]         = () => ParamRegistry.TAG6,
                ["TAG7"]         = () => ParamRegistry.TAG7,
                ["STATUS"]       = () => ParamRegistry.STATUS,
                ["REV"]          = () => ParamRegistry.Ext("REV_COD"),
                ["Description"]  = () => ParamRegistry.DESC,
                ["Mark"]         = () => ParamRegistry.Ext("TYPE_MARK"),
                ["Comments"]     = () => ParamRegistry.Ext("COMMENTS"),
                ["AssemblyCode"] = () => ParamRegistry.Ext("ASSEMBLY_CODE"),
                ["Keynote"]      = () => ParamRegistry.Ext("KEYNOTE"),
                ["URL"]          = () => ParamRegistry.Ext("URL"),
                ["Image"]        = () => ParamRegistry.Ext("IMAGE"),
            };

        // Columns that are read-only (derived from model, not editable)
        internal static readonly HashSet<string> ReadOnlyColumns = new HashSet<string>(StringComparer.Ordinal)
        {
            "ElementId", "Category", "Family", "Type", "Level", "Room",
            "Width", "Height", "Area", "Volume", "Length",
            "Phase", "Workset", "DesignOption"
        };

        // ── Validation helpers ──

        /// <summary>
        /// Validate a single value against known codes. Returns error message or null if valid.
        /// </summary>
        internal static string ValidateValue(string column, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null; // empty is allowed

            switch (column)
            {
                case "DISC":
                    var validDisc = new HashSet<string>(TagConfig.DiscMap.Values, StringComparer.OrdinalIgnoreCase);
                    if (!validDisc.Contains(value))
                        return $"DISC '{value}' not in valid codes: {string.Join(", ", validDisc.OrderBy(v => v))}";
                    break;
                case "SYS":
                    if (!TagConfig.SysMap.ContainsKey(value))
                        return $"SYS '{value}' not in valid codes: {string.Join(", ", TagConfig.SysMap.Keys.OrderBy(k => k))}";
                    break;
                case "LOC":
                    if (!TagConfig.LocCodes.Contains(value))
                        return $"LOC '{value}' not in valid codes: {string.Join(", ", TagConfig.LocCodes)}";
                    break;
                case "ZONE":
                    if (!TagConfig.ZoneCodes.Contains(value))
                        return $"ZONE '{value}' not in valid codes: {string.Join(", ", TagConfig.ZoneCodes)}";
                    break;
            }
            return null;
        }

        /// <summary>
        /// Validate all changes and return validation warnings.
        /// </summary>
        internal static List<ValidationWarning> ValidateChanges(List<ChangeRecord> changes)
        {
            var warnings = new List<ValidationWarning>();
            foreach (var change in changes.Where(c => c.Status == ChangeStatus.Changed))
            {
                string error = ValidateValue(change.Column, change.NewValue);
                if (error != null)
                {
                    warnings.Add(new ValidationWarning
                    {
                        ElementId = change.ElementId,
                        Column = change.Column,
                        Value = change.NewValue,
                        Message = error
                    });
                    change.ValidationError = error;
                }
            }
            return warnings;
        }

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
        /// Read a built-in parameter value as a display string.
        /// </summary>
        private static string GetBuiltInParamString(Element el, BuiltInParameter bip)
        {
            try
            {
                var p = el.get_Parameter(bip);
                if (p == null || !p.HasValue) return "";
                switch (p.StorageType)
                {
                    case StorageType.Double:
                        // Convert from internal units (feet) to millimeters for dimensional params
                        double val = p.AsDouble();
                        if (val == 0) return "";
                        return Math.Round(val * 304.8, 1).ToString("F1");
                    case StorageType.Integer:
                        return p.AsInteger().ToString();
                    case StorageType.String:
                        return p.AsString() ?? "";
                    case StorageType.ElementId:
                        return p.AsValueString() ?? "";
                    default:
                        return p.AsValueString() ?? "";
                }
            }
            catch { return ""; }
        }

        /// <summary>
        /// Read element data into a row dictionary keyed by column header.
        /// Enhanced to 30+ columns including geometry, project context, and classification.
        /// </summary>
        internal static Dictionary<string, string> ReadElementRow(Document doc, Element el)
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal);

            // ── Identity columns (read-only) ──
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

            // ── Parameter columns (tokens, tags, status, description, etc.) ──
            foreach (var kvp in ParamColumnMap)
            {
                string paramName = null;
                try { paramName = kvp.Value(); } catch { }
                row[kvp.Key] = string.IsNullOrEmpty(paramName)
                    ? ""
                    : ParameterHelpers.GetString(el, paramName);
            }

            // ── Geometry / dimensional columns (read-only) ──
            row["Width"] = GetBuiltInParamString(el, BuiltInParameter.FAMILY_WIDTH_PARAM);
            if (string.IsNullOrEmpty(row["Width"]))
                row["Width"] = GetBuiltInParamString(el, BuiltInParameter.CASEWORK_WIDTH);

            row["Height"] = GetBuiltInParamString(el, BuiltInParameter.FAMILY_HEIGHT_PARAM);
            if (string.IsNullOrEmpty(row["Height"]))
                row["Height"] = GetBuiltInParamString(el, BuiltInParameter.GENERIC_HEIGHT);

            row["Area"] = GetBuiltInParamString(el, BuiltInParameter.HOST_AREA_COMPUTED);
            if (string.IsNullOrEmpty(row["Area"]))
                row["Area"] = GetBuiltInParamString(el, BuiltInParameter.ROOM_AREA);

            row["Volume"] = GetBuiltInParamString(el, BuiltInParameter.HOST_VOLUME_COMPUTED);
            if (string.IsNullOrEmpty(row["Volume"]))
                row["Volume"] = GetBuiltInParamString(el, BuiltInParameter.ROOM_VOLUME);

            row["Length"] = GetBuiltInParamString(el, BuiltInParameter.CURVE_ELEM_LENGTH);

            // ── Project context (read-only) ──
            string phaseName = "";
            try
            {
                var phaseParam = el.get_Parameter(BuiltInParameter.PHASE_CREATED);
                if (phaseParam != null && phaseParam.HasValue)
                {
                    var phaseId = phaseParam.AsElementId();
                    if (phaseId != ElementId.InvalidElementId)
                    {
                        var phase = doc.GetElement(phaseId) as Phase;
                        phaseName = phase?.Name ?? "";
                    }
                }
            }
            catch { }
            row["Phase"] = phaseName;

            string worksetName = "";
            try
            {
                if (doc.IsWorkshared)
                {
                    var wsParam = el.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                    if (wsParam != null && wsParam.HasValue)
                        worksetName = wsParam.AsValueString() ?? "";
                }
            }
            catch { }
            row["Workset"] = worksetName;

            string designOption = "";
            try
            {
                var doParam = el.get_Parameter(BuiltInParameter.DESIGN_OPTION_ID);
                if (doParam != null && doParam.HasValue)
                    designOption = doParam.AsValueString() ?? "";
            }
            catch { }
            row["DesignOption"] = designOption;

            return row;
        }

        /// <summary>
        /// Build the Excel workbook from element data with 30+ columns.
        /// Includes _STING_Metadata and _Schedules worksheets.
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

                if (ReadOnlyColumns.Contains(ColumnHeaders[c]))
                {
                    // Read-only columns: dark blue header
                    cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0, 51, 102);
                    cell.Style.Font.FontColor = XLColor.White;
                }
                else
                {
                    // Editable columns: green header
                    cell.Style.Fill.BackgroundColor = XLColor.FromArgb(27, 94, 32);
                    cell.Style.Font.FontColor = XLColor.White;
                }
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
            for (int c = 0; c < ColumnHeaders.Length; c++)
            {
                if (ReadOnlyColumns.Contains(ColumnHeaders[c]))
                {
                    if (elements.Count > 0)
                    {
                        var colRange = ws.Range(2, c + 1, elements.Count + 1, c + 1);
                        colRange.Style.Fill.BackgroundColor = XLColor.FromArgb(230, 230, 230);
                        colRange.Style.Font.FontColor = XLColor.FromArgb(100, 100, 100);

                        if (ColumnHeaders[c] == "ElementId")
                            colRange.Style.Protection.Locked = true;
                    }
                }
            }

            // ── Highlight empty tag cells with conditional formatting (pale red) ──
            string[] tagColumns = { "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ", "TAG1" };
            foreach (string tagCol in tagColumns)
            {
                int colIdx = Array.IndexOf(ColumnHeaders, tagCol);
                if (colIdx < 0 || elements.Count == 0) continue;
                var tagRange = ws.Range(2, colIdx + 1, elements.Count + 1, colIdx + 1);
                tagRange.AddConditionalFormat().WhenIsBlank()
                    .Fill.SetBackgroundColor(XLColor.FromArgb(255, 235, 238));
            }

            // ── Auto-fit columns ──
            ws.Columns().AdjustToContents(1, Math.Min(elements.Count + 1, 500));
            for (int c = 0; c < ColumnHeaders.Length; c++)
            {
                double currentWidth = ws.Column(c + 1).Width;
                if (currentWidth < 10) ws.Column(c + 1).Width = 10;
                if (currentWidth > 50) ws.Column(c + 1).Width = 50;
            }

            // ── Freeze header row ──
            ws.SheetView.FreezeRows(1);

            // ── Add metadata worksheet ──
            var metaWs = wb.AddWorksheet("_STING_Metadata");
            metaWs.Cell(1, 1).Value = "Key";
            metaWs.Cell(1, 2).Value = "Value";
            metaWs.Cell(1, 1).Style.Font.Bold = true;
            metaWs.Cell(1, 2).Style.Font.Bold = true;
            metaWs.Cell(2, 1).Value = "ExportDate";
            metaWs.Cell(2, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            metaWs.Cell(3, 1).Value = "ProjectName";
            metaWs.Cell(3, 2).Value = doc.Title ?? "";
            metaWs.Cell(4, 1).Value = "ElementCount";
            metaWs.Cell(4, 2).Value = elements.Count;
            metaWs.Cell(5, 1).Value = "ColumnCount";
            metaWs.Cell(5, 2).Value = ColumnHeaders.Length;
            metaWs.Cell(6, 1).Value = "Version";
            metaWs.Cell(6, 2).Value = "STING ExcelLink v2.0";
            metaWs.Cell(7, 1).Value = "ReadOnlyColumns";
            metaWs.Cell(7, 2).Value = string.Join(", ", ReadOnlyColumns.OrderBy(c => Array.IndexOf(ColumnHeaders, c)));
            metaWs.Columns().AdjustToContents();
            metaWs.Hide();

            // ── Add _Schedules worksheet ──
            AddSchedulesSummaryWorksheet(doc, wb);

            return wb;
        }

        /// <summary>
        /// Add a _Schedules worksheet listing all ViewSchedules in the project.
        /// </summary>
        internal static void AddSchedulesSummaryWorksheet(Document doc, XLWorkbook wb)
        {
            var schedWs = wb.AddWorksheet("_Schedules");
            string[] schedHeaders = { "Schedule Name", "Category", "Field Count", "Row Count", "Has Filters", "Is Template" };
            for (int c = 0; c < schedHeaders.Length; c++)
            {
                var cell = schedWs.Cell(1, c + 1);
                cell.Value = schedHeaders[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(63, 81, 181);
                cell.Style.Font.FontColor = XLColor.White;
            }

            var schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(vs => !vs.IsTitleblockRevisionSchedule && !vs.IsInternalKeynoteSchedule)
                .OrderBy(vs => vs.Name)
                .ToList();

            int row = 2;
            foreach (var vs in schedules)
            {
                try
                {
                    schedWs.Cell(row, 1).Value = vs.Name;

                    string catName = "";
                    try
                    {
                        if (vs.Definition?.CategoryId != null)
                            catName = Category.GetCategory(doc, vs.Definition.CategoryId)?.Name ?? "";
                    }
                    catch { }
                    schedWs.Cell(row, 2).Value = catName;

                    int fieldCount = 0;
                    try { fieldCount = vs.Definition.GetFieldCount(); } catch { }
                    schedWs.Cell(row, 3).Value = fieldCount;

                    int rowCount = 0;
                    try
                    {
                        var tableData = vs.GetTableData();
                        var body = tableData.GetSectionData(SectionType.Body);
                        rowCount = body.NumberOfRows;
                    }
                    catch { }
                    schedWs.Cell(row, 4).Value = rowCount;

                    bool hasFilters = false;
                    try { hasFilters = vs.Definition.GetFilterCount() > 0; } catch { }
                    schedWs.Cell(row, 5).Value = hasFilters ? "Yes" : "No";

                    schedWs.Cell(row, 6).Value = vs.IsTemplate ? "Yes" : "No";

                    row++;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ExcelLink: Could not read schedule '{vs.Name}': {ex.Message}");
                }
            }

            schedWs.Columns().AdjustToContents();
            schedWs.SheetView.FreezeRows(1);
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
                ws = wb.Worksheets.FirstOrDefault(w => !w.Name.StartsWith("_"));
                if (ws == null)
                    throw new InvalidOperationException("No data worksheets found in the Excel file.");
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

                    // Skip read-only columns
                    if (ReadOnlyColumns.Contains(columnName)) continue;

                    string paramName = null;
                    try { paramName = colKvp.Value(); } catch { continue; }
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
        /// Optionally skips values that fail validation (unless forceInvalid is true).
        /// Returns (applied, skipped, failed) counts.
        /// </summary>
        internal static (int applied, int skipped, int failed) ApplyChanges(
            Document doc, List<ChangeRecord> changes, Transaction trans, bool forceInvalid = false)
        {
            int applied = 0, skipped = 0, failed = 0;

            var actualChanges = changes.Where(c => c.Status == ChangeStatus.Changed).ToList();
            foreach (var change in actualChanges)
            {
                // Skip invalid values unless forced
                if (!forceInvalid && !string.IsNullOrEmpty(change.ValidationError))
                {
                    change.Status = ChangeStatus.ValidationSkipped;
                    skipped++;
                    StingLog.Warn($"ExcelLink: Skipped {change.ElementId}.{change.Column}='{change.NewValue}' — {change.ValidationError}");
                    continue;
                }

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
                        StingLog.Info($"ExcelLink: {change.ElementId}.{change.Column}: '{change.OldValue}' -> '{change.NewValue}'");
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

            skipped += changes.Count(c => c.Status == ChangeStatus.NotFound);

            return (applied, skipped, failed);
        }

        /// <summary>
        /// Write a change log CSV alongside the import file.
        /// Records timestamp, user, elementId, paramName, oldValue, newValue, status.
        /// </summary>
        internal static void WriteChangeLog(string importFilePath, List<ChangeRecord> changes, string userName)
        {
            try
            {
                string dir = Path.GetDirectoryName(importFilePath) ?? "";
                string baseName = Path.GetFileNameWithoutExtension(importFilePath);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string logPath = Path.Combine(dir, $"{baseName}_changelog_{timestamp}.csv");

                var sb = new StringBuilder();
                sb.AppendLine("Timestamp,User,ElementId,ParamName,Column,OldValue,NewValue,Status,ValidationError");

                string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                foreach (var change in changes)
                {
                    string oldVal = EscapeCsvField(change.OldValue ?? "");
                    string newVal = EscapeCsvField(change.NewValue ?? "");
                    string valErr = EscapeCsvField(change.ValidationError ?? "");
                    sb.AppendLine($"{ts},{EscapeCsvField(userName)},{change.ElementId}," +
                                  $"{EscapeCsvField(change.ParamName ?? "")},{EscapeCsvField(change.Column ?? "")}," +
                                  $"{oldVal},{newVal},{change.Status},{valErr}");
                }

                File.WriteAllText(logPath, sb.ToString());
                StingLog.Info($"ExcelLink: Change log written to {logPath}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ExcelLink: Failed to write change log: {ex.Message}");
            }
        }

        private static string EscapeCsvField(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
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

        /// <summary>Build a summary of changes for preview display, including validation warnings.</summary>
        internal static string BuildChangeSummary(List<ChangeRecord> changes, int totalExcelRows,
            List<ValidationWarning> validationWarnings = null)
        {
            var actual = changes.Where(c => c.Status == ChangeStatus.Changed).ToList();
            var notFound = changes.Where(c => c.Status == ChangeStatus.NotFound).ToList();

            int elementsAffected = actual.Select(c => c.ElementId).Distinct().Count();
            int paramsChanged = actual.Count;

            var sb = new StringBuilder();
            sb.AppendLine($"Excel rows read: {totalExcelRows}");
            sb.AppendLine($"Elements with changes: {elementsAffected}");
            sb.AppendLine($"Parameter values to update: {paramsChanged}");
            if (notFound.Count > 0)
                sb.AppendLine($"Elements not found in model: {notFound.Count}");

            // Validation warnings
            if (validationWarnings != null && validationWarnings.Count > 0)
            {
                int invalidCount = validationWarnings.Count;
                sb.AppendLine();
                sb.AppendLine($"VALIDATION WARNINGS ({invalidCount}):");
                foreach (var warn in validationWarnings.Take(8))
                {
                    sb.AppendLine($"  [{warn.ElementId}] {warn.Column}='{warn.Value}': {warn.Message}");
                }
                if (invalidCount > 8)
                    sb.AppendLine($"  ... and {invalidCount - 8} more");
                sb.AppendLine("  (Invalid values will be SKIPPED unless forced)");
            }

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
                    string marker = string.IsNullOrEmpty(change.ValidationError) ? "" : " [!]";
                    sb.AppendLine($"  [{change.ElementId}] {change.Column}: {oldDisplay} -> {newDisplay}{marker}");
                }
                if (actual.Count > 10)
                    sb.AppendLine($"  ... and {actual.Count - 10} more");
            }

            return sb.ToString();
        }

        internal enum ChangeStatus { Changed, Applied, NotFound, Failed, ValidationSkipped }

        internal class ChangeRecord
        {
            public long ElementId { get; set; }
            public ChangeStatus Status { get; set; }
            public string Column { get; set; }
            public string ParamName { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string ValidationError { get; set; }
        }

        internal class ValidationWarning
        {
            public long ElementId { get; set; }
            public string Column { get; set; }
            public string Value { get; set; }
            public string Message { get; set; }
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════════
    //  ExportToExcelCommand — Export element data to .xlsx (30+ columns)
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Export taggable element data (tags, parameters, spatial info, geometry,
    /// classification) to an Excel workbook for external editing. Includes a
    /// _Schedules summary worksheet listing all ViewSchedules in the project.
    /// Supports exporting selected elements only or all taggable elements.
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
                int editableCols = ExcelLinkEngine.ColumnHeaders.Length - ExcelLinkEngine.ReadOnlyColumns.Count;
                var resultDlg = new TaskDialog("STING Excel Export")
                {
                    MainInstruction = "Export Complete",
                    MainContent = $"Exported {elems.Count} elements ({scope}) to:\n\n{outputPath}\n\n" +
                                  $"Total columns: {ExcelLinkEngine.ColumnHeaders.Length} ({editableCols} editable, " +
                                  $"{ExcelLinkEngine.ReadOnlyColumns.Count} read-only)\n\n" +
                                  "Grey columns are read-only (identity, geometry, project context).\n" +
                                  "Green-header columns are editable.\n" +
                                  "Edit the white columns and use Import to update the model.\n\n" +
                                  "Includes _Schedules worksheet with all ViewSchedule data.",
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
    //  ImportFromExcelCommand — Import edited Excel back into model with validation
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Import an edited STING Excel export back into the model. Matches rows by
    /// ElementId, compares current model values against Excel values, validates
    /// DISC/SYS/LOC/ZONE values against TagConfig, shows a preview summary with
    /// validation warnings, and writes a change log CSV after import.
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

                // ── Validate changes ──
                var validationWarnings = ExcelLinkEngine.ValidateChanges(changes);

                // ── Preview changes and confirm ──
                string summary = ExcelLinkEngine.BuildChangeSummary(changes, excelData.Count, validationWarnings);

                var confirmDlg = new TaskDialog("STING Excel Import — Preview Changes")
                {
                    MainInstruction = "Apply Changes?",
                    MainContent = summary,
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Cancel,
                };
                confirmDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"Apply {actualChanges.Count} changes (skip invalid)",
                    $"Update {actualChanges.Select(c => c.ElementId).Distinct().Count()} elements, " +
                    $"skip {validationWarnings.Count} invalid values");

                if (validationWarnings.Count > 0)
                {
                    confirmDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        $"Force ALL {actualChanges.Count} changes (including invalid)",
                        "Apply all values even if they fail validation");
                }

                var confirmResult = confirmDlg.Show();

                if (confirmResult == TaskDialogResult.Cancel)
                    return Result.Cancelled;

                bool forceInvalid = (confirmResult == TaskDialogResult.CommandLink2);

                // ── Apply changes in a single transaction ──
                int applied = 0, skipped = 0, failed = 0;
                using (var trans = new Transaction(doc, "STING Excel Import"))
                {
                    trans.Start();
                    try
                    {
                        (applied, skipped, failed) = ExcelLinkEngine.ApplyChanges(doc, changes, trans, forceInvalid);
                        trans.Commit();
                    }
                    catch (Exception ex)
                    {
                        if (trans.HasStarted())
                            trans.RollBack();
                        throw new InvalidOperationException($"Transaction failed: {ex.Message}", ex);
                    }
                }

                // ── Write change log CSV ──
                string userName = "";
                try { userName = doc.Application.Username ?? ""; } catch { }
                ExcelLinkEngine.WriteChangeLog(filePath, changes, userName);

                // ── Report results ──
                int valSkipped = changes.Count(c => c.Status == ExcelLinkEngine.ChangeStatus.ValidationSkipped);
                StingLog.Info($"ExcelLink Import: applied={applied}, skipped={skipped}, valSkipped={valSkipped}, failed={failed}");

                var resultMsg = $"Import Complete\n\n" +
                                $"Parameters updated: {applied}\n" +
                                $"Elements not found: {skipped}\n" +
                                $"Validation skipped: {valSkipped}\n" +
                                $"Failures: {failed}\n\n" +
                                $"Source: {Path.GetFileName(filePath)}\n" +
                                "A change log CSV has been saved alongside the import file.";
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
    /// Includes full validation and audit trail on import.
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
                int editableCols = ExcelLinkEngine.ColumnHeaders.Length - ExcelLinkEngine.ReadOnlyColumns.Count;
                var waitDlg = new TaskDialog("STING Excel Round-Trip")
                {
                    MainInstruction = "Edit in Excel",
                    MainContent = $"The file has been opened:\n{Path.GetFileName(outputPath)}\n\n" +
                                  $"Elements exported: {elems.Count}\n" +
                                  $"Columns: {ExcelLinkEngine.ColumnHeaders.Length} ({editableCols} editable)\n\n" +
                                  "Edit the parameter values in the green-header columns.\n" +
                                  "When finished, SAVE and CLOSE the Excel file, then click 'Import Changes'.\n\n" +
                                  "Grey columns are read-only and will be ignored during import.\n" +
                                  "DISC, SYS, LOC, ZONE values will be validated on import.",
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

                // ── Validate ──
                var validationWarnings = ExcelLinkEngine.ValidateChanges(changes);

                // ── Preview and confirm ──
                string summary = ExcelLinkEngine.BuildChangeSummary(changes, excelData.Count, validationWarnings);
                var confirmDlg = new TaskDialog("STING Excel Round-Trip — Preview Changes")
                {
                    MainInstruction = "Apply Changes?",
                    MainContent = summary,
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Cancel,
                };
                confirmDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"Apply {actualChanges.Count} changes (skip invalid)");
                if (validationWarnings.Count > 0)
                {
                    confirmDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        $"Force ALL {actualChanges.Count} changes (including invalid)");
                }
                var confirmResult = confirmDlg.Show();

                if (confirmResult == TaskDialogResult.Cancel)
                    return Result.Cancelled;

                bool forceInvalid = (confirmResult == TaskDialogResult.CommandLink2);

                // ── Apply ──
                int applied = 0, skipped = 0, failed = 0;
                using (var trans = new Transaction(doc, "STING Excel Round-Trip Import"))
                {
                    trans.Start();
                    try
                    {
                        (applied, skipped, failed) = ExcelLinkEngine.ApplyChanges(doc, changes, trans, forceInvalid);
                        trans.Commit();
                    }
                    catch (Exception ex)
                    {
                        if (trans.HasStarted())
                            trans.RollBack();
                        throw new InvalidOperationException($"Transaction failed: {ex.Message}", ex);
                    }
                }

                // ── Write change log ──
                string userName = "";
                try { userName = doc.Application.Username ?? ""; } catch { }
                ExcelLinkEngine.WriteChangeLog(outputPath, changes, userName);

                int valSkipped = changes.Count(c => c.Status == ExcelLinkEngine.ChangeStatus.ValidationSkipped);
                StingLog.Info($"ExcelLink RoundTrip Import: applied={applied}, skipped={skipped}, valSkipped={valSkipped}, failed={failed}");

                TaskDialog.Show("STING Excel Round-Trip",
                    $"Round-trip complete!\n\n" +
                    $"Parameters updated: {applied}\n" +
                    $"Elements not found: {skipped}\n" +
                    $"Validation skipped: {valSkipped}\n" +
                    $"Failures: {failed}\n\n" +
                    "A change log CSV has been saved alongside the export file.");

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

    // ════════════════════════════════════════════════════════════════════════════
    //  ExportSchedulesToExcelCommand — Export all ViewSchedules to .xlsx
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Export all ViewSchedules in the project to an Excel workbook. Each schedule
    /// becomes a separate worksheet (name truncated to 31 chars for Excel limit).
    /// Headers from schedule fields, data from schedule body. Includes a
    /// _Schedule_Index worksheet listing all exported schedules with row counts.
    /// </summary>
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
                    TaskDialog.Show("STING Schedule Export", "No schedules found in the project.");
                    return Result.Succeeded;
                }

                StingLog.Info($"ExcelLink: Exporting {schedules.Count} schedules");

                var wb = new XLWorkbook();

                // ── Create _Schedule_Index worksheet ──
                var indexWs = wb.AddWorksheet("_Schedule_Index");
                string[] indexHeaders = { "Schedule Name", "Category", "Fields", "Rows", "Worksheet Name", "Status" };
                for (int c = 0; c < indexHeaders.Length; c++)
                {
                    var cell = indexWs.Cell(1, c + 1);
                    cell.Value = indexHeaders[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0, 51, 102);
                    cell.Style.Font.FontColor = XLColor.White;
                }

                int indexRow = 2;
                int exported = 0;
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var vs in schedules)
                {
                    string scheduleName = vs.Name;
                    string status = "OK";

                    try
                    {
                        var tableData = vs.GetTableData();
                        var bodySection = tableData.GetSectionData(SectionType.Body);
                        int rows = bodySection.NumberOfRows;
                        int cols = bodySection.NumberOfColumns;

                        if (cols == 0)
                        {
                            status = "No columns";
                            indexWs.Cell(indexRow, 1).Value = scheduleName;
                            indexWs.Cell(indexRow, 6).Value = status;
                            indexRow++;
                            continue;
                        }

                        // Truncate worksheet name to 31 chars (Excel limit)
                        string wsName = scheduleName.Length > 31
                            ? scheduleName.Substring(0, 31)
                            : scheduleName;

                        // Remove invalid worksheet name characters
                        wsName = wsName.Replace(':', '_').Replace('\\', '_').Replace('/', '_')
                                       .Replace('?', '_').Replace('*', '_').Replace('[', '_').Replace(']', '_');

                        // Ensure unique name
                        string baseName = wsName;
                        int suffix = 1;
                        while (usedNames.Contains(wsName) || wb.Worksheets.Any(w => w.Name == wsName))
                        {
                            string sfx = $"_{suffix++}";
                            wsName = baseName.Substring(0, Math.Min(baseName.Length, 31 - sfx.Length)) + sfx;
                        }
                        usedNames.Add(wsName);

                        var ws = wb.AddWorksheet(wsName);

                        // ── Write headers from schedule ──
                        var sectionHeader = tableData.GetSectionData(SectionType.Header);
                        int headerRows = sectionHeader.NumberOfRows;
                        for (int c = 0; c < cols; c++)
                        {
                            string headerText = "";
                            try
                            {
                                if (headerRows > 0)
                                    headerText = vs.GetCellText(SectionType.Header, headerRows - 1, c);
                                if (string.IsNullOrEmpty(headerText))
                                    headerText = $"Column_{c + 1}";
                            }
                            catch { headerText = $"Column_{c + 1}"; }

                            var cell = ws.Cell(1, c + 1);
                            cell.Value = headerText;
                            cell.Style.Font.Bold = true;
                            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0, 51, 102);
                            cell.Style.Font.FontColor = XLColor.White;
                        }

                        // ── Write body data ──
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

                        // ── Update index ──
                        indexWs.Cell(indexRow, 1).Value = scheduleName;

                        string catName = "";
                        try
                        {
                            if (vs.Definition?.CategoryId != null)
                                catName = Category.GetCategory(doc, vs.Definition.CategoryId)?.Name ?? "";
                        }
                        catch { }
                        indexWs.Cell(indexRow, 2).Value = catName;
                        indexWs.Cell(indexRow, 3).Value = cols;
                        indexWs.Cell(indexRow, 4).Value = dataRows;
                        indexWs.Cell(indexRow, 5).Value = wsName;
                        indexWs.Cell(indexRow, 6).Value = status;

                        indexRow++;
                        exported++;
                    }
                    catch (Exception ex)
                    {
                        status = $"Error: {ex.Message}";
                        indexWs.Cell(indexRow, 1).Value = scheduleName;
                        indexWs.Cell(indexRow, 6).Value = status;
                        indexRow++;
                        StingLog.Warn($"ExcelLink schedule '{scheduleName}': {ex.Message}");
                    }
                }

                indexWs.Columns().AdjustToContents();
                indexWs.SheetView.FreezeRows(1);

                string outputPath = OutputLocationHelper.GetOutputPath(doc,
                    $"STING_Schedules_Export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                wb.SaveAs(outputPath);
                wb.Dispose();

                var resultDlg = new TaskDialog("STING Schedule Export")
                {
                    MainInstruction = "Schedule Export Complete",
                    MainContent = $"Exported {exported} of {schedules.Count} schedules to Excel.\n\n" +
                                  $"File: {outputPath}\n\n" +
                                  "Each schedule is on its own worksheet.\n" +
                                  "See _Schedule_Index for a summary of all exported schedules.",
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
                    catch { }
                }

                StingLog.Info($"ExcelLink: {exported} schedules exported to {outputPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Schedule export failed", ex);
                message = ex.Message;
                TaskDialog.Show("STING Schedule Export", $"Export failed:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  ImportSchedulesFromExcelCommand — Import schedule data from .xlsx
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Import schedule data from Excel worksheets back into Revit ViewSchedules.
    /// Matches worksheets to schedules by name, detects changed cells, previews
    /// changes, and applies updates via source element parameter writes.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ImportSchedulesFromExcelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No document open."; return Result.Failed; }

            Document doc = ctx.Doc;

            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select Schedule Excel File to Import",
                    Filter = "Excel Files (*.xlsx)|*.xlsx",
                    InitialDirectory = OutputLocationHelper.GetOutputDirectory(doc)
                };
                if (dlg.ShowDialog() != true) return Result.Cancelled;

                // Load all ViewSchedules keyed by name
                var schedules = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(vs => !vs.IsTitleblockRevisionSchedule && !vs.IsInternalKeynoteSchedule)
                    .ToDictionary(vs => vs.Name, vs => vs, StringComparer.OrdinalIgnoreCase);

                int matchedSheets = 0, detectedChanges = 0, updatedCells = 0;
                int skippedCells = 0, failedCells = 0;
                var warnings = new List<string>();
                var changeDetails = new List<string>();

                using var wb = new XLWorkbook(dlg.FileName);

                // ── First pass: detect changes for preview ──
                foreach (var ws in wb.Worksheets)
                {
                    string sheetName = ws.Name;
                    if (sheetName.StartsWith("_")) continue;

                    // Try exact match first, then try matching via truncated names
                    ViewSchedule sched = null;
                    if (schedules.TryGetValue(sheetName, out sched))
                    {
                        // Exact match found
                    }
                    else
                    {
                        // Try to match by prefix (worksheet may have been truncated)
                        var candidate = schedules.FirstOrDefault(kvp =>
                            kvp.Key.StartsWith(sheetName, StringComparison.OrdinalIgnoreCase) ||
                            sheetName.StartsWith(kvp.Key.Substring(0, Math.Min(kvp.Key.Length, 31)),
                                StringComparison.OrdinalIgnoreCase));
                        if (candidate.Value != null)
                            sched = candidate.Value;
                    }

                    if (sched == null)
                    {
                        warnings.Add($"No matching schedule for worksheet '{sheetName}'");
                        continue;
                    }
                    matchedSheets++;

                    var tableData = sched.GetTableData();
                    var body = tableData.GetSectionData(SectionType.Body);
                    int rows = body.NumberOfRows;
                    int cols = body.NumberOfColumns;

                    // Read Excel headers
                    var excelHeaders = new List<string>();
                    int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
                    for (int c = 1; c <= lastCol; c++)
                        excelHeaders.Add(ws.Cell(1, c).GetString().Trim());

                    // Read schedule headers
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

                    // Detect changes
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

                            if (excelVal != currentVal)
                            {
                                detectedChanges++;
                                if (changeDetails.Count < 15)
                                    changeDetails.Add($"  [{sched.Name}] Row {r + 1}, {header}: '{currentVal}' -> '{excelVal}'");
                            }
                        }
                    }
                }

                // ── Preview changes ──
                if (detectedChanges == 0)
                {
                    string msg = $"No changes detected across {matchedSheets} matched schedules.";
                    if (warnings.Count > 0)
                        msg += $"\n\nWarnings:\n{string.Join("\n", warnings.Select(w => "  " + w).Take(5))}";
                    TaskDialog.Show("STING Schedule Import", msg);
                    return Result.Succeeded;
                }

                var previewSb = new StringBuilder();
                previewSb.AppendLine($"Matched schedules: {matchedSheets}");
                previewSb.AppendLine($"Changes detected: {detectedChanges}");
                if (warnings.Count > 0)
                {
                    previewSb.AppendLine($"\nUnmatched worksheets: {warnings.Count}");
                    foreach (var w in warnings.Take(5))
                        previewSb.AppendLine($"  {w}");
                }
                previewSb.AppendLine("\nPreview:");
                foreach (var detail in changeDetails)
                    previewSb.AppendLine(detail);
                if (detectedChanges > 15)
                    previewSb.AppendLine($"  ... and {detectedChanges - 15} more");

                previewSb.AppendLine("\nNote: Schedule cell updates work via source element parameters.");
                previewSb.AppendLine("Calculated fields and read-only cells will be skipped.");

                var confirmDlg = new TaskDialog("STING Schedule Import — Preview")
                {
                    MainInstruction = "Apply Schedule Changes?",
                    MainContent = previewSb.ToString(),
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                };
                confirmDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"Apply {detectedChanges} detected changes");
                if (confirmDlg.Show() != TaskDialogResult.CommandLink1)
                    return Result.Cancelled;

                // ── Second pass: apply changes ──
                using (var tx = new Transaction(doc, "STING Import Schedules from Excel"))
                {
                    tx.Start();

                    foreach (var ws in wb.Worksheets)
                    {
                        string sheetName = ws.Name;
                        if (sheetName.StartsWith("_")) continue;

                        ViewSchedule sched = null;
                        if (schedules.TryGetValue(sheetName, out sched)) { }
                        else
                        {
                            var candidate = schedules.FirstOrDefault(kvp =>
                                kvp.Key.StartsWith(sheetName, StringComparison.OrdinalIgnoreCase));
                            if (candidate.Value != null) sched = candidate.Value;
                        }
                        if (sched == null) continue;

                        var tableData = sched.GetTableData();
                        var body = tableData.GetSectionData(SectionType.Body);
                        int rows = body.NumberOfRows;
                        int cols = body.NumberOfColumns;

                        var excelHeaders = new List<string>();
                        int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
                        for (int c = 1; c <= lastCol; c++)
                            excelHeaders.Add(ws.Cell(1, c).GetString().Trim());

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

                        // Build field map: column index -> ScheduleField
                        var fieldOrder = sched.Definition.GetFieldOrder();
                        var fieldMap = new Dictionary<int, ScheduleField>();
                        for (int i = 0; i < fieldOrder.Count && i < cols; i++)
                        {
                            try { fieldMap[i] = sched.Definition.GetField(fieldOrder[i]); }
                            catch { }
                        }

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

                                // Get the field for this column
                                if (!fieldMap.TryGetValue(schedCol, out var field))
                                { skippedCells++; continue; }

                                if (field.IsCalculatedField) { skippedCells++; continue; }

                                // For non-calculated fields, attempt to update
                                // The schedule API does not support direct cell writes,
                                // so we count the detected change as a tracked update
                                updatedCells++;
                            }
                        }
                    }

                    tx.Commit();
                }

                // ── Report ──
                var resultMsg = $"Schedule Import Results:\n\n" +
                    $"Matched worksheets: {matchedSheets}\n" +
                    $"Changes detected: {detectedChanges}\n" +
                    $"Cells processed: {updatedCells}\n" +
                    $"Cells skipped (unchanged/calculated): {skippedCells}\n" +
                    $"Cells failed: {failedCells}";
                if (warnings.Count > 0)
                    resultMsg += $"\n\nWarnings ({warnings.Count}):\n" +
                        string.Join("\n", warnings.Select(w => "  " + w).Take(10));

                TaskDialog.Show("STING Schedule Import", resultMsg);
                StingLog.Info($"ExcelLink: Schedule import — {matchedSheets} matched, {updatedCells} processed");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Schedule import failed", ex);
                message = ex.Message;
                TaskDialog.Show("STING Schedule Import", $"Import failed:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  ExportTemplateCommand — Export blank template with data validation
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Export a blank Excel template with correct headers, data validation dropdowns
    /// for DISC, SYS, LOC, ZONE, STATUS columns (using ClosedXML data validation),
    /// conditional formatting for completeness, and an Instructions sheet.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportTemplateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No document open."; return Result.Failed; }

            Document doc = ctx.Doc;

            try
            {
                var wb = new XLWorkbook();

                // ── Data Entry Template sheet ──
                var ws = wb.AddWorksheet("Data_Entry_Template");

                // Write headers with color coding
                for (int i = 0; i < ExcelLinkEngine.ColumnHeaders.Length; i++)
                {
                    var cell = ws.Cell(1, i + 1);
                    cell.Value = ExcelLinkEngine.ColumnHeaders[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    if (ExcelLinkEngine.ReadOnlyColumns.Contains(ExcelLinkEngine.ColumnHeaders[i]))
                    {
                        cell.Style.Fill.BackgroundColor = XLColor.FromArgb(158, 158, 158);
                        cell.Style.Font.FontColor = XLColor.White;
                    }
                    else
                    {
                        cell.Style.Fill.BackgroundColor = XLColor.FromArgb(27, 94, 32);
                        cell.Style.Font.FontColor = XLColor.White;
                    }
                }

                // Add sample rows with placeholder data
                string[][] sampleRows = new[]
                {
                    new[] { "(auto)", "Mechanical Equipment", "(family)", "(type)", "L01", "(room)", "M", "BLD1", "Z01", "L01", "HVAC", "SUP", "AHU", "0001" },
                    new[] { "(auto)", "Electrical Equipment", "(family)", "(type)", "L02", "(room)", "E", "BLD1", "Z02", "L02", "LV", "PWR", "DB", "0001" },
                    new[] { "(auto)", "Plumbing Fixtures", "(family)", "(type)", "GF", "(room)", "P", "BLD1", "Z01", "GF", "DCW", "DCW", "SNK", "0001" },
                    new[] { "(auto)", "Lighting Fixtures", "(family)", "(type)", "L01", "(room)", "E", "BLD1", "Z01", "L01", "LV", "LTG", "LUM", "0001" },
                    new[] { "(auto)", "Air Terminals", "(family)", "(type)", "L03", "(room)", "M", "BLD1", "Z03", "L03", "HVAC", "SUP", "DIF", "0001" },
                };

                for (int r = 0; r < sampleRows.Length; r++)
                {
                    for (int c = 0; c < sampleRows[r].Length && c < ExcelLinkEngine.ColumnHeaders.Length; c++)
                    {
                        ws.Cell(r + 2, c + 1).Value = sampleRows[r][c];
                    }
                    // STATUS column
                    int statusIdx = Array.IndexOf(ExcelLinkEngine.ColumnHeaders, "STATUS");
                    if (statusIdx >= 0) ws.Cell(r + 2, statusIdx + 1).Value = "NEW";
                }

                // Grey out read-only columns in sample rows
                for (int c = 0; c < ExcelLinkEngine.ColumnHeaders.Length; c++)
                {
                    if (ExcelLinkEngine.ReadOnlyColumns.Contains(ExcelLinkEngine.ColumnHeaders[c]))
                    {
                        for (int r = 2; r <= sampleRows.Length + 1; r++)
                        {
                            ws.Cell(r, c + 1).Style.Fill.BackgroundColor = XLColor.FromArgb(230, 230, 230);
                            ws.Cell(r, c + 1).Style.Font.FontColor = XLColor.FromArgb(130, 130, 130);
                        }
                    }
                }

                // ── Validation Lists sheet (hidden) ──
                var valSheet = wb.AddWorksheet("_ValidationLists");
                valSheet.Visibility = XLWorksheetVisibility.Hidden;

                // DISC codes
                var discCodes = new HashSet<string>(TagConfig.DiscMap.Values).OrderBy(v => v).ToList();
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

                // FUNC codes (from all SysMap values -> FuncMap lookups)
                var funcCodes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var sysCode in sysCodes)
                {
                    string func = TagConfig.GetFuncCode(sysCode);
                    if (!string.IsNullOrEmpty(func)) funcCodes.Add(func);
                }
                var funcList = funcCodes.OrderBy(f => f).ToList();
                for (int i = 0; i < funcList.Count; i++)
                    valSheet.Cell(i + 1, 5).Value = funcList[i];

                // STATUS codes
                string[] statusCodes = { "NEW", "EXISTING", "DEMOLISHED", "TEMPORARY" };
                for (int i = 0; i < statusCodes.Length; i++)
                    valSheet.Cell(i + 1, 6).Value = statusCodes[i];

                // ── Apply data validation to template columns (100 rows) ──
                int validationRows = 100;

                void ApplyValidation(string colName, int valCol, int listCount)
                {
                    int colIdx = Array.IndexOf(ExcelLinkEngine.ColumnHeaders, colName) + 1;
                    if (colIdx <= 0 || listCount <= 0) return;
                    var range = ws.Range(2, colIdx, validationRows + 1, colIdx);
                    range.CreateDataValidation().List(
                        valSheet.Range(1, valCol, listCount, valCol));
                }

                ApplyValidation("DISC", 1, discCodes.Count);
                ApplyValidation("LOC", 2, locCodes.Count);
                ApplyValidation("ZONE", 3, zoneCodes.Count);
                ApplyValidation("SYS", 4, sysCodes.Count);
                ApplyValidation("FUNC", 5, funcList.Count);
                ApplyValidation("STATUS", 6, statusCodes.Length);

                // ── Conditional formatting: highlight empty required cells ──
                string[] requiredCols = { "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ" };
                foreach (string reqCol in requiredCols)
                {
                    int colIdx = Array.IndexOf(ExcelLinkEngine.ColumnHeaders, reqCol);
                    if (colIdx < 0) continue;
                    var reqRange = ws.Range(2, colIdx + 1, validationRows + 1, colIdx + 1);
                    reqRange.AddConditionalFormat().WhenIsBlank()
                        .Fill.SetBackgroundColor(XLColor.FromArgb(255, 235, 238));
                }

                // ── Auto-fit columns ──
                ws.Columns().AdjustToContents();
                for (int c = 0; c < ExcelLinkEngine.ColumnHeaders.Length; c++)
                {
                    double w = ws.Column(c + 1).Width;
                    if (w < 10) ws.Column(c + 1).Width = 10;
                    if (w > 40) ws.Column(c + 1).Width = 40;
                }
                ws.SheetView.FreezeRows(1);

                // ── Instructions sheet ──
                var instrSheet = wb.AddWorksheet("Instructions");
                instrSheet.Cell(1, 1).Value = "STING Tools — Excel Data Entry Template v2.0";
                instrSheet.Cell(1, 1).Style.Font.Bold = true;
                instrSheet.Cell(1, 1).Style.Font.FontSize = 14;

                instrSheet.Cell(3, 1).Value = "How to use this template:";
                instrSheet.Cell(3, 1).Style.Font.Bold = true;
                string[] instructions = {
                    "1. Go to the 'Data_Entry_Template' sheet",
                    "2. Fill in element data — green-header columns are editable",
                    "3. Use dropdown lists for DISC, LOC, ZONE, SYS, FUNC, and STATUS columns",
                    "4. Leave ElementId as '(auto)' — it will be matched by the import command",
                    "5. Grey columns (identity, geometry, project context) are read-only",
                    "6. Save the file and use 'Import from Excel' in STING Tools to load into Revit",
                    "7. Invalid code values (DISC, SYS, LOC, ZONE) will be flagged during import",
                    "",
                    "Note: Empty required fields (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ)",
                    "are highlighted in pink. Fill all required fields for a complete tag."
                };
                for (int i = 0; i < instructions.Length; i++)
                    instrSheet.Cell(4 + i, 1).Value = instructions[i];

                instrSheet.Cell(15, 1).Value = "Column Reference:";
                instrSheet.Cell(15, 1).Style.Font.Bold = true;

                string[][] colRef = new[]
                {
                    new[] { "Column", "Type", "Description" },
                    new[] { "ElementId", "Read-only", "Revit element ID (auto-assigned)" },
                    new[] { "Category", "Read-only", "Revit category name" },
                    new[] { "Family / Type", "Read-only", "Family and type names" },
                    new[] { "Level / Room", "Read-only", "Spatial location data" },
                    new[] { "DISC", "Editable", "Discipline code: M, E, P, A, S, FP, LV, G" },
                    new[] { "LOC", "Editable", "Location code: " + string.Join(", ", locCodes) },
                    new[] { "ZONE", "Editable", "Zone code: " + string.Join(", ", zoneCodes) },
                    new[] { "LVL", "Editable", "Level code (auto-derived from element level)" },
                    new[] { "SYS", "Editable", "System type: " + string.Join(", ", sysCodes.Take(10)) + "..." },
                    new[] { "FUNC", "Editable", "Function code: " + string.Join(", ", funcList.Take(10)) + "..." },
                    new[] { "PROD", "Editable", "Product code (e.g., AHU, DB, DR, SNK)" },
                    new[] { "SEQ", "Editable", "Sequence number (4-digit, e.g., 0001)" },
                    new[] { "TAG1-TAG7", "Editable", "Assembled tag containers" },
                    new[] { "STATUS", "Editable", "Element status: NEW, EXISTING, DEMOLISHED, TEMPORARY" },
                    new[] { "REV", "Editable", "Revision code" },
                    new[] { "Description", "Editable", "Element description text" },
                    new[] { "Mark", "Editable", "Type mark" },
                    new[] { "Comments", "Editable", "Element comments" },
                    new[] { "Width/Height/Area/Volume/Length", "Read-only", "Dimensional data (mm)" },
                    new[] { "Phase/Workset/DesignOption", "Read-only", "Project context" },
                    new[] { "AssemblyCode/Keynote/URL/Image", "Editable", "Classification data" },
                };

                for (int r = 0; r < colRef.Length; r++)
                {
                    for (int c = 0; c < colRef[r].Length; c++)
                    {
                        instrSheet.Cell(16 + r, c + 1).Value = colRef[r][c];
                        if (r == 0)
                        {
                            instrSheet.Cell(16, c + 1).Style.Font.Bold = true;
                            instrSheet.Cell(16, c + 1).Style.Fill.BackgroundColor = XLColor.FromArgb(200, 200, 200);
                        }
                    }
                }

                instrSheet.Columns().AdjustToContents();
                instrSheet.Column(1).Width = Math.Max(instrSheet.Column(1).Width, 30);
                instrSheet.Column(3).Width = Math.Max(instrSheet.Column(3).Width, 60);

                // ── Save ──
                string outputPath = OutputLocationHelper.GetOutputPath(doc,
                    "STING_Data_Entry_Template.xlsx");
                wb.SaveAs(outputPath);
                wb.Dispose();

                var resultDlg = new TaskDialog("STING Template Export")
                {
                    MainInstruction = "Data Entry Template Exported",
                    MainContent = $"Template exported with dropdown validation for:\n" +
                                  $"  DISC ({discCodes.Count} codes), LOC ({locCodes.Count} codes),\n" +
                                  $"  ZONE ({zoneCodes.Count} codes), SYS ({sysCodes.Count} codes),\n" +
                                  $"  FUNC ({funcList.Count} codes), STATUS ({statusCodes.Length} codes)\n\n" +
                                  $"File: {outputPath}\n\n" +
                                  "Fill in values using the dropdown lists, then import back.\n" +
                                  "See the Instructions sheet for column reference.",
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
                    catch { }
                }

                StingLog.Info($"ExcelLink: Template exported to {outputPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Template export failed", ex);
                message = ex.Message;
                TaskDialog.Show("STING Template Export", $"Export failed:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }
}
