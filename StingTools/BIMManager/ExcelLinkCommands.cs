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
using StingTools.UI;

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
        // ── Static FUNC-PROD cross-validation sets (allocated once, used by ValidateTokenCrossRefs) ──
        private static readonly HashSet<string> SanitaryProds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "WC", "BAS", "SHR", "URN", "BDT", "SNK", "BTH" };
        private static readonly HashSet<string> PlumbingProds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "WC", "BAS", "SHR", "URN", "BDT", "SNK", "BTH", "TAP", "TMV", "CIS" };
        private static readonly HashSet<string> HvacProds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "AHU", "FCU", "VAV", "CHR", "BLR", "FAN", "GRL", "DIF", "ATU" };
        private static readonly HashSet<string> ElectricalProds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "DB", "MSB", "TFR", "GEN", "UPS", "SWT", "SKT", "LUM", "EMR" };

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
                ["REV"]          = () => ParamRegistry.REV,
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
        // Phase 74: Cache validation sets — avoids 35K+ HashSet allocations per 5K-element import
        private static HashSet<string> _cachedValidDisc;
        private static HashSet<string> _cachedValidFunc;
        private static HashSet<string> _cachedValidProd;
        private static HashSet<string> EnsureValidDisc() => _cachedValidDisc ??= new HashSet<string>(TagConfig.DiscMap.Values, StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> EnsureValidFunc() => _cachedValidFunc ??= new HashSet<string>(TagConfig.FuncMap.Values, StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> EnsureValidProd() => _cachedValidProd ??= new HashSet<string>(TagConfig.ProdMap.Values, StringComparer.OrdinalIgnoreCase);

        /// <summary>DI-02 FIX: Invalidate cached validation sets when config changes (e.g., after TagConfig.LoadFromFile).
        /// Should be called from TagConfig.LoadFromFile() or any config reload path.</summary>
        internal static void InvalidateValidationCache()
        {
            _cachedValidDisc = null;
            _cachedValidFunc = null;
            _cachedValidProd = null;
        }

        internal static string ValidateValue(string column, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null; // empty is allowed

            switch (column)
            {
                case "DISC":
                    if (!EnsureValidDisc().Contains(value))
                        return $"DISC '{value}' not in valid codes: {string.Join(", ", EnsureValidDisc().OrderBy(v => v))}";
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
                case "FUNC":
                    if (!EnsureValidFunc().Contains(value))
                        return $"FUNC '{value}' not in valid codes: {string.Join(", ", EnsureValidFunc().OrderBy(v => v))}";
                    break;
                case "PROD":
                    if (!EnsureValidProd().Contains(value))
                        return $"PROD '{value}' not in valid codes: {string.Join(", ", EnsureValidProd().OrderBy(v => v).Take(20))}...";
                    break;
                case "SEQ":
                    if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{1,6}$"))
                        return $"SEQ '{value}' must be numeric (1-6 digits)";
                    break;
            }
            return null;
        }

        /// <summary>
        /// GAP-BIM-001: Cross-validate token combinations (FUNC must be valid for SYS, PROD for DISC).
        /// Returns error message or null if valid.
        /// </summary>
        internal static string ValidateTokenCrossRefs(string disc, string sys, string func, string prod)
        {
            // FUNC-SYS cross-validation: FUNC must be valid for the given SYS per CIBSE/Uniclass
            if (!string.IsNullOrEmpty(sys) && !string.IsNullOrEmpty(func))
            {
                var validFuncs = ISO19650Validator.GetValidFuncsForSys(sys);
                if (validFuncs != null && validFuncs.Count > 0 && !validFuncs.Contains(func))
                    return $"FUNC '{func}' is not valid for SYS '{sys}'. Valid: {string.Join(", ", validFuncs)}";
            }

            // DISC-SYS cross-validation: SYS must belong to correct discipline
            if (!string.IsNullOrEmpty(disc) && !string.IsNullOrEmpty(sys))
            {
                bool sysBelongsToDisc = false;
                foreach (var kv in TagConfig.SysMap)
                {
                    if (kv.Key == sys)
                    {
                        // Check if any category in this SYS maps to the given DISC
                        foreach (string cat in kv.Value)
                        {
                            if (TagConfig.DiscMap.TryGetValue(cat, out string catDisc) && catDisc == disc)
                            { sysBelongsToDisc = true; break; }
                        }
                        break;
                    }
                }
                // Only warn if we have data to validate against (some SYS codes are universal)
                if (!sysBelongsToDisc && TagConfig.SysMap.ContainsKey(sys))
                    return $"SYS '{sys}' does not typically belong to discipline '{disc}'";
            }

            // FUNC-PROD cross-validation: certain FUNC codes are incompatible with certain PROD codes
            if (!string.IsNullOrEmpty(func) && !string.IsNullOrEmpty(prod))
            {
                // SUP (supply) is incompatible with sanitary products
                if (func == "SUP" && SanitaryProds.Contains(prod))
                    return $"FUNC=SUP incompatible with sanitary PROD={prod}";

                // PWR (power) is incompatible with plumbing products
                if (func == "PWR" && PlumbingProds.Contains(prod))
                    return $"FUNC=PWR incompatible with plumbing PROD={prod}";

                // SAN (sanitary) is incompatible with HVAC products
                if (func == "SAN" && HvacProds.Contains(prod))
                    return $"FUNC=SAN incompatible with HVAC PROD={prod}";

                // HTG (heating) / CLG (cooling) incompatible with electrical products
                if ((func == "HTG" || func == "CLG") && ElectricalProds.Contains(prod))
                    return $"FUNC={func} incompatible with electrical PROD={prod}";

                // LTG (lighting) incompatible with plumbing products
                if (func == "LTG" && PlumbingProds.Contains(prod))
                    return $"FUNC=LTG incompatible with plumbing PROD={prod}";
            }

            return null;
        }

        /// <summary>
        /// Validate all changes and return validation warnings.
        /// Now includes cross-token validation for FUNC-SYS and DISC-SYS consistency.
        /// </summary>
        internal static List<ValidationWarning> ValidateChanges(List<ChangeRecord> changes)
        {
            var warnings = new List<ValidationWarning>();

            // Phase 1: Individual token validation
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

            // Phase 2: Cross-token validation (GAP-BIM-001)
            // Group changes by element to validate token combinations
            foreach (var group in changes.Where(c => c.Status == ChangeStatus.Changed)
                .GroupBy(c => c.ElementId))
            {
                string disc = group.FirstOrDefault(c => c.Column == "DISC")?.NewValue;
                string sys = group.FirstOrDefault(c => c.Column == "SYS")?.NewValue;
                string func = group.FirstOrDefault(c => c.Column == "FUNC")?.NewValue;
                string prod = group.FirstOrDefault(c => c.Column == "PROD")?.NewValue;

                // Only cross-validate if at least 2 related tokens are being changed
                int changedTokenCount = new[] { disc, sys, func, prod }.Count(v => v != null);
                if (changedTokenCount >= 2)
                {
                    string crossError = ValidateTokenCrossRefs(disc ?? "", sys ?? "", func ?? "", prod ?? "");
                    if (crossError != null)
                    {
                        warnings.Add(new ValidationWarning
                        {
                            ElementId = group.Key,
                            Column = "CROSS_VALIDATION",
                            Value = $"DISC={disc},SYS={sys},FUNC={func}",
                            Message = crossError
                        });
                    }
                }

                // BIM-EXCEL-CROSS-01: FUNC↔SYS validity matrix (Phase148Engine).
                // Catches invalid combinations like FUNC=PWR on SYS=HVAC that the
                // legacy ValidateTokenCrossRefs path doesn't cover.
                if (!string.IsNullOrEmpty(sys) && !string.IsNullOrEmpty(func))
                {
                    var hits = FuncSysValidator.Validate(new[] {
                        (row: 0, tagId: group.Key.ToString(), sys: sys, func: func)
                    });
                    foreach (var hit in hits)
                    {
                        warnings.Add(new ValidationWarning
                        {
                            ElementId = group.Key,
                            Column = "FUNC_SYS_MATRIX",
                            Value = $"SYS={sys},FUNC={func}",
                            Message = hit.Reason
                        });
                    }
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

            // All taggable elements in the project.
            // S1.4 (N-G1): pre-filter with ElementMulticategoryFilter on
            // AllCategoryEnums so .Where() only runs on tagged categories,
            // not the entire document.
            var allElements = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(SharedParamGuids.AllCategoryEnums))
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; }
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
            catch (Exception ex) { StingLog.Warn($"element may not have spatial context: {ex.Message}"); }
            row["Room"] = roomName;

            // ── Parameter columns (tokens, tags, status, description, etc.) ──
            foreach (var kvp in ParamColumnMap)
            {
                string paramName = null;
                try { paramName = kvp.Value(); } catch (Exception ex) { StingLog.Warn($"Param column map evaluation failed for {kvp.Key}: {ex.Message}"); }
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
            catch (Exception ex) { StingLog.Warn($"Phase name lookup failed: {ex.Message}"); }
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
            catch (Exception ex) { StingLog.Warn($"Workset name lookup failed: {ex.Message}"); }
            row["Workset"] = worksetName;

            string designOption = "";
            try
            {
                var doParam = el.get_Parameter(BuiltInParameter.DESIGN_OPTION_ID);
                if (doParam != null && doParam.HasValue)
                    designOption = doParam.AsValueString() ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"Design option lookup failed: {ex.Message}"); }
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
            metaWs.Cell(8, 1).Value = "ProjectGUID";
            metaWs.Cell(8, 2).Value = doc.ProjectInformation?.UniqueId ?? "";
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
                    catch (Exception ex) { StingLog.Warn($"Schedule category name lookup failed: {ex.Message}"); }
                    schedWs.Cell(row, 2).Value = catName;

                    int fieldCount = 0;
                    try { fieldCount = vs.Definition.GetFieldCount(); } catch (Exception ex) { StingLog.Warn($"Schedule field count lookup failed: {ex.Message}"); }
                    schedWs.Cell(row, 3).Value = fieldCount;

                    int rowCount = 0;
                    try
                    {
                        var tableData = vs.GetTableData();
                        var body = tableData.GetSectionData(SectionType.Body);
                        rowCount = body.NumberOfRows;
                    }
                    catch (Exception ex) { StingLog.Warn($"Schedule row count lookup failed: {ex.Message}"); }
                    schedWs.Cell(row, 4).Value = rowCount;

                    bool hasFilters = false;
                    try { hasFilters = vs.Definition.GetFilterCount() > 0; } catch (Exception ex) { StingLog.Warn($"Schedule filter count lookup failed: {ex.Message}"); }
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
            // LOGIC-06: Case-insensitive header mapping — "Disc" matches "DISC"
            var headerMap = new Dictionary<int, string>();
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int c = 1; c <= lastCol; c++)
            {
                string header = ws.Cell(1, c).GetString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(header))
                    headerMap[c] = header.ToUpperInvariant();
            }

            // Verify ElementId column exists (case-insensitive)
            int? elementIdCol = headerMap.FirstOrDefault(kv =>
                string.Equals(kv.Value, "ELEMENTID", StringComparison.OrdinalIgnoreCase)).Key;
            if (elementIdCol == null || elementIdCol == 0)
                throw new InvalidOperationException("ElementId column not found in header row.");

            // BIM-EXCEL-STREAM-01: Raised cap from 10K to 500K since batched processing handles memory
            const int MaxImportRows = 500000;
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            int dataRows = lastRow - 1;
            if (dataRows > MaxImportRows)
            {
                StingLog.Warn($"Excel import limited to {MaxImportRows} rows (file has {dataRows})");
                lastRow = MaxImportRows + 1; // Clamp to max
            }

            // Read data rows
            for (int r = 2; r <= lastRow; r++)
            {
                // ClosedXML.GetString() returns null for truly-empty cells in some paths;
                // null-coalesce before Trim to avoid NRE on empty rows.
                string idStr = (ws.Cell(r, elementIdCol.Value).GetString() ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(idStr)) continue;
                if (!long.TryParse(idStr, out long elementId)) continue;

                var rowData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in headerMap)
                {
                    rowData[kvp.Value] = ws.Cell(r, kvp.Key).GetString() ?? string.Empty;
                }

                result[elementId] = rowData;
            }

            return result;
        }

        /// <summary>
        /// BIM-EXCEL-STREAM-01: Streaming Excel import that reads and processes rows in batches
        /// of TagConfig.ExcelImportBatchSize. Avoids loading all rows into a single dictionary.
        /// Returns total row count and yields batches via callback for per-batch processing.
        /// </summary>
        internal static StreamingImportResult StreamingImport(
            string path, Document doc, bool forceInvalid,
            UI.StingProgressDialog progress = null)
        {
            var result = new StreamingImportResult();

            // Phase 165 (BIM-EXCEL-STREAM-01 hardening): the underlying ClosedXML
            // workbook still materialises the whole package into memory. Catch
            // OOM here and surface guidance — the alternative would be a full
            // OpenXmlReader rewrite, which is deferred until ClosedXML 1.x.
            XLWorkbook wb;
            try
            {
                wb = new XLWorkbook(path);
            }
            catch (OutOfMemoryException oom)
            {
                StingLog.Error($"Excel streaming import OOM at workbook load: {path}", oom);
                throw new InvalidOperationException(
                    "Excel file too large to load even in streaming mode. " +
                    "Split the workbook into smaller files (e.g. one sheet per discipline) " +
                    "and re-run the import.", oom);
            }

            using var _ = wb;
            var ws = wb.Worksheet("STING Data");
            if (ws == null)
            {
                ws = wb.Worksheets.FirstOrDefault(w => !w.Name.StartsWith("_"));
                if (ws == null)
                    throw new InvalidOperationException("No data worksheets found in the Excel file.");
            }

            // LOGIC-06: Case-insensitive header mapping
            var headerMap = new Dictionary<int, string>();
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int c = 1; c <= lastCol; c++)
            {
                string header = ws.Cell(1, c).GetString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(header))
                    headerMap[c] = header.ToUpperInvariant();
            }

            int? elementIdCol = headerMap.FirstOrDefault(kv =>
                string.Equals(kv.Value, "ELEMENTID", StringComparison.OrdinalIgnoreCase)).Key;
            if (elementIdCol == null || elementIdCol == 0)
                throw new InvalidOperationException("ElementId column not found in header row.");

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            int totalDataRows = lastRow - 1;
            result.TotalRowsInFile = totalDataRows;

            int batchSize = TagConfig.ExcelImportBatchSize;
            var currentBatch = new Dictionary<long, Dictionary<string, string>>();
            int batchNum = 0;
            int rowsProcessed = 0;

            // Token params for tag rebuild detection
            var tokenParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC, ParamRegistry.PROD
            };

            // Pre-load pipeline resources once (not per batch)
            var formulas = TagPipelineHelper.LoadFormulas();
            var gridLines = TagPipelineHelper.LoadGridLines(doc);

            for (int r = 2; r <= lastRow; r++)
            {
                string idStr = ws.Cell(r, elementIdCol.Value).GetString().Trim();
                if (string.IsNullOrEmpty(idStr)) continue;
                if (!long.TryParse(idStr, out long elementId)) continue;

                var rowData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in headerMap)
                {
                    rowData[kvp.Value] = ws.Cell(r, kvp.Key).GetString();
                }
                currentBatch[elementId] = rowData;
                rowsProcessed++;

                // Process batch when full or at end of file
                if (currentBatch.Count >= batchSize || r == lastRow)
                {
                    batchNum++;
                    if (progress != null)
                    {
                        progress.Increment(
                            $"Batch {batchNum}: processing {currentBatch.Count} rows...");
                        if (progress.IsCancelled)
                        {
                            result.WasCancelled = true;
                            break;
                        }
                    }

                    // Compute + apply this batch
                    ProcessImportBatch(doc, currentBatch, tokenParams, formulas, gridLines,
                        forceInvalid, result);

                    currentBatch = new Dictionary<long, Dictionary<string, string>>();
                }
            }

            result.BatchCount = batchNum;
            return result;
        }

        /// <summary>Process a single batch of Excel rows: compute changes, apply, rebuild tags.</summary>
        private static void ProcessImportBatch(
            Document doc,
            Dictionary<long, Dictionary<string, string>> batchData,
            HashSet<string> tokenParams,
            List<Temp.FormulaEngine.FormulaDefinition> formulas,
            List<Grid> gridLines,
            bool forceInvalid,
            StreamingImportResult result)
        {
            var changes = ComputeChanges(doc, batchData);
            var actualChanges = changes.Where(c => c.Status == ChangeStatus.Changed).ToList();
            result.AllChanges.AddRange(changes);

            if (actualChanges.Count == 0) return;

            // Validate
            var warnings = ValidateChanges(changes);
            result.AllValidationWarnings.AddRange(warnings);

            // Apply parameter changes in a transaction
            using (var trans = new Transaction(doc, "STING Excel Import — Batch"))
            {
                trans.Start();
                try
                {
                    var (applied, skipped, failed) = ApplyChanges(doc, changes, trans, forceInvalid);
                    result.Applied += applied;
                    result.Skipped += skipped;
                    result.Failed += failed;
                    trans.Commit();
                }
                catch (Exception ex)
                {
                    if (trans.HasStarted()) trans.RollBack();
                    StingLog.Error($"Excel streaming import batch failed", ex);
                    return;
                }
            }

            // Rebuild tags for token-changed elements
            var affectedIds = changes
                .Where(c => c.Status == ChangeStatus.Applied && tokenParams.Contains(c.ParamName))
                .Select(c => new ElementId(c.ElementId))
                .Distinct()
                .ToList();

            if (affectedIds.Count > 0)
            {
                using (var rebuildTrans = new Transaction(doc, "STING Excel Import — Tag Rebuild"))
                {
                    rebuildTrans.Start();
                    try
                    {
                        var (tagIndex, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
                        string cachedRev = PhaseAutoDetect.DetectProjectRevision(doc);

                        foreach (var eid in affectedIds)
                        {
                            Element el = doc.GetElement(eid);
                            if (el == null) continue;
                            try
                            {
                                string catName = ParameterHelpers.GetCategoryName(el);

                                // Audit trail
                                try
                                {
                                    string prevTag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                                    if (!string.IsNullOrEmpty(prevTag))
                                    {
                                        ParameterHelpers.SetString(el, "ASS_TAG_PREV_TXT", prevTag, overwrite: true);
                                        ParameterHelpers.SetString(el, "ASS_TAG_MODIFIED_DT",
                                            DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), overwrite: true);
                                    }
                                }
                                catch (Exception atEx) { StingLog.Warn($"Excel streaming audit trail for {el.Id}: {atEx.Message}"); }

                                try { TokenAutoPopulator.TypeTokenInherit(doc, el); }
                                catch (Exception tiEx) { StingLog.Warn($"Excel streaming TypeTokenInherit for {el.Id}: {tiEx.Message}"); }

                                try
                                {
                                    var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
                                    if (popCtx != null && popCtx.IsValid())
                                        TokenAutoPopulator.PopulateAll(doc, el, popCtx, overwrite: false);
                                }
                                catch (Exception paEx) { StingLog.Warn($"Excel streaming PopulateAll for {el.Id}: {paEx.Message}"); }

                                try { NativeParamMapper.MapAll(doc, el); }
                                catch (Exception nmEx) { StingLog.Warn($"Excel streaming NativeMapper for {el.Id}: {nmEx.Message}"); }

                                // Formula evaluation
                                if (formulas != null && formulas.Count > 0)
                                {
                                    try
                                    {
                                        foreach (var formula in formulas)
                                        {
                                            Parameter fp = el.LookupParameter(formula.ParameterName);
                                            if (fp == null || fp.IsReadOnly) continue;
                                            var fCtx = Temp.FormulaEngine.BuildContext(el, formula);
                                            if (fCtx == null) continue;
                                            if (formula.DataType == "TEXT")
                                            {
                                                string fResult = Temp.FormulaEngine.EvaluateText(formula.Expression, fCtx);
                                                if (fResult != null && fp.StorageType == StorageType.String)
                                                    fp.Set(fResult);
                                            }
                                            else
                                            {
                                                double? fResult = Temp.FormulaEngine.EvaluateNumeric(formula.Expression, fCtx);
                                                if (fResult.HasValue && !double.IsNaN(fResult.Value) && !double.IsInfinity(fResult.Value))
                                                    Temp.FormulaEngine.WriteNumericResult(fp, fResult.Value);
                                            }
                                        }
                                    }
                                    catch (Exception fEx) { StingLog.Warn($"Excel streaming formula eval for {el.Id}: {fEx.Message}"); }
                                }

                                TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                                    skipComplete: false, tagIndex, TagCollisionMode.Overwrite, null,
                                    cachedRev: cachedRev);
                                string[] tokenVals = ParamRegistry.ReadTokenValues(el);
                                ParamRegistry.WriteContainers(el, tokenVals, catName, overwrite: true);
                                TagConfig.WriteTag7All(doc, el, catName, tokenVals);

                                if (gridLines != null && gridLines.Count > 0)
                                {
                                    try
                                    {
                                        string gridRef = SpatialAutoDetect.GetGridRef(el, gridLines);
                                        if (!string.IsNullOrEmpty(gridRef))
                                            ParameterHelpers.SetIfEmpty(el, ParamRegistry.GRID_REF, gridRef);
                                    }
                                    catch (Exception grEx) { StingLog.Warn($"Excel streaming GridRef for {el.Id}: {grEx.Message}"); }
                                }

                                result.Rebuilt++;
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"Excel streaming tag rebuild failed for {eid}: {ex.Message}");
                            }
                        }
                        rebuildTrans.Commit();

                        try { TagConfig.SaveSeqSidecar(doc, seqCounters); }
                        catch (Exception ssEx) { StingLog.Warn($"Excel streaming SaveSeqSidecar: {ssEx.Message}"); }
                    }
                    catch (Exception ex)
                    {
                        if (rebuildTrans.HasStarted()) rebuildTrans.RollBack();
                        StingLog.Error("Excel streaming tag rebuild transaction failed", ex);
                    }
                }
            }
        }

        /// <summary>Result aggregator for streaming Excel import.</summary>
        internal class StreamingImportResult
        {
            public int TotalRowsInFile { get; set; }
            public int BatchCount { get; set; }
            public int Applied { get; set; }
            public int Skipped { get; set; }
            public int Failed { get; set; }
            public int Rebuilt { get; set; }
            public bool WasCancelled { get; set; }
            public List<ChangeRecord> AllChanges { get; } = new List<ChangeRecord>();
            public List<ValidationWarning> AllValidationWarnings { get; } = new List<ValidationWarning>();
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
                    try { paramName = colKvp.Value(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); continue; }
                    if (string.IsNullOrEmpty(paramName)) continue;

                    if (!excelRow.TryGetValue(columnName, out string excelValue))
                        continue;

                    excelValue = excelValue ?? "";
                    string modelValue = ParameterHelpers.GetString(el, paramName) ?? "";

                    // C-04 FIX: Skip empty Excel cells when model already has data.
                    // Prevents silent data loss when user exports, edits some cells,
                    // then re-imports without touching other columns (empty ≠ intentional clear).
                    // R4-B FIX: Handle "CLEAR" sentinel — user types CLEAR to intentionally empty a field.
                    if (excelValue.Equals("CLEAR", StringComparison.OrdinalIgnoreCase))
                        excelValue = "";
                    else if (string.IsNullOrEmpty(excelValue) && !string.IsNullOrEmpty(modelValue))
                        continue;

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

        /// <summary>HR-01: Read project GUID from STING_META metadata in exported workbook.</summary>
        internal static string ReadProjectGuid(string filePath)
        {
            try
            {
                using var wb = new XLWorkbook(filePath);
                if (wb.TryGetWorksheet("_STING_Metadata", out var meta))
                {
                    // Scan rows for ProjectGUID key
                    for (int r = 1; r <= 20; r++)
                    {
                        if (meta.Cell(r, 1).GetValue<string>() == "ProjectGUID")
                            return meta.Cell(r, 2).GetValue<string>() ?? "";
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ReadProjectGuid: {ex.Message}"); }
            return "";
        }

        /// <summary>
        /// Find the latest STING Excel export file in the output directory.
        /// HR-01: Prefers files matching current project GUID.
        /// </summary>
        internal static string FindLatestExport(Document doc)
        {
            string dir = OutputLocationHelper.GetOutputDirectory(doc);
            if (!Directory.Exists(dir)) return null;

            var allFiles = Directory.GetFiles(dir, "STING_Excel_Export_*.xlsx")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();

            if (allFiles.Count == 0) return null;

            // HR-01: Prefer files matching current project GUID
            string currentGuid = doc.ProjectInformation?.UniqueId ?? "";
            if (!string.IsNullOrEmpty(currentGuid))
            {
                foreach (var file in allFiles)
                {
                    try
                    {
                        string fileGuid = ReadProjectGuid(file);
                        if (fileGuid == currentGuid)
                            return file;
                    }
                    catch (Exception ex) { StingLog.Warn($"FindLatestExport GUID check: {ex.Message}"); }
                }
            }

            return allFiles.FirstOrDefault();
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
                            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true })?.Dispose();
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

                // HR-01: Cross-project import guard
                try
                {
                    string exportedGuid = ExcelLinkEngine.ReadProjectGuid(filePath);
                    string currentGuid = doc.ProjectInformation?.UniqueId ?? "";
                    if (!string.IsNullOrEmpty(exportedGuid) && exportedGuid != currentGuid)
                    {
                        var mismatch = new TaskDialog("STING Excel Import — Project Mismatch")
                        {
                            MainInstruction = "WARNING: Project Mismatch Detected",
                            MainContent = $"This Excel file was exported from a different project.\n\n" +
                                $"Exported project GUID: {exportedGuid}\n" +
                                $"Current project GUID:  {currentGuid}\n\n" +
                                "Importing may overwrite elements in the wrong project.",
                            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                            DefaultButton = TaskDialogResult.No,
                        };
                        if (mismatch.Show() == TaskDialogResult.No)
                        {
                            StingLog.Info("ExcelLink Import cancelled: project GUID mismatch");
                            return Result.Cancelled;
                        }
                        StingLog.Warn($"ExcelLink Import: user accepted project mismatch (exported={exportedGuid}, current={currentGuid})");
                    }
                }
                catch (Exception ex) { StingLog.Warn($"ExcelLink project GUID check: {ex.Message}"); }

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

                // BIM-EXCEL-STREAM-01: For large files, offer streaming import (batch processing)
                const int StreamingThreshold = 10000;
                if (excelData.Count > StreamingThreshold)
                {
                    var largeDlg = new TaskDialog("STING Excel Import — Large File")
                    {
                        MainInstruction = $"Large File Detected ({excelData.Count:N0} rows)",
                        MainContent = "This file contains more than 10,000 rows.\n\n" +
                                      "Streaming import processes data in batches with progress\n" +
                                      "tracking, cancellation support, and partial commit on failure.\n\n" +
                                      "Detailed preview loads all changes into a grid for review\n" +
                                      "but may be slower for very large files.",
                        CommonButtons = TaskDialogCommonButtons.Cancel,
                    };
                    largeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        "Streaming import (recommended)", "Batch processing with progress bar");
                    largeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        "Detailed preview", "View all changes in a grid before applying");
                    var largeResult = largeDlg.Show();

                    if (largeResult == TaskDialogResult.Cancel)
                        return Result.Cancelled;

                    if (largeResult == TaskDialogResult.CommandLink1)
                    {
                        // BIM-EXCEL-STREAM-01: Streaming import path — batched processing
                        var progress = UI.StingProgressDialog.Show("STING Excel Streaming Import", excelData.Count);
                        ExcelLinkEngine.StreamingImportResult streamResult;
                        try
                        {
                            streamResult = ExcelLinkEngine.StreamingImport(filePath, doc, false, progress);
                        }
                        finally
                        {
                            progress.Close();
                        }

                        StingAutoTagger.InvalidateContext();
                        ComplianceScan.InvalidateCache();
                        TagConfig.CheckComplianceGate(doc, "ExcelImport");

                        string sUserName = "";
                        try { sUserName = doc.Application.Username ?? ""; }
                        catch (Exception ex) { StingLog.Warn($"Username lookup failed: {ex.Message}"); }
                        if (streamResult.AllChanges.Count > 0)
                            ExcelLinkEngine.WriteChangeLog(filePath, streamResult.AllChanges, sUserName);

                        string cancelNote = streamResult.WasCancelled
                            ? "\n\n⚠ Import was cancelled — partial results applied." : "";
                        StingLog.Info($"ExcelLink Streaming Import: batches={streamResult.BatchCount}, " +
                            $"applied={streamResult.Applied}, skipped={streamResult.Skipped}, " +
                            $"failed={streamResult.Failed}, rebuilt={streamResult.Rebuilt}");
                        TaskDialog.Show("STING Excel Import",
                            $"Streaming Import Complete\n\n" +
                            $"Total rows: {streamResult.TotalRowsInFile:N0}\n" +
                            $"Batches processed: {streamResult.BatchCount}\n" +
                            $"Parameters updated: {streamResult.Applied}\n" +
                            $"Tags rebuilt: {streamResult.Rebuilt}\n" +
                            $"Skipped: {streamResult.Skipped}\n" +
                            $"Failures: {streamResult.Failed}" +
                            cancelNote +
                            "\n\nA change log CSV has been saved alongside the import file.");
                        return Result.Succeeded;
                    }
                    // else: fall through to existing detailed preview path
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

                // ── Preview changes in WPF DataGrid dialog ──
                int distinctElements = actualChanges.Select(c => c.ElementId).Distinct().Count();
                string subtitle = $"{actualChanges.Count} changes across {distinctElements} elements | " +
                                  $"{validationWarnings.Count} validation warnings";

                var previewDlg = new UI.StingDataGridDialog(
                    "STING Excel Import — Preview Changes", subtitle, 1020, 580);
                previewDlg.AddTextColumn("Element ID", "ElementId", 80);
                previewDlg.AddTextColumn("Column", "Column", 120);
                previewDlg.AddTextColumn("Current Value", "OldValue", 0, true, System.Windows.Media.Color.FromRgb(120, 120, 140));
                previewDlg.AddTextColumn("New Value", "NewValue", 0, true, System.Windows.Media.Color.FromRgb(0, 100, 50));
                previewDlg.AddTextColumn("Status", "StatusText", 80);
                previewDlg.AddTextColumn("Validation", "ValidationError", 140,
                    true, System.Windows.Media.Color.FromRgb(200, 50, 50));

                // Build preview rows
                var previewRows = actualChanges.Select(c => new
                {
                    c.ElementId,
                    c.Column,
                    c.OldValue,
                    c.NewValue,
                    StatusText = c.Status.ToString(),
                    ValidationError = c.ValidationError ?? ""
                }).ToList();

                // Add column filter
                var columns = previewRows.Select(r => r.Column).Distinct().OrderBy(c => c).ToList();
                columns.Insert(0, "All Columns");
                previewDlg.AddFilter("Column", columns, col =>
                {
                    if (col == "All Columns") previewDlg.RefreshItems(previewRows);
                    else previewDlg.RefreshItems(previewRows.Where(r => r.Column == col).ToList());
                });

                previewDlg.AddActionButton("Cancel", "Cancel");
                if (validationWarnings.Count > 0)
                    previewDlg.AddActionButton($"Force ALL ({actualChanges.Count})", "ForceAll");
                previewDlg.AddActionButton($"Apply ({actualChanges.Count} valid)", "Apply", true);

                previewDlg.SetItems(previewRows);
                previewDlg.SetStatus(subtitle);

                if (previewDlg.ShowDialog() != true)
                    return Result.Cancelled;
                string confirmAction = previewDlg.ResultAction;

                // Map result action to original logic
                TaskDialogResult confirmResult;
                if (confirmAction == "ForceAll")
                    confirmResult = TaskDialogResult.CommandLink2;
                else
                    confirmResult = TaskDialogResult.CommandLink1;

                bool forceInvalid = (confirmResult == TaskDialogResult.CommandLink2);

                // HR-06: Wrap parameter import + tag rebuild in TransactionGroup for atomic rollback
                int applied = 0, skipped = 0, failed = 0;
                int rebuilt = 0;
                using (var tg = new TransactionGroup(doc, "STING Excel Import + Tag Rebuild"))
                {
                tg.Start();
                try
                {
                using (var trans = new Transaction(doc, "STING Excel Import — Parameters"))
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

                // ── BUG-03: Rebuild tags for elements whose tokens were changed ──
                var tokenParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                    ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC, ParamRegistry.PROD
                };
                var affectedIds = changes
                    .Where(c => c.Status == ExcelLinkEngine.ChangeStatus.Applied && tokenParams.Contains(c.ParamName))
                    .Select(c => new ElementId(c.ElementId))
                    .Distinct()
                    .ToList();
                if (affectedIds.Count > 0)
                {
                    using (var rebuildTrans = new Transaction(doc, "STING Import Tag Rebuild"))
                    {
                        rebuildTrans.Start();
                        try
                        {
                            var (tagIndex, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
                            string cachedRev = PhaseAutoDetect.DetectProjectRevision(doc);
                            // FIX-R07: Load formulas and grid lines for import rebuild
                            var elFormulas = TagPipelineHelper.LoadFormulas();
                            var elGridLines = TagPipelineHelper.LoadGridLines(doc);
                            foreach (var eid in affectedIds)
                            {
                                Element el = doc.GetElement(eid);
                                if (el == null) continue;
                                try
                                {
                                    string catName = ParameterHelpers.GetCategoryName(el);
                                    // Phase 40: Audit trail — record previous tag before rebuild
                                    try
                                    {
                                        string prevTag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                                        if (!string.IsNullOrEmpty(prevTag))
                                        {
                                            ParameterHelpers.SetString(el, "ASS_TAG_PREV_TXT", prevTag, overwrite: true);
                                            ParameterHelpers.SetString(el, "ASS_TAG_MODIFIED_DT",
                                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), overwrite: true);
                                        }
                                    }
                                    catch (Exception atEx) { StingLog.Warn($"ExcelLink audit trail for {el.Id}: {atEx.Message}"); }
                                    // Inherit family-type token defaults before rebuild
                                    try { TokenAutoPopulator.TypeTokenInherit(doc, el); }
                                    catch (Exception tiEx) { StingLog.Warn($"ExcelLink TypeTokenInherit for {el.Id}: {tiEx.Message}"); }
                                    // Phase 40: PopulateAll — derive all 9 tokens from spatial/category context
                                    // This was MISSING — elements imported with empty tokens stayed empty
                                    try
                                    {
                                        var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
                                        if (popCtx != null && popCtx.IsValid())
                                            TokenAutoPopulator.PopulateAll(doc, el, popCtx, overwrite: false);
                                    }
                                    catch (Exception paEx) { StingLog.Warn($"ExcelLink PopulateAll for {el.Id}: {paEx.Message}"); }
                                    // FIX-10: Bridge native params before tag rebuild
                                    try { NativeParamMapper.MapAll(doc, el); }
                                    catch (Exception nmEx) { StingLog.Warn($"ExcelLink NativeMapper for {el.Id}: {nmEx.Message}"); }
                                    // FIX-R07: Evaluate formulas after native mapper
                                    if (elFormulas != null && elFormulas.Count > 0)
                                    {
                                        try
                                        {
                                            foreach (var formula in elFormulas)
                                            {
                                                Parameter fp = el.LookupParameter(formula.ParameterName);
                                                if (fp == null || fp.IsReadOnly) continue;
                                                var fCtx = Temp.FormulaEngine.BuildContext(el, formula);
                                                if (fCtx == null) continue;
                                                if (formula.DataType == "TEXT")
                                                {
                                                    string fResult = Temp.FormulaEngine.EvaluateText(formula.Expression, fCtx);
                                                    if (fResult != null && fp.StorageType == StorageType.String)
                                                        fp.Set(fResult);
                                                }
                                                else
                                                {
                                                    double? fResult = Temp.FormulaEngine.EvaluateNumeric(formula.Expression, fCtx);
                                                    if (fResult.HasValue && !double.IsNaN(fResult.Value) && !double.IsInfinity(fResult.Value))
                                                        Temp.FormulaEngine.WriteNumericResult(fp, fResult.Value);
                                                }
                                            }
                                        }
                                        catch (Exception fEx) { StingLog.Warn($"ExcelLink formula eval for {el.Id}: {fEx.Message}"); }
                                    }
                                    TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                                        skipComplete: false, tagIndex, TagCollisionMode.Overwrite, null,
                                        cachedRev: cachedRev);
                                    string[] tokenVals = ParamRegistry.ReadTokenValues(el);
                                    ParamRegistry.WriteContainers(el, tokenVals, catName, overwrite: true);
                                    TagConfig.WriteTag7All(doc, el, catName, tokenVals);
                                    // FIX-R07: Write GridRef per element
                                    if (elGridLines != null && elGridLines.Count > 0)
                                    {
                                        try
                                        {
                                            string gridRef = SpatialAutoDetect.GetGridRef(el, elGridLines);
                                            if (!string.IsNullOrEmpty(gridRef))
                                                ParameterHelpers.SetIfEmpty(el, ParamRegistry.GRID_REF, gridRef);
                                        }
                                        catch (Exception grEx) { StingLog.Warn($"ExcelLink GridRef for {el.Id}: {grEx.Message}"); }
                                    }
                                    rebuilt++;
                                }
                                catch (Exception ex)
                                {
                                    StingLog.Warn($"ExcelLink tag rebuild failed for {eid}: {ex.Message}");
                                }
                            }
                            rebuildTrans.Commit();
                            // FIX-R07: Save SEQ sidecar after commit
                            try { TagConfig.SaveSeqSidecar(doc, seqCounters); }
                            catch (Exception ssEx) { StingLog.Warn($"ExcelLink Import SaveSeqSidecar: {ssEx.Message}"); }
                        }
                        catch (Exception ex)
                        {
                            if (rebuildTrans.HasStarted()) rebuildTrans.RollBack();
                            StingLog.Error("ExcelLink tag rebuild transaction failed", ex);
                        }
                    }
                }

                tg.Assimilate();
                } // end try
                catch (Exception tgEx)
                {
                    try { tg.RollBack(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                    StingLog.Error("ExcelLink import TransactionGroup rolled back", tgEx);
                    throw;
                }
                } // end using TransactionGroup

                // LOG-12 FIX: Invalidate AutoTagger cached seqCounters and compliance cache
                // after import to prevent SEQ collisions on next auto-tag operation
                StingAutoTagger.InvalidateContext();
                ComplianceScan.InvalidateCache();
                TagConfig.CheckComplianceGate(doc, "ExcelImport");
                if (rebuilt > 0)
                    StingLog.Info($"ExcelLink: Rebuilt tags for {rebuilt} elements");

                // ── Write change log CSV ──
                string userName = "";
                try { userName = doc.Application.Username ?? ""; } catch (Exception ex) { StingLog.Warn($"Username lookup failed: {ex.Message}"); }
                ExcelLinkEngine.WriteChangeLog(filePath, changes, userName);

                // ── Report results ──
                int valSkipped = changes.Count(c => c.Status == ExcelLinkEngine.ChangeStatus.ValidationSkipped);
                StingLog.Info($"ExcelLink Import: applied={applied}, skipped={skipped}, valSkipped={valSkipped}, failed={failed}, rebuilt={rebuilt}");

                var resultMsg = $"Import Complete\n\n" +
                                $"Parameters updated: {applied}\n" +
                                $"Tags rebuilt: {rebuilt}\n" +
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
                    Process.Start(new ProcessStartInfo { FileName = outputPath, UseShellExecute = true })?.Dispose();
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

                // HR-01: Cross-project guard (safety check for round-trip)
                try
                {
                    string exportedGuid = ExcelLinkEngine.ReadProjectGuid(outputPath);
                    string currentGuid = doc.ProjectInformation?.UniqueId ?? "";
                    if (!string.IsNullOrEmpty(exportedGuid) && exportedGuid != currentGuid)
                    {
                        StingLog.Warn($"ExcelLink RoundTrip: project GUID mismatch detected (exported={exportedGuid}, current={currentGuid})");
                        TaskDialog.Show("STING Excel Round-Trip",
                            "Warning: The exported file's project GUID does not match the current project.\nThis may indicate the document was switched during editing.");
                    }
                }
                catch (Exception ex) { StingLog.Warn($"ExcelLink RoundTrip project GUID check: {ex.Message}"); }

                if (excelData.Count == 0)
                {
                    TaskDialog.Show("STING Excel Round-Trip", "No data rows found in the file.");
                    return Result.Succeeded;
                }

                // BIM-EXCEL-STREAM-01: For large round-trip files, offer streaming import
                const int StreamingThreshold = 10000;
                if (excelData.Count > StreamingThreshold)
                {
                    var largeDlg = new TaskDialog("STING Excel Round-Trip — Large File")
                    {
                        MainInstruction = $"Large File Detected ({excelData.Count:N0} rows)",
                        MainContent = "This round-trip file contains more than 10,000 rows.\n\n" +
                                      "Streaming import processes data in batches with progress\n" +
                                      "tracking, cancellation support, and partial commit on failure.\n\n" +
                                      "Detailed preview loads all changes for review\n" +
                                      "but may be slower for very large files.",
                        CommonButtons = TaskDialogCommonButtons.Cancel,
                    };
                    largeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        "Streaming import (recommended)", "Batch processing with progress bar");
                    largeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        "Detailed preview", "View all changes before applying");
                    var largeResult = largeDlg.Show();

                    if (largeResult == TaskDialogResult.Cancel)
                        return Result.Cancelled;

                    if (largeResult == TaskDialogResult.CommandLink1)
                    {
                        // BIM-EXCEL-STREAM-01: Streaming round-trip path
                        var progress = UI.StingProgressDialog.Show(
                            "STING Excel Round-Trip Streaming Import", excelData.Count);
                        ExcelLinkEngine.StreamingImportResult streamResult;
                        try
                        {
                            streamResult = ExcelLinkEngine.StreamingImport(
                                outputPath, doc, false, progress);
                        }
                        finally
                        {
                            progress.Close();
                        }

                        StingAutoTagger.InvalidateContext();
                        ComplianceScan.InvalidateCache();
                        TagConfig.CheckComplianceGate(doc, "ExcelRoundTrip");

                        string sUserName = "";
                        try { sUserName = doc.Application.Username ?? ""; }
                        catch (Exception ex) { StingLog.Warn($"Username lookup failed: {ex.Message}"); }
                        if (streamResult.AllChanges.Count > 0)
                            ExcelLinkEngine.WriteChangeLog(outputPath, streamResult.AllChanges, sUserName);

                        string cancelNote = streamResult.WasCancelled
                            ? "\n\n⚠ Import was cancelled — partial results applied." : "";
                        StingLog.Info($"ExcelLink RoundTrip Streaming: batches={streamResult.BatchCount}, " +
                            $"applied={streamResult.Applied}, skipped={streamResult.Skipped}, " +
                            $"failed={streamResult.Failed}, rebuilt={streamResult.Rebuilt}");
                        TaskDialog.Show("STING Excel Round-Trip",
                            $"Streaming Round-Trip Complete\n\n" +
                            $"Total rows: {streamResult.TotalRowsInFile:N0}\n" +
                            $"Batches processed: {streamResult.BatchCount}\n" +
                            $"Parameters updated: {streamResult.Applied}\n" +
                            $"Tags rebuilt: {streamResult.Rebuilt}\n" +
                            $"Skipped: {streamResult.Skipped}\n" +
                            $"Failures: {streamResult.Failed}" +
                            cancelNote +
                            "\n\nA change log CSV has been saved alongside the export file.");
                        return Result.Succeeded;
                    }
                    // else: fall through to existing detailed preview path
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

                // ── BUG-03: Rebuild tags for elements whose tokens were changed ──
                var tokenParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                    ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC, ParamRegistry.PROD
                };
                var affectedIds = changes
                    .Where(c => c.Status == ExcelLinkEngine.ChangeStatus.Applied && tokenParams.Contains(c.ParamName))
                    .Select(c => new ElementId(c.ElementId))
                    .Distinct()
                    .ToList();
                int rebuilt = 0;
                if (affectedIds.Count > 0)
                {
                    using (var rebuildTrans = new Transaction(doc, "STING RoundTrip Tag Rebuild"))
                    {
                        rebuildTrans.Start();
                        try
                        {
                            var (tagIndex, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
                            string cachedRev = PhaseAutoDetect.DetectProjectRevision(doc);
                            // FIX-R07: Load formulas and grid lines for round-trip rebuild
                            var rtFormulas = TagPipelineHelper.LoadFormulas();
                            var rtGridLines = TagPipelineHelper.LoadGridLines(doc);
                            foreach (var eid in affectedIds)
                            {
                                Element el = doc.GetElement(eid);
                                if (el == null) continue;
                                try
                                {
                                    string catName = ParameterHelpers.GetCategoryName(el);
                                    // Phase 40: Audit trail — record previous tag before rebuild
                                    try
                                    {
                                        string prevTag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                                        if (!string.IsNullOrEmpty(prevTag))
                                        {
                                            ParameterHelpers.SetString(el, "ASS_TAG_PREV_TXT", prevTag, overwrite: true);
                                            ParameterHelpers.SetString(el, "ASS_TAG_MODIFIED_DT",
                                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), overwrite: true);
                                        }
                                    }
                                    catch (Exception atEx) { StingLog.Warn($"ExcelLink RoundTrip audit trail for {el.Id}: {atEx.Message}"); }
                                    // Inherit family-type token defaults before round-trip rebuild
                                    try { TokenAutoPopulator.TypeTokenInherit(doc, el); }
                                    catch (Exception tiEx) { StingLog.Warn($"ExcelLink RoundTrip TypeTokenInherit for {el.Id}: {tiEx.Message}"); }
                                    // Phase 40: PopulateAll — derive all 9 tokens from spatial/category context
                                    try
                                    {
                                        var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
                                        if (popCtx != null && popCtx.IsValid())
                                            TokenAutoPopulator.PopulateAll(doc, el, popCtx, overwrite: false);
                                    }
                                    catch (Exception paEx) { StingLog.Warn($"ExcelLink RoundTrip PopulateAll for {el.Id}: {paEx.Message}"); }
                                    // Phase2: Bridge native params before tag rebuild
                                    try { NativeParamMapper.MapAll(doc, el); }
                                    catch (Exception nmEx) { StingLog.Warn($"ExcelLink RoundTrip NativeMapper for {el.Id}: {nmEx.Message}"); }
                                    // FIX-R07: Evaluate formulas after native mapper
                                    if (rtFormulas != null && rtFormulas.Count > 0)
                                    {
                                        try
                                        {
                                            foreach (var formula in rtFormulas)
                                            {
                                                Parameter fp = el.LookupParameter(formula.ParameterName);
                                                if (fp == null || fp.IsReadOnly) continue;
                                                var fCtx = Temp.FormulaEngine.BuildContext(el, formula);
                                                if (fCtx == null) continue;
                                                if (formula.DataType == "TEXT")
                                                {
                                                    string fResult = Temp.FormulaEngine.EvaluateText(formula.Expression, fCtx);
                                                    if (fResult != null && fp.StorageType == StorageType.String)
                                                        fp.Set(fResult);
                                                }
                                                else
                                                {
                                                    double? fResult = Temp.FormulaEngine.EvaluateNumeric(formula.Expression, fCtx);
                                                    if (fResult.HasValue && !double.IsNaN(fResult.Value) && !double.IsInfinity(fResult.Value))
                                                        Temp.FormulaEngine.WriteNumericResult(fp, fResult.Value);
                                                }
                                            }
                                        }
                                        catch (Exception fEx) { StingLog.Warn($"ExcelLink RoundTrip formula eval for {el.Id}: {fEx.Message}"); }
                                    }
                                    TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                                        skipComplete: false, tagIndex, TagCollisionMode.Overwrite, null,
                                        cachedRev: cachedRev);
                                    string[] tokenVals = ParamRegistry.ReadTokenValues(el);
                                    ParamRegistry.WriteContainers(el, tokenVals, catName, overwrite: true);
                                    TagConfig.WriteTag7All(doc, el, catName, tokenVals);
                                    // FIX-R07: Write GridRef per element
                                    if (rtGridLines != null && rtGridLines.Count > 0)
                                    {
                                        try
                                        {
                                            string gridRef = SpatialAutoDetect.GetGridRef(el, rtGridLines);
                                            if (!string.IsNullOrEmpty(gridRef))
                                                ParameterHelpers.SetIfEmpty(el, ParamRegistry.GRID_REF, gridRef);
                                        }
                                        catch (Exception grEx) { StingLog.Warn($"ExcelLink RoundTrip GridRef for {el.Id}: {grEx.Message}"); }
                                    }
                                    rebuilt++;
                                }
                                catch (Exception ex)
                                {
                                    StingLog.Warn($"ExcelLink RoundTrip tag rebuild failed for {eid}: {ex.Message}");
                                }
                            }
                            rebuildTrans.Commit();
                            // FIX-R07: Save SEQ sidecar after commit
                            try { TagConfig.SaveSeqSidecar(doc, seqCounters); }
                            catch (Exception ssEx) { StingLog.Warn($"ExcelLink RoundTrip SaveSeqSidecar: {ssEx.Message}"); }
                        }
                        catch (Exception ex)
                        {
                            if (rebuildTrans.HasStarted()) rebuildTrans.RollBack();
                            StingLog.Error("ExcelLink RoundTrip tag rebuild failed", ex);
                        }
                    }
                }

                // LOG-12 FIX: Invalidate AutoTagger cached seqCounters and compliance cache
                StingAutoTagger.InvalidateContext();
                ComplianceScan.InvalidateCache();
                TagConfig.CheckComplianceGate(doc, "ExcelRoundTrip");
                if (rebuilt > 0)
                    StingLog.Info($"ExcelLink RoundTrip: Rebuilt tags for {rebuilt} elements");

                // ── Write change log ──
                string userName = "";
                try { userName = doc.Application.Username ?? ""; } catch (Exception ex) { StingLog.Warn($"Username lookup failed: {ex.Message}"); }
                ExcelLinkEngine.WriteChangeLog(outputPath, changes, userName);

                int valSkipped = changes.Count(c => c.Status == ExcelLinkEngine.ChangeStatus.ValidationSkipped);
                StingLog.Info($"ExcelLink RoundTrip Import: applied={applied}, skipped={skipped}, valSkipped={valSkipped}, failed={failed}, rebuilt={rebuilt}");

                TaskDialog.Show("STING Excel Round-Trip",
                    $"Round-trip complete!\n\n" +
                    $"Parameters updated: {applied}\n" +
                    $"Tags rebuilt: {rebuilt}\n" +
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

                using var wb = new XLWorkbook();

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
                            catch (Exception ex) { StingLog.Warn($"Header read: {ex.Message}"); headerText = $"Column_{c + 1}"; }

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
                                catch (Exception ex) { StingLog.Warn($"Schedule cell read failed at row {r}, col {c}: {ex.Message}"); }
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
                        catch (Exception ex) { StingLog.Warn($"Schedule index category lookup failed: {ex.Message}"); }
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
                            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true })?.Dispose();
                    }
                    catch (Exception ex) { StingLog.Warn($"Open export folder failed: {ex.Message}"); }
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
                            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); schedHeaders.Add(""); }
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
                            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); continue; }

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

                // ── Second pass: apply changes via source element parameters ──
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
                                catch (Exception ex) { schedHeaders.Add(""); StingLog.Warn($"Schedule header read: {ex.Message}"); }
                            }
                        }

                        // Build field map: column index -> ScheduleField
                        var fieldOrder = sched.Definition.GetFieldOrder();
                        var fieldMap = new Dictionary<int, ScheduleField>();
                        for (int i = 0; i < fieldOrder.Count && i < cols; i++)
                        {
                            try { fieldMap[i] = sched.Definition.GetField(fieldOrder[i]); }
                            catch (Exception ex) { StingLog.Warn($"Schedule field lookup failed at index {i}: {ex.Message}"); }
                        }

                        // Collect source elements for this schedule.
                        // NOTE: FilteredElementCollector(doc, schedId) does NOT guarantee
                        // row-order correspondence with schedule display. Build an ElementId
                        // lookup so we can match rows by reading the element's parameter
                        // values rather than relying on index-based access.
                        var schedElementsById = new Dictionary<long, Element>();
                        foreach (var se in new FilteredElementCollector(doc, sched.Id)
                            .WhereElementIsNotElementType())
                        {
                            schedElementsById[se.Id.Value] = se;
                        }
                        var schedElementsList = schedElementsById.Values.ToList();

                        // Try to find an "ElementId" or "Id" column for reliable row-to-element matching
                        int idSchedCol = -1;
                        for (int ci = 0; ci < schedHeaders.Count; ci++)
                        {
                            string h = schedHeaders[ci];
                            if (h.Equals("ElementId", StringComparison.OrdinalIgnoreCase) ||
                                h.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                                h.Equals("Element Id", StringComparison.OrdinalIgnoreCase))
                            { idSchedCol = ci; break; }
                        }

                        int excelRow = 2;
                        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
                        for (int r = 0; r < rows && excelRow <= lastRow; r++, excelRow++)
                        {
                            // Resolve source element for this row — prefer ElementId column match
                            Element srcElement = null;
                            if (idSchedCol >= 0)
                            {
                                try
                                {
                                    string idText = sched.GetCellText(SectionType.Body, r, idSchedCol).Trim();
                                    if (long.TryParse(idText, out long eid) && schedElementsById.TryGetValue(eid, out var matched))
                                        srcElement = matched;
                                }
                                catch (Exception ex) { StingLog.Warn($"Schedule Id column read: {ex.Message}"); }
                            }
                            // Fallback: index-based (may not match row order — best effort)
                            if (srcElement == null && r < schedElementsList.Count)
                                srcElement = schedElementsList[r];

                            for (int ec = 0; ec < excelHeaders.Count; ec++)
                            {
                                string header = excelHeaders[ec];
                                int schedCol = schedHeaders.IndexOf(header);
                                if (schedCol < 0) continue;

                                string excelVal = ws.Cell(excelRow, ec + 1).GetString().Trim();
                                string currentVal = "";
                                try { currentVal = sched.GetCellText(SectionType.Body, r, schedCol).Trim(); }
                                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); continue; }

                                if (excelVal == currentVal) { skippedCells++; continue; }

                                // Get the field for this column
                                if (!fieldMap.TryGetValue(schedCol, out var field))
                                { skippedCells++; continue; }

                                if (field.IsCalculatedField) { skippedCells++; continue; }

                                if (srcElement == null) { failedCells++; continue; }

                                // Write to source element parameter
                                bool written = false;
                                try
                                {
                                    // Resolve parameter: try BuiltInParameter from FieldId, then by column name
                                    Parameter param = null;
                                    var paramId = field.ParameterId;
                                    if (paramId != null && paramId != ElementId.InvalidElementId)
                                    {
                                        int bipInt = (int)paramId.Value;
                                        if (Enum.IsDefined(typeof(BuiltInParameter), bipInt))
                                            param = srcElement.get_Parameter((BuiltInParameter)bipInt);
                                    }

                                    // Fallback: try by schedule column name as shared/project param
                                    if (param == null)
                                        param = srcElement.LookupParameter(header);

                                    // Fallback: try by field's column header text
                                    if (param == null)
                                    {
                                        string fieldName = field.GetName();
                                        if (!string.IsNullOrEmpty(fieldName) && fieldName != header)
                                            param = srcElement.LookupParameter(fieldName);
                                    }

                                    if (param != null && !param.IsReadOnly)
                                    {
                                        switch (param.StorageType)
                                        {
                                            case StorageType.String:
                                                param.Set(excelVal);
                                                written = true;
                                                break;
                                            case StorageType.Double:
                                                if (double.TryParse(excelVal, out double dv))
                                                { param.Set(dv); written = true; }
                                                break;
                                            case StorageType.Integer:
                                                if (int.TryParse(excelVal, out int iv))
                                                { param.Set(iv); written = true; }
                                                break;
                                        }
                                    }
                                }
                                catch (Exception writeEx)
                                {
                                    StingLog.Warn($"Schedule import write [{sched.Name}] row {r + 1} col '{header}': {writeEx.Message}");
                                }

                                if (written)
                                    updatedCells++;
                                else
                                    failedCells++;
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
                using var wb = new XLWorkbook();

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

                // PROD codes (from ProdMap values — family-aware codes)
                var prodCodes = new HashSet<string>(TagConfig.ProdMap.Values, StringComparer.Ordinal);
                var prodList = prodCodes.OrderBy(p => p).ToList();
                for (int i = 0; i < prodList.Count; i++)
                    valSheet.Cell(i + 1, 7).Value = prodList[i];

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
                ApplyValidation("PROD", 7, prodList.Count);

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
                            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true })?.Dispose();
                    }
                    catch (Exception ex) { StingLog.Warn($"Open template folder failed: {ex.Message}"); }
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

    // ════════════════════════════════════════════════════════════════════════════
    //  ExcelExchangeWizardCommand — Consolidated wizard for Export/Import/RoundTrip
    // ════════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExcelExchangeWizardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            Document doc = ctx.Doc;
            UIDocument uidoc = ctx.UIDoc;

            // Launch the Excel Exchange Wizard
            var settings = ExcelExchangeWizard.Show(doc);
            if (settings == null) return Result.Cancelled;

            StingLog.Info($"ExcelLink Wizard: mode={settings.Mode}, scope={settings.Scope}, file={settings.FilePath}");

            // Dispatch based on selected mode
            switch (settings.Mode)
            {
                case "Export":
                    return ExecuteExport(doc, uidoc, settings, ref message);
                case "Import":
                    return ExecuteImport(doc, uidoc, settings, ref message);
                case "RoundTrip":
                    return ExecuteRoundTrip(doc, uidoc, settings, ref message);
                case "Template":
                    return ExecuteTemplate(doc, settings, ref message);
                default:
                    TaskDialog.Show("STING", $"Unknown mode: {settings.Mode}");
                    return Result.Failed;
            }
        }

        private Result ExecuteExport(Document doc, UIDocument uidoc, ExcelExchangeSettings settings, ref string message)
        {
            try
            {
                bool selectionOnly = settings.Scope == "Selected";
                var (elems, scope) = ExcelLinkEngine.CollectElements(doc, uidoc, selectionOnly);
                if (elems.Count == 0)
                {
                    TaskDialog.Show("STING Excel Exchange", "No taggable elements found in the selected scope.");
                    return Result.Succeeded;
                }

                using var wb = ExcelLinkEngine.BuildWorkbook(doc, elems);
                string outputPath = settings.FilePath;
                if (string.IsNullOrEmpty(outputPath))
                    outputPath = OutputLocationHelper.GetOutputPath(doc, $"STING_Excel_Export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

                wb.SaveAs(outputPath);
                StingLog.Info($"ExcelLink Wizard: Exported {elems.Count} elements to {outputPath}");

                TaskDialog.Show("STING Excel Exchange",
                    $"Export Complete\n\nExported {elems.Count} elements ({scope}) to:\n{outputPath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExcelLink Wizard Export failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private Result ExecuteImport(Document doc, UIDocument uidoc, ExcelExchangeSettings settings, ref string message)
        {
            try
            {
                string filePath = settings.FilePath;
                if (!File.Exists(filePath))
                {
                    TaskDialog.Show("STING Excel Exchange", $"File not found:\n{filePath}");
                    return Result.Failed;
                }

                StingLog.Info($"ExcelLink Wizard: Importing from {filePath}");
                var excelData = ExcelLinkEngine.ReadExcelFile(filePath);
                if (excelData.Count == 0)
                {
                    TaskDialog.Show("STING Excel Exchange", "No data rows found in the Excel file.");
                    return Result.Succeeded;
                }

                var changes = ExcelLinkEngine.ComputeChanges(doc, excelData);
                var actualChanges = changes.Where(c => c.Status == ExcelLinkEngine.ChangeStatus.Changed).ToList();
                if (actualChanges.Count == 0)
                {
                    TaskDialog.Show("STING Excel Exchange", "No changes detected in the Excel file.");
                    return Result.Succeeded;
                }

                using (Transaction tx = new Transaction(doc, "STING Excel Import (Wizard)"))
                {
                    tx.Start();
                    var (applied, skippedI, failedI) = ExcelLinkEngine.ApplyChanges(doc, actualChanges, tx);
                    tx.Commit();
                }

                ComplianceScan.InvalidateCache();
                StingAutoTagger.InvalidateContext();

                TaskDialog.Show("STING Excel Exchange",
                    $"Import Complete\n\nApplied {actualChanges.Count} changes from:\n{filePath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExcelLink Wizard Import failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private Result ExecuteRoundTrip(Document doc, UIDocument uidoc, ExcelExchangeSettings settings, ref string message)
        {
            try
            {
                // Export
                bool selectionOnly = settings.Scope == "Selected";
                var (elems, scope) = ExcelLinkEngine.CollectElements(doc, uidoc, selectionOnly);
                if (elems.Count == 0)
                {
                    TaskDialog.Show("STING Excel Exchange", "No taggable elements found.");
                    return Result.Succeeded;
                }

                string filePath = settings.FilePath;
                if (string.IsNullOrEmpty(filePath))
                    filePath = OutputLocationHelper.GetOutputPath(doc, $"STING_RoundTrip_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

                using (var wb = ExcelLinkEngine.BuildWorkbook(doc, elems))
                    wb.SaveAs(filePath);

                // Open in Excel
                TaskDialog.Show("STING Excel Exchange",
                    $"Exported {elems.Count} elements to:\n{filePath}\n\n" +
                    "Edit the file in Excel, save it, then click OK to import changes.");

                // Import
                if (File.Exists(filePath))
                {
                    var excelData = ExcelLinkEngine.ReadExcelFile(filePath);
                    var changes = ExcelLinkEngine.ComputeChanges(doc, excelData);
                    var actualChanges = changes.Where(c => c.Status == ExcelLinkEngine.ChangeStatus.Changed).ToList();
                    if (actualChanges.Count > 0)
                    {
                        using (Transaction tx = new Transaction(doc, "STING Excel RoundTrip Import"))
                        {
                            tx.Start();
                            ExcelLinkEngine.ApplyChanges(doc, actualChanges, tx);
                            tx.Commit();
                        }
                        ComplianceScan.InvalidateCache();
                        StingAutoTagger.InvalidateContext();
                    }

                    TaskDialog.Show("STING Excel Exchange",
                        $"Round-Trip Complete\n\nApplied {actualChanges.Count} changes.");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExcelLink Wizard RoundTrip failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private Result ExecuteTemplate(Document doc, ExcelExchangeSettings settings, ref string message)
        {
            try
            {
                // Delegate to the existing ExportTemplateCommand for template generation
                string filePath = settings.FilePath;
                if (string.IsNullOrEmpty(filePath))
                    filePath = OutputLocationHelper.GetOutputPath(doc, $"STING_Template_{DateTime.Now:yyyyMMdd}.xlsx");

                // Build template inline (same logic as ExportTemplateCommand)
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.AddWorksheet("Data_Entry_Template");
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
                    ws.Columns().AdjustToContents(1, 50);
                    wb.SaveAs(filePath);
                }

                StingLog.Info($"ExcelLink Wizard: Template exported to {filePath}");
                TaskDialog.Show("STING Excel Exchange", $"Template exported to:\n{filePath}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ExcelLink Wizard Template failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
