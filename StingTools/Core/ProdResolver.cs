using System.Collections.Generic;

namespace StingTools.Core
{
    /// <summary>
    /// Pure (Revit-free) PROD-code resolution: the precedence chain + the two
    /// built-in special cases a category-keyed CSV can't express (LPS
    /// cross-category set, Generic-Models sleeves). Factored out of
    /// <see cref="TagConfig"/> so the precedence + source-tiering rules are
    /// unit-tested in StingTools.Tags.Tests without a Revit document.
    ///
    /// Precedence (first match wins): project overlay → corporate CSV → LPS →
    /// sleeve → category default → GEN. The caller resolves the per-category
    /// rule lists from disk and passes them in; this class owns the ordering.
    /// </summary>
    public static class ProdResolver
    {
        /// <summary>The source tiers <see cref="Resolve"/> reports via its
        /// <c>out source</c>. Single definition so callers never hand-spell them.</summary>
        public static class Sources
        {
            public const string Project = "project";

            /// <summary>The TYPE NAME stated the code itself, in ISO 22014 form
            /// (<c>PLNS_WBL_Hollow200-Plastered</c>). Nothing was inferred.</summary>
            public const string Declared = "declared";

            public const string Corporate = "corporate";
            public const string Lps = "lps";
            public const string Sleeve = "sleeve";
            public const string Category = "category";
            public const string Gen = "gen";
        }

        /// <summary>
        /// True when a source tier is a genuine family-aware PROD code (project /
        /// corporate CSV rule, or the LPS / sleeve special case) as opposed to the
        /// generic category default. Single source of truth for the "specific vs
        /// generic" split used by Prod_GenerateRules (gap filter) and
        /// Prod_CoverageAudit (coverage %).
        /// </summary>
        public static bool IsSpecific(string source)
            => source == Sources.Project || source == Sources.Declared
            || source == Sources.Corporate
            || source == Sources.Lps || source == Sources.Sleeve;

        /// <param name="familyName">Element family name (may be null/empty).</param>
        /// <param name="typeName">Element type/symbol name (may be null).</param>
        /// <param name="categoryName">Revit category name.</param>
        /// <param name="projRulesForCategory">Project-overlay (pattern, prod) rules for this category, or null.</param>
        /// <param name="corpRulesForCategory">Corporate (pattern, prod) rules for this category, or null.</param>
        /// <param name="prodMap">Category → default PROD code map (the last-resort generic).</param>
        /// <param name="source">project | corporate | lps | sleeve | category | gen.</param>
        public static string Resolve(
            string familyName,
            string typeName,
            string categoryName,
            IReadOnlyList<(string Pattern, string ProdCode)> projRulesForCategory,
            IReadOnlyList<(string Pattern, string ProdCode)> corpRulesForCategory,
            IReadOnlyDictionary<string, string> prodMap,
            out string source,
            ICollection<string> knownCodes = null)
        {
            string combinedName = $"{familyName} {typeName}".ToUpperInvariant();

            // 0. The TYPE NAME states its own code, ISO 22014 style. This sits ABOVE
            //    the corporate patterns and BELOW the project overlay: a stated code
            //    beats an inference, an explicit project instruction beats both.
            //
            //    Deliberately OUTSIDE the non-empty-family guard below. A wall's family
            //    name is "Basic Wall" for every wall ever made, so gating a name that
            //    already carries its answer on that check would be testing the one thing
            //    the name has made irrelevant.
            string declared = ProdNameCode.Extract(typeName, knownCodes);

            if (!string.IsNullOrEmpty(familyName))
            {
                // 1. Project overlay wins.
                string proj = Strongest(projRulesForCategory, combinedName);
                if (proj != null) { source = Sources.Project; return proj; }

                if (declared != null) { source = Sources.Declared; return declared; }

                // 2. Corporate baseline.
                string corp = Strongest(corpRulesForCategory, combinedName);
                if (corp != null) { source = Sources.Corporate; return corp; }

                // 3. Lightning Protection System (BS EN 62305) — CROSS-category;
                //    family-name (not category) discriminates the sub-element kind.
                string lps = ResolveLps(combinedName);
                if (lps != null) { source = Sources.Lps; return lps; }

                // 4. Generic-Models sleeves / firestops (not a CSV category).
                if (categoryName == "Generic Models" && IsSleeve(combinedName))
                {
                    source = Sources.Sleeve;
                    return "SLV";
                }
            }

            // A coded name answers even with no family name at all.
            if (declared != null) { source = Sources.Declared; return declared; }

            // 5. Category default — last resort (generic, not family-specific).
            if (prodMap != null && categoryName != null &&
                prodMap.TryGetValue(categoryName, out string prod))
            {
                source = Sources.Category;
                return prod;
            }
            source = Sources.Gen;
            return "GEN";
        }

