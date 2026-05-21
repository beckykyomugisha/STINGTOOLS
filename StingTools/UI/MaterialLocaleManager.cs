using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Per-project locale: region driver for unit + currency display
    /// across the MAT tab. Stored once in <c>ProjectInformation</c> via
    /// the existing PRJ_REGION_TXT parameter (when bound) and re-read
    /// every time a row is built so changes take effect on the next
    /// Refresh without restarting Revit.
    ///
    /// Five canonical regions cover most of STING's surface area
    /// (UK / EU / US / AU / Africa). Each region pins:
    ///   - currency symbol  (£ / € / $ / A$ / $)
    ///   - density unit     (kg/m³ vs lb/ft³)
    ///   - cost format      (thousands separator + decimals)
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
        public const string ProjectParam = "PRJ_REGION_TXT";

        public static MaterialLocale Resolve(Document doc)
        {
            var region = ReadRegionFromProject(doc);
            return BuildLocale(region);
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
            if (doc?.ProjectInformation == null) return MaterialRegion.UK;
            try
            {
                var p = doc.ProjectInformation.LookupParameter(ProjectParam);
                if (p != null && p.HasValue && p.StorageType == StorageType.String)
                {
                    var raw = (p.AsString() ?? "").Trim();
                    if (Enum.TryParse<MaterialRegion>(raw, true, out var r)) return r;
                }
            }
            catch (Exception ex) { StingLog.Warn($"MaterialLocaleManager read: {ex.Message}"); }
            return MaterialRegion.UK;
        }

        public static void WriteRegionToProject(Document doc, MaterialRegion region)
        {
            if (doc?.ProjectInformation == null) return;
            using (var t = new Transaction(doc, "STING Set Material Region"))
            {
                t.Start();
                try
                {
                    var p = doc.ProjectInformation.LookupParameter(ProjectParam);
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    {
                        p.Set(region.ToString());
                        t.Commit();
                        MaterialAuditLogger.Log(doc, "MAT_RegionChange", "(project)",
                            new Dictionary<string, object> { ["region"] = region.ToString() });
                        return;
                    }
                    t.RollBack();
                    Autodesk.Revit.UI.TaskDialog.Show("Region",
                        $"Project parameter '{ProjectParam}' isn't bound. Load STING shared parameters from the dock panel, then re-try.");
                }
                catch (Exception ex) { try { t.RollBack(); } catch { } StingLog.Warn($"WriteRegion: {ex.Message}"); }
            }
        }
    }
}
