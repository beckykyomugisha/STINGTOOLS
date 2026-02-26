using System;
using System.Collections.Generic;
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
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchTagCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            TaskDialog confirm = new TaskDialog("Batch Tag");
            confirm.MainInstruction = "Batch tag entire project?";
            confirm.MainContent =
                "This will apply ISO 19650 tags to all untagged elements " +
                "across the entire model. This may take a while for large projects.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();

            int tagged = 0;
            int skipped = 0;
            var sequenceCounters = TagConfig.GetExistingSequenceCounters(doc);

            using (Transaction tx = new Transaction(doc, "STING Batch Tag"))
            {
                tx.Start();

                foreach (Element el in collector)
                {
                    if (TagConfig.BuildAndWriteTag(doc, el, sequenceCounters))
                        tagged++;
                    else
                        skipped++;
                }

                tx.Commit();
            }

            TaskDialog.Show("Batch Tag",
                $"Batch tagging complete.\n\n" +
                $"  Tagged:   {tagged}\n" +
                $"  Skipped:  {skipped}\n" +
                $"  Total:    {tagged + skipped}");

            return Result.Succeeded;
        }
    }
}
