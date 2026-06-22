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
            out string source)
        {
            string combinedName = $"{familyName} {typeName}".ToUpperInvariant();

            if (!string.IsNullOrEmpty(familyName))
            {
                // 1. Project overlay wins.
                if (projRulesForCategory != null)
                    foreach (var (pattern, prodCode) in projRulesForCategory)
                        if (ProdPatternMatcher.Matches(combinedName, pattern)) { source = "project"; return prodCode; }

                // 2. Corporate baseline.
                if (corpRulesForCategory != null)
                    foreach (var (pattern, prodCode) in corpRulesForCategory)
                        if (ProdPatternMatcher.Matches(combinedName, pattern)) { source = "corporate"; return prodCode; }

                // 3. Lightning Protection System (BS EN 62305) — CROSS-category;
                //    family-name (not category) discriminates the sub-element kind.
                string lps = ResolveLps(combinedName);
                if (lps != null) { source = "lps"; return lps; }

                // 4. Generic-Models sleeves / firestops (not a CSV category).
                if (categoryName == "Generic Models" && IsSleeve(combinedName))
                {
                    source = "sleeve";
                    return "SLV";
                }
            }

            // 5. Category default — last resort (generic, not family-specific).
            if (prodMap != null && categoryName != null &&
                prodMap.TryGetValue(categoryName, out string prod))
            {
                source = "category";
                return prod;
            }
            source = "gen";
            return "GEN";
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
