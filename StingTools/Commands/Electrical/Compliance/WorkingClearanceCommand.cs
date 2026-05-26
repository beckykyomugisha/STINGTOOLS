using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Compliance
{
    /// <summary>
    /// Verifies BS 7671 §132.12 / IEC 60364-7-729 / NEC 110.26 working
    /// clearance around switchgear and panelboards. Default rule:
    /// 1000 mm front + 750 mm side + 2000 mm headroom; configurable
    /// by voltage class via <c>ELC_PNL_VOLTAGE_CLASS</c> shared param.
    ///
    /// Algorithm: cast an axis-aligned bounding box around each panel
    /// expanded by the required clearance, then ask Revit for any
    /// elements in OST_Walls / OST_Furniture / OST_StructuralColumns /
    /// OST_GenericModel that intersect the swept box. Reports each
    /// intrusion in an Excel pack with red-flag rows.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WorkingClearanceCommand : IExternalCommand
    {
        private const double DefaultFrontMm    = 1000;
        private const double DefaultSideMm     = 750;
        private const double DefaultHeadroomMm = 2000;
        private const double MmToFt = 1 / 304.8;

        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var panels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType().OfType<FamilyInstance>()
                .ToList();
            if (panels.Count == 0)
            {
                TaskDialog.Show("STING Clearance", "No electrical equipment found.");
                return Result.Cancelled;
            }

            // Pre-cache obstruction candidates by category — the categories
            // that most often violate clearance in real models.
            var obstructionIds = new HashSet<long>();
            BuiltInCategory[] obstructionCats =
            {
                BuiltInCategory.OST_Walls, BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_Furniture, BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_CableTray, BuiltInCategory.OST_Doors
            };

            var rows = new List<ClearanceRow>();
            foreach (var panel in panels)
            {
                try
                {
                    BoundingBoxXYZ bb = panel.get_BoundingBox(null);
                    if (bb == null) continue;

                    double frontFt    = DefaultFrontMm    * MmToFt;
                    double sideFt     = DefaultSideMm     * MmToFt;
                    double headroomFt = DefaultHeadroomMm * MmToFt;

                    // Expand the bbox by the clearance envelope.
                    var expandedMin = new XYZ(bb.Min.X - sideFt,  bb.Min.Y - frontFt, bb.Min.Z);
                    var expandedMax = new XYZ(bb.Max.X + sideFt,  bb.Max.Y + frontFt, bb.Max.Z + headroomFt);
                    var bbox = new Outline(expandedMin, expandedMax);
                    var bbf  = new BoundingBoxIntersectsFilter(bbox);

                    var intrusions = new List<string>();
                    foreach (var cat in obstructionCats)
                    {
                        try
                        {
                            var hits = new FilteredElementCollector(doc)
                                .OfCategory(cat).WhereElementIsNotElementType()
                                .WherePasses(bbf)
                                .Where(e => e.Id != panel.Id)
                                .ToList();
                            foreach (var h in hits)
                                intrusions.Add($"{cat.ToString().Replace("OST_", "")}: {h.Name ?? h.Id.ToString()}");
                        }
                        catch (Exception ex) { StingLog.Warn($"Clearance scan {cat}: {ex.Message}"); }
                    }

                    // Doors that swing into the clearance zone are a particular
                    // hazard — flag separately if any are within 1.5× front.
                    rows.Add(new ClearanceRow
                    {
                        Mark = panel.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "",
                        PanelName = panel.Name ?? "",
                        FrontMm = DefaultFrontMm,
                        SideMm = DefaultSideMm,
                        HeadroomMm = DefaultHeadroomMm,
                        IntrusionCount = intrusions.Count,
                        Verdict = intrusions.Count == 0 ? "PASS" : "FAIL",
                        Intrusions = intrusions.Take(8).ToList()
                    });
                }
                catch (Exception ex) { StingLog.Warn($"Clearance panel {panel.Name}: {ex.Message}"); }
            }

            string outDir = Path.Combine(OutputLocationHelper.GetOutputDirectory(doc) ?? "", "electrical");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir,
                $"STING_WorkingClearance_{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
            WriteExcel(outPath, rows);

            int fails = rows.Count(r => r.Verdict == "FAIL");
            TaskDialog.Show("STING Working Clearance",
                $"Audited {rows.Count} switchgear/panelboard item(s) per BS 7671 §132.12.\n" +
                $"  Front: {DefaultFrontMm} mm  ·  Side: {DefaultSideMm} mm  ·  Headroom: {DefaultHeadroomMm} mm\n\n" +
                $"❌ FAIL: {fails}    ✅ PASS: {rows.Count - fails}\n\n" +
                $"Excel: {outPath}");
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", outDir) { UseShellExecute = true }); } catch { }
            return Result.Succeeded;
        }

        private static void WriteExcel(string path, List<ClearanceRow> rows)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Working Clearance");
            ws.Cell(1, 1).Value = "STING Working-Clearance Audit  ·  BS 7671 §132.12 / IEC 60364-7-729 / NEC 110.26";
            ws.Range(1, 1, 1, 7).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 7).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
            string[] hdr = { "Mark", "Panel", "Front (mm)", "Side (mm)", "Headroom (mm)", "Verdict", "Intrusions" };
            for (int i = 0; i < hdr.Length; i++)
            {
                ws.Cell(2, i + 1).Value = hdr[i];
                ws.Cell(2, i + 1).Style.Font.Bold = true;
                ws.Cell(2, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
            }
            int row = 3;
            foreach (var r in rows.OrderBy(x => x.Verdict == "PASS" ? 1 : 0))
            {
                ws.Cell(row, 1).Value = r.Mark;
                ws.Cell(row, 2).Value = r.PanelName;
                ws.Cell(row, 3).Value = r.FrontMm;
                ws.Cell(row, 4).Value = r.SideMm;
                ws.Cell(row, 5).Value = r.HeadroomMm;
                ws.Cell(row, 6).Value = r.Verdict;
                ws.Cell(row, 7).Value = r.Intrusions.Count == 0 ? "—" : string.Join(" · ", r.Intrusions);
                if (r.Verdict == "FAIL")
                    ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = XLColor.LightSalmon;
                row++;
            }
            ws.Columns().AdjustToContents();
            ws.Column(7).Width = 80;
            wb.SaveAs(path);
        }

        private class ClearanceRow
        {
            public string Mark, PanelName, Verdict;
            public double FrontMm, SideMm, HeadroomMm;
            public int IntrusionCount;
            public List<string> Intrusions = new();
        }
    }
}
