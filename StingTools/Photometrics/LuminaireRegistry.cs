using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using StingTools.Core;

namespace StingTools.Photometrics
{
    /// <summary>
    /// Project-scoped registry mapping each Revit luminaire family/type to
    /// its manufacturer, model, photometric file, and key luminaire metadata
    /// (CCT, CRI, lumens, watts). Stored at
    /// <c>&lt;project&gt;/_BIM_COORD/luminaire_registry.csv</c> so it
    /// version-controls with the project file.
    ///
    /// Use cases:
    ///
    /// <list type="bullet">
    /// <item><b>Auto-bind:</b> when AssignPhotometricCommand runs and the
    /// fixture instance has no IES file yet, the registry's IesPath is
    /// applied automatically — no manual file picker.</item>
    /// <item><b>Calc sheet population:</b> LightingCalcSheetCommand reads
    /// manufacturer/model/CCT/CRI from the registry instead of forcing the
    /// engineer to repeat them per fixture.</item>
    /// <item><b>Procurement schedule:</b> the registry IS the procurement
    /// schedule the QS sends to the supplier — model number, CCT, CRI,
    /// quantity (count of placements).</item>
    /// </list>
    ///
    /// CSV columns (exact order, header row required):
    /// <c>FamilyName, TypeName, Manufacturer, Model, IESPath, Lumens, Watts,
    /// CCT_K, CRI, BeamAngleDeg, IPRating, Notes</c>
    ///
    /// Rows with empty FamilyName are ignored (allows comment rows). Lookup
    /// is case-insensitive on FamilyName + TypeName.
    /// </summary>
    public class LuminaireRegistry
    {
        public const string FileName = "luminaire_registry.csv";

        public List<LuminaireEntry> Entries { get; } = new List<LuminaireEntry>();

        private static LuminaireRegistry _cache;
        private static string _cachePath;
        private static readonly object _lock = new object();

        public static LuminaireRegistry LoadFor(string projectFolder)
        {
            lock (_lock)
            {
                string path = ResolvePath(projectFolder);
                if (_cache != null && string.Equals(_cachePath, path, StringComparison.OrdinalIgnoreCase))
                    return _cache;
                _cache = LoadFromDisk(path);
                _cachePath = path;
                return _cache;
            }
        }

        public static void InvalidateCache()
        {
            lock (_lock) { _cache = null; _cachePath = null; }
        }

        public static string ResolvePath(string projectFolder)
        {
            if (string.IsNullOrEmpty(projectFolder)) return null;
            return Path.Combine(projectFolder, "_BIM_COORD", FileName);
        }

        private static LuminaireRegistry LoadFromDisk(string path)
        {
            var reg = new LuminaireRegistry();
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return reg;
                var lines = File.ReadAllLines(path);
                if (lines.Length < 2) return reg;
                // Skip header
                for (int i = 1; i < lines.Length; i++)
                {
                    var raw = lines[i];
                    if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#")) continue;
                    var cells = ParseCsvLine(raw);
                    if (cells.Count < 2 || string.IsNullOrWhiteSpace(cells[0])) continue;
                    reg.Entries.Add(new LuminaireEntry
                    {
                        FamilyName    = Get(cells, 0),
                        TypeName      = Get(cells, 1),
                        Manufacturer  = Get(cells, 2),
                        Model         = Get(cells, 3),
                        IesPath       = Get(cells, 4),
                        Lumens        = ParseDouble(Get(cells, 5)),
                        Watts         = ParseDouble(Get(cells, 6)),
                        CctK          = ParseDouble(Get(cells, 7)),
                        Cri           = ParseDouble(Get(cells, 8)),
                        BeamAngleDeg  = ParseDouble(Get(cells, 9)),
                        IpRating      = Get(cells, 10),
                        Notes         = Get(cells, 11)
                    });
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LuminaireRegistry load: {ex.Message}");
            }
            return reg;
        }

        public LuminaireEntry Find(string familyName, string typeName)
        {
            if (string.IsNullOrEmpty(familyName)) return null;
            return Entries.FirstOrDefault(e =>
                string.Equals(e.FamilyName, familyName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.TypeName,   typeName,   StringComparison.OrdinalIgnoreCase))
                ?? Entries.FirstOrDefault(e =>  // family-only match as a fallback
                    string.Equals(e.FamilyName, familyName, StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrEmpty(e.TypeName));
        }

        public static void SaveTemplate(string path, IEnumerable<(string family, string type)> seedRows)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using var writer = new StreamWriter(path, append: false, encoding: new UTF8Encoding(true));
                writer.WriteLine("FamilyName,TypeName,Manufacturer,Model,IESPath,Lumens,Watts,CCT_K,CRI,BeamAngleDeg,IPRating,Notes");
                foreach (var (family, type) in seedRows ?? Enumerable.Empty<(string, string)>())
                {
                    if (string.IsNullOrWhiteSpace(family)) continue;
                    writer.WriteLine($"{Esc(family)},{Esc(type)},,,,,,,,,,");
                }
            }
            catch (Exception ex) { StingLog.Warn($"LuminaireRegistry save: {ex.Message}"); }
        }

        // ── helpers ─────────────────────────────────────────────────────

        private static string Get(List<string> row, int i) =>
            (i >= 0 && i < row.Count) ? (row[i] ?? "").Trim() : "";

        private static double ParseDouble(string s)
            => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        // Tiny CSV parser handling quoted fields with embedded commas/quotes.
        // Mirrors StingToolsApp.ParseCsvLine but kept local so the registry
        // is self-contained for unit testing.
        private static List<string> ParseCsvLine(string line)
        {
            var cells = new List<string>();
            if (line == null) return cells;
            var sb = new StringBuilder();
            bool inQuote = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuote)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuote = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == ',') { cells.Add(sb.ToString()); sb.Clear(); }
                    else if (c == '"') inQuote = true;
                    else sb.Append(c);
                }
            }
            cells.Add(sb.ToString());
            return cells;
        }
    }

    public class LuminaireEntry
    {
        public string FamilyName, TypeName, Manufacturer, Model, IesPath, IpRating, Notes;
        public double Lumens, Watts, CctK, Cri, BeamAngleDeg;
    }
}
