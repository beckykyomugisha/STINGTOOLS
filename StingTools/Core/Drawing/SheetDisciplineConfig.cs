// StingTools — Drawing Template Manager · the discipline vocabulary, as data
//
// WHY THIS FILE EXISTS
// --------------------
// SheetDisciplineResolver decides a sheet's discipline from its number prefix and
// the words in its title, and both tables were hard-coded C#. A practice that
// numbers architecture "AR-101", or titles its drawings in a language other than
// the one those keywords are in, could not configure either — while everything
// else this system reads has been data-driven since Phase 113.
//
// That mattered more than it looks, because the discipline decides the ROLE
// segment of the issued document identifier. A prefix the table does not know
// falls through to GEN, GEN folds to role Z, and a drawing goes out attributed to
// nobody in particular. The practice could see the wrong letter and had no way to
// correct it short of renaming every sheet to suit the plugin.
//
// WHAT IS NOT CONFIGURABLE, AND WHY
// ---------------------------------
// The ROLE LETTERS themselves. A, S, M, E, P, Y, Z are ISO 19650's alphabet, not
// a project's preference — a container stamped with a letter outside it is not
// interoperable with anyone, which is the entire point of the standard. What a
// project may configure is which of ITS discipline codes maps to which standard
// letter: "FS" meaning fire safety can be told it is Y. A mapping whose target is
// not in the alphabet is REJECTED and named, not accepted quietly, because the
// failure would otherwise be a plausible-looking letter on an issued drawing.
//
// Layering matches DrawingTypeRegistry / VisibilityPresetStore: a corporate
// baseline in Data/STING_SHEET_DISCIPLINES.json with a per-project file layered on
// top. Revit-free — every entry point takes JSON text, so the Revit-bound caller
// resolves paths through StingPaths and the whole thing stays unit-testable.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    /// <summary>One discipline's title keywords, kept as a list rather than a map so
    /// the ORDER a title is tested in is part of the data. A title can name two
    /// things ("COORDINATED SERVICES PLAN"), and which one wins must be decidable by
    /// reading the file rather than by dictionary iteration order.</summary>
    public class TitleKeywordRule
    {
        [JsonProperty("discipline")] public string Discipline { get; set; }
        [JsonProperty("words")] public List<string> Words { get; set; }
    }

    public class SheetDisciplineLibrary
    {
        [JsonProperty("numberPrefixes")] public Dictionary<string, string> NumberPrefixes { get; set; }
        [JsonProperty("titleKeywords")] public List<TitleKeywordRule> TitleKeywords { get; set; }
        [JsonProperty("csvColumns")] public Dictionary<string, string> CsvColumns { get; set; }
        [JsonProperty("roleLetters")] public Dictionary<string, string> RoleLetters { get; set; }
    }

    /// <summary>The active vocabulary. Starts as the built-in defaults and is
    /// replaced when a baseline / project file loads.</summary>
    public static class SheetDisciplineConfig
    {
        public const string BaselineFileName = "STING_SHEET_DISCIPLINES.json";
        public const string ProjectFileName = "sheet_disciplines.json";

        /// <summary>Every role letter ISO 19650 defines. Deliberately not data:
        /// a letter outside this set is not interoperable with anyone.</summary>
        public const string RoleAlphabet = "ABCDEFGHIKLMPQSTWXYZ";

        private static Dictionary<string, string> _prefixes;
        private static List<TitleKeywordRule> _keywords;
        private static Dictionary<string, string> _csvColumns;
        private static Dictionary<string, string> _roleLetters;

        /// <summary>Problems found in the last Load, for a report to surface. Empty
        /// is the normal case; a non-empty list means part of the file was ignored,
        /// and silence about that is how a project runs for weeks on defaults it
        /// believes it overrode.</summary>
        public static IReadOnlyList<string> Problems { get; private set; } = new List<string>();

        public static IReadOnlyDictionary<string, string> NumberPrefixes
            => _prefixes ?? SheetDisciplineDefaults.NumberPrefixes;

        public static IReadOnlyList<TitleKeywordRule> TitleKeywords
            => (IReadOnlyList<TitleKeywordRule>)_keywords ?? SheetDisciplineDefaults.TitleKeywords;

        public static IReadOnlyDictionary<string, string> CsvColumns
            => _csvColumns ?? SheetDisciplineDefaults.CsvColumns;

        /// <summary>Project discipline code to ISO role letter, or null when the
        /// project declares none — in which case Iso19650DocumentCode's own table
        /// applies unchanged.</summary>
        public static IReadOnlyDictionary<string, string> RoleLetters => _roleLetters;

        /// <summary>Whether a file was loaded at all. False means the built-in
        /// defaults are live, which is a legitimate state and not an error — but a
        /// report that says "configured" when nothing was is a lie.</summary>
        public static bool IsConfigured { get; private set; }

        /// <summary>Layer a project file over a baseline. Either may be null or
        /// blank. Returns the problems found, and applies everything that was
        /// valid — one bad row must not discard a file's other twenty.</summary>
        public static IReadOnlyList<string> Load(string baselineJson, string projectJson)
        {
            var problems = new List<string>();

            var baseline = Parse(baselineJson, "baseline", problems);
            var project = Parse(projectJson, "project", problems);

            if (baseline == null && project == null)
            {
                Reset();
                Problems = problems;
                return Problems;
            }

            // Project entries win by key; both layers merge onto the built-in
            // defaults, so a file that names three prefixes adds three rather than
            // replacing the shipped set with three.
            var prefixes = new Dictionary<string, string>(
                SheetDisciplineDefaults.NumberPrefixes, StringComparer.OrdinalIgnoreCase);
            var csv = new Dictionary<string, string>(
                SheetDisciplineDefaults.CsvColumns, StringComparer.OrdinalIgnoreCase);
            var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var keywords = new List<TitleKeywordRule>();

            foreach (var lib in new[] { baseline, project })
            {
                if (lib == null) continue;

                if (lib.NumberPrefixes != null)
                    foreach (var kv in lib.NumberPrefixes)
                    {
                        if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value))
                        {
                            problems.Add($"numberPrefixes: an entry has a blank key or value; ignored");
                            continue;
                        }
                        prefixes[kv.Key.Trim().ToUpperInvariant()] = kv.Value.Trim().ToUpperInvariant();
                    }

                if (lib.CsvColumns != null)
                    foreach (var kv in lib.CsvColumns)
                    {
                        if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value))
                        {
                            problems.Add("csvColumns: an entry has a blank key or value; ignored");
                            continue;
                        }
                        csv[kv.Key.Trim().ToUpperInvariant()] = kv.Value.Trim().ToUpperInvariant();
                    }

                if (lib.RoleLetters != null)
                    foreach (var kv in lib.RoleLetters)
                    {
                        string letter = (kv.Value ?? "").Trim().ToUpperInvariant();
                        if (string.IsNullOrWhiteSpace(kv.Key) || letter.Length != 1
                            || !RoleAlphabet.Contains(letter))
                        {
                            // Rejected, and named. A role letter outside ISO 19650's
                            // alphabet is not interoperable, and it would look
                            // perfectly ordinary printed on a title block.
                            problems.Add($"roleLetters: '{kv.Key}' -> '{kv.Value}' is not one of "
                                + $"ISO 19650's role letters ({RoleAlphabet}); ignored");
                            continue;
                        }
                        roles[kv.Key.Trim().ToUpperInvariant()] = letter;
                    }

                if (lib.TitleKeywords != null)
                    foreach (var rule in lib.TitleKeywords)
                    {
                        if (rule == null || string.IsNullOrWhiteSpace(rule.Discipline)
                            || rule.Words == null || rule.Words.Count == 0)
                        {
                            problems.Add("titleKeywords: a rule has no discipline or no words; ignored");
                            continue;
                        }
                        keywords.Add(new TitleKeywordRule
                        {
                            Discipline = rule.Discipline.Trim().ToUpperInvariant(),
                            Words = rule.Words.Where(w => !string.IsNullOrWhiteSpace(w))
                                              .Select(w => w.Trim().ToUpperInvariant())
                                              .ToList(),
                        });
                    }
            }

            _prefixes = prefixes;
            _csvColumns = csv;
            _roleLetters = roles.Count > 0 ? roles : null;

            // Keywords REPLACE rather than merge when a file declares any, because
            // their order is the rule. Appending a project's rules after the shipped
            // ones would make them unreachable for every title the shipped list
            // already matches, which is the opposite of overriding.
            _keywords = keywords.Count > 0 ? keywords : null;

            IsConfigured = true;
            Problems = problems;
            return Problems;
        }

        /// <summary>Back to the built-in defaults. Used by tests, and by a reload
        /// that finds no file.</summary>
        public static void Reset()
        {
            _prefixes = null;
            _keywords = null;
            _csvColumns = null;
            _roleLetters = null;
            IsConfigured = false;
            Problems = new List<string>();
        }

        private static SheetDisciplineLibrary Parse(string json, string which, List<string> problems)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonConvert.DeserializeObject<SheetDisciplineLibrary>(json);
            }
            catch (Exception ex)
            {
                // Named, not swallowed. A malformed override that silently does
                // nothing is indistinguishable from one that is being honoured.
                problems.Add($"{which}: could not be read as JSON and was ignored — {ex.Message}");
                return null;
            }
        }
    }
}
