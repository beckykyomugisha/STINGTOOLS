using System;
using System.Collections.Generic;

namespace StingTools.Standards.NFPA99
{
    /// <summary>
    /// NFPA 99 — Health Care Facilities Code.
    /// Phase H-4 covers Ch. 5 (medical gas) + Ch. 6 (essential electrical).
    /// </summary>
    public static class NFPA99Standards
    {
        public static readonly Dictionary<string, double> NominalGasPressureKPa =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "O2",   400 },
            { "MA4",  400 },
            { "MA7",  700 },
            { "N2O",  400 },
            { "N2",  1100 },
            { "CO2",  400 },
            { "HE",   400 },
            { "VAC",  -65 }, // gauge: -65 kPa
            { "AGS",  -25 }
        };

        public static readonly Dictionary<string, double> AlarmTolerancePctNominal =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "O2", 20 }, { "MA4", 20 }, { "MA7", 20 }, { "N2O", 20 },
            { "N2", 20 }, { "CO2", 20 }, { "HE", 20 }, { "VAC", 20 }, { "AGS", 20 }
        };

        // NFPA 99 §6.4 EES — three branches.
        public enum EesBranch { LifeSafety, Critical, Equipment, Normal, Unknown }

        public static EesBranch ParseBranch(string code)
        {
            if (string.IsNullOrEmpty(code)) return EesBranch.Unknown;
            switch (code.Trim().ToUpperInvariant())
            {
                case "LIFE-SAF": case "LS": return EesBranch.LifeSafety;
                case "CRIT": case "CR":     return EesBranch.Critical;
                case "EQP-BR": case "EQ":   return EesBranch.Equipment;
                case "NORMAL": case "N":    return EesBranch.Normal;
                default:                    return EesBranch.Unknown;
            }
        }

        public const double AtsTransferTimeMaxLifeSafetyS = 10.0;
        public const double AtsTransferTimeMaxCriticalS = 10.0;
        public const double AtsTransferTimeMaxEquipmentS = 60.0;

        // NFPA 110 — Type 10 generator runtime requirements.
        public const double GeneratorRunHrsAtFullLoadType10 = 96.0;

        // NFPA 99 anaesthetising-location rules — wet-location IPS required.
        public static readonly HashSet<string> WetLocationRoomClasses =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "OR-ULTRA", "OR-CONV", "OR-HYBRID", "CATHLAB", "IR" };

        public static bool RequiresIPS(string roomClass) =>
            !string.IsNullOrEmpty(roomClass) && WetLocationRoomClasses.Contains(roomClass);

        public static double? GetNominalKPa(string gasCode) =>
            string.IsNullOrEmpty(gasCode) ? (double?)null :
            NominalGasPressureKPa.TryGetValue(gasCode, out var v) ? v : (double?)null;

        // NFPA 99 §5.1.13 — diversity factors for medical gas pipe sizing.
        public static readonly Dictionary<string, double> DiversityFactor =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "O2",  0.50 },
            { "MA4", 0.50 },
            { "MA7", 0.10 },
            { "N2O", 0.20 },
            { "VAC", 0.50 },
            { "AGS", 0.30 }
        };

        public static double? GetDiversity(string gasCode) =>
            string.IsNullOrEmpty(gasCode) ? (double?)null :
            DiversityFactor.TryGetValue(gasCode, out var v) ? v : (double?)null;
    }
}
