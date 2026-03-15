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
                try { TaskDialog.Show("STING Tools", $"Tag & Combine failed:\n{ex.Message}"); } catch { }
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

            // Step 0: Choose scope
            TaskDialog scopeDlg = new TaskDialog("Tag & Combine All");
            scopeDlg.MainInstruction = "Tag and populate all containers";
            scopeDlg.MainContent =
                "This will:\n" +
                "  1. Auto-detect LOC/ZONE from spatial data + worksets\n" +
                "  2. Auto-populate all tokens (DISC, PROD, SYS, FUNC, LVL)\n" +
                "  3. Auto-detect STATUS from Revit phases + worksets\n" +
                "  4. Auto-detect REV from project revision sequence\n" +
                "  5. Tag all untagged elements (continuing from existing numbers)\n" +
                "  6. Combine tokens into ALL 53 tag containers\n\n" +
                "Choose scope:";
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Active View",
                "Process only elements visible in the current view");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Selected Elements",
                $"{uidoc.Selection.GetElementIds().Count} elements selected");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Entire Project",
                "Process all taggable elements across the entire model");
            scopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            var scopeResult = scopeDlg.Show();

            ICollection<ElementId> targetIds;
            string scopeLabel;
            switch (scopeResult)
            {
                case TaskDialogResult.CommandLink1:
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
                case TaskDialogResult.CommandLink2:
                    targetIds = uidoc.Selection.GetElementIds();
                    if (targetIds.Count == 0)
                    {
                        TaskDialog.Show("Tag & Combine", "No elements selected.");
                        return Result.Cancelled;
                    }
                    scopeLabel = $"{targetIds.Count} selected elements";
                    break;
                case TaskDialogResult.CommandLink3:
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

            int populated = 0;
            int tagged = 0;
            int combined = 0;
            int totalProcessed = 0;
            int locDetected = 0;
            int zoneDetected = 0;
            int statusDetected = 0;
            int revSet = 0;
            int errors = 0;
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

                    try
                    {
                        totalProcessed++;

                        // Steps 1-5: Auto-populate all 9 tokens via shared TokenAutoPopulator
                        // Uses cached phases/rooms/project data — no per-element collectors
                        var popResult = TokenAutoPopulator.PopulateAll(doc, el, popCtx);
                        populated += popResult.TokensSet;
                        if (popResult.LocDetected) locDetected++;
                        if (popResult.ZoneDetected) zoneDetected++;
                        if (popResult.StatusDetected) statusDetected++;
                        if (popResult.RevSet) revSet++;

                        // Step 6: Tag if not already complete (with collision detection)
                        // BuildAndWriteTag already writes containers internally, so we only
                        // need an explicit combine for elements that were SKIPPED by BuildAndWriteTag
                        // (already-tagged elements that still need container refresh).
                        bool tagWritten = TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                            existingTags: tagIndex, stats: stats,
                            cachedRev: popCtx.ProjectRev);
                        if (tagWritten) tagged++;

                        // Step 7: For elements NOT freshly tagged (skipped by BuildAndWriteTag),
                        // ensure containers and TAG7 are still populated/refreshed.
                        // BuildAndWriteTag already handles containers for newly-tagged elements.
                        string tag1Check = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                        if (!tagWritten && !string.IsNullOrEmpty(tag1Check))
                        {
                            string[] tokenVals = ParamRegistry.ReadTokenValues(el);
                            combined += ParamRegistry.WriteContainers(el, tokenVals, catName,
                                overwrite: true, skipParam: ParamRegistry.TAG7);
                            combined += TagConfig.WriteTag7All(doc, el, catName, tokenVals, overwrite: true);
                        }
                        else if (tagWritten)
                        {
                            // TAG7 still needs explicit write (BuildAndWriteTag doesn't call WriteTag7All)
                            string[] tokenVals = ParamRegistry.ReadTokenValues(el);
                            combined += TagConfig.WriteTag7All(doc, el, catName, tokenVals, overwrite: true);
                            // Count the containers written by BuildAndWriteTag
                            combined += 1; // TAG1 was written
                        }
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
            }
            sw.Stop();
            ComplianceScan.InvalidateCache();

            var report = new StringBuilder();
            report.AppendLine("Tag & Combine All Complete");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Scope:            {scopeLabel}");
            report.AppendLine($"  Processed:        {totalProcessed} elements");
            report.AppendLine($"  Populated:        {populated} token values");
            report.AppendLine($"  Tagged:           {tagged} new tags");
            report.AppendLine($"  Combined:         {combined} container values");
            if (locDetected > 0)
                report.AppendLine($"  LOC auto-detect:  {locDetected} (rooms/project/workset)");
            if (zoneDetected > 0)
                report.AppendLine($"  ZONE auto-detect: {zoneDetected} (rooms/workset)");
            if (statusDetected > 0)
                report.AppendLine($"  STATUS detect:    {statusDetected} (from Revit phases/worksets)");
            if (revSet > 0)
                report.AppendLine($"  REV auto-set:     {revSet} (revision '{popCtx.ProjectRev}')");
            if (errors > 0)
                report.AppendLine($"  Errors:           {errors} (see log for details)");
            report.AppendLine($"  Duration:         {sw.Elapsed.TotalSeconds:F1}s");
            if (stats.TotalCollisions > 0)
                report.AppendLine($"  Collisions:       {stats.TotalCollisions} (auto-resolved)");
            report.AppendLine();
            report.Append(stats.BuildReport());

            TaskDialog td = new TaskDialog("Tag & Combine All");
            td.MainInstruction = $"Processed {totalProcessed} elements ({combined} containers)";
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
                $"populated={populated}, tagged={tagged}, combined={combined}, " +
                $"locDetect={locDetected}, zoneDetect={zoneDetected}, " +
                $"statusDetect={statusDetected}, revSet={revSet}, " +
                $"compliance={postScan?.StatusBarText ?? "N/A"}, " +
                $"elapsed={sw.Elapsed.TotalSeconds:F1}s");

            return Result.Succeeded;
        }
    }
}
