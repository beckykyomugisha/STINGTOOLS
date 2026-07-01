// ══════════════════════════════════════════════════════════════════════════
//  BoqTotals.cs — the ONE canonical markup waterfall.
//
//  Extracted out of BOQModels.cs (P0-7 consolidation) so the markup math is
//  Document-free — zero Autodesk.Revit.* imports — and can be linked into the
//  headless test projects. Every BOQ surface (panel KPI, professional export,
//  4D/5D estimate, snapshot drift hash, budget variance) computes its Contract
//  Sum through BoqTotals.Compute so there is a single source of truth.
// ══════════════════════════════════════════════════════════════════════════
using System;

namespace StingTools.BOQ
{
    /// <summary>WP1 — the single canonical markup waterfall for a BOQ. Every
    /// component carries its absolute value so a caller can render the Grand
    /// Summary without re-deriving any base.</summary>
    public struct BoqMarkupBreakdown
    {
        public double Works;        // Σ measured-works line totals (ex any markup)
        public double Prelims;      // preliminaries (itemised Σ or % of works)
        public double Overhead;     // main-contractor OH&P
        public double Contingency;  // design/construction contingency
        public double NetExVat;     // Works + Prelims + Overhead + Contingency (Contract Sum ex-VAT)
        public double Vat;          // VAT on NetExVat
        public double GrandTotal;   // NetExVat + Vat (Contract Sum incl VAT) — rounded
    }

    /// <summary>
    /// WP1 — the ONE markup model. Defines each component's base so the panel
    /// KPI, both exporters' Contract Sum, the snapshot list, the drift hash and
    /// the budget-variance write-back all reconcile to a single number.
    ///
    /// Canonical convention (documented):
    ///   1. Works subtotal  W   = Σ line totals
    ///   2. Preliminaries   P   = itemised schedule Σ, or W × prelim%   (base: works)
    ///   3. Overhead+Profit O   = (W + P) × ohp%                        (base: works + prelims)
    ///   4. Contingency     C   = (W + P + O) × cont%                   (base: works + prelims + OH&P)
    ///   5. Net ex-VAT          = W + P + O + C  (Contract Sum exclusive of tax)
    ///   6. VAT             V   = Net × vat%
    ///   7. Contract Sum        = Net + V        (the canonical GrandTotal, incl VAT)
    ///
    /// Contingency is applied *after* prelims and OH&P per standard practice.
    /// Per-element rate-level OH&P (the opt-in ES override loaded rate, i.e. a
    /// subcontractor's already-loaded quote) is a DIFFERENT layer baked into the
    /// net unit rate — it is part of <c>Works</c>, not re-applied here, so the
    /// document OH&P never double-fires against the project markup.
    /// </summary>
    public static class BoqTotals
    {
        public static BoqMarkupBreakdown Compute(double works, double prelimsAbsolute,
            double overheadPct, double contingencyPct, double vatPct)
        {
            var b = new BoqMarkupBreakdown
            {
                Works = works,
                Prelims = prelimsAbsolute
            };
            double sub1 = works + b.Prelims;
            b.Overhead = sub1 * (overheadPct / 100.0);
            double sub2 = sub1 + b.Overhead;
            b.Contingency = sub2 * (contingencyPct / 100.0);
            b.NetExVat = sub2 + b.Contingency;
            b.Vat = b.NetExVat * (vatPct / 100.0);
            b.GrandTotal = Math.Round(b.NetExVat + b.Vat, 0);
            return b;
        }
    }
}
