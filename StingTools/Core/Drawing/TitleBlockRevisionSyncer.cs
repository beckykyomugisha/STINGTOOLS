// StingTools — Title Block Revision Syncer (Gap 4)
//
// THE single engine bridging Revit's Revision system and the title-block
// parameter layer. Both the "Rev Sync" (tag RevisionSync) and "Sync Rev"
// (tag DrawingTypes_SyncRevisions) dock buttons route here, as does
// Phase C of Produce & Export and IssueSheetsForRevisionCommand.
//
// For every non-placeholder sheet it reads the newest Revision by
// SequenceNumber and writes:
//   - on the ViewSheet:            SHT_REV_TXT, SHT_REV_DATE_TXT
//   - on each title-block instance: PRJ_TB_REVISION_NR_TXT,
//                                   PRJ_TB_REVISION_DATE_TXT,
//                                   PRJ_TB_REVISION_DESCRIPTION_TXT,
//                                   PRJ_TB_ISSUE_SUMMARY_TXT
// Those four TB params are the ones STING_TITLE_BLOCKS.json actually
// binds labels to, so the writes are visible on the drawing.
//
// The value written is Revision.RevisionNumber (e.g. "P01"), NEVER the
// internal SequenceNumber (1, 2, 3...). A "R{SequenceNumber}" fallback
// applies only when RevisionNumber is empty or unreadable.
//
// Key design choices:
//   - SyncAll runs inside a single Transaction; per-sheet failures are
//     caught and recorded as warnings, never aborting the batch.
//   - WriteIfChanged / WriteParamIfExists are skip-if-equal so the
//     undo stack is not polluted by no-op re-runs.
//   - Sheets with no revisions have their values cleared, so stale data
//     from a prior revision is never left on the drawing.
//   - The syncer is idempotent — safe to call repeatedly.
//
// Historical note: this class used to also write a five-row revision
// strip to per-row column/date/description parameters. Those parameters
// were declared nowhere — not in STING_TITLE_BLOCKS.json, not in
// MR_PARAMETERS, with no labels bound — so every write was a silent
// no-op. That path has been removed.

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
    /// revision-box parameters on title-block instances.
    /// </summary>
    public static class TitleBlockRevisionSyncer
    {
        // Shared parameter names written onto the ViewSheet itself.
        // SHT_REV_TXT is the canonical sheet-level revision param.
        // We derive a revision date param name from the same SHT_ prefix
        // convention used elsewhere in ParamRegistry.
        private const string SheetRevParam     = ParamRegistry.SHT_REV;           // "SHT_REV_TXT"
        private const string SheetRevDateParam = "SHT_REV_DATE_TXT";

        // Title-block instance params that STING_TITLE_BLOCKS.json binds
        // revision-box labels to.
        private const string TbRevNrParam   = "PRJ_TB_REVISION_NR_TXT";
        private const string TbRevDateParam = "PRJ_TB_REVISION_DATE_TXT";
        private const string TbRevDescParam = "PRJ_TB_REVISION_DESCRIPTION_TXT";

        /// <summary>
        /// Syncs revision data for every non-placeholder sheet in the document.
        /// </summary>
        /// <param name="stampedOnly">When true, restrict the sweep to sheets
        /// carrying a non-empty STING_DRAWING_TYPE_ID_TXT stamp written by
        /// <see cref="DrawingTypeStamper"/>. Default false — all sheets.</param>
        public static RevisionSyncResult SyncAll(Document doc, bool stampedOnly = false)
        {
            var result = new RevisionSyncResult();
            if (doc == null) return result;

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .Where(s => !stampedOnly || !string.IsNullOrEmpty(DrawingTypeStamper.Read(s)))
                .OrderBy(s => s.SheetNumber)
                .ToList();

            StingLog.Info($"TitleBlockRevisionSyncer.SyncAll: {sheets.Count} sheet(s) " +
                $"(stampedOnly={stampedOnly}).");

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

            string revNr = "", revDate = "", revDesc = "";

            if (revIds != null && revIds.Count > 0)
            {
                // Sort newest-first: Revit assigns SequenceNumber in creation
                // order; higher SequenceNumber == more recent.
                var latest = revIds
                    .Select(id => doc.GetElement(id))
                    .OfType<Revision>()
                    .OrderByDescending(r => r.SequenceNumber)
                    .FirstOrDefault();

                if (latest != null)
                {
                    revNr   = ResolveRevisionNumber(latest);
                    revDate = latest.RevisionDate ?? "";
                    revDesc = latest.Description  ?? "";
                }
            }

            // Sheet-level params. When there are no revisions these are written
            // as empty, clearing stale values from a prior revision.
            if (WriteIfChanged(sheet, SheetRevParam,     revNr))   result.ParamsWritten++;
            if (WriteIfChanged(sheet, SheetRevDateParam, revDate)) result.ParamsWritten++;

            // Revision box on every title-block instance on this sheet.
            foreach (var tb in CollectTitleBlocks(doc, sheet))
            {
                if (WriteParamIfExists(tb, TbRevNrParam,   revNr))   result.ParamsWritten++;
                if (WriteParamIfExists(tb, TbRevDateParam, revDate)) result.ParamsWritten++;
                if (WriteParamIfExists(tb, TbRevDescParam, revDesc)) result.ParamsWritten++;
                if (WriteParamIfExists(tb, ParamRegistry.TB_ISSUE_SUMMARY, revDesc)) result.ParamsWritten++;
            }

            result.SheetsProcessed++;
        }

        // The user-facing revision number ("P01", "C02", ...) — never the
        // internal SequenceNumber. Falls back to "R{SequenceNumber}" only when
        // RevisionNumber is empty or unreadable.
        private static string ResolveRevisionNumber(Revision rev)
        {
            try
            {
                string n = rev.RevisionNumber;
                if (!string.IsNullOrWhiteSpace(n)) return n;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TitleBlockRevisionSyncer: RevisionNumber unreadable — {ex.Message}");
            }
            try { return "R" + rev.SequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture); }
            catch { return ""; }
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
