// LiveClashFlag.cs — writes CLASH_LIVE_FLAG yes/no parameter on elements flagged by live clash.
// Uses a suppressed transaction so the user's undo history is not polluted.
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    public static class LiveClashFlag
    {
        public const string ParamName = "CLASH_LIVE_FLAG";

        public static void Apply(Document doc, IEnumerable<int> flaggedElementIds, IEnumerable<int> clearedElementIds)
        {
            if (doc == null) return;
            try
            {
                // rec-10: Plain Transaction — no TransactionGroup, no Assimilate().
                //
                // Prior implementation wrapped the write in TransactionGroup +
                // Transaction + Assimilate(). Two problems:
                //   1. STING repo convention (CLAUDE.md Phase 7 entry 44) explicitly
                //      avoids TransactionGroup.Assimilate() after prior native crash
                //      reports — assimilating from inside an IExternalEventHandler
                //      tick after a user transaction just committed can trip
                //      InvalidOperationException intermittently.
                //   2. A TransactionGroup that only contains a single child
                //      Transaction is semantically identical to the Transaction
                //      alone once assimilated — the wrapper adds nothing.
                //
                // Single undo entry labeled "STING live clash flag" is acceptable
                // UX: users expect to be able to undo their edit; the clash flag
                // change that piggybacked on it goes with it.
                using var t = new Transaction(doc, "STING live clash flag");
                t.Start();
                var opts = t.GetFailureHandlingOptions();
                opts.SetClearAfterRollback(true);
                opts.SetForcedModalHandling(false);
                opts.SetFailuresPreprocessor(new SilentFlagFailurePreprocessor());
                t.SetFailureHandlingOptions(opts);
                foreach (var id in flaggedElementIds) SetParam(doc, id, true);
                foreach (var id in clearedElementIds) SetParam(doc, id, false);
                t.Commit();
            }
            catch (Exception ex) { StingLog.Warn($"LiveClashFlag.Apply: {ex.Message}"); }
        }

        /// <summary>
        /// rec-10: Silent failures preprocessor. LiveClashFlag writes are low-stakes
        /// diagnostic UI (a yes/no marker). A read-only parameter or a deleted
        /// element shouldn't raise modal warnings to the user mid-edit.
        /// </summary>
        private sealed class SilentFlagFailurePreprocessor : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                foreach (var fm in accessor.GetFailureMessages())
                {
                    accessor.DeleteWarning(fm);
                }
                return FailureProcessingResult.Continue;
            }
        }

        private static void SetParam(Document doc, int elementId, bool value)
        {
            // Revit 2024+: ElementId(int) ctor obsolete; use the long overload.
            var el = doc.GetElement(new ElementId((long)elementId));
            if (el == null) return;
            var p = el.LookupParameter(ParamName);
            if (p == null || p.IsReadOnly) return;
            if (p.StorageType == StorageType.Integer)
                p.Set(value ? 1 : 0);
            else if (p.StorageType == StorageType.String)
                p.Set(value ? "1" : "0");
        }
    }
}
