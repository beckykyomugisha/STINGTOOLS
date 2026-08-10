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

        /// <summary>
        /// D9 — a rate with no unit is not a rate.
        /// <para>
        /// Both tiers below used to fall back to "each" when the unit was unknown, so a
        /// per-m² material silently billed per item. That is exactly what produced two
        /// block families at UGX 2,220 and UGX 96,200 with nothing to say which was
        /// which: 213 of the 1,279 library rows carry a RATE and a NULL
        /// MAT_COST_UNIT_OF_MEASURE.
        /// </para>
        /// <para>
        /// Returns null rather than guessing — A-1/H-1: never emit a number you could
        /// not measure. The caller drops to the next provider, and the miss is counted
        /// so the gap is visible instead of priced.
        /// </para>
        /// </summary>
        private static bool UnitIsKnown(RateRequest req, string matName, out string unit)
        {
            unit = req?.Unit;
            if (!string.IsNullOrWhiteSpace(unit)) return true;
            StingLog.WarnRateLimited("MatLibRate.NoUnit",
                $"Material '{matName}' has a rate but NO unit of measure. Refusing to price it "
              + "rather than defaulting to 'each' — a per-m2 rate billed per item is the "
              + "UGX 2,220 vs 96,200 defect. Populate MAT_COST_UNIT_OF_MEASURE.");
            return false;
        }

        public RateLookup Resolve(RateRequest req)
        {
            if (req?.Element == null) return null;
            // E-6 — every exit below is now recorded. A miss here silently drops
            // the row to CsvRateProvider's CATEGORY-keyed rate, so "Walls" prices
            // the same whether it is 200 mm hollow block or a glazed screen.
            MaterialRateMissLog.RecordAttempt();
            string cat = null;
            try { cat = req.Element.Category?.Name; } catch { }
            try
            {
                var doc = req.Element.Document;
                if (doc == null) return null;

                string matName = ResolvePrimaryMaterialName(req.Element);
                if (string.IsNullOrWhiteSpace(matName))
                {
                    // Not a rate-library gap — a modelling gap. Counted separately
                    // so the report does not blame the library for it.
                    MaterialRateMissLog.RecordNoMaterial(cat);
                    return null;
                }

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
                            {
                                if (!UnitIsKnown(req, matName, out string t1Unit)) return null;   // D9
                                MaterialRateMissLog.RecordHit();
                                return new RateLookup
                                {
                                    // CA-1 — ALL_MODEL_COST holds USD, so the registry's FX
                                    // layer must rebase it (× UGX_PER_USD, RateCurrency.ToUgx).
                                    //
                                    // The earlier "UGX" label was reasoned from the human case:
                                    // someone typing into Revit's material browser does enter
                                    // project-base currency. But almost nothing is hand-typed.
                                    // MaterialCommands writes ALL_MODEL_COST from the library's
                                    // MAT_COST_UNIT_USD column, and that is where all 1,279 rows
                                    // come from — so labelling it UGX suppressed a conversion the
                                    // value needed, and every material rate priced ~3,700× low.
                                    // At priority 95 it also OUTRANKS the correct category rate
                                    // from CsvRateProvider (90), so the wrong figure won.
                                    //
                                    // Measured across the shipped library: MAT_COST_UNIT_UGX is
                                    // not independent data — it is MAT_COST_UNIT_USD × 3700 on
                                    // every one of the 815 BLE rows and 441 of 464 MEP rows (the
                                    // rest are 3750/3722 rounding). So the UGX column cannot be
                                    // the fix: reading it would freeze a 2026 rate into every
                                    // material permanently and drift as the shilling moves.
                                    // The library's real price is USD. Label it honestly and let
                                    // the one FX layer convert, so rates follow UGX_PER_USD.
                                    //
                                    // Residual ambiguity, unresolved here: a hand-typed UGX cost
                                    // is now read as USD and inflated. The provider cannot tell
                                    // the two apart while both share ALL_MODEL_COST — the real
                                    // fix is a dedicated STING_MAT_RATE_* pair stamped at
                                    // material creation (gap E-12), which is not this pass.
                                    UnitRate = v,
                                    CurrencyCode = "USD",
                                    Unit = t1Unit,
                                    SourceId = Id,
                                    Confidence = 95,
                                    Provenance = $"Material '{mat.Name}' ALL_MODEL_COST (live, MAT panel)",
                                    MatchedKey = mat.Name,
                                };
                            }
                        }
                    }
                    catch (Exception ex) { StingLog.WarnRateLimited("MatLibRate.MatParam", $"MatLibRate mat param: {ex.Message}"); }
                }

                // Tier 2 — Corporate MATERIAL_LOOKUP.csv.
                double libVal = StingTools.UI.MaterialLookupCsv.GetCost(matName);
                if (libVal > 0)
                {
                    if (!UnitIsKnown(req, matName, out string t2Unit)) return null;   // D9
                    MaterialRateMissLog.RecordHit();
                    return new RateLookup
                    {
                        // CA-1 — MATERIAL_LOOKUP.csv is a DIFFERENT source from the
                        // USD material library above, so it keeps the base-currency
                        // (UGX) label; do not read the ALL_MODEL_COST note as applying
                        // here. Unverified either way in practice: the file carries no
                        // cost property in any group, so GetCost returns 0 and this
                        // tier is unreachable (gap E-8). Settle the currency when a
                        // cost column is actually added.
                        UnitRate = libVal,
                        CurrencyCode = "UGX",
                        Unit = t2Unit,
                        SourceId = Id,
                        Confidence = 90,
                        Provenance = $"Material '{matName}' MATERIAL_LOOKUP.csv (corporate)",
                        MatchedKey = matName,
                    };
                }

                // Named material, no price in any tier — the real library gap.
                MaterialRateMissLog.RecordMiss(matName, cat);
                return null;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("MatLibRate", $"MaterialLibraryRateProvider.Resolve: {ex.Message}"); }
            return null;
        }

        // E-5 — was the non-deterministic one: Material param, else the FIRST id
        // out of GetMaterialIds(false). `.First()` is not a documented ordering,
        // so a compound wall could be PRICED off its plaster skin while being
        // carbon-counted off its blockwork core — and the same bill re-run could
        // disagree with itself. Now the shared dominant-by-volume resolver, the
        // same one density, carbon, waste and the description already used.
        // Returns null rather than "" so the existing miss path is unchanged.
        private static string ResolvePrimaryMaterialName(Element el)
        {
            string n = StingTools.BOQ.PrimaryMaterial.Resolve(el);
            return string.IsNullOrEmpty(n) ? null : n;
        }
    }
}
