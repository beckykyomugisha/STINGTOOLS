// EmergencyKeywords — the ONE list of words that mark a luminaire, circuit or
// system as emergency lighting. Revit-free: the corporate baseline is
// Data/STING_EMERGENCY_KEYWORDS.json, a project may extend or replace it at
// <project>/_BIM_COORD/emergency_keywords.json (EmergencyKeywordRegistry).
//
// Three kinds of keyword, because they need different token rules
// (see EmergencyNameMatcher for why):
//  - longKeywords        match anywhere, case-insensitive ("EmergencyLight_LED")
//  - wordStartKeywords   must START a word ("ExitSign", not "Deexited")
//  - abbreviations       must stand alone, matched on original casing
//                        ("EM", "Em", "em" — not "Emerald", "System", "EMBASSY")

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace StingTools.Core.Electrical
{
    public sealed class EmergencyKeywords
    {
        [JsonProperty("longKeywords")]
        public List<string> LongKeywords { get; set; } = new List<string>();

        [JsonProperty("wordStartKeywords")]
        public List<string> WordStartKeywords { get; set; } = new List<string>();

        [JsonProperty("abbreviations")]
        public List<string> Abbreviations { get; set; } = new List<string>();

        /// <summary>Project override only: true = replace the baseline, false (default) = add to it.</summary>
        [JsonProperty("replaceBaseline")]
        public bool ReplaceBaseline { get; set; }

        /// <summary>
        /// Built-in list — identical to the shipped Data/STING_EMERGENCY_KEYWORDS.json
        /// (asserted by EmergencyKeywordsTests). Used when the data file is missing so
        /// the matcher never silently degrades to "nothing is emergency".
        /// </summary>
        public static EmergencyKeywords BuiltIn() => new EmergencyKeywords
        {
            LongKeywords = new List<string>
            {
                "emergency", "emerg", "maintained",
                "emergencia",                 // es / pt
                "secours",                    // fr: éclairage de secours
                "notbeleuchtung", "notlicht", // de
                "sicherheitsbeleuchtung",     // de: safety lighting
                "dharura",                    // sw: taa ya dharura
            },
            WordStartKeywords = new List<string> { "exit" },
            Abbreviations = new List<string> { "EM" },
        };

        public static EmergencyKeywords FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var k = JsonConvert.DeserializeObject<EmergencyKeywords>(json);
            return k?.Normalised();
        }

        /// <summary>Baseline with a project override applied (additive unless it says replaceBaseline).</summary>
        public static EmergencyKeywords Merge(EmergencyKeywords baseline, EmergencyKeywords project)
        {
            baseline = (baseline ?? BuiltIn()).Normalised();
            if (project == null) return baseline;
            project = project.Normalised();
            if (project.ReplaceBaseline) return project;
            return new EmergencyKeywords
            {
                LongKeywords = Union(baseline.LongKeywords, project.LongKeywords),
                WordStartKeywords = Union(baseline.WordStartKeywords, project.WordStartKeywords),
                Abbreviations = Union(baseline.Abbreviations, project.Abbreviations),
            };
        }

        private EmergencyKeywords Normalised() => new EmergencyKeywords
        {
            LongKeywords = Clean(LongKeywords),
            WordStartKeywords = Clean(WordStartKeywords),
            Abbreviations = Clean(Abbreviations),
            ReplaceBaseline = ReplaceBaseline,
        };

        private static List<string> Clean(IEnumerable<string> xs) =>
            (xs ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private static List<string> Union(List<string> a, List<string> b) => Clean(a.Concat(b));

        // ── compiled matchers (built once per instance) ──────────────────────

        private Regex _long, _abbr;

        internal Regex LongRegex => _long ??= BuildLong();
        internal Regex AbbrRegex => _abbr ??= BuildAbbr();

        private Regex BuildLong()
        {
            var parts = LongKeywords.Select(Regex.Escape)
                .Concat(WordStartKeywords.Select(k => @"(?<![a-z])" + Regex.Escape(k)))
                .ToList();
            if (parts.Count == 0) return null;
            return new Regex(string.Join("|", parts),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private Regex BuildAbbr()
        {
            // Case-sensitive on purpose: the abbreviation as its own token in UPPER,
            // Capitalised or lower case. Not followed by a lower-case letter
            // ("Emerald") nor by two capitals ("EMBASSY" — an all-caps word), but
            // "EMBulkhead" (capital then lower-case: a new CamelCase word) counts.
            var variants = Abbreviations.SelectMany(a => new[]
            {
                a.ToUpperInvariant(),
                a.Length > 1 ? char.ToUpperInvariant(a[0]) + a.Substring(1).ToLowerInvariant() : a.ToUpperInvariant(),
                a.ToLowerInvariant(),
            }).Distinct(StringComparer.Ordinal).Select(Regex.Escape).ToList();
            if (variants.Count == 0) return null;
            return new Regex(@"(?<![A-Za-z])(" + string.Join("|", variants) + @")(?![a-z])(?![A-Z]{2})",
                RegexOptions.CultureInvariant);
        }
    }
}
