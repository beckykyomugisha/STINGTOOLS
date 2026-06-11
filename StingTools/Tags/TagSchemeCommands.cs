using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 191 — Tag Scheme commands.
    //
    // RenderSchemeTagsCommand  — batch-render every enabled project tag scheme
    //                            onto already-tagged elements (selection wins,
    //                            else whole project) and update the render
    //                            stamp. The per-element pipeline hook in
    //                            TagPipelineHelper keeps NEW tags current;
    //                            this command back-fills existing tags and
    //                            heals after a scheme definition change.
    // TagSchemeInspectCommand  — read-only: scheme list, enablement, validity,
    //                            checksum drift vs the last render stamp, and
    //                            rendered-coverage counts.
    // TagSchemeAuditCommand    — read-only consistency audit: re-renders each
    //                            enabled scheme from the element's current
    //                            tokens and compares with the stored string;
    //                            mismatches (hand edits / stale renders) are
    //                            reported and exported to CSV.
    // ─────────────────────────────────────────────────────────────────────────

    internal static class TagSchemeCommandHelper
    {
        /// <summary>
        /// Elements in scope for scheme operations: current selection when
        /// non-empty, else every taggable element in the project that already
        /// carries a canonical tag (schemes render tagged elements only —
        /// untagged elements get their scheme string via the tagging pipeline).
        /// </summary>
        public static List<Element> CollectScope(UIDocument uidoc, Document doc, out string scopeLabel)
        {
            var selIds = uidoc?.Selection?.GetElementIds();
            if (selIds != null && selIds.Count > 0)
            {
                scopeLabel = $"selection ({selIds.Count})";
                return selIds.Select(doc.GetElement).Where(e => e != null).ToList();
            }

            scopeLabel = "project";
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            var catEnums = SharedParamGuids.AllCategoryEnums;
            FilteredElementCollector collector;
            if (catEnums != null && catEnums.Length > 0)
            {
                collector = (FilteredElementCollector)new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));
            }
            else
            {
                collector = (FilteredElementCollector)new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType();
            }
            return collector
                .Where(e => known.Contains(ParameterHelpers.GetCategoryName(e)))
                .ToList();
        }
    }

    /// <summary>
    /// Batch-render all enabled tag schemes onto tagged elements and update
    /// the render stamp. Idempotent — re-runs converge to the same strings.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RenderSchemeTagsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var schemes = TagSchemeRegistry.EnabledSchemes(doc);
            if (schemes.Count == 0)
            {
                TaskDialog.Show("Render Scheme Tags",
                    "No tag scheme is enabled for this project.\n\n" +
                    "Enable one via <project>\\_BIM_COORD\\tag_schemes.json " +
                    "(set \"enabled\": true) and run TagScheme Inspect to verify.\n\n" +
                    $"Corporate baseline: {StingToolsApp.FindDataFile("STING_TAG_SCHEMES.json") ?? "(not found)"}");
                return Result.Succeeded;
            }

            var scope = TagSchemeCommandHelper.CollectScope(ctx.UIDoc, doc, out string scopeLabel);
            if (scope.Count == 0)
            {
                TaskDialog.Show("Render Scheme Tags", "No taggable elements in scope.");
                return Result.Succeeded;
            }

            int written = 0, skippedUntagged = 0, failed = 0, processed = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            using (var t = new Transaction(doc, "STING Render Scheme Tags"))
            {
                t.Start();
                foreach (var el in scope)
                {
                    processed++;
                    if (processed % 500 == 0 && EscapeChecker.IsEscapePressed())
                    {
                        StingLog.Info("RenderSchemeTags: cancelled by user (Escape)");
                        break;
                    }

                    try
                    {
                        // Schemes are renderings of the canonical tokens — only
                        // elements that already carry a canonical tag qualify.
                        string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                        if (string.IsNullOrEmpty(tag1)) { skippedUntagged++; continue; }

                        if (!TagPipelineHelper.IsEditableInWorksharing(doc, el)) { failed++; continue; }

                        int n = TagSchemeRenderer.RenderAll(doc, el, tokenVals: null);
                        if (n > 0) written++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        StingLog.Warn($"RenderSchemeTags: element {el?.Id}: {ex.Message}");
                    }
                }
                t.Commit();
            }
            sw.Stop();

            // Record the rendered scheme checksums so Inspect can detect later edits
            TagSchemeRegistry.SaveStamp(doc, schemes);

            var report = new StringBuilder();
            report.AppendLine($"Scope: {scopeLabel} — {scope.Count} elements");
            report.AppendLine($"Schemes rendered ({schemes.Count}):");
            foreach (var s in schemes)
                report.AppendLine($"  • {s.Id} → {s.TargetParam}");
            report.AppendLine();
            report.AppendLine($"Elements written:        {written}");
            report.AppendLine($"Skipped (no ASS_TAG_1):  {skippedUntagged}");
            report.AppendLine($"Failed / locked:         {failed}");
            report.AppendLine($"Duration:                {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine();
            report.AppendLine("Render stamp updated — Inspect now reports schemes as current.");

            TaskDialog td = new TaskDialog("Render Scheme Tags")
            {
                MainInstruction = $"{written} element(s) rendered across {schemes.Count} scheme(s)",
                MainContent = report.ToString()
            };
            td.Show();
            StingLog.Info($"RenderSchemeTags: {written} written, {skippedUntagged} untagged, {failed} failed ({scopeLabel})");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Read-only diagnostic: lists all schemes visible to this document with
    /// enablement, validity, drift vs the last render stamp, and coverage.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagSchemeInspectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Reload so on-disk edits are visible without restarting Revit
            TagSchemeRegistry.Reload(doc);
            var all = TagSchemeRegistry.GetAll(doc);
            var drifted = new HashSet<string>(TagSchemeRegistry.DriftedSchemeIds(doc));

            var report = new StringBuilder();
            report.AppendLine($"Schemes visible: {all.Count}");
            report.AppendLine($"Project overlay: {TagSchemeRegistry.ProjectOverlayPath(doc) ?? "(unsaved document)"}");
            report.AppendLine(new string('─', 46));

            // Coverage scan: tagged elements vs elements carrying each target param
            int taggedCount = 0;
            var targetCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var scope = TagSchemeCommandHelper.CollectScope(null, doc, out _);
                foreach (var el in scope)
                {
                    if (string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.TAG1))) continue;
                    taggedCount++;
                    foreach (var s in all)
                    {
                        if (!s.Enabled || string.IsNullOrEmpty(s.TargetParam)) continue;
                        if (!string.IsNullOrEmpty(ParameterHelpers.GetString(el, s.TargetParam)))
                        {
                            targetCounts.TryGetValue(s.Id, out int c);
                            targetCounts[s.Id] = c + 1;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagSchemeInspect coverage scan: {ex.Message}");
            }

            foreach (var s in all.OrderByDescending(x => x.Enabled).ThenBy(x => x.Id))
            {
                string validity = TagSchemeRegistry.ValidateScheme(s);
                report.AppendLine($"{(s.Enabled ? "●" : "○")} {s.Id}  [{s.Origin}]");
                report.AppendLine($"   {s.Name}");
                report.AppendLine($"   target: {s.TargetParam}   segments: {s.Segments?.Count ?? 0}   sep: '{s.Separator}'");
                if (validity != null)
                    report.AppendLine($"   ⚠ INVALID: {validity}");
                if (s.Enabled)
                {
                    targetCounts.TryGetValue(s.Id, out int covered);
                    report.AppendLine($"   coverage: {covered}/{taggedCount} tagged elements rendered");
                    if (drifted.Contains(s.Id))
                        report.AppendLine("   ⚠ DRIFT: definition changed since last render — run Render Scheme Tags to heal");
                }
                report.AppendLine();
            }

            if (all.Count == 0)
                report.AppendLine("No schemes found. Ship STING_TAG_SCHEMES.json in data/ or add a project overlay.");

            int enabledCount = all.Count(x => x.Enabled);
            TaskDialog td = new TaskDialog("Tag Scheme Inspect")
            {
                MainInstruction = $"{enabledCount} enabled / {all.Count} schemes — " +
                    (drifted.Count > 0 ? $"{drifted.Count} drifted" : "no drift"),
                MainContent = report.ToString()
            };
            td.Show();
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Read-only consistency audit: for every enabled scheme, re-render from
    /// the element's current tokens and compare with the stored string.
    /// Mismatches mean a hand-edited scheme tag, a stale render after a token
    /// or scheme change, or a project-info edit not yet re-rendered.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TagSchemeAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var schemes = TagSchemeRegistry.EnabledSchemes(doc);
            if (schemes.Count == 0)
            {
                TaskDialog.Show("Tag Scheme Audit", "No tag scheme is enabled for this project — nothing to audit.");
                return Result.Succeeded;
            }

            var scope = TagSchemeCommandHelper.CollectScope(ctx.UIDoc, doc, out string scopeLabel);
            int tagged = 0, consistent = 0, mismatched = 0, unrendered = 0;
            var rows = new List<string> { "ElementId,Category,SchemeId,Stored,Expected,State" };
            var firstMismatches = new List<string>();

            foreach (var el in scope)
            {
                string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (string.IsNullOrEmpty(tag1)) continue;
                tagged++;

                string[] tokenVals = ParamRegistry.ReadTokenValues(el);
                foreach (var scheme in schemes)
                {
                    string expected = TagSchemeRenderer.Render(doc, el, scheme, tokenVals);
                    string stored = ParameterHelpers.GetString(el, scheme.TargetParam);
                    string state;
                    if (string.IsNullOrEmpty(stored))
                    {
                        state = "UNRENDERED";
                        unrendered++;
                    }
                    else if (string.Equals(stored, expected, StringComparison.Ordinal))
                    {
                        state = "OK";
                        consistent++;
                        continue; // don't bloat the CSV with passes
                    }
                    else
                    {
                        state = "MISMATCH";
                        mismatched++;
                        if (firstMismatches.Count < 10)
                            firstMismatches.Add($"  {el.Id} [{ParameterHelpers.GetCategoryName(el)}] stored '{stored}' ≠ expected '{expected}'");
                    }
                    rows.Add($"{el.Id},\"{ParameterHelpers.GetCategoryName(el)}\",{scheme.Id},\"{stored}\",\"{expected}\",{state}");
                }
            }

            string csvPath = null;
            if (rows.Count > 1)
            {
                try
                {
                    csvPath = OutputLocationHelper.GetOutputPath(doc, "STING_TagScheme_Audit.csv");
                    File.WriteAllLines(csvPath, rows, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"TagSchemeAudit CSV write: {ex.Message}");
                    csvPath = null;
                }
            }

            var report = new StringBuilder();
            report.AppendLine($"Scope: {scopeLabel} — {tagged} tagged elements × {schemes.Count} scheme(s)");
            report.AppendLine();
            report.AppendLine($"Consistent:  {consistent}");
            report.AppendLine($"Mismatched:  {mismatched}");
            report.AppendLine($"Unrendered:  {unrendered}");
            if (firstMismatches.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("First mismatches:");
                foreach (var line in firstMismatches) report.AppendLine(line);
            }
            if (mismatched + unrendered > 0)
            {
                report.AppendLine();
                report.AppendLine("Heal: run Render Scheme Tags — scheme strings are derived, never hand-edited.");
            }
            if (csvPath != null)
            {
                report.AppendLine();
                report.AppendLine($"CSV: {csvPath}");
            }

            TaskDialog td = new TaskDialog("Tag Scheme Audit")
            {
                MainInstruction = mismatched + unrendered == 0
                    ? "All scheme tags consistent with tokens"
                    : $"{mismatched} mismatch(es), {unrendered} unrendered",
                MainContent = report.ToString()
            };
            td.Show();
            return Result.Succeeded;
        }
    }
}
