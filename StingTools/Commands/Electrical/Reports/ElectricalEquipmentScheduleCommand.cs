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

namespace StingTools.Commands.Electrical.Reports
{
    /// <summary>
    /// Project-wide electrical equipment schedule covering switchgear,
    /// transformers, panels (sub-DBs + main DBs), UPS, generators. Distinct
    /// from the panel-schedule (which lists circuits within one panel) — this
    /// is the one-line nameplate roll-up that goes into the design pack.
    ///
    /// Reads native + STING shared params: kVA / kW, voltage, fault rating,
    /// IP rating, weight, lifting points, manufacturer, model. Skips items
    /// missing both a name and a kVA rating to keep the sheet clean.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ElectricalEquipmentScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var rows = new List<EquipmentRow>();
            CollectFromCategory(doc, BuiltInCategory.OST_ElectricalEquipment, "Switchgear/Panel", rows);
            // Some plants register transformers in mechanical/equipment by accident — try both.
            try { CollectFromCategory(doc, BuiltInCategory.OST_MechanicalEquipment, "Transformer (cat-misclassified)", rows); }
            catch { }

            // Promote known-equipment families.
            foreach (var r in rows)
            {
                string fn = (r.FamilyName ?? "").ToLowerInvariant();
                if (fn.Contains("transformer"))      r.Discipline = "Transformer";
                else if (fn.Contains("generator"))   r.Discipline = "Generator";
                else if (fn.Contains("ups"))         r.Discipline = "UPS";
                else if (fn.Contains("switchboard")) r.Discipline = "Switchboard";
                else if (fn.Contains("inverter") || fn.Contains("pv")) r.Discipline = "Inverter / PV";
            }

            if (rows.Count == 0)
            {
                TaskDialog.Show("STING Equipment Schedule", "No electrical equipment found.");
                return Result.Cancelled;
            }

            string outDir = Path.Combine(OutputLocationHelper.GetOutputDirectory(doc) ?? "", "electrical");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir,
                $"STING_EquipmentSchedule_{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
            WriteExcel(outPath, rows);

