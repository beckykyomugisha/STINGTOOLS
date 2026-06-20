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

namespace StingTools.ExLink
{
    // ─────────────────────────────────────────────────────────────────────────
    // Fohlio finishes mode (#16) — room floor / wall / ceiling / base finishes
    // round-trip with Fohlio (link, never duplicate; matched by Room Number).
    //
    // Fohlio_ExportFinishes  emit the room-finish schedule (CSV) in Fohlio shape.
    // Fohlio_ImportFinishes  read a Fohlio finishes export, preview the diff, then
    //                        write the four built-in room finish params + FOHLIO_REF.
    //
    // Complements the FF&E-object commands in FohlioCommands.cs.
    // ─────────────────────────────────────────────────────────────────────────
    internal static class FohlioFinishes
    {
        public static readonly (string header, BuiltInParameter bip)[] FinishCols =
        {
            ("Floor Finish",   BuiltInParameter.ROOM_FINISH_FLOOR),
            ("Wall Finish",    BuiltInParameter.ROOM_FINISH_WALL),
            ("Ceiling Finish", BuiltInParameter.ROOM_FINISH_CEILING),
            ("Base Finish",    BuiltInParameter.ROOM_FINISH_BASE),
        };

        public static List<Element> Rooms(Document doc) =>
            new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Where(r => (r.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0) > 0) // placed only
                .ToList();

        public static string Bi(Element r, BuiltInParameter bip) => r.get_Parameter(bip)?.AsString() ?? "";
        public static string Num(Element r) => r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
        public static string Nm(Element r)  => r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
        public static string Csv(string s)  => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class FohlioExportFinishesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var rooms = FohlioFinishes.Rooms(doc);
            if (rooms.Count == 0) { TaskDialog.Show("Fohlio Finishes Export", "No placed rooms found."); return Result.Succeeded; }

            string path;
            try
            {
                var header = new List<string> { "Room Number", "Room Name" };
                header.AddRange(FohlioFinishes.FinishCols.Select(c => c.header));
                header.Add("Area m2"); header.Add("Fohlio Ref");
                var rows = new List<string> { string.Join(",", header.Select(FohlioFinishes.Csv)) };
                foreach (var r in rooms.OrderBy(FohlioFinishes.Num))
                {
                    var cells = new List<string> { FohlioFinishes.Num(r), FohlioFinishes.Nm(r) };
                    cells.AddRange(FohlioFinishes.FinishCols.Select(c => FohlioFinishes.Bi(r, c.bip)));
                    double areaSf = r.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0;
                    cells.Add((areaSf * 0.09290304).ToString("F2"));   // ft² → m²
                    cells.Add(ParameterHelpers.GetString(r, ParamRegistry.FOHLIO_REF));
                    rows.Add(string.Join(",", cells.Select(FohlioFinishes.Csv)));
                }
                path = OutputLocationHelper.GetOutputPath(doc, $"STING_Fohlio_Finishes_{DateTime.Now:yyyyMMdd}.csv");
                File.WriteAllLines(path, rows, Encoding.UTF8);
            }
            catch (Exception ex) { TaskDialog.Show("Fohlio Finishes Export", "Export failed:\n" + ex.Message); return Result.Failed; }

            new TaskDialog("Fohlio Finishes Export")
            {
                MainInstruction = $"Exported finishes for {rooms.Count} room(s)",
                MainContent = "Columns: Room Number, Room Name, Floor/Wall/Ceiling/Base Finish, Area m², Fohlio Ref.\n\n" +
                              $"CSV: {path}\n\nUpdate finishes in Fohlio, then run Fohlio Import Finishes to write them " +
                              "back (matched by Room Number)."
            }.Show();
            StingLog.Info($"Fohlio_ExportFinishes: {rooms.Count} rooms → {path}");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FohlioImportFinishesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select the Fohlio finishes export (CSV or XLSX with a Room Number column)",
                Filter = "Fohlio finishes (*.csv;*.xlsx)|*.csv;*.xlsx",
                InitialDirectory = OutputLocationHelper.GetOutputDirectory(doc)
            };
            if (dlg.ShowDialog() != true) return Result.Cancelled;

            List<Dictionary<string, string>> rows;
            try { rows = ReadRows(dlg.FileName); }
            catch (Exception ex) { TaskDialog.Show("Fohlio Import Finishes", "Read failed:\n" + ex.Message); return Result.Failed; }

