// ════════════════════════════════════════════════════════════════════════════
// McpEngineRegistry — the SINGLE source of engine-backed writes (Phase 3a)
//
// Maps a command tag → an EngineHandler(doc, args, dryRun) that either computes a
// PLAN (dryRun, mutates nothing) or performs the real mutation inside a rolled-back
// TransactionGroup and returns structured read-back {changed, skipped, errors, sampleIds}.
//
// Both surfaces dispatch through here:
//   - the named Tier-2 verb  auto_tag  (McpWriteTools)
//   - invoke_capability      (McpDiscoveryTools)
// Add a handler once → it is reachable from both automatically, and
// McpCapabilityCatalogue.EngineBacked is derived from IsEngineBacked (single source).
//
// Phase 3a registers exactly ONE engine: the AutoTag/BatchTag tagging pipeline
// (TagPipelineHelper.RunFullPipeline — the verified dialog-free entry point). More
// handlers land in Phase 3b.
//
// DispatchWrite() applies the shared guardrails: dry-run plans, the confirm gate,
// and the sync (scoped) vs async (project) execution policy. No modal UI ever runs
// inside a job (it would deadlock the waiting/async handler), so PostTagCleanup's
// compliance-gate TaskDialog is deliberately NOT called — the safe cleanup steps
// are replicated inline.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Tags;

namespace StingTools.Mcp
{
    /// <summary>Handler contract: dryRun → plan (no mutation); else mutate + read-back.</summary>
    internal delegate McpJobResult EngineHandler(Document doc, JObject args, bool dryRun);

    internal static class McpEngineRegistry
    {
        private static readonly Dictionary<string, EngineHandler> _handlers =
            new Dictionary<string, EngineHandler>(StringComparer.OrdinalIgnoreCase)
            {
                ["AutoTag"]          = AutoTagHandler,          // view-scope tagging
                ["BatchTag"]         = AutoTagHandler,          // project-scope tagging
                ["TagScheme_Render"] = TagSchemeRenderHandler,  // render project tag schemes
            };

        public static bool IsEngineBacked(string tag) => tag != null && _handlers.ContainsKey(tag);
        public static EngineHandler Get(string tag) =>
            tag != null && _handlers.TryGetValue(tag, out var h) ? h : null;
        public static IEnumerable<string> Tags => _handlers.Keys;

        // ── Shared write dispatcher (guardrails + sync/async policy) ─────────────

        /// <summary>
        /// Dispatch an engine-backed write with the standard guardrails:
        ///   dryRun            → synchronous plan (60s), never mutates.
        ///   scope=project     → confirm required; returns {jobId, status:running} (async).
        ///   scope=selection/view → synchronous (60s); handler enforces confirm for &gt;25.
        /// Returns an McpJobResult (caller renders via ToCallResult).
        /// </summary>
        public static McpJobResult DispatchWrite(string tag, JObject args)
        {
            EngineHandler handler = Get(tag);
            if (handler == null)
                return McpJobResult.Error("no_engine_path", $"'{tag}' has no engine handler.");

            args = args ?? new JObject();
            bool dryRun  = McpSafety.IsDryRun(args);
            bool confirm = McpSafety.IsConfirmed(args);

            // Normalise scope so the handler and the confirm gate agree.
            string scope = args["scope"]?.Value<string>()?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(scope))
                scope = tag.Equals("BatchTag", StringComparison.OrdinalIgnoreCase) ? "project" : "view";
            args["scope"] = scope;

            // Dry run: synchronous, bounded, never mutates.
            if (dryRun)
                return McpJobBridge.Run(uiApp => RunHandler(uiApp, handler, args, true), 60000);

            // Project scope: confirm gate up front (fast, synchronous), then async execute.
            if (scope == "project")
            {
                if (!confirm)
                    return McpSafety.RequireConfirmation(-1, isProjectScope: true, confirmed: false);

                string jobId = McpJobBridge.Submit(uiApp => RunHandler(uiApp, handler, args, false));
                return McpJobResult.Success(
                    $"'{tag}' started (project scope). Poll get_job_status with this jobId.",
                    new Dictionary<string, object> { ["jobId"] = jobId, ["status"] = "running" });
            }

