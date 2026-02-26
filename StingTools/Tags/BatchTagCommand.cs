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
    /// Batch-apply ISO 19650 tags to ALL taggable elements in the entire project model.
    /// Uses TagConfig.BuildAndWriteTag for shared tag-building logic.
    /// Continues sequence numbering from the highest existing numbers in the project.
    /// Includes progress reporting and collision detection.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchTagCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            // Count elements upfront so the user knows the scope
            var allElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToList();
            int totalElements = allElements.Count;

            // Step 1: Choose collision handling mode
            TaskDialog modeDlg = new TaskDialog("Batch Tag — Collision Mode");
            modeDlg.MainInstruction = $"Batch tag {totalElements:N0} elements — choose collision handling:";
            modeDlg.MainContent =
                "When an element already has a complete tag, how should it be handled?";
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Skip existing (default)",
                "Only tag untagged elements. Already-tagged elements are left unchanged.");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Overwrite all",
                "Re-derive and overwrite ALL tag tokens, even on already-tagged elements.");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Auto-increment on collision",
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

            int tagged = 0;
            int skipped = 0;
            var sequenceCounters = TagConfig.GetExistingSequenceCounters(doc);
            var tagIndex = TagConfig.BuildExistingTagIndex(doc);
            int indexBefore = tagIndex.Count;
            var sw = Stopwatch.StartNew();

            // Pre-build spatial index for LOC/ZONE auto-detection
            var roomIndex = SpatialAutoDetect.BuildRoomIndex(doc);
            string projectLoc = SpatialAutoDetect.DetectProjectLoc(doc);
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            int populated = 0;

            StingLog.Info($"Batch Tag: starting — {totalElements} elements to process, mode={collisionMode}");

            using (Transaction tx = new Transaction(doc, "STING Batch Tag"))
            {
                tx.Start();

                int processed = 0;
                foreach (Element el in allElements)
                {
                    string catName = ParameterHelpers.GetCategoryName(el);

                    // Pre-populate LOC/ZONE from spatial data before tagging
                    if (known.Contains(catName))
                    {
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
                    }

                    bool skipComplete = (collisionMode != TagCollisionMode.Overwrite);
                    if (TagConfig.BuildAndWriteTag(doc, el, sequenceCounters,
                        skipComplete: skipComplete,
                        existingTags: tagIndex,
                        collisionMode: collisionMode))
                        tagged++;
                    else
                        skipped++;

                    processed++;
                    if (processed % 500 == 0)
                        StingLog.Info($"Batch Tag progress: {processed}/{totalElements} ({tagged} tagged)");
                }

                tx.Commit();
            }

            sw.Stop();
            int collisionsResolved = tagIndex.Count - indexBefore - tagged;
            if (collisionsResolved < 0) collisionsResolved = 0;

            string report = $"Batch tagging complete.\n\n" +
                $"  Tagged:     {tagged:N0}\n" +
                $"  Skipped:    {skipped:N0}\n" +
                $"  Total:      {totalElements:N0}\n" +
                $"  LOC/ZONE:   {populated} auto-populated\n" +
                $"  Mode:       {collisionMode}\n" +
                $"  Duration:   {sw.Elapsed.TotalSeconds:F1}s";
            if (collisionsResolved > 0)
                report += $"\n  Collisions: {collisionsResolved} resolved (SEQ auto-incremented)";

            StingLog.Info($"Batch Tag: tagged={tagged}, skipped={skipped}, " +
                $"collisions={collisionsResolved}, elapsed={sw.Elapsed.TotalSeconds:F1}s");
            TaskDialog.Show("Batch Tag", report);

            return Result.Succeeded;
        }
    }
}
