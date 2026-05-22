// LpsRegionalNg.cs — Wave E #17.
//
// Climate-driven Ng (ground flash density, flashes/km²/yr) lookup.
// The LpsRiskInput.GroundFlashDensity defaulted to 2.0 (UK/EU
// temperate average) which is wrong for tropical / sub-Saharan
// projects where Ng routinely exceeds 10. This helper reads the
// project's active climate site (loaded for the HVAC engine), maps
// its latitude to one of four climate bands per BS EN 62305-1
// Annex A Fig A.1, and returns an Ng appropriate for that band.
//
// Returns 0 when no climate site is configured — caller falls back
// to STING_LPS_FLASH_DENSITY.json regional default or the user's
// explicit textbox value.

using System;
using Autodesk.Revit.DB;

namespace StingTools.Core.Lightning
{
    public static class LpsRegionalNg
    {
        /// <summary>
        /// Conservative regional Ng estimate (flashes / km² / yr)
        /// from absolute latitude. Tropics 12.0 · sub-tropics 4.0 ·
        /// temperate 1.5 · sub-polar 0.5. Returns 0 when no climate
        /// site is available.
        /// </summary>
        public static double EstimateFromClimate(Document doc)
        {
            if (doc == null) return 0;
            try
            {
                var site = StingTools.Core.Climate.ClimateRegistry.ActiveSite(doc);
                if (site == null) return 0;
                double absLat = Math.Abs(site.Lat);
                if (absLat <= 0.001) return 0; // no lat data → no estimate
                if (absLat < 23.5)  return 12.0;  // tropical band
                if (absLat < 35.0)  return 4.0;   // sub-tropical
                if (absLat < 50.0)  return 1.5;   // temperate
                return 0.5;                       // sub-polar
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"LpsRegionalNg.EstimateFromClimate: {ex.Message}");
                return 0;
            }
        }
    }
}
