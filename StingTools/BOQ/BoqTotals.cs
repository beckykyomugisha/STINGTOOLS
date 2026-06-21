// ══════════════════════════════════════════════════════════════════════════
//  BoqTotals.cs — single source of truth for BOQ summary arithmetic.
//
//  ACCURACY FIX (audit #3, #4, #6). Before this, three places computed the
//  grand total independently and INCONSISTENTLY:
//    • BOQModels.GrandTotalUGX          — additive, no VAT
//    • BOQExportCommand totals block    — additive, no VAT
//    • BOQProfessionalExportCommand     — additive + VAT
//  and all three applied preliminaries, contingency AND overhead+profit as a
//  flat percentage of the NET measured works, then summed — which is not how
//  NRM/estimating practice cascades the base.
//
//  This pure, host-free helper is now the ONLY place markups are applied, so
//  every surface (model dashboard, basic export, tender export, snapshot list)
//  agrees to the shilling. Unit-tested in StingTools.Boq.Tests.
//
//  Two modes:
//    Cascade (default, NRM-aligned)
//        prelims      = net × P%
//        ohp          = (ohpBase × (1 + P%)) × O%     ← OH&P sits on works+prelims
//        contingency  = (net + prelims + ohp) × C%    ← on the construction cost
//    Flat (legacy, opt-in via COST_MARKUP_MODE="flat")
//        prelims      = net × P%
//        ohp          = ohpBase × O%
//        contingency  = net × C%
//
//  In BOTH modes:
//        subTotalExclVat = net + prelims + ohp + contingency
//        vat             = subTotalExclVat × V%
//        contractSum     = subTotalExclVat + vat
//
//  FIX #6 — OH&P double-count: `ohpBaseWorks` is the net works EXCLUDING any
//  line whose unit rate already carries overhead+profit (e.g. a fully-loaded
//  Extensible-Storage rate override). Only the OH&P base is reduced so loaded
//  lines are not marked up twice.
//
//  Phase C.1 (KUT lifecycle review Finding 1) — `provisionalSumWorks` carries the
//  Σ of Provisional / Prime-Cost-sum line totals (e.g. Owner-procured FF&E priced
//  from the Fohlio register). A PC sum is a fixed, fully-inclusive allowance: the
//  contractor does not earn preliminaries OR contingency on it (NRM2 prices
//  attendance/profit on PC sums as separate items; design contingency sits on
//  measured works, not on the allowance). So the markup base is
//  `markupBase = netWorks − provisionalSumWorks`; prelims, OH&P and contingency
//  apply to that base, and the PC sums are added back AFTER the cascade, before
//  VAT. `vatOnPcSums` (default true — spent PC sums are VATable works) controls
//  whether VAT applies to the PC-sum portion. When provisionalSumWorks == 0 every
//  figure is byte-identical to the pre-C.1 result.
//
//  Zero Autodesk.Revit.* dependencies on purpose.
// ══════════════════════════════════════════════════════════════════════════
using System;

namespace StingTools.BOQ
{
    public enum MarkupMode
    {
        /// <summary>NRM-aligned cascading bases (default).</summary>
        Cascade = 0,
        /// <summary>Legacy flat % of net works for every markup.</summary>
        Flat = 1
    }

    /// <summary>Immutable breakdown of a BOQ summary. All figures in the
    /// document currency, rounded to whole units (UGX has no minor unit).</summary>
    public sealed class BoqTotalsResult
    {
        public double NetWorks;          // Σ line totals (incl. PC sums)
        public double OhpBaseWorks;      // net works excluding OH&P-loaded lines
        public double ProvisionalSums;   // PC/provisional-sum portion (excl. from markup)
        public double MarkupBase;        // netWorks − provisionalSums (prelims/contingency base)
        public double Preliminaries;
        public double OverheadProfit;
        public double Contingency;
        public double SubTotalExclVat;   // markupBase + prelims + ohp + contingency + PC sums
        public double Vat;
        public double ContractSum;       // subTotal + vat
        public MarkupMode Mode;
    }

    public static class BoqTotals
    {
        /// <summary>Parse the COST_MARKUP_MODE config string. Unknown / empty
        /// → Cascade (the correct default).</summary>
        public static MarkupMode ParseMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) return MarkupMode.Cascade;
            return mode.Trim().ToLowerInvariant() == "flat" ? MarkupMode.Flat : MarkupMode.Cascade;
        }

        /// <summary>
        /// Compute the full summary. <paramref name="ohpBaseWorks"/> defaults to
        /// <paramref name="netWorks"/> when the caller passes a non-positive value
        /// (i.e. "no OH&amp;P-loaded lines"). Negative percentages are clamped to 0.
        /// </summary>
        public static BoqTotalsResult Compute(
            double netWorks, double ohpBaseWorks,
            double prelimPct, double overheadPct, double contingencyPct, double vatPct,
            MarkupMode mode,
            double provisionalSumWorks = 0, bool vatOnPcSums = true)
        {
            double net = netWorks > 0 ? netWorks : 0;
            // Phase C.1 — PC/provisional sums are carried outside the markup cascade.
            double prov = provisionalSumWorks > 0 ? Math.Min(provisionalSumWorks, net) : 0;
            double markupBase = net - prov;
            // OH&P base never exceeds the markup base (it already excludes PC sums
            // when they carry RateIncludesOhp; clamp guards the ratio-derived caller).
            double ohpBase = ohpBaseWorks > 0 ? Math.Min(ohpBaseWorks, markupBase) : markupBase;
            double p = Clamp(prelimPct) / 100.0;
            double o = Clamp(overheadPct) / 100.0;
            double c = Clamp(contingencyPct) / 100.0;
            double v = Clamp(vatPct) / 100.0;

            double prelims = markupBase * p;

            double ohp = mode == MarkupMode.Cascade
                ? ohpBase * (1.0 + p) * o     // OH&P on (works + prelims)
                : ohpBase * o;                // legacy flat

            double contingency = mode == MarkupMode.Cascade
                ? (markupBase + prelims + ohp) * c   // contingency on construction cost
                : markupBase * c;                    // legacy flat

            // PC sums added back AFTER the cascade. (markupBase + prov == net, so for
            // prov == 0 this equals net + prelims + ohp + contingency — unchanged.)
            double subTotal = markupBase + prelims + ohp + contingency + prov;
            double vatBase = vatOnPcSums ? subTotal : subTotal - prov;
            double vat = vatBase * v;
            double contract = subTotal + vat;

            return new BoqTotalsResult
            {
                NetWorks = Math.Round(net, 0),
                OhpBaseWorks = Math.Round(ohpBase, 0),
                ProvisionalSums = Math.Round(prov, 0),
                MarkupBase = Math.Round(markupBase, 0),
                Preliminaries = Math.Round(prelims, 0),
                OverheadProfit = Math.Round(ohp, 0),
                Contingency = Math.Round(contingency, 0),
                SubTotalExclVat = Math.Round(subTotal, 0),
                Vat = Math.Round(vat, 0),
                ContractSum = Math.Round(contract, 0),
                Mode = mode
            };
        }

        private static double Clamp(double pct) => double.IsNaN(pct) || pct < 0 ? 0 : pct;
    }
}
