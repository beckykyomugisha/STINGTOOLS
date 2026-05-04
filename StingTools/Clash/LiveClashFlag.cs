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
        // F2: Per-element clash-count integer parameter. Lets view filters and
        //     schedules pick "elements with > N clashes" — unlocks heat-map
        //     view templates beyond the binary FLAG.
        public const string CountParamName = "CLASH_COUNT_INT";

        public static void Apply(Document doc, IEnumerable<int> flaggedElementIds, IEnumerable<int> clearedElementIds)
        {
            // F2: Default to count=1 for newly-flagged, count=0 for cleared.
            // ApplyWithCounts is the richer entry point.
            var flagged = flaggedElementIds == null ? null : new List<int>(flaggedElementIds);
            var cleared = clearedElementIds == null ? null : new List<int>(clearedElementIds);
            Dictionary<int, int> counts = null;
            if (flagged != null)
            {
                counts = new Dictionary<int, int>(flagged.Count);
                foreach (var id in flagged) counts[id] = 1;
            }
            ApplyWithCounts(doc, flagged, cleared, counts);
        }

        /// <summary>
        /// F2: Apply flag + per-element count atomically. CLASH_COUNT_INT
        /// reflects the number of *active* clashes the element is part of.
        /// Cleared elements get count=0.
        /// </summary>
        public static void ApplyWithCounts(Document doc,
            IEnumerable<int> flaggedElementIds,
            IEnumerable<int> clearedElementIds,
            IDictionary<int, int> counts)
        {
            if (doc == null) return;
            try
            {
                using var t = new Transaction(doc, "STING live clash flag");
                t.Start();
                var opts = t.GetFailureHandlingOptions();
                opts.SetClearAfterRollback(true);
                opts.SetForcedModalHandling(false);
                opts.SetFailuresPreprocessor(new SilentFlagFailurePreprocessor());
                t.SetFailureHandlingOptions(opts);
                if (flaggedElementIds != null)
                {
                    foreach (var id in flaggedElementIds)
                    {
                        SetParam(doc, id, true);
                        int cnt = (counts != null && counts.TryGetValue(id, out int c)) ? c : 1;
                        SetCountParam(doc, id, cnt);
                    }
                }
                if (clearedElementIds != null)
                {
                    foreach (var id in clearedElementIds)
                    {
                        SetParam(doc, id, false);
                        SetCountParam(doc, id, 0);
                    }
                }
                t.Commit();
            }
            catch (Exception ex) { StingLog.Warn($"LiveClashFlag.ApplyWithCounts: {ex.Message}"); }
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
            // ElementId(int) ctor is obsolete in Revit 2024+; use Int64 overload.
            var el = doc.GetElement(new ElementId((long)elementId));
            if (el == null) return;
            var p = el.LookupParameter(ParamName);
            if (p == null || p.IsReadOnly) return;
            if (p.StorageType == StorageType.Integer)
                p.Set(value ? 1 : 0);
            else if (p.StorageType == StorageType.String)
                p.Set(value ? "1" : "0");
        }

        /// <summary>F2: Set CLASH_COUNT_INT on an element. Best-effort.</summary>
        private static void SetCountParam(Document doc, int elementId, int count)
        {
            try
            {
                var el = doc.GetElement(new ElementId((long)elementId));
                if (el == null) return;
                var p = el.LookupParameter(CountParamName);
                if (p == null || p.IsReadOnly) return;
                if (p.StorageType == StorageType.Integer) p.Set(count);
                else if (p.StorageType == StorageType.Double) p.Set((double)count);
                else if (p.StorageType == StorageType.String) p.Set(count.ToString());
            }
            catch (Exception ex) { StingLog.Warn($"LiveClashFlag.SetCountParam({elementId}): {ex.Message}"); }
        }
    }
}
