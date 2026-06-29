// StingTools — Country → setup cascade (WS J1).
//
// On Country change, fills the climate site (default city), climate zone (the
// capital's ASHRAE 169 zone, or AshraeClimateZone.ClassifyByLatitude on its lat),
// and the grid + diesel carbon factors from the country seed — WITHOUT clobbering
// a value the user typed explicitly. This is the single fill point used by both
// the SETUP save and the engine run, so picking a country drives the result.
//
// Pure POCO — no Revit dependency. Unit-tested.

using System.Collections.Generic;

namespace StingTools.Core.Sustainability
{
    public static class CountryCascade
    {
        /// <summary>Fill blank/non-explicit location + carbon fields on
        /// <paramref name="setup"/> from <paramref name="row"/>. Returns the names of
        /// the fields actually applied (for reporting / tests). A null/default row or a
        /// null setup is a no-op.</summary>
        public static List<string> Apply(SustainProjectSetup setup, CountryRow row)
        {
            var applied = new List<string>();
            if (setup == null || row == null || row.IsDefault) return applied;

            // Climate site (default city) — display value; engine still synthesises
            // climate from the country latitude when the city isn't a registry id.
            if (string.IsNullOrWhiteSpace(setup.ClimateSiteId) && !string.IsNullOrWhiteSpace(row.DefaultCity))
            {
                setup.ClimateSiteId = row.DefaultCity;
                applied.Add("climateSite");
            }

            // Climate zone — the capital's zone, else latitude-derived.
            if (string.IsNullOrWhiteSpace(setup.ClimateZone))
            {
                setup.ClimateZone = !string.IsNullOrWhiteSpace(row.ClimateZone)
                    ? row.ClimateZone
                    : AshraeClimateZone.ClassifyByLatitude(row.Lat);
                applied.Add("climateZone");
            }

            // Grid + diesel — only when the user hasn't explicitly set them.
            if (setup.Supply != null)
            {
                if (!setup.Supply.GridCarbonExplicit && row.GridKgCo2ePerKwh > 0)
                {
                    setup.Supply.GridCarbonKgco2eKwh = row.GridKgCo2ePerKwh;
                    applied.Add("gridCarbon");
                }
                if (!setup.Supply.DieselCarbonExplicit && row.DieselKgCo2ePerKwh > 0)
                {
                    setup.Supply.DieselCarbonKgco2eKwh = row.DieselKgCo2ePerKwh;
                    applied.Add("dieselCarbon");
                }
            }
            return applied;
        }
    }
}
