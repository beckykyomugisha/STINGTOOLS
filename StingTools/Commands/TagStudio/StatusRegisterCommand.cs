// ============================================================================
// StatusRegisterCommand.cs — colour-coded data/QA status register → Excel.
//
//   *** Universal Tag pivot — status delivery ***
//
// Revit tag families cannot react to the tagged element's parameters in a formula
// (only labels can DISPLAY them), so per-element status can't be a coloured in-tag
// badge. The right home for status is a REGISTER: this command computes each
// taggable element's data-completeness gate + QA/sign-off gate (reusing
// ComplianceScan.ComputeElementGates — the same logic Stamp Gates uses) and writes
// a colour-coded Excel workbook to share with modellers:
//   • "Register" sheet — one row per element (Tag / Category / Discipline / Level /
//     Data gate + issue / QA gate + issue / Status / Element Id), gate cells filled
//     green / amber / red, auto-filtered, reds sorted to the top.
//   • "Summary" sheet — red/amber/green counts per discipline + grand total.
//
// Read-only (computes fresh — no stamp run required, no model writes).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.TagStudio
{
    [Transaction(TransactionMode.ReadOnly)]
    public class StatusRegisterCommand : IExternalCommand
    {
        private static readonly XLColor Navy  = XLColor.FromHtml("#1E3A5F");
        private static readonly XLColor Green = XLColor.FromHtml("#00B050");
        private static readonly XLColor Amber = XLColor.FromHtml("#FFC000");
        private static readonly XLColor Red   = XLColor.FromHtml("#C00000");
        private static readonly XLColor AltRow = XLColor.FromHtml("#F5F8FF");

        private sealed class Row
        {
            public string Tag, Cat, Disc, Lvl, Status, DataMsg, QaMsg;
            public int DataGate, QaGate;
            public long ElemId;
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            if (known.Count == 0)
            {
                TaskDialog.Show("Status Register", "Tag configuration not loaded. Run 'Load Config' first.");
                return Result.Cancelled;
            }

            var coll = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            var catEnums = SharedParamGuids.AllCategoryEnums;
            if (catEnums != null && catEnums.Length > 0)
                coll.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));

            var targets = coll.Where(e => e != null && e.IsValidObject &&
                                          known.Contains(ParameterHelpers.GetCategoryName(e)))
                              .ToList();
            if (targets.Count == 0)
            {
                TaskDialog.Show("Status Register", "No taggable STING elements found in this project.");
                return Result.Succeeded;
            }

            var rows = new List<Row>(targets.Count);
            var progress = StingProgressDialog.Show("Status Register", (targets.Count + 249) / 250);
            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if ((i % 250) == 0)
                    {
                        progress.Increment($"Scanning ({i + 1}/{targets.Count})");
                        if (EscapeChecker.IsEscapePressed()) break;
                    }
                    Element el = targets[i];
                    ComplianceScan.GateResult g;
                    try { g = ComplianceScan.ComputeElementGates(doc, el); }
                    catch (Exception ex) { StingLog.Warn($"StatusRegister {el.Id}: {ex.Message}"); continue; }

                    rows.Add(new Row
                    {
                        Tag     = ParameterHelpers.GetString(el, ParamRegistry.TAG1),
                        Cat     = ParameterHelpers.GetCategoryName(el),
                        Disc    = ParameterHelpers.GetString(el, ParamRegistry.DISC),
                        Lvl     = ParameterHelpers.GetLevelCode(doc, el),
                        Status  = ParameterHelpers.GetString(el, ParamRegistry.STATUS),
                        DataGate = g.DataGate, DataMsg = g.DataMsg ?? "",
                        QaGate   = g.QaGate,   QaMsg   = g.QaMsg   ?? "",
                        ElemId   = el.Id.Value,
                    });
                }
            }
            finally { progress.Close(); }

            // Worst status first (reds float up), then discipline, then category.
            rows = rows.OrderBy(r => Math.Min(r.DataGate, r.QaGate))
                       .ThenBy(r => r.Disc ?? "")
                       .ThenBy(r => r.Cat ?? "")
                       .ThenBy(r => r.Tag ?? "")
                       .ToList();

            string path;
            try { path = WriteWorkbook(doc, rows); }
            catch (Exception ex)
            {
                StingLog.Error("StatusRegister export", ex);
                TaskDialog.Show("Status Register", $"Export failed: {ex.Message}");
                return Result.Failed;
            }

            int dRed = rows.Count(r => r.DataGate == 0), dAmb = rows.Count(r => r.DataGate == 1), dGrn = rows.Count(r => r.DataGate == 2);
            int qRed = rows.Count(r => r.QaGate   == 0), qAmb = rows.Count(r => r.QaGate   == 1), qGrn = rows.Count(r => r.QaGate   == 2);

            var td = new TaskDialog("Status Register — done");
            td.MainInstruction = $"{rows.Count} elements written to the register";
            td.MainContent =
                $"Data gate:  🔴 {dRed}   🟡 {dAmb}   🟢 {dGrn}\n" +
                $"QA gate:    🔴 {qRed}   🟡 {qAmb}   🟢 {qGrn}\n\n" +
                $"Excel: {path}";
            td.Show();
            StingLog.Info($"StatusRegister: {rows.Count} rows, data(r/a/g)={dRed}/{dAmb}/{dGrn}, qa={qRed}/{qAmb}/{qGrn}, file={path}");
            return Result.Succeeded;
        }

        private static string GateText(int g) => g == 2 ? "GREEN" : g == 1 ? "AMBER" : "RED";
        private static XLColor GateFill(int g) => g == 2 ? Green : g == 1 ? Amber : Red;

        private static string WriteWorkbook(Document doc, List<Row> rows)
        {
            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, $"STING_StatusRegister_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

            using (var wb = new XLWorkbook())
            {
                // ── Register sheet ──
                var ws = wb.AddWorksheet("Register");
                string[] hdr = { "Tag", "Category", "Discipline", "Level",
                                 "Data gate", "Data issue", "QA gate", "QA issue", "Status", "Element Id" };
                for (int c = 0; c < hdr.Length; c++)
                {
                    var cell = ws.Cell(1, c + 1);
                    cell.Value = hdr[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Fill.BackgroundColor = Navy;
                }
                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i]; int row = i + 2;
                    ws.Cell(row, 1).Value = r.Tag;
                    ws.Cell(row, 2).Value = r.Cat;
                    ws.Cell(row, 3).Value = r.Disc;
                    ws.Cell(row, 4).Value = r.Lvl;
                    var dg = ws.Cell(row, 5); dg.Value = GateText(r.DataGate);
                    dg.Style.Fill.BackgroundColor = GateFill(r.DataGate);
                    dg.Style.Font.FontColor = r.DataGate == 1 ? XLColor.Black : XLColor.White;
                    dg.Style.Font.Bold = true;
                    ws.Cell(row, 6).Value = r.DataMsg;
                    var qg = ws.Cell(row, 7); qg.Value = GateText(r.QaGate);
                    qg.Style.Fill.BackgroundColor = GateFill(r.QaGate);
                    qg.Style.Font.FontColor = r.QaGate == 1 ? XLColor.Black : XLColor.White;
                    qg.Style.Font.Bold = true;
                    ws.Cell(row, 8).Value = r.QaMsg;
                    ws.Cell(row, 9).Value = r.Status;
                    ws.Cell(row, 10).Value = r.ElemId;
                    if (i % 2 == 1)
                        for (int c = 1; c <= hdr.Length; c++)
                            if (c != 5 && c != 7) ws.Cell(row, c).Style.Fill.BackgroundColor = AltRow;
                }
                ws.Columns().AdjustToContents();
                ws.SheetView.FreezeRows(1);
                ws.Range(1, 1, Math.Max(1, rows.Count + 1), hdr.Length).SetAutoFilter();

                // ── Summary sheet ──
                var sm = wb.AddWorksheet("Summary");
                string[] shdr = { "Discipline", "Data 🔴", "Data 🟡", "Data 🟢", "QA 🔴", "QA 🟡", "QA 🟢", "Total" };
                for (int c = 0; c < shdr.Length; c++)
                {
                    var cell = sm.Cell(1, c + 1);
                    cell.Value = shdr[c]; cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White; cell.Style.Fill.BackgroundColor = Navy;
                }
                int sr = 2;
                foreach (var grp in rows.GroupBy(r => string.IsNullOrEmpty(r.Disc) ? "(none)" : r.Disc)
                                        .OrderByDescending(gp => gp.Count(x => x.DataGate == 0 || x.QaGate == 0))
                                        .ThenBy(gp => gp.Key))
                {
                    var list = grp.ToList();
                    sm.Cell(sr, 1).Value = grp.Key;
                    sm.Cell(sr, 2).Value = list.Count(x => x.DataGate == 0);
                    sm.Cell(sr, 3).Value = list.Count(x => x.DataGate == 1);
                    sm.Cell(sr, 4).Value = list.Count(x => x.DataGate == 2);
                    sm.Cell(sr, 5).Value = list.Count(x => x.QaGate == 0);
                    sm.Cell(sr, 6).Value = list.Count(x => x.QaGate == 1);
                    sm.Cell(sr, 7).Value = list.Count(x => x.QaGate == 2);
                    sm.Cell(sr, 8).Value = list.Count;
                    sr++;
                }
                var tot = sm.Cell(sr, 1); tot.Value = "TOTAL"; tot.Style.Font.Bold = true;
                sm.Cell(sr, 2).Value = rows.Count(x => x.DataGate == 0);
                sm.Cell(sr, 3).Value = rows.Count(x => x.DataGate == 1);
                sm.Cell(sr, 4).Value = rows.Count(x => x.DataGate == 2);
                sm.Cell(sr, 5).Value = rows.Count(x => x.QaGate == 0);
                sm.Cell(sr, 6).Value = rows.Count(x => x.QaGate == 1);
                sm.Cell(sr, 7).Value = rows.Count(x => x.QaGate == 2);
                sm.Cell(sr, 8).Value = rows.Count;
                sm.Row(sr).Style.Font.Bold = true;
                sm.Columns().AdjustToContents();

                wb.SaveAs(path);
            }

            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); }
            catch (Exception ex) { StingLog.Warn($"Open folder: {ex.Message}"); }
            return path;
        }
    }
}
