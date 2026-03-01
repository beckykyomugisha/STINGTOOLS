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
    ///   3. Full 9-token auto-population via TokenAutoPopulator (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, STATUS, REV)
    ///   4. MEP system-aware SYS derivation
    ///   5. Phase-aware STATUS auto-detection from Revit phases/worksets
    ///   6. REV auto-population from project revision sequence
    ///   7. O(1) collision detection with mode selection
    ///   8. Rich per-discipline/level/system reporting via TaggingStats
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
                if (TagConfig.TagIsComplete(ParameterHelpers.GetString(e, ParamRegistry.TAG1)))
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
            int statusDetected = 0, revSet = 0;
            var sequenceCounters = TagConfig.GetExistingSequenceCounters(doc);
            var tagIndex = TagConfig.BuildExistingTagIndex(doc);
            var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
            var stats = new TaggingStats();

            using (Transaction tx = new Transaction(doc, "STING Auto Tag"))
            {
                tx.Start();

                foreach (Element el in sorted)
                {
                    try
                    {
                        // Full 9-token auto-population via shared helper
                        var popResult = TokenAutoPopulator.PopulateAll(doc, el, popCtx,
                            overwrite: collisionMode == TagCollisionMode.Overwrite);
                        populated += popResult.TokensSet;
                        if (popResult.StatusDetected) statusDetected++;
                        if (popResult.RevSet) revSet++;

                        bool skipComplete = (collisionMode != TagCollisionMode.Overwrite);
                        TagConfig.BuildAndWriteTag(doc, el, sequenceCounters,
                            skipComplete: skipComplete,
                            existingTags: tagIndex,
                            collisionMode: collisionMode,
                            stats: stats);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"AutoTag: failed on element {el?.Id}: {ex.Message}", ex);
                        stats.RecordWarning($"Error on element {el?.Id}: {ex.Message}");
                    }
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
            report.AppendLine($"  Tokens:     {populated} auto-populated");
            if (statusDetected > 0)
                report.AppendLine($"  STATUS:     {statusDetected} (from Revit phases/worksets)");
            if (revSet > 0)
                report.AppendLine($"  REV:        {revSet} (revision '{popCtx.ProjectRev}')");
            report.AppendLine();
            report.Append(stats.BuildReport());

            TaskDialog td = new TaskDialog("Auto Tag");
            td.MainInstruction = $"Tagged {stats.TotalTagged} of {taggable} elements in '{activeView.Name}'";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"AutoTag: view='{activeView.Name}', tagged={stats.TotalTagged}, " +
                $"skipped={stats.TotalSkipped}, collisions={stats.TotalCollisions}, " +
                $"populated={populated}, statusDetect={statusDetected}, revSet={revSet}, " +
                $"mode={collisionMode}");

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Tag only NEW (untagged) elements in the project. Unlike BatchTag which processes
    /// all elements, this command pre-filters to only elements with empty ASS_TAG_1_TXT,
    /// making it much faster for incremental tagging after adding new elements.
    /// Auto-populates all 9 tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD/STATUS/REV)
    /// via TokenAutoPopulator, then assigns SEQ and builds tags.
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

                string existingTag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
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

            var seqCounters = TagConfig.GetExistingSequenceCounters(doc);
            var tagIndex = TagConfig.BuildExistingTagIndex(doc);
            var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
            var stats = new TaggingStats();
            var sw = Stopwatch.StartNew();
            int populated = 0;
            int statusDetected = 0, revSet = 0;

            using (Transaction tx = new Transaction(doc, "STING Tag New Only"))
            {
                tx.Start();

                foreach (Element el in sorted)
                {
                    try
                    {
                        // Full 9-token auto-population via shared helper
                        var popResult = TokenAutoPopulator.PopulateAll(doc, el, popCtx);
                        populated += popResult.TokensSet;
                        if (popResult.StatusDetected) statusDetected++;
                        if (popResult.RevSet) revSet++;

                        // Tag with collision detection and stats tracking
                        TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                            existingTags: tagIndex, stats: stats);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"TagNewOnly: failed on element {el?.Id}: {ex.Message}", ex);
                        stats.RecordWarning($"Error on element {el?.Id}: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            sw.Stop();

            var report = new StringBuilder();
            report.AppendLine($"Tag New Only — {untagged.Count} elements");
            report.AppendLine(new string('=', 50));
            report.AppendLine($"  Tokens:    {populated} auto-populated");
            if (statusDetected > 0)
                report.AppendLine($"  STATUS:    {statusDetected} (from Revit phases/worksets)");
            if (revSet > 0)
                report.AppendLine($"  REV:       {revSet} (revision '{popCtx.ProjectRev}')");
            report.AppendLine($"  Duration:  {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine();
            report.Append(stats.BuildReport());

            TaskDialog td = new TaskDialog("Tag New Only");
            td.MainInstruction = $"Tagged {stats.TotalTagged} new elements";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"TagNewOnly: tagged={stats.TotalTagged}, populated={populated}, " +
                $"statusDetect={statusDetected}, revSet={revSet}, " +
                $"collisions={stats.TotalCollisions}, elapsed={sw.Elapsed.TotalSeconds:F1}s");

            return Result.Succeeded;
        }
    }
}
