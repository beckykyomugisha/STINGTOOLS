// ============================================================================
// TagFamilyParamScope.cs — TYPE or INSTANCE for a parameter added to a tag family.
//
// One rule, used by every path that adds shared parameters to a tag family:
// Create Tag Families (AddSharedParameters, which also serves declared and
// tie-in families), Migrate Tag Families and Family Parameter Creator.
//
// The type-level machinery — TagTypeVariantWriter, Set Depth, the Tag Style
// Engine — writes to family TYPES, so the parameters it owns must be TYPE or
// they are invisible to it and every write silently does nothing:
//   * the depth gates and warning toggle (TagFamilyConfig.VisibilityParams),
//   * the style parameters (TagFamilyConfig.StyleParams: TAG_STYLE_CODE_TXT,
//     opted-in switches, box / leader / scale / depth),
//   * TAG_POS (drives the type-level offset Calculated Value),
//   * anything TAG_BOX_* / TAG_LEADER_*, and any TAG_{size}{style}_{colour}_BOOL
//     switch whether or not the catalogue opts it in.
// Everything else — ASS_TAG_* containers, tokens, description, category label
// params — is per-element and stays INSTANCE.
//
// TAGFAM-9 (2026-10-01): Create Tag Families used to add every parameter as
// INSTANCE, so TAG_STYLE_CODE_TXT had no per-type value to hold. Revit-free so
// StingTools.Tags.Tests can hold the rule.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core
{
    public static class TagFamilyParamScope
    {
        /// <summary>Prefixes that are always type-scoped.</summary>
        public static readonly string[] TypePrefixes = { "TAG_BOX_", "TAG_LEADER_" };

        /// <summary>The explicit type-scoped name set, case-insensitive.</summary>
        public static HashSet<string> NameSet(params IEnumerable<string>[] groups)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups ?? Array.Empty<IEnumerable<string>>())
                foreach (var n in g ?? Enumerable.Empty<string>())
                    if (!string.IsNullOrEmpty(n)) set.Add(n);
            return set;
        }

        /// <summary>True when <paramref name="name"/> must be added as a TYPE parameter.</summary>
        public static bool IsType(string name, ICollection<string> typeScopedNames)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (typeScopedNames != null && typeScopedNames.Contains(name)) return true;
            foreach (var p in TypePrefixes)
                if (name.StartsWith(p, StringComparison.Ordinal)) return true;
            return TagStyleFamilyParams.LooksLikeSwitch(name);
        }
    }
}
