// StingTools — Matrix Excel round-trip (M3): auto-model from a design sheet.
//
// Export the current grid to .xlsx and re-import a design sheet (rooms x element
// counts) to populate the grid. Reuses ClosedXML (already a project dependency).
// Round-trips losslessly via a companion "Columns" sheet that fully describes each
// element-type column; a hand-authored sheet without it is matched tolerantly
// (header -> category, row -> room-type) and reported.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using ClosedXML.Excel;

namespace StingTools.Core.Placement.Matrix
{
    public sealed class MatrixImportResult
    {
        public bool Ok;
        public int RowsMatched;
        public int RowsUnmatched;
        public int ColumnsMatched;
        public int ColumnsUnmatched;
        public int CellsSet;
        public List<string> UnmatchedRooms = new List<string>();
        public List<string> UnmatchedColumns = new List<string>();
        public List<string> Messages = new List<string>();
    }

    public static class MatrixExcel
    {
        private const string MatrixSheet = "Matrix";
        private const string ColumnsSheet = "Columns";

        // ── Export ──────────────────────────────────────────────────────────
        public static void Export(Document doc, MatrixDocument matrix, MatrixScanResult scan, string path)
        {
            if (matrix == null || scan == null || string.IsNullOrEmpty(path)) return;
            using (var wb = new XLWorkbook())
            {
                var cols = matrix.Columns ?? new List<MatrixColumnDef>();

                // Columns sheet — lossless column definition (import reads this first).
                var cs = wb.Worksheets.Add(ColumnsSheet);
                string[] chdr = { "Id", "Label", "Category", "Variant", "Anchor", "MountingHeightMm", "HeightStandard", "AutoGrid", "LoadVaOverride" };
                for (int i = 0; i < chdr.Length; i++) cs.Cell(1, i + 1).Value = chdr[i];
                cs.Row(1).Style.Font.Bold = true;
                for (int r = 0; r < cols.Count; r++)
                {
                    var c = cols[r];
                    cs.Cell(r + 2, 1).Value = c.Id;
                    cs.Cell(r + 2, 2).Value = c.DisplayLabel();
                    cs.Cell(r + 2, 3).Value = c.Category;
                    cs.Cell(r + 2, 4).Value = c.Variant;
                    cs.Cell(r + 2, 5).Value = c.Anchor;
                    cs.Cell(r + 2, 6).Value = c.MountingHeightMm;
                    cs.Cell(r + 2, 7).Value = c.HeightStandard;
                    cs.Cell(r + 2, 8).Value = c.AutoGrid;
                    cs.Cell(r + 2, 9).Value = c.LoadVaOverride;
                }
                cs.Columns().AdjustToContents();

                // Matrix sheet — room-types x counts.
                var ms = wb.Worksheets.Add(MatrixSheet);
                ms.Cell(1, 1).Value = "Room Type";
                ms.Cell(1, 2).Value = "Rooms";
                ms.Cell(1, 3).Value = "Area m2";
                int col = 4;
                foreach (var c in cols) ms.Cell(1, col++).Value = c.DisplayLabel();
                ms.Cell(1, col).Value = "Est Load VA";
                ms.Row(1).Style.Font.Bold = true;

                var typeByKey = scan.Types.ToDictionary(t => t.Key, t => t, StringComparer.OrdinalIgnoreCase);
                int row = 2;
                foreach (var t in matrix.RoomTypes ?? new List<MatrixTypeCounts>())
                {
                    typeByKey.TryGetValue(t.Key, out var st);
                    ms.Cell(row, 1).Value = t.Key;
                    ms.Cell(row, 2).Value = st?.PlaceableCount ?? 0;
                    ms.Cell(row, 3).Value = Math.Round(st?.TypicalAreaM2 ?? 0, 1);
                    col = 4;
                    double loadVa = 0;
                    foreach (var c in cols)
                    {
                        int n = t.Cells != null && t.Cells.TryGetValue(c.Id, out var v) ? v : 0;
                        ms.Cell(row, col++).Value = n;
                        double per = c.LoadVaOverride > 0 ? c.LoadVaOverride : MatrixDefaults.LoadVa(doc, c.Category);
                        loadVa += n * per * (st?.PlaceableCount ?? 1);
                    }
                    ms.Cell(row, col).Value = Math.Round(loadVa, 0);
                    row++;
                }
                ms.Columns().AdjustToContents();

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                wb.SaveAs(path);
            }
        }