            // Scoped (selection / view): synchronous with a longer timeout. The handler
            // enforces the confirm gate itself once it has counted the affected elements.
            return McpJobBridge.Run(uiApp => RunHandler(uiApp, handler, args, false), 60000);
        }

        /// <summary>License+document guard; resolves the live selection into args for
        /// selection scope; then runs the handler on the API thread.</summary>
        private static McpJobResult RunHandler(UIApplication uiApp, EngineHandler handler, JObject args, bool dryRun)
        {
            var lic = McpSafety.RequireLicense();
            if (lic != null) return lic;
            var de = McpSafety.RequireDocument(uiApp);
            if (de != null) return de;

            UIDocument uidoc = uiApp.ActiveUIDocument;
            Document doc = uidoc.Document;

            string scope = args["scope"]?.Value<string>()?.ToLowerInvariant() ?? "view";
            if (scope == "selection")
                args["_elementIds"] = new JArray(uidoc.Selection.GetElementIds().Select(i => i.Value));

            return handler(doc, args, dryRun);
        }

        // ── AutoTag / BatchTag engine handler ────────────────────────────────────
        //
        // Drives TagPipelineHelper.RunFullPipeline — the verified dialog-free tagging
        // engine, exactly as AutoTagCommand/BatchTagCommand do, minus all UI. Signature:
        //   bool RunFullPipeline(Document, Element, PopulationContext, HashSet<string> tagIndex,
        //       Dictionary<string,int> seqCounters, List<FormulaDefinition> formulas,
        //       List<Grid> gridLines, bool overwrite, bool skipComplete,
        //       TagCollisionMode collisionMode, TaggingStats stats)