        /// <summary>
        /// The PROD code of the MOST SPECIFIC rule that matches, or null when none does.
        ///
        /// <para>Within one tier the rules are peers, not a chain: the author of
        /// <c>*Fire Damper*</c> and the author of <c>*Damper*</c> were describing two
        /// different products, and which of them sits higher in the CSV is an accident
        /// of when each was added. Ten shipped rows lost that accident — see
        /// <see cref="ProdPatternMatcher.Strength"/> for the list.</para>
        ///
        /// <para>Ties keep FILE ORDER, so two rules of equal specificity resolve exactly
        /// as they always did, and the project overlay's documented "prepended wins"
        /// behaviour is unchanged.</para>
        /// </summary>
        private static string Strongest(
            IReadOnlyList<(string Pattern, string ProdCode)> rules, string combinedName)
        {
            if (rules == null) return null;

            string best = null;
            int bestStrength = int.MinValue;

            for (int i = 0; i < rules.Count; i++)
            {
                int s = ProdPatternMatcher.Strength(combinedName, rules[i].Pattern);
                if (s < 0) continue;                 // no match
                if (s <= bestStrength) continue;     // strictly greater, so ties keep file order
                bestStrength = s;
                best = rules[i].ProdCode;
            }
            return best;
        }

        /// <summary>Returns the LPS PROD code for an upper-cased family+type name,
        /// or null when the name is not an LPS element.</summary>
        public static string ResolveLps(string upper)
        {
            if (string.IsNullOrEmpty(upper)) return null;
            bool isLps = upper.Contains("LPS") || upper.Contains("LIGHTNING") ||
                         upper.Contains("AIR TERMINAL") || upper.Contains("FINIAL") ||
                         upper.Contains("DOWN CONDUCTOR") || upper.Contains("DOWNCOND") ||
                         upper.Contains("EARTH ROD") || upper.Contains("EARTH ELECTRODE") ||
                         upper.Contains("RING EARTH") || upper.Contains("FOUNDATION EARTH") ||
                         upper.Contains("TEST CLAMP") || upper.Contains("EQUIPOTENTIAL");
            if (!isLps) return null;

            if (upper.Contains("AIR TERMINAL") || upper.Contains("FINIAL") ||
                upper.Contains("STRIKE TERMINATION") || upper.Contains("AIR ROD")) return "ATR";
            if (upper.Contains("AIR MESH") || upper.Contains("MESH NODE")) return "AMS";
            if (upper.Contains("CATENARY")) return "ACT";
            if (upper.Contains("DOWN CONDUCTOR") || upper.Contains("DOWNCOND") || upper.Contains("DESCENT")) return "DCN";
            if (upper.Contains("EARTH ROD") || upper.Contains("ROD EARTH")) return "ERD";
            if (upper.Contains("RING EARTH") || upper.Contains("EARTH RING")) return "ERG";
            if (upper.Contains("FOUNDATION EARTH")) return "EFE";
            if (upper.Contains("MESH EARTH") || upper.Contains("EARTH MESH")) return "EME";
            if (upper.Contains("EARTH ELECTRODE") || upper.Contains("EARTH PLATE")) return "ERD";
            if (upper.Contains("BONDING BAR") || upper.Contains("EARTH BAR")) return "BBR";
            if (upper.Contains("BOND") || upper.Contains("EQUIPOTENTIAL")) return "BCN";
            if (upper.Contains("SPARK GAP")) return "BSG";
            if (upper.Contains("TYPE 1") && upper.Contains("SPD")) return "SPD1";
            if (upper.Contains("TYPE 2") && upper.Contains("SPD")) return "SPD2";
            if (upper.Contains("TYPE 3") && upper.Contains("SPD")) return "SPD3";
            if (upper.Contains("TEST CLAMP") || upper.Contains("INSPECTION POINT")) return "TCL";
            return "LPS"; // generic LPS fallback
        }

        /// <summary>True when an upper-cased name denotes an MEP sleeve / firestop.</summary>
        public static bool IsSleeve(string upper)
            => !string.IsNullOrEmpty(upper) &&
               (upper.Contains("SLEEVE") || upper.Contains("SLV") || upper.Contains("PENETRATION")
                || upper.Contains("FIRESTOP") || upper.Contains("FIRE STOP") || upper.Contains("FIRE SEAL"));
    }
}
