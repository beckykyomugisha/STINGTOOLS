using System;
using System.Collections.Generic;

namespace StingTools.Standards.USP797800
{
    /// <summary>
    /// USP &lt;797&gt; sterile compounding + USP &lt;800&gt; hazardous drug
    /// compounding cleanroom design rules.
    /// </summary>
    public static class USPStandards
    {
        public class CleanroomSpec
        {
            public string Code;
            public string Description;
            public string IsoClass;
            public int MinAch;
            public string PressureRelationToAdjacent;
            public double MinDeltaPaPa;       // Pa to next compartment
            public bool RequiresExternalExhaust;
        }

        // USP <797> — sterile compounding (positive cascade).
        public static readonly CleanroomSpec Sterile_PEC = new CleanroomSpec {
            Code = "PEC-797",
            Description = "Primary Engineering Control (laminar airflow workbench / BSC)",
            IsoClass = "ISO 5",
            MinAch = 0,
            PressureRelationToAdjacent = "N/A — within Buffer",
            MinDeltaPaPa = 0,
            RequiresExternalExhaust = false
        };

        public static readonly CleanroomSpec Sterile_Buffer = new CleanroomSpec {
            Code = "BUF-797",
            Description = "Buffer Area (positive pressure)",
            IsoClass = "ISO 7",
            MinAch = 30,
            PressureRelationToAdjacent = "POS to anteroom",
            MinDeltaPaPa = 5.0,    // 0.02" w.c.
            RequiresExternalExhaust = false
        };

        public static readonly CleanroomSpec Sterile_Anteroom = new CleanroomSpec {
            Code = "ANT-797",
            Description = "Anteroom (positive to corridor)",
            IsoClass = "ISO 7",
            MinAch = 30,
            PressureRelationToAdjacent = "POS to corridor",
            MinDeltaPaPa = 5.0,
            RequiresExternalExhaust = false
        };

        // USP <800> — hazardous drug compounding (negative cascade).
        public static readonly CleanroomSpec Hazardous_CPEC = new CleanroomSpec {
            Code = "CPEC-800",
            Description = "Containment Primary Engineering Control (Class II BSC / CACI)",
            IsoClass = "ISO 5",
            MinAch = 0,
            PressureRelationToAdjacent = "NEG within C-SEC",
            MinDeltaPaPa = 0,
            RequiresExternalExhaust = true
        };

        public static readonly CleanroomSpec Hazardous_CSEC = new CleanroomSpec {
            Code = "CSEC-800",
            Description = "Containment Secondary Engineering Control",
            IsoClass = "ISO 7",
            MinAch = 30,
            PressureRelationToAdjacent = "NEG to anteroom",
            MinDeltaPaPa = 2.5,    // 0.01" w.c. min, 0.03" max
            RequiresExternalExhaust = true
        };

        public static readonly CleanroomSpec Hazardous_Anteroom = new CleanroomSpec {
            Code = "ANT-800",
            Description = "Anteroom (positive to corridor, negative to C-SEC)",
            IsoClass = "ISO 7",
            MinAch = 30,
            PressureRelationToAdjacent = "POS to corridor",
            MinDeltaPaPa = 5.0,
            RequiresExternalExhaust = false
        };

        public static readonly Dictionary<string, CleanroomSpec> Catalogue =
            new Dictionary<string, CleanroomSpec>(StringComparer.OrdinalIgnoreCase)
        {
            { "PEC-797",  Sterile_PEC   },
            { "BUF-797",  Sterile_Buffer },
            { "ANT-797",  Sterile_Anteroom },
            { "CPEC-800", Hazardous_CPEC },
            { "CSEC-800", Hazardous_CSEC },
            { "ANT-800",  Hazardous_Anteroom }
        };

        public static CleanroomSpec Lookup(string code) =>
            string.IsNullOrEmpty(code) ? null :
            Catalogue.TryGetValue(code, out var v) ? v : null;

        // USP <800> recertification cycle — environmental sampling minimum frequency.
        public const int RecertificationCycleMonths = 6;

        // Canonical CLN_ROOM_CLASS_TXT codes that are USP-governed pharmacy
        // cleanrooms subject to the 6-monthly environmental recertification cycle.
        // Matches the canonical room-class vocabulary (RoomClassCodes) — sterile
        // compounding (USP <797>) and hazardous compounding (USP <800>).
        private static readonly HashSet<string> _cleanroomRoomClasses =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PH-CSP-797", "PH-CSP-800" };

        public static IReadOnlyCollection<string> CleanroomRoomClasses => _cleanroomRoomClasses;

        /// <summary>True when the (canonical) room class is a USP-governed cleanroom.</summary>
        public static bool IsCleanroomRoomClass(string canonicalRoomClass) =>
            !string.IsNullOrEmpty(canonicalRoomClass) && _cleanroomRoomClasses.Contains(canonicalRoomClass);
    }
}