        private static McpJobResult AutoTagHandler(Document doc, JObject args, bool dryRun)
        {
            string mode = args["mode"]?.Value<string>()?.Trim().ToLowerInvariant() ?? "skip";
            bool overwrite = mode == "overwrite";
            TagCollisionMode collisionMode =
                mode == "overwrite" ? TagCollisionMode.Overwrite :
                mode == "increment" ? TagCollisionMode.AutoIncrement :
                                      TagCollisionMode.Skip;

            List<Element> targets = CollectTaggableTargets(doc, args);
            if (targets.Count == 0)
                return McpJobResult.Success("No taggable elements in scope.",
                    McpSafety.WriteResult(0, 0, null, null));

            // "Would change" = untagged/incomplete (skip mode) or all (overwrite).
            var changeList = targets
                .Where(el => overwrite || !TagConfig.TagIsComplete(ParameterHelpers.GetString(el, ParamRegistry.TAG1)))
                .ToList();
            var sampleIds = changeList.Take(25).Select(e => e.Id.Value).ToList();

            if (dryRun)
            {
                var plan = new Dictionary<string, object>
                {
                    ["status"]          = "dry_run",
                    ["mode"]            = mode,
                    ["totalTargets"]    = targets.Count,
                    ["plannedChanges"]  = changeList.Count,
                    ["alreadyComplete"] = targets.Count - changeList.Count,
                    ["sampleIds"]       = sampleIds,
                };
                return McpJobResult.Success(
                    $"Dry run: would tag {changeList.Count} of {targets.Count} element(s) [mode={mode}]; nothing mutated.",
                    plan);
            }

            // Confirm gate (co-located with the mutation). Project scope forces confirm;
            // scoped runs force it only when the affected count exceeds the threshold.
            bool isProject = (args["scope"]?.Value<string>()?.ToLowerInvariant() ?? "") == "project";
            var confirmErr = McpSafety.RequireConfirmation(changeList.Count, isProject, McpSafety.IsConfirmed(args));
            if (confirmErr != null) return confirmErr;

            // Build the engine context once (all dialog-free).
            var (tagIndex, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
            var popCtx = TokenAutoPopulator.PopulationContext.Build(doc);
            if (popCtx == null || !popCtx.IsValid())
                return McpJobResult.Error("exception",
                    "Failed to build tagging context: " + (popCtx?.DiagnosticSummary ?? "null context") +
                    " (check rooms placed / levels defined / shared params bound).");

            var formulas = TagPipelineHelper.LoadFormulas();
            var gridLines = TagPipelineHelper.LoadGridLines(doc);
            var stats = new TaggingStats();
            var sorted = BatchTagCommand.SmartSortElements(doc, targets);

            // All mutation inside the TransactionGroup → any uncaught exception rolls the
            // whole op back (McpSafety.RunInTransactionGroup catches → RollBack → rethrow).
            // Per-element failures are caught + recorded (best-effort) so one bad element
            // does not undo the rest.
            McpSafety.RunInTransactionGroup(doc, $"STING MCP {(isProject ? "BatchTag" : "AutoTag")}", () =>
            {
                using (var tx = new Transaction(doc, "STING MCP AutoTag"))
                {
                    tx.Start();
                    foreach (Element el in sorted)
                    {
                        try
                        {
                            bool skipComplete = collisionMode != TagCollisionMode.Overwrite;
                            bool ow = collisionMode == TagCollisionMode.Overwrite;
                            TagPipelineHelper.RunFullPipeline(doc, el, popCtx, tagIndex, seqCounters,
                                formulas, gridLines, overwrite: ow, skipComplete: skipComplete,
                                collisionMode: collisionMode, stats: stats);
                        }
                        catch (Exception ex)
                        {
                            stats.RecordWarning($"Element {el?.Id}: {ex.Message}");
                            StingLog.Warn($"MCP AutoTag element {el?.Id}: {ex.Message}");
                        }
                    }
                    tx.Commit();
                }
            });

            // Safe post-cleanup — replicates PostTagCleanup MINUS its compliance-gate
            // TaskDialog (which would deadlock the API-thread job).
            SafePostCleanup(doc, seqCounters);

            int changed = stats.TotalTagged + stats.TotalOverwritten;
            StingLog.Info($"MCP AutoTag[{(isProject ? "project" : "scoped")}]: {targets.Count} targets → " +
                          $"{changed} tagged/overwritten, {stats.TotalSkipped} skipped, {stats.Warnings.Count} warning(s).");

            var rb = McpSafety.WriteResult(changed, stats.TotalSkipped, stats.Warnings, sampleIds);
            rb["totalTargets"] = targets.Count;
            rb["collisions"]   = stats.TotalCollisions;
            return McpJobResult.Success(
                $"Tagged {changed} element(s); skipped {stats.TotalSkipped}; {stats.Warnings.Count} warning(s).", rb);
        }

        // ── TagScheme_Render engine handler ──────────────────────────────────────
        //
        // Drives TagSchemeRenderer.RenderAll — the verified dialog-free per-element scheme
        // renderer ("Must be called inside an open transaction"), quoted:
        //   public static int RenderAll(Document doc, Element el, string[] tokenVals)
        // TagSchemeRenderer.Render(...) is the read-only sibling used for the dry-run plan.

        private static McpJobResult TagSchemeRenderHandler(Document doc, JObject args, bool dryRun)
        {
            var schemes = TagSchemeRegistry.EnabledSchemes(doc);
            if (schemes == null || schemes.Count == 0)
                return McpJobResult.Success("No enabled tag schemes in this project — nothing to render.",
                    McpSafety.WriteResult(0, 0, null, null));

            List<Element> targets = CollectTaggableTargets(doc, args);
            if (targets.Count == 0)
                return McpJobResult.Success("No taggable elements in scope.",
                    McpSafety.WriteResult(0, 0, null, null));

            // Read-only would-change scan (Render writes nothing) for the plan + confirm gate.
            var wouldChange = new List<Element>();
            foreach (Element el in targets)
            {
                try
                {
                    foreach (var s in schemes)
                    {
                        string rendered = TagSchemeRenderer.Render(doc, el, s, null);
                        if (string.IsNullOrEmpty(rendered)) continue;
                        if (!string.Equals(rendered, ParameterHelpers.GetString(el, s.TargetParam), StringComparison.Ordinal))
                        { wouldChange.Add(el); break; }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"TagScheme dry-scan {el?.Id}: {ex.Message}"); }
            }
            var sampleIds = wouldChange.Take(25).Select(e => e.Id.Value).ToList();

            if (dryRun)
            {
                var plan = new Dictionary<string, object>
                {
                    ["status"]         = "dry_run",
                    ["enabledSchemes"] = schemes.Count,
                    ["totalTargets"]   = targets.Count,
                    ["plannedChanges"] = wouldChange.Count,
                    ["sampleIds"]      = sampleIds,
                };
                return McpJobResult.Success(
                    $"Dry run: would update scheme tags on {wouldChange.Count} of {targets.Count} element(s) " +
                    $"({schemes.Count} enabled scheme(s)); nothing mutated.", plan);
            }

            bool isProject = (args["scope"]?.Value<string>()?.ToLowerInvariant() ?? "") == "project";
            var confirmErr = McpSafety.RequireConfirmation(wouldChange.Count, isProject, McpSafety.IsConfirmed(args));
            if (confirmErr != null) return confirmErr;

            int changed = 0, tokensWritten = 0;
            var errors = new List<string>();

            McpSafety.RunInTransactionGroup(doc, "STING MCP TagScheme_Render", () =>
            {
                using (var tx = new Transaction(doc, "STING MCP TagScheme_Render"))
                {
                    tx.Start();
                    foreach (Element el in targets)
                    {
                        try
                        {
                            int written = TagSchemeRenderer.RenderAll(doc, el, null);
                            if (written > 0) { changed++; tokensWritten += written; }
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"{el?.Id?.Value}: {ex.Message}");
                            StingLog.Warn($"MCP TagScheme_Render {el?.Id}: {ex.Message}");
                        }
                    }
                    tx.Commit();
                }
            });

            StingLog.Info($"MCP TagScheme_Render[{(isProject ? "project" : "scoped")}]: {targets.Count} targets → " +
                          $"{changed} element(s) updated, {tokensWritten} scheme string(s) written, {errors.Count} error(s).");

            var rb = McpSafety.WriteResult(changed, targets.Count - changed, errors, sampleIds);
            rb["enabledSchemes"] = schemes.Count;
            rb["tokensWritten"]  = tokensWritten;
            return McpJobResult.Success(
                $"Rendered scheme tags on {changed} element(s); {tokensWritten} scheme string(s) written; {errors.Count} error(s).", rb);
        }

