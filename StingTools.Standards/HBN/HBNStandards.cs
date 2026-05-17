using System;
using System.Collections.Generic;

namespace StingTools.Standards.HBN
{
    /// <summary>
    /// Health Building Notes (HBN) — NHS facility-type design briefing.
    /// Cross-referenced from CLN_HBN_REF_TXT and ROM ASS_ROOM_FUNCTION_USE_TXT.
    /// Phase H-4 ships the HBN catalogue + adjacency target matrix only.
    /// </summary>
    public static class HBNStandards
    {
        public static readonly Dictionary<string, string> Catalogue =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "HBN 00-01", "General design guidance for healthcare buildings" },
            { "HBN 00-04", "Circulation and communication spaces" },
            { "HBN 03-01", "Adult acute mental health units" },
            { "HBN 04-01", "Adult in-patient facilities" },
            { "HBN 06",    "Diagnostic imaging" },
            { "HBN 07-02", "Renal dialysis facilities" },
            { "HBN 09-02", "Critical care" },
            { "HBN 09-03", "Neonatal units" },
            { "HBN 11-01", "Primary and community care" },
            { "HBN 12",    "Cardiac facilities" },
            { "HBN 13",    "Sterile services / HSDU" },
            { "HBN 16",    "Mortuary and post-mortem" },
            { "HBN 21",    "Maternity" },
            { "HBN 22",    "Day surgery" },
            { "HBN 26",    "Operating department" }
        };

        // HBN-derived adjacency expectations.
        // 0 = should not be adjacent (always separate)
        // 1 = preferred adjacency (within 3 doors / on same floor)
        // 2 = mandatory adjacency (directly accessible)
        public static readonly Dictionary<(string, string), int> AdjacencyTargets =
            new Dictionary<(string, string), int>
        {
            { ("ED",        "IMAGING"), 2 },
            { ("ED",        "ICU"),     2 },
            { ("ED",        "OR"),      1 },
            { ("OR",        "HSDU"),    2 },
            { ("OR",        "RECOV"),   2 },
            { ("OR",        "ICU"),     1 },
            { ("ICU",       "IMAGING"), 1 },
            { ("PHARMACY",  "WARD"),    1 },
            { ("MORT",      "WARD"),    0 },
            { ("MORT",      "OPD"),     0 },
            { ("HSDU-W",    "HSDU-P"),  1 },
            { ("MAT-LDR",   "NICU"),    1 }
        };

        public static readonly string[] CleanDirtyFlowRules = new[]
        {
            "Clean and dirty material flows must not cross",
            "Sterile supply route must be a dedicated path from HSDU-P to OR",
            "Linen / waste route must not enter clinical-care areas",
            "Mortuary collection route must avoid public corridors",
            "Visitor circulation must be segregated from staff-only paths to OR / ICU"
        };

        public static int? GetAdjacencyTarget(string a, string b)
        {
            if (AdjacencyTargets.TryGetValue((a, b), out var v)) return v;
            if (AdjacencyTargets.TryGetValue((b, a), out v)) return v;
            return null;
        }

        public static IEnumerable<string> ListReferences() => Catalogue.Keys;
    }
}
