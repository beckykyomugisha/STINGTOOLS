// ============================================================================
// UniversalTagRowSpec.cs — the 65 universal-tag label rows, as data.
//
// WHY THIS EXISTS
//
// docs/UNIVERSAL_TAG_LABEL_BUILD_SHEET.md is the authoritative build guide for
// the ONE universal STING tag label. StingTools.csproj does not deploy *.md
// (csproj:262), so the rows reach the plugin as STING_UNIVERSAL_TAG_ROWS.csv,
// GENERATED from that markdown by tools/extract_universal_tag_rows.py. The
// markdown stays the copy a human edits; --check gates the two together so they
// cannot drift. Do not hand-edit the CSV.
//
// THE TWO-PARAMETER CONTRACT
//
// Every non-T1 row is TWO parameters, not one:
//
//   Name       "Show T4 - Commissioning - State"  — the calculated value that
//                                                   sits in the label and holds
//                                                   the formula
//   Source     COMM_STATE_TXT                     — the shared parameter the
//                                                   formula READS
//
// The formula is if(TAG_PARA_STATE_n_BOOL, <Source>, "") set on <Name>. Setting
// it on <Source> instead is self-referential: Revit rejects it, or accepts it
// and the source value is destroyed. FamilyLabelAuthor.ApplyVisibilityFormulas
// does exactly that today, which is the likeliest reason it was never called
// from anywhere. The extractor validates the distinction; this loader carries
// it; anything authoring rows must honour it.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>One row of the universal tag label, as declared by the build sheet.</summary>
    internal sealed class UniversalTagRow
    {
        /// <summary>1-based position in the label, matching the build sheet.</summary>
        public int Row { get; set; }

        /// <summary>"T1".."T10". Tier 3 carries no rows — STEP 1 removes them.</summary>
        public string Tier { get; set; }

        /// <summary>Calculated-value name; the parameter that HOLDS the formula. Empty for the T1 primary row.</summary>
        public string Name { get; set; }

        /// <summary>Full formula text. Empty for the T1 primary row, which is a plain label row.</summary>
        public string Formula { get; set; }

        public string Prefix { get; set; }
        public string Suffix { get; set; }
        public bool Break { get; set; }

        /// <summary>Numeric tier, parsed from <see cref="Tier"/>. 0 if unparseable.</summary>
        public int TierNumber
        {
            get
            {
                int n;
                return int.TryParse((Tier ?? "").TrimStart('T', 't'), out n) ? n : 0;
            }
        }

        /// <summary>The gate this row's formula tests, e.g. TAG_PARA_STATE_4_BOOL. Empty for T1.</summary>
        public string GateParameter
        {
            get { return TierNumber > 0 && !string.IsNullOrEmpty(Formula) ? "TAG_PARA_STATE_" + TierNumber + "_BOOL" : ""; }
        }

        /// <summary>The shared parameter the formula READS. Empty for T1 or an unparseable formula.</summary>
        public string SourceParameter { get; set; }

        /// <summary>True when this row is a calculated value that must carry a formula.</summary>
        public bool IsCalculated
        {
            get { return !string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(Formula); }
        }
    }

    internal static class UniversalTagRowSpec
    {
        public const string DataFileName = "STING_UNIVERSAL_TAG_ROWS.csv";

        // if(GATE, SOURCE, "") — kept in sync with the same expression in the
        // extractor, which fails the build sheet if any row departs from it.
        private static readonly Regex SourceRe =
            new Regex("^if\\(\\s*([A-Za-z0-9_]+)\\s*,\\s*([A-Za-z0-9_]+)\\s*,\\s*\"\"\\s*\\)$",
                      RegexOptions.Compiled);

        private static List<UniversalTagRow> _cache;

        /// <summary>
        /// The 65 rows in label order. Returns an EMPTY list only when the data
        /// file is genuinely absent — callers must treat empty as "spec missing"
        /// and say so, never as "nothing to check".
        /// </summary>
        public static List<UniversalTagRow> Load()
        {
            if (_cache != null) return _cache;

            var rows = new List<UniversalTagRow>();
            string path = StingToolsApp.FindDataFile(DataFileName);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                StingLog.Warn($"UniversalTagRowSpec: {DataFileName} not found — the row spec is unavailable.");
                _cache = rows;
                return rows;
            }

            try
            {
                string[] lines = File.ReadAllLines(path);
                for (int i = 1; i < lines.Length; i++)   // skip header
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string[] c = StingToolsApp.ParseCsvLine(line);
                    if (c == null || c.Length < 7) continue;

                    int num;
                    if (!int.TryParse(c[0], out num)) continue;

                    var r = new UniversalTagRow
                    {
                        Row = num,
                        Tier = c[1],
                        Name = c[2],
                        Formula = c[3],
                        Prefix = c[4],
                        Suffix = c[5],
                        Break = string.Equals(c[6], "true", StringComparison.OrdinalIgnoreCase),
                    };

                    Match m = SourceRe.Match(r.Formula ?? "");
                    if (m.Success) r.SourceParameter = m.Groups[2].Value;

                    rows.Add(r);
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("UniversalTagRowSpec.Load", ex);
                rows.Clear();
            }

            _cache = rows;
            StingLog.Info($"UniversalTagRowSpec: loaded {rows.Count} rows from {DataFileName}");
            return rows;
        }

        /// <summary>Drop the cache so an edited CSV is picked up without restarting Revit.</summary>
        public static void Reload() { _cache = null; }

        /// <summary>Every distinct source parameter a formula reads, in first-seen order.</summary>
        public static List<string> SourceParameters()
        {
            return Load()
                .Where(r => !string.IsNullOrEmpty(r.SourceParameter))
                .Select(r => r.SourceParameter)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Every distinct tier gate the spec references, e.g. TAG_PARA_STATE_2_BOOL.</summary>
        public static List<string> GateParameters()
        {
            return Load()
                .Where(r => !string.IsNullOrEmpty(r.GateParameter))
                .Select(r => r.GateParameter)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(g => g, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Compare two formula strings ignoring whitespace differences only.
        /// Revit normalises spacing when it stores a formula, so a byte compare
        /// reports false mismatches on rows that are in fact correct.
        /// </summary>
        public static bool FormulaEquals(string a, string b)
        {
            return string.Equals(Squash(a), Squash(b), StringComparison.Ordinal);
        }

        private static string Squash(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char ch in s) if (!char.IsWhiteSpace(ch)) sb.Append(ch);
            return sb.ToString();
        }
    }
}
