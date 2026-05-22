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
        /// Phase 175 — option-aware variant. Adds the routing rule's
        /// optionMatches regex predicate so a profile catalogue can
        /// route the same (disc, phase, docType) tuple to different
        /// profiles based on the active design option (e.g. baseline
        /// option uses production pack, VE option uses presentation
        /// pack).
        /// </summary>
        public static DrawingType Resolve(Document doc, string discipline, string phase, string docType,
            string levelCode, string optionName)
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
        /// Splits the DrawingType.TitleBlockFamily string into a
        /// (familyName, symbolName) pair. If the value contains a colon
        /// (e.g. "A1 Title Block:Portrait") the left side is the family
        /// name and the right side is the type/symbol name. When no colon
        /// is present the whole string is treated as the family name and
        /// the symbol name is empty (caller falls back to first loaded type).
        /// Returns ("", "") when dt is null or TitleBlockFamily is empty.
        /// </summary>
        public static (string family, string symbol) ResolveTitleBlockVariant(DrawingType dt)
        {
            if (dt == null) return ("", "");
            var raw = dt.TitleBlockFamily ?? "";
            if (string.IsNullOrWhiteSpace(raw)) return ("", "");
            var idx = raw.IndexOf(':');
            if (idx < 0) return (raw.Trim(), "");
            return (raw.Substring(0, idx).Trim(), raw.Substring(idx + 1).Trim());
        }
    }
}
