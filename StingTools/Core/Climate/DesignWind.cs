// StingTools — design wind speed for the infiltration model (Revit-free).
//
// What this value is:
//   The mean wind speed COINCIDENT with the design temperature, at the 10 m
//   met-station height, from the ASHRAE Handbook — Fundamentals (2021) Ch. 14
//   climatic design conditions:
//     heatingWindMs  <- "MCWS/PCWD to 99.6 % DB"  (heating design day)
//     coolingWindMs  <- "MCWS/PCWD to 0.4 % DB"   (cooling design day)
//   It drives only the wind-pressure term of the stack + wind infiltration
//   model (BlockLoadEngine.CibseInfiltrationLs, dP_wind = 0.5·Cp·rho·v²).
//
// What it is NOT:
//   NEVER a structural design wind. BS EN 1991-1-4 / UK NA basic wind
//   velocity vb,0 (about 20–25 m/s, a 50-year return-period value) and
//   PRJ_REGION_WIND_MS from the regional defaults are structural figures;
//   used here they would overstate infiltration by more than ten times.
//
// When a site records no wind:
//   DefaultMs (4.0 m/s) is used and the *Assumed flag is set, so the load
//   report lists it as an assumption. 4 m/s is the wind speed behind the
//   BS EN ISO 6946 external surface resistance Rse = 0.04 m²K/W that the
//   envelope U-values already assume (EnvelopeDetector), so conduction and
//   infiltration rest on the same assumption. It is a convention, not site
//   data.
//
// Site records: every site in the corporate STING_CLIMATE_DATA.json carries
// heatingWindMs / coolingWindMs and a windSource object (station, WMO number,
// ASHRAE edition, design-conditions period, URL). The figures were read from
// the climate.onebuilding.org redistribution of the ASHRAE 2025 design
// conditions, not the printed handbook, so each windSource keeps a "verify"
// note; it is carried here as Source / Verify and the load report repeats it.
// Never estimate a value: a site without one stays at the default and flagged.
// A project override may supply them. The legacy single key "designWindMs" is
// still read and applies to whichever design day has no specific value.

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Climate
{
    /// <summary>Per-design-day wind speeds for one climate site. A null speed
    /// means the site record carries none, and <see cref="DesignWind.DefaultMs"/>
    /// is used with the matching Assumed flag set.</summary>
    public class SiteDesignWind
    {
        public double? HeatingRecordedMs { get; set; }
        public double? CoolingRecordedMs { get; set; }

        public double HeatingMs => HeatingRecordedMs ?? DesignWind.DefaultMs;
        public double CoolingMs => CoolingRecordedMs ?? DesignWind.DefaultMs;

        public bool HeatingAssumed => !HeatingRecordedMs.HasValue;
        public bool CoolingAssumed => !CoolingRecordedMs.HasValue;

        public double MsFor(bool cooling) => cooling ? CoolingMs : HeatingMs;
        public bool AssumedFor(bool cooling) => cooling ? CoolingAssumed : HeatingAssumed;

        /// <summary>Where the recorded figures come from, from the site's windSource
        /// object ("station, WMO n, edition"); empty when the record names none.</summary>
        public string Source { get; set; } = "";

        /// <summary>The windSource "verify" note: what is still unconfirmed about the
        /// recorded figures. Empty when the record carries none.</summary>
        public string Verify { get; set; } = "";

        /// <summary>True when a speed is recorded for at least one design day but the
        /// record does not say where it comes from.</summary>
        public bool RecordedWithoutSource =>
            (HeatingRecordedMs.HasValue || CoolingRecordedMs.HasValue) && Source.Length == 0;

        /// <summary>Wind keys present in the record but not usable (not a positive
        /// number). The caller logs them; they are treated as not recorded.</summary>
        public List<string> RejectedKeys { get; } = new List<string>();
    }

    public static class DesignWind
    {
        /// <summary>Used when a site records no wind for the design day. See the
        /// file header for why 4.0 m/s.</summary>
        public const double DefaultMs = 4.0;

        /// <summary>Reads heatingWindMs / coolingWindMs from a site record, with
        /// the legacy designWindMs applied to whichever day has no specific value.
        /// A missing, non-numeric or non-positive value counts as not recorded.</summary>
        public static SiteDesignWind FromSiteRecord(JObject site)
        {
            var w = new SiteDesignWind();
            if (site == null) return w;
            double? legacy = Read(site, "designWindMs", w);
            w.HeatingRecordedMs = Read(site, "heatingWindMs", w) ?? legacy;
            w.CoolingRecordedMs = Read(site, "coolingWindMs", w) ?? legacy;
            if (site["windSource"] is JObject src)
            {
                string station = ((string)src["station"] ?? "").Trim();
                string wmo     = ((string)src["wmo"] ?? "").Trim();
                string edition = ((string)src["ashraeEdition"] ?? "").Trim();
                var parts = new List<string>();
                if (station.Length > 0) parts.Add(station);
                if (wmo.Length > 0) parts.Add("WMO " + wmo);
                if (edition.Length > 0) parts.Add(edition);
                w.Source = string.Join(", ", parts);
                w.Verify = ((string)src["verify"] ?? "").Trim();
            }
            return w;
        }

        private static double? Read(JObject site, string key, SiteDesignWind w)
        {
            var t = site[key];
            if (t == null || t.Type == JTokenType.Null) return null;
            if ((t.Type == JTokenType.Float || t.Type == JTokenType.Integer) && t.Value<double>() > 0)
                return t.Value<double>();
            w.RejectedKeys.Add(key);
            return null;
        }
    }
}
