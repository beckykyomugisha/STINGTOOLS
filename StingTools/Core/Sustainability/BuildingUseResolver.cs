// StingTools — Building-use resolver (WS I1).
//
// Derives the canonical building use from whatever the model offers — an explicit
// setup choice, a ProjectInformation hint (building type / occupancy), or the room
// program (room / department names) — instead of silently defaulting to "office".
// The result carries a SOURCE + a Found flag so the readiness gate can BLOCK when
// no use could be resolved (never fabricate office).
//
// Free-text → canonical-use synonym mapping is the testable part (same pattern as
// FixtureFlowReader.ClassifyKind). The canonical uses come from BuildingUseCatalog,
// so adding a use to the registries surfaces it here too.
//
// Pure POCO — no Revit dependency. Unit-tested. The Revit adapter only gathers the
// candidate signal strings and passes them in.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Sustainability
{
    public class BuildingUseResolution
    {
        public string Use    { get; set; } = "";
        /// <summary>"setup" (explicit), "model" (derived from project info / rooms),
        /// or "unset" (nothing resolved — caller must block, not default to office).</summary>
        public string Source { get; set; } = "unset";
        public bool   Found  { get; set; }
    }

    public static class BuildingUseResolver
    {
        // Free-text keyword → canonical building use. Longest/most-specific keyword
        // wins. Maps the terms a Revit model actually carries (room names, departments,
        // building-type params) onto the canonical BuildingUseCatalog uses.
        private static readonly (string Keyword, string Use)[] Synonyms =
        {
            ("residential", "residential"), ("apartment", "residential"), ("dwelling", "residential"),
            ("house", "residential"), ("flat", "residential"), ("bedroom", "residential"),
            ("hospital", "healthcare"), ("clinic", "healthcare"), ("patient", "healthcare"),
            ("ward", "healthcare"), ("healthcare", "healthcare"), ("medical", "healthcare"),
            ("classroom", "education"), ("school", "education"), ("education", "education"),
            ("lecture", "education"), ("university", "education"), ("teaching", "education"),
            ("warehouse", "warehouse"), ("storage", "warehouse"), ("distribution", "warehouse"),
            ("laboratory", "lab"), ("lab", "lab"),
            ("restaurant", "restaurant"), ("dining", "restaurant"), ("canteen", "restaurant"),
            ("kitchen", "restaurant"),
            ("hotel", "hotel"), ("guest room", "hotel"), ("hospitality", "hotel"),
            ("retail", "retail"), ("shop", "retail"), ("store", "retail"), ("mall", "retail"),
            ("factory", "industrial"), ("industrial", "industrial"), ("plant", "industrial"),
            ("office", "office"), ("workstation", "office"), ("meeting", "office"),
        };

        /// <summary>Map one free-text string to a canonical use, or null when no
        /// keyword matches. Longest keyword wins (so "guest room" beats "room").</summary>
        public static string MapText(string text)
        {
            string s = (text ?? "").ToLowerInvariant();
            if (s.Length == 0) return null;
            string best = null; int bestLen = -1;
            foreach (var (kw, use) in Synonyms)
                if (s.Contains(kw) && kw.Length > bestLen) { best = use; bestLen = kw.Length; }
            return best;
        }

        /// <summary>Resolve the building use from prioritised signals. Each signal is
        /// (source, text); the first whose text maps to a known use wins, and only
        /// uses present in <paramref name="knownUses"/> (the live catalogue) are
        /// accepted. Returns Found=false / Source="unset" when nothing resolves — the
        /// caller must then BLOCK rather than default to office.</summary>
        public static BuildingUseResolution Resolve(
            IEnumerable<(string source, string text)> signals, IEnumerable<string> knownUses)
        {
            var known = new HashSet<string>(
                (knownUses ?? BuildingUseCatalog.CommonUses).Where(u => !string.IsNullOrWhiteSpace(u)),
                StringComparer.OrdinalIgnoreCase);

            if (signals != null)
                foreach (var (source, text) in signals)
                {
                    string use = MapText(text);
                    if (use != null && known.Contains(use))
                        return new BuildingUseResolution { Use = use, Source = source ?? "model", Found = true };
                }

            return new BuildingUseResolution { Use = "", Source = "unset", Found = false };
        }
    }
}
