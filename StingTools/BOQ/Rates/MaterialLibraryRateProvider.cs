// ══════════════════════════════════════════════════════════════════════════
//  MaterialLibraryRateProvider.cs — N+8.
//
//  Resolves unit rates from the live Material library:
//    Tier 1 — Material element's ALL_MODEL_COST parameter (project override
//             set inline via MAT > Browse cell-edit)
//    Tier 2 — MaterialLookupCsv corporate baseline
//
//  Priority 95 — sits above the CSV category match (90) but below explicit
//  parameter overrides (100), so a project that has curated material cost
//  in the MAT panel always beats the cost_rates_5d.csv category rate.
//
//  Closes BOQ-2 + BOQ-11 from the integration audit.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.BOQ.Rates
{
    internal sealed class MaterialLibraryRateProvider : IRateProvider
    {
        public string Id => "material-library";
        public int Priority => 95;
        public bool RequiresNetwork => false;

        public RateLookup Resolve(RateRequest req)
        {
            if (req?.Element == null) return null;
            try
            {
                var doc = req.Element.Document;
                if (doc == null) return null;

                string matName = ResolvePrimaryMaterialName(req.Element);
                if (string.IsNullOrWhiteSpace(matName)) return null;

                // Tier 1 — Live Material element's ALL_MODEL_COST.
                var mat = new FilteredElementCollector(doc).OfClass(typeof(Material))
                    .Cast<Material>()
                    .FirstOrDefault(m => string.Equals(m.Name, matName, StringComparison.OrdinalIgnoreCase));
                if (mat != null)
                {
                    try
                    {
                        var cp = mat.get_Parameter(BuiltInParameter.ALL_MODEL_COST);
                        if (cp != null && cp.StorageType == StorageType.Double)
                        {
                            double v = cp.AsDouble();
                            if (v > 0)
                                return new RateLookup
                                {
                                    UnitRate = v,
                                    CurrencyCode = "USD", // ALL_MODEL_COST is project-currency; BOQ FX layer rebases.
                                    Unit = string.IsNullOrEmpty(req.Unit) ? "each" : req.Unit,
                                    SourceId = Id,
                                    Confidence = 95,
                                    Provenance = $"Material '{mat.Name}' ALL_MODEL_COST (live, MAT panel)",
                                    MatchedKey = mat.Name,
                                };
                        }
                    }
                    catch (Exception ex) { StingLog.WarnRateLimited("MatLibRate.MatParam", $"MatLibRate mat param: {ex.Message}"); }
                }

                // Tier 2 — Corporate MATERIAL_LOOKUP.csv.
                double libVal = StingTools.UI.MaterialLookupCsv.GetCost(matName);
                if (libVal > 0)
                    return new RateLookup
                    {
                        UnitRate = libVal,
                        CurrencyCode = "USD",
                        Unit = string.IsNullOrEmpty(req.Unit) ? "each" : req.Unit,
                        SourceId = Id,
                        Confidence = 90,
                        Provenance = $"Material '{matName}' MATERIAL_LOOKUP.csv (corporate)",
                        MatchedKey = matName,
                    };
            }
            catch (Exception ex) { StingLog.WarnRateLimited("MatLibRate", $"MaterialLibraryRateProvider.Resolve: {ex.Message}"); }
            return null;
        }

        private static string ResolvePrimaryMaterialName(Element el)
        {
            try
            {
                Parameter p = el.LookupParameter("Material") ?? el.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    var mid = p.AsElementId();
                    if (mid != null && mid.Value > 0)
                        return el.Document?.GetElement(mid)?.Name;
                }
                var mats = el.GetMaterialIds(false);
                if (mats != null)
                    foreach (var mid in mats)
                        if (mid != null && mid.Value > 0)
                            return el.Document?.GetElement(mid)?.Name;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("MatLibRate.PrimMat", $"ResolvePrimaryMaterialName: {ex.Message}"); }
            return null;
        }
    }
}
