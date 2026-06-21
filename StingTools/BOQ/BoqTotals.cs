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
//  Extensible-Storage rate override). Preliminaries and contingency still apply
//  to the full net works; only the OH&P base is reduced so loaded lines are not
//  marked up twice. When no line carries loaded OH&P, ohpBaseWorks == netWorks
//  and the result is unchanged.
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
        public double NetWorks;          // Σ line totals
        public double OhpBaseWorks;      // net works excluding OH&P-loaded lines
        public double Preliminaries;
        public double OverheadProfit;
        public double Contingency;
        public double SubTotalExclVat;   // net + prelims + ohp + contingency
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
            MarkupMode mode)
        {
            double net = netWorks > 0 ? netWorks : 0;
            double ohpBase = ohpBaseWorks > 0 && ohpBaseWorks <= net ? ohpBaseWorks : net;
            double p = Clamp(prelimPct) / 100.0;
            double o = Clamp(overheadPct) / 100.0;
            double c = Clamp(contingencyPct) / 100.0;
            double v = Clamp(vatPct) / 100.0;

            double prelims = net * p;

            double ohp = mode == MarkupMode.Cascade
                ? ohpBase * (1.0 + p) * o     // OH&P on (works + prelims)
                : ohpBase * o;                // legacy flat

            double contingency = mode == MarkupMode.Cascade
                ? (net + prelims + ohp) * c   // contingency on construction cost
                : net * c;                    // legacy flat

            double subTotal = net + prelims + ohp + contingency;
            double vat = subTotal * v;
            double contract = subTotal + vat;

            return new BoqTotalsResult
            {
                NetWorks = Math.Round(net, 0),
                OhpBaseWorks = Math.Round(ohpBase, 0),
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
