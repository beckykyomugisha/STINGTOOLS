using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Tags
{
    /// <summary>
    /// One-click "Tag and Combine All" — chains AutoTag + CombineParameters
    /// so the user gets a fully-tagged project with all containers populated
    /// in a single click. This is the max-automation counterpart to MasterSetup.
    ///
    /// Workflow:
    ///   1. Auto-detect LOC from project info / room data / workset (eliminates SetLoc)
    ///   2. Auto-detect ZONE from room name patterns / workset (eliminates SetZone)
    ///   3. Auto-populate tokens (DISC, PROD, SYS, FUNC, LVL) with family-aware PROD
    ///   4. Auto-detect STATUS from Revit phases / workset (eliminates SetStatus)
    ///   5. Auto-detect REV from project revision sequence
    ///   6. Auto-tag all untagged elements (continues from existing sequence)
    ///   7. Combine parameters into ALL 53 containers (universal + discipline + MAT + FIN + ENV + STR + COMP + PERF + SUST + EQP)
    ///
    /// Scope options:
    ///   - Active view only
    ///   - Selected elements only
    ///   - Entire project
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagAndCombineCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try { return ExecuteCore(commandData, ref message, elements); }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("TagAndCombineCommand crashed", ex);
                try { TaskDialog.Show("STING Tools", $"Tag & Combine failed:\n{ex.Message}"); } catch (Exception dlgEx) { StingLog.Warn($"TaskDialog fallback: {dlgEx.Message}"); }
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

            // Step 0: Choose scope using StingModePicker
            int selCount = uidoc.Selection.GetElementIds().Count;
            var scopeOptions = new List<UI.StingModePicker.ModeOption>
            {
                new("Active View",
                    "Process only elements visible in the current view", "view", true),
                new("Selected Elements",
                    $"{selCount} elements currently selected", "selected"),
                new("Entire Project",
                    "Process all taggable elements across the entire model", "project"),
            };
            string scopeResult = UI.StingModePicker.Show(
                "Tag & Combine All",
                "Auto-populate tokens, tag, and combine into all 53 containers",
                scopeOptions,
                "Auto-detects LOC/ZONE/STATUS/REV from spatial data and phases");

            ICollection<ElementId> targetIds;
            string scopeLabel;
            switch (scopeResult)
            {
                case "view":
                    if (doc.ActiveView == null) { TaskDialog.Show("Tag & Combine", "No active view."); return Result.Failed; }
                    {
                        // Performance: use ElementMulticategoryFilter to skip non-taggable elements
                        var viewCollector = new FilteredElementCollector(doc, doc.ActiveView.Id)
                            .WhereElementIsNotElementType();
                        var catEnums = SharedParamGuids.AllCategoryEnums;
                        if (catEnums != null && catEnums.Length > 0)
                            viewCollector.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));
                        targetIds = viewCollector.Select(e => e.Id).ToList();
                    }
                    scopeLabel = $"active view '{doc.ActiveView.Name}'";
                    break;
                case "selected":
                    targetIds = uidoc.Selection.GetElementIds();
                    if (targetIds.Count == 0)
                    {
                        TaskDialog.Show("Tag & Combine", "No elements selected.");
                        return Result.Cancelled;
                    }
                    scopeLabel = $"{targetIds.Count} selected elements";
                    break;
                case "project":
                    {
                        // Performance: use ElementMulticategoryFilter to skip non-taggable elements
                        var projCollector = new FilteredElementCollector(doc)
                            .WhereElementIsNotElementType();
                        var catEnums = SharedParamGuids.AllCategoryEnums;
                        if (catEnums != null && catEnums.Length > 0)
                            projCollector.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));
                        targetIds = projCollector.Select(e => e.Id).ToList();
                    }
                    scopeLabel = "entire project";
                    break;
                default:
                    return Result.Cancelled;
            }

            var (tagIndex, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
            var sw = Stopwatch.StartNew();

            // Build PopulationContext ONCE — caches room index, LOC, REV, phases
            var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
            if (popCtx == null || !popCtx.IsValid())
            {
                string diag = popCtx?.DiagnosticSummary ?? "Context build returned null";
                StingLog.Error($"TagAndCombine: PopulationContext failed — {diag}");
                TaskDialog.Show("Tag & Combine",
                    $"Failed to build population context.\n\nDiagnostics: {diag}\n\n" +
                    "Check: rooms placed? Levels defined? Shared parameters bound?");
                return Result.Failed;
            }
            var formulas = TagPipelineHelper.LoadFormulas();
            var gridLines = TagPipelineHelper.LoadGridLines(doc);

            int totalProcessed = 0;
            int errors = 0;
            int skippedWorkset = 0, skippedDemolished = 0;
            var stats = new TaggingStats();

            bool cancelled = false;

            using (Transaction tx = new Transaction(doc, "STING Tag & Combine All"))
            {
                tx.Start();
                int loopIndex = 0;
                foreach (ElementId id in targetIds)
                {
                    // Check for user cancellation via Escape key every 100 elements
                    if (loopIndex % 100 == 0 && EscapeChecker.IsEscapePressed())
                    {
                        StingLog.Info($"TagAndCombine: cancelled by user at {totalProcessed} processed");
                        cancelled = true;
                        break;
                    }
                    loopIndex++;

                    Element el = doc.GetElement(id);
                    if (el == null) continue;

                    string catName = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(catName) || !popCtx.KnownCategories.Contains(catName))
                        continue;

                    // GAP-WS-01: Skip elements on worksets owned by other users
                    if (!TagPipelineHelper.IsEditableInWorksharing(doc, el))
                    { skippedWorkset++; continue; }

                    // GAP-PH-01: Skip demolished elements
                    if (TagPipelineHelper.IsDemolished(el))
                    { skippedDemolished++; continue; }

                    try
                    {
                        totalProcessed++;

                        // Full pipeline: populate → map → formulas → tag → containers → TAG7 → grid
                        bool pipelineOk = TagPipelineHelper.RunFullPipeline(doc, el, popCtx,
                            tagIndex, seqCounters, formulas, gridLines,
                            overwrite: true, skipComplete: false,
                            collisionMode: TagCollisionMode.AutoIncrement, stats: stats);
                        if (!pipelineOk) errors++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"TagAndCombine: failed on element {id}: {ex.Message}", ex);
                        errors++;
                    }
                }

                if (cancelled)
                {
                    tx.RollBack();
                    TaskDialog.Show("Tag & Combine", $"Cancelled by user.\n{totalProcessed} elements processed before cancellation.\nAll changes rolled back.");
                    return Result.Cancelled;
                }

                tx.Commit();
                // P6: Save SEQ sidecar after commit
                TagConfig.SaveSeqSidecar(doc, seqCounters);
            }
            sw.Stop();
            ComplianceScan.InvalidateCache();
            // FIX-13: Invalidate auto-tagger cached context after batch tagging
            StingAutoTagger.InvalidateContext();
            TagConfig.CheckComplianceGate(doc, "TagAndCombine");

            var report = new StringBuilder();
            report.AppendLine("Tag & Combine All Complete");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Scope:            {scopeLabel}");
            report.AppendLine($"  Processed:        {totalProcessed} elements");
            if (skippedWorkset > 0)
                report.AppendLine($"  Skipped (workset):{skippedWorkset:N0} (owned by other users)");
            if (skippedDemolished > 0)
                report.AppendLine($"  Skipped (demol.): {skippedDemolished:N0} (demolished elements)");
            report.AppendLine($"  Tagged:           {stats.TotalTagged:N0} new tags");
            if (stats.TotalSkipped > 0)
                report.AppendLine($"  Skipped:          {stats.TotalSkipped:N0} (already complete)");
            if (stats.TotalOverwritten > 0)
                report.AppendLine($"  Overwritten:      {stats.TotalOverwritten:N0}");
            if (errors > 0)
                report.AppendLine($"  Errors:           {errors} (see log for details)");
            report.AppendLine($"  Duration:         {sw.Elapsed.TotalSeconds:F1}s");
            if (stats.TotalCollisions > 0)
                report.AppendLine($"  Collisions:       {stats.TotalCollisions} (auto-resolved)");
            report.AppendLine();
            report.Append(stats.BuildReport());

            // LOGIC-03: Run compliance scan BEFORE TaskDialog so results are visible to user
            var postScan = ComplianceScan.Scan(doc);
            if (postScan != null)
            {
                report.AppendLine();
                report.AppendLine($"Compliance: {postScan.StatusBarText}");
            }

            TaskDialog td = new TaskDialog("Tag & Combine All");
            td.MainInstruction = $"Processed {totalProcessed} elements ({stats.TotalTagged:N0} tagged)";
            td.MainContent = report.ToString();
            td.Show();

            // GAP-017: Post-batch compliance summary for workflow chain visibility
            var postScan = ComplianceScan.Scan(doc);
            if (postScan != null)
            {
                report.AppendLine();
                report.AppendLine($"Compliance: {postScan.StatusBarText}");
            }

            StingLog.Info($"TagAndCombine: scope={scopeLabel}, processed={totalProcessed}, " +
                $"tagged={stats.TotalTagged}, skipped={stats.TotalSkipped}, " +
                $"collisions={stats.TotalCollisions}, errors={errors}, " +
                $"compliance={postScan?.StatusBarText ?? "N/A"}, " +
                $"elapsed={sw.Elapsed.TotalSeconds:F1}s");

            // Phase 165 follow-up — explicit batch teardown.
            TokenAutoPopulator.PopulationContext.EndSession();
            return Result.Succeeded;
        }
    }
}
