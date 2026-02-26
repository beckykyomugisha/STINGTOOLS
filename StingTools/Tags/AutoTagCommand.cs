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
}
