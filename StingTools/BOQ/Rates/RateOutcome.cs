// ══════════════════════════════════════════════════════════════════════════
//  RateOutcome.cs — what a rate lookup DECIDED, beyond the number (DSCH-26).
//
//  Before this, `UnitRate <= 0` meant "no rate" everywhere in the chain, so an
//  item a QS had deliberately priced at nil fell through to the next provider
//  and came back priced off a category average or the USD default baseline.
//
//  Four states a bill line can be in. Only the first three are RATE outcomes:
//
//    Priced    rate > 0. The ordinary case.
//    Nil       measured, and deliberately costs nothing. Bill: "Nil", amount "–".
//    Included  the cost is carried by another item. Bill: "Incl." or
//              "Incl. in <ref>", amount "–".
//    (not priced)  nobody priced it. Not a RateOutcome: the lookup is null, the
//              chain moves on, and an unmatched line is flagged at risk. It is
//              never a silent 0.
//
//  A provisional sum is not a rate at all; it stays a row source
//  (BOQRowSource.ProvisionalSum) — NRM2 2.9 treats it as a sum, defined or
//  undefined, not as quantity x rate.
//
//  A deliberate zero has to be DECLARED. In a rate file the declaration is a
//  token in the rate cell itself — NIL, INCL, or INCL:<ref> — because that is
//  how a QS writes it in a priced bill. A bare 0 is NOT nil: it is logged and
//  treated as not priced, which is what it meant before this change, now said
//  out loud.
//
//  Revit-free so the token grammar and the chain's accept/skip rule are tested
//  headlessly (StingTools.Boq.Tests).
// ══════════════════════════════════════════════════════════════════════════
using System;

namespace StingTools.BOQ.Rates
{
    public enum RateOutcome
    {
        Priced = 0,
        Nil = 1,
        Included = 2,
    }

    public static class RateOutcomeToken
    {
        /// <summary>
        /// Read a declaration from a rate cell or a stamped rate parameter.
        /// Accepts (case-insensitive, surrounding space ignored):
        ///   NIL
        ///   INCL | INCLUDED | INCL. — optionally followed by ':' and a reference
        ///   ("INCL:E10/2", "INCLUDED: item 14.3.2").
        /// Returns false for anything else, including numbers and blanks — those
        /// are rates (or nothing), not declarations.
        /// </summary>
        public static bool TryParse(string raw, out RateOutcome outcome, out string includedIn)
        {
            outcome = RateOutcome.Priced;
            includedIn = "";
            string s = (raw ?? "").Trim();
            if (s.Length == 0) return false;

            string head = s, tail = "";
            int colon = s.IndexOf(':');
            if (colon >= 0) { head = s.Substring(0, colon).Trim(); tail = s.Substring(colon + 1).Trim(); }
            head = head.TrimEnd('.').Trim();

            if (head.Equals("NIL", StringComparison.OrdinalIgnoreCase))
            {
                if (colon >= 0) return false;   // "NIL:x" is not a form anyone writes; refuse rather than guess
                outcome = RateOutcome.Nil;
                return true;
            }
            if (head.Equals("INCL", StringComparison.OrdinalIgnoreCase)
                || head.Equals("INCLUDED", StringComparison.OrdinalIgnoreCase))
            {
                outcome = RateOutcome.Included;
                includedIn = tail;
                return true;
            }
            return false;
        }

        /// <summary>The token that round-trips through <see cref="TryParse"/>.
        /// Null for <see cref="RateOutcome.Priced"/> — a priced rate is written as a number.</summary>
        public static string ToToken(RateOutcome outcome, string includedIn)
        {
            switch (outcome)
            {
                case RateOutcome.Nil: return "NIL";
                case RateOutcome.Included:
                    return string.IsNullOrWhiteSpace(includedIn) ? "INCL" : "INCL:" + includedIn.Trim();
                default: return null;
            }
        }

        /// <summary>What the rate column of a priced bill shows. Null for Priced.</summary>
        public static string BillRateText(RateOutcome outcome, string includedIn)
        {
            switch (outcome)
            {
                case RateOutcome.Nil: return "Nil";
                case RateOutcome.Included:
                    return string.IsNullOrWhiteSpace(includedIn) ? "Incl." : "Incl. in " + includedIn.Trim();
                default: return null;
            }
        }

        /// <summary>
        /// Text for a rate parameter stamped on an element: the declaration token
        /// when the outcome is declared, else the UGX rate as an invariant integer.
        /// </summary>
        public static string StampText(RateOutcome outcome, string includedIn, double rateUgx)
            => ToToken(outcome, includedIn)
               ?? rateUgx.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>What the provider chain does with one provider's answer.</summary>
    public enum RateChainStep
    {
        /// <summary>No answer, or an undeclared zero — ask the next provider.</summary>
        Continue,
        /// <summary>An undeclared zero: ask the next provider, and say so.</summary>
        ContinueUndeclaredZero,
        /// <summary>A priced rate, or a declared Nil / Included — stop here.</summary>
        Accept,
    }

    public static class RateChainRule
    {
        /// <summary>
        /// The one rule every caller of the chain follows. A declared Nil or
        /// Included is an answer and stops the chain; a zero without a declaration
        /// is not, and the next provider is asked.
        /// </summary>
        public static RateChainStep Decide(bool hasAnswer, RateOutcome outcome, double unitRate)
        {
            if (!hasAnswer) return RateChainStep.Continue;
            if (outcome != RateOutcome.Priced) return RateChainStep.Accept;
            if (unitRate > 0) return RateChainStep.Accept;
            return RateChainStep.ContinueUndeclaredZero;
        }

        /// <summary>True when a resolved line carries a decided price: a positive
        /// rate or a declared Nil / Included. False is "not priced".</summary>
        public static bool IsDecided(RateOutcome outcome, double unitRate)
            => outcome != RateOutcome.Priced || unitRate > 0;
    }
}
