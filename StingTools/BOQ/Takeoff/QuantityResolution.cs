// QuantityResolution.cs — G-27. Did we MEASURE this, or did we GUESS it?
//
// This is CompoundTakeoffBuilder's RC-1 `Resolution` vocabulary, promoted so the
// FORMULA side can record into the SAME structure. It is deliberately not a second
// flagging scheme: two take-off paths keeping two independent notions of "did this
// resolve" is the shape that produced G-15 in the first place.
//
// The C# side already had two of the three states (Empty, Unmatched). The third —
// Unresolved — existed as behaviour on the formula side (G-5: no row at all means
// the formula is skipped, never written as 0) but was never recorded as a state.

using System;
using System.Collections.Generic;

namespace StingTools.BOQ.Takeoff
{
    /// <summary>How a single ratio lookup resolved.</summary>
    public enum LookupState
    {
        /// <summary>Key matched a specific row. The number is measured.</summary>
        Measured = 0,

        /// <summary>
        /// Key was absent or did not match, so the table's DEFAULT row was used.
        /// NOT an error — a DEFAULT row is a legitimate estimating assumption — but
        /// it is an assumption, and until now it was indistinguishable from a
        /// measurement on the page.
        /// </summary>
        Defaulted = 1,

        /// <summary>No row at all. Nothing can be produced; the quantity is skipped.</summary>
        Unresolved = 2,
    }

    /// <summary>One recorded lookup, kept so a QS can see which input was assumed.</summary>
    public sealed class LookupTrace
    {
        public string Table = "";
        public string KeyParam = "";
        public string KeyValue = "";
        public string Column = "";
        public LookupState State = LookupState.Measured;

        public override string ToString() =>
            $"{Table}.{Column} [{KeyParam}={(string.IsNullOrEmpty(KeyValue) ? "<empty>" : KeyValue)}] → {State}";
    }

    /// <summary>
    /// Ratio-resolution tracking, shared by the C# take-off and the formula
    /// evaluator. Carries RC-1's original two lists unchanged so existing
    /// behaviour (confidence floor, note text) is preserved exactly.
    /// </summary>
    public sealed class QuantityResolution
    {
        /// <summary>Param unset → project default. RC-1's original list.</summary>
        public readonly List<string> Empty = new List<string>();

        /// <summary>Value set but not in the table (typo). RC-1's original list.</summary>
        public readonly List<string> Unmatched = new List<string>();

        /// <summary>No row at all — the quantity could not be produced.</summary>
        public readonly List<string> Unresolved = new List<string>();

        /// <summary>Every lookup, in order, for the audit surface.</summary>
        public readonly List<LookupTrace> Traces = new List<LookupTrace>();

        public bool Any => Empty.Count > 0 || Unmatched.Count > 0 || Unresolved.Count > 0;

        /// <summary>
        /// RC-1's original floor, unchanged for the two states it knew about.
        /// Unresolved is worse than either: no number exists at all.
        /// </summary>
        public int ConfidenceFloor =>
            Unresolved.Count > 0 ? 0
          : Unmatched.Count > 0 ? 35
          : Empty.Count > 0 ? 55
          : 100;

        /// <summary>
        /// The single word a QS needs on the row: was this measured, assumed, or
        /// not produced at all?
        /// </summary>
        public LookupState Worst =>
            Unresolved.Count > 0 ? LookupState.Unresolved
          : (Unmatched.Count > 0 || Empty.Count > 0) ? LookupState.Defaulted
          : LookupState.Measured;

        public void Record(string table, string keyParam, string keyValue, string column, LookupState state)
        {
            Traces.Add(new LookupTrace
            {
                Table = table ?? "", KeyParam = keyParam ?? "",
                KeyValue = keyValue ?? "", Column = column ?? "", State = state
            });

            string label = $"{table}.{column} ({keyParam})";
            if (state == LookupState.Unresolved) { if (!Unresolved.Contains(label)) Unresolved.Add(label); return; }
            if (state != LookupState.Defaulted) return;

            // Distinguish the two ways of defaulting, exactly as RC-1 did: an empty
            // parameter is an omission, a set-but-unmatched value is probably a typo
            // and is the more dangerous of the two.
            if (string.IsNullOrWhiteSpace(keyValue))
            { if (!Empty.Contains(label)) Empty.Add(label); }
            else
            { if (!Unmatched.Contains(label)) Unmatched.Add(label); }
        }

        /// <summary>RC-1's note text, extended with the third state.</summary>
        public string Note()
        {
            var parts = new List<string>();
            if (Unresolved.Count > 0) parts.Add("NOT MEASURED (no table row): " + string.Join(", ", Unresolved));
            if (Unmatched.Count > 0) parts.Add("UNMATCHED→DEFAULT (check value): " + string.Join(", ", Unmatched));
            if (Empty.Count > 0) parts.Add("param empty→project default: " + string.Join(", ", Empty));
            return parts.Count > 0 ? "[Ratio: " + string.Join("; ", parts) + "]" : "";
        }

        /// <summary>Short column value for the BOQ export.</summary>
        public string ExportFlag()
        {
            switch (Worst)
            {
                case LookupState.Unresolved: return "NOT MEASURED";
                case LookupState.Defaulted:  return "ASSUMED (default)";
                default:                     return "measured";
            }
        }
    }
}
