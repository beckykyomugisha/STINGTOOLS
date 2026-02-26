using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Automatically applies ISO 19650 asset tags to all taggable elements in the active view.
    /// Assembles: DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ → ASS_TAG_1_TXT.
    /// Uses TagConfig.BuildAndWriteTag for shared tag-building logic.
    /// Continues sequence numbering from the highest existing numbers in the project.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoTagCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            if (activeView == null)
            {
                TaskDialog.Show("Auto Tag", "No active view.");
                return Result.Failed;
            }

            var collector = new FilteredElementCollector(doc, activeView.Id)
                .WhereElementIsNotElementType();

            int tagged = 0;
            int skipped = 0;
            var sequenceCounters = TagConfig.GetExistingSequenceCounters(doc);
            var tagIndex = TagConfig.BuildExistingTagIndex(doc);

            using (Transaction tx = new Transaction(doc, "STING Auto Tag"))
            {
                tx.Start();

                foreach (Element el in collector)
                {
                    if (TagConfig.BuildAndWriteTag(doc, el, sequenceCounters,
                        existingTags: tagIndex))
                        tagged++;
                    else
                        skipped++;
                }

                tx.Commit();
            }

            TaskDialog.Show("Auto Tag",
                $"Tagged {tagged} elements in '{activeView.Name}'.\n" +
                $"Skipped {skipped} (already tagged or unsupported category).");

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Tag only NEW (untagged) elements in the project. Unlike BatchTag which processes
    /// all elements, this command pre-filters to only elements with empty ASS_TAG_1_TXT,
    /// making it much faster for incremental tagging after adding new elements.
    /// Also auto-populates LOC/ZONE from spatial data and uses family-aware PROD codes.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagNewOnlyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            // Pre-filter: only elements with empty ASS_TAG_1_TXT
            var untagged = new List<Element>();
            foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!known.Contains(cat)) continue;

                string existingTag = ParameterHelpers.GetString(el, "ASS_TAG_1_TXT");
                if (string.IsNullOrEmpty(existingTag))
                    untagged.Add(el);
            }

            if (untagged.Count == 0)
            {
                TaskDialog.Show("Tag New Only",
                    "All taggable elements already have tags.\nNo new elements to tag.");
                return Result.Succeeded;
            }

            TaskDialog confirm = new TaskDialog("Tag New Only");
            confirm.MainInstruction = $"Tag {untagged.Count} new elements?";
            confirm.MainContent =
                $"Found {untagged.Count} taggable elements without tags.\n" +
                "This will auto-populate tokens and assign tags to only these elements.\n" +
                "Existing tags will not be modified.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            var seqCounters = TagConfig.GetExistingSequenceCounters(doc);
            var tagIndex = TagConfig.BuildExistingTagIndex(doc);
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);
            var sw = Stopwatch.StartNew();

            int tagged = 0;
            int populated = 0;

            using (Transaction tx = new Transaction(doc, "STING Tag New Only"))
            {
                tx.Start();

                foreach (Element el in untagged)
                {
                    string catName = ParameterHelpers.GetCategoryName(el);

                    // Auto-populate LOC from spatial data
                    if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, "ASS_LOC_TXT")))
                    {
                        string loc = SpatialAutoDetect.DetectLoc(doc, el, roomIndex, projectLoc);
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_LOC_TXT", loc)) populated++;
                    }

                    // Auto-populate ZONE from room data
                    if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, "ASS_ZONE_TXT")))
                    {
                        string zone = SpatialAutoDetect.DetectZone(doc, el, roomIndex);
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_ZONE_TXT", zone)) populated++;
                    }

                    // Auto-populate DISC
                    string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "XX";
                    if (ParameterHelpers.SetIfEmpty(el, "ASS_DISCIPLINE_COD_TXT", disc)) populated++;

                    // Family-aware PROD code
                    string prod = TagConfig.GetFamilyAwareProdCode(el, catName);
                    if (ParameterHelpers.SetIfEmpty(el, "ASS_PRODCT_COD_TXT", prod)) populated++;

                    // SYS and FUNC
                    string sys = TagConfig.GetSysCode(catName);
                    if (!string.IsNullOrEmpty(sys))
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_SYSTEM_TYPE_TXT", sys)) populated++;
                    string func = TagConfig.GetFuncCode(sys);
                    if (!string.IsNullOrEmpty(func))
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_FUNC_TXT", func)) populated++;

                    // LVL
                    string lvl = ParameterHelpers.GetLevelCode(doc, el);
                    if (lvl != "XX")
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_LVL_COD_TXT", lvl)) populated++;

                    // Tag with collision detection
                    if (TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                        existingTags: tagIndex))
                        tagged++;
                }

                tx.Commit();
            }

            sw.Stop();

            TaskDialog.Show("Tag New Only",
                $"Tagged {tagged} new elements.\n" +
                $"Auto-populated {populated} token values.\n" +
                $"Duration: {sw.Elapsed.TotalSeconds:F1}s");

            StingLog.Info($"TagNewOnly: tagged={tagged}, populated={populated}, " +
                $"elapsed={sw.Elapsed.TotalSeconds:F1}s");

            return Result.Succeeded;
        }
    }
}