        // ── Import ──────────────────────────────────────────────────────────
        /// <summary>Populate <paramref name="into"/> from an .xlsx. Reconstructs columns from the
        /// "Columns" sheet when present; otherwise maps header cells to categories tolerantly.
        /// Room rows are matched to room-types by normalised name. Never places — the caller
        /// previews then places.</summary>
        public static MatrixImportResult Import(
            Document doc, string path, MatrixDocument into, MatrixScanResult scan,
            IEnumerable<string> allowedCategories)
        {
            var res = new MatrixImportResult();
            if (into == null || string.IsNullOrEmpty(path) || !File.Exists(path))
            { res.Messages.Add("File not found."); return res; }

            var allowed = new HashSet<string>(allowedCategories ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var wb = new XLWorkbook(path))
                {
                    var ms = wb.Worksheets.FirstOrDefault(w => w.Name.Equals(MatrixSheet, StringComparison.OrdinalIgnoreCase))
                          ?? wb.Worksheets.FirstOrDefault();
                    if (ms == null) { res.Messages.Add("No worksheet found."); return res; }

                    // 1) Reconstruct columns from the Columns sheet if present (lossless).
                    var cs = wb.Worksheets.FirstOrDefault(w => w.Name.Equals(ColumnsSheet, StringComparison.OrdinalIgnoreCase));
                    if (cs != null) RebuildColumnsFromSheet(cs, into, allowed, res);

                    // 2) Map the Matrix header row -> column ids (label match, else category fuzzy).
                    var headerRow = ms.Row(1);
                    int lastCol = ms.LastColumnUsed()?.ColumnNumber() ?? 3;
                    var colIdByIndex = new Dictionary<int, string>();
                    for (int cix = 4; cix <= lastCol; cix++)
                    {
                        string hdr = SafeStr(headerRow.Cell(cix));
                        if (string.IsNullOrWhiteSpace(hdr)) continue;
                        if (hdr.IndexOf("Est Load", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        string colId = MatchOrCreateColumn(hdr, into, allowed, res);
                        if (colId != null) colIdByIndex[cix] = colId;
                    }

                    // 3) Rows -> room-types.
                    var typeKeys = new HashSet<string>(
                        scan.Types.Select(t => t.Key), StringComparer.OrdinalIgnoreCase);
                    int lastRow = ms.LastRowUsed()?.RowNumber() ?? 1;
                    for (int r = 2; r <= lastRow; r++)
                    {
                        string roomName = SafeStr(ms.Cell(r, 1));
                        if (string.IsNullOrWhiteSpace(roomName)) continue;
                        string key = ResolveTypeKey(roomName, typeKeys);
                        if (key == null) { res.RowsUnmatched++; res.UnmatchedRooms.Add(roomName); continue; }
                        res.RowsMatched++;
                        var tc = into.Type(key);
                        if (tc == null) { tc = new MatrixTypeCounts { Key = key }; into.RoomTypes.Add(tc); }
                        foreach (var kv in colIdByIndex)
                        {
                            int n = SafeInt(ms.Cell(r, kv.Key));
                            if (n < 0) n = 0;
                            tc.Cells[kv.Value] = n;
                            if (n > 0) res.CellsSet++;
                        }
                    }
                }
                res.Ok = true;
                res.Messages.Add($"Imported: {res.RowsMatched} room-type row(s), {res.ColumnsMatched} column(s), {res.CellsSet} non-zero cell(s).");
                if (res.RowsUnmatched > 0)
                    res.Messages.Add($"{res.RowsUnmatched} unmatched row(s): {string.Join(", ", res.UnmatchedRooms.Take(10))}{(res.UnmatchedRooms.Count > 10 ? ", ..." : "")}");
                if (res.ColumnsUnmatched > 0)
                    res.Messages.Add($"{res.ColumnsUnmatched} unmatched column(s): {string.Join(", ", res.UnmatchedColumns.Take(10))}");
            }
            catch (Exception ex)
            {
                StingLog.Error("MatrixExcel.Import", ex);
                res.Messages.Add($"Import failed: {ex.Message}");
            }
            return res;
        }

        private static void RebuildColumnsFromSheet(
            IXLWorksheet cs, MatrixDocument into, HashSet<string> allowed, MatrixImportResult res)
        {
            int last = cs.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 2; r <= last; r++)
            {
                string cat = SafeStr(cs.Cell(r, 3));
                if (string.IsNullOrWhiteSpace(cat)) continue;
                if (allowed.Count > 0 && !allowed.Contains(cat))
                { res.ColumnsUnmatched++; res.UnmatchedColumns.Add(cat + " (not in allowlist)"); continue; }
                string id = SafeStr(cs.Cell(r, 1));
                if (string.IsNullOrWhiteSpace(id) || into.Column(id) != null) id = into.NextColumnId();
                var def = new MatrixColumnDef
                {
                    Id = id,
                    Label = SafeStr(cs.Cell(r, 2)),
                    Category = cat,
                    Variant = SafeStr(cs.Cell(r, 4)),
                    Anchor = SafeStr(cs.Cell(r, 5)),
                    MountingHeightMm = SafeDouble(cs.Cell(r, 6)),
                    HeightStandard = SafeStr(cs.Cell(r, 7)),
                    AutoGrid = SafeBool(cs.Cell(r, 8), true),
                    LoadVaOverride = SafeDouble(cs.Cell(r, 9))
                };
                if (string.IsNullOrWhiteSpace(def.Anchor)) def.Anchor = MatrixDefaults.DefaultAnchor(cat, def.AutoGrid);
                into.Columns.Add(def);
                res.ColumnsMatched++;
            }
        }

