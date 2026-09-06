using System;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.BOQ;

namespace StingTools.UI
{
    /// <summary>
    /// I-1 — Stamp Pset_EnvironmentalImpactIndicators (IFC4 standard) +
    /// Pset_StingMaterial (custom) onto each element so the live
    /// material library state flows into IFC export.
    /// </summary>
    public static class IfcMaterialPsetWriter
    {
        /// <summary>
        /// H-1 — returns the number of parameters that ACTUALLY took a value.
        /// Zero means every target param was unbound or read-only and nothing
        /// was written; the caller must not report that as a success.
        /// </summary>
        public static int Stamp(Element el, BOQLineItem item)
        {
            if (el == null || item == null) return 0;
            int written = 0;
            try
            {
                string matName = ReadPrimaryMaterialName(el);
                if (string.IsNullOrWhiteSpace(matName)) return 0;
                var mat = MaterialNameCache.ResolveMaterial(el.Document, matName);

                double carbon = item.EmbodiedCarbonKg;
                string epdSrc = "", epdDate = "";
                if (mat != null)
                {
                    var c = mat.LookupParameter("STING_EMB_CARBON_NR");
                    if (c != null && c.HasValue && c.StorageType == StorageType.Double && c.AsDouble() > 0)
                    {
                        // value here is per-m³; the BOQ item already carries
                        // total carbon for this element.
                    }
                    var es = mat.LookupParameter("STING_MAT_EPD_SRC_TXT");
                    if (es != null && es.HasValue && es.StorageType == StorageType.String) epdSrc = es.AsString() ?? "";
                    var ed = mat.LookupParameter("STING_MAT_EPD_DATE_TXT");
                    if (ed != null && ed.HasValue && ed.StorageType == StorageType.String) epdDate = ed.AsString() ?? "";
                }
                string uniclass = MaterialUniclassMapper.ResolveCode(mat?.MaterialClass ?? "");

                // IFC4 Pset_EnvironmentalImpactIndicators (subset).
                // WP-C — the headline GWP is the A1-A3 FOSSIL figure (RICS WLCA);
                // biogenic (≤ 0) is written as a SEPARATE indicator, never netted in.
                if (Set(el, "Pset_EnvironmentalImpactIndicators", "GlobalWarmingPotential_PerLifeCycle", carbon)) written++;
                if (Set(el, "Pset_EnvironmentalImpactIndicators", "GlobalWarmingPotential_Biogenic", item.BiogenicKg)) written++;
                if (SetString(el, "Pset_EnvironmentalImpactIndicators", "ReferenceUnit", "kgCO2e")) written++;
                if (SetString(el, "Pset_EnvironmentalImpactIndicators", "CarbonScope", "A1-A3 fossil (upfront)")) written++;
                if (!string.IsNullOrEmpty(epdSrc))
                    if (SetString(el, "Pset_EnvironmentalImpactIndicators", "ProductionReference", epdSrc)) written++;

                // STING custom material Pset.
                if (SetString(el, "Pset_StingMaterial", "MaterialName", matName)) written++;
                if (SetString(el, "Pset_StingMaterial", "MaterialClass", mat?.MaterialClass ?? "")) written++;
                if (SetString(el, "Pset_StingMaterial", "UniclassCode", uniclass ?? "")) written++;
                if (SetString(el, "Pset_StingMaterial", "EpdSource",  epdSrc)) written++;
                if (SetString(el, "Pset_StingMaterial", "EpdDate",    epdDate)) written++;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("IfcMatPset", $"IfcMaterialPsetWriter.Stamp: {ex.Message}"); }
            return written;
        }

        private static string ReadPrimaryMaterialName(Element el)
        {
            try
            {
                var p = el.LookupParameter("Material") ?? el.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    var mid = p.AsElementId();
                    if (mid != null && mid.Value > 0) return el.Document?.GetElement(mid)?.Name;
                }
                var mats = el.GetMaterialIds(false);
                if (mats != null)
                    foreach (var mid in mats)
                        if (mid != null && mid.Value > 0) return el.Document?.GetElement(mid)?.Name;
            }
            catch { }
            return null;
        }

        // IFC Pset values are written through Revit's "<Pset>_<Property>" param
        // convention. Revit's IFC exporter picks these up when project
        // ProjectIfcExportSetup is configured to include user-defined Psets.
        /// <summary>True only when the value actually landed on a parameter.</summary>
        private static bool SetString(Element el, string pset, string prop, string value)
        {
            try
            {
                var p = el.LookupParameter($"{pset}_{prop}");
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    return p.Set(value ?? "");
            }
            catch (Exception ex) { StingLog.WarnRateLimited("IfcMatPset.Str", $"SetString '{pset}_{prop}': {ex.Message}"); }
            return false;
        }

        /// <summary>True only when the value actually landed on a parameter.</summary>
        private static bool Set(Element el, string pset, string prop, double value)
        {
            try
            {
                var p = el.LookupParameter($"{pset}_{prop}");
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                    return p.Set(value);
            }
            catch (Exception ex) { StingLog.WarnRateLimited("IfcMatPset.Num", $"Set '{pset}_{prop}': {ex.Message}"); }
            return false;
        }
    }
}
