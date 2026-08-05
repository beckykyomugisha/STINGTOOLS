using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.BOQ
{
    /// <summary>
    /// R-1 — Carbon factor resolver that reports the FACTOR + the UNIT
    /// it's expressed in. Closes the silent mass-vs-volume mismatch.
    ///
    /// Resolution chain:
    ///   1) Material.STING_EMB_CARBON_NR  (kgCO₂e/m³)
    ///   2) MaterialLookupCsv.GetCarbon    (kgCO₂e/m³)
    ///   3) CarbonTrackingEngine dict       (kgCO₂e/kg — legacy)
    ///   4) GetDefaultCarbonFactor keyword  (kgCO₂e/kg — legacy)
    /// The first two tiers return PerM3; tiers 3+4 return PerKg.
    /// Callers multiply by volume or mass accordingly.
    /// </summary>
    public enum CarbonFactorUnit { KgCo2ePerM3, KgCo2ePerKg, Unknown }

    public struct CarbonFactorResult
    {
        public double Factor;
        public CarbonFactorUnit PerUnit;
        public string Source;
    }

    public static class CarbonFactorResolver
    {
        public static CarbonFactorResult Resolve(Document doc, string materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName))
                return new CarbonFactorResult { Factor = 0, PerUnit = CarbonFactorUnit.Unknown, Source = "" };

            // Tier 1 — Material parameter (per m³).
            // P-2 — MaterialNameCache lookup is O(1); previously we ran a
            // fresh FilteredElementCollector per BOQ row.
            try
            {
                if (doc != null)
                {
                    var mat = StingTools.UI.MaterialNameCache.ResolveMaterial(doc, materialName);
                    if (mat != null)
                    {
                        var p = mat.LookupParameter("STING_EMB_CARBON_NR");
                        if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                        {
                            double v = p.AsDouble();
                            if (v > 0)
                                return new CarbonFactorResult { Factor = v, PerUnit = CarbonFactorUnit.KgCo2ePerM3, Source = "material-param" };
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.WarnRateLimited("CFRes.Mat", $"CarbonFactorResolver mat: {ex.Message}"); }

            // Tier 2 — Corporate library (per m³).
            try
            {
                double libVal = StingTools.UI.MaterialLookupCsv.GetCarbon(materialName);
                if (libVal > 0)
                    return new CarbonFactorResult { Factor = libVal, PerUnit = CarbonFactorUnit.KgCo2ePerM3, Source = "material-lookup-csv" };
            }
            catch (Exception ex) { StingLog.WarnRateLimited("CFRes.Lib", $"CarbonFactorResolver lookup: {ex.Message}"); }

            // Tier 3 + 4 — Legacy mass-based factor.
            try
            {
                double dictVal = StingTools.BIMManager.CarbonTrackingEngine.GetCarbonFactor(materialName);
                if (dictVal > 0)
                    return new CarbonFactorResult { Factor = dictVal, PerUnit = CarbonFactorUnit.KgCo2ePerKg, Source = "carbon-factors-csv" };
            }
            catch (Exception ex) { StingLog.WarnRateLimited("CFRes.Legacy", $"CarbonFactorResolver legacy: {ex.Message}"); }

            return new CarbonFactorResult { Factor = 0, PerUnit = CarbonFactorUnit.Unknown, Source = "none" };
        }

        // ── Z-25 — WLCA-separated A1-A3 carbon (RIBA 2030 / LETI / RICS) ──────
        // The net Resolve() above is unchanged. These expose the fossil and
        // biogenic components (per m³) so whole-life reports can list A1-A3
        // fossil + biogenic separately instead of a single net figure.
        //
        // Resolution: material param first (STING_EMB_CARBON_FOSSIL_NR /
        // _BIOGENIC_NR), then the corporate library. Fossil is >= 0; biogenic is
        // <= 0 for timber (sequestration) — so biogenic must NOT be gated on > 0
        // the way the net path is, or every sequestration credit would be dropped.
        //
        // NOTE: until MaterialCommands writes the two new params at material
        // creation (follow-up) AND/OR Z-24b adds timber rows to MATERIAL_LOOKUP,
        // these resolve to 0 at runtime — the authoritative split currently lives
        // in the BLE_/MEP_MATERIALS.csv PROP_CARBON_FOSSIL/BIOGENIC_KG_M3 columns.
        public static double GetCarbonFossilPerM3(Document doc, string materialName)
        {
            double v = ReadMatParam(doc, materialName, "STING_EMB_CARBON_FOSSIL_NR");
            if (v > 0) return v;
            try { return StingTools.UI.MaterialLookupCsv.GetCarbonFossil(materialName); }
            catch (Exception ex) { StingLog.WarnRateLimited("CFRes.Fossil", $"fossil: {ex.Message}"); return 0; }
        }

        public static double GetCarbonBiogenicPerM3(Document doc, string materialName)
        {
            // Allow negatives — sequestration is the whole point.
            if (TryReadMatParam(doc, materialName, "STING_EMB_CARBON_BIOGENIC_NR", out double v) && v != 0)
                return v;
            try { return StingTools.UI.MaterialLookupCsv.GetCarbonBiogenic(materialName); }
            catch (Exception ex) { StingLog.WarnRateLimited("CFRes.Bio", $"biogenic: {ex.Message}"); return 0; }
        }

        private static double ReadMatParam(Document doc, string materialName, string paramName)
            => TryReadMatParam(doc, materialName, paramName, out double v) ? v : 0;

        private static bool TryReadMatParam(Document doc, string materialName, string paramName, out double value)
        {
            value = 0;
            try
            {
                if (doc == null || string.IsNullOrWhiteSpace(materialName)) return false;
                var mat = StingTools.UI.MaterialNameCache.ResolveMaterial(doc, materialName);
                var p = mat?.LookupParameter(paramName);
                if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                {
                    value = p.AsDouble();
                    return true;
                }
            }
            catch (Exception ex) { StingLog.WarnRateLimited("CFRes.MatParam", $"{paramName}: {ex.Message}"); }
            return false;
        }
    }
}
