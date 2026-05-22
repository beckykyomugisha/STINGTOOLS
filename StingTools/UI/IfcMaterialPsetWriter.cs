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
        public static void Stamp(Element el, BOQLineItem item)
        {
            if (el == null || item == null) return;
            try
            {
                string matName = ReadPrimaryMaterialName(el);
                if (string.IsNullOrWhiteSpace(matName)) return;
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
                Set(el, "Pset_EnvironmentalImpactIndicators", "GlobalWarmingPotential_PerLifeCycle", carbon);
                Set(el, "Pset_EnvironmentalImpactIndicators", "ReferenceUnit", "kgCO2e");
                if (!string.IsNullOrEmpty(epdSrc))
                    SetString(el, "Pset_EnvironmentalImpactIndicators", "ProductionReference", epdSrc);

                // STING custom material Pset.
                SetString(el, "Pset_StingMaterial", "MaterialName", matName);
                SetString(el, "Pset_StingMaterial", "MaterialClass", mat?.MaterialClass ?? "");
                SetString(el, "Pset_StingMaterial", "UniclassCode", uniclass ?? "");
                SetString(el, "Pset_StingMaterial", "EpdSource",  epdSrc);
                SetString(el, "Pset_StingMaterial", "EpdDate",    epdDate);
            }
            catch (Exception ex) { StingLog.WarnRateLimited("IfcMatPset", $"IfcMaterialPsetWriter.Stamp: {ex.Message}"); }
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
        private static void SetString(Element el, string pset, string prop, string value)
        {
            try
            {
                var p = el.LookupParameter($"{pset}_{prop}");
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String) p.Set(value ?? "");
            }
            catch (Exception ex) { StingLog.WarnRateLimited("IfcMatPset.Str", $"SetString '{pset}_{prop}': {ex.Message}"); }
        }

        private static void Set(Element el, string pset, string prop, double value)
        {
            try
            {
                var p = el.LookupParameter($"{pset}_{prop}");
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double) p.Set(value);
            }
            catch (Exception ex) { StingLog.WarnRateLimited("IfcMatPset.Num", $"Set '{pset}_{prop}': {ex.Message}"); }
        }
    }
}
