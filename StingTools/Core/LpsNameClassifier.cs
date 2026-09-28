// LpsNameClassifier.cs — the one place that decides, from a family + type name,
// whether an element is lightning protection and which LPS sub-function it is.
//
// Revit-free so StingTools.Tags.Tests can <Compile Include> it.
//
// WHY THIS EXISTS
//
// Until 2026-09-27 the "is this LPS?" keyword list was written three times —
// TagConfig.IsLightningProtection, TagConfig.ResolveLpsFunc and
// ProdResolver.ResolveLps — and the three had drifted: a "Spark Gap" or
// "Earth Bar" family was LPS to the validators but not to the PROD resolver.
// The FUNC half was worse: ResolveLpsFunc had no callers, so every LPS element
// got FuncMap's default, and its codes disagreed with STING_FUNC_SYS_MATRIX.csv
// (AIR/DOW/ERT/BND/TST there, AT/DC/EE/BOND/TC here and in the LPS tag handler).
// The FUNC vocabulary is now AT / DC / EE / BOND / SPD / TC everywhere.

namespace StingTools.Core
{
    public static class LpsNameClassifier
    {
        /// <summary>
        /// True when an UPPER-CASED "family type" string carries lightning-protection
        /// markers. Scopes BS EN 62305 checks and the LPS PROD codes.
        /// </summary>
        public static bool IsLps(string upper)
        {
            if (string.IsNullOrEmpty(upper)) return false;
            return upper.Contains("LPS") || upper.Contains("LIGHTNING") ||
                   upper.Contains("AIR TERMINAL") || upper.Contains("FINIAL") ||
                   upper.Contains("DOWN CONDUCTOR") || upper.Contains("DOWNCOND") ||
                   upper.Contains("EARTH ROD") || upper.Contains("EARTH ELECTRODE") ||
                   upper.Contains("RING EARTH") || upper.Contains("FOUNDATION EARTH") ||
                   upper.Contains("MESH EARTH") || upper.Contains("EARTH MESH") ||
                   upper.Contains("EQUIPOTENTIAL") || upper.Contains("BONDING BAR") ||
                   upper.Contains("EARTH BAR") || upper.Contains("TEST CLAMP") ||
                   upper.Contains("INSPECTION POINT") || upper.Contains("SPARK GAP");
        }

        /// <summary>
        /// LPS sub-function (FUNC token) for an UPPER-CASED "family type" string:
        /// AT air termination · DC down conductor · EE earth electrode ·
        /// BOND equipotential bonding · SPD surge protection · TC test clamp.
        /// Returns null when the name names no specific component, so the caller
        /// falls back to FuncMap["LPS"].
        /// </summary>
        public static string Func(string upper)
        {
            if (string.IsNullOrEmpty(upper)) return null;
            if (upper.Contains("AIR TERMINAL") || upper.Contains("FINIAL") ||
                upper.Contains("STRIKE TERMINATION") || upper.Contains("AIR ROD") ||
                upper.Contains("AIR MESH") || upper.Contains("CATENARY"))
                return "AT";
            if (upper.Contains("DOWN CONDUCTOR") || upper.Contains("DOWNCOND") ||
                upper.Contains("DESCENT"))
                return "DC";
            if (upper.Contains("EARTH ROD") || upper.Contains("EARTH ELECTRODE") ||
                upper.Contains("RING EARTH") || upper.Contains("FOUNDATION EARTH") ||
                upper.Contains("MESH EARTH") || upper.Contains("EARTH MESH") ||
                upper.Contains("EARTH PLATE"))
                return "EE";
            if (upper.Contains("EQUIPOTENTIAL") || upper.Contains("BONDING BAR") ||
                upper.Contains("EARTH BAR") || (upper.Contains("BOND") && upper.Contains("LPS")) ||
                upper.Contains("SPARK GAP"))
                return "BOND";
            if ((upper.Contains("SPD") || upper.Contains("SURGE PROTECT")) &&
                (upper.Contains("LIGHTNING") || upper.Contains("TYPE 1") ||
                 upper.Contains("TYPE 2") || upper.Contains("TYPE 3")))
                return "SPD";
            if (upper.Contains("TEST CLAMP") || upper.Contains("INSPECTION POINT"))
                return "TC";
            return null;
        }
    }
}
