using System;
using System.Collections.Generic;

namespace StingTools.Standards.ASHRAE170
{
    /// <summary>
    /// ANSI/ASHRAE/ASHE 170 — Ventilation of Health Care Facilities.
    /// 2025 edition embedded by FGI 2026 Facility Code.
    /// </summary>
    public static class ASHRAE170Standards
    {
        // Table 7.1 — design parameters per space (subset).
        public class SpaceParams
        {
            public string Space;
            public string PressureRelation;  // POS / NEG / NEUTRAL
            public int MinOutsideAch;
            public int MinTotalAch;
            public bool ExhaustToOutside;
            public bool RecirculationByMeansOfRoomUnitsAllowed;
            public bool AllRoomAirExhaustedToOutside;
            public string DesignRhPct;       // "min-max"
            public string DesignTempC;       // "min-max"
        }

        public static readonly Dictionary<string, SpaceParams> Table71 =
            new Dictionary<string, SpaceParams>(StringComparer.OrdinalIgnoreCase)
        {
            { "OR-CONV", new SpaceParams { Space="Operating room class B/C",
                PressureRelation="POS", MinOutsideAch=4, MinTotalAch=20,
                ExhaustToOutside=false, AllRoomAirExhaustedToOutside=false,
                RecirculationByMeansOfRoomUnitsAllowed=false,
                DesignRhPct="20-60", DesignTempC="20-24" } },
            { "OR-ULTRA", new SpaceParams { Space="Operating room (orthopaedic)",
                PressureRelation="POS", MinOutsideAch=4, MinTotalAch=300,
                ExhaustToOutside=false, AllRoomAirExhaustedToOutside=false,
                RecirculationByMeansOfRoomUnitsAllowed=false,
                DesignRhPct="20-60", DesignTempC="17-19" } },
            { "AIIR", new SpaceParams { Space="Airborne infection isolation",
                PressureRelation="NEG", MinOutsideAch=2, MinTotalAch=12,
                ExhaustToOutside=true, AllRoomAirExhaustedToOutside=true,
                RecirculationByMeansOfRoomUnitsAllowed=false,
                DesignRhPct="max 60", DesignTempC="21-24" } },
            { "PE-PROT", new SpaceParams { Space="Protective environment",
                PressureRelation="POS", MinOutsideAch=2, MinTotalAch=12,
                ExhaustToOutside=false, AllRoomAirExhaustedToOutside=false,
                RecirculationByMeansOfRoomUnitsAllowed=false,
                DesignRhPct="max 60", DesignTempC="21-24" } },
            { "ICU", new SpaceParams { Space="Critical/intensive care",
                PressureRelation="NEUTRAL", MinOutsideAch=2, MinTotalAch=6,
                ExhaustToOutside=false, AllRoomAirExhaustedToOutside=false,
                RecirculationByMeansOfRoomUnitsAllowed=false,
                DesignRhPct="30-60", DesignTempC="21-24" } },
            { "WARD-INPT", new SpaceParams { Space="Patient room",
                PressureRelation="NEUTRAL", MinOutsideAch=2, MinTotalAch=6,
                ExhaustToOutside=false, AllRoomAirExhaustedToOutside=false,
                RecirculationByMeansOfRoomUnitsAllowed=true,
                DesignRhPct="max 60", DesignTempC="21-24" } },
            { "PH-CSP-797", new SpaceParams { Space="Sterile compounding (USP <797>)",
                PressureRelation="POS", MinOutsideAch=4, MinTotalAch=30,
                ExhaustToOutside=false, AllRoomAirExhaustedToOutside=false,
                RecirculationByMeansOfRoomUnitsAllowed=false,
                DesignRhPct="max 60", DesignTempC="20-22" } },
            { "PH-CSP-800", new SpaceParams { Space="Hazardous drug compounding (USP <800>)",
                PressureRelation="NEG", MinOutsideAch=4, MinTotalAch=30,
                ExhaustToOutside=true, AllRoomAirExhaustedToOutside=true,
                RecirculationByMeansOfRoomUnitsAllowed=false,
                DesignRhPct="max 60", DesignTempC="20-22" } },
            { "RECOV-1", new SpaceParams { Space="Phase I recovery",
                PressureRelation="NEUTRAL", MinOutsideAch=2, MinTotalAch=6,
                ExhaustToOutside=false, AllRoomAirExhaustedToOutside=false,
                RecirculationByMeansOfRoomUnitsAllowed=false,
                DesignRhPct="20-60", DesignTempC="21-24" } },
            { "MORT", new SpaceParams { Space="Mortuary holding",
                PressureRelation="NEG", MinOutsideAch=2, MinTotalAch=10,
                ExhaustToOutside=true, AllRoomAirExhaustedToOutside=true,
                RecirculationByMeansOfRoomUnitsAllowed=false,
                DesignRhPct="-", DesignTempC="-" } },
            { "POST", new SpaceParams { Space="Autopsy",
                PressureRelation="NEG", MinOutsideAch=2, MinTotalAch=12,
                ExhaustToOutside=true, AllRoomAirExhaustedToOutside=true,
                RecirculationByMeansOfRoomUnitsAllowed=false,
                DesignRhPct="-", DesignTempC="20-22" } }
        };

        public static SpaceParams Lookup(string roomClass) =>
            string.IsNullOrEmpty(roomClass) ? null :
            Table71.TryGetValue(roomClass, out var v) ? v : null;
    }
}
