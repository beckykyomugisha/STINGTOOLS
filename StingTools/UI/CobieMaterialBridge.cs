using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// N+5 — COBie ↔ Material library sync bridge.
    ///
    /// COBIE_TYPE_MAP.csv carries a free-text <c>Material</c> column per
    /// row (e.g. "Galvanised Steel"). The live Material library is
    /// separate — two truths that drift the moment anyone edits one and
    /// not the other. This bridge keeps them in lockstep.
    ///
    /// Two surfaces
    ///   1) Per-material sync — called from MatCellCommitter after every
    ///      Class / Cost / Carbon edit. Fast O(N) over COBie rows that
    ///      reference the edited material name; rewrites nothing when
    ///      the values already match, so a tab-through is free.
    ///   2) Full audit — exposed as MAT > I/O > "Sync COBie" action.
    ///      Reports COBie rows whose Material text does not resolve to
    ///      any project Material element, and project materials that
    ///      no COBie row references (informational only — many
    ///      materials aren't equipment-typed and shouldn't be).
    ///
    /// Per-project CSV preferred — uses project override path
    /// <c>&lt;project&gt;/_BIM_COORD/COBIE_TYPE_MAP.csv</c> when present;
    /// falls back to the corporate baseline at <c>Data/COBIE_TYPE_MAP.csv</c>.
    /// </summary>
    public class CobieMaterialMismatch
    {
        public string TypeCode { get; set; }
        public string TypeName { get; set; }
        public string CobieMaterial { get; set; }
        public string Resolution { get; set; } // "no project material" | "name drift: '<live name>'" | "OK"
    }

    public class CobieMaterialAuditResult
    {
        public List<CobieMaterialMismatch> Mismatches { get; } = new List<CobieMaterialMismatch>();
        public int RowsScanned { get; set; }
        public string CsvPath { get; set; }
    }

    public static class CobieMaterialBridge
    {
        public const string CsvFileName = "COBIE_TYPE_MAP.csv";

        // ── Lookup ─────────────────────────────────────────────────────────

        /// <summary>Resolve the project-override CSV path first; falls
        /// back to the corporate baseline. Returns null if neither
        /// exists.</summary>
        public static string ResolveCsvPath(Document doc)
        {
            try
            {
                if (doc != null)
                {
                    string proj = Path.Combine(
                        Core.ProjectFolderEngine.GetDataPath(doc, "") ?? "", CsvFileName);
                    if (File.Exists(proj)) return proj;
                }
                return StingToolsApp.FindDataFile(CsvFileName);
            }
            catch (Exception ex) { StingLog.Warn($"CobieMaterialBridge ResolveCsvPath: {ex.Message}"); return null; }
        }

        // ── Per-material sync (hot path) ───────────────────────────────────

        /// <summary>
        /// Called by MatCellCommitter after every inline edit. The only
        /// COBie field that travels with a Material is the Material name
        /// itself (cost / carbon / class don't have COBie counterparts) —
        /// this method exists so a future rename action (or a name-trail
        /// repair) can pivot through one call site.
        ///
        /// Today's body is a no-op when the live material's name matches
        /// every referencing COBie row's Material text. Always cheap.
        /// </summary>
        public static int SyncFromMaterial(Document doc, Material mat)
        {
            if (doc == null || mat == null) return 0;
            string csv = ResolveCsvPath(doc);
            if (string.IsNullOrEmpty(csv) || !File.Exists(csv)) return 0;
            string liveName = mat.Name ?? "";
            try
            {
                var (header, rows) = ReadCsv(csv);
                int iMat = ColIdx(header, "Material");
                int iCode = ColIdx(header, "TypeCode");
                if (iMat < 0) return 0;

                int writes = 0;
                foreach (var row in rows)
                {
                    if (iMat >= row.Length) continue;
                    string raw = (row[iMat] ?? "").Trim();
                    if (string.IsNullOrEmpty(raw)) continue;
                    // Match on case-insensitive equality of the Material
                    // column to the live material name. A rename will land
                    // as a sync only if the OLD name was stored in COBie
                    // (callers tracking the old name should call
                    // RenameInCobie(doc, oldName, newName) instead).
                    if (!string.Equals(raw, liveName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(raw, liveName, StringComparison.Ordinal)) continue; // case-equal already
                    row[iMat] = liveName;
                    writes++;
                }
                if (writes > 0)
                {
                    WriteCsv(csv, header, rows);
                    MaterialAuditLogger.Log(doc, "MAT_CobieSync", mat.Name,
                        new Dictionary<string, object> { ["rowsUpdated"] = writes, ["csv"] = csv });
                    StingLog.Info($"CobieMaterialBridge: synced casing on {writes} COBie row(s) for '{liveName}'");
                }
                return writes;
            }
            catch (Exception ex) { StingLog.Warn($"CobieMaterialBridge.SyncFromMaterial: {ex.Message}"); return 0; }
        }

        /// <summary>Update every COBie row's Material column from
        /// <paramref name="oldName"/> to <paramref name="newName"/>.
        /// Returns the number of rows changed.</summary>
        public static int RenameInCobie(Document doc, string oldName, string newName)
        {
            if (doc == null || string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName)) return 0;
            if (string.Equals(oldName, newName, StringComparison.Ordinal)) return 0;
            string csv = ResolveCsvPath(doc);
            if (string.IsNullOrEmpty(csv) || !File.Exists(csv)) return 0;
            try
            {
                var (header, rows) = ReadCsv(csv);
                int iMat = ColIdx(header, "Material");
                if (iMat < 0) return 0;
                int writes = 0;
                foreach (var row in rows)
                {
                    if (iMat >= row.Length) continue;
                    if (!string.Equals(row[iMat]?.Trim(), oldName, StringComparison.OrdinalIgnoreCase)) continue;
                    row[iMat] = newName;
                    writes++;
                }
                if (writes > 0)
                {
                    WriteCsv(csv, header, rows);
                    MaterialAuditLogger.Log(doc, "MAT_CobieRename", newName,
                        new Dictionary<string, object> { ["oldName"] = oldName, ["rowsUpdated"] = writes });
                }
                return writes;
            }
            catch (Exception ex) { StingLog.Warn($"CobieMaterialBridge.RenameInCobie: {ex.Message}"); return 0; }
        }

        // ── Full audit (on demand) ─────────────────────────────────────────

        public static CobieMaterialAuditResult Audit(Document doc)
        {
            var result = new CobieMaterialAuditResult();
            if (doc == null) return result;
            string csv = ResolveCsvPath(doc);
            result.CsvPath = csv;
            if (string.IsNullOrEmpty(csv) || !File.Exists(csv)) return result;

            try
            {
                var liveNames = new HashSet<string>(
                    new FilteredElementCollector(doc).OfClass(typeof(Material))
                        .Cast<Material>().Select(m => m.Name ?? ""),
                    StringComparer.OrdinalIgnoreCase);
                // Also build a case-aware lookup so we can report "name drift"
                // (right name, wrong case) separately from "missing".
                var liveExact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>())
                    liveExact[m.Name ?? ""] = m.Name ?? "";

                var (header, rows) = ReadCsv(csv);
                int iMat = ColIdx(header, "Material");
                int iCode = ColIdx(header, "TypeCode");
                int iName = ColIdx(header, "TypeName");
                if (iMat < 0) return result;
                result.RowsScanned = rows.Count;
                foreach (var row in rows)
                {
                    string raw = iMat < row.Length ? (row[iMat] ?? "").Trim() : "";
                    if (string.IsNullOrEmpty(raw)) continue;
                    if (!liveNames.Contains(raw))
                    {
                        result.Mismatches.Add(new CobieMaterialMismatch
                        {
                            TypeCode = iCode >= 0 && iCode < row.Length ? row[iCode] : "",
                            TypeName = iName >= 0 && iName < row.Length ? row[iName] : "",
                            CobieMaterial = raw,
                            Resolution = "no project material",
                        });
                    }
                    else if (liveExact.TryGetValue(raw, out string canonical) &&
                             !string.Equals(raw, canonical, StringComparison.Ordinal))
                    {
                        result.Mismatches.Add(new CobieMaterialMismatch
                        {
                            TypeCode = iCode >= 0 && iCode < row.Length ? row[iCode] : "",
                            TypeName = iName >= 0 && iName < row.Length ? row[iName] : "",
                            CobieMaterial = raw,
                            Resolution = $"name drift: '{canonical}'",
                        });
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"CobieMaterialBridge.Audit: {ex.Message}"); }
            return result;
        }

        // ── CSV I/O ────────────────────────────────────────────────────────

        private static (string[] header, List<string[]> rows) ReadCsv(string path)
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0) return (Array.Empty<string>(), new List<string[]>());
            var header = StingToolsApp.ParseCsvLine(lines[0]);
            var rows = new List<string[]>(lines.Length - 1);
            for (int i = 1; i < lines.Length; i++)
            {
                var parsed = StingToolsApp.ParseCsvLine(lines[i]);
                if (parsed != null && parsed.Length > 0) rows.Add(parsed);
            }
            return (header, rows);
        }

        private static void WriteCsv(string path, string[] header, List<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", header.Select(EscapeCsv)));
            foreach (var row in rows)
                sb.AppendLine(string.Join(",", row.Select(EscapeCsv)));
            File.WriteAllText(path, sb.ToString());
        }

        private static int ColIdx(string[] header, string name)
            => Array.FindIndex(header, h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));

        private static string EscapeCsv(string s)
        {
            if (s == null) return "";
            bool needsQuote = s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            string esc = s.Replace("\"", "\"\"");
            return needsQuote ? "\"" + esc + "\"" : esc;
        }
    }
}
