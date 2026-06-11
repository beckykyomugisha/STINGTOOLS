using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StingTools.Core.Classification
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (C2) — CSI MasterFormat resolver (pure logic).
    //
    // Resolves a Revit element (category + family + type + SYS token) to a CSI
    // MasterFormat section + title from STING_CSI_MASTERFORMAT_MAP.csv. Free of
    // Autodesk.Revit.* so it can be unit-tested; the command (CsiCommands) reads
    // the CSV (corporate + project overlay) and walks the model.
    // ─────────────────────────────────────────────────────────────────────────

    public class CsiRule
    {
        public string Category { get; set; } = "";
        public string FamilyRegex { get; set; } = "";
        public string TypeRegex { get; set; } = "";
        public string Sys { get; set; } = "";
        public string Section { get; set; } = "";
        public string Title { get; set; } = "";

        private Regex _famRx, _typeRx;
        private bool _compiled;

        private void Compile()
        {
            if (_compiled) return;
            _compiled = true;
            if (!string.IsNullOrEmpty(FamilyRegex)) { try { _famRx = new Regex(FamilyRegex); } catch { } }
            if (!string.IsNullOrEmpty(TypeRegex)) { try { _typeRx = new Regex(TypeRegex); } catch { } }
        }

        /// <summary>Match score, or -1 when the rule does not apply. Higher = more specific.</summary>
        public int Score(string category, string family, string type, string sys)
        {
            Compile();
            int score = 0;

            bool anyCat = !string.IsNullOrEmpty(Category) && Category != "*";
            if (anyCat)
            {
                if (!string.Equals(Category, category, StringComparison.OrdinalIgnoreCase)) return -1;
                score++;
            }
            if (!string.IsNullOrEmpty(FamilyRegex))
            {
                if (_famRx == null || string.IsNullOrEmpty(family) || !_famRx.IsMatch(family)) return -1;
                score++;
            }
            if (!string.IsNullOrEmpty(TypeRegex))
            {
                if (_typeRx == null || string.IsNullOrEmpty(type) || !_typeRx.IsMatch(type)) return -1;
                score++;
            }
            if (!string.IsNullOrEmpty(Sys))
            {
                if (!string.Equals(Sys, sys, StringComparison.OrdinalIgnoreCase)) return -1;
                score++;
            }
            return score;
        }
    }

    public static class CsiMasterFormat
    {
        /// <summary>Parse map CSV lines (skips blank, '#'-comment, and the header row).</summary>
        public static List<CsiRule> ParseCsvLines(IEnumerable<string> lines)
        {
            var rules = new List<CsiRule>();
            foreach (var raw in lines ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string line = raw.TrimEnd('\r');
                if (line.TrimStart().StartsWith("#")) continue;
                var f = line.Split(new[] { ',' }, 6);
                if (f.Length < 6) continue;
                string cat = f[0].Trim();
                if (cat.Length == 0) continue;
                // skip the header row
                if (cat.Equals("Category", StringComparison.OrdinalIgnoreCase)) continue;
                rules.Add(new CsiRule
                {
                    Category = cat,
                    FamilyRegex = f[1].Trim(),
                    TypeRegex = f[2].Trim(),
                    Sys = f[3].Trim(),
                    Section = f[4].Trim(),
                    Title = f[5].Trim(),
                });
            }
            return rules;
        }

        /// <summary>Best-matching rule for the element context, or null when none apply.
        /// Highest score wins; ties resolve to the earliest rule in the list.</summary>
        public static CsiRule Resolve(IReadOnlyList<CsiRule> rules, string category, string family, string type, string sys)
        {
            CsiRule best = null;
            int bestScore = -1;
            if (rules == null) return null;
            for (int i = 0; i < rules.Count; i++)
            {
                int s = rules[i].Score(category, family, type, sys);
                if (s > bestScore) { bestScore = s; best = rules[i]; }
            }
            return bestScore >= 0 ? best : null;
        }

        /// <summary>Canonical key for a CSI section number (trim, collapse spaces, upper).</summary>
        public static string NormalizeSection(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            return Regex.Replace(s.Trim().ToUpperInvariant(), "\\s+", " ");
        }

        public class CsiTocEntry { public string Section; public string Title; }
        public class CsiReconcileResult
        {
            // model CSI sections with no matching spec section
            public List<CsiTocEntry> SpecGaps { get; } = new List<CsiTocEntry>();
            // spec sections with no model content (INFO — possible over-specification)
            public List<CsiTocEntry> OverSpec { get; } = new List<CsiTocEntry>();
            // section present in both but titles differ
            public List<(string Section, string ModelTitle, string SpecTitle)> TitleMismatches { get; }
                = new List<(string, string, string)>();
        }

        /// <summary>
        /// Reconcile model CSI sections against the spec TOC. modelSections /
        /// specSections map a normalised section number → a representative title.
        /// </summary>
        public static CsiReconcileResult Reconcile(
            IDictionary<string, string> modelSections, IDictionary<string, string> specSections)
        {
            var result = new CsiReconcileResult();
            var model = Norm(modelSections);
            var spec = Norm(specSections);

            foreach (var kv in model.OrderBy(k => k.Key))
            {
                if (!spec.TryGetValue(kv.Key, out string specTitle))
                    result.SpecGaps.Add(new CsiTocEntry { Section = kv.Key, Title = kv.Value });
                else if (!TitlesEqual(kv.Value, specTitle))
                    result.TitleMismatches.Add((kv.Key, kv.Value, specTitle));
            }
            foreach (var kv in spec.OrderBy(k => k.Key))
                if (!model.ContainsKey(kv.Key))
                    result.OverSpec.Add(new CsiTocEntry { Section = kv.Key, Title = kv.Value });

            return result;
        }

        private static Dictionary<string, string> Norm(IDictionary<string, string> src)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in src ?? new Dictionary<string, string>())
            {
                string key = NormalizeSection(kv.Key);
                if (key.Length == 0) continue;
                if (!d.ContainsKey(key)) d[key] = kv.Value ?? "";
            }
            return d;
        }

        private static bool TitlesEqual(string a, string b)
        {
            string Na(string s) => Regex.Replace((s ?? "").Trim().ToLowerInvariant(), "\\s+", " ");
            string na = Na(a), nb = Na(b);
            if (na.Length == 0 || nb.Length == 0) return true; // missing title ≠ mismatch
            return na == nb;
        }
    }
}
