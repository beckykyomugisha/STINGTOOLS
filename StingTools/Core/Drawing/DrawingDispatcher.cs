// StingTools — Drawing Template Manager
//
// DrawingDispatcher turns a (discipline, phase, docType) triple into a
// resolved DrawingType via the routing rules loaded by
// DrawingTypeRegistry. Rules are evaluated in order, first match wins.
// '*' is a wildcard that matches anything.
//
// Generation commands (fabrication composer, batch sections/elevations,
// sheet manager CreateFromTemplate, etc.) call Resolve(doc, …) at the
// start of a run; if no rule matches they either fall through to their
// current hard-coded behaviour or ask the user to pick a type.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using System.Text.RegularExpressions;

namespace StingTools.Core.Drawing
{
    public static class DrawingDispatcher
    {
        /// <summary>
        /// Resolve a DrawingType for the given key. Returns null when no
        /// rule matches — callers should treat null as "keep default
        /// behaviour" rather than throwing, so adding the dispatcher to
        /// an existing command stays no-op until the routing table names
        /// a matching entry.
        /// </summary>
        public static DrawingType Resolve(Document doc, string discipline, string phase, string docType)
            => Resolve(doc, discipline, phase, docType, levelCode: null, optionName: null);

        /// <summary>
        /// Level-aware variant — Week 6. When the routing table uses
        /// the levelMatches regex predicate, this overload lets callers
        /// supply the current level code so basement-specific or
        /// roof-specific profiles can fire.
        /// </summary>
        public static DrawingType Resolve(Document doc, string discipline, string phase, string docType, string levelCode)
            => Resolve(doc, discipline, phase, docType, levelCode, optionName: null);

        /// <summary>
        /// Option-aware variant — Phase 175. Lets callers inside a design
        /// option supply the active option name so routing rules carrying an
        /// <c>optionMatches</c> regex predicate fire. When optionName is null
        /// the predicate is evaluated against the "Main Model" label (via
        /// <see cref="DrawingOptionApplier.MatchesOptionPredicate"/>), so a
        /// rule scoped to a specific option is correctly skipped for the
        /// baseline model. Rules without an optionMatches predicate are
        /// unaffected.
        /// </summary>
        public static DrawingType Resolve(Document doc, string discipline, string phase, string docType, string levelCode, string optionName)
        {
            var lib = DrawingTypeRegistry.GetLibrary(doc);
            if (lib?.Routing == null || lib.Routing.Count == 0) return null;

            string projectCode = ReadProjectCode(doc);

            foreach (var rule in lib.Routing)
            {
                if (!MatchesField(rule.Discipline, rule.DisciplineMatches, discipline)) continue;
                if (!MatchesField(rule.Phase,      rule.PhaseMatches,      phase))      continue;
                if (!MatchesField(rule.DocType,    rule.DocTypeMatches,    docType))    continue;
                if (!string.IsNullOrEmpty(rule.LevelMatches)
                    && !RegexMatches(rule.LevelMatches, levelCode)) continue;
                if (!string.IsNullOrEmpty(rule.ProjectCodeMatches)
                    && !RegexMatches(rule.ProjectCodeMatches, projectCode)) continue;
                if (!DrawingOptionApplier.MatchesOptionPredicate(rule, optionName)) continue;
                return DrawingTypeRegistry.Get(doc, rule.DrawingTypeId);
            }
            return null;
        }

        private static bool MatchesField(string exact, string regexPattern, string actual)
        {
            // Regex predicate beats the exact-match field when both are
            // set. Unset predicate falls through to wildcard match.
            if (!string.IsNullOrEmpty(regexPattern)) return RegexMatches(regexPattern, actual);
            return MatchesWildcard(exact, actual);
        }

        private static bool RegexMatches(string pattern, string actual)
        {
            if (string.IsNullOrEmpty(actual)) return false;
            try { return System.Text.RegularExpressions.Regex.IsMatch(actual, pattern); }
            catch { return false; }
        }

        private static string ReadProjectCode(Document doc)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                if (pi == null) return null;
                var p = pi.LookupParameter("PRJ_ORG_PROJECT_CODE");
                return p?.StorageType == StorageType.String ? p.AsString() : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Return every DrawingType that could plausibly apply to the
        /// given discipline — used by pickers that want to offer the
        /// user a filtered list ("all Arch types", "all Pipe types").
        /// </summary>
        public static IReadOnlyList<DrawingType> CandidatesForDiscipline(Document doc, string discipline)
        {
            return DrawingTypeRegistry.ListAll(doc)
                .Where(t => MatchesWildcard(t.Discipline, discipline))
                .ToList();
        }

