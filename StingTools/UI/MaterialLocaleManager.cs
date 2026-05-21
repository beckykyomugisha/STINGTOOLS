using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Per-project locale: region driver for unit + currency display
    /// across the MAT tab.
    ///
    /// N+1 — Single-sourced through <see cref="StingTools.Standards.ProjectStandardsManager"/>
    /// so the MAT panel, the ProjectSetupWizard, and the HVAC / Plumbing
    /// surfaces all read from the same shared state. The legacy
    /// PRJ_REGION_TXT parameter is no longer touched.
    ///
    /// ProjectStandardsManager.Region keys (USA / UK / Europe /
    /// EastAfrica / Uganda / Kenya / SouthAfrica / Australia /
    /// International) map down to the five canonical
    /// <see cref="MaterialRegion"/> values STING uses for formatting.
    /// </summary>
    public enum MaterialRegion { UK, EU, US, AU, Africa }

    public class MaterialLocale
    {
        public MaterialRegion Region { get; set; } = MaterialRegion.UK;
        public string CurrencySymbol { get; set; } = "£";
        public string DensityUnit { get; set; } = "kg/m³";
        public string ThermalUnit { get; set; } = "W/m·K";
        public string CarbonUnit { get; set; } = "kgCO₂e";
        public System.Globalization.CultureInfo Culture { get; set; }
            = System.Globalization.CultureInfo.GetCultureInfo("en-GB");

        public string FormatCost(double v)
        {
            if (v <= 0) return "";
            return $"{CurrencySymbol}{v.ToString("N2", Culture)}";
        }

        public string FormatCarbon(double v)
            => v <= 0 ? "" : $"{v.ToString("N1", Culture)} {CarbonUnit}";
    }

    public static class MaterialLocaleManager
    {
        public static MaterialLocale Resolve(Document doc)
        {
            // ProjectStandardsManager.Instance.Region is the single source.
            // doc parameter is kept for the signature so all callers stay
            // unchanged when we route through the singleton.
            string raw = "";
            try { raw = StingTools.Standards.ProjectStandardsManager.Instance.Region ?? ""; }
            catch (Exception ex) { StingLog.Warn($"MaterialLocaleManager.Resolve read: {ex.Message}"); }
            return BuildLocale(MapToMaterialRegion(raw));
        }

        public static MaterialLocale BuildLocale(MaterialRegion region) => region switch
        {
            MaterialRegion.US     => new MaterialLocale
            {
                Region = region, CurrencySymbol = "$",
                DensityUnit = "lb/ft³", ThermalUnit = "Btu·in/h·ft²·°F",
                Culture = System.Globalization.CultureInfo.GetCultureInfo("en-US"),
            },
            MaterialRegion.EU     => new MaterialLocale
            {
                Region = region, CurrencySymbol = "€",
                DensityUnit = "kg/m³", ThermalUnit = "W/m·K",
                Culture = System.Globalization.CultureInfo.GetCultureInfo("de-DE"),
            },
            MaterialRegion.AU     => new MaterialLocale
            {
                Region = region, CurrencySymbol = "A$",
                DensityUnit = "kg/m³", ThermalUnit = "W/m·K",
                Culture = System.Globalization.CultureInfo.GetCultureInfo("en-AU"),
            },
            MaterialRegion.Africa => new MaterialLocale
            {
                Region = region, CurrencySymbol = "$",
                DensityUnit = "kg/m³", ThermalUnit = "W/m·K",
                Culture = System.Globalization.CultureInfo.GetCultureInfo("en-US"),
            },
            _                     => new MaterialLocale // UK default
            {
                Region = MaterialRegion.UK, CurrencySymbol = "£",
                DensityUnit = "kg/m³", ThermalUnit = "W/m·K",
                Culture = System.Globalization.CultureInfo.GetCultureInfo("en-GB"),
            },
        };

        public static MaterialRegion ReadRegionFromProject(Document doc)
        {
            // Reads the singleton — doc unused but kept for API stability.
            try { return MapToMaterialRegion(StingTools.Standards.ProjectStandardsManager.Instance.Region ?? ""); }
            catch (Exception ex) { StingLog.Warn($"MaterialLocaleManager.ReadRegion: {ex.Message}"); }
            return MaterialRegion.UK;
        }

        public static void WriteRegionToProject(Document doc, MaterialRegion region)
        {
            // Writes the singleton; ProjectStandardsManager fires its
            // StandardsChanged event so the HVAC / Plumbing surfaces
            // pick up the change automatically.
            try
            {
                string standardsKey = MapToStandardsKey(region);
                StingTools.Standards.ProjectStandardsManager.Instance.Region = standardsKey;
                MaterialAuditLogger.Log(doc, "MAT_RegionChange", "(project)",
                    new Dictionary<string, object> { ["region"] = region.ToString(), ["standardsKey"] = standardsKey });
            }
            catch (Exception ex) { StingLog.Warn($"WriteRegion: {ex.Message}"); }
        }

        /// <summary>
        /// Translate ProjectStandardsManager's region key (USA / UK / Europe /
        /// EastAfrica / Uganda / Kenya / SouthAfrica / Australia / International)
        /// into the MaterialRegion enum the MAT formatter understands.
        /// Unknown values fall back to UK.
        /// </summary>
        public static MaterialRegion MapToMaterialRegion(string standardsKey)
        {
            if (string.IsNullOrWhiteSpace(standardsKey)) return MaterialRegion.UK;
            switch (standardsKey.Trim().ToLowerInvariant())
            {
                case "usa": return MaterialRegion.US;
                case "uk":  return MaterialRegion.UK;
                case "europe": case "eu": return MaterialRegion.EU;
                case "australia": case "au": return MaterialRegion.AU;
                case "eastafrica": case "uganda": case "kenya": case "southafrica": case "africa":
                    return MaterialRegion.Africa;
                case "international": default: return MaterialRegion.UK;
            }
        }

        /// <summary>Inverse of MapToMaterialRegion — picks a canonical
        /// ProjectStandardsManager key for each MaterialRegion.</summary>
        public static string MapToStandardsKey(MaterialRegion region) => region switch
        {
            MaterialRegion.US     => "USA",
            MaterialRegion.UK     => "UK",
            MaterialRegion.EU     => "Europe",
            MaterialRegion.AU     => "Australia",
            MaterialRegion.Africa => "EastAfrica",
            _                     => "UK",
        };
    }
}
