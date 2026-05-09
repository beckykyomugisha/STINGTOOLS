using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Standards.HTM
{
    /// <summary>
    /// Health Technical Memoranda (HTM) — NHS England estate-wide guidance
    /// for the design / installation / operation of specialised building
    /// and engineering technology in healthcare premises.
    ///
    /// Covered: HTM 00, 02-01, 03-01 A/B, 04-01 A/B/C, 05-01, 05-02,
    /// 06-01, 07-01/04/07, 08-01, 08-02 plus regional variants
    /// (SHTM Scotland, WHTM Wales, NHS-NI Northern Ireland).
    ///
    /// Phase H-4 ships lookup tables + checklist generators only.
    /// Validators (Phase H-5) consume these tables.
    /// </summary>
    public static class HTMStandards
    {
        public static readonly string[] DocumentIndex = new[]
        {
            "HTM 00 — Best practice umbrella",
            "HTM 02-01 — Medical gas pipeline systems (Pts A/B)",
            "HTM 03-01 — Specialist ventilation (Pts A/B)",
            "HTM 04-01 — Safe water (Pts A/B/C)",
            "HTM 05-01 — Managing healthcare fire safety",
            "HTM 05-02 — Firecode functional provisions",
            "HTM 06-01 — Electrical services supply / distribution",
            "HTM 07-01 — Safe management of healthcare waste",
            "HTM 07-04 — Water management (sustainable)",
            "HTM 07-07 — Sustainable energy",
            "HTM 08-01 — Acoustics",
            "HTM 08-02 — Lifts in healthcare"
        };

        // HTM 03-01 Table A1 — minimum design ACH per room class.
        // Field-tested values reuse HVC_AIR_CHANGES_PER_HR.
        public static readonly Dictionary<string, int> MinAchByRoomClass =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "OR-ULTRA",   300 }, // ultra-clean laminar flow
            { "OR-CONV",     20 },
            { "OR-HYBRID",   25 },
            { "ICU",          6 },
            { "HDU",          6 },
            { "AIIR",        12 }, // 12 ACH for new-build, 6 for retrofit
            { "PE-PROT",     12 },
            { "ANTERM",      10 },
            { "RECOV-1",     15 },
            { "RECOV-2",      6 },
            { "WARD-INPT",    6 },
            { "ENDOSCOPY",   15 },
            { "PH-CSP-797",  30 },
            { "PH-CSP-800",  30 },
            { "MORT",         6 },
            { "POST",        15 },
            { "DECON-D",     10 },
            { "DECON-C",     10 },
            { "HSDU-W",      10 },
            { "HSDU-P",      10 }
        };

        // HTM 03-01 §6 / ASHRAE 170 — design pressure regime per room class.
        public static readonly Dictionary<string, string> DesignPressureRegime =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "OR-ULTRA",   "POS" },
            { "OR-CONV",    "POS" },
            { "OR-HYBRID",  "POS" },
            { "ICU",        "NEUTRAL" },
            { "AIIR",       "NEG" },
            { "PE-PROT",    "POS" },
            { "ANTERM",     "POS" },  // generally positive between AIIR and corridor (PE flips)
            { "PH-CSP-797", "POS" },
            { "PH-CSP-800", "NEG" },
            { "MORT",       "NEG" },
            { "POST",       "NEG" },
            { "DECON-D",    "NEG" },
            { "DECON-C",    "POS" },
            { "HSDU-W",     "NEG" },
            { "HSDU-P",     "POS" }
        };

        // HTM 03-01 — design Δp magnitude (Pa).
        public static readonly Dictionary<string, int> DesignDeltaPaPaByRoomClass =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "OR-ULTRA",   25 },
            { "OR-CONV",    25 },
            { "OR-HYBRID",  25 },
            { "AIIR",       15 },  // ≥ 2.5 Pa CDC; HTM/ASHRAE recommends ≥ 15 Pa
            { "PE-PROT",    12 },
            { "ANTERM",     10 },
            { "PH-CSP-797",  5 },
            { "PH-CSP-800",  3 },
            { "MORT",       15 },
            { "DECON-D",    10 },
            { "HSDU-W",     10 }
        };

        // HTM 04-01 — sentinel-point dead-leg ≤ 1 m for sentinel temperature monitoring.
        public const double DeadLegSentinelMaxM = 1.0;

        // HTM 04-01 — augmented-care temperature window for hot-water at outlet.
        public const double TmvOutletMinC = 38.0;
        public const double TmvOutletMaxC = 41.0;

        // HTM 05-02 — BS 9999 progressive horizontal evacuation refuge sizing
        // baseline (m² per non-ambulant occupant in the receiving compartment).
        public const double PheRefugeM2PerPerson = 0.75;

        // HTM 06-01 — generator runtime target (h) at full load.
        public const double GeneratorRunHrsTarget = 72.0;

        // HTM 02-01 / NFPA 99 — automatic transfer switch time (s).
        public const double AtsTimeMaxLifeSafetyS = 10.0;
        public const double AtsTimeMaxCriticalS = 10.0;

        public static int? GetMinAch(string roomClass) =>
            string.IsNullOrEmpty(roomClass) ? (int?)null :
            MinAchByRoomClass.TryGetValue(roomClass, out var v) ? v : (int?)null;

        public static string GetDesignRegime(string roomClass) =>
            string.IsNullOrEmpty(roomClass) ? null :
            DesignPressureRegime.TryGetValue(roomClass, out var v) ? v : null;

        public static int? GetDesignDeltaPa(string roomClass) =>
            string.IsNullOrEmpty(roomClass) ? (int?)null :
            DesignDeltaPaPaByRoomClass.TryGetValue(roomClass, out var v) ? v : (int?)null;

        public static IEnumerable<string> KnownRoomClasses() => MinAchByRoomClass.Keys;

        /// <summary>HTM 02-01 / NFPA 99 verification 12-step checklist.</summary>
        public static readonly string[] MgpsVerificationChecklist = new[]
        {
            "Pre-purge with oil-free dry nitrogen",
            "Cross-connection test (each gas in turn)",
            "Particulate test at every terminal unit",
            "Purity test (oxygen 99 % min, others per BS EN ISO 7396-1)",
            "Pressure decay / standing pressure test",
            "Indexing / NIST / DISS gas-specific connector test",
            "Labelling test (gas identification at every TU + ZVB + alarm)",
            "Area alarm operability under simulated fault",
            "Master alarm operability under simulated fault",
            "Source / plant changeover test (manifold + VIE)",
            "Emergency reserve test",
            "Sign-off by ASSE 6030 verifier with cert reference"
        };
    }
}
