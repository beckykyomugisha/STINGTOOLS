// TxReport — what a command says when Revit rolled its transaction back (ROADMAP ELEC-32).
// Revit-free: StingTools.Tags.Tests compiles this file.
//
// Transaction.Commit() returns RolledBack when a failure handler or the user cancelling
// Revit's error dialog undoes the work. Across the plugin ~850 commits ignored that and
// went on to report "Tagged 1,204 / Created 12 sheets" for work that was not in the model.
// StingTx.Commit throws with this message instead; StingTx.TryCommit returns it.
//
// TxFailureLog keeps the error text Revit raised for each transaction name (recorded by a
// passive FailuresProcessing listener), so the message can say WHY, not only that it failed.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core
{
    public static class TxReport
    {
        /// <summary>The user-facing message for a step Revit rolled back.</summary>
        public static string RolledBack(string what, string status, IEnumerable<string> reasons = null)
        {
            string st = string.IsNullOrWhiteSpace(status) ? "not committed" : status.Trim();
            string w = string.IsNullOrWhiteSpace(what) ? "the change" : what.Trim();
            var why = (reasons ?? Enumerable.Empty<string>())
                .Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct().Take(5).ToList();
            return $"ROLLED BACK ({st}): {w} — Revit undid it; nothing from this step was kept in the model. "
                 + "Any counts it would have reported are void."
                 + (why.Count > 0 ? " Revit reported: " + string.Join(" | ", why) : " Revit's own warning (if shown) says why.");
        }
    }

    /// <summary>
    /// Error text Revit raised, per transaction name, during the current command. Filled by
    /// StingToolsApp's FailuresProcessing listener (read-only: it never resolves or deletes a
    /// failure, so Revit's own handling is unchanged); read by StingTx when a commit fails.
    /// </summary>
    public static class TxFailureLog
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, List<string>> ByName =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        public static void Record(string transactionName, IEnumerable<string> messages)
        {
            if (messages == null) return;
            var list = messages.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
            if (list.Count == 0) return;
            lock (Gate)
            {
                string key = transactionName ?? "";
                if (!ByName.TryGetValue(key, out var l)) ByName[key] = l = new List<string>();
                l.AddRange(list);
                if (l.Count > 20) l.RemoveRange(0, l.Count - 20);
            }
        }

        /// <summary>The messages recorded for <paramref name="transactionName"/>, removed as read.</summary>
        public static List<string> Take(string transactionName)
        {
            lock (Gate)
            {
                string key = transactionName ?? "";
                if (!ByName.TryGetValue(key, out var l)) return new List<string>();
                ByName.Remove(key);
                return l;
            }
        }
    }
}
