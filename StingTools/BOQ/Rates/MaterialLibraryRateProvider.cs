// ══════════════════════════════════════════════════════════════════════════
//  MaterialLibraryRateProvider.cs — N+8.
//
//  Resolves unit rates from the live Material library:
//    Tier 1 — Material element's ALL_MODEL_COST parameter (project override
//             set inline via MAT > Browse cell-edit)
//    Tier 2 — MaterialLookupCsv corporate baseline
//
//  Priority 85 (Phase 195 re-rank) — sits below the project rate card (93),
//  the ES manual correction (98), Fohlio PO price (96) and inline parameter
//  override (100), but above the COBie type map (75) and 4D default (60).
//  Rationale: a negotiated project rate card / sub-contractor quote and a
//  per-element manual correction both reflect commercial intent that should
//  beat a generic material-library cost. Override via
//  _BIM_COORD/boq_rate_policy.json (RatePolicy overlay).
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
        public int Priority => 85;
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
                // P-1 — Routed through MaterialNameCache (O(1) lookup) to
                // avoid the per-element FilteredElementCollector scan.
                var mat = StingTools.UI.MaterialNameCache.ResolveMaterial(doc, matName);
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
