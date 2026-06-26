// StingTools — ASHRAE 169 climate-zone classifier (Phase 195, WS F).
//
// Pure POCO / Revit-free + unit-tested. Derives the ASHRAE 169 / 90.1 thermal
// climate-zone NUMBER from cooling/heating degree-days (CDD10°C + HDD18°C, SI),
// so a blank project climate zone can be auto-derived from the resolved climate
// site instead of silently defaulting to a temperate zone. The moisture
// sub-type (A humid / B dry / C marine) needs precipitation/humidity the
// design-day registry doesn't carry, so it is ASSUMED ('A') and the assumption
// is surfaced by the caller.

namespace StingTools.Core.Sustainability
{
    public static class AshraeClimateZone
    {
        /// <summary>ASHRAE 169 thermal zone number (0–8) from CDD10°C + HDD18°C.
        /// Cooling-dominated zones (0–3) key off cooling degree-days; heating zones
        /// (4–8) off heating degree-days.</summary>
        public static int Number(double cdd10, double hdd18)
        {
            if (cdd10 > 6000) return 0;
            if (cdd10 > 5000) return 1;
            if (cdd10 > 3500) return 2;
            if (cdd10 > 2500) return 3;
            if (hdd18 <= 3000) return 4;   // cdd10 ≤ 2500 implied
            if (hdd18 <= 4000) return 5;
            if (hdd18 <= 5000) return 6;
            if (hdd18 <= 7000) return 7;
            return 8;
        }

        /// <summary>Zone string with an assumed moisture sub-type. Zones 7/8 carry
        /// no letter (per ASHRAE 169); 0–6 get the assumed letter (default 'A').</summary>
        public static string Classify(double cdd10, double hdd18, string moisture = "A")
        {
            int n = Number(cdd10, hdd18);
            if (n >= 7) return n.ToString();
            string m = string.IsNullOrEmpty(moisture) ? "A" : moisture;
            return n.ToString() + m;
        }
    }
}
