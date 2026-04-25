// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/ExcelBidirectionalSync.cs — S6.11 (N-G15).
//
// Bidirectional Excel sync with formula preservation.
//
// Export:  writes a workbook mirroring the current model tag set.
// Import:  reads the workbook and applies changes to the model. If
//          a cell that was computed (formula) is overwritten by the
//          user in Excel, we import the *resulting value* and push
//          it back into the model — but we *preserve the formula*
//          on the Excel side so the next export can keep it. The
//          formula is also captured in a sidecar JSON so future
//          exports can recreate it if the workbook is lost.
//
// Uses ClosedXML (existing dependency). QS-friendly round-trip.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.V6
{
    public sealed class ExcelSyncReport
    {
        public int ExportedRows { get; set; }
        public int ImportedRows { get; set; }
        public int FormulasPreserved { get; set; }
        public int WriteFailures { get; set; }
        public string WorkbookPath { get; set; } = string.Empty;
        public string SidecarPath { get; set; } = string.Empty;
        public List<string> Errors { get; set; } = new List<string>();
    }

    public static class ExcelBidirectionalSync
    {
        private const string SheetName = "STING_SYNC";

        /// <summary>
        /// Export the tagged-element set to an Excel workbook.
        /// Columns: ElementId, TAG1, DISC, LOC, ZONE, LVL, SYS, FUNC,
        /// PROD, SEQ, STATUS, REV.
        /// </summary>
        public static ExcelSyncReport Export(Document doc, string path)
        {
            var report = new ExcelSyncReport { WorkbookPath = path };
            if (doc == null || string.IsNullOrWhiteSpace(path)) return report;
            try
            {
                var col = new FilteredElementCollector(doc)
                    .WherePasses(new ElementMulticategoryFilter(SharedParamGuids.AllCategoryEnums))
                    .WhereElementIsNotElementType();

                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add(SheetName);
                int row = 1;
                string[] headers = { "ElementId", "TAG1", "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ", "STATUS", "REV" };
                for (int i = 0; i < headers.Length; i++) ws.Cell(row, i + 1).Value = headers[i];
                row++;
                foreach (var el in col)
                {
                    ws.Cell(row, 1).Value = el.Id.Value;
                    ws.Cell(row, 2).Value = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                    ws.Cell(row, 3).Value = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                    ws.Cell(row, 4).Value = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                    ws.Cell(row, 5).Value = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
                    ws.Cell(row, 6).Value = ParameterHelpers.GetString(el, ParamRegistry.LVL);
                    ws.Cell(row, 7).Value = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                    ws.Cell(row, 8).Value = ParameterHelpers.GetString(el, ParamRegistry.FUNC);
                    ws.Cell(row, 9).Value = ParameterHelpers.GetString(el, ParamRegistry.PROD);
                    ws.Cell(row, 10).Value = ParameterHelpers.GetString(el, ParamRegistry.SEQ);
                    ws.Cell(row, 11).Value = ParameterHelpers.GetString(el, "ASS_STATUS_TXT");
                    ws.Cell(row, 12).Value = ParameterHelpers.GetString(el, "ASS_REV_TXT");
                    row++;
                }
                report.ExportedRows = row - 2;
                // Restore any formulas captured from a prior import.
                RestoreFormulas(ws, path);
                wb.SaveAs(path);
            }
            catch (Exception ex)
            {
                report.Errors.Add(ex.Message);
                StingLog.Error("ExcelBidirectionalSync.Export failed", ex);
            }
            return report;
        }

        /// <summary>
        /// Import an Excel workbook and apply changes. If a cell has a
        /// formula we (a) use its computed value to update the model
        /// and (b) capture the formula into a sidecar JSON next to the
        /// workbook so future exports re-populate it.
        /// </summary>
        public static ExcelSyncReport Import(Document doc, string path)
        {
            var report = new ExcelSyncReport { WorkbookPath = path };
            if (doc == null || !File.Exists(path)) { report.Errors.Add("Workbook missing"); return report; }
            var formulas = new JArray();
            try
            {
                using var wb = new XLWorkbook(path);
                var ws = wb.Worksheet(SheetName);
                if (ws == null) { report.Errors.Add($"Worksheet '{SheetName}' not found"); return report; }

                TransactionHelper.RunInScope(doc, "STING Excel import", t =>
                {
                    foreach (var row in ws.RangeUsed().RowsUsed().Skip(1))
                    {
                        try
                        {
                            long id = (long)row.Cell(1).GetDouble();
                            var el = doc.GetElement(new ElementId(id));
                            if (el == null) { report.WriteFailures++; continue; }
                            WriteIfChanged(el, ParamRegistry.TAG1,  row.Cell(2).GetString(), ref report, formulas, id, "TAG1",  row.Cell(2));
                            WriteIfChanged(el, ParamRegistry.DISC,  row.Cell(3).GetString(), ref report, formulas, id, "DISC",  row.Cell(3));
                            WriteIfChanged(el, ParamRegistry.LOC,   row.Cell(4).GetString(), ref report, formulas, id, "LOC",   row.Cell(4));
                            WriteIfChanged(el, ParamRegistry.ZONE,  row.Cell(5).GetString(), ref report, formulas, id, "ZONE",  row.Cell(5));
                            WriteIfChanged(el, ParamRegistry.LVL,   row.Cell(6).GetString(), ref report, formulas, id, "LVL",   row.Cell(6));
                            WriteIfChanged(el, ParamRegistry.SYS,   row.Cell(7).GetString(), ref report, formulas, id, "SYS",   row.Cell(7));
                            WriteIfChanged(el, ParamRegistry.FUNC,  row.Cell(8).GetString(), ref report, formulas, id, "FUNC",  row.Cell(8));
                            WriteIfChanged(el, ParamRegistry.PROD,  row.Cell(9).GetString(), ref report, formulas, id, "PROD",  row.Cell(9));
                            WriteIfChanged(el, ParamRegistry.SEQ,   row.Cell(10).GetString(), ref report, formulas, id, "SEQ",  row.Cell(10));
                            WriteIfChanged(el, "ASS_STATUS_TXT",    row.Cell(11).GetString(), ref report, formulas, id, "STATUS", row.Cell(11));
                            WriteIfChanged(el, "ASS_REV_TXT",       row.Cell(12).GetString(), ref report, formulas, id, "REV",  row.Cell(12));
                            report.ImportedRows++;
                        }
                        catch (Exception ex)
                        {
                            report.WriteFailures++;
                            report.Errors.Add($"Row: {ex.Message}");
                        }
                    }
                });
                report.SidecarPath = Path.Combine(
                    Path.GetDirectoryName(path)!,
                    Path.GetFileNameWithoutExtension(path) + ".formulas.json");
                File.WriteAllText(report.SidecarPath, formulas.ToString());
            }
            catch (Exception ex)
            {
                report.Errors.Add(ex.Message);
                StingLog.Error("ExcelBidirectionalSync.Import failed", ex);
            }
            return report;
        }

        private static void WriteIfChanged(Element el, string paramName, string value,
            ref ExcelSyncReport report, JArray formulas, long elId, string field, IXLCell cell)
        {
            if (cell != null && cell.HasFormula)
            {
                formulas.Add(new JObject
                {
                    ["element_id"] = elId,
                    ["field"] = field,
                    ["formula"] = cell.FormulaA1,
                });
                report.FormulasPreserved++;
            }
            string cur = ParameterHelpers.GetString(el, paramName);
            if (string.Equals(cur, value, StringComparison.Ordinal)) return;
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) { report.WriteFailures++; return; }
                p.Set(value ?? string.Empty);
            }
            catch (Exception ex)
            {
                report.WriteFailures++;
                report.Errors.Add($"Element {elId} / {paramName}: {ex.Message}");
            }
        }

        private static void RestoreFormulas(IXLWorksheet ws, string workbookPath)
        {
            string side = Path.Combine(Path.GetDirectoryName(workbookPath)!,
                Path.GetFileNameWithoutExtension(workbookPath) + ".formulas.json");
            if (!File.Exists(side)) return;
            try
            {
                var arr = JArray.Parse(File.ReadAllText(side));
                var byRow = new Dictionary<long, JObject>();
                foreach (JObject f in arr)
                {
                    long id = (long?)f["element_id"] ?? 0;
                    if (id != 0) byRow[id] = f;
                }
                foreach (var row in ws.RangeUsed().RowsUsed().Skip(1))
                {
                    long id = (long)row.Cell(1).GetDouble();
                    if (!byRow.TryGetValue(id, out var f)) continue;
                    string field = (string)f["field"];
                    string formula = (string)f["formula"];
                    int col = ColFor(field);
                    if (col > 0) row.Cell(col).FormulaA1 = formula;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn("ExcelBidirectionalSync.RestoreFormulas: " + ex.Message);
            }
        }

        private static int ColFor(string field) => field switch
        {
            "TAG1" => 2, "DISC" => 3, "LOC" => 4, "ZONE" => 5, "LVL" => 6,
            "SYS" => 7, "FUNC" => 8, "PROD" => 9, "SEQ" => 10, "STATUS" => 11, "REV" => 12,
            _ => 0,
        };
    }
}
