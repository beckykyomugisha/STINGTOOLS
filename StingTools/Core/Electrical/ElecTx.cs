// ElecTx — the electrical module's name for StingTools.Core.StingTx (ROADMAP ELEC-28/32).
//
// ELEC-28 introduced this for the electrical commands; ELEC-32 moved the implementation
// to StingTx so the whole plugin shares one: commit, or throw a
// TransactionRolledBackException whose message says ROLLED BACK and why. Kept so the 84
// electrical call sites need no edit.

using Autodesk.Revit.DB;

namespace StingTools.Core.Electrical
{
    internal static class ElecTx
    {
        /// <summary>See <see cref="StingTx.Commit"/>.</summary>
        public static void Commit(Transaction tx, string what) => StingTx.Commit(tx, what);

        /// <summary>See <see cref="StingTx.RollBackIfOpen"/>.</summary>
        public static void RollBackIfOpen(Transaction tx) => StingTx.RollBackIfOpen(tx);
    }
}