            var byNum = new Dictionary<string, Element>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in FohlioFinishes.Rooms(doc))
            {
                var n = FohlioFinishes.Num(r);
                if (!string.IsNullOrEmpty(n) && !byNum.ContainsKey(n)) byNum[n] = r;
            }

            var changes = new List<(Element room, string label, BuiltInParameter? bip, string param, string oldV, string newV)>();
            int matched = 0, unmatched = 0;
            foreach (var row in rows)
            {
                if (!row.TryGetValue("Room Number", out string num) || string.IsNullOrEmpty(num)) { unmatched++; continue; }
                if (!byNum.TryGetValue(num.Trim(), out var room)) { unmatched++; continue; }
                matched++;
                foreach (var c in FohlioFinishes.FinishCols)
                {
                    if (!row.TryGetValue(c.header, out string nv)) continue;
                    nv = (nv ?? "").Trim();
                    if (nv.Length == 0) continue;
                    string ov = FohlioFinishes.Bi(room, c.bip);
                    if (!string.Equals(ov, nv, StringComparison.Ordinal))
                        changes.Add((room, c.header, c.bip, null, ov, nv));
                }
                if (row.TryGetValue("Fohlio Ref", out string fref) && !string.IsNullOrWhiteSpace(fref))
                {
                    string ov = ParameterHelpers.GetString(room, ParamRegistry.FOHLIO_REF);
                    if (!string.Equals(ov, fref.Trim(), StringComparison.Ordinal))
                        changes.Add((room, "Fohlio Ref", null, ParamRegistry.FOHLIO_REF, ov, fref.Trim()));
                }
            }

            if (changes.Count == 0)
            {
                TaskDialog.Show("Fohlio Import Finishes", $"Matched {matched} room(s), {unmatched} unmatched. No finish changes to write.");
                return Result.Succeeded;
            }

            var prev = new StringBuilder();
            prev.AppendLine($"Matched {matched} room(s) by Room Number — {unmatched} unmatched.");
            prev.AppendLine($"{changes.Count} finish change(s) proposed.");
            prev.AppendLine();
            foreach (var c in changes.Take(15)) prev.AppendLine($"  {FohlioFinishes.Num(c.room)} {c.label}: '{c.oldV}' → '{c.newV}'");
            if (changes.Count > 15) prev.AppendLine($"  … +{changes.Count - 15} more");

            var confirm = new TaskDialog("Fohlio Import Finishes — preview")
            {
                MainInstruction = "Review before writing to the model",
                MainContent = prev.ToString(),
                CommonButtons = TaskDialogCommonButtons.Cancel,
                AllowCancellation = true
            };
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Apply — fill empty only", "Write only where the room value is currently blank");
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Apply — overwrite", "Overwrite existing room finish values");
            var choice = confirm.Show();
            if (choice == TaskDialogResult.Cancel) return Result.Cancelled;
            bool overwrite = choice == TaskDialogResult.CommandLink2;

            int written = 0;
            using (var t = new Transaction(doc, "STING Fohlio Import Finishes"))
            {
                t.Start();
                foreach (var c in changes)
                {
                    if (!TagPipelineHelper.IsEditableInWorksharing(doc, c.room)) continue;
                    bool ok;
                    if (c.bip.HasValue)
                    {
                        var p = c.room.get_Parameter(c.bip.Value);
                        if (p == null || p.IsReadOnly) continue;
                        if (!overwrite && !string.IsNullOrEmpty(p.AsString())) continue;
                        ok = p.Set(c.newV);
                    }
                    else
                    {
                        ok = overwrite
                            ? ParameterHelpers.SetString(c.room, c.param, c.newV, overwrite: true)
                            : ParameterHelpers.SetIfEmpty(c.room, c.param, c.newV);
                    }
                    if (ok) written++;
                }
                t.Commit();
            }

            new TaskDialog("Fohlio Import Finishes")
            {
                MainInstruction = $"Wrote {written} finish value(s) ({(overwrite ? "overwrite" : "fill empty")})",
                MainContent = $"Matched: {matched}\nUnmatched: {unmatched}\n\n" +
                              "Fohlio remains authoritative for finishes; FOHLIO_REF links each room."
            }.Show();
            StingLog.Info($"Fohlio_ImportFinishes: matched={matched} wrote={written}");
            return Result.Succeeded;
        }

        private static List<Dictionary<string, string>> ReadRows(string path)
        {
            var rows = new List<Dictionary<string, string>>();
            var want = new List<string> { "Room Number", "Floor Finish", "Wall Finish", "Ceiling Finish", "Base Finish", "Fohlio Ref" };
            if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                using var wb = new XLWorkbook(path);
                var ws = wb.Worksheets.First();
                var used = ws.RangeUsed();
                if (used == null) return rows;
                int fr = used.FirstRow().RowNumber(), lr = used.LastRow().RowNumber();
                int fc = used.FirstColumn().ColumnNumber(), lc = used.LastColumn().ColumnNumber();
                var hdr = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int c = fc; c <= lc; c++) hdr[ws.Cell(fr, c).GetString().Trim()] = c;
                for (int r = fr + 1; r <= lr; r++)
                {
                    var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var h in want) if (hdr.TryGetValue(h, out int c)) row[h] = ws.Cell(r, c).GetString();
                    rows.Add(row);
                }
            }
            else
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length < 2) return rows;
                var hf = StingToolsApp.ParseCsvLine(lines[0]);
                var hdr = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < hf.Length; i++) hdr[hf[i].Trim()] = i;
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var f = StingToolsApp.ParseCsvLine(lines[i]);
                    var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var h in want) if (hdr.TryGetValue(h, out int c) && c < f.Length) row[h] = f[c];
                    rows.Add(row);
                }
            }
            return rows;
        }
    }
}
