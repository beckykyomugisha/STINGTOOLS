// ElecTx — commit a transaction or stop the command (ROADMAP ELEC-28).
//
// Transaction.Commit() returns RolledBack when a failure handler or the user cancelling
// Revit's error dialog undoes the transaction. Seventy-seven electrical commands ignored
// the status and went on to report "Placed 12 / Stamped 40 / Created …" for values that
// were not in the model. Commit() throws instead, so the code after it — the success
// report — is never reached; the dispatchers (StingCommandHandler, the Electrical
// handler, the workflow engine) show the exception's message, which says ROLLED BACK.

using System;
using Autodesk.Revit.DB;

namespace StingTools.Core.Electrical
{
    /// <summary>A transaction Revit did not commit. Its message is the user-facing report.</summary>
    public sealed class TransactionRolledBackException : Exception
    {
        public TransactionRolledBackException(string message) : base(message) { }
    }

    internal static class ElecTx
    {
        /// <summary>Commit, or throw <see cref="TransactionRolledBackException"/> naming <paramref name="what"/>.</summary>
        public static void Commit(Transaction tx, string what)
        {
            TransactionStatus st = tx.Commit();
            if (st != TransactionStatus.Committed)
            {
                string msg = ElecWriteReport.RolledBack(what ?? tx.GetName(), st.ToString());
                StingLog.Warn(msg);
                throw new TransactionRolledBackException(msg);
            }
        }

        /// <summary>Roll back only a transaction still open — a catch after a failed Commit
        /// must not throw "transaction not started" over the real error.</summary>
        public static void RollBackIfOpen(Transaction tx)
        {
            try { if (tx != null && tx.HasStarted() && !tx.HasEnded()) tx.RollBack(); }
            catch (Exception ex) { StingLog.Warn($"Roll back '{tx?.GetName()}': {ex.Message}"); }
        }
    }
}
