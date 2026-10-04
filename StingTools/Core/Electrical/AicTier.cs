// AicTier — the standard breaking-capacity (AIC) tier a board needs for its
// prospective fault level. Revit-free: StingTools.Tags.Tests compiles this file.
//
// Until 2026-10 the lookup returned the LARGEST tier when the fault level (plus
// margin) exceeded every tier, so a 120 kA board was stamped "100 kA required" —
// a device rating below the fault it must break. It also returned the raw fault
// level, presented as a tier, when no tier data was loaded. Both now return 0,
// which every caller reads as "no standard tier covers this board".

using System.Globalization;
using System.Linq;

namespace StingTools.Core.Electrical
{
    public static class AicTier
    {
        /// <summary>Default margin over the calculated fault level, %.</summary>
        public const double DefaultMarginPct = 10.0;

        /// <summary>
        /// The smallest tier ≥ fault × (1 + margin). 0 when no tier is large enough,
        /// when no tiers are loaded, or when the fault level is unknown (≤ 0) —
        /// never a tier smaller than the fault, never the fault itself.
        /// </summary>
        public static double Next(double faultKa, double[] tiers, double marginPct = DefaultMarginPct)
        {
            if (!(faultKa > 0) || tiers == null || tiers.Length == 0) return 0;
            double target = faultKa * (1.0 + (marginPct > 0 ? marginPct : 0) / 100.0);
            foreach (double t in tiers.Where(x => x > 0).OrderBy(x => x))
                if (t >= target - 1e-9) return t;
            return 0;
        }

        /// <summary>The reason no tier was returned, for the report; null when one was.</summary>
        public static string NoTierReason(double faultKa, double[] tiers, double marginPct = DefaultMarginPct)
        {
            if (Next(faultKa, tiers, marginPct) > 0) return null;
            if (!(faultKa > 0)) return "fault level not calculated";
            if (tiers == null || tiers.Length == 0 || !tiers.Any(x => x > 0))
                return "no AIC tiers loaded (STING_AIC_TIERS.json)";
            double max = tiers.Max();
            return $"{faultKa.ToString("0.0", CultureInfo.InvariantCulture)} kA + {marginPct.ToString("0", CultureInfo.InvariantCulture)} % " +
                   $"exceeds the largest standard tier ({max.ToString("0", CultureInfo.InvariantCulture)} kA) — specify the device from a fault study";
        }
    }
}
