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
    /// Repairs duplicate SEQ numbers across the project by scanning all tagged elements,
    /// identifying duplicates, and auto-incrementing SEQ to resolve collisions.
    /// Writes TAG7 + containers after repair to maintain pipeline consistency.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RepairDuplicateSeqCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Build tag index and sequence counters
            var (existingTags, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);

            // Collect all tagged elements and find duplicates
            var catEnums = SharedParamGuids.AllCategoryEnums;
            IEnumerable<Element> allElements;
            if (catEnums != null && catEnums.Length > 0)
                allElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));
            else
                allElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType();

            // Group elements by their TAG1 value to find duplicates
            var tagMap = new Dictionary<string, List<Element>>();
            foreach (Element el in allElements)
            {
                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (string.IsNullOrEmpty(tag) || !TagConfig.TagIsComplete(tag)) continue;

                if (!tagMap.TryGetValue(tag, out var list))
                {
                    list = new List<Element>();
                    tagMap[tag] = list;
                }
                list.Add(el);
            }

            var duplicates = tagMap.Where(kvp => kvp.Value.Count > 1).ToList();
            if (duplicates.Count == 0)
            {
                TaskDialog.Show("Repair Duplicate SEQ", "No duplicate tags found.");
                return Result.Succeeded;
            }

            int totalDupes = duplicates.Sum(d => d.Value.Count - 1);
            TaskDialog confirm = new TaskDialog("Repair Duplicate SEQ");
            confirm.MainInstruction = $"Found {duplicates.Count} duplicate tag groups ({totalDupes} elements to repair).";
            confirm.MainContent = "The first occurrence of each duplicate will be kept; subsequent elements will receive new SEQ numbers.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            int retagged = 0;
            // FIX-05: Build population context for pre-enrichment before SEQ repair
            var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
            // FIX-R04: Load formulas and grid lines for pipeline completeness
            var rdFormulas = TagPipelineHelper.LoadFormulas();
            var rdGridLines = TagPipelineHelper.LoadGridLines(doc);
            using (Transaction tx = new Transaction(doc, "STING Repair Duplicate SEQ"))
            {
                tx.Start();
                foreach (var kvp in duplicates)
                {
                    // Keep first, retag the rest
                    for (int i = 1; i < kvp.Value.Count; i++)
                    {
                        Element el = kvp.Value[i];
                        try
                        {
                            // Route the re-tagging step through the canonical pipeline so all
                            // 11 steps (skip list, audit trail, type inherit, locked-token
                            // snapshot, PopulateAll, force-SYS, token overrides, native mapper
                            // with cached RoomIndex, type-cached formulas, BuildAndWriteTag
                            // which writes containers internally, TAG7, and GridRef) run
                            // consistently. AutoIncrement assigns a new collision-free SEQ;
                            // overwrite=false preserves the already-correct non-SEQ tokens.
                            bool pipelineOk = TagPipelineHelper.RunFullPipeline(
                                doc, el, popCtx, existingTags, seqCounters,
                                rdFormulas, rdGridLines,
                                overwrite: false,
                                skipComplete: false,
                                collisionMode: TagCollisionMode.AutoIncrement);
                            if (pipelineOk) retagged++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"RepairDuplicateSeq failed for {el.Id}: {ex.Message}");
                        }
                    }
                }
                tx.Commit();
            }

            // FIX-05: Save SEQ sidecar + invalidate caches after repair
            try { TagConfig.SaveSeqSidecar(doc, seqCounters); }
            catch (Exception ssEx) { StingLog.Warn($"RepairDuplicateSeq SaveSeqSidecar: {ssEx.Message}"); }
            ComplianceScan.InvalidateCache();
            StingAutoTagger.InvalidateContext();
            TagConfig.CheckComplianceGate(doc, "RepairDuplicateSeq");

            TaskDialog.Show("Repair Duplicate SEQ",
                $"Repaired {retagged} elements across {duplicates.Count} duplicate groups.");
            // Phase 165 follow-up — explicit batch teardown.
            TokenAutoPopulator.PopulationContext.EndSession();
            return Result.Succeeded;
        }
    }
}
