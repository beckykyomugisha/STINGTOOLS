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

        /// <summary>
        /// Optional 4th column. When this matches the material name the rule does
        /// NOT apply and matching continues to later rules.
        ///
        /// <para>WHY A COLUMN AND NOT A SPECIAL CASE. "Lead-Free Solder" took the
        /// lead code: the word IS "lead", correctly bounded, so no amount of
        /// boundary work fixes it. It is a NEGATION, and the table had no way to
        /// say one. A <c>(?!-free)</c> hard-coded into the lead row would have
        /// invited tin-free, chrome-free and every other one after it as more
        /// hard-coding; expressing it as data means the next one is an edit to a
        /// CSV.</para>
        /// </summary>
        public Regex Exclude;

        /// <summary>The raw exclusion cell, kept for diagnostics and the data gate.</summary>
        public string ExcludeText = "";
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
                // 4-way now: the optional ExcludePattern is the last column. A
                // 3-column row still parses exactly as before, so every existing
                // row is unchanged.
                var cols = line.Split(new[] { ',' }, 4);
                if (cols.Length < 3) continue;

                string cat = cols[0].Trim();
                string pat = cols[1].Trim();
                string suf = cols[2].Trim();
                string exc = cols.Length > 3 ? cols[3].Trim() : "";
                if (pat.Length == 0 || suf.Length == 0) continue;

                try
                {
                    Regex excRx = null;
                    if (exc.Length > 0)
                    {
                        // A malformed EXCLUSION must not silently widen the rule
                        // back to matching everything it was meant to exclude, so
                        // the row is dropped whole and named - the same policy the
                        // pattern column already has, for the same reason.
                        try
                        {
                            excRx = new Regex(exc, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                        }
                        catch (Exception exEx)
                        {
                            warnings?.Add($"bad exclusion regex '{exc}' on rule '{pat}': {exEx.Message}"
                                          + " - rule dropped rather than applied without its exclusion");
                            continue;
                        }
                    }

                    rules.Add(new MaterialProdRule
                    {
                        Category = cat.Length == 0 ? "*" : cat,
                        Pattern = new Regex(pat, RegexOptions.IgnoreCase | RegexOptions.Compiled),
                        PatternText = pat,
                        Suffix = suf,
                        Exclude = excRx,
                        ExcludeText = exc,
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
                if (!r.Pattern.IsMatch(materialName)) continue;

                // Excluded: this rule does not apply, and matching CONTINUES.
                // Returning null here instead would stop a later rule that
                // legitimately matches - "Lead-Free Solder" must be free to be
                // caught by a solder rule if one is ever added.
                if (r.Exclude != null && r.Exclude.IsMatch(materialName)) continue;

                return r.Suffix;
            }
            return null;
        }
    }
}
