using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

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
            try { return ExecuteCore(commandData, ref message, elements); }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("AutoTagCommand crashed", ex);
                try { TaskDialog.Show("STING Tools", $"Auto Tag failed:\n{ex.Message}"); } catch (Exception dlgEx) { StingLog.Warn($"TaskDialog fallback: {dlgEx.Message}"); }
                return Result.Failed;
            }
        }

        private Result ExecuteCore(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            if (ctx.ActiveView == null) { TaskDialog.Show("STING", "No active view."); return Result.Failed; }
            UIDocument uidoc = ctx.UIDoc; Document doc = ctx.Doc;
            View activeView = ctx.ActiveView;

            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            // Performance: use ElementMulticategoryFilter to skip non-taggable elements at API level
            var catEnums = SharedParamGuids.AllCategoryEnums;
            var collector = new FilteredElementCollector(doc, activeView.Id)
                .WhereElementIsNotElementType();
            if (catEnums != null && catEnums.Length > 0)
                collector.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));
            var viewElements = collector.ToList();

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
                if (string.IsNullOrEmpty(cat) || !known.Contains(cat)) continue;

                // Discipline-aware filtering: skip categories not relevant to this view
                if (relevantDiscs != null)
                {
                    string disc = TagConfig.DiscMap.TryGetValue(cat, out string dd) ? dd : "A";
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
                string filtInfo = filteredOut > 0 ? $" ({filteredOut} skipped by [{discFilterLabel}] filter)" : "";
                var modeOptions = new List<UI.StingModePicker.ModeOption>
                {
                    new($"Skip existing — tag {untagged} new only",
                        "Only tag untagged elements in this view", "skip", true),
                    new($"Overwrite all {taggable}",
                        "Re-derive and overwrite all tags including existing ones", "overwrite"),
                    new("Auto-increment on collision",
                        "Tag untagged; auto-increment SEQ if collision found", "increment"),
                };
                string modeResult = UI.StingModePicker.Show(
                    "Auto Tag — Collision Mode",
                    $"{taggable} taggable, {alreadyTagged} tagged, {untagged} new{filtInfo}",
                    modeOptions);

                if (modeResult == null) return Result.Cancelled;
                collisionMode = modeResult switch
                {
                    "overwrite" => TagCollisionMode.Overwrite,
                    "increment" => TagCollisionMode.AutoIncrement,
                    _ => TagCollisionMode.Skip,
                };
            }

            // GAP-020: Pre-flight audit trail log
            StingLog.Info($"AutoTag pre-flight: {taggable} taggable, {alreadyTagged} tagged, {untagged} new, mode={collisionMode}");

            // Smart sort for contiguous SEQ assignment
            var sorted = BatchTagCommand.SmartSortElements(doc, taggableElements);

            var (tagIndex, sequenceCounters) = TagConfig.BuildTagIndexAndCounters(doc);
            var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
            if (popCtx == null)
            {
                TaskDialog.Show("Auto Tag", "Failed to build population context.");
                return Result.Failed;
            }
            var formulas = TagPipelineHelper.LoadFormulas();
            var gridLines = TagPipelineHelper.LoadGridLines(doc);
            var stats = new TaggingStats();

            bool cancelled = false;
            var progress = StingProgressDialog.Show("Auto Tag", taggable);

            using (Transaction tx = new Transaction(doc, "STING Auto Tag"))
            {
                tx.Start();

                int processed = 0;
                foreach (Element el in sorted)
                {
                    if (progress.IsCancelled)
                    {
                        StingLog.Info($"AutoTag: cancelled by user at {processed}/{taggable}");
                        cancelled = true;
                        break;
                    }
                    processed++;

                    try
                    {
                        bool skipComplete = (collisionMode != TagCollisionMode.Overwrite);
                        bool ow = (collisionMode == TagCollisionMode.Overwrite);
                        bool pipelineOk = TagPipelineHelper.RunFullPipeline(doc, el, popCtx,
                            tagIndex, sequenceCounters, formulas, gridLines,
                            overwrite: ow, skipComplete: skipComplete,
                            collisionMode: collisionMode, stats: stats);
                        if (!pipelineOk)
                            StingLog.Warn($"AutoTag: pipeline returned false for element {el?.Id}");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"AutoTag: failed on element {el?.Id}: {ex.Message}", ex);
                        stats.RecordWarning($"Error on element {el?.Id}: {ex.Message}");
                    }

                    progress.Increment($"Tagging element {processed}/{taggable}");
                }

                progress.Close();

                if (cancelled)
                {
                    tx.RollBack();
                    TaskDialog.Show("Auto Tag", $"Cancelled by user at {processed}/{taggable}.\nAll changes rolled back.");
                    return Result.Cancelled;
                }

                tx.Commit();
            }
            TagPipelineHelper.PostTagCleanup(doc, sequenceCounters, "AutoTag");
            var report = new StringBuilder();
            report.AppendLine($"Auto Tag — '{activeView.Name}'");
            report.AppendLine(new string('=', 50));
            report.AppendLine($"  Mode:       {collisionMode}");
            report.AppendLine($"  Disciplines: {discFilterLabel}");
            if (filteredOut > 0)
                report.AppendLine($"  Filtered:   {filteredOut} (wrong discipline for view)");
            report.AppendLine();

            // TAG-07: Warn when >10% of FUNC or PROD tokens are empty after tagging
            if (stats.TotalTagged > 0)
            {
                int emptyFunc = 0, emptyProd = 0;
                foreach (Element el in viewElements)
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (!known.Contains(cat)) continue;
                    if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.FUNC))) emptyFunc++;
                    if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.PROD))) emptyProd++;
                }
                int pctFunc = emptyFunc * 100 / taggable;
                int pctProd = emptyProd * 100 / taggable;
                if (pctFunc > 10)
                    report.AppendLine($"  WARNING: {emptyFunc} elements ({pctFunc}%) missing FUNC codes — run FamilyStagePopulate");
                if (pctProd > 10)
                    report.AppendLine($"  WARNING: {emptyProd} elements ({pctProd}%) missing PROD codes — run FamilyStagePopulate");
            }

            report.Append(stats.BuildReport());

            TaskDialog td = new TaskDialog("Auto Tag");
            td.MainInstruction = $"Tagged {stats.TotalTagged} of {taggable} elements in '{activeView.Name}'";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"AutoTag: view='{activeView.Name}', tagged={stats.TotalTagged}, " +
                $"skipped={stats.TotalSkipped}, collisions={stats.TotalCollisions}, " +
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
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            // FIX-08: Scope selection — active view or entire project
            var scopeDlg = new TaskDialog("Tag New Only — Scope");
            scopeDlg.MainInstruction = "Select scope for tagging new elements";
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Active view only",
                "Tag untagged elements visible in the current view");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Entire project",
                "Tag all untagged elements across the entire model");
            scopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            scopeDlg.DefaultButton = TaskDialogResult.CommandLink1;
            var scopeResult = scopeDlg.Show();
            if (scopeResult == TaskDialogResult.Cancel)
                return Result.Cancelled;
            bool viewScopeOnly = (scopeResult == TaskDialogResult.CommandLink1);
            string scopeLabel = viewScopeOnly ? "Active View" : "Entire Project";

            // Pre-filter: only elements with empty ASS_TAG_1_TXT
            // Performance: use ElementMulticategoryFilter to skip non-taggable elements at API level
            var catEnums = SharedParamGuids.AllCategoryEnums;
            FilteredElementCollector tagNewCollector;
            if (viewScopeOnly && doc.ActiveView != null)
                tagNewCollector = new FilteredElementCollector(doc, doc.ActiveView.Id).WhereElementIsNotElementType();
            else
                tagNewCollector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            if (catEnums != null && catEnums.Length > 0)
                tagNewCollector.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));
            var untagged = new List<Element>();
            foreach (Element el in tagNewCollector)
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (string.IsNullOrEmpty(cat))
                {
                    StingLog.Warn($"TagNewOnly: skipping element {el?.Id} — null/empty category");
                    continue;
                }
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
                $"Scope: {scopeLabel}\n" +
                $"Found {untagged.Count} taggable elements without tags.\n" +
                "This will auto-populate tokens and assign tags to only these elements.\n" +
                "Existing tags will not be modified.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            // Smart sort for contiguous SEQ
            var sorted = BatchTagCommand.SmartSortElements(doc, untagged);

            var (tagIndex, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
            var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
            if (popCtx == null)
            {
                TaskDialog.Show("Tag New Only", "Failed to build population context.");
                return Result.Failed;
            }
            var formulas = TagPipelineHelper.LoadFormulas();
            var gridLines = TagPipelineHelper.LoadGridLines(doc);
            var stats = new TaggingStats();
            var sw = Stopwatch.StartNew();

            bool cancelled = false;

            using (Transaction tx = new Transaction(doc, "STING Tag New Only"))
            {
                tx.Start();

                int processed = 0;
                foreach (Element el in sorted)
                {
                    if (processed % 100 == 0 && EscapeChecker.IsEscapePressed())
                    {
                        StingLog.Info($"TagNewOnly: cancelled by user at {processed}/{untagged.Count}");
                        cancelled = true;
                        break;
                    }
                    processed++;

                    try
                    {
                        bool pipelineOk = TagPipelineHelper.RunFullPipeline(doc, el, popCtx,
                            tagIndex, seqCounters, formulas, gridLines,
                            overwrite: false, skipComplete: true,
                            collisionMode: TagCollisionMode.Skip, stats: stats);
                        if (!pipelineOk)
                            StingLog.Warn($"TagNewOnly: pipeline returned false for element {el?.Id}");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"TagNewOnly: failed on element {el?.Id}: {ex.Message}", ex);
                        stats.RecordWarning($"Error on element {el?.Id}: {ex.Message}");
                    }
                }

                if (cancelled)
                {
                    tx.RollBack();
                    TaskDialog.Show("Tag New Only", $"Cancelled by user.\nAll changes rolled back.");
                    return Result.Cancelled;
                }

                tx.Commit();
            }
            sw.Stop();
            TagPipelineHelper.PostTagCleanup(doc, seqCounters, "TagNewOnly");

            var report = new StringBuilder();
            report.AppendLine($"Tag New Only — {untagged.Count} elements");
            report.AppendLine(new string('=', 50));
            report.AppendLine($"  Duration:  {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine();
            report.Append(stats.BuildReport());

            TaskDialog td = new TaskDialog("Tag New Only");
            td.MainInstruction = $"Tagged {stats.TotalTagged} new elements";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"TagNewOnly: tagged={stats.TotalTagged}, " +
                $"collisions={stats.TotalCollisions}, elapsed={sw.Elapsed.TotalSeconds:F1}s");

            return Result.Succeeded;
        }
    }
}
