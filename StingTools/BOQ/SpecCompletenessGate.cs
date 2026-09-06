// ══════════════════════════════════════════════════════════════════════════
//  SpecCompletenessGate.cs — Phase H2 (KUT lifecycle, max automation).
//
//  Pure, host-free tender gate: counts priced BOQ lines that carry no CSI/spec
//  reference (PRICED_UNSPECIFIED — money is in the bill for something the spec
//  doesn't describe). A tender that goes out with PRICED_UNSPECIFIED > 0 invites
//  scope disputes; the professional (tender) export consults this before writing
//  and warns / blocks per the COST_REQUIRE_SPEC_FOR_TENDER config.
//
//  Mirrors the SpecLink_Reconcile Phase-B PRICED_UNSPECIFIED definition so the
//  gate and the reconciliation report agree. Operates on the pure BOQDocument
//  (no Autodesk.Revit.*), so it is unit-tested in StingTools.Boq.Tests.
// ══════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;
using System.Linq;

namespace StingTools.BOQ
{
    public sealed class SpecGateResult
    {
        public int PricedUnspecifiedCount;
        public double PricedUnspecifiedValueUGX;
        public int PricedTotalCount;
        public double PricedTotalValueUGX;

        /// <summary>Share of priced value with no spec reference (0..1).</summary>
        public double UnspecifiedValueFraction =>
            PricedTotalValueUGX > 0 ? PricedUnspecifiedValueUGX / PricedTotalValueUGX : 0.0;

        public bool Passes => PricedUnspecifiedCount == 0;
    }

    public static class SpecCompletenessGate
    {
        /// <summary>A line is "priced" when it is a modelled measured/priced row with a
        /// positive value. Provisional sums + owner-procured FF&amp;E are intentionally
        /// excluded — a PC sum is unspecified BY DESIGN, not a gap to chase.</summary>
        private static bool IsPriced(BOQLineItem li) =>
            li != null
            && li.Source == BOQRowSource.Model
            && !li.FfeOwnerProcured
            && li.TotalUGX > 0;

        private static bool IsSpecified(BOQLineItem li) =>
            !string.IsNullOrWhiteSpace(li?.CsiSection);

        public static SpecGateResult Evaluate(IEnumerable<BOQLineItem> lines)
        {
            var r = new SpecGateResult();
            foreach (var li in (lines ?? Enumerable.Empty<BOQLineItem>()).Where(IsPriced))
            {
                r.PricedTotalCount++;
                r.PricedTotalValueUGX += li.TotalUGX;
                if (!IsSpecified(li))
                {
                    r.PricedUnspecifiedCount++;
                    r.PricedUnspecifiedValueUGX += li.TotalUGX;
                }
            }
            return r;
        }

        public static SpecGateResult Evaluate(BOQDocument doc) =>
            Evaluate(doc?.AllItems);

        /// <summary>The first N priced-unspecified lines (highest value first) for the
        /// gate dialog — enough to point the QS at what to spec.</summary>
        public static List<BOQLineItem> TopUnspecified(BOQDocument doc, int n = 10) =>
            (doc?.AllItems ?? new List<BOQLineItem>())
            .Where(li => IsPriced(li) && !IsSpecified(li))
            .OrderByDescending(li => li.TotalUGX)
            .Take(n)
            .ToList();
    }
}
