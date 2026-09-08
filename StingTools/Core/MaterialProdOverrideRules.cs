// ══════════════════════════════════════════════════════════════════════════
//  MaterialProdOverrideRules.cs — the Revit-free half of the material-driven
//  PROD suffix: parse the rule table, and answer a suffix for a MATERIAL NAME.
//
//  Split out of MaterialProdOverrideRegistry (which needs an Element to read
//  the material name off) for one reason: the rules are UNANCHORED REGEXES,
//  and an unanchored regex over short chemistry abbreviations misfires in
//  exactly the way PatternMatch.Contains was written to stop.
//
//      "Sheeps Wool Insulation"    matched  (?i)eps      -> INS-EPS
//      "Acoustic Spiral Liner"     matched  (?i)pir      -> INS-PIR
//      "Respirator Filter Housing" matched  (?i)pir      -> INS-PIR
//      "Prefabricated Ductwork"    matched  (?i)fabric   -> FAB
//
//  Sheep's wool is the one that matters: a REAL insulation handed a DIFFERENT
//  insulation's code. Nothing errors, and the tag reads as a confident fact.
//
//  The fix is in the DATA, not here — \b anchors, curated per alternative,
//  because only the author knows whether a stem is deliberate. `galv` must
//  keep matching "galvanised" and `cement` must keep matching "cementitious",
//  while `eps` must stop matching "sheeps". No mechanism can infer that split,
//  so this class exists to make the data ASSERTABLE instead of guessing at it.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StingTools.Core
{
    /// <summary>One parsed row of STING_MATERIAL_PROD_OVERRIDES.csv.</summary>
    public sealed class MaterialProdRule
    {
        /// <summary>Revit category name, or "*" for every category.</summary>
        public string Category = "*";

        /// <summary>The compiled MaterialPattern cell.</summary>
        public Regex Pattern;

        /// <summary>The raw cell, kept verbatim for diagnostics and for the data gate.</summary>
        public string PatternText = "";

        /// <summary>Appended to the base PROD with a "-" separator.</summary>
        public string Suffix = "";
    }

    public static class MaterialProdOverrideRules
    {
        /// <summary>
        /// Parse the CSV lines (header included) into rules, in file order —
        /// which IS the precedence order, first match wins.
        ///
        /// A row with a malformed regex is skipped and named in
        /// <paramref name="warnings"/> rather than aborting the load: one bad
        /// cell must not disable material-aware PROD for the whole project.
        /// </summary>
        public static List<MaterialProdRule> Parse(IEnumerable<string> lines, List<string> warnings = null)
        {
            var rules = new List<MaterialProdRule>();
            if (lines == null) return rules;

            bool first = true;
            foreach (string raw in lines)
            {
                string line = (raw ?? "").Trim();
                if (first) { first = false; continue; }          // header
                if (line.Length == 0 || line.StartsWith("#")) continue;

                // Category,MaterialPattern,Suffix — the pattern may itself contain
                // no commas by construction (alternation uses '|'), so a plain
                // 3-way split is faithful and keeps this Revit-free.
                var cols = line.Split(new[] { ',' }, 3);
                if (cols.Length < 3) continue;

                string cat = cols[0].Trim();
                string pat = cols[1].Trim();
                string suf = cols[2].Trim();
                if (pat.Length == 0 || suf.Length == 0) continue;

                try
                {
                    rules.Add(new MaterialProdRule
                    {
                        Category = cat.Length == 0 ? "*" : cat,
                        Pattern = new Regex(pat, RegexOptions.IgnoreCase | RegexOptions.Compiled),
                        PatternText = pat,
                        Suffix = suf,
                    });
                }
                catch (Exception ex)
                {
                    warnings?.Add($"bad regex '{pat}': {ex.Message}");
                }
            }
            return rules;
        }

        /// <summary>
        /// First rule whose category applies and whose pattern matches the
        /// material name, or null. Null means "no suffix" — the base PROD stands.
        /// </summary>
        public static string ResolveSuffix(
            IReadOnlyList<MaterialProdRule> rules, string materialName, string categoryName)
        {
            if (rules == null || string.IsNullOrWhiteSpace(materialName)) return null;

            for (int i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                if (r?.Pattern == null) continue;
                if (!string.IsNullOrEmpty(r.Category) && r.Category != "*" &&
                    !string.Equals(r.Category, categoryName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (r.Pattern.IsMatch(materialName)) return r.Suffix;
            }
            return null;
        }
    }
}
