// ══════════════════════════════════════════════════════════════════════════
//  MoneyRound.cs — one money-rounding convention. PM-1 shared helper.
//
//  The audit's central finding is divergence from duplicated logic. Money totals
//  across snapshot-list, KPIs, certs and EVM must reconcile to the shilling, so
//  every money rounding routes through here.
//
//  Convention: banker's rounding (round-half-to-even), the accounting default —
//  it removes the systematic upward bias of away-from-zero rounding across a bill
//  of thousands of lines.
//
//  Scoped decision (recorded in CHANGELOG): money stays `double` for now because
//  the persisted snapshot / cert / EVM JSON is double-typed and a `decimal`
//  migration would change that serialization contract; instead all rounding is
//  funnelled through this half-even helper so UGX-billions × thousands-of-lines
//  float residue can no longer be masked by inconsistent per-call rounding. A
//  `decimal` overload is provided for new cert/EVM money paths that opt in.
//
//  Pure (no Revit / no I/O) — unit-tested in StingTools.Cost.Tests.
// ══════════════════════════════════════════════════════════════════════════
using System;

namespace StingTools.Core
{
    public static class MoneyRound
    {
        /// <summary>Round a money amount to whole currency units (half-even).</summary>
        public static double Round(double value) => Round(value, 0);

        /// <summary>Round a money amount to <paramref name="decimals"/> places (half-even).</summary>
        public static double Round(double value, int decimals)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            return Math.Round(value, decimals, MidpointRounding.ToEven);
        }

        /// <summary>Decimal half-even rounding for opt-in cert / EVM money paths.</summary>
        public static decimal Round(decimal value, int decimals = 2)
            => Math.Round(value, decimals, MidpointRounding.ToEven);
    }
}