        // Match a header to an existing column (by label), else create a column when the header
        // resolves to an allowlisted category (exact, then substring).
        private static string MatchOrCreateColumn(
            string header, MatrixDocument into, HashSet<string> allowed, MatrixImportResult res)
        {
            var existing = into.Columns.FirstOrDefault(c =>
                string.Equals(c.DisplayLabel(), header, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Label, header, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Category, header, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { res.ColumnsMatched++; return existing.Id; }

            string cat = ResolveCategory(header, allowed);
            if (cat == null) { res.ColumnsUnmatched++; res.UnmatchedColumns.Add(header); return null; }
            var def = new MatrixColumnDef
            {
                Id = into.NextColumnId(), Category = cat, Label = header,
                Anchor = MatrixDefaults.DefaultAnchor(cat, true), AutoGrid = true
            };
            into.Columns.Add(def);
            res.ColumnsMatched++;
            return def.Id;
        }

        private static string ResolveCategory(string header, HashSet<string> allowed)
        {
            if (allowed == null || allowed.Count == 0) return null;
            var exact = allowed.FirstOrDefault(a => string.Equals(a, header, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            string h = Norm(header);
            // header contains a category name, or a category name contains the header token
            var byContains = allowed.FirstOrDefault(a => Norm(a).Contains(h) || h.Contains(Norm(a)));
            return byContains;
        }

        private static string ResolveTypeKey(string roomName, HashSet<string> typeKeys)
        {
            var direct = typeKeys.FirstOrDefault(k => string.Equals(k, roomName, StringComparison.OrdinalIgnoreCase));
            if (direct != null) return direct;
            string norm = MatrixRoomScanner.NormaliseTypeKey(roomName);
            var byNorm = typeKeys.FirstOrDefault(k => string.Equals(k, norm, StringComparison.OrdinalIgnoreCase));
            if (byNorm != null) return byNorm;
            // last resort: substring match
            string n = Norm(norm);
            return typeKeys.FirstOrDefault(k => Norm(k).Contains(n) || n.Contains(Norm(k)));
        }

        private static string Norm(string s) => (s ?? "").Trim().ToLowerInvariant();
        private static string SafeStr(IXLCell c) { try { return c.GetString()?.Trim() ?? ""; } catch { return ""; } }
        private static int SafeInt(IXLCell c) { try { return (int)Math.Round(c.GetDouble()); } catch { return int.TryParse(SafeStr(c), out var v) ? v : 0; } }
        private static double SafeDouble(IXLCell c) { try { return c.GetDouble(); } catch { return double.TryParse(SafeStr(c), out var v) ? v : 0; } }
        private static bool SafeBool(IXLCell c, bool dflt) { try { return c.GetBoolean(); } catch { var s = SafeStr(c); return string.IsNullOrEmpty(s) ? dflt : (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1"); } }
    }
}
