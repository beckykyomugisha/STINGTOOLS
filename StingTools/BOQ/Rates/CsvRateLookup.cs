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
//
//  DSCH-26 — a key the file declares NIL / INCL is an answer and stops the
//  passes, exactly like a rate. A key whose rate is 0 WITHOUT a declaration is
//  not an answer: that pass is skipped and the next, less specific one is
//  asked. (Before, a 0 on the PROD row was returned, and the registry then threw
//  away the whole CSV answer — the category rate in the same file included.)
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
        /// <summary>DSCH-26 — Nil / Included when the matched key is a declaration;
        /// <see cref="UnitRate"/> is then 0.</summary>
        public RateOutcome Outcome = RateOutcome.Priced;
        public string IncludedIn = "";
    }

    public static class CsvRateLookup
    {
        /// <summary>
        /// The five passes, most specific first. Returns null when nothing matched —
        /// which is a real answer, not an error, and must not become a default rate.
        /// <paramref name="declared"/> holds the keys the file prices NIL / INCL
        /// (<see cref="CostRateCsv.ToTables"/>); null means none.
        /// </summary>
        public static CsvRateMatch Resolve(
            IReadOnlyDictionary<string, (double rate, string unit)> rates,
            string sourceFile,
            string categoryName, string discipline, string prodCode,
            string systemType, string matCode,
            IReadOnlyDictionary<string, DeclaredRate> declared = null)
        {
            if ((rates == null || rates.Count == 0) && (declared == null || declared.Count == 0)) return null;
            string src = sourceFile ?? "cost_rates_5d.csv";

            // Pass 0 (D6) — DISCIPLINE + PRODUCT. The most specific key there is.
            //
            // PROD alone is not unique: Air Terminals carries a mechanical air terminal
            // and a lightning air terminal, both GRL in ProdMap, at different rates.
            // DISC (M vs E) separates them without inventing a PROD code or migrating
            // ProdMap — which would have touched every tag in every existing model.
            if (!string.IsNullOrEmpty(prodCode) && !string.IsNullOrEmpty(discipline))
            {
                string k = discipline + "|" + prodCode;
                var m = Try(rates, declared, k, 97, RateResolutionLevel.Product,
                            src + " product match (" + k + ")");
                if (m != null) return m;
            }

            // Pass 1 — PRODUCT without discipline. Kept for rate cards that carry a
            // globally-unique PROD code and no discipline column.
            if (!string.IsNullOrEmpty(prodCode))
            {
                var m = Try(rates, declared, prodCode, 95, RateResolutionLevel.Product,
                            src + " PROD match (" + prodCode + ")");
                if (m != null) return m;
            }

            // Pass 2 (RC-2) — CATEGORY|SYSTEM. Lets a project price otherwise-identical
            // categories differently by ASS_SYSTEM_TYPE_TXT ("Pipes|MedicalGas").
            if (!string.IsNullOrEmpty(categoryName) && !string.IsNullOrEmpty(systemType))
            {
                string sysKey = categoryName + "|" + systemType;
                var m = Try(rates, declared, sysKey, 92, RateResolutionLevel.System,
                            src + " system match (" + systemType + ")");
                if (m != null) return m;
            }

            // Pass 3 — MATERIAL. Empty for every element ever costed until W2, because
            // MAT_CODE was read off the element and is bound to Materials.
            if (!string.IsNullOrEmpty(matCode))
            {
                var m = Try(rates, declared, matCode, 85, RateResolutionLevel.Material,
                            src + " MAT_CODE match");
                if (m != null) return m;
            }

            // Pass 4 — CATEGORY. An average across every product in the category.
            // Legitimate as a fallback, dishonest as a default: flagged so the QS can
            // see how many lines were priced this way (2.4).
            if (!string.IsNullOrEmpty(categoryName))
            {
                var m = Try(rates, declared, categoryName, 70, RateResolutionLevel.Category,
                            src + " category average (" + categoryName + ")");
                if (m != null) return m;
            }

            return null;
        }

        /// <summary>One pass. A positive rate or a declaration answers; an absent key
        /// or an undeclared 0 does not (null — ask the next pass).</summary>
        private static CsvRateMatch Try(
            IReadOnlyDictionary<string, (double rate, string unit)> rates,
            IReadOnlyDictionary<string, DeclaredRate> declared,
            string key, int confidence, RateResolutionLevel level, string provenance)
        {
            if (rates != null && rates.TryGetValue(key, out var hit) && hit.rate > 0)
                return new CsvRateMatch
                {
                    UnitRate = hit.rate,
                    Unit = hit.unit ?? "each",
                    Confidence = confidence,
                    Level = level,
                    Provenance = provenance,
                    MatchedKey = key,
                };
            if (declared != null && declared.TryGetValue(key, out var d) && d != null
                && d.Outcome != RateOutcome.Priced)
                return new CsvRateMatch
                {
                    UnitRate = 0,
                    Unit = d.Unit ?? "each",
                    Confidence = confidence,
                    Level = level,
                    Provenance = provenance + " — declared " + RateOutcomeToken.ToToken(d.Outcome, d.IncludedIn),
                    MatchedKey = key,
                    Outcome = d.Outcome,
                    IncludedIn = d.IncludedIn ?? "",
                };
            return null;
        }
    }
}
