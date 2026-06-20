// NiagaraCommands.cs — BMS (Niagara) point-list export + station reconcile.
//
// Niagara is the Owner's BMS framework. The BIM Manager's scope is BMS MODEL
// CONTENT + controls submittals (coordinate, not author) — so these two
// commands bridge the model and the Niagara station without a live transport:
//
//   Niagara_ExportPoints  Export a Niagara-ingestable point list (controls
//                         submittal) from BMS/IoT devices tagged in the model.
//   Niagara_Reconcile     Compare a Niagara/BACnet station export against the
//                         modelled BMS devices: points in the station with no
//                         model element, and model devices with no station
//                         point. Mirrors SpecLink_Reconcile.
//
// Device source is IoTDeviceRegistry — elements carrying ICT_HEALTHIOT_DEVICE_ID_TXT
// (+ _PROTOCOL_TXT / _ENDPOINT_TXT / _ALERT_BAND_TXT). Read-only; no Revit
// transaction. Live Niagara read-back (oBIX / Haystack / BACnet) stays an FM /
// commissioning add-on behind TwinReadbackBase.
//
// Built without dotnet build verification (Linux sandbox).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;
using StingTools.Core.Twin;

namespace StingTools.Commands.Twin
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class NiagaraPointListExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var devices = new IoTDeviceRegistry(doc).All().ToList();
            if (devices.Count == 0)
            {
                TaskDialog.Show("Niagara — Export Points",
                    "No BMS/IoT points found.\n\n" +
                    "Tag BMS controllers/points with ICT_HEALTHIOT_DEVICE_ID_TXT " +
                    "(+ _PROTOCOL_TXT / _ENDPOINT_TXT / _ALERT_BAND_TXT), then re-run.");
                return Result.Succeeded;
            }

            int noEndpoint = 0;
            string path;
            try
            {
                var rows = new List<string>
                { "DeviceId,Protocol,Endpoint,AlertBand,ElementId,Category,Family,Type,Tag,Room" };
                foreach (var d in devices)
                {
                    var el = doc.GetElement(d.BimElementId);
                    if (el == null) continue;
                    if (string.IsNullOrEmpty(d.EndpointAddress)) noEndpoint++;
                    string room = "";
                    try
                    {
                        var r = ParameterHelpers.GetRoomAtElement(doc, el);
                        if (r != null) room = $"{r.Number} {r.Name}".Trim();
                    }
                    catch { }
                    rows.Add(string.Join(",",
                        Csv(d.DeviceId), Csv(d.Protocol), Csv(d.EndpointAddress), Csv(d.AlertBand),
                        el.Id.Value, Csv(ParameterHelpers.GetCategoryName(el)),
                        Csv(ParameterHelpers.GetFamilyName(el)), Csv(ParameterHelpers.GetFamilySymbolName(el)),
                        Csv(ParameterHelpers.GetString(el, ParamRegistry.TAG1)), Csv(room)));
                }
                path = OutputLocationHelper.GetOutputPath(doc, $"STING_Niagara_Points_{DateTime.Now:yyyyMMdd}.csv");
                File.WriteAllLines(path, rows, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                StingLog.Error("Niagara_ExportPoints", ex);
                TaskDialog.Show("Niagara — Export Points", "Export failed:\n" + ex.Message);
                return Result.Failed;
            }

            new TaskDialog("Niagara — Export Points")
            {
                MainInstruction = $"Exported {devices.Count} BMS point(s)",
                MainContent = $"{noEndpoint} point(s) have no endpoint address (incomplete controls submittal).\n\n" +
                              $"CSV: {path}\n\nIngest into the Niagara station, then run Niagara Reconcile " +
                              "against the station export to close the loop."
            }.Show();
            StingLog.Info($"Niagara_ExportPoints: {devices.Count} points, {noEndpoint} no-endpoint → {path}");
            return Result.Succeeded;
        }

        private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class NiagaraReconcileCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select the Niagara / BACnet station export (CSV or XLSX with a point/device id column)",
                Filter = "Station export (*.csv;*.xlsx)|*.csv;*.xlsx",
                InitialDirectory = OutputLocationHelper.GetOutputDirectory(doc)
            };
            if (dlg.ShowDialog() != true) return Result.Cancelled;

            HashSet<string> stationIds;
            try { stationIds = ReadIds(dlg.FileName); }
            catch (Exception ex) { TaskDialog.Show("Niagara Reconcile", "Could not read the station export:\n" + ex.Message); return Result.Failed; }
            if (stationIds.Count == 0)
            {
                TaskDialog.Show("Niagara Reconcile",
                    "No point ids read — the export needs a column like Device/Point/Object/Name/Id.");
                return Result.Succeeded;
            }

            var modelDevices = new IoTDeviceRegistry(doc).All().ToList();
            var modelIds = new HashSet<string>(
                modelDevices.Select(d => d.DeviceId).Where(s => !string.IsNullOrEmpty(s)),
                StringComparer.OrdinalIgnoreCase);

            var inStationNotModel = stationIds.Where(s => !modelIds.Contains(s)).OrderBy(s => s).ToList();
            var inModelNotStation = modelIds.Where(m => !stationIds.Contains(m)).OrderBy(s => s).ToList();
            int matched = modelIds.Count(m => stationIds.Contains(m));

            string report = WriteReport(doc, matched, inStationNotModel, inModelNotStation);

            var sb = new StringBuilder();
            sb.AppendLine($"Station points: {stationIds.Count}   Model BMS devices: {modelIds.Count}");
            sb.AppendLine();
            sb.AppendLine($"Matched:                       {matched}");
            sb.AppendLine($"In station, no model element:   {inStationNotModel.Count}");
            sb.AppendLine($"In model, no station point:     {inModelNotStation.Count}");
            if (inModelNotStation.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Model devices missing a station point:");
                foreach (var m in inModelNotStation.Take(10)) sb.AppendLine($"   {m}");
            }
            if (report != null) { sb.AppendLine(); sb.AppendLine("Report: " + report); }

            new TaskDialog("Niagara Reconcile")
            {
                MainInstruction = $"{matched} matched · {inStationNotModel.Count} station-only · {inModelNotStation.Count} model-only",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"Niagara_Reconcile: matched={matched} stationOnly={inStationNotModel.Count} modelOnly={inModelNotStation.Count}");
            return Result.Succeeded;
        }

        private static HashSet<string> ReadIds(string path)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var idHeaders = new[] { "deviceid", "device", "point", "object", "name", "id" };

            if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                using var wb = new XLWorkbook(path);
                var ws = wb.Worksheets.First();
                var used = ws.RangeUsed();
                if (used == null) return ids;
                int fr = used.FirstRow().RowNumber(), lr = used.LastRow().RowNumber();
                int fc = used.FirstColumn().ColumnNumber(), lc = used.LastColumn().ColumnNumber();
                var hdr = new List<string>();
                for (int c = fc; c <= lc; c++) hdr.Add(ws.Cell(fr, c).GetString().Trim().ToLowerInvariant());
                int col = FindCol(hdr, idHeaders);
                if (col < 0) return ids;
                for (int r = fr + 1; r <= lr; r++)
                {
                    string v = ws.Cell(r, fc + col).GetString().Trim();
                    if (v.Length > 0) ids.Add(v);
                }
            }
            else
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length < 2) return ids;
                var hdr = StingToolsApp.ParseCsvLine(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
                int col = FindCol(hdr, idHeaders);
                if (col < 0) return ids;
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var f = StingToolsApp.ParseCsvLine(lines[i]);
                    if (col < f.Length && !string.IsNullOrWhiteSpace(f[col])) ids.Add(f[col].Trim());
                }
            }
            return ids;
        }

        private static int FindCol(List<string> hdr, string[] cands)
        {
            foreach (var cand in cands)
                for (int i = 0; i < hdr.Count; i++)
                    if (!string.IsNullOrEmpty(hdr[i]) && hdr[i].Contains(cand)) return i;
            return -1;
        }

        private static string WriteReport(Document doc, int matched, List<string> stationOnly, List<string> modelOnly)
        {
            try
            {
                var rows = new List<string> { "Type,PointId" };
                rows.Add($"MATCHED_COUNT,{matched}");
                foreach (var s in stationOnly) rows.Add("STATION_ONLY," + "\"" + s.Replace("\"", "\"\"") + "\"");
                foreach (var m in modelOnly) rows.Add("MODEL_ONLY," + "\"" + m.Replace("\"", "\"\"") + "\"");
                string path = OutputLocationHelper.GetOutputPath(doc, $"STING_Niagara_Reconcile_{DateTime.Now:yyyyMMdd}.csv");
                File.WriteAllLines(path, rows, Encoding.UTF8);
                return path;
            }
            catch (Exception ex) { StingLog.Warn("Niagara reconcile report: " + ex.Message); return null; }
        }
    }
}
