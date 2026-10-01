// StingTools — Drawing Template Manager · Phase 168
//
// DrawingHealTitleBlocksCommand is the partial-sync sibling of
// DrawingSyncStylesCommand. Where SyncStyles re-applies the entire
// profile (scale, detail level, view template, view-style pack,
// annotation, etc.), this command targets only the title-block
// parameter binding layer. Use it when the operator wants to fix
// title-block cells without disturbing in-progress view-template
// edits or pending annotation work.
//
// Each healed sheet contributes a row to <project>/_BIM_COORD/
// titleblock_heal_audit.jsonl so the project history records what
// changed, when, and by whom.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DrawingHealTitleBlocksCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            try
            {
                var doc = (data?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No document open."; return Result.Failed; }

                // Collect every stamped sheet — even the ones drift detector
                // missed because their drift was String-only previously and
                // we now want to heal Integer/Yes-No/Double/ElementId too.
                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder)
                    .Where(s => !DrawingTypeStamper.IsLocked(s))
                    .Select(s => new
                    {
                        Sheet = s,
                        DtId  = ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID) ?? "",
                    })
                    .Where(x => !string.IsNullOrEmpty(x.DtId))
                    .ToList();

                if (sheets.Count == 0)
                {
                    BatchProduceCommons.Show("STING — Heal Title Blocks",
                        "No stamped & unlocked sheets found. Stamp sheets via the Drawing Types pipeline first.");
                    return Result.Succeeded;
                }

                var confirm = new TaskDialog("STING — Heal Title Blocks")
                {
                    MainInstruction = $"Heal title blocks on {sheets.Count} sheet(s)?",
                    MainContent =
                        "This rewrites every title-block parameter declared by each sheet's profile " +
                        "declared in titleBlockParams. Scale / detail level / view template / " +
                        "view-style pack / annotation are NOT touched. Style-locked sheets are skipped, and " +
                        "title blocks frozen with PRJ_TB_LOCK_BOOL are left untouched.",
                    CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Ok,
                };
                if (!BatchProduceCommons.Confirm(confirm)) return Result.Cancelled;

                var auditRows = new List<HealRow>();
                int totalParams = 0;
                int healed = 0;
                int lockedSkipped = 0;
                int wrongFamily = 0;
                int unresolvedCells = 0;
                int totalUnchanged = 0;
                var unknownType = new List<string>();
                var notKept = new List<string>();
                var runWarnings = new List<string>();
                bool stopped = false;
                using (var runner = new ProductionItemRunner(doc, "Heal Title Blocks", sheets.Count))
                using (TitleBlockParamApplier.Batch())
                using (var tg = new TransactionGroup(doc, "STING — Heal Title Blocks"))
                {
                    tg.Start();
                    foreach (var x in sheets)
                    {
                        if (runner.ShouldStop()) break;   // DTW-204: between sheets, never inside one
                        var dt = DrawingTypeRegistry.Get(doc, x.DtId);
                        if (dt == null)
                        {
                            // DTW-16: dropped silently yet counted in "of N" —
                            // a sheet stamped with a type this project no longer
                            // defines read as merely "not needing a heal".
                            unknownType.Add($"{x.Sheet.SheetNumber} ({x.DtId})");
                            continue;
                        }
                        // DTW-202: one transaction per sheet. A single sheet owned by a
                        // colleague rolled back every heal, and the audit log still
                        // recorded them all. Each sheet (and its title blocks) is
                        // pre-checked in a workshared model; counts and the audit row
                        // are taken only once Revit has committed that sheet.
                        string label = $"{x.Sheet.SheetNumber} - {x.Sheet.Name}";
                        TitleBlockApplyResult kept = null;
                        bool wrong = false;
                        var outcome = runner.Run($"STING — Heal Title Blocks - {x.Sheet.SheetNumber}", label,
                            () => runner.Preflight.Active ? runner.Preflight.Check(SheetAndTitleBlocks(doc, x.Sheet)) : null,
                            () => kept = HealOne(doc, x.Sheet, x.DtId, dt, out wrong),
                            st => $"{label}: the transaction did not commit ({st}); not healed.",
                            runWarnings);
                        if (outcome != ProductionItemRunner.ItemResult.Committed || kept == null)
                        {
                            notKept.Add(label);
                            continue;
                        }
                        var result = kept;
                        if (wrong) wrongFamily++;
                        totalParams += result.ParamsWritten;
                        totalUnchanged += result.ParamsUnchanged;
                        unresolvedCells += result.CellsUnresolved;
                        // T-3: Apply now leaves PRJ_TB_LOCK_BOOL title blocks
                        // alone. Count them so "healed N of M" doesn't quietly
                        // read as a failure on a project that locks sheets on
                        // purpose — a skipped lock is a success, not a miss.
                        lockedSkipped += result.LockedSkipped;
                        if (result.ParamsWritten > 0) healed++;
                        if (result.ParamsWritten > 0 || result.Warnings.Count > 0)
                        {
                            auditRows.Add(new HealRow
                            {
                                UtcTimestamp  = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                                User          = Environment.UserName ?? "unknown",
                                SheetNumber   = x.Sheet.SheetNumber ?? "",
                                SheetName     = x.Sheet.Name ?? "",
                                DrawingTypeId = x.DtId,
                                ParamsWritten = result.ParamsWritten,
                                Warnings      = result.Warnings.Take(10).ToList(),
                            });
                        }
                    }
                    tg.Assimilate();   // a stopped run keeps what it committed
                    stopped = runner.Stopped;
                    if (stopped) runWarnings.Insert(0, runner.StoppedLine("sheet(s)"));
                }

                // DTW-202: the audit records heals Revit kept — nothing rolled back.
                if (auditRows.Count > 0) AppendAuditLog(doc, auditRows);
                foreach (var w in runWarnings) StingLog.Warn($"Heal Title Blocks: {w}");

                var sb = new StringBuilder();
                if (stopped) sb.AppendLine(runWarnings[0]);
                sb.AppendLine($"Healed title blocks on {healed} of {sheets.Count} sheet(s).");
                if (notKept.Count > 0)
                {
                    sb.AppendLine($"{notKept.Count} sheet(s) NOT healed — skipped or rolled back, nothing changed on them:");
                    foreach (var n in notKept.Take(10)) sb.AppendLine("  " + n);
                    if (notKept.Count > 10) sb.AppendLine($"  …({notKept.Count - 10} more)");
                    foreach (var w in runWarnings.Take(10)) sb.AppendLine("  " + w);
                }
                AppendSummary(sb, totalParams, totalUnchanged, unknownType, wrongFamily, lockedSkipped, unresolvedCells, auditRows);
                BatchProduceCommons.Show("STING — Heal Title Blocks", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingHealTitleBlocks", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }

        private static ICollection<ElementId> SheetAndTitleBlocks(Document doc, ViewSheet sheet)
        {
            var ids = new List<ElementId> { sheet.Id };
            ids.AddRange(new FilteredElementCollector(doc, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType().ToElementIds());
            return ids;
        }

        /// <summary>Heal one sheet (caller owns the transaction). <paramref name="wrongFamily"/>
        /// is true when the sheet is on another title-block family than its profile's.</summary>
        private static TitleBlockApplyResult HealOne(Document doc, ViewSheet sheet, string dtId, DrawingType dt, out bool wrongFamily)
        {
            wrongFamily = false;
            // T-5: one shared builder with Migrate / drift — recovers
            // {lvl}/{mark} from the sheet's production-context stamp
            // instead of dropping them (Heal used to pass no level).
            var tokens = DrawingTokenContext.BuildForExistingSheet(doc, sheet, dt);
            var result = TitleBlockParamApplier.Apply(doc, sheet, dt, tokens);

            // T-9: heal re-stamps parameters but never checked whether
            // the sheet is on the RIGHT title-block family, so a sheet
            // carrying the wrong family reported "healed" while staying
            // wrong. Detect and report; replacing a family is a
            // destructive change and is deliberately not automatic.
            try
            {
                // DTW-17: resolve the variant first (TitleBlockVariantRules
                // and the "Family:Symbol" form), as Doctor and the
                // Validator do — the raw TitleBlockFamily flagged every
                // variant-routed or colon-form sheet as a wrong family.
                string declared = dt.TitleBlockFamily;
                try { declared = DrawingDispatcher.ResolveTitleBlockVariant(dt).family; }
                catch (Exception exVar) { StingLog.Warn($"Heal variant resolve '{dt.Id}': {exVar.Message}"); }
                if (string.IsNullOrWhiteSpace(declared)) declared = dt.TitleBlockFamily;
                var wanted = TitleBlockResolver.ToConcreteFamily(doc, dt, declared);
                if (!string.IsNullOrWhiteSpace(wanted))
                {
                    foreach (var el in new FilteredElementCollector(doc, sheet.Id)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .WhereElementIsNotElementType())
                    {
                        var actual = (el as FamilyInstance)?.Symbol?.FamilyName;
                        if (string.IsNullOrEmpty(actual)) continue;
                        if (!string.Equals(actual, wanted, StringComparison.OrdinalIgnoreCase))
                        {
                            wrongFamily = true;
                            result.Warnings.Add(
                                $"Sheet is on title-block family '{actual}' but profile '{dtId}' " +
                                $"expects '{wanted}' — parameters were healed, the family was not changed.");
                            break;
                        }
                    }
                }
            }
            catch (Exception exFam) { StingLog.Warn($"Heal family check: {exFam.Message}"); }
            return result;
        }

        private static void AppendSummary(StringBuilder sb, int totalParams, int totalUnchanged, List<string> unknownType,
            int wrongFamily, int lockedSkipped, int unresolvedCells, List<HealRow> auditRows)
        {
            sb.AppendLine($"Total param writes: {totalParams} ({totalUnchanged} cell(s) already correct, not rewritten).");
            if (unknownType.Count > 0)
            {
                sb.AppendLine($"{unknownType.Count} sheet(s) NOT healed — stamped with a drawing type this project " +
                              "does not define (renamed or removed profile):");
                foreach (var u in unknownType.Take(10)) sb.AppendLine("  " + u);
                if (unknownType.Count > 10) sb.AppendLine($"  …({unknownType.Count - 10} more)");
                StingLog.Warn($"Heal Title Blocks: {unknownType.Count} sheet(s) stamped with an unknown drawing type: " +
                              string.Join(", ", unknownType));
            }
            if (wrongFamily > 0)
                sb.AppendLine($"{wrongFamily} sheet(s) are on the WRONG title-block family — see the audit log.");
            if (lockedSkipped > 0)
                sb.AppendLine($"{lockedSkipped} title block(s) skipped — locked with {ParamRegistry.TB_LOCK}.");
            if (unresolvedCells > 0)
                sb.AppendLine($"{unresolvedCells} cell(s) NOT written — their template needs a value the sheet " +
                              "cannot supply (unbound Project Information parameter, or a {token} with no " +
                              "production-context stamp). Existing values were kept; see the audit log.");
            int withWarn = auditRows.Count(a => a.Warnings != null && a.Warnings.Count > 0);
            if (withWarn > 0) sb.AppendLine($"{withWarn} sheet(s) emitted warnings — see _BIM_COORD/titleblock_heal_audit.jsonl.");
        }

        private sealed class HealRow
        {
            public string UtcTimestamp { get; set; }
            public string User { get; set; }
            public string SheetNumber { get; set; }
            public string SheetName { get; set; }
            public string DrawingTypeId { get; set; }
            public int    ParamsWritten { get; set; }
            public List<string> Warnings { get; set; }
        }

        private static void AppendAuditLog(Document doc, List<HealRow> rows)
        {
            try
            {
                // OutputLocationHelper, not Path.GetTempPath(): under Revit that is a
                // per-session GUID folder, so the audit trail disappeared with the session.
                string outDir = !string.IsNullOrEmpty(doc?.PathName)
                    ? StingPaths.Meta(doc, "_BIM_COORD")
                    : OutputLocationHelper.GetOutputDirectory(doc);
                Directory.CreateDirectory(outDir);
                var path = Path.Combine(outDir, "titleblock_heal_audit.jsonl");
                using (var sw = File.AppendText(path))
                {
                    foreach (var r in rows)
                        sw.WriteLine(JsonConvert.SerializeObject(r, Formatting.None));
                }
            }
            catch (Exception ex) { StingLog.Warn($"AppendAuditLog: {ex.Message}"); }
        }
    }
}
