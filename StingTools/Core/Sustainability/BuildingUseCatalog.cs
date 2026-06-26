// StingTools — Building-use catalogue (Phase 195, WS B4).
//
// Pure POCO / Revit-free + unit-tested. Produces the canonical building-use list
// the SETUP dropdown offers, DATA-DRIVEN: the union of a curated seed set with
// whatever keys the project's registries (baselines / water profiles / load
// profiles) actually carry. Adding a use to STING_GREEN_BASELINES.json or
// STING_WATER_USAGE_PROFILES.json therefore surfaces it in the dropdown with no
// UI code change (closes the hardcoded 5-item XAML list).

using System;
using System.Collections.Generic;

namespace StingTools.Core.Sustainability
{
    public static class BuildingUseCatalog
    {
        /// <summary>The uses STING supports out of the box (lower-case keys matching
        /// the registry convention). A SEED — the live catalogue is the union of this
        /// with the registry keys. Includes the common uses the original UI lacked
        /// (education / warehouse / lab / restaurant / industrial).</summary>
        public static readonly string[] CommonUses =
        {
            "office", "residential", "healthcare", "retail", "hotel",
            "education", "warehouse", "lab", "restaurant", "industrial"
        };

        /// <summary>Union of the curated seed list with the registry key sets, in a
        /// stable order (seed first, registry-only extras appended), distinct +
        /// case-insensitive, skipping blanks and the "*" wildcard.</summary>
        public static List<string> Resolve(params IEnumerable<string>[] registryKeySets)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordered = new List<string>();

            void Add(string raw)
            {
                string k = (raw ?? "").Trim();
                if (k.Length == 0 || k == "*") return;
                string key = k.ToLowerInvariant();
                if (seen.Add(key)) ordered.Add(key);
            }

            foreach (var u in CommonUses) Add(u);
            if (registryKeySets != null)
                foreach (var set in registryKeySets)
                    if (set != null)
                        foreach (var k in set) Add(k);

            return ordered;
        }
    }
}
