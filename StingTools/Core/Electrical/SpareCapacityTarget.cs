// SpareCapacityTarget — how much spare capacity a panel is expected to keep,
// for THIS building's sector, in one place.
//
// WHY THIS EXISTS
//
// The answer was in the codebase twice, and the two copies disagreed.
//
//   LoadDemandEngine.AssessSpareCapacity read spareTargetsPct from
//   STING_DIVERSITY_FACTORS.json - Commercial 25, Industrial 30, Residential
//   20, Healthcare 35, Education 25, Retail 25, cited to IEC 60364-5-52 Annex
//   B and CIBSE Guide K.
//
//   The WARN_ELC_PNL_SPARE_WAYS tag warning used a flat 20.
//
// Flat 20 matches exactly one sector, Residential. On a hospital the warning
// stayed quiet at 22% spare while the design target was 35 - silent precisely
// where the requirement is strictest, which is the worst direction for a check
// to be wrong in.
//
// A third number nearly joined them: WARN_ELC_PNL_SPARES_LOW, filed against a
// test clamp, said 10%. It was deleted on 2026-09-22 as misfiled, and finding
// it is what surfaced the disagreement between the other two.
//
// So both paths now call this, and the table is the only place a target is
// written down. Two functions that must agree, made into one that cannot
// disagree with itself.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Electrical
{
    /// <summary>Per-sector spare-capacity targets, and which sector a project is.</summary>
    public static class SpareCapacityTarget
    {
        /// <summary>
        /// Used when the project's sector cannot be told. Commercial, matching
        /// <c>LoadDemandEngine.AssessSpareCapacity</c>'s own default - the two
        /// must not disagree about the unknown case either.
        /// </summary>
        public const string DefaultSector = "Commercial";

        /// <summary>
        /// Used when the table itself is missing or unreadable. 25 is the
        /// Commercial value, so a lost data file degrades to the default sector
        /// rather than to a number that appears nowhere in the standards.
        /// </summary>
        public const double FallbackPct = 25.0;

        /// <summary>
        /// The target for a sector, given the table. The only place the number
        /// is decided.
        ///
        /// <para>Takes the table rather than loading it, so this file stays
        /// Revit-free and the rule is testable. <c>SpareCapacityTable</c> does
        /// the loading.</para>
        /// </summary>
        public static double TargetPct(string sector, Dictionary<string, double> table)
        {
            if (table == null || table.Count == 0) return FallbackPct;
            if (!string.IsNullOrWhiteSpace(sector) && table.TryGetValue(sector, out double pct)) return pct;
            // An unrecognised sector is NOT an error - a project can be a type
            // nobody has written a target for - but it must not silently take
            // some other sector's number either.
            return table.TryGetValue(DefaultSector, out double d) ? d : FallbackPct;
        }

        /// <summary>
        /// Classifies a free-text description into a sector. Revit-free, so the
        /// matching can be tested against real project names.
        ///
        /// <para>Order matters: "university hospital" is Healthcare, not
        /// Education, because the electrical requirement follows the clinical
        /// function. Healthcare is therefore tested first.</para>
        /// </summary>
        public static string SectorFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return DefaultSector;
            string b = text.ToLowerInvariant();

            if (Has(b, "hospital", "healthcare", "health centre", "health center", "clinic",
                       "medical", "surgery", "ward")) return "Healthcare";
            if (Has(b, "school", "university", "college", "academy", "campus",
                       "education")) return "Education";
            if (Has(b, "warehouse", "factory", "industrial", "plant", "workshop",
                       "manufacturing")) return "Industrial";
            if (Has(b, "residential", "dwelling", "housing", "apartment", "flats",
                       "hostel")) return "Residential";
            if (Has(b, "retail", "shop", "store", "mall", "supermarket")) return "Retail";
            return DefaultSector;
        }

        private static bool Has(string haystack, params string[] needles)
            => needles.Any(n => haystack.Contains(n));
    }
}
