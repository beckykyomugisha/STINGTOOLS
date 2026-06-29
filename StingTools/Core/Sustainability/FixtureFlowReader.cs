// StingTools — Fixture-flow reader (Phase 195, gap fix #2).
//
// The water estimate previously fell back to a hardcoded "25% below baseline"
// indicative default because ReadDesignFixtureFlows always returned null — no
// fixture flow data was ever read from the model. This pure helper closes the
// gap by reading the low-flow ratings that good schedules already carry in the
// fixture TYPE / family name (e.g. "WC - Dual Flush 6/4L", "Basin Mixer 5 L/min",
// "Shower - Eco 8 lpm"). A designer reads those numbers off the schedule; so can
// the engine.
//
// The Revit adapter (SustainabilityEngine.ReadDesignFixtureFlows) classifies
// each plumbing fixture, tries an explicit stamped flow parameter first, then
// falls back to this name parser, and aggregates a median per fixture kind.
//
// Pure POCO / Revit-free + unit-tested. Name parsing is the testable part.

using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace StingTools.Core.Sustainability
{
    public enum FixtureKind { Unknown, Wc, Urinal, Basin, Shower, KitchenTap }

    public static class FixtureFlowReader
    {
        // Plausible flow bands per kind — guards against a stray year ("2024") or
        // a model number being mistaken for a flow. Litres-per-flush for WC/urinal;
        // litres-per-minute for taps + showers.
        private const double WcMin = 2.0,      WcMax = 13.0;
        private const double UrinalMin = 0.5,  UrinalMax = 6.0;
        private const double BasinMin = 1.0,   BasinMax = 12.0;
        private const double ShowerMin = 4.0,  ShowerMax = 16.0;
        private const double KitchenMin = 1.5, KitchenMax = 14.0;

        /// <summary>Per-kind plausible-flow band (litres-per-flush for WC/urinal,
        /// litres-per-minute for taps/showers). Used to guard an explicitly-stamped
        /// numeric param value against an out-of-band reading.</summary>
        public static bool InBand(FixtureKind kind, double v)
        {
            switch (kind)
            {
                case FixtureKind.Wc:         return v >= WcMin      && v <= WcMax;
                case FixtureKind.Urinal:     return v >= UrinalMin  && v <= UrinalMax;
                case FixtureKind.Basin:      return v >= BasinMin   && v <= BasinMax;
                case FixtureKind.Shower:     return v >= ShowerMin  && v <= ShowerMax;
                case FixtureKind.KitchenTap: return v >= KitchenMin && v <= KitchenMax;
                default: return false;
            }
        }

        /// <summary>Classify a fixture from its combined name text (family + type +
        /// instance). Order matters: urinal before WC (a urinal name may contain
        /// neither "wc" nor "toilet"); shower + bath before basin; kitchen/sink
        /// taps separated from basin/lavatory taps.</summary>
        public static FixtureKind ClassifyKind(string text)
        {
            string s = (text ?? "").ToLowerInvariant();
            if (s.Length == 0) return FixtureKind.Unknown;

            if (s.Contains("urinal")) return FixtureKind.Urinal;
            if (s.Contains("wc") || s.Contains("water closet") || s.Contains("toilet")
                || s.Contains("cistern") || s.Contains("pan")) return FixtureKind.Wc;
            if (s.Contains("shower")) return FixtureKind.Shower;
            if (s.Contains("kitchen") || s.Contains("sink")) return FixtureKind.KitchenTap;
            if (s.Contains("basin") || s.Contains("lavatory") || s.Contains("wash hand")
                || s.Contains("whb") || s.Contains("vanity")
                || (s.Contains("tap") || s.Contains("mixer") || s.Contains("faucet")))
                return FixtureKind.Basin;
            return FixtureKind.Unknown;
        }

        /// <summary>
        /// Parse a plausible flow rating from a fixture name for the given kind.
        /// WC/urinal → litres-per-flush; tap/shower → litres-per-minute. Handles
        /// dual-flush "6/4" pairs (averaged), decimal commas, and unit suffixes
        /// (l, ltr, lpm, l/min, l/flush). Returns null when no in-band number is
        /// found, so the caller can fall through to the indicative default.
        /// </summary>
        public static double? ParseFlow(FixtureKind kind, string text)
        {
            if (kind == FixtureKind.Unknown) return null;
            string s = (text ?? "").ToLowerInvariant().Replace(',', '.');
            if (s.Length == 0) return null;

            double lo, hi;
            switch (kind)
            {
                case FixtureKind.Wc:        lo = WcMin;      hi = WcMax;      break;
                case FixtureKind.Urinal:    lo = UrinalMin;  hi = UrinalMax;  break;
                case FixtureKind.Basin:     lo = BasinMin;   hi = BasinMax;   break;
                case FixtureKind.Shower:    lo = ShowerMin;  hi = ShowerMax;  break;
                case FixtureKind.KitchenTap:lo = KitchenMin; hi = KitchenMax; break;
                default: return null;
            }

            // 1) Dual-flush pair "6/4" or "4.5/3" → average the two (effective use).
            var pair = Regex.Match(s, @"(\d+(?:\.\d+)?)\s*/\s*(\d+(?:\.\d+)?)");
            if (pair.Success
                && double.TryParse(pair.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
                && double.TryParse(pair.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
            {
                double avg = (a + b) / 2.0;
                if (avg >= lo && avg <= hi) return avg;
            }

            // 2) A number immediately followed by a litre unit anywhere in the name.
            //    e.g. "4.5l", "6 ltr", "8 lpm", "5 l/min", "9 l/flush".
            foreach (Match m in Regex.Matches(s, @"(\d+(?:\.\d+)?)\s*(?:l\b|ltr|lpm|l\s*/\s*(?:min|flush))"))
            {
                if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                    && v >= lo && v <= hi)
                    return v;
            }

            // 3) Last resort: any standalone in-band number (avoids 4-digit years /
            //    model numbers via the band guard). Take the first that fits.
            foreach (Match m in Regex.Matches(s, @"\d+(?:\.\d+)?"))
            {
                if (double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                    && v >= lo && v <= hi)
                    return v;
            }
            return null;
        }
    }
}
