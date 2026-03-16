using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// NF-02: Scans for duplicate TAG1 values and re-tags duplicates with
    /// next available SEQ numbers. Keeps the element with the lowest ElementId
    /// as the original; all others get new unique SEQ values.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RepairDuplicateSeqCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;

                // Collect all tagged elements grouped by TAG1
                var tagGroups = new Dictionary<string, List<Element>>();
                var cats = SharedParamGuids.AllCategoryEnums;
                var catSet = new List<BuiltInCategory>(cats);

                foreach (var bic in catSet)
                {
                    try
                    {
                        var collector = new FilteredElementCollector(doc)
                            .OfCategory(bic)
                            .WhereElementIsNotElementType();

                        foreach (Element el in collector)
                        {
                            string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                            if (string.IsNullOrEmpty(tag1)) continue;

                            if (!tagGroups.ContainsKey(tag1))
                                tagGroups[tag1] = new List<Element>();
                            tagGroups[tag1].Add(el);
                        }
                    }
                    catch { }
                }

                // Find duplicates
                var duplicates = tagGroups.Where(g => g.Value.Count > 1).ToList();
                if (duplicates.Count == 0)
                {
                    TaskDialog.Show("Repair Duplicates", "No duplicate tags found.");
                    return Result.Succeeded;
                }

                int totalDupes = duplicates.Sum(g => g.Value.Count - 1);
                var td = new TaskDialog("Repair Duplicates");
                td.MainContent = $"Found {duplicates.Count} duplicate tag value(s) " +
                    $"affecting {totalDupes} element(s).\n\n" +
                    "Elements with the lowest ElementId will keep their tag.\n" +
                    "All others will receive new unique SEQ numbers.";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Repair All Duplicates");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Cancel");
                td.CommonButtons = TaskDialogCommonButtons.None;

                var result = td.Show();
                if (result != TaskDialogResult.CommandLink1)
                    return Result.Cancelled;

                // Get existing tag index and SEQ counters
                var existingTags = TagConfig.BuildExistingTagIndex(doc);
                var seqCounters = TagConfig.GetExistingSequenceCounters(doc);
                int retagged = 0;

                using (var t = new Transaction(doc, "STING Repair Duplicate SEQ"))
                {
                    t.Start();

                    foreach (var group in duplicates)
                    {
                        // Sort by ElementId -- keep lowest
                        var sorted = group.Value
                            .OrderBy(e => e.Id.Value)
                            .ToList();

                        // Skip first (original), retag rest
                        for (int i = 1; i < sorted.Count; i++)
                        {
                            Element el = sorted[i];

                            // Re-derive tag with auto-increment
                            TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                                skipComplete: false, existingTags,
                                TagCollisionMode.AutoIncrement, null);
                            retagged++;
                        }
                    }

                    t.Commit();
                }

                TaskDialog.Show("Repair Duplicates",
                    $"Repaired {retagged} duplicate tag(s) across {duplicates.Count} group(s).");
                StingLog.Info($"RepairDuplicateSeq: repaired {retagged} duplicates in {duplicates.Count} groups");

                // Invalidate compliance cache
                ComplianceScan.InvalidateCache();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("RepairDuplicateSeqCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
