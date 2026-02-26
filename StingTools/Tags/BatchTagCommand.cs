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

            TaskDialog confirm = new TaskDialog("Batch Tag");
            confirm.MainInstruction = "Batch tag entire project?";
            confirm.MainContent =
                $"This will apply ISO 19650 tags to all untagged elements " +
                $"across the entire model.\n\n" +
                $"  Elements to process: {totalElements:N0}\n\n" +
                $"This may take a while for large projects.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            int tagged = 0;
            int skipped = 0;
            var sequenceCounters = TagConfig.GetExistingSequenceCounters(doc);
            var tagIndex = TagConfig.BuildExistingTagIndex(doc);
            int indexBefore = tagIndex.Count;
            var sw = Stopwatch.StartNew();

            StingLog.Info($"Batch Tag: starting — {totalElements} elements to process");

            using (Transaction tx = new Transaction(doc, "STING Batch Tag"))
            {
                tx.Start();

                int processed = 0;
                foreach (Element el in allElements)
                {
                    if (TagConfig.BuildAndWriteTag(doc, el, sequenceCounters,
                        existingTags: tagIndex))
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
