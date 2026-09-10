// ══════════════════════════════════════════════════════════════════════════
//  CsvRateLookup.cs — the CSV rate table's five passes, lifted out of the
//  provider so they can be run without Revit.
//
//  WHY. `CsvRateProvider` is the provider that prices most of a model, and its
//  pass ORDER is the thing that has gone wrong before — K-16b found category
//  consulted first and returning, which made the PROD pass dead and priced a
//  fire door and a cupboard door alike. None of that logic needs a Document; it
//  needs a dictionary and five strings. It was untestable only because it sat
//  in a file that imports the Revit API for other classes.
//
//  This is the compute half. `CsvRateProvider.Resolve` is now the present half:
//  it unpacks a RateRequest, calls this, and packs a RateLookup. Same code, one
//  copy — a second implementation for tests would prove nothing about the one
//  that runs.
//
//  PASS ORDER IS SPECIFICITY ORDER, and confidence follows it:
//
//      DISC|PROD (97)  →  PROD (95)  →  CATEGORY|SYSTEM (92)
//                      →  MAT_CODE (85)  →  CATEGORY (70)
//
//  A category rate is the LEAST specific answer available, not the most
//  confident one.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.BOQ.Rates
{
    /// <summary>One resolved rate, before it is dressed as a <c>RateLookup</c>.</summary>
    public sealed class CsvRateMatch
    {
        public double UnitRate;
        public string Unit = "each";
        public int Confidence;
        public RateResolutionLevel Level;
        /// <summary>The sentence the audit reads. Asserted by the end-to-end test,
        /// because a rate that happens to be right for another reason is not this
        /// working.</summary>
        public string Provenance = "";
        public string MatchedKey = "";
    }

    public static class CsvRateLookup
    {
        /// <summary>
        /// The five passes, most specific first. Returns null when nothing matched —
        /// which is a real answer, not an error, and must not become a default rate.
        /// </summary>
        public static CsvRateMatch Resolve(
            IReadOnlyDictionary<string, (double rate, string unit)> rates,
            string sourceFile,
            string categoryName, string discipline, string prodCode,
            string systemType, string matCode)
        {
            if (rates == null || rates.Count == 0) return null;
            string src = sourceFile ?? "cost_rates_5d.csv";

            // Pass 0 (D6) — DISCIPLINE + PRODUCT. The most specific key there is.
            //
            // PROD alone is not unique: Air Terminals carries a mechanical air terminal
            // and a lightning air terminal, both GRL in ProdMap, at different rates.
            // DISC (M vs E) separates them without inventing a PROD code or migrating
            // ProdMap — which would have touched every tag in every existing model.
            if (!string.IsNullOrEmpty(prodCode) && !string.IsNullOrEmpty(discipline)
                && rates.TryGetValue(discipline + "|" + prodCode, out var byDiscProd))
                return new CsvRateMatch
                {
                    UnitRate = byDiscProd.rate,
                    Unit = byDiscProd.unit ?? "each",
                    Confidence = 97,
                    Level = RateResolutionLevel.Product,
                    Provenance = src + " product match (" + discipline + "|" + prodCode + ")",
                    MatchedKey = discipline + "|" + prodCode,
                };

            // Pass 1 — PRODUCT without discipline. Kept for rate cards that carry a
            // globally-unique PROD code and no discipline column.
            if (!string.IsNullOrEmpty(prodCode) && rates.TryGetValue(prodCode, out var byProd))
                return new CsvRateMatch
                {
                    UnitRate = byProd.rate,
                    Unit = byProd.unit ?? "each",
                    Confidence = 95,
                    Level = RateResolutionLevel.Product,
                    Provenance = src + " PROD match (" + prodCode + ")",
                    MatchedKey = prodCode,
                };

            // Pass 2 (RC-2) — CATEGORY|SYSTEM. Lets a project price otherwise-identical
            // categories differently by ASS_SYSTEM_TYPE_TXT ("Pipes|MedicalGas").
            if (!string.IsNullOrEmpty(categoryName) && !string.IsNullOrEmpty(systemType))
            {
                string sysKey = categoryName + "|" + systemType;
                if (rates.TryGetValue(sysKey, out var bySys))
                    return new CsvRateMatch
                    {
                        UnitRate = bySys.rate,
                        Unit = bySys.unit ?? "each",
                        Confidence = 92,
                        Level = RateResolutionLevel.System,
                        Provenance = src + " system match (" + systemType + ")",
                        MatchedKey = sysKey,
                    };
            }

            // Pass 3 — MATERIAL. Empty for every element ever costed until W2, because
            // MAT_CODE was read off the element and is bound to Materials.
            if (!string.IsNullOrEmpty(matCode) && rates.TryGetValue(matCode, out var byMat))
                return new CsvRateMatch
                {
                    UnitRate = byMat.rate,
                    Unit = byMat.unit ?? "each",
                    Confidence = 85,
                    Level = RateResolutionLevel.Material,
                    Provenance = src + " MAT_CODE match",
                    MatchedKey = matCode,
                };

            // Pass 4 — CATEGORY. An average across every product in the category.
            // Legitimate as a fallback, dishonest as a default: flagged so the QS can
            // see how many lines were priced this way (2.4).
            if (!string.IsNullOrEmpty(categoryName) && rates.TryGetValue(categoryName, out var direct))
                return new CsvRateMatch
                {
                    UnitRate = direct.rate,
                    Unit = direct.unit ?? "each",
                    Confidence = 70,
                    Level = RateResolutionLevel.Category,
                    Provenance = src + " category average (" + categoryName + ")",
                    MatchedKey = categoryName,
                };

            return null;
        }
    }
}
