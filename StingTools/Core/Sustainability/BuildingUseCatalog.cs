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
using System.Linq;

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
            "office", "residential", "healthcare", "clinic", "hotel", "hotel-public",
            "retail", "restaurant", "kitchen", "education", "lecture-hall", "library",
            "warehouse", "lab", "industrial", "datacentre", "gym", "worship",
            "cinema", "parking"
        };

        /// <summary>WS K3 — friendly labels for the SETUP dropdown (mirror the J2
        /// country pattern). Falls back to Title Case of the id when unmapped.</summary>
        private static readonly Dictionary<string, string> _labels =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["office"] = "Office", ["residential"] = "Residential (dwelling)",
            ["healthcare"] = "Healthcare (inpatient)", ["clinic"] = "Clinic (outpatient)",
            ["hotel"] = "Hotel (guestroom)", ["hotel-public"] = "Hotel (public/lobby)",
            ["retail"] = "Retail", ["restaurant"] = "Restaurant", ["kitchen"] = "Commercial kitchen",
            ["education"] = "Education (classroom)", ["lecture-hall"] = "Lecture hall",
            ["library"] = "Library", ["warehouse"] = "Warehouse", ["lab"] = "Laboratory",
            ["industrial"] = "Light industrial", ["datacentre"] = "Data centre",
            ["gym"] = "Gym / fitness", ["worship"] = "Place of worship", ["cinema"] = "Cinema / theatre",
            ["parking"] = "Parking"
        };

        /// <summary>"office" → "Office", "hotel-public" → "Hotel (public/lobby)".</summary>
        public static string FriendlyLabel(string useId)
        {
            string k = (useId ?? "").Trim();
            if (k.Length == 0) return "";
            if (_labels.TryGetValue(k, out var lbl)) return lbl;
            // Title-case the id (hyphen/underscore → space).
            var words = k.Replace("-", " ").Replace("_", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
        }

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
