using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Text;
using System.Linq;

namespace StingTools.Core
{
    /// <summary>
    /// Parses the v5 tag config CSVs (STING_TAG_CONFIG_v5_0_{ARCH|MEP|STR|GEN}[_DesignConstruction].csv)
    /// into <see cref="TierPlan"/> entries keyed by tag-family name. Reuses the row
    /// schema defined in <see cref="PerFamilyTierMap"/> so downstream consumers
    /// (FamilyLabelAuthor, drift detector, post-author validator) share one
    /// data model with the hardcoded map.
    /// </summary>
    /// <remarks>
    /// Pure C#: no Autodesk.Revit.* dependency so the reader can be exercised
    /// from unit tests or headless scripts. Assembles only T4..T10 rows — T1..T3
    /// are default-present in every family and live outside the tier-variation
    /// plan (<see cref="PerFamilyTierMap"/> never stores T1..T3).
    /// </remarks>
    public static class TagConfigCsvReader
    {
        private static readonly Regex FamilyHeaderRegex =
            new Regex(@"^\s*Tag Family\s+#\d+:\s*(?<name>.+?)\s*$",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex TierRegex =
            new Regex(@"^T(?<n>\d{1,2})$",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Sentinel that ends a family's data rows. Matches the "⚠ WARNING PARAMETERS"
        // banner the regenerator emits after the last tier row of each family.
        private const string WarningBanner = "⚠ WARNING PARAMETERS";

        // Column indexes inside the v5 data header:
        //   #,Tier,Parameter,Prefix,Suffix,Spc,Brk,Discipline,Type,Name,Formula,Style,Color,Size,Box,Arrow
        private const int ColTier      = 1;
        private const int ColParameter = 2;
        private const int ColPrefix    = 3;
        private const int ColSuffix    = 4;
        private const int ColSpc       = 5;
        private const int ColBrk       = 6;
        private const int ColName      = 9;
        private const int ColStyle     = 11;
        private const int ColColor     = 12;
        private const int ColSize      = 13;
        private const int MinColumns   = 14;

        /// <summary>
        /// Load one CSV file and return a dictionary of family name → <see cref="TierPlan"/>.
        /// Families whose blocks contain no T4..T10 rows still receive a plan
        /// with all tiers marked <see cref="TierState.Omit"/> so callers can
        /// tell "file has this family with nothing to author" apart from "file
        /// does not mention this family at all".
        /// </summary>
        public static Dictionary<string, TierPlan> LoadFile(string csvPath)
        {
            if (string.IsNullOrEmpty(csvPath))
                throw new ArgumentException("csvPath is required", nameof(csvPath));
            if (!File.Exists(csvPath))
                throw new FileNotFoundException("Tag config CSV not found", csvPath);

            var lines = File.ReadAllLines(csvPath);
            return Parse(lines, csvPath);
        }

        /// <summary>
        /// Load every CSV in <paramref name="csvPaths"/> and merge into one
        /// dictionary. When the same family name appears in more than one file
        /// the later entry wins — callers should pass discipline CSVs in the
        /// order they expect to shadow each other (typically GEN → disc-specific).
        /// </summary>
        public static Dictionary<string, TierPlan> LoadFiles(IEnumerable<string> csvPaths)
        {
            if (csvPaths == null) throw new ArgumentNullException(nameof(csvPaths));
            var merged = new Dictionary<string, TierPlan>(StringComparer.Ordinal);
            foreach (var path in csvPaths)
            {
                var perFile = LoadFile(path);
                foreach (var kv in perFile)
                    merged[kv.Key] = kv.Value;
            }
            return merged;
        }

        /// <summary>
        /// Parse an in-memory CSV (one row per string). Exposed so unit tests
        /// can exercise the parser without touching the filesystem.
        /// </summary>
        public static Dictionary<string, TierPlan> Parse(IEnumerable<string> lines, string sourcePath = null)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            var result = new Dictionary<string, TierPlan>(StringComparer.Ordinal);

            string currentFamily = null;
            TierPlan currentPlan = null;
            int lineNo = 0;

            foreach (var raw in lines)
            {
                lineNo++;
                if (string.IsNullOrWhiteSpace(raw)) continue;
                // Comments: the regenerator prefixes schema / legend lines with '#'.
                // Data rows also start with a '#' column header so we only skip
                // lines whose FIRST non-space char is '#' AND are not the data header.
                var trimmed = raw.TrimStart();
                if (trimmed.StartsWith("#") && !trimmed.StartsWith("#,"))
                    continue;
                if (trimmed.StartsWith(WarningBanner))
                {
                    // Warning rows follow this banner. They do not contribute to
                    // TierPlan, but they may be followed by more family blocks
                    // in the same file — so we just close the current family
                    // and keep scanning.
                    currentFamily = null;
                    currentPlan = null;
                    continue;
                }

                // Family block start? Commits the previous plan (if any) and
                // opens a fresh one.
                var famMatch = FamilyHeaderRegex.Match(raw);
                if (famMatch.Success)
                {
                    var name = famMatch.Groups["name"].Value.Trim();
                    if (!string.IsNullOrEmpty(currentFamily) && currentPlan != null)
                        result[currentFamily] = Finalise(currentPlan);
                    currentFamily = name;
                    currentPlan = new TierPlan
                    {
                        T4 = TierState.Omit, T5 = TierState.Omit, T6 = TierState.Omit,
                        T7 = TierState.Omit, T8 = TierState.Omit, T9 = TierState.Omit,
                        T10 = TierState.Omit,
                    };
                    continue;
                }

                // STING_TAG_CONFIG_v5_0_HEALTH.csv uses a row-based family
                // catalog: "TAG_FAMILY,<name>,<disc>,<cat>,..." with no
                // associated T4-T10 tier rows. Register each row as a
                // plan-less family declaration so callers can distinguish
                // "CSV mentions this family, no tier customisation needed"
                // from "CSV does not mention this family at all".
                if (trimmed.StartsWith("TAG_FAMILY,", StringComparison.Ordinal))
                {
                    string[] cols;
                    try { cols = ParseCsvLine(raw); }
                    catch { continue; }
                    if (cols.Length >= 2)
                    {
                        string famName = (cols[1] ?? string.Empty).Trim();
                        if (!string.IsNullOrEmpty(famName) && !result.ContainsKey(famName))
                        {
                            result[famName] = new TierPlan
                            {
                                T4 = TierState.Omit, T5 = TierState.Omit, T6 = TierState.Omit,
                                T7 = TierState.Omit, T8 = TierState.Omit, T9 = TierState.Omit,
                                T10 = TierState.Omit,
                            };
                        }
                    }
                    continue;
                }

                // Skip the data-header row itself (#,Tier,Parameter,...).
                if (trimmed.StartsWith("#,")) continue;

                if (currentPlan == null) continue; // pre-amble rows before the first family
                TryAbsorbRow(raw, currentPlan, sourcePath, lineNo);
            }

            if (!string.IsNullOrEmpty(currentFamily) && currentPlan != null)
                result[currentFamily] = Finalise(currentPlan);

            return result;
        }

        private static void TryAbsorbRow(string rawLine, TierPlan plan, string sourcePath, int lineNo)
        {
            string[] cols;
            try { cols = ParseCsvLine(rawLine); }
            catch (Exception ex)
            {
                // StingLog is file-only so it is safe from headless contexts, but
                // guard anyway so the parser stays usable before the plugin boots.
                try { StingLog.Warn($"TagConfigCsvReader: malformed row at {sourcePath ?? "(in-memory)"}:{lineNo} — {ex.Message}"); }
                catch { /* swallow — reader must stay pure */ }
                return;
            }
            if (cols.Length < MinColumns) return;

            var tierStr = cols[ColTier].Trim();
            var tierMatch = TierRegex.Match(tierStr);
            if (!tierMatch.Success) return;
            if (!int.TryParse(tierMatch.Groups["n"].Value, out var tierNum)) return;
            if (tierNum < 4 || tierNum > 10) return; // plan only covers T4..T10

            var row = new TierRow
            {
                Tier      = tierStr,
                Parameter = cols[ColParameter].Trim(),
                Prefix    = cols[ColPrefix],
                Suffix    = cols[ColSuffix],
                Spc       = ParseInt(cols[ColSpc]),
                Brk       = ParseBrk(cols[ColBrk]),
                Name      = cols[ColName].Trim(),
                Style     = cols[ColStyle].Trim(),
                Color     = cols[ColColor].Trim(),
                Size      = ParseDouble(cols[ColSize]),
            };
            if (string.IsNullOrEmpty(row.Parameter)) return;

            switch (tierNum)
            {
                case 4:  plan.T4Rows.Add(row);  plan.T4  = TierState.Replace; break;
                case 5:  plan.T5Rows.Add(row);  plan.T5  = TierState.Replace; break;
                case 6:  plan.T6Rows.Add(row);  plan.T6  = TierState.Replace; break;
                case 7:  plan.T7Rows.Add(row);  plan.T7  = TierState.Replace; break;
                case 8:  plan.T8Rows.Add(row);  plan.T8  = TierState.Replace; break;
                case 9:  plan.T9Rows.Add(row);  plan.T9  = TierState.Replace; break;
                case 10: plan.T10Rows.Add(row); plan.T10 = TierState.Replace; break;
            }
        }

        private static TierPlan Finalise(TierPlan plan) => plan;

        private static int ParseInt(string s)
            => int.TryParse((s ?? string.Empty).Trim(), out var n) ? n : 0;

        private static double ParseDouble(string s)
            => double.TryParse((s ?? string.Empty).Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var d) ? d : 0.0;

        // Brk column uses '✓' / blank in the v5 CSVs; tolerate a few alternates.
        private static bool ParseBrk(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            var t = s.Trim();
            return t == "✓" || t.Equals("true", StringComparison.OrdinalIgnoreCase)
                            || t == "1" || t.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        // Duplicated from StingToolsApp.ParseCsvLine so the reader has no
        // transitive dependency on the Revit-bound entry point. Semantics must
        // stay identical — the two implementations read the same CSV files.
        private static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuote = false;
            var current = new System.Text.StringBuilder();
            foreach (char c in line ?? string.Empty)
            {
                if (c == '"') { inQuote = !inQuote; }
                else if (c == ',' && !inQuote)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result.ToArray();
        }
    }
}
