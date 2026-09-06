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
    ///
    /// Per-element rate-level OH&P (the opt-in ES override loaded rate, i.e. a
    /// subcontractor's already-loaded quote) is a DIFFERENT layer, baked into the net
    /// unit rate. It is part of <c>Works</c> — and therefore part of the OH&amp;P base
    /// unless it is declared, which is what <paramref name="ohpLoadedWorks"/> is for.
    /// This docstring previously claimed the document OH&amp;P "never double-fires"
    /// against such a line; nothing implemented that, and step 3 above marks up the
    /// whole works subtotal including the loaded portion. Declaring the loaded Σ is now
    /// the mechanism that makes the claim true (ROADMAP LIFE-3).
    /// </summary>
    public static class BoqTotals
    {
        /// <param name="markupExemptWorks">
        /// The portion of <paramref name="works"/> a main contractor does not earn
        /// overhead, profit or contingency on — Owner-procured FF&amp;E bought direct
        /// from the supplier's register. It stays in the bill at cost (it is real money
        /// the Owner spends) but is removed from the OH&amp;P and contingency BASE, which
        /// is the arithmetic that makes an at-cost FF&amp;E line different from a
        /// contractor-supplied one. Preliminaries keep the full works base: site
        /// establishment, storage and handling are earned on Owner-supplied goods too.
        /// <para>DEFAULTS TO 0, which reproduces the previous arithmetic exactly.</para>
        /// </param>
        /// <param name="ohpLoadedWorks">
        /// The portion of <paramref name="works"/> whose unit rate ALREADY carries the
        /// contractor's overhead and profit — a stamped rate override from a
        /// subcontractor's loaded quote. The document OH&amp;P percentage must not fire
        /// against it a second time.
        /// <para>Distinct from <paramref name="markupExemptWorks"/>, and deliberately not
        /// folded into it: Owner-procured FF&amp;E leaves the OH&amp;P base AND the
        /// contingency base, because the contractor neither earns margin nor carries risk
        /// on goods the Owner buys. A loaded rate leaves the OH&amp;P base ONLY — the work
        /// is still contractor-executed and still carries design/construction risk, so it
        /// stays in the contingency base. Merging the two parameters would silently stop
        /// charging contingency on subcontracted work.</para>
        /// <para>DEFAULTS TO 0, which reproduces the previous arithmetic exactly.</para>
        /// </param>
        public static BoqMarkupBreakdown Compute(double works, double prelimsAbsolute,
            double overheadPct, double contingencyPct, double vatPct,
            double markupExemptWorks = 0, double ohpLoadedWorks = 0)
        {
            var b = new BoqMarkupBreakdown
            {
                Works = works,
                Prelims = prelimsAbsolute
            };
            // Never let a bad exempt figure invert the base. The two exemptions are
            // clamped together as well as individually: a line can be both Owner-procured
            // and loaded, and double-subtracting it would drive the base negative.
            double exempt = Math.Max(0, Math.Min(markupExemptWorks, works));
            double loaded = Math.Max(0, Math.Min(ohpLoadedWorks, works - exempt));
            double sub1 = works + b.Prelims;
            double ohpBase = sub1 - exempt - loaded;
            b.Overhead = ohpBase * (overheadPct / 100.0);
            // Contingency base excludes the FF&E only -- the loaded lines are added back,
            // because risk is carried on them even though margin is not earned twice.
            double contBase = ohpBase + loaded + b.Overhead;
            b.Contingency = contBase * (contingencyPct / 100.0);
            b.NetExVat = sub1 + b.Overhead + b.Contingency;
            b.Vat = b.NetExVat * (vatPct / 100.0);
            b.GrandTotal = Math.Round(b.NetExVat + b.Vat, 0);
            return b;
        }
    }
}
