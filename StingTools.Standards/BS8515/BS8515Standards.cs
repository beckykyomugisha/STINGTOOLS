// BS 8515:2009+A1:2013 — Rainwater harvesting systems: code of practice.
// Demand factors (Annex C), runoff coefficients, simplified Annex B
// tank sizing. UK rainfall regional table for Annex A daily simulation
// would be supplied externally; this module only ships the bands used
// by RainwaterHarvestingCalc as defaults.

using System;
using System.Collections.Generic;

namespace StingTools.Standards.BS8515
{
    public static class BS8515Standards
    {
        // Annex C — non-potable demand per person per day (litres).
        public static readonly Dictionary<string, double> DemandFactors = new Dictionary<string, double>
        {
            { "WC_6L",              33.0 },
            { "WC_4L",              22.0 },
            { "WC_DUAL_FLUSH",      25.0 },
            { "URINAL",             4.0  },
            { "IRRIGATION",         4.0  },
            { "LAUNDRY_DOM",        20.0 },
            { "LAUNDRY_COMM_PER_KG", 7.0 },
            { "VEHICLE_WASH",       10.0 },
        };

        public static double GetDemandLpd(string useCode)
            => DemandFactors.TryGetValue(useCode?.ToUpperInvariant() ?? "", out var v) ? v : 0;

        // Runoff coefficients per roof material.
        public static double GetRunoffCoefficient(string roofMaterial)
        {
            string m = (roofMaterial ?? "").ToUpperInvariant();
            if (m.Contains("METAL") || m.Contains("GRP"))            return 0.85;
            if (m.Contains("TILE")  || m.Contains("SLATE"))          return 0.75;
            if (m.Contains("FELT")  || m.Contains("BITUMEN"))        return 0.70;
            if (m.Contains("GREEN") && m.Contains("INTENSIVE"))      return 0.45;
            if (m.Contains("GREEN") && m.Contains("EXTENSIVE"))      return 0.60;
            if (m.Contains("GREEN"))                                  return 0.55;
            if (m.Contains("CONCRETE"))                               return 0.80;
            return 0.75;
        }

        // First-flush diverter removes ~10% of yield.
        public const double FilterEfficiency = 0.90;

        // Table B.1 — UK regional annual rainfall (mm) lookup by
        // postcode-area prefix. 18 regions span the bulk of UK practice.
        public static readonly Dictionary<string, double> UKRainfallMmByRegion = new Dictionary<string, double>
        {
            { "SW",  900 },  // South West
            { "SE",  650 },  // South East
            { "S",   720 },  // South Central
            { "L",   600 },  // London
            { "EC",  600 },
            { "WC",  600 },
            { "E",   600 },  // East
            { "EN",  650 },
            { "N",   650 },  // North London
            { "NW",  1100 }, // North West (Cumbria, Lake District)
            { "NE",  750 },  // North East
            { "Y",   780 },  // Yorkshire
            { "M",   850 },  // Midlands / Manchester
            { "B",   650 },  // Birmingham
            { "W",   1200 }, // Wales (West)
            { "CF",  1100 }, // Cardiff
            { "G",   1100 }, // Glasgow
            { "EH",  650 },  // Edinburgh
            { "AB",  800 },  // Aberdeen
            { "BT",  900 },  // Northern Ireland
        };

        public static double GetUKRainfallMm(string postcodePrefix)
        {
            if (string.IsNullOrWhiteSpace(postcodePrefix)) return 800;
            string p = postcodePrefix.ToUpperInvariant();
            if (UKRainfallMmByRegion.TryGetValue(p, out var v)) return v;
            // Two-letter then one-letter fallback.
            if (p.Length >= 2 && UKRainfallMmByRegion.TryGetValue(p.Substring(0, 2), out v)) return v;
            if (UKRainfallMmByRegion.TryGetValue(p.Substring(0, 1), out v)) return v;
            return 800;
        }

        // Annex B simplified tank sizing — 5 % of annual non-potable demand
        // (or annual yield, whichever smaller).
        public static double GetRecommendedTankVolumeM3(double annualDemandM3, double annualYieldM3)
            => 0.05 * Math.Min(annualDemandM3, annualYieldM3);
    }
}
