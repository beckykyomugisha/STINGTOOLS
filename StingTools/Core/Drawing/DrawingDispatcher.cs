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
        {
            var lib = DrawingTypeRegistry.GetLibrary(doc);
            if (lib?.Routing == null || lib.Routing.Count == 0) return null;

            foreach (var rule in lib.Routing)
            {
                if (!MatchesWildcard(rule.Discipline, discipline)) continue;
                if (!MatchesWildcard(rule.Phase,      phase))      continue;
                if (!MatchesWildcard(rule.DocType,    docType))    continue;
                return DrawingTypeRegistry.Get(doc, rule.DrawingTypeId);
            }
            return null;
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
    }
}
