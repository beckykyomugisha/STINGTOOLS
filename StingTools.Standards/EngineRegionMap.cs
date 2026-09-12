// ============================================================================
// StingTools Standards — project region → engine region vocabularies
//
// KUT-8. ProjectStandardsManager.Region is the project-level answer to "where is
// this building". Two engines ALSO have a region, and neither of them speaks the
// same vocabulary:
//
//   MEP sizing  UK_SI | US_IP | EU_SI | DE_SI | SE_SI   (STING_MEP_SIZING_RULES.json)
//   LPS risk    UK | EU | US | TROPIC | AFRICA          (BS EN 62305-2 Ng bands)
//
// Before this file nothing reconciled them: both defaulted to their own
// hardcoded constant ("UK_SI", "UK") and stayed there no matter what the project
// preset said. Choosing "Uganda" in the setup wizard left duct sizing on the UK
// size ladder and lightning risk on Ng ≈ 0.5–1.0 — for a country whose ground
// flash density is among the highest measured anywhere, an input wrong by more
// than an order of magnitude, silently.
//
// Revit-free on purpose so the mapping is unit-testable. Same shape and same
// unknown-value discipline as MaterialLocaleManager.MapToMaterialRegion, which
// is the in-repo precedent for exactly this translation.
// ============================================================================

using System;
using System.Collections.Generic;

namespace StingTools.Standards
{
    /// <summary>
    /// Translates a <see cref="ProjectStandardsManager"/> region key into the region
    /// vocabulary each calculation engine actually reads.
    ///
    /// <para><b>Unknown input returns the engine's own documented default</b> rather
    /// than a guess. A region we do not recognise must leave the engine exactly where
    /// it was, because the alternative — inventing a plausible mapping — silently
    /// changes a calculation input on the strength of a spelling.</para>
    /// </summary>
    public static class EngineRegionMap
    {
        /// <summary>The MEP sizing default, matching <c>duct._defaultRegion</c> in
        /// STING_MEP_SIZING_RULES.json. Kept as a constant so a drift between the two
        /// is a one-line fix rather than a hunt.</summary>
        public const string MepSizingDefault = "UK_SI";

        /// <summary>The LPS default, matching the panel's pre-selected combo item.</summary>
        public const string LpsDefault = "UK";

        /// <summary>
        /// Project region → MEP sizing region (duct standard sizes, pipe bores, air
        /// density). Metric-standard regions map to the SI ladders; only the USA is on
        /// the inch-pound ladder.
        ///
        /// <list type="bullet">
        /// <item><c>USA</c> → <c>US_IP</c>.</item>
        /// <item><c>UK</c> → <c>UK_SI</c> (DW/144).</item>
        /// <item><c>Europe</c> → <c>EU_SI</c>.</item>
        /// <item><c>EastAfrica</c> / <c>Uganda</c> / <c>Kenya</c> / <c>SouthAfrica</c>
        ///   → <c>UK_SI</c>. EAS, UNBS, KEBS and SANS duct/pipe schedules are
        ///   BS-derived and metric; there is no separate African size ladder in the
        ///   rules file, and mapping them to EU_SI would assert a difference the data
        ///   does not carry.</item>
        /// <item><c>Australia</c> → <c>UK_SI</c>. AS 4254 is metric and closest to the
        ///   DW/144 ladder. <b>AS/NZS is not itself represented in the rules file</b>;
        ///   a project on AS sizes should add an <c>AU_SI</c> region to
        ///   STING_MEP_SIZING_RULES.json (or a project override) rather than rely on
        ///   this approximation.</item>
        /// <item><c>International</c> → <c>UK_SI</c>, which is the file's own default,
        ///   so a project that never chose a region is unchanged.</item>
        /// </list>
        /// </summary>
        public static string ToMepSizingRegion(string projectRegion)
        {
            switch (Norm(projectRegion))
            {
                case "usa": case "us": return "US_IP";
                case "uk": return "UK_SI";
                case "europe": case "eu": return "EU_SI";
                case "germany": case "de": return "DE_SI";
                case "nordic": case "sweden": case "se": return "SE_SI";
                case "eastafrica": case "uganda": case "kenya": case "southafrica":
                case "australia": case "international":
                    return MepSizingDefault;
                default:
                    return MepSizingDefault;
            }
        }

        /// <summary>
        /// Project region → LPS risk region. This one selects a <b>ground flash density
        /// band (Ng)</b>, so it is a calculation input, not a presentation choice: BS EN
        /// 62305-2 risk is proportional to Ng, and the bands the panel offers span
        /// 0.5 to 25 — a factor of fifty end to end.
        ///
        /// <list type="bullet">
        /// <item><c>USA</c> → <c>US</c> (Ng ≈ 1–12).</item>
        /// <item><c>UK</c> → <c>UK</c> (Ng ≈ 0.5–1.0).</item>
        /// <item><c>Europe</c> → <c>EU</c> (Ng ≈ 1.0–4.0).</item>
        /// <item><c>EastAfrica</c> / <c>Uganda</c> / <c>Kenya</c> / <c>SouthAfrica</c>
        ///   → <c>AFRICA</c> (Ng ≈ 8–25). Equatorial East Africa carries some of the
        ///   highest ground flash densities measured anywhere; leaving such a project
        ///   on the UK band understates the strike rate by more than an order of
        ///   magnitude.</item>
        /// <item><c>Australia</c> → <c>UK</c>, i.e. UNCHANGED from the panel default.
        ///   Australia spans Ng ≈ 0.1 in the south to well above 10 in the tropical
        ///   north, so no single band is defensible and the honest answer is to leave
        ///   the engineer's own selection alone rather than pick one for them.</item>
        /// <item><c>International</c> → <c>UK</c>, the panel default, unchanged.</item>
        /// </list>
        /// </summary>
        public static string ToLpsRegion(string projectRegion)
        {
            switch (Norm(projectRegion))
            {
                case "usa": case "us": return "US";
                case "uk": return "UK";
                case "europe": case "eu": case "germany": case "de":
                case "nordic": case "sweden": case "se":
                    return "EU";
                case "eastafrica": case "uganda": case "kenya": case "southafrica":
                    return "AFRICA";
                case "australia": case "international":
                    return LpsDefault;
                default:
                    return LpsDefault;
            }
        }

        /// <summary>Every project region key this map answers for by name, as opposed to
        /// by falling through to a default. Used by the test that holds this map to
        /// <see cref="ProjectStandardsManager.RegionalPresets"/>, so a preset added there
        /// without a mapping here is caught rather than silently defaulted.</summary>
        public static readonly IReadOnlyCollection<string> MappedProjectRegions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "USA", "UK", "Europe", "EastAfrica", "Uganda", "Kenya",
                "SouthAfrica", "Australia", "International",
            };

        private static string Norm(string s)
            => string.IsNullOrWhiteSpace(s) ? "" : s.Trim().ToLowerInvariant();
    }
}
