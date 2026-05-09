using System;
using System.Collections.Generic;

namespace StingTools.Standards.FGI
{
    /// <summary>
    /// FGI Guidelines (Facility Guidelines Institute) — US healthcare design.
    /// 2026 edition transitions to a Facility Code (enforceable language).
    /// </summary>
    public static class FGIStandards
    {
        public static readonly string[] Editions = new[]
        {
            "FGI 2014", "FGI 2018", "FGI 2022 (Hospital + Outpatient + Residential)",
            "FGI Facility Code 2026 (Hospital — enforceable)"
        };

        // FGI 2018+ — minimum clear-floor-area square metres per single-occupancy
        // patient rooms (excluding closets, toilets).
        public static readonly Dictionary<string, double> MinRoomAreaM2 =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "WARD-INPT",   11.15 }, // 120 sq ft
            { "ICU",         18.58 }, // 200 sq ft
            { "NICU",        13.94 }, // 150 sq ft single-infant
            { "OR-CONV",     37.16 }, // 400 sq ft general
            { "OR-HYBRID",   65.03 }, // 700 sq ft minimum
            { "OR-ULTRA",    74.32 }, // 800 sq ft
            { "RECOV-1",      7.43 }, // 80 sq ft per bay
            { "EXAM",         9.29 }, // 100 sq ft
            { "AIIR",        13.94 }, // 150 sq ft
            { "PE-PROT",     13.94 }
        };

        // FGI 2026 single-occupancy mandate flag (rural emergency hospitals
        // and behavioural-health units have different rules).
        public static readonly HashSet<string> SingleOccupancyMandated =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "WARD-INPT", "ICU", "NICU", "RECOV-2", "PSY-BED" };

        // FGI Pt 2 + HBN 03-01 — anti-ligature applies to these classes by default.
        public static readonly HashSet<string> AntiLigatureRoomClasses =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "PSY-BED", "PSY-OBS", "SECL", "BEH-DAY" };

        public static double? GetMinRoomAreaM2(string roomClass) =>
            string.IsNullOrEmpty(roomClass) ? (double?)null :
            MinRoomAreaM2.TryGetValue(roomClass, out var v) ? v : (double?)null;

        public static bool RequiresSingleOccupancy(string roomClass) =>
            !string.IsNullOrEmpty(roomClass) && SingleOccupancyMandated.Contains(roomClass);

        public static bool RequiresAntiLigature(string roomClass) =>
            !string.IsNullOrEmpty(roomClass) && AntiLigatureRoomClasses.Contains(roomClass);
    }
}
