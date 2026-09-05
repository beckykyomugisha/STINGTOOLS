using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Model
{
    /// <summary>
    /// One row of the room finish code legend — a project-facing finish code joined to a
    /// real material in <c>BLE_MATERIALS.csv</c>.
    /// </summary>
    public class FinishCode
    {
        /// <summary>Project-facing code, e.g. FL-01 / WL-01 / CL-01 / SK-01.</summary>
        public string Code { get; set; }
        /// <summary>FLOOR / WALL / CEILING / BASE.</summary>
        public string Surface { get; set; }
        /// <summary>Human description written into the prose finish parameter.</summary>
        public string Description { get; set; }
        /// <summary>MAT_CODE joining this code to a row of BLE_MATERIALS.csv.</summary>
        public string MatCode { get; set; }
        /// <summary>MAT_NAME as it appears in BLE_MATERIALS.csv.</summary>
        public string MatName { get; set; }
        /// <summary>Nominal finish thickness in millimetres (0 when not stated).</summary>
        public double ThicknessMm { get; set; }
        /// <summary>Preferred Revit type name for the finish element. Blank for non-floor surfaces.</summary>
        public string RevitTypeHint { get; set; }
        /// <summary>Free-text note carried through to reports.</summary>
        public string Notes { get; set; }

        public bool IsFloor =>
            string.Equals(Surface, "FLOOR", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Loads the room finish code legend — corporate baseline
    /// <c>data/STING_FINISH_CODES.csv</c>, layered with a project override at
    /// <c>&lt;project&gt;/_BIM_COORD/finish_codes.csv</c> where project entries win by code.
    /// Cached per document path, same shape as the other STING data registries.
    /// </summary>
    public static class FinishCodeRegistry
    {
        private const string CorporateFile = "STING_FINISH_CODES.csv";
        private const string OverrideFile = "finish_codes.csv";

        private static readonly Dictionary<string, Dictionary<string, FinishCode>> _cache =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new();

        /// <summary>
        /// All finish codes for this document, keyed by code (case-insensitive).
        /// Never null — an unreadable or missing legend yields an empty table, and the
        /// caller reports every code as unresolved rather than silently guessing.
        /// </summary>
        public static Dictionary<string, FinishCode> Get(Document doc)
        {
            string key = doc?.PathName ?? "<no-document>";
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var cached)) return cached;
                var table = Load(doc);
                _cache[key] = table;
                return table;
            }
        }

        /// <summary>Drops the cache so an edit to either CSV is picked up without restarting Revit.</summary>
        public static void Reload()
        {
            lock (_lock) _cache.Clear();
        }

        /// <summary>
        /// Resolves a single code. Returns false for null/blank input and for codes absent
        /// from the legend — the caller must treat that as unresolved, not as a default.
        /// </summary>
        public static bool TryGet(Document doc, string code, out FinishCode finish)
        {
            finish = null;
            if (string.IsNullOrWhiteSpace(code)) return false;
            return Get(doc).TryGetValue(code.Trim(), out finish);
        }

        /// <summary>All codes for one surface, ordered by code.</summary>
        public static List<FinishCode> ForSurface(Document doc, string surface)
        {
            return Get(doc).Values
                .Where(f => string.Equals(f.Surface, surface, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static Dictionary<string, FinishCode> Load(Document doc)
        {
            var table = new Dictionary<string, FinishCode>(StringComparer.OrdinalIgnoreCase);

            string corporate = StingToolsApp.FindDataFile(CorporateFile);
            int corporateCount = ReadInto(table, corporate);

            // Project override — additive and by-code, so a project can retune a corporate
            // code or add its own without the baseline on disk being edited.
            int overrideCount = 0;
            string projectPath = null;
            try { projectPath = StingPaths.MetaFile(doc, "_BIM_COORD", OverrideFile); }
            catch (Exception ex) { StingLog.Warn($"FinishCodeRegistry: override path failed — {ex.Message}"); }
            if (!string.IsNullOrEmpty(projectPath))
                overrideCount = ReadInto(table, projectPath);

            if (corporateCount == 0 && overrideCount == 0)
                StingLog.Warn($"FinishCodeRegistry: no finish codes loaded — '{CorporateFile}' not found or empty. " +
                    "Finish codes will all report as unresolved.");
            else
                StingLog.Info($"FinishCodeRegistry: {table.Count} finish codes " +
                    $"({corporateCount} corporate, {overrideCount} project override).");

            return table;
        }

        /// <summary>Reads one CSV into the table, later rows winning by code. Returns rows accepted.</summary>
        private static int ReadInto(Dictionary<string, FinishCode> table, string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return 0;

            int accepted = 0;
            try
            {
                string[] lines = File.ReadAllLines(path);
                string[] header = null;

                foreach (string raw in lines)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    if (raw.TrimStart().StartsWith("#")) continue;

                    string[] cells = StingToolsApp.ParseCsvLine(raw);
                    if (cells == null || cells.Length == 0) continue;

                    if (header == null)
                    {
                        // First non-comment row is the header.
                        header = cells.Select(c => (c ?? "").Trim()).ToArray();
                        continue;
                    }

                    string Cell(string name)
                    {
                        int i = Array.FindIndex(header, h =>
                            string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
                        return i >= 0 && i < cells.Length ? (cells[i] ?? "").Trim() : "";
                    }

                    string code = Cell("FINISH_CODE");
                    if (string.IsNullOrEmpty(code)) continue;

                    double.TryParse(Cell("THICKNESS_MM"),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double thk);

                    table[code] = new FinishCode
                    {
                        Code = code,
                        Surface = Cell("SURFACE"),
                        Description = Cell("FINISH_DESCRIPTION"),
                        MatCode = Cell("MAT_CODE"),
                        MatName = Cell("MAT_NAME"),
                        ThicknessMm = thk,
                        RevitTypeHint = Cell("REVIT_TYPE_HINT"),
                        Notes = Cell("NOTES"),
                    };
                    accepted++;
                }
            }
            catch (Exception ex)
            {
                StingLog.Error($"FinishCodeRegistry: failed reading '{path}'", ex);
            }

            return accepted;
        }
    }
}
