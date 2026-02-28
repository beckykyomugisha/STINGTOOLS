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
    ///   1. Auto-detect LOC from project info / room data (eliminates SetLoc)
    ///   2. Auto-detect ZONE from room name patterns (eliminates SetZone)
    ///   3. Auto-populate tokens (DISC, PROD, SYS, FUNC, LVL) with family-aware PROD
    ///   4. Auto-tag all untagged elements (continues from existing sequence)
    ///   5. Combine parameters into ALL 53 containers (universal + discipline + MAT + FIN + ENV + STR + COMP + PERF + SUST + EQP)
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
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Step 0: Choose scope
            TaskDialog scopeDlg = new TaskDialog("Tag & Combine All");
            scopeDlg.MainInstruction = "Tag and populate all containers";
            scopeDlg.MainContent =
                "This will:\n" +
                "  1. Auto-detect LOC/ZONE from spatial data\n" +
                "  2. Auto-populate all tokens (DISC, PROD, SYS, FUNC, LVL)\n" +
                "  3. Tag all untagged elements (continuing from existing numbers)\n" +
                "  4. Combine tokens into ALL 53 tag containers\n\n" +
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
            var seqCounters = TagConfig.GetExistingSequenceCounters(doc);
            var tagIndex = TagConfig.BuildExistingTagIndex(doc);
            var sw = Stopwatch.StartNew();

            // Pre-build room spatial index for LOC/ZONE auto-detection
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);

            int populated = 0;
            int tagged = 0;
            int combined = 0;
            int totalProcessed = 0;
            int locDetected = 0;
            int zoneDetected = 0;

            using (Transaction tx = new Transaction(doc, "STING Tag & Combine All"))
            {
                tx.Start();

                foreach (ElementId id in targetIds)
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;

                    string catName = ParameterHelpers.GetCategoryName(el);
                    if (string.IsNullOrEmpty(catName) || !known.Contains(catName))
                        continue;

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
                    string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "XX";
                    if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.DISC, disc)) populated++;

                    // Family-aware PROD code: check family name before falling back to category
                    string prod = TagConfig.GetFamilyAwareProdCode(el, catName);
                    if (!string.IsNullOrEmpty(prod))
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.PROD, prod)) populated++;

                    // MEP system-aware SYS derivation (uses connected system name when available)
                    string sys = TagConfig.GetMepSystemAwareSysCode(el, catName);
                    if (!string.IsNullOrEmpty(sys))
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.SYS, sys)) populated++;
                    string func = TagConfig.GetFuncCode(sys);
                    if (!string.IsNullOrEmpty(func))
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.FUNC, func)) populated++;
                    string lvl = ParameterHelpers.GetLevelCode(doc, el);
                    if (lvl != "XX")
                        if (ParameterHelpers.SetIfEmpty(el, ParamRegistry.LVL, lvl)) populated++;

                    // Step 4: Tag if not already complete (with collision detection)
                    if (TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                        existingTags: tagIndex))
                        tagged++;

                    // Step 5: Combine into ALL containers (universal + discipline)
                    string[] tokenValues = ParamRegistry.ReadTokenValues(el);
                    if (tokenValues.Any(v => !string.IsNullOrEmpty(v)))
                    {
                        combined += ParamRegistry.WriteContainers(el, tokenValues, catName);
                    }
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
                report.AppendLine($"  LOC auto-detect:  {locDetected} (from rooms/project)");
            if (zoneDetected > 0)
                report.AppendLine($"  ZONE auto-detect: {zoneDetected} (from rooms)");
            report.AppendLine($"  Duration:         {sw.Elapsed.TotalSeconds:F1}s");

            TaskDialog td = new TaskDialog("Tag & Combine All");
            td.MainInstruction = $"Processed {totalProcessed} elements ({combined} containers)";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"TagAndCombine: scope={scopeLabel}, processed={totalProcessed}, " +
                $"populated={populated}, tagged={tagged}, combined={combined}, " +
                $"locDetect={locDetected}, zoneDetect={zoneDetected}, " +
                $"elapsed={sw.Elapsed.TotalSeconds:F1}s");

            return Result.Succeeded;
        }
    }
}
