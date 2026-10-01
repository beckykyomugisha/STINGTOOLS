// ══════════════════════════════════════════════════════════════════════════
//  BoqRowSource.cs — what kind of bill line a row is, and how each kind is
//  labelled, synced and presented. Revit-free (moved out of BOQModels.cs for
//  DSCH-44 so the vocabulary is tested in StingTools.Boq.Tests).
//
//  A provisional sum and a prime cost sum are different things and a bill must
//  not blur them:
//    Provisional sum   work that cannot be fully described or measured at
//                      tender; Defined or Undefined (NRM2 2.9.1 — see
//                      ProvisionalSumType).
//    PC (prime cost)   an allowance for goods or services from a named supplier
//                      or register, already priced; the contractor's profit and
//                      attendance are added in the bill.
// ══════════════════════════════════════════════════════════════════════════
namespace StingTools.BOQ
{
    public enum BOQRowSource
    {
        Model,
        Manual,
        ProvisionalSum,
        Dayworks,       // P3.1 — daywork / time-and-material rows
        PCSum          // P3.1 — prime-cost sum (named supplier allowance)
    }

    /// <summary>P3.1 — shared source label / parse helpers so the export label,
    /// import parser and panel agree on one spelling per source.</summary>
    public static class BoqSourceUtil
    {
        public static string Label(BOQRowSource s)
        {
            switch (s)
            {
                case BOQRowSource.Manual:         return "Manual";
                case BOQRowSource.ProvisionalSum: return "Provisional Sum";
                case BOQRowSource.Dayworks:       return "Dayworks";
                case BOQRowSource.PCSum:          return "PC Sum";
                default:                          return "Model";
            }
        }

        /// <summary>Parse a source label (case-insensitive, substring-tolerant).
        /// Returns Model for unrecognised input.</summary>
        public static BOQRowSource Parse(string label)
        {
            string l = (label ?? "").Trim().ToLowerInvariant();
            // "PS" is what the panel's add-row prompt offers; it used to fall
            // through to Model (then Manual), so a typed PS never became one.
            if (l.Contains("provisional") || l == "ps") return BOQRowSource.ProvisionalSum;
            if (l.Contains("daywork"))     return BOQRowSource.Dayworks;
            if (l.Contains("pc") || l.Contains("prime cost")) return BOQRowSource.PCSum;
            if (l.Contains("manual"))      return BOQRowSource.Manual;
            return BOQRowSource.Model;
        }

        /// <summary>True for QS-authored rows that must never be overwritten by
        /// a model re-takeoff (everything except Model).</summary>
        public static bool IsQsAuthored(BOQRowSource s) => s != BOQRowSource.Model;

        /// <summary>
        /// DSCH-44 — the QuantityLine.LineKind sent to Planscape Server. PC sums and
        /// dayworks used to go as "Measured", so the server could not tell them from
        /// measured work. The values match the server entity's vocabulary; a
        /// provisional sum stays "ProvisionalSum", the value already stored on
        /// synced baselines.
        /// </summary>
        public static string SyncLineKind(BOQRowSource s)
        {
            switch (s)
            {
                case BOQRowSource.ProvisionalSum: return "ProvisionalSum";
                case BOQRowSource.PCSum:          return "PcSum";
                case BOQRowSource.Dayworks:       return "Daywork";
                case BOQRowSource.Manual:         return "Manual";
                default:                          return "Measured";
            }
        }

        /// <summary>
        /// DSCH-44 — the prefix a priced bill puts on a sum's description, so a PC
        /// sum is never read as a provisional sum or as measured work. Null for
        /// lines that carry no prefix.
        /// </summary>
        public static string BillPrefix(BOQRowSource s, ProvisionalSumType psType)
        {
            switch (s)
            {
                case BOQRowSource.ProvisionalSum: return $"PROVISIONAL SUM ({ProvisionalSumTypes.Marker(psType)}): ";
                case BOQRowSource.PCSum:          return "PRIME COST SUM: ";
                default:                          return null;
            }
        }
    }
}
