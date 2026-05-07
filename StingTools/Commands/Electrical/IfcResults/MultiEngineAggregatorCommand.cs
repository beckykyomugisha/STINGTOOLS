using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;

namespace StingTools.Commands.Electrical.IfcResults
{
    /// <summary>
    /// Differentiator from the Phase 180 research report — surface DIALux,
    /// ElumTools, Relux and STING-estimate lux values per room side-by-side
    /// with delta highlighting. ElumTools cannot do this because it ships
    /// its own engine and ignores the others; STING's open IFC results
    /// contract makes this the killer feature.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MultiEngineAggregatorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var rows = new List<AggregatorRow>();
            foreach (var room in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().OfType<Room>()
                .Where(r => r.Area > 0))
            {
                double dialux = ParseDouble(ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_LUX_DIALUX));
                double elum   = ParseDouble(ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_LUX_ELUMTOOLS));
                double relux  = ParseDouble(ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_LUX_RELUX));
                double stng   = ParseDouble(ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_LUX));
                double ugr    = ParseDouble(ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_UGR));
                string engine = ParameterHelpers.GetString(room, ParamRegistry.ELC_PHOTO_LAST_ENGINE);

                if (dialux == 0 && elum == 0 && relux == 0 && stng == 0) continue;

                rows.Add(new AggregatorRow
                {
                    RoomName    = room.Name ?? "",
                    Dialux      = dialux, Elum = elum, Relux = relux, StingEstimate = stng,
                    UGR         = ugr, LastEngine = engine,
                    Min         = MinNonZero(dialux, elum, relux, stng),
                    Max         = Math.Max(Math.Max(dialux, elum), Math.Max(relux, stng)),
                });
            }
            if (rows.Count == 0)
            {
                TaskDialog.Show("STING Multi-Engine Aggregator",
                    "No photometric results found. Import an IFC from DIALux / ElumTools / Relux first " +
                    "or run STING → Photometric Link → Estimate from Watts.");
                return Result.Cancelled;
            }

            // Excel export — one row per room, one column per engine, plus delta.
            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            try { outDir = Path.Combine(outDir, "electrical"); Directory.CreateDirectory(outDir); } catch { }
            string outPath = Path.Combine(outDir,
                $"STING_PhotometricAggregator_{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Aggregator");
                ws.Cell(1, 1).Value = "Room";
                ws.Cell(1, 2).Value = "DIALux";
                ws.Cell(1, 3).Value = "ElumTools";
                ws.Cell(1, 4).Value = "Relux";
                ws.Cell(1, 5).Value = "STING Estimate";
                ws.Cell(1, 6).Value = "Min";
                ws.Cell(1, 7).Value = "Max";
                ws.Cell(1, 8).Value = "Δ Max-Min";
                ws.Cell(1, 9).Value = "Δ %";
                ws.Cell(1, 10).Value = "UGR";
                ws.Cell(1, 11).Value = "Last Engine";
                ws.Range(1, 1, 1, 11).Style.Font.Bold = true;

                int row = 2;
                foreach (var r in rows.OrderByDescending(x => x.DeltaPct))
                {
                    ws.Cell(row, 1).Value = r.RoomName;
                    ws.Cell(row, 2).Value = r.Dialux;
                    ws.Cell(row, 3).Value = r.Elum;
                    ws.Cell(row, 4).Value = r.Relux;
                    ws.Cell(row, 5).Value = r.StingEstimate;
                    ws.Cell(row, 6).Value = r.Min;
                    ws.Cell(row, 7).Value = r.Max;
                    ws.Cell(row, 8).Value = r.Delta;
                    ws.Cell(row, 9).Value = r.DeltaPct;
                    ws.Cell(row, 10).Value = r.UGR;
                    ws.Cell(row, 11).Value = r.LastEngine ?? "";
                    if (r.DeltaPct > 25)
                        ws.Range(row, 1, row, 11).Style.Fill.BackgroundColor = XLColor.LightSalmon;
                    else if (r.DeltaPct > 10)
                        ws.Range(row, 1, row, 11).Style.Fill.BackgroundColor = XLColor.LightYellow;
                    row++;
                }
                ws.Columns().AdjustToContents();
                wb.SaveAs(outPath);
            }
            catch (Exception ex)
            {
                StingLog.Error($"MultiEngineAggregator save: {ex.Message}", ex);
                TaskDialog.Show("STING Aggregator", $"Save failed: {ex.Message}");
                return Result.Failed;
            }

            int worst = rows.Count(r => r.DeltaPct > 25);
            TaskDialog.Show("STING Multi-Engine Aggregator",
                $"Aggregated photometric results across {rows.Count} room(s).\n" +
                $"⚠ {worst} rooms show > 25 % engine-to-engine delta — review.\n\n" +
                $"Excel: {outPath}");
            return Result.Succeeded;
        }

        private class AggregatorRow
        {
            public string RoomName;
            public double Dialux, Elum, Relux, StingEstimate;
            public double Min, Max, UGR;
            public string LastEngine;
            public double Delta => Max - Min;
            public double DeltaPct => Min > 0 ? Delta / Min * 100.0 : 0;
        }

        private static double MinNonZero(params double[] xs)
        {
            double m = double.MaxValue;
            foreach (var x in xs) if (x > 0 && x < m) m = x;
            return m == double.MaxValue ? 0 : m;
        }
        private static double ParseDouble(string s) => double.TryParse(s, out double v) ? v : 0;
    }
}
