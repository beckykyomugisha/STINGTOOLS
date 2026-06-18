using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Validation;

namespace StingTools.Commands.Validation
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (B3) — Program Audit comparator command.
    //
    // Audits model rooms against the Owner's live program Excel template
    // (A1 §19). Reads the template (header-forgiving, configurable via
    // _BIM_COORD/program_audit_map.json), reads placed rooms, calls the pure
    // ProgramAuditEngine, and writes an XLSX deficiency log with a Status column.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Configurable, header-forgiving column mapping for the program template.</summary>
    public class ProgramAuditMap
    {
        public List<string> RoomNameHeaders { get; set; } = new List<string> { "room name", "name", "space name", "room" };
        public List<string> RoomNumberHeaders { get; set; } = new List<string> { "room number", "number", "room no", "no", "rm number" };
        public List<string> RequiredAreaHeaders { get; set; } = new List<string> { "required area", "area", "net area", "nia", "target area", "program area" };
        public List<string> DepartmentHeaders { get; set; } = new List<string> { "department", "zone", "dept" };
        public List<string> RequiredCountHeaders { get; set; } = new List<string> { "required count", "count", "qty", "quantity", "number of rooms" };
        public List<string> BuildingHeaders { get; set; } = new List<string> { "building", "volume", "block", "loc" };
        public string AreaUnit { get; set; } = "m2";   // "m2" or "ft2"
        public double TolerancePct { get; set; } = 5.0;

        public static ProgramAuditMap Load(Document doc)
        {
            var map = new ProgramAuditMap();
            try
            {
                string dir = Path.GetDirectoryName(doc?.PathName ?? "");
                if (!string.IsNullOrEmpty(dir))
                {
                    string p = Path.Combine(dir, "_BIM_COORD", "program_audit_map.json");
                    if (File.Exists(p))
                    {
                        var overlay = JsonConvert.DeserializeObject<ProgramAuditMap>(File.ReadAllText(p));
                        if (overlay != null) { map = overlay; StingLog.Info($"ProgramAudit: map overlay from {p}"); }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ProgramAudit map load: {ex.Message}"); }
            return map;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ProgramAuditCommand : IExternalCommand
    {
        private const double SqFtToM2 = 0.09290304;

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select the Owner program template (Excel)",
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                InitialDirectory = OutputLocationHelper.GetOutputDirectory(doc)
            };
            if (dlg.ShowDialog() != true) return Result.Cancelled;

            var map = ProgramAuditMap.Load(doc);

            List<ProgramRow> program;
            try { program = ReadProgram(dlg.FileName, map, out string headerNote); if (headerNote != null) StingLog.Info(headerNote); }
            catch (Exception ex)
            {
                TaskDialog.Show("Program Audit", $"Could not read the template:\n{ex.Message}");
                return Result.Failed;
            }
            if (program.Count == 0)
            {
                TaskDialog.Show("Program Audit",
                    "No program rows read. Check the template has a header row with a Room Name " +
                    "column (configurable via _BIM_COORD/program_audit_map.json).");
                return Result.Succeeded;
            }

            var rooms = ReadRooms(doc);
            var result = ProgramAuditEngine.Compare(program, rooms, map.TolerancePct);

            string xlsx = WriteDeficiencyLog(doc, result);

            var sb = new StringBuilder();
            sb.AppendLine($"Template: {Path.GetFileName(dlg.FileName)}  ({program.Count} program rows, unit {map.AreaUnit})");
            sb.AppendLine($"Model: {rooms.Count} placed room(s)   tolerance ±{map.TolerancePct:F0}%");
            sb.AppendLine();
            sb.AppendLine($"Compliant:        {result.Compliant}");
            sb.AppendLine($"Over area:        {result.Over}");
            sb.AppendLine($"Under area:       {result.Under}");
            sb.AppendLine($"Missing (in template, not model): {result.Missing}");
            sb.AppendLine($"Extra (in model, not program):    {result.Extra}");
            if (result.CountMismatch > 0)
                sb.AppendLine($"Count mismatches: {result.CountMismatch}");
            sb.AppendLine();
            var firstIssues = result.Rows
                .Where(r => r.Status != ProgramAuditStatus.Compliant)
                .Take(15).ToList();
            if (firstIssues.Count > 0)
            {
                sb.AppendLine("First deficiencies:");
                foreach (var r in firstIssues)
                    sb.AppendLine($"   [{result.StatusName(r.Status)}] {r.RoomNumber} {r.RoomName}" +
                                  (r.DeltaPct.HasValue ? $"  Δ{r.DeltaPct:+0.0;-0.0}%" : "") +
                                  (string.IsNullOrEmpty(r.Note) ? "" : $"  ({r.Note})"));
            }
            if (xlsx != null) { sb.AppendLine(); sb.AppendLine($"Deficiency log: {xlsx}"); }

            new TaskDialog("Program Audit")
            {
                MainInstruction = $"{result.Compliant}/{program.Count} compliant — " +
                                  $"{result.Missing} missing, {result.Extra} extra",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"Program_Audit: {result.Compliant} compliant, {result.Missing} missing, {result.Extra} extra");
            return Result.Succeeded;
        }

        private static List<ProgramRow> ReadProgram(string path, ProgramAuditMap map, out string headerNote)
        {
            headerNote = null;
            var rows = new List<ProgramRow>();
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheets.First();
            var used = ws.RangeUsed();
            if (used == null) return rows;

            int firstRow = used.FirstRow().RowNumber();
            int lastRow = used.LastRow().RowNumber();
            int firstCol = used.FirstColumn().ColumnNumber();
            int lastCol = used.LastColumn().ColumnNumber();

            // Header row = the used range's first row. Map by normalised header text.
            var headerByCol = new Dictionary<int, string>();
            for (int c = firstCol; c <= lastCol; c++)
                headerByCol[c] = NormHeader(ws.Cell(firstRow, c).GetString());

            int colName = FindCol(headerByCol, map.RoomNameHeaders);
            int colNumber = FindCol(headerByCol, map.RoomNumberHeaders);
            int colArea = FindCol(headerByCol, map.RequiredAreaHeaders);
            int colDept = FindCol(headerByCol, map.DepartmentHeaders);
            int colCount = FindCol(headerByCol, map.RequiredCountHeaders);
            int colBldg = FindCol(headerByCol, map.BuildingHeaders);
            headerNote = $"ProgramAudit headers: name={colName} number={colNumber} area={colArea} dept={colDept} count={colCount} bldg={colBldg}";

            bool ft2 = (map.AreaUnit ?? "m2").Trim().Equals("ft2", StringComparison.OrdinalIgnoreCase);

            for (int r = firstRow + 1; r <= lastRow; r++)
            {
                string name = colName > 0 ? ws.Cell(r, colName).GetString().Trim() : "";
                string number = colNumber > 0 ? ws.Cell(r, colNumber).GetString().Trim() : "";
                if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(number)) continue;

                var pr = new ProgramRow
                {
                    RoomName = name,
                    RoomNumber = number,
                    Department = colDept > 0 ? ws.Cell(r, colDept).GetString().Trim() : "",
                    Building = colBldg > 0 ? ws.Cell(r, colBldg).GetString().Trim() : "",
                    SourceRowIndex = r,
                };
                if (colArea > 0 && ws.Cell(r, colArea).TryGetValue(out double area) && area > 0)
                    pr.RequiredAreaM2 = ft2 ? area * SqFtToM2 : area;
                if (colCount > 0 && ws.Cell(r, colCount).TryGetValue(out double cnt) && cnt > 0)
                    pr.RequiredCount = (int)Math.Round(cnt);
                rows.Add(pr);
            }
            return rows;
        }

        private static List<ModelRoomRow> ReadRooms(Document doc)
        {
            var rooms = new List<ModelRoomRow>();
            var collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType();
            foreach (var e in collector)
            {
                if (!(e is Room room)) continue;
                if (room.Area <= 1e-6 || room.Location == null) continue; // unplaced/redundant
                rooms.Add(new ModelRoomRow
                {
                    Name = room.Name ?? "",
                    Number = room.Number ?? "",
                    Loc = ParameterHelpers.GetString(room, ParamRegistry.LOC),
                    AreaM2 = room.Area * SqFtToM2,
                    ElementId = room.Id.Value,
                });
            }
            return rooms;
        }

        private static string WriteDeficiencyLog(Document doc, ProgramAuditResult r)
        {
            try
            {
                string path = OutputLocationHelper.GetOutputPath(doc,
                    $"STING_ProgramAudit_{DateTime.Now:yyyyMMdd}.xlsx");
                using var wb = new XLWorkbook();
                var ws = wb.AddWorksheet("Program Audit");
                string[] headers = { "Status", "Room Number", "Room Name", "Building",
                    "Required Area (m²)", "Actual Area (m²)", "Δ %", "Required Count", "Actual Count", "Note", "ElementId" };
                for (int c = 0; c < headers.Length; c++)
                {
                    var cell = ws.Cell(1, c + 1);
                    cell.Value = headers[c];
                    cell.Style.Font.Bold = true;
                }
                int row = 2;
                foreach (var x in r.Rows.OrderBy(a => a.Status == ProgramAuditStatus.Compliant).ThenBy(a => a.RoomNumber))
                {
                    ws.Cell(row, 1).Value = r.StatusName(x.Status);
                    ws.Cell(row, 2).Value = x.RoomNumber;
                    ws.Cell(row, 3).Value = x.RoomName;
                    ws.Cell(row, 4).Value = x.Building;
                    if (x.RequiredAreaM2.HasValue) ws.Cell(row, 5).Value = Math.Round(x.RequiredAreaM2.Value, 2);
                    if (x.ActualAreaM2.HasValue) ws.Cell(row, 6).Value = Math.Round(x.ActualAreaM2.Value, 2);
                    if (x.DeltaPct.HasValue) ws.Cell(row, 7).Value = x.DeltaPct.Value;
                    if (x.RequiredCount.HasValue) ws.Cell(row, 8).Value = x.RequiredCount.Value;
                    if (x.ActualCount.HasValue) ws.Cell(row, 9).Value = x.ActualCount.Value;
                    ws.Cell(row, 10).Value = x.Note;
                    if (x.ModelElementId != 0) ws.Cell(row, 11).Value = x.ModelElementId.ToString();
                    row++;
                }
                ws.Columns().AdjustToContents();
                wb.SaveAs(path);
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"ProgramAudit XLSX write: {ex.Message}"); return null; }
        }

        private static string NormHeader(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return new string(s.Where(c => !char.IsWhiteSpace(c) || c == ' ').ToArray())
                .Trim().ToLowerInvariant();
        }

        // Find the first column whose normalised header contains any of the candidate
        // keywords (longest candidate first so "room number" beats "room").
        private static int FindCol(Dictionary<int, string> headerByCol, List<string> candidates)
        {
            foreach (var cand in candidates.OrderByDescending(c => c.Length))
            {
                string nc = cand.Trim().ToLowerInvariant();
                foreach (var kv in headerByCol)
                    if (!string.IsNullOrEmpty(kv.Value) && kv.Value.Contains(nc))
                        return kv.Key;
            }
            return -1;
        }
    }
}
