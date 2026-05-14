// StingTools — Title Block Revision Strip Syncer (Gap 4)
//
// Bridges Revit's Revision system and the title-block parameter layer.
// For every sheet that has a stamped DrawingType, reads the sheet's
// issued Revision sequence, writes SHT_REV_TXT (the closest equivalent
// to ASS_REV_TXT for sheets) shared params on the sheet, and ensures
// the revision strip explicit five-row table cells PRJ_TB_REV_COL_n /
// PRJ_TB_REV_DATE_n / PRJ_TB_REV_DESC_n (n = 1..5) on the title-block
// FamilyInstance are kept in sync with the live Revit revision sequence.
//
// For projects using a live ScheduleSheetInstance (the Revit-native
// revision schedule), the five-row write is still performed so title
// blocks that carry both a schedule region AND discrete text cells
// remain consistent.
//
// Key design choices:
//   - SyncAll runs inside a single Transaction; per-sheet failures are
//     caught and recorded as warnings, never aborting the batch.
//   - WriteIfChanged / WriteParamIfExists are skip-if-equal so the
//     undo stack is not polluted by no-op re-runs.
//   - Revisions are sorted newest-first so row 1 always carries the
//     most recent revision (standard UK practice for revision strips).
//   - The syncer is idempotent — safe to call repeatedly.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Drawing
{
    /// <summary>
    /// Summary results returned by <see cref="TitleBlockRevisionSyncer.SyncAll"/>
    /// and <see cref="TitleBlockRevisionSyncer.SyncSheet(Document,ViewSheet)"/>.
    /// </summary>
    public sealed class RevisionSyncResult
    {
        public int SheetsProcessed { get; set; }
        public int ParamsWritten   { get; set; }
        public int SheetsSkipped   { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Syncs Revit revision data onto sheet shared parameters and the
    /// explicit five-row revision table cells on title-block instances.
    /// </summary>
    public static class TitleBlockRevisionSyncer
    {
        // Max revision rows supported in the explicit 5-row table approach.
        private const int MaxRevRows = 5;

        // Param name templates for the explicit rev table cells on the
        // title-block FamilyInstance (n = 1..MaxRevRows).
        private static string RevColParam(int n)  => $"PRJ_TB_REV_COL_{n}";
        private static string RevDateParam(int n) => $"PRJ_TB_REV_DATE_{n}";
        private static string RevDescParam(int n) => $"PRJ_TB_REV_DESC_{n}";

        // Shared parameter names written onto the ViewSheet itself.
        // SHT_REV_TXT is the canonical sheet-level revision param.
        // We derive a revision date param name from the same SHT_ prefix
        // convention used elsewhere in ParamRegistry.
        private const string SheetRevParam     = ParamRegistry.SHT_REV;           // "SHT_REV_TXT"
        private const string SheetRevDateParam = "SHT_REV_DATE_TXT";

        /// <summary>
        /// Syncs revision data for all stamped sheets in the document.
        /// A sheet is "stamped" when it carries a non-empty
        /// STING_DRAWING_TYPE_ID_TXT value written by
        /// <see cref="DrawingTypeStamper"/>.
        /// </summary>
        public static RevisionSyncResult SyncAll(Document doc)
        {
            var result = new RevisionSyncResult();
            if (doc == null) return result;

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !string.IsNullOrEmpty(DrawingTypeStamper.Read(s)))
                .ToList();

            StingLog.Info($"TitleBlockRevisionSyncer.SyncAll: {sheets.Count} stamped sheet(s).");

            using (var tx = new Transaction(doc, "STING Sync Revision Strip"))
            {
                tx.Start();
                foreach (var sheet in sheets)
                {
                    try
                    {
                        SyncSheet(doc, sheet, result);
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Sheet {sheet.SheetNumber}: {ex.Message}");
                        result.SheetsSkipped++;
                        StingLog.Warn($"TitleBlockRevisionSyncer: sheet {sheet.SheetNumber} — {ex.Message}");
                    }
                }
                tx.Commit();
            }

            StingLog.Info($"TitleBlockRevisionSyncer.SyncAll done — " +
                $"{result.SheetsProcessed} processed, {result.ParamsWritten} params written, " +
                $"{result.SheetsSkipped} skipped, {result.Warnings.Count} warning(s).");
            return result;
        }

        /// <summary>
        /// Syncs revision data for a single sheet (wraps its own transaction).
        /// </summary>
        public static RevisionSyncResult SyncSheet(Document doc, ViewSheet sheet)
        {
            var result = new RevisionSyncResult();
            if (doc == null || sheet == null) return result;
            using (var tx = new Transaction(doc, "STING Sync Revision Strip (sheet)"))
            {
                tx.Start();
                try
                {
                    SyncSheet(doc, sheet, result);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Sheet {sheet.SheetNumber}: {ex.Message}");
                    result.SheetsSkipped++;
                    tx.RollBack();
                    return result;
                }
                tx.Commit();
            }
            return result;
        }

        // Core implementation — must be called inside an active transaction.
        private static void SyncSheet(Document doc, ViewSheet sheet, RevisionSyncResult result)
        {
            // Collect all revision ids issued to this sheet.
            IList<ElementId> revIds;
            try { revIds = sheet.GetAllRevisionIds(); }
            catch { revIds = null; }

            if (revIds == null || revIds.Count == 0)
            {
                // No revisions — clear any previously written values so stale
                // data from a prior revision is not left in the cells.
                WriteIfChanged(sheet, SheetRevParam,     "");
                WriteIfChanged(sheet, SheetRevDateParam, "");
                result.ParamsWritten += 2;
                ClearRevRowsOnTitleBlocks(doc, sheet, result);
                result.SheetsProcessed++;
                return;
            }

            // Sort newest-first: Revit assigns SequenceNumber in creation order;
            // higher SequenceNumber == more recent.
            var revisions = revIds
                .Select(id => doc.GetElement(id))
                .OfType<Revision>()
                .OrderByDescending(r => r.SequenceNumber)
                .ToList();

            // Write the most recent revision onto the sheet shared params.
            var latest = revisions[0];
            WriteIfChanged(sheet, SheetRevParam,     latest.RevisionNumber ?? "");
            WriteIfChanged(sheet, SheetRevDateParam, latest.RevisionDate   ?? "");
            result.ParamsWritten += 2;

            // Write the explicit 5-row table onto every title-block instance.
            var tbInstances = CollectTitleBlocks(doc, sheet);

            for (int n = 1; n <= MaxRevRows; n++)
            {
                var rev = n <= revisions.Count ? revisions[n - 1] : null;
                string col  = rev?.RevisionNumber ?? "";
                string date = rev?.RevisionDate   ?? "";
                string desc = rev?.Description    ?? "";

                foreach (var tb in tbInstances)
                {
                    if (WriteParamIfExists(tb, RevColParam(n),  col))  result.ParamsWritten++;
                    if (WriteParamIfExists(tb, RevDateParam(n), date)) result.ParamsWritten++;
                    if (WriteParamIfExists(tb, RevDescParam(n), desc)) result.ParamsWritten++;
                }
            }

            result.SheetsProcessed++;
        }

        // Clears all PRJ_TB_REV_COL/DATE/DESC rows (called when a sheet has
        // no revisions, so leftover data from a prior revision is erased).
        private static void ClearRevRowsOnTitleBlocks(Document doc, ViewSheet sheet, RevisionSyncResult result)
        {
            var tbInstances = CollectTitleBlocks(doc, sheet);
            for (int n = 1; n <= MaxRevRows; n++)
            {
                foreach (var tb in tbInstances)
                {
                    if (WriteParamIfExists(tb, RevColParam(n),  "")) result.ParamsWritten++;
                    if (WriteParamIfExists(tb, RevDateParam(n), "")) result.ParamsWritten++;
                    if (WriteParamIfExists(tb, RevDescParam(n), "")) result.ParamsWritten++;
                }
            }
        }

        // Writes value to a String parameter if it exists, is writable,
        // and if the current value differs (skip-if-equal).
        // Returns true when a write actually happened.
        private static bool WriteIfChanged(Element el, string paramName, string value)
        {
            var p = el?.LookupParameter(paramName);
            if (p == null || p.IsReadOnly) return false;
            if (p.StorageType != StorageType.String) return false;
            if (string.Equals(p.AsString() ?? "", value ?? "", StringComparison.Ordinal)) return false;
            try { p.Set(value ?? ""); return true; }
            catch { return false; }
        }

        // Same as WriteIfChanged but silently returns false when the parameter
        // does not exist on this element (graceful for optional TB params).
        private static bool WriteParamIfExists(Element el, string paramName, string value)
        {
            if (el == null) return false;
            var p = el.LookupParameter(paramName);
            if (p == null || p.IsReadOnly) return false;
            if (p.StorageType != StorageType.String) return false;
            if (string.Equals(p.AsString() ?? "", value ?? "", StringComparison.Ordinal)) return false;
            try { p.Set(value ?? ""); return true; }
            catch { return false; }
        }

        private static List<FamilyInstance> CollectTitleBlocks(Document doc, ViewSheet sheet)
        {
            try
            {
                return new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .ToList();
            }
            catch { return new List<FamilyInstance>(); }
        }
    }
}
