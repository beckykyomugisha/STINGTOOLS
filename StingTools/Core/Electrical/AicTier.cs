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
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Electrical
{
    /// <summary>The loaded tier table and the margin to apply over the fault level.</summary>
    public sealed class AicTierSet
    {
        public double[] Tiers { get; set; } = new double[0];
        public double MarginPct { get; set; } = AicTier.DefaultMarginPct;
    }

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

        /// <summary>
        /// STING_AIC_TIERS.json: <c>tiers_kA</c> and <c>safetyMarginPct</c>. The margin used to be
        /// ignored — the code always applied 10 %, so a project that edited it changed nothing.
        /// A missing or invalid margin falls back to <see cref="DefaultMarginPct"/>; a negative one
        /// is refused (treated as the default) because it would pick a tier below the fault.
        /// </summary>
        public static AicTierSet Parse(string json)
        {
            var set = new AicTierSet();
            if (string.IsNullOrWhiteSpace(json)) return set;
            var root = Newtonsoft.Json.Linq.JObject.Parse(json);
            set.Tiers = ((root["tiers_kA"] as Newtonsoft.Json.Linq.JArray) ?? new Newtonsoft.Json.Linq.JArray())
                .Where(t => t.Type == Newtonsoft.Json.Linq.JTokenType.Float || t.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                .Select(t => t.Value<double>()).Where(x => x > 0).OrderBy(x => x).ToArray();
            var m = root["safetyMarginPct"];
            if (m != null && (m.Type == Newtonsoft.Json.Linq.JTokenType.Float || m.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                && m.Value<double>() >= 0)
                set.MarginPct = m.Value<double>();
            return set;
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
