// StingTools — Drawing Template Manager · viewport type names
//
// ONE list of the viewport type names STING asks for. Before this, all 93
// drawing types said viewportTypeName "STING - Standard Viewport" while
// SheetManagerEngineExt.DefaultViewportTypeRules asked for "STING Viewport",
// "STING Section Viewport", "STING Elevation Viewport", "STING 3D Viewport",
// "STING Detail Viewport" and "STING Legend Viewport" - two vocabularies for
// one idea, and nothing created a type under either, so both lookups missed
// and every viewport silently kept Revit's default type.
//
// Canonical names follow the catalogue's "STING - <Role> Viewport" form. The
// older unhyphenated names stay recognised as ALIASES because project
// templates built against the old rules may already carry them - a project
// that has "STING Section Viewport" keeps using it.
//
// Revit-free; linked into StingTools.Tags.Tests. The Revit-bound resolver /
// on-demand creator is ViewportTypeResolver.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    public static class StingViewportTypes
    {
        public const string Standard  = "STING - Standard Viewport";
        public const string Section   = "STING - Section Viewport";
        public const string Elevation = "STING - Elevation Viewport";
        public const string ThreeD    = "STING - 3D Viewport";
        public const string Detail    = "STING - Detail Viewport";
        public const string Legend    = "STING - Legend Viewport";

        public static readonly IReadOnlyList<string> Canonical =
            new[] { Standard, Section, Elevation, ThreeD, Detail, Legend };

        /// <summary>Pre-unification names -> canonical name.</summary>
        public static readonly IReadOnlyDictionary<string, string> LegacyAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["STING Viewport"]           = Standard,
                ["STING Section Viewport"]   = Section,
                ["STING Elevation Viewport"] = Elevation,
                ["STING 3D Viewport"]        = ThreeD,
                ["STING Detail Viewport"]    = Detail,
                ["STING Legend Viewport"]    = Legend,
            };

        /// <summary>
        /// Revit ViewType name -> canonical viewport type. The table
        /// SheetManagerEngineExt.DefaultViewportTypeRules is built from.
        /// </summary>
        public static readonly IReadOnlyList<KeyValuePair<string, string>> ByViewType = new[]
        {
            new KeyValuePair<string, string>("FloorPlan",   Standard),
            new KeyValuePair<string, string>("CeilingPlan", Standard),
            new KeyValuePair<string, string>("Section",     Section),
            new KeyValuePair<string, string>("Elevation",   Elevation),
            new KeyValuePair<string, string>("ThreeD",      ThreeD),
            new KeyValuePair<string, string>("Detail",      Detail),
            new KeyValuePair<string, string>("Legend",      Legend),
        };

        public static bool IsCanonical(string name)
            => !string.IsNullOrWhiteSpace(name)
            && Canonical.Any(c => string.Equals(c, name.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>The canonical name for a canonical or legacy STING name; null for anything else.</summary>
        public static string CanonicalFor(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var n = name.Trim();
            var c = Canonical.FirstOrDefault(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase));
            if (c != null) return c;
            return LegacyAliases.TryGetValue(n, out var mapped) ? mapped : null;
        }

        /// <summary>
        /// Names to try, in order, when a viewport type called
        /// <paramref name="requested"/> is wanted: the requested name itself,
        /// then — for a STING name, canonical or legacy — its canonical form
        /// and every legacy alias of it. A non-STING name yields only itself:
        /// a user's own type name is never silently swapped for another.
        /// </summary>
        public static IReadOnlyList<string> Candidates(string requested)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(requested)) return list;
            list.Add(requested.Trim());
            var canonical = CanonicalFor(requested);
            if (canonical == null) return list;
            if (!list.Contains(canonical, StringComparer.OrdinalIgnoreCase)) list.Add(canonical);
            foreach (var kv in LegacyAliases)
                if (kv.Value == canonical && !list.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
                    list.Add(kv.Key);
            return list;
        }
    }
}
