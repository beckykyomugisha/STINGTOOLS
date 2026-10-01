// ══════════════════════════════════════════════════════════════════════════
//  ProvisionalSumType.cs — defined vs undefined provisional sums (DSCH-35).
//
//  NRM2 (RICS New Rules of Measurement 2, 2012) paragraph 2.9.1 defines two kinds of
//  provisional sum, and the JCT Standard Building Contract With Quantities
//  2016 adopts the same definitions:
//
//    Defined    the work is not completely designed, but its nature and
//               construction, how and where it is fixed, and quantities
//               indicating its scope can be given. The contractor is DEEMED
//               to have made due allowance in programming, planning and
//               pricing preliminaries.
//    Undefined  that information cannot be given. The contractor is NOT
//               deemed to have allowed for it; the employer carries the
//               programme and preliminaries risk.
//
//  Which one a sum is decides whether a contractor may claim time and
//  preliminaries when it is instructed, so a bill must say. A provisional sum
//  with no declaration is NOT defaulted to either: it is Undeclared, shown as
//  "NOT DECLARED" in the bill, and flagged by validation.
//
//  VERIFY: the sub-paragraph numbers 2.9.1.1 (defined) and 2.9.1.2 (undefined)
//  are as commonly cited; confirm against the licensed NRM2 text before they
//  are quoted in a contract preamble. The definitions themselves are
//  confirmed (NRM2 2.9.1, as adopted by JCT SBC/Q 2016).
//
//  Revit-free; tested in StingTools.Boq.Tests (ProvisionalSumTypeTests).
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.BOQ
{
    public enum ProvisionalSumType
    {
        /// <summary>Nobody has said. Never treated as either kind.</summary>
        Undeclared = 0,
        Defined = 1,
        Undefined = 2,
    }

    public static class ProvisionalSumTypes
    {
        /// <summary>Bill marker for a provisional sum whose type was never declared.</summary>
        public const string NotDeclaredMarker = "NOT DECLARED";

        /// <summary>
        /// Read a declaration (parameter text, panel input, JSON). Accepts
        /// "Defined" / "Undefined" and the single letters "D" / "U",
        /// case-insensitive, surrounding space ignored. Anything else, blank
        /// included, is refused — the caller keeps Undeclared and says so.
        /// </summary>
        public static bool TryParse(string raw, out ProvisionalSumType type)
        {
            type = ProvisionalSumType.Undeclared;
            string s = (raw ?? "").Trim();
            if (s.Equals("Defined", StringComparison.OrdinalIgnoreCase) || s.Equals("D", StringComparison.OrdinalIgnoreCase))
            { type = ProvisionalSumType.Defined; return true; }
            if (s.Equals("Undefined", StringComparison.OrdinalIgnoreCase) || s.Equals("U", StringComparison.OrdinalIgnoreCase))
            { type = ProvisionalSumType.Undefined; return true; }
            return false;
        }

        /// <summary>The text written to CST_PS_TYPE_TXT and shown in a bill:
        /// "Defined" / "Undefined", or "NOT DECLARED".</summary>
        public static string Marker(ProvisionalSumType type)
        {
            switch (type)
            {
                case ProvisionalSumType.Defined: return "Defined";
                case ProvisionalSumType.Undefined: return "Undefined";
                default: return NotDeclaredMarker;
            }
        }

        /// <summary>
        /// The validation finding for one provisional-sum row, or null when it is
        /// declared. <paramref name="label"/> names the row (line ref or item name).
        /// </summary>
        public static string Finding(ProvisionalSumType type, string label)
        {
            if (type != ProvisionalSumType.Undeclared) return null;
            string who = string.IsNullOrWhiteSpace(label) ? "A provisional sum" : $"Provisional sum '{label.Trim()}'";
            return $"{who} is not declared Defined or Undefined (NRM2 2.9.1) — "
                 + "this decides whether the contractor is deemed to have allowed for programming, planning and preliminaries.";
        }

        /// <summary>
        /// Message for a declaration that was present but unreadable (e.g. a
        /// parameter holding "Def."), so the row is kept Undeclared visibly.
        /// </summary>
        public static string UnreadableNote(string raw)
            => $"PS type '{(raw ?? "").Trim()}' is not Defined / Undefined — treated as NOT DECLARED";

        /// <summary>Counts of provisional sums by declaration.</summary>
        public static (int defined, int undefined, int undeclared) Count(IEnumerable<ProvisionalSumType> types)
        {
            int d = 0, u = 0, n = 0;
            if (types != null)
                foreach (var t in types)
                {
                    if (t == ProvisionalSumType.Defined) d++;
                    else if (t == ProvisionalSumType.Undefined) u++;
                    else n++;
                }
            return (d, u, n);
        }

        /// <summary>
        /// Preamble clauses for a priced bill. Always states both definitions;
        /// adds a warning clause when any sum is undeclared, so an issued bill
        /// cannot read as if every sum had been classified.
        /// </summary>
        public static IReadOnlyList<string> PreambleClauses(int undeclaredCount)
        {
            var list = new List<string>
            {
                "Provisional Sums are marked \"Defined\" or \"Undefined\" in accordance with NRM2 paragraph 2.9.1.",
                "Defined Provisional Sums: the nature and construction of the work, how and where it is fixed and quantities "
                + "indicating its scope are stated. The Contractor shall be deemed to have made due allowance in programming, "
                + "planning and pricing Preliminaries for this work.",
                "Undefined Provisional Sums: that information cannot be given at the time of tender. The Contractor shall not be "
                + "deemed to have made any allowance in programming, planning or pricing Preliminaries for this work.",
            };
            if (undeclaredCount > 0)
                list.Add($"{undeclaredCount} Provisional Sum(s) are marked \"{NotDeclaredMarker}\": their type has not been decided "
                         + "and must be declared before this Bill is issued for tender.");
            return list;
        }
    }
}
