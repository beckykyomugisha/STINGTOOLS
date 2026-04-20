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
                using var tg = new TransactionGroup(doc, "STING live clash flag");
                tg.Start();
                using var t = new Transaction(doc, "STING live clash flag write");
                t.Start();
                foreach (var id in flaggedElementIds) SetParam(doc, id, true);
                foreach (var id in clearedElementIds) SetParam(doc, id, false);
                var opts = t.GetFailureHandlingOptions();
                opts.SetClearAfterRollback(true);
                opts.SetForcedModalHandling(false);
                t.SetFailureHandlingOptions(opts);
                t.Commit();
                tg.Assimilate();
            }
            catch (Exception ex) { StingLog.Warn($"LiveClashFlag.Apply: {ex.Message}"); }
        }

        private static void SetParam(Document doc, int elementId, bool value)
        {
            // Revit 2024+: ElementId(int) is obsolete — use the long overload.
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
