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
                    targetIds = new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .WhereElementIsNotElementType()
                        .Select(e => e.Id).ToList();
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
                    targetIds = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .Select(e => e.Id).ToList();
                    scopeLabel = "entire project";
                    break;
                default:
                    return Result.Cancelled;
            }

            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            var (tagIndex, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
            var sw = Stopwatch.StartNew();

            // Pre-build room spatial index for LOC/ZONE auto-detection
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);

            // Pre-detect project-level values once
            string projectRev = PhaseAutoDetect.DetectProjectRevision(doc);

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

            // Container definitions now loaded from PARAMETER_REGISTRY.json via ParamRegistry
            // This eliminates DRY violations — all container definitions in a single source of truth.

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
                    if (string.IsNullOrEmpty(catName) || !known.Contains(catName))
                        continue;

                    try
                    {
                        totalProcessed++;

                        // Step 1: Auto-detect LOC from spatial data
                        string existingLoc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                        if (string.IsNullOrEmpty(existingLoc))
                        {
                            string detectedLoc = SpatialAutoDetect.DetectLoc(doc, el, roomIndex, projectLoc);
                            if (!string.IsNullOrEmpty(detectedLoc))
                            {
                                ParameterHelpers.SetIfEmpty(el, ParamRegistry.LOC, detectedLoc);
                                locDetected++;
                                populated++;
                            }
                        }

                        // Step 2: Auto-detect ZONE from room data
                        string existingZone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
                        if (string.IsNullOrEmpty(existingZone))
                        {
                            string detectedZone = SpatialAutoDetect.DetectZone(doc, el, roomIndex);
                            if (!string.IsNullOrEmpty(detectedZone))
                            {
                                ParameterHelpers.SetIfEmpty(el, ParamRegistry.ZONE, detectedZone);
                                zoneDetected++;
                                populated++;
                            }
                        }

                        // Step 3: Auto-populate tokens from category + family lookup
                        // (guaranteed defaults: DISC→"A", SYS→discipline, FUNC→FuncMap, LVL→"L00")
                        string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "A";
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.DISC, disc)) populated++;

                        // Family-aware PROD code: check family name before falling back to category
                        string prod = TagConfig.GetFamilyAwareProdCode(el, catName);
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.PROD, prod)) populated++;

                        // MEP system-aware SYS derivation (guaranteed default from discipline)
                        string sys = TagConfig.GetMepSystemAwareSysCode(el, catName);
                        if (string.IsNullOrEmpty(sys)) sys = TagConfig.GetDiscDefaultSysCode(disc);
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.SYS, sys)) populated++;
                        // System-aware DISC correction for pipes
                        disc = TagConfig.GetSystemAwareDisc(disc, sys, catName);
                        ParameterHelpers.SetString(el, ParamRegistry.DISC, disc, overwrite: true);

                        string func = TagConfig.GetSmartFuncCode(el, sys);
                        if (string.IsNullOrEmpty(func))
                            func = TagConfig.FuncMap.TryGetValue(sys, out string fv) ? fv : "GEN";
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.FUNC, func)) populated++;

                        string lvl = ParameterHelpers.GetLevelCode(doc, el);
                        if (lvl == "XX") lvl = "L00";
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.LVL, lvl)) populated++;

                        // Step 4: Auto-detect STATUS from Revit phases/worksets (guaranteed: "NEW")
                        string existingStatus = ParameterHelpers.GetString(el, ParamRegistry.STATUS);
                        if (string.IsNullOrEmpty(existingStatus))
                        {
                            string status = PhaseAutoDetect.DetectStatus(doc, el);
                            if (string.IsNullOrEmpty(status)) status = "NEW";
                            if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.STATUS, status))
                            {
                                statusDetected++;
                                populated++;
                            }
                        }

                        // Step 5: Auto-detect REV from project revision (guaranteed: "P01")
                        string rev = !string.IsNullOrEmpty(projectRev) ? projectRev : "P01";
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.REV, rev))
                        {
                            revSet++;
                            populated++;
                        }

                        // Step 6: Tag if not already complete (with collision detection)
                        if (TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                            existingTags: tagIndex, stats: stats))
                            tagged++;

                        // Step 7: Combine into ALL containers via ParamRegistry (single source of truth)
                        string[] tokenVals = ParamRegistry.ReadTokenValues(el);
                        combined += ParamRegistry.WriteContainers(el, tokenVals, catName,
                            overwrite: true, skipParam: ParamRegistry.TAG7);

                        // Step 7b: Write TAG7 + sub-sections (TAG7A-TAG7F) — rich descriptive narrative
                        combined += TagConfig.WriteTag7All(doc, el, catName, tokenVals, overwrite: true);
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
                report.AppendLine($"  REV auto-set:     {revSet} (revision '{projectRev}')");
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

            StingLog.Info($"TagAndCombine: scope={scopeLabel}, processed={totalProcessed}, " +
                $"populated={populated}, tagged={tagged}, combined={combined}, " +
                $"locDetect={locDetected}, zoneDetect={zoneDetected}, " +
                $"statusDetect={statusDetected}, revSet={revSet}, " +
                $"elapsed={sw.Elapsed.TotalSeconds:F1}s");

            return Result.Succeeded;
        }
    }
}
