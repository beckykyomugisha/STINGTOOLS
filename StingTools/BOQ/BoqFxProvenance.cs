// StingTools — FX provenance for a priced BOQ line.
//
// Revit-free and in its own file so it can be <Compile Include>d into
// StingTools.Boq.Tests. BOQCostManager imports Autodesk.Revit.DB, so the rule below
// would otherwise be unreachable by any automated test.
//
// ASS_CST_FX_DATE_DT records WHEN the exchange rate behind a foreign-currency rate was
// fixed. Three places write it — Fohlio_Import, CostStamp and Cost_MigrateCurrencyParams
// — and until now nothing read it. A stamped value nobody surfaces is indistinguishable
// from one that was never written, and "which day's rate produced this number" is exactly
// what a QS is asked at valuation.
//
// The rule is small but not obvious, which is why it lives somewhere it can be asserted:
// a fixing date is shown ONLY on a line whose rate was actually converted. Printing one
// against a rate that was already in the document currency would imply a conversion that
// never happened — a confident wrong provenance, which is worse than none.

namespace StingTools.BOQ
{
    /// <summary>How a priced line's FX basis is reported on the bill.</summary>
    public static class BoqFxProvenance
    {
        /// <summary>True when the registry converted this rate from another currency.
        /// The FX adapter leaves <c>SourceCurrencyCode</c> empty when source and target
        /// already matched, so a non-empty value IS the record that a conversion ran.</summary>
        public static bool WasConverted(string sourceCurrency) =>
            !string.IsNullOrWhiteSpace(sourceCurrency);

        /// <summary>The fixing date to show on the line: the element's stamp when a
        /// conversion happened, otherwise empty.</summary>
        public static string FxDateFor(string sourceCurrency, string stampedDate) =>
            WasConverted(sourceCurrency) ? (stampedDate ?? "").Trim() : "";

        /// <summary>The note a converted-but-undated line needs, or null when the line
        /// needs none.
        ///
        /// <para>A converted rate with no fixing date cannot be defended — nobody can say
        /// which day's rate produced the figure — so the gap is stated on the line rather
        /// than left as a blank cell that reads like "not applicable".</para></summary>
        public static string MissingFixingDateNote(string sourceCurrency, string stampedDate)
        {
            if (!WasConverted(sourceCurrency)) return null;
            if (!string.IsNullOrWhiteSpace(stampedDate)) return null;
            return $"FX applied (converted from {sourceCurrency.Trim()}) with no fixing date recorded";
        }
    }
}
