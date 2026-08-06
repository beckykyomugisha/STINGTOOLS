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

        /// <summary>Canonical key for a CSI section number. Removes ALL whitespace (and
        /// upper-cases) so spaced "23 05 00" and unspaced "230500" reconcile to the same
        /// key — SpecLink exports spaced, models often store unspaced. Dots are preserved,
        /// so a child section "23 05 00.13" stays distinct from its parent.</summary>
        public static string NormalizeSection(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            return Regex.Replace(s.Trim().ToUpperInvariant(), "\\s+", "");
        }

        /// <summary>
        /// The DISPLAY form of a CSI section number: canonical CSI spacing,
        /// "23 05 00" — what MasterFormat prints and what a QS expects to read.
        ///
        /// <para>This is the other half of <see cref="NormalizeSection"/>, and #554 is
        /// what happens without it. Normalisation strips whitespace so SpecLink's spaced
        /// "23 05 00" and a model's unspaced "230500" reconcile to one key — that is
        /// correct and must stay. But <see cref="Reconcile"/> then reported the
        /// normalised KEY as the section's identity, so its output read "224000" where
        /// CSI canonically writes "22 40 00". A matching convention had leaked into
        /// user-facing output.</para>
        ///
        /// <para>Matching and display are different jobs. Collapsing them either breaks
        /// reconciliation across the two source formats or prints a section number in a
        /// form CSI does not use; keeping both functions costs one method and closes the
        /// seam instead of picking a loser.</para>
        ///
        /// <para>Level-4 child sections keep their dot suffix: "230500.13" renders as
        /// "23 05 00.13". Anything that is not a recognisable CSI number is returned
        /// UNCHANGED rather than forced into pairs — inventing a shape for input we do
        /// not understand would print a confident wrong section number, which is worse
        /// than printing exactly what we were given.</para>
        /// </summary>
        public static string FormatSection(string s)
        {
            string key = NormalizeSection(s);
            if (key.Length == 0) return "";

            // Split an optional level-4 suffix: "230500.13" → "230500" + ".13"
            string stem = key, suffix = "";
            int dot = key.IndexOf('.');
            if (dot >= 0) { stem = key.Substring(0, dot); suffix = key.Substring(dot); }

            // CSI numbers are an even count of digits in 2-digit pairs (division 23,
            // level-2 2305, level-3 230500). Anything else is not ours to reformat.
            if (stem.Length < 2 || stem.Length % 2 != 0) return key;
            foreach (char c in stem) if (c < '0' || c > '9') return key;

            var sb = new System.Text.StringBuilder(stem.Length + stem.Length / 2 + suffix.Length);
            for (int i = 0; i < stem.Length; i += 2)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(stem, i, 2);
            }
            sb.Append(suffix);
            return sb.ToString();
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

            // #554 — MATCH on the normalised key (kv.Key), REPORT the display form.
            // Ordering also uses the normalised key: it sorts identically to the spaced
            // form and avoids re-deriving it per comparison.
            foreach (var kv in model.OrderBy(k => k.Key))
            {
                if (!spec.TryGetValue(kv.Key, out string specTitle))
                    result.SpecGaps.Add(new CsiTocEntry { Section = FormatSection(kv.Key), Title = kv.Value });
                else if (!TitlesEqual(kv.Value, specTitle))
                    result.TitleMismatches.Add((FormatSection(kv.Key), kv.Value, specTitle));
            }
            foreach (var kv in spec.OrderBy(k => k.Key))
                if (!model.ContainsKey(kv.Key))
                    result.OverSpec.Add(new CsiTocEntry { Section = FormatSection(kv.Key), Title = kv.Value });

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
