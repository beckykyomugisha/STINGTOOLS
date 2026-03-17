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
                            // FIX-05: Pre-enrich tokens before rebuild to ensure
                            // spatial/system data is current before SEQ reassignment
                            TokenAutoPopulator.TypeTokenInherit(doc, el);
                            if (popCtx != null)
                                TokenAutoPopulator.PopulateAll(doc, el, popCtx, overwrite: false);
                            try { NativeParamMapper.MapAll(doc, el); }
                            catch (Exception nmEx) { StingLog.Warn($"RepairDuplicateSeq NativeMapper for {el.Id}: {nmEx.Message}"); }

                            // FIX-R04: Evaluate formulas after native mapper
                            if (rdFormulas != null && rdFormulas.Count > 0)
                            {
                                try
                                {
                                    foreach (var formula in rdFormulas)
                                    {
                                        Parameter fp = el.LookupParameter(formula.ParameterName);
                                        if (fp == null || fp.IsReadOnly) continue;
                                        var fCtx = Temp.FormulaEngine.BuildContext(el, formula);
                                        if (fCtx == null) continue;
                                        if (formula.DataType == "TEXT")
                                        {
                                            string fResult = Temp.FormulaEngine.EvaluateText(formula.Expression, fCtx);
                                            if (fResult != null && fp.StorageType == StorageType.String)
                                                fp.Set(fResult);
                                        }
                                        else
                                        {
                                            double? fResult = Temp.FormulaEngine.EvaluateNumeric(formula.Expression, fCtx);
                                            if (fResult.HasValue && !double.IsNaN(fResult.Value) && !double.IsInfinity(fResult.Value))
                                                Temp.FormulaEngine.WriteNumericResult(fp, fResult.Value);
                                        }
                                    }
                                }
                                catch (Exception fEx) { StingLog.Warn($"RepairDuplicateSeq formula eval for {el.Id}: {fEx.Message}"); }
                            }

                            TagConfig.BuildAndWriteTag(doc, el, seqCounters,
                                skipComplete: false, existingTags,
                                TagCollisionMode.AutoIncrement, null);

                            // NP5: Write TAG7 + containers after SEQ repair
                            try
                            {
                                string repairCat = ParameterHelpers.GetCategoryName(el);
                                string[] repairToks = ParamRegistry.ReadTokenValues(el);
                                TagConfig.WriteTag7All(doc, el, repairCat, repairToks, overwrite: true);
                                ParamRegistry.WriteContainers(el, repairToks, repairCat, overwrite: true,
                                    skipParam: ParamRegistry.TAG1);
                            }
                            catch (Exception repEx)
                            {
                                StingLog.Warn($"RepairDuplicateSeq TAG7+containers for {el.Id}: {repEx.Message}");
                            }

                            // FIX-R04: Write GridRef per element
                            if (rdGridLines != null && rdGridLines.Count > 0)
                            {
                                try { SpatialAutoDetect.GetGridRef(el, rdGridLines); }
                                catch (Exception grEx) { StingLog.Warn($"RepairDuplicateSeq GridRef for {el.Id}: {grEx.Message}"); }
                            }
                            retagged++;
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

            TaskDialog.Show("Repair Duplicate SEQ",
                $"Repaired {retagged} elements across {duplicates.Count} duplicate groups.");
            return Result.Succeeded;
        }
    }
}