        private static bool MatchesWildcard(string ruleValue, string actual)
        {
            if (string.IsNullOrEmpty(ruleValue) || ruleValue == "*") return true;
            if (string.IsNullOrEmpty(actual)) return false;
            return string.Equals(ruleValue, actual, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolve the (familyName, symbolName) title-block pair for a profile.
        /// Resolution order (first hit wins):
        /// <list type="number">
        /// <item><description>
        /// <see cref="DrawingType.TitleBlockVariantRules"/> — each rule's
        /// <c>When</c> condition is evaluated against the profile context
        /// (phase / discipline / print colour scheme / screen-vs-print). The
        /// first matching rule supplies <c>UseFamily</c> / <c>UseSymbol</c>.
        /// </description></item>
        /// <item><description>
        /// <see cref="DrawingType.TitleBlockFamily"/> for the family. A
        /// <c>"Family:Symbol"</c> colon form names the symbol inline;
        /// otherwise <see cref="DrawingType.TitleBlockSymbolType"/> supplies
        /// it. When neither names a symbol the caller falls back to the first
        /// loaded type.
        /// </description></item>
        /// </list>
        /// Returns ("", "") when dt is null or TitleBlockFamily is empty.
        /// </summary>
        public static (string family, string symbol) ResolveTitleBlockVariant(DrawingType dt)
            => ResolveTitleBlockVariant(dt, context: "screen");

        /// <summary>
        /// Context-aware overload. <paramref name="context"/> ("screen" |
        /// "print") satisfies <c>context=…</c> conditions in a variant rule's
        /// <c>When</c>. Sheet-creation callers pass "screen" (the default);
        /// an export/print path passes "print" to select print-only title
        /// blocks.
        /// </summary>
        public static (string family, string symbol) ResolveTitleBlockVariant(DrawingType dt, string context)
        {
            if (dt == null) return ("", "");

            if (dt.TitleBlockVariantRules != null)
            {
                foreach (var rule in dt.TitleBlockVariantRules)
                {
                    if (rule == null) continue;
                    if (!EvaluateTitleBlockWhen(rule.When, dt, context)) continue;
                    var fam = !string.IsNullOrWhiteSpace(rule.UseFamily)
                        ? rule.UseFamily : dt.TitleBlockFamily;
                    return SplitFamilySymbol(fam, rule.UseSymbol ?? dt.TitleBlockSymbolType);
                }
            }

            return SplitFamilySymbol(dt.TitleBlockFamily, dt.TitleBlockSymbolType);
        }

        /// <summary>
        /// Splits a <c>"Family"</c> or <c>"Family:Symbol"</c> string. When the
        /// colon form is absent (or its symbol part empty) the
        /// <paramref name="fallbackSymbol"/> supplies the symbol so the
        /// separate TitleBlockSymbolType field is honoured.
        /// </summary>
        private static (string family, string symbol) SplitFamilySymbol(string raw, string fallbackSymbol)
        {
            raw = raw ?? "";
            string fb = (fallbackSymbol ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw)) return ("", "");
            var idx = raw.IndexOf(':');
            if (idx < 0) return (raw.Trim(), fb);
            var fam = raw.Substring(0, idx).Trim();
            var sym = raw.Substring(idx + 1).Trim();
            return (fam, string.IsNullOrEmpty(sym) ? fb : sym);
        }

        /// <summary>
        /// Evaluate a <see cref="TitleBlockVariantRule.When"/> expression.
        /// Empty / null = unconditional (matches). Conditions are joined by
        /// " AND " (all must hold). Supported keys (case-insensitive):
        /// <c>phase=</c>, <c>discipline=</c>, <c>print.colourScheme=</c>,
        /// <c>context=</c> (screen | print). An unrecognised key fails the
        /// rule (conservative — better to skip than mis-fire).
        /// </summary>
        private static bool EvaluateTitleBlockWhen(string when, DrawingType dt, string context)
        {
            if (string.IsNullOrWhiteSpace(when)) return true;
            // Split on " AND " case-insensitively, tolerating extra whitespace.
            foreach (var raw in System.Text.RegularExpressions.Regex.Split(
                         when, @"\s+AND\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                var cond = raw.Trim();
                if (cond.Length == 0) continue;
                var eq = cond.IndexOf('=');
                if (eq <= 0) return false;
                var key = cond.Substring(0, eq).Trim();
                var val = cond.Substring(eq + 1).Trim();
                string actual;
                if (key.Equals("phase", StringComparison.OrdinalIgnoreCase))
                    actual = dt.Phase;
                else if (key.Equals("discipline", StringComparison.OrdinalIgnoreCase))
                {
                    // Accept ISO short code or full name on either side.
                    if (!string.Equals(DisciplineCode(dt.Discipline), DisciplineCode(val),
                                       StringComparison.OrdinalIgnoreCase))
                        return false;
                    continue;
                }
                else if (key.Equals("print.colourScheme", StringComparison.OrdinalIgnoreCase)
                      || key.Equals("print.colorScheme", StringComparison.OrdinalIgnoreCase))
                    actual = dt.Print?.ColourScheme;
                else if (key.Equals("context", StringComparison.OrdinalIgnoreCase))
                    actual = context;
                else
                    return false; // unknown key — don't fire
                if (!string.Equals(actual ?? "", val, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        // Maps an ISO 19650 discipline full name to its short code; passes a
        // value that is already a code (or unknown) through unchanged. Lets a
        // variant rule's `discipline=Electrical` match a profile's `"E"`.
        private static readonly Dictionary<string, string> _discFullToCode =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "architectural", "A" }, { "architecture", "A" }, { "structural", "S" },
                { "mechanical", "M" }, { "electrical", "E" }, { "plumbing", "P" },
                { "public health", "P" }, { "fire protection", "FP" }, { "fire", "FP" },
                { "comms", "LV" }, { "communications", "LV" }, { "civil", "G" },
                { "healthcare", "H" }, { "medical gas", "MG" }, { "radiation protection", "RP" },
            };

        private static string DisciplineCode(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return s;
            return _discFullToCode.TryGetValue(s, out var code) ? code : s;
        }
    }
}
