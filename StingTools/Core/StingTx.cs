// StingTx — commit a Revit transaction and never report work Revit undid (ROADMAP ELEC-32).
//
//   StingTx.Commit(tx, "what")        a step whose success report follows the commit: throws
//                                      TransactionRolledBackException on a rollback, so the
//                                      report is never reached; every dispatcher (and the
//                                      workflow engine) shows the exception's message.
//   StingTx.TryCommit(tx, "what", out reason)
//                                      a per-item transaction inside a loop: returns false and
//                                      the reason, so the caller counts THAT item as failed and
//                                      carries on — a rolled-back item must not stop the run.
//   StingTx.RollBackIfOpen(tx)        for catch blocks: a catch after a failed commit must not
//                                      throw "transaction not started" over the real error.
//
// The reason comes from TxFailureLog (Revit's error text for this transaction name).
// tools/check_unchecked_commits.py fails CI on any Transaction.Commit() whose status is
// discarded outside this file.

using System;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    /// <summary>A transaction Revit did not commit. Its message is the user-facing report.</summary>
    public sealed class TransactionRolledBackException : Exception
    {
        public TransactionRolledBackException(string message) : base(message) { }
    }

    public static class StingTx
    {
        /// <summary>Commit, or throw <see cref="TransactionRolledBackException"/> naming <paramref name="what"/>.</summary>
        public static void Commit(Transaction tx, string what = null)
        {
            if (!TryCommit(tx, what, out string reason))
                throw new TransactionRolledBackException(reason);
        }

        /// <summary>Commit; false (with the ROLLED BACK message) when Revit did not commit.</summary>
        public static bool TryCommit(Transaction tx, string what, out string reason)
        {
            reason = null;
            string name = SafeName(tx);
            TransactionStatus st = tx.Commit();
            var revitSaid = TxFailureLog.Take(name);
            if (st == TransactionStatus.Committed) return true;
            reason = TxReport.RolledBack(string.IsNullOrWhiteSpace(what) ? name : what, st.ToString(), revitSaid);
            StingLog.Warn(reason);
            return false;
        }

        /// <summary>Roll back only a transaction that is still open.</summary>
        public static void RollBackIfOpen(Transaction tx)
        {
            try { if (tx != null && tx.HasStarted() && !tx.HasEnded()) tx.RollBack(); }
            catch (Exception ex) { StingLog.Warn($"Roll back '{SafeName(tx)}': {ex.Message}"); }
        }

        private static string SafeName(Transaction tx)
        {
            try { return tx?.GetName() ?? ""; } catch { return ""; }
        }

        /// <summary>
        /// The FailuresProcessing listener StingToolsApp registers: records each failure's
        /// description under the transaction name. Read-only — it never resolves, deletes or
        /// changes the outcome, so Revit's own failure handling is untouched.
        /// </summary>
        public static void RecordFailures(object sender, Autodesk.Revit.DB.Events.FailuresProcessingEventArgs e)
        {
            try
            {
                var fa = e?.GetFailuresAccessor();
                if (fa == null) return;
                var msgs = new System.Collections.Generic.List<string>();
                foreach (FailureMessageAccessor f in fa.GetFailureMessages())
                    if (f.GetSeverity() != FailureSeverity.Warning) msgs.Add(f.GetDescriptionText());
                if (msgs.Count > 0) TxFailureLog.Record(fa.GetTransactionName(), msgs);
            }
            catch (Exception ex) { StingLog.Info($"TxFailureLog: {ex.Message}"); }
        }
    }
}
