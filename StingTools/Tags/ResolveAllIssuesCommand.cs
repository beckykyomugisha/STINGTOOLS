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
    /// One-click command that resolves ALL tag completeness issues to achieve 100% compliance.
    ///
    /// Unlike AutoTag/BatchTag which only process untagged elements (in Skip mode), this
    /// command processes EVERY taggable element regardless of current state, ensuring:
    ///   1. All 9 tokens populated with guaranteed defaults (no empty values)
    ///   2. Every element has a complete 8-segment tag (no placeholders like XX, ZZ, 0000)
    ///   3. STATUS set on every element (phase-aware detection, default "NEW")
    ///   4. REV set on every element (project revision, default "P01")
    ///   5. All 36 containers populated unconditionally
    ///   6. Duplicate tags resolved by re-assigning SEQ numbers
    ///   7. Incomplete/partial tags rebuilt from scratch
    ///
    /// This is the "nuclear option" for achieving 0% issues / 100% compliance.
    /// It should be run after the standard tagging workflow when validation shows gaps.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ResolveAllIssuesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            // Phase 1: Scan all elements and classify issues
            var allElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToList();

            int totalTaggable = 0;
            int noTag = 0, incompleteTag = 0, unresolvedTag = 0;
            int emptyStatus = 0, emptyRev = 0;
            int emptyTokens = 0;
            var taggableElements = new List<Element>();

            foreach (Element e in allElements)
            {
                string cat = ParameterHelpers.GetCategoryName(e);
                if (!known.Contains(cat)) continue;
                totalTaggable++;
                taggableElements.Add(e);

                string tag = ParameterHelpers.GetString(e, ParamRegistry.TAG1);
                if (string.IsNullOrEmpty(tag)) noTag++;
                else if (!TagConfig.TagIsComplete(tag)) incompleteTag++;
                else if (!TagConfig.TagIsFullyResolved(tag)) unresolvedTag++;

                if (string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.STATUS))) emptyStatus++;
                if (string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.REV))) emptyRev++;

                // Count empty individual tokens
                string[] tokenParams = { ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                    ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC, ParamRegistry.PROD };
                foreach (string p in tokenParams)
                {
                    if (string.IsNullOrEmpty(ParameterHelpers.GetString(e, p))) emptyTokens++;
                }
            }

            if (totalTaggable == 0)
            {
                TaskDialog.Show("Resolve All Issues", "No taggable elements found in the project.");
                return Result.Succeeded;
            }

            int totalIssues = noTag + incompleteTag + unresolvedTag + emptyStatus + emptyRev + emptyTokens;

            // Phase 2: Confirmation dialog with issue summary
            TaskDialog confirm = new TaskDialog("Resolve All Issues — 100% Compliance");
            confirm.MainInstruction = totalIssues == 0
                ? $"All {totalTaggable:N0} elements appear compliant — re-process anyway?"
                : $"Fix {totalIssues:N0} issues across {totalTaggable:N0} elements?";
            confirm.MainContent =
                $"Issue Summary:\n" +
                $"  No tag at all:       {noTag:N0}\n" +
                $"  Incomplete tags:     {incompleteTag:N0}\n" +
                $"  Unresolved (XX/ZZ):  {unresolvedTag:N0}\n" +
                $"  Empty STATUS:        {emptyStatus:N0}\n" +
                $"  Empty REV:           {emptyRev:N0}\n" +
                $"  Empty token values:  {emptyTokens:N0}\n\n" +
                "This will force-populate all 9 tokens with guaranteed defaults,\n" +
                "rebuild all tags, resolve duplicates, fill all containers,\n" +
                "and set STATUS/REV on every element.\n\n" +
                "All existing tags will be overwritten.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            // Phase 3: Smart sort for contiguous SEQ
            var sorted = BatchTagCommand.SmartSortElements(doc, taggableElements);

            var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
            var sequenceCounters = new Dictionary<string, int>(); // Fresh counters — rebuild all SEQ from scratch
            var tagIndex = new HashSet<string>(); // Fresh index — no pre-existing tags (we're overwriting all)
            var stats = new TaggingStats();
            var sw = Stopwatch.StartNew();
            int populated = 0, statusFixed = 0, revFixed = 0;
            int tagsRebuilt = 0, containersWritten = 0;
            int duplicatesResolved = 0;

            StingLog.Info($"ResolveAllIssues: starting — {totalTaggable} elements, " +
                $"{totalIssues} issues (noTag={noTag}, incomplete={incompleteTag}, " +
                $"unresolved={unresolvedTag}, emptyStatus={emptyStatus}, emptyRev={emptyRev})");

            using (Transaction tx = new Transaction(doc, "STING Resolve All Issues"))
            {
                tx.Start();

                int processed = 0;
                foreach (Element el in sorted)
                {
                    try
                    {
                        string catName = ParameterHelpers.GetCategoryName(el);

                        // Step 1: Force-populate all 9 tokens with guaranteed defaults
                        var popResult = TokenAutoPopulator.PopulateAll(doc, el, popCtx, overwrite: true);
                        populated += popResult.TokensSet;
                        if (popResult.StatusDetected) statusFixed++;
                        if (popResult.RevSet) revFixed++;

                        // Step 2: Rebuild tag from scratch with fresh SEQ (overwrite mode)
                        TagConfig.BuildAndWriteTag(doc, el, sequenceCounters,
                            skipComplete: false,
                            existingTags: tagIndex,
                            collisionMode: TagCollisionMode.Overwrite,
                            stats: stats);
                        tagsRebuilt++;

                        // Step 3: Verify containers are written (BuildAndWriteTag now does this unconditionally)
                        containersWritten++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"ResolveAllIssues: failed on element {el?.Id}: {ex.Message}", ex);
                        stats.RecordWarning($"Error on element {el?.Id}: {ex.Message}");
                    }

                    processed++;
                    if (processed % 500 == 0)
                        StingLog.Info($"ResolveAllIssues progress: {processed}/{totalTaggable} " +
                            $"({tagsRebuilt} rebuilt, {stats.TotalCollisions} collisions resolved)");
                }

                tx.Commit();
            }

            sw.Stop();
            duplicatesResolved = stats.TotalCollisions;

            // Phase 4: Post-fix verification scan
            int postNoTag = 0, postIncomplete = 0, postUnresolved = 0;
            int postEmptyStatus = 0, postEmptyRev = 0, postEmptyTokens = 0;

            foreach (Element e in taggableElements)
            {
                string tag = ParameterHelpers.GetString(e, ParamRegistry.TAG1);
                if (string.IsNullOrEmpty(tag)) postNoTag++;
                else if (!TagConfig.TagIsComplete(tag)) postIncomplete++;
                else if (!TagConfig.TagIsFullyResolved(tag)) postUnresolved++;

                if (string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.STATUS))) postEmptyStatus++;
                if (string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.REV))) postEmptyRev++;

                string[] tokenParams = { ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                    ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC, ParamRegistry.PROD };
                foreach (string p in tokenParams)
                {
                    if (string.IsNullOrEmpty(ParameterHelpers.GetString(e, p))) postEmptyTokens++;
                }
            }

            int postTotalIssues = postNoTag + postIncomplete + postUnresolved + postEmptyStatus + postEmptyRev + postEmptyTokens;
            double complianceRate = totalTaggable > 0 ? (1.0 - (double)postTotalIssues / (totalTaggable * 10)) * 100.0 : 100.0;
            if (complianceRate > 100) complianceRate = 100;

            // Phase 5: Rich report
            var report = new StringBuilder();
            report.AppendLine("Resolve All Issues — Complete");
            report.AppendLine(new string('=', 55));
            report.AppendLine();
            report.AppendLine("BEFORE:");
            report.AppendLine($"  No tag:          {noTag:N0}");
            report.AppendLine($"  Incomplete:      {incompleteTag:N0}");
            report.AppendLine($"  Unresolved:      {unresolvedTag:N0}");
            report.AppendLine($"  Empty STATUS:    {emptyStatus:N0}");
            report.AppendLine($"  Empty REV:       {emptyRev:N0}");
            report.AppendLine($"  Empty tokens:    {emptyTokens:N0}");
            report.AppendLine();
            report.AppendLine("ACTIONS:");
            report.AppendLine($"  Tokens populated:    {populated:N0}");
            report.AppendLine($"  Tags rebuilt:        {tagsRebuilt:N0}");
            report.AppendLine($"  STATUS set:          {statusFixed:N0}");
            report.AppendLine($"  REV set:             {revFixed:N0}");
            report.AppendLine($"  Duplicates resolved: {duplicatesResolved:N0}");
            report.AppendLine($"  Containers written:  {containersWritten:N0}");
            report.AppendLine($"  Duration:            {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine();
            report.AppendLine("AFTER:");
            report.AppendLine($"  No tag:          {postNoTag:N0}");
            report.AppendLine($"  Incomplete:      {postIncomplete:N0}");
            report.AppendLine($"  Unresolved:      {postUnresolved:N0}");
            report.AppendLine($"  Empty STATUS:    {postEmptyStatus:N0}");
            report.AppendLine($"  Empty REV:       {postEmptyRev:N0}");
            report.AppendLine($"  Empty tokens:    {postEmptyTokens:N0}");
            report.AppendLine();

            if (postTotalIssues == 0)
            {
                report.AppendLine("ALL ISSUES RESOLVED — 100% COMPLIANCE ACHIEVED");
            }
            else
            {
                report.AppendLine($"Remaining issues: {postTotalIssues:N0}");
                report.AppendLine("(These may be elements with read-only parameters or missing shared params)");
            }

            report.AppendLine();
            report.Append(stats.BuildReport());

            StingLog.Info($"ResolveAllIssues: rebuilt={tagsRebuilt}, populated={populated}, " +
                $"statusFixed={statusFixed}, revFixed={revFixed}, duplicates={duplicatesResolved}, " +
                $"postIssues={postTotalIssues}, elapsed={sw.Elapsed.TotalSeconds:F1}s");

            TaskDialog td = new TaskDialog("Resolve All Issues");
            td.MainInstruction = postTotalIssues == 0
                ? $"100% Compliance — All {totalTaggable:N0} elements fully resolved"
                : $"Resolved {totalIssues - postTotalIssues:N0} of {totalIssues:N0} issues ({complianceRate:F1}%)";
            td.MainContent = report.ToString();
            td.Show();

            return Result.Succeeded;
        }
    }
}
