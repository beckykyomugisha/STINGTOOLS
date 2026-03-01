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
    /// Automatically applies ISO 19650 asset tags to all taggable elements in the active view.
    /// Assembles: DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ -> ASS_TAG_1_TXT.
    ///
    /// Intelligence layers:
    ///   1. Smart element ordering by Level -> Discipline -> Category
    ///   2. Pre-flight taggable/tagged/untagged counts shown in collision mode dialog
    ///   3. Spatial auto-detection for LOC/ZONE
    ///   4. MEP system-aware SYS derivation
    ///   5. O(1) collision detection with mode selection
    ///   6. Rich per-discipline/level/system reporting via TaggingStats
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

            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            var viewElements = new FilteredElementCollector(doc, activeView.Id)
                .WhereElementIsNotElementType()
                .ToList();

            // Intelligence Layer: detect relevant disciplines from view name/template/VG
            var relevantDiscs = TagConfig.GetViewRelevantDisciplines(activeView);
            string discFilterLabel = relevantDiscs != null
                ? string.Join(", ", relevantDiscs.OrderBy(x => x))
                : "ALL";

            // Pre-flight: count taggable, already-tagged, untagged
            int taggable = 0, alreadyTagged = 0, filteredOut = 0;
            var taggableElements = new List<Element>();
            foreach (Element e in viewElements)
            {
                string cat = ParameterHelpers.GetCategoryName(e);
                if (!known.Contains(cat)) continue;

                // Discipline-aware filtering: skip categories not relevant to this view
                if (relevantDiscs != null)
                {
                    string disc = TagConfig.DiscMap.TryGetValue(cat, out string dd) ? dd : "XX";
                    if (!relevantDiscs.Contains(disc))
                    {
                        filteredOut++;
                        continue;
                    }
                }

                taggable++;
                taggableElements.Add(e);
                if (TagConfig.TagIsComplete(ParameterHelpers.GetString(e, "ASS_TAG_1_TXT")))
                    alreadyTagged++;
            }

            if (taggable == 0)
            {
                string filterMsg = filteredOut > 0
                    ? $"\n({filteredOut} elements skipped — disciplines [{discFilterLabel}] active for this view)"
                    : "";
                TaskDialog.Show("Auto Tag", "No taggable elements in this view." + filterMsg);
                return Result.Succeeded;
            }

            int untagged = taggable - alreadyTagged;

            // Collision mode dialog with pre-flight counts
            TagCollisionMode collisionMode = TagCollisionMode.Skip;
            if (alreadyTagged > 0)
            {
                TaskDialog modeDlg = new TaskDialog("Auto Tag — Collision Mode");
                string filtInfo = filteredOut > 0 ? $" ({filteredOut} skipped by [{discFilterLabel}] filter)" : "";
                modeDlg.MainInstruction = $"{taggable} taggable, {alreadyTagged} tagged, {untagged} new{filtInfo}";
                modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"Skip existing — tag {untagged} new only",
                    "Only tag untagged elements in this view");
                modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    $"Overwrite all {taggable}",
                    "Re-derive and overwrite all tags including existing ones");
                modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "Auto-increment on collision",
                    "Tag untagged; auto-increment SEQ if collision found");
                modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                switch (modeDlg.Show())
                {
                    case TaskDialogResult.CommandLink1: collisionMode = TagCollisionMode.Skip; break;
                    case TaskDialogResult.CommandLink2: collisionMode = TagCollisionMode.Overwrite; break;
                    case TaskDialogResult.CommandLink3: collisionMode = TagCollisionMode.AutoIncrement; break;
                    default: return Result.Cancelled;
                }
            }

            // Smart sort for contiguous SEQ assignment
            var sorted = BatchTagCommand.SmartSortElements(doc, taggableElements);

            int populated = 0;
            var (tagIndex, sequenceCounters) = TagConfig.BuildTagIndexAndCounters(doc);
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);
            var stats = new TaggingStats();

            using (Transaction tx = new Transaction(doc, "STING Auto Tag"))
            {
                tx.Start();

                foreach (Element el in sorted)
                {
                    // Pre-populate LOC/ZONE from spatial data before tagging
                    if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, "ASS_LOC_TXT")))
                    {
                        string loc = SpatialAutoDetect.DetectLoc(doc, el, roomIndex, projectLoc);
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_LOC_TXT", loc)) populated++;
                    }
                    if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, "ASS_ZONE_TXT")))
                    {
                        string zone = SpatialAutoDetect.DetectZone(doc, el, roomIndex);
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_ZONE_TXT", zone)) populated++;
                    }

                    bool skipComplete = (collisionMode != TagCollisionMode.Overwrite);
                    TagConfig.BuildAndWriteTag(doc, el, sequenceCounters,
                        skipComplete: skipComplete,
                        existingTags: tagIndex,
                        collisionMode: collisionMode,
                        stats: stats);
                }

                tx.Commit();
            }

            var report = new StringBuilder();
            report.AppendLine($"Auto Tag — '{activeView.Name}'");
            report.AppendLine(new string('=', 50));
            report.AppendLine($"  Mode:       {collisionMode}");
            report.AppendLine($"  Disciplines: {discFilterLabel}");
            if (filteredOut > 0)
                report.AppendLine($"  Filtered:   {filteredOut} (wrong discipline for view)");
            report.AppendLine($"  LOC/ZONE:   {populated} auto-populated");
            report.AppendLine();
            report.Append(stats.BuildReport());

            TaskDialog td = new TaskDialog("Auto Tag");
            td.MainInstruction = $"Tagged {stats.TotalTagged} of {taggable} elements in '{activeView.Name}'";
            td.MainContent = report.ToString();
            td.Show();

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

            // Smart sort for contiguous SEQ
            var sorted = BatchTagCommand.SmartSortElements(doc, untagged);

            var (tagIndex, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);
            var stats = new TaggingStats();
            var sw = Stopwatch.StartNew();
            int populated = 0;

            using (Transaction tx = new Transaction(doc, "STING Tag New Only"))
            {
                tx.Start();

                foreach (Element el in sorted)
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

                    // SYS and FUNC (MEP system-aware)
                    string sys = TagConfig.GetMepSystemAwareSysCode(el, catName);
                    if (!string.IsNullOrEmpty(sys))
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_SYSTEM_TYPE_TXT", sys)) populated++;
                    string func = TagConfig.GetFuncCode(sys);
                    if (!string.IsNullOrEmpty(func))
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_FUNC_TXT", func)) populated++;

                    // LVL
                    string lvl = ParameterHelpers.GetLevelCode(doc, el);
                    if (lvl != "XX")
                        if (ParameterHelpers.SetIfEmpty(el, "ASS_LVL_COD_TXT", lvl)) populated++;

                    // Tag with collision detection and stats tracking
                    TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                        existingTags: tagIndex, stats: stats);
                }

                tx.Commit();
            }

            sw.Stop();

            var report = new StringBuilder();
            report.AppendLine($"Tag New Only — {untagged.Count} elements");
            report.AppendLine(new string('=', 50));
            report.AppendLine($"  Populated: {populated} token values");
            report.AppendLine($"  Duration:  {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine();
            report.Append(stats.BuildReport());

            TaskDialog td = new TaskDialog("Tag New Only");
            td.MainInstruction = $"Tagged {stats.TotalTagged} new elements";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"TagNewOnly: tagged={stats.TotalTagged}, populated={populated}, " +
                $"collisions={stats.TotalCollisions}, elapsed={sw.Elapsed.TotalSeconds:F1}s");

            return Result.Succeeded;
        }
    }
}
