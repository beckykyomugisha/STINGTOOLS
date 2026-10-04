// ElecWriteReport — the one line a command prints about writes it made inside a
// transaction. Revit-free: StingTools.Tags.Tests compiles this file.
//
// Transaction.Commit() returns a status; a failure handler (or the user cancelling
// Revit's error dialog) rolls everything back and Commit() returns RolledBack. The
// electrical commands counted each write as it was made and printed that count after
// Commit() without looking at the status, so a rolled-back run reported
// "Stamped 12 values" with nothing in the model.

namespace StingTools.Core.Electrical
{
    public static class ElecWriteReport
    {
        /// <summary>Writes that are actually in the model: the count, or 0 when rolled back.</summary>
        public static int Kept(int written, bool committed) => committed ? written : 0;

        /// <summary>
        /// "{count} {noun}." when the transaction committed; otherwise says the writes
        /// were made but ROLLED BACK, with the status, so a count is never shown for
        /// values that are not there.
        /// </summary>
        /// <summary>The message of a step whose transaction Revit rolled back (ElecTx.Commit).</summary>
        public static string RolledBack(string what, string status) => StingTools.Core.TxReport.RolledBack(what, status);

        public static string Landed(string noun, int written, bool committed, string status = null)
        {
            if (committed) return $"{written} {noun}.";
            string st = string.IsNullOrWhiteSpace(status) ? "not committed" : status.Trim();
            return written > 0
                ? $"ROLLED BACK ({st}): 0 {noun} — {written} write(s) were made and then undone; nothing was kept."
                : $"ROLLED BACK ({st}): 0 {noun}.";
        }
    }
}