        /// <summary>Collect taggable targets for the requested scope (selection ids /
        /// active view / project), filtered to STING-taggable + editable + not demolished.</summary>
        private static List<Element> CollectTaggableTargets(Document doc, JObject args)
        {
            IEnumerable<Element> candidates;

            if (args["_elementIds"] is JArray idsArr)
            {
                var list = new List<Element>();
                foreach (var t in idsArr)
                {
                    long v = t?.Value<long?>() ?? -1;
                    if (v < 0) continue;
                    Element el = doc.GetElement(new ElementId(v));
                    if (el != null) list.Add(el);
                }
                candidates = list;
            }
            else
            {
                string scope = args["scope"]?.Value<string>()?.ToLowerInvariant() ?? "view";
                FilteredElementCollector col = (scope == "view" && doc.ActiveView != null)
                    ? new FilteredElementCollector(doc, doc.ActiveView.Id)
                    : new FilteredElementCollector(doc);
                candidates = col.WhereElementIsNotElementType();
            }

            var taggable = new List<Element>();
            foreach (Element e in candidates)
            {
                try
                {
                    if (e.LookupParameter(ParamRegistry.TAG1) == null) continue;   // not STING-taggable
                    if (!TagPipelineHelper.IsEditableInWorksharing(doc, e)) continue;
                    if (TagPipelineHelper.IsDemolished(e)) continue;
                    taggable.Add(e);
                }
                catch (Exception ex) { StingLog.Warn($"CollectTaggableTargets {e?.Id}: {ex.Message}"); }
            }
            return taggable;
        }

        /// <summary>Non-UI subset of TagPipelineHelper.PostTagCleanup (no compliance-gate dialog).</summary>
        private static void SafePostCleanup(Document doc, Dictionary<string, int> seqCounters)
        {
            try { TagConfig.SaveSeqSidecar(doc, seqCounters); }
            catch (Exception ex) { StingLog.Warn($"MCP AutoTag SaveSeqSidecar: {ex.Message}"); }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"MCP AutoTag InvalidateCache: {ex.Message}"); }
            try { StingAutoTagger.InvalidateContext(); } catch (Exception ex) { StingLog.Warn($"MCP AutoTag InvalidateContext: {ex.Message}"); }
            try { TokenAutoPopulator.PopulationContext.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"MCP AutoTag PopCtx invalidate: {ex.Message}"); }
        }
    }
}
