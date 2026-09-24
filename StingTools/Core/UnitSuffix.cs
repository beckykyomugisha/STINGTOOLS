// UnitSuffix — Revit-free: which display unit a STING parameter's NAME says
// it holds. STING names carry their unit (HVC_AIRFLOW_LPS, PLM_PPE_SZ_MM,
// HVC_VEL_MPS, ELC_CKT_PWR_KW). NativeParamMapper copies Revit built-ins into
// them; when the target is a TEXT or unitless NUMBER parameter it cannot
// convert on write, so the mapper must convert to the unit the name promises.
// Before this, flow landed in ft³/s (~28× too small as L/s), velocity in ft/s,
// duct sizes in feet — all under names that promised SI.

namespace StingTools.Core
{
    internal static class UnitSuffix
    {
        /// <summary>
        /// The unit token at the end of a parameter name, lower-case
        /// ("mm", "lps", "kw"...), or null when the name carries none we know.
        /// Longest suffixes are tested first so "_M2" is not read as "_M".
        /// </summary>
        public static string Of(string paramName)
        {
            if (string.IsNullOrEmpty(paramName)) return null;
            string n = paramName.ToUpperInvariant();
            foreach (var s in Known)
                if (n.EndsWith("_" + s)) return s.ToLowerInvariant();
            return null;
        }

        // Ordered longest-first within each family so suffix tests are unambiguous.
        private static readonly string[] Known =
        {
            "MMH2O", "KVA", "KPA", "LPS", "CFM", "MPS", "M2", "M3", "MM", "KW", "PA", "M", "V", "W", "A",
        };

        /// <summary>
        /// Kilo-units: the SI value (VA, W) must be divided by 1000.
        /// </summary>
        public static bool IsKilo(string suffix) => suffix == "kw" || suffix == "kva";
    }
}
