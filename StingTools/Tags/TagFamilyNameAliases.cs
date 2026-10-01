// TAGFAM-7: how two tag family names are judged to be the same family, and the one
// table of family names STING used to ship and no longer does.
//
// Revit-free on purpose: PerFamilyTierMap.Resolve, the tag family loader and the
// gate tests in StingTools.Tags.Tests all match names through this one file, so the
// rule the tests hold is the rule the plugin runs.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Tags
{
    public static class TagFamilyNameAliases
    {
        /// <summary>
        /// Old family name → canonical family name. A project that loaded the old
        /// name has its family renamed in place on the next library load (placed tags
        /// are kept, no second family is loaded). The canonical names are the declared
        /// ones (MEP tag config, PerFamilyTierMap), written with "/" where the
        /// declaration has it; the shipped file and the loaded family carry "-"
        /// instead, which <see cref="SameFamily"/> treats as equal.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> LegacyFamilyNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Shipped until 2026-10-01 with a doubled " Tag": the creator's tie-in suffix
            // was the verbose declared name, so "{prefix} - {suffix} Tag" appended a second one.
            { "STING - Tie-In Point Tag (Pipe — Plumbing & Hydraulic) Tag", "STING - Tie-In Point Tag (Pipe — Plumbing & Hydraulic)" },
            { "STING - Tie-In Point Tag (Duct — HVAC) Tag",                 "STING - Tie-In Point Tag (Duct — HVAC)" },
            { "STING - Tie-In Point Tag (Cable Tray — Electrical) Tag",     "STING - Tie-In Point Tag (Cable Tray — Electrical)" },
        };

        /// <summary>
        /// The key two family names are compared on: trimmed, with "/" read as "-".
        /// A family name cannot carry "/" once it has been through a file name
        /// ("LV/ELV" ships as "LV-ELV"), so the two spellings are one family.
        /// Comparison on the key is case-insensitive, as Revit's family names are.
        /// </summary>
        public static string MatchKey(string name)
            => string.IsNullOrWhiteSpace(name) ? "" : name.Trim().Replace('/', '-');

        /// <summary>True when the two names denote the same family (see <see cref="MatchKey"/>).</summary>
        public static bool SameFamily(string a, string b)
            => string.Equals(MatchKey(a), MatchKey(b), StringComparison.OrdinalIgnoreCase)
               && MatchKey(a).Length > 0;

        /// <summary>The canonical name for a legacy one, or null when the name is not legacy.</summary>
        public static string CanonicalForLegacy(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            foreach (var kv in LegacyFamilyNames)
                if (SameFamily(kv.Key, name)) return kv.Value;
            return null;
        }

        /// <summary>The name with any legacy spelling replaced by its canonical one.</summary>
        public static string Canonicalise(string name) => CanonicalForLegacy(name) ?? name;

        /// <summary>The legacy names that map to this canonical name.</summary>
        public static IEnumerable<string> LegacyNamesFor(string canonical)
            => LegacyFamilyNames.Where(kv => SameFamily(kv.Value, canonical)).Select(kv => kv.Key);

        /// <summary>What a library load should do about one canonical family, given the
        /// family names already in the project.</summary>
        public enum LegacyAction { None, Rename, BothPresent }

        /// <summary>
        /// Decide for one canonical family: <see cref="LegacyAction.Rename"/> when the
        /// project holds a legacy name for it and not the canonical one (rename that
        /// family in place); <see cref="LegacyAction.BothPresent"/> when it holds both
        /// (report, change nothing); otherwise <see cref="LegacyAction.None"/>.
        /// <paramref name="legacyInProject"/> is the project's spelling of the legacy name.
        /// </summary>
        public static LegacyAction Decide(string canonical, IEnumerable<string> projectFamilyNames,
                                          out string legacyInProject)
        {
            legacyInProject = null;
            var names = projectFamilyNames?.ToList() ?? new List<string>();
            var legacies = LegacyNamesFor(canonical).ToList();
            if (legacies.Count == 0) return LegacyAction.None;

            legacyInProject = names.FirstOrDefault(n => legacies.Any(l => SameFamily(l, n)));
            if (legacyInProject == null) return LegacyAction.None;

            bool canonicalPresent = names.Any(n => SameFamily(n, canonical));
            return canonicalPresent ? LegacyAction.BothPresent : LegacyAction.Rename;
        }
    }
}
