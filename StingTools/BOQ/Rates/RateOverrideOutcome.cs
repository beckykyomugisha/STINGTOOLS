// ══════════════════════════════════════════════════════════════════════════
//  RateOverrideOutcome.cs — the per-element rate override's outcome, Revit-free
//  (DSCH-33).
//
//  The element rate override lives in Extensible Storage
//  (Core/Storage/StingCostRateOverrideSchema). ES schemas are immutable once
//  registered, so v1 and v2 have no field that can say "priced at nil" or
//  "included in another item" (RateOutcome, DSCH-26). v3 adds two string
//  fields, Outcome and IncludedIn. This file owns:
//
//    * the field encoding   Encode / TryDecode. The stored text is the enum
//                           NAME ("Priced" / "Nil" / "Included"), so it reads
//                           in Revit Lookup and survives a reordering of the enum.
//    * the write rule       CheckWrite: what a writer may store.
//    * the provider answer  Resolve: what the ES rate provider hands the
//                           chain, so RateChainRule decides it like any other.
//
//  v1 and v2 entities carry no outcome; the reader treats them as Priced
//  (all they could ever mean). An undecodable Outcome in a v3 entity is NOT
//  read as Priced: the override is reported unreadable instead, so a corrupt
//  Nil can never come back as a priced rate.
//
//  Tested headlessly in StingTools.Boq.Tests (RateOverrideOutcomeTests).
// ══════════════════════════════════════════════════════════════════════════
using System;

namespace StingTools.BOQ.Rates
{
    /// <summary>What the ES override provider answers for one element.</summary>
    public sealed class RateOverrideAnswer
    {
        public RateOutcome Outcome { get; set; } = RateOutcome.Priced;
        /// <summary>Loaded rate (OH then profit applied) for Priced; 0 for Nil / Included.</summary>
        public double UnitRate { get; set; }
        public string IncludedIn { get; set; } = "";
        /// <summary>Provenance text for a declared outcome ("Nil (ES override)"); empty for Priced.</summary>
        public string OutcomeText { get; set; } = "";
    }

    public static class RateOverrideOutcome
    {
        /// <summary>The text stored in the v3 Outcome field.</summary>
        public static string Encode(RateOutcome outcome)
        {
            switch (outcome)
            {
                case RateOutcome.Nil: return "Nil";
                case RateOutcome.Included: return "Included";
                default: return "Priced";
            }
        }

        /// <summary>
        /// Read the v3 Outcome field. Accepts exactly the three names
        /// <see cref="Encode"/> writes (case-insensitive, trimmed). Anything else,
        /// blank and numbers included, is refused: the caller reports the override
        /// unreadable rather than guessing Priced.
        /// </summary>
        public static bool TryDecode(string stored, out RateOutcome outcome)
        {
            outcome = RateOutcome.Priced;
            string s = (stored ?? "").Trim();
            if (s.Equals("Priced", StringComparison.OrdinalIgnoreCase)) { outcome = RateOutcome.Priced; return true; }
            if (s.Equals("Nil", StringComparison.OrdinalIgnoreCase)) { outcome = RateOutcome.Nil; return true; }
            if (s.Equals("Included", StringComparison.OrdinalIgnoreCase)) { outcome = RateOutcome.Included; return true; }
            return false;
        }

        /// <summary>
        /// What a writer may store. Null when the write is acceptable, else why it
        /// is refused:
        ///   * a negative or non-finite rate is not a rate;
        ///   * Nil / Included with a non-zero rate is two contradictory answers;
        ///   * IncludedIn on anything but Included is a stray reference.
        /// A Priced 0 is allowed: it is what a v1 / v2 entity can already hold and
        /// what the v1 migration copies. The chain treats it as an undeclared zero
        /// and says so (RateChainRule).
        /// </summary>
        public static string CheckWrite(double rate, RateOutcome outcome, string includedIn)
        {
            if (double.IsNaN(rate) || double.IsInfinity(rate) || rate < 0)
                return $"rate {rate} is not a rate";
            if (outcome != RateOutcome.Priced && rate != 0)
                return $"a {Encode(outcome)} override cannot also carry a rate ({rate})";
            if (outcome != RateOutcome.Included && !string.IsNullOrWhiteSpace(includedIn))
                return $"IncludedIn '{includedIn.Trim()}' is only meaningful on an Included override";
            return null;
        }

        /// <summary>
        /// The provider's answer for a stored override. A declared Nil / Included
        /// carries no money: rate 0, no overhead or profit, and
        /// <see cref="RateChainRule"/> accepts it and stops the chain. A Priced
        /// override is loaded with OH then profit (waste is quantity-side and is
        /// NOT applied here; see WasteFactor). A Priced 0 comes back as 0 so the
        /// chain logs an undeclared zero and asks the next provider.
        /// </summary>
        public static RateOverrideAnswer Resolve(double rate, RateOutcome outcome, string includedIn,
            double overheadPercent, double profitPercent)
        {
            if (outcome != RateOutcome.Priced)
            {
                return new RateOverrideAnswer
                {
                    Outcome = outcome,
                    UnitRate = 0,
                    IncludedIn = outcome == RateOutcome.Included ? (includedIn ?? "").Trim() : "",
                    OutcomeText = RateOutcomeToken.BillRateText(outcome, includedIn) + " (ES override)",
                };
            }

            double loaded = rate > 0 ? rate : 0;
            if (loaded > 0 && overheadPercent > 0) loaded *= 1.0 + overheadPercent / 100.0;
            if (loaded > 0 && profitPercent > 0) loaded *= 1.0 + profitPercent / 100.0;
            return new RateOverrideAnswer { Outcome = RateOutcome.Priced, UnitRate = loaded };
        }
    }
}
