// ══════════════════════════════════════════════════════════════════════════
//  PrimaryMaterial.cs — the single answer to "which material is this element?"
//
//  E-5. There were THREE implementations, not two, and two of them were
//  non-deterministic:
//
//    BOQCostManager.GetPrimaryMaterialName          dominant by volume    ✓
//      → density, embodied carbon, bill description, waste (after E-4)
//
//    MaterialLibraryRateProvider.ResolvePrimaryMaterialName
//      → Material param, else GetMaterialIds(false).First()               ✗
//      → the RATE. So a compound wall could be priced off whichever skin
//        Revit happened to enumerate first and carbon-counted off its core.
//
//    BOQTemplateLibraryExtensions.GetMaterialName
//      → GetMaterialIds(false).First(), else STRUCTURAL_MATERIAL_PARAM     ✗
//      → the [material] token in NRM2 paragraph text.
//
//  `.First()` on GetMaterialIds is not a documented ordering. Two rows of the
//  same wall type could disagree, and the same bill re-run could disagree with
//  itself — which is the worst shape of defect here, because it looks like a
//  data-entry difference rather than a bug.
//
//  RESOLUTION ORDER — dominant-by-volume leads, because it is the only tier
//  that is both deterministic and defensible for a compound assembly:
//
//    1. Dominant material by volume across GetMaterialIds(false).
//       Ties broken by material name (ordinal) so the answer does not depend
//       on Revit's enumeration order even when every volume is zero.
//    2. The explicit Material instance parameter (MATERIAL_ID_PARAM).
//    3. STRUCTURAL_MATERIAL_PARAM.
//
//  Tiers 2 and 3 are the fallbacks the two replaced implementations carried;
//  keeping them means this is a strict superset — no call site loses a source
//  it previously had, and GetPrimaryMaterialName gains two it lacked.
// ══════════════════════════════════════════════════════════════════════════
using System;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.BOQ
{
    public static class PrimaryMaterial
    {
        /// <summary>
        /// The element's governing material name, or "" when none can be found.
        /// Deterministic: the same element always yields the same answer.
        /// </summary>
        public static string Resolve(Element el)
        {
            if (el == null) return "";
            Document doc = null;
            try { doc = el.Document; } catch { }
            if (doc == null) return "";

            // 1. Dominant by volume.
            try
            {
                var ids = el.GetMaterialIds(false);
                if (ids != null && ids.Count > 0)
                {
                    string bestName = null;
                    double bestVol = double.NegativeInfinity;

                    foreach (var id in ids)
                    {
                        if (id == null || id.Value <= 0) continue;
                        string name;
                        try { name = (doc.GetElement(id) as Material)?.Name; } catch { continue; }
                        if (string.IsNullOrEmpty(name)) continue;

                        double v;
                        try { v = el.GetMaterialVolume(id); } catch { v = 0; }

                        // Strictly-greater volume wins; on an exact tie the
                        // ordinally-smaller name wins. Without the tie-break, an
                        // element whose materials all report zero volume would
                        // resolve by enumeration order — the defect this replaces.
                        if (v > bestVol ||
                            (v == bestVol && bestName != null &&
                             string.CompareOrdinal(name, bestName) < 0))
                        {
                            bestVol = v;
                            bestName = name;
                        }
                    }

                    if (!string.IsNullOrEmpty(bestName)) return bestName;
                }
            }
            catch (Exception ex) { StingLog.WarnRateLimited("PrimMat.Vol", $"PrimaryMaterial volume tier: {ex.Message}"); }

            // 2. Explicit Material instance parameter.
            string byParam = FromParam(doc, el.LookupParameter("Material"))
                          ?? FromParam(doc, SafeBuiltIn(el, BuiltInParameter.MATERIAL_ID_PARAM));
            if (!string.IsNullOrEmpty(byParam)) return byParam;

            // 3. Structural material reference (framing / columns / walls).
            string byStruct = FromParam(doc, SafeBuiltIn(el, BuiltInParameter.STRUCTURAL_MATERIAL_PARAM));
            return byStruct ?? "";
        }

        private static Parameter SafeBuiltIn(Element el, BuiltInParameter bip)
        {
            try { return el.get_Parameter(bip); } catch { return null; }
        }

        private static string FromParam(Document doc, Parameter p)
        {
            try
            {
                if (p == null || p.StorageType != StorageType.ElementId) return null;
                var mid = p.AsElementId();
                if (mid == null || mid.Value <= 0) return null;
                string n = (doc.GetElement(mid) as Material)?.Name;
                return string.IsNullOrEmpty(n) ? null : n;
            }
            catch { return null; }
        }
    }
}
