using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Family-Stage Pre-Population: the "before tagging" intelligence layer.
    ///
    /// The highest-logic approach to tagging is to ensure ALL token values are
    /// correct BEFORE tag generation. This command pre-populates:
    ///   - DISC from element category (deterministic — never wrong)
    ///   - LOC from spatial context (room, project info, workset)
    ///   - ZONE from room department/name/workset
    ///   - LVL from element level (deterministic)
    ///   - SYS from category → system mapping (6-layer MEP-aware)
    ///   - FUNC from system → function mapping (smart subsystem differentiation)
    ///   - PROD from family name (specific) or category (generic)
    ///   - STATUS from Revit phase/workset (EXISTING, NEW, DEMOLISHED, TEMPORARY)
    ///   - REV from project revision sequence
    ///
    /// After this runs, tagging is purely mechanical: just concatenate tokens + assign SEQ.
    /// No collisions from wrong tokens, no ISO violations from invalid codes.
    ///
    /// This is designed to run at the "family stage" — after elements are placed but
    /// before any tagging occurs. It's the foundation for zero-touch automation.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FamilyStagePopulateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try { return ExecuteCore(commandData, ref message, elements); }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("FamilyStagePopulateCommand crashed", ex);
                try { TaskDialog.Show("STING Tools", $"Family Stage Populate failed:\n{ex.Message}"); } catch { }
                return Result.Failed;
            }
        }

        private Result ExecuteCore(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;

            // Scope selection
            TaskDialog scopeDlg = new TaskDialog("Family-Stage Populate");
            scopeDlg.MainInstruction = "Pre-populate all tokens at family stage";
            scopeDlg.MainContent =
                "This ensures every token is correct BEFORE tagging.\n" +
                "After this, tag generation is pure concatenation — no guessing.\n\n" +
                "Tokens populated: DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, STATUS, REV\n" +
                "(SEQ is NOT assigned — that happens during tagging)\n\n" +
                "Click a scope option below to proceed:";
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Selected elements only",
                $"{uidoc.Selection.GetElementIds().Count} elements selected — click to proceed");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Active view",
                "Pre-populate all taggable elements in this view — click to proceed");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Entire project",
                "Pre-populate every taggable element in the model — click to proceed");
            scopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            ICollection<ElementId> targetIds;
            string scopeLabel;
            switch (scopeDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    targetIds = uidoc.Selection.GetElementIds();
                    if (targetIds.Count == 0)
                    {
                        TaskDialog.Show("Family-Stage Populate", "No elements selected.");
                        return Result.Cancelled;
                    }
                    scopeLabel = $"{targetIds.Count} selected elements";
                    break;
                case TaskDialogResult.CommandLink2:
                    if (doc.ActiveView == null) { TaskDialog.Show("Populate", "No active view."); return Result.Failed; }
                    targetIds = new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .WhereElementIsNotElementType()
                        .Select(e => e.Id).ToList();
                    scopeLabel = $"active view '{doc.ActiveView.Name}'";
                    break;
                case TaskDialogResult.CommandLink3:
                    targetIds = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .Select(e => e.Id).ToList();
                    scopeLabel = "entire project";
                    break;
                default:
                    return Result.Cancelled;
            }

            // Override mode
            TaskDialog overDlg = new TaskDialog("Token Write Mode");
            overDlg.MainInstruction = "How to handle existing token values?";
            overDlg.MainContent = "Click an option below to proceed, or Cancel to abort.";
            overDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Fill empty only (safe) — Recommended",
                "Only populate tokens that are currently empty — existing values untouched");
            overDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Overwrite all (force)",
                "Re-derive all tokens from scratch, overwriting any existing values");
            overDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            overDlg.DefaultButton = TaskDialogResult.CommandLink1;

            bool overwrite;
            switch (overDlg.Show())
            {
                case TaskDialogResult.CommandLink1: overwrite = false; break;
                case TaskDialogResult.CommandLink2: overwrite = true; break;
                default: return Result.Cancelled;
            }

            // Build PopulationContext ONCE — caches room index, project LOC, REV, phases
            var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
            var sw = Stopwatch.StartNew();

            int processed = 0;
            int totalTokensSet = 0;
            int locDetected = 0, zoneDetected = 0, statusDetected = 0, revSet = 0;
            int familyProdUsed = 0;
            int errors = 0;

            bool cancelled = false;

            using (Transaction tx = new Transaction(doc, "STING Family-Stage Populate"))
            {
                tx.Start();

                int loopIndex = 0;
                foreach (ElementId id in targetIds)
                {
                    if (loopIndex % 100 == 0 && EscapeChecker.IsEscapePressed())
                    {
                        StingLog.Info($"FamilyStagePopulate: cancelled by user at {processed} processed");
                        cancelled = true;
                        break;
                    }
                    loopIndex++;

                    Element el = doc.GetElement(id);
                    if (el == null) continue;

                    string catName = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(catName) || !popCtx.KnownCategories.Contains(catName))
                        continue;

                    processed++;

                    try
                    {
                        // Delegate to shared TokenAutoPopulator — single source of truth for
                        // all 9 token derivation logic (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD/STATUS/REV).
                        // Uses cached phases/rooms/project data — no per-element collectors.
                        var result = TokenAutoPopulator.PopulateAll(doc, el, popCtx, overwrite);
                        totalTokensSet += result.TokensSet;
                        if (result.LocDetected) locDetected++;
                        if (result.ZoneDetected) zoneDetected++;
                        if (result.StatusDetected) statusDetected++;
                        if (result.RevSet) revSet++;
                        if (result.FamilyProdUsed) familyProdUsed++;
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        StingLog.Error($"FamilyStagePopulate: element {el?.Id}: {ex.Message}", ex);
                    }
                }

                if (cancelled)
                {
                    tx.RollBack();
                    TaskDialog.Show("Family-Stage Populate", $"Cancelled by user.\n{processed} elements processed before cancellation.\nAll changes rolled back.");
                    return Result.Cancelled;
                }

                tx.Commit();
            }
            sw.Stop();
            int totalPopulated = totalTokensSet;

            var report = new StringBuilder();
            report.AppendLine("Family-Stage Pre-Population Complete");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Scope:    {scopeLabel}");
            report.AppendLine($"  Mode:     {(overwrite ? "Overwrite all" : "Fill empty only")}");
            report.AppendLine($"  Elements: {processed}");
            report.AppendLine($"  Duration: {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine();
            report.AppendLine("── TOKEN POPULATION ──");
            report.AppendLine($"  Total tokens set: {totalPopulated}");
            if (locDetected > 0) report.AppendLine($"  LOC auto-detect:  {locDetected} (from spatial data)");
            if (zoneDetected > 0) report.AppendLine($"  ZONE auto-detect: {zoneDetected} (from room data)");
            if (statusDetected > 0) report.AppendLine($"  STATUS detect:    {statusDetected} (from Revit phases)");
            if (revSet > 0) report.AppendLine($"  REV auto-set:     {revSet} (revision '{popCtx.ProjectRev}')");
            if (familyProdUsed > 0) report.AppendLine($"  Family PROD:      {familyProdUsed} (family-specific codes)");
            if (errors > 0) report.AppendLine($"  Errors:           {errors} (see log)");
            report.AppendLine();
            report.AppendLine("Next step: Run Auto Tag / Batch Tag / Tag & Combine");
            report.AppendLine("to assign SEQ numbers and assemble final tags.");

            TaskDialog td = new TaskDialog("Family-Stage Populate");
            td.MainInstruction = $"Pre-populated {totalPopulated} tokens on {processed} elements";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"FamilyStagePopulate: scope={scopeLabel}, elements={processed}, " +
                $"tokens={totalPopulated}, familyProd={familyProdUsed}, " +
                $"locDetect={locDetected}, zoneDetect={zoneDetected}, " +
                $"statusDetect={statusDetected}, rev={revSet}, errors={errors}, " +
                $"elapsed={sw.Elapsed.TotalSeconds:F1}s");

            return Result.Succeeded;
        }
    }
}