            TaskDialog.Show("STING Equipment Schedule",
                $"Scheduled {rows.Count} item(s) across "
                + $"{rows.Select(r => r.Discipline).Distinct().Count()} discipline(s).\n\n"
                + $"Excel: {outPath}");
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", outDir)
                { UseShellExecute = true });
            }
            catch { }
            return Result.Succeeded;
        }

        private static void CollectFromCategory(Document doc, BuiltInCategory cat, string defaultDisc,
            List<EquipmentRow> rows)
        {
            foreach (var el in new FilteredElementCollector(doc)
                .OfCategory(cat).WhereElementIsNotElementType()
                .OfType<FamilyInstance>())
            {
                try
                {
                    string name = el.Name ?? "";
                    double kva = SafeDouble(el, "ELC_PNL_KVA");
                    if (kva <= 0) kva = SafeDouble(el, "ELC_KVA_RATING");
                    if (string.IsNullOrEmpty(name) && kva <= 0) continue;
                    var row = new EquipmentRow
                    {
                        Tag           = el.LookupParameter("ASS_TAG_1")?.AsString() ?? el.Id.ToString(),
                        Mark          = el.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "",
                        Discipline    = defaultDisc,
                        FamilyName    = el.Symbol?.FamilyName ?? "",
                        TypeName      = el.Symbol?.Name ?? "",
                        Voltage       = SafeText(el, "ELC_PNL_VOLTAGE", "ELC_VLT_PRIMARY_RATING_V"),
                        KvaRating     = kva,
                        FaultKa       = SafeDouble(el, "ELC_PNL_SHORT_CIRCUIT_RATING_KA"),
                        IpRating      = SafeText(el, "ELC_PNL_IP_RATING", "ELC_IP_RATING"),
                        WeightKg      = SafeDouble(el, "ELC_WEIGHT_KG"),
                        Manufacturer  = SafeText(el, "ELC_MANUFACTURER", "Manufacturer"),
                        Model         = SafeText(el, "ELC_MODEL", "Model"),
                        SerialNumber  = SafeText(el, "ELC_SERIAL_NUMBER"),
                        Location      = LocationFor(doc, el),
                        Notes         = ""
                    };
                    rows.Add(row);
                }
                catch (Exception ex) { StingLog.Warn($"EquipSchedule item: {ex.Message}"); }
            }
        }

        private static string LocationFor(Document doc, Element el)
        {
            try
            {
                if (el is FamilyInstance fi && fi.Room != null) return fi.Room.Name ?? "";
                var lvl = el.LevelId != null && el.LevelId != ElementId.InvalidElementId
                    ? doc.GetElement(el.LevelId)?.Name : "";
                return lvl ?? "";
            }
            catch { return ""; }
        }

        private static double SafeDouble(Element el, params string[] names)
        {
            foreach (var n in names)
            {
                var p = el?.LookupParameter(n);
                if (p == null) continue;
                try
                {
                    if (p.StorageType == StorageType.Double) { var v = p.AsDouble(); if (v > 0) return v; }
                    if (p.StorageType == StorageType.String && double.TryParse(p.AsString(), out double v2) && v2 > 0) return v2;
                    if (p.StorageType == StorageType.Integer) { var v = p.AsInteger(); if (v > 0) return v; }
                }
                catch { }
            }
            return 0;
        }

        private static string SafeText(Element el, params string[] names)
        {
            foreach (var n in names)
            {
                var p = el?.LookupParameter(n);
                if (p == null) continue;
                try
                {
                    if (p.StorageType == StorageType.String)
                    {
                        var s = p.AsString();
                        if (!string.IsNullOrEmpty(s)) return s;
                    }
                    var asv = p.AsValueString();
                    if (!string.IsNullOrEmpty(asv)) return asv;
                }
                catch { }
            }
            return "";
        }

        private static void WriteExcel(string path, List<EquipmentRow> rows)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Equipment Schedule");
            ws.Cell(1, 1).Value = $"STING Electrical Equipment Schedule  ·  {rows.Count} items  ·  {DateTime.Now:yyyy-MM-dd HH:mm}";
            ws.Range(1, 1, 1, 13).Merge().Style.Font.Bold = true;
            ws.Range(1, 1, 1, 13).Style.Fill.BackgroundColor = XLColor.LightSteelBlue;

            string[] hdr = { "Mark", "Tag", "Discipline", "Family", "Type",
                             "Voltage", "kVA", "Fault (kA)", "IP", "Weight (kg)",
                             "Manufacturer", "Model", "Location" };
            for (int i = 0; i < hdr.Length; i++)
            {
                ws.Cell(2, i + 1).Value = hdr[i];
                ws.Cell(2, i + 1).Style.Font.Bold = true;
                ws.Cell(2, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
            }
            int row = 3;
            foreach (var r in rows.OrderBy(x => x.Discipline).ThenBy(x => x.Mark))
            {
                ws.Cell(row, 1).Value = r.Mark;
                ws.Cell(row, 2).Value = r.Tag;
                ws.Cell(row, 3).Value = r.Discipline;
                ws.Cell(row, 4).Value = r.FamilyName;
                ws.Cell(row, 5).Value = r.TypeName;
                ws.Cell(row, 6).Value = r.Voltage;
                ws.Cell(row, 7).Value = r.KvaRating;
                ws.Cell(row, 8).Value = r.FaultKa;
                ws.Cell(row, 9).Value = r.IpRating;
                ws.Cell(row, 10).Value = r.WeightKg;
                ws.Cell(row, 11).Value = r.Manufacturer;
                ws.Cell(row, 12).Value = r.Model;
                ws.Cell(row, 13).Value = r.Location;
                row++;
            }
            ws.Columns().AdjustToContents();
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.FitToPages(1, 0);
            ws.PageSetup.PaperSize = XLPaperSize.A3Paper;
            wb.SaveAs(path);
        }

        private class EquipmentRow
        {
            public string Tag, Mark, Discipline, FamilyName, TypeName, Voltage,
                          IpRating, Manufacturer, Model, SerialNumber, Location, Notes;
            public double KvaRating, FaultKa, WeightKg;
        }
    }
}
