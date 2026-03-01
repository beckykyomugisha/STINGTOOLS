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
    /// Batch-apply ISO 19650 tags to ALL taggable elements in the entire project model.
    ///
    /// Intelligence layers:
    ///   1. Smart element ordering: groups by Level → Discipline → Category for contiguous
    ///      sequence numbers (all HVAC on L01 get consecutive SEQ before moving to L02)
    ///   2. Pre-flight validation: counts taggable/tagged/untagged before starting
    ///   3. Full 9-token auto-population via TokenAutoPopulator (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, STATUS, REV)
    ///   4. Phase-aware STATUS auto-detection from Revit phases/worksets
    ///   5. REV auto-population from project revision sequence
    ///   6. Family-aware PROD codes (35+ specific identifiers)
    ///   7. MEP system-aware SYS derivation from connected systems
    ///   8. O(1) collision detection with configurable resolution (Skip/Overwrite/AutoIncrement)
    ///   9. Rich post-batch reporting: per-discipline, per-level, collision depth stats
    ///  10. Progress logging every 500 elements for monitoring
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchTagCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            // Step 1: Pre-flight scan — collect and classify all elements
            var allElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToList();

            int totalTaggable = 0, alreadyTagged = 0, untagged = 0;
            var taggableElements = new List<Element>();

            foreach (Element e in allElements)
            {
                string cat = ParameterHelpers.GetCategoryName(e);
                if (!known.Contains(cat)) continue;
                totalTaggable++;
                taggableElements.Add(e);
                if (TagConfig.TagIsComplete(ParameterHelpers.GetString(e, ParamRegistry.TAG1)))
                    alreadyTagged++;
                else
                    untagged++;
            }

            // Step 2: Choose collision handling mode (with pre-flight counts)
            TaskDialog modeDlg = new TaskDialog("Batch Tag — Collision Mode");
            modeDlg.MainInstruction = $"Batch tag {totalTaggable:N0} elements";
            modeDlg.MainContent =
                $"  Taggable:       {totalTaggable:N0}\n" +
                $"  Already tagged: {alreadyTagged:N0}\n" +
                $"  Untagged:       {untagged:N0}\n\n" +
                "Choose collision handling:";
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Skip existing — tag {untagged:N0} new only",
                "Only tag untagged elements. Already-tagged elements are left unchanged.");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                $"Overwrite all {totalTaggable:N0}",
                "Re-derive and overwrite ALL tag tokens, even on already-tagged elements.");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                $"Auto-increment on collision",
                "Tag untagged elements; if a generated tag collides with an existing one, auto-increment SEQ.");
            modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            TagCollisionMode collisionMode;
            switch (modeDlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    collisionMode = TagCollisionMode.Skip;
                    break;
                case TaskDialogResult.CommandLink2:
                    collisionMode = TagCollisionMode.Overwrite;
                    break;
                case TaskDialogResult.CommandLink3:
                    collisionMode = TagCollisionMode.AutoIncrement;
                    break;
                default:
                    return Result.Cancelled;
            }

            // Step 3: Smart ordering — sort by Level → Discipline → Category
            // This ensures contiguous SEQ numbers per group (all HVAC on L01 together)
            var sorted = SmartSortElements(doc, taggableElements);

            var sequenceCounters = TagConfig.GetExistingSequenceCounters(doc);
            var tagIndex = TagConfig.BuildExistingTagIndex(doc);
            var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
            var stats = new TaggingStats();
            var sw = Stopwatch.StartNew();
            int populated = 0;
            int statusDetected = 0, revSet = 0;

            StingLog.Info($"Batch Tag: starting — {totalTaggable} taggable, {alreadyTagged} tagged, mode={collisionMode}");

            using (Transaction tx = new Transaction(doc, "STING Batch Tag"))
            {
                tx.Start();

                int processed = 0;
                foreach (Element el in sorted)
                {
                    try
                    {
                        // Full 9-token auto-population via shared helper
                        bool overwriteMode = (collisionMode == TagCollisionMode.Overwrite);
                        var popResult = TokenAutoPopulator.PopulateAll(doc, el, popCtx,
                            overwrite: overwriteMode);
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
                        StingLog.Error($"BatchTag: failed on element {el?.Id}: {ex.Message}", ex);
                        stats.RecordWarning($"Error on element {el?.Id}: {ex.Message}");
                    }

                    processed++;
                    if (processed % 500 == 0)
                        StingLog.Info($"Batch Tag progress: {processed}/{totalTaggable} " +
                            $"({stats.TotalTagged} tagged, {stats.TotalCollisions} collisions)");
                }

                tx.Commit();
            }

            sw.Stop();

            // Step 4: Rich reporting
            var report = new StringBuilder();
            report.AppendLine("Batch Tagging Complete");
            report.AppendLine(new string('=', 50));
            report.AppendLine($"  Mode:         {collisionMode}");
            report.AppendLine($"  Tokens:       {populated} auto-populated");
            if (statusDetected > 0)
                report.AppendLine($"  STATUS:       {statusDetected} (from Revit phases/worksets)");
            if (revSet > 0)
                report.AppendLine($"  REV:          {revSet} (revision '{popCtx.ProjectRev}')");
            report.AppendLine($"  Duration:     {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine();
            report.Append(stats.BuildReport());

            StingLog.Info($"Batch Tag: tagged={stats.TotalTagged}, skipped={stats.TotalSkipped}, " +
                $"collisions={stats.TotalCollisions}, populated={populated}, " +
                $"statusDetect={statusDetected}, revSet={revSet}, " +
                $"elapsed={sw.Elapsed.TotalSeconds:F1}s");

            TaskDialog td = new TaskDialog("Batch Tag");
            td.MainInstruction = $"Tagged {stats.TotalTagged:N0} of {totalTaggable:N0} elements";
            td.MainContent = report.ToString();
            td.Show();

            return Result.Succeeded;
        }

        /// <summary>
        /// Smart sort: Level (elevation ascending) -> Discipline -> SYS -> Category.
        /// Ensures contiguous sequence numbers within each group — all HVAC on L01
        /// get SEQ 0001-0050 before moving to DCW on L01, then L02.
        /// The SYS sort key groups elements by system type within each discipline,
        /// matching the SEQ key format (DISC_SYS_LVL) for optimal numbering.
        /// </summary>
        internal static List<Element> SmartSortElements(Document doc, List<Element> elements)
        {
            // Build level elevation lookup
            var levelElevation = new Dictionary<ElementId, double>();
            foreach (Level lvl in new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>())
            {
                levelElevation[lvl.Id] = lvl.Elevation;
            }

            return elements.OrderBy(e =>
                {
                    ElementId lvlId = e.LevelId;
                    if (lvlId != null && lvlId != ElementId.InvalidElementId &&
                        levelElevation.TryGetValue(lvlId, out double elev))
                        return elev;
                    return double.MaxValue;
                })
                .ThenBy(e =>
                {
                    string cat = ParameterHelpers.GetCategoryName(e);
                    return TagConfig.DiscMap.TryGetValue(cat, out string d) ? d : "A";
                })
                .ThenBy(e =>
                {
                    // SYS sort key: groups elements by ACTUAL system within discipline
                    // Uses MEP-aware detection so pipes group by DCW/HWS/SAN/GAS
                    string cat = ParameterHelpers.GetCategoryName(e);
                    string disc = TagConfig.DiscMap.TryGetValue(cat, out string d) ? d : "A";
                    string sys = TagConfig.GetMepSystemAwareSysCode(e, cat);
                    return !string.IsNullOrEmpty(sys) ? sys : TagConfig.GetDiscDefaultSysCode(disc);
                })
                .ThenBy(e => ParameterHelpers.GetCategoryName(e))
                .ThenBy(e => e.Id.Value) // Stable sort: consistent ordering across runs
                .ToList();
        }
    }
}
