using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    // ════════════════════════════════════════════════════════════════════════════
    //  WORKFLOW ORCHESTRATION ENGINE
    //
    //  Enables zero-touch project delivery by chaining named command sequences
    //  from JSON preset files. Each preset defines an ordered list of command
    //  keys (matching StingCommandHandler dispatch tags) with optional parameters.
    //
    //  Features:
    //    - JSON-based workflow presets (data/ directory)
    //    - Per-step progress reporting to WPF status bar
    //    - Escape key cancellation between steps
    //    - Each step runs in its own transaction
    //    - Built-in presets: ProjectKickoff, DailyQA, DocumentPackage
    //
    //  Commands:
    //    WorkflowPresetCommand     — Run a named preset
    //    ListWorkflowPresetsCommand — List available presets
    //    CreateWorkflowPresetCommand — Create/edit preset from current config
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Run a named workflow preset — chained command sequence with cancel support.
    /// Presets are JSON files in the data/ directory or built-in defaults.
    /// Each step runs in its own transaction.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WorkflowPresetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var presets = WorkflowEngine.GetAvailablePresets();
            if (presets.Count == 0)
            {
                TaskDialog.Show("Workflow Presets", "No workflow presets found.\nUse 'Create Workflow Preset' to define one.");
                return Result.Cancelled;
            }

            // Auto-select preset if name passed via ExtraParam (from Document Manager)
            string autoName = UI.StingCommandHandler.GetExtraParam("WorkflowPresetName");
            UI.StingCommandHandler.ClearExtraParam("WorkflowPresetName");
            if (!string.IsNullOrEmpty(autoName))
            {
                var auto = presets.FirstOrDefault(p => p.Name.Equals(autoName, StringComparison.OrdinalIgnoreCase))
                    ?? WorkflowEngine.GetBuiltInPreset(autoName);
                if (auto != null)
                    return WorkflowEngine.ExecutePreset(auto, commandData, elements);
            }

            // WF-01 FIX: Support unlimited presets via paged selection.
            // If ≤4 presets, use CommandLinks directly. If >4, show pages of 3 + "More..." link.
            WorkflowPreset selected = null;
            int pageStart = 0;

            while (selected == null)
            {
                TaskDialog picker = new TaskDialog("Run Workflow Preset");
                picker.MainInstruction = "Select a workflow preset to run";

                int remaining = presets.Count - pageStart;
                int showCount = Math.Min(remaining, presets.Count <= 4 ? 4 : 3);
                bool hasMore = remaining > showCount;

                var sb = new StringBuilder();
                for (int i = pageStart; i < pageStart + showCount; i++)
                {
                    var p = presets[i];
                    sb.AppendLine($"  {i + 1}. {p.Name}: {p.Description} ({p.Steps.Count} steps)");
                }
                if (hasMore)
                    sb.AppendLine($"\n  ({remaining - showCount} more preset(s) available...)");
                picker.MainContent = sb.ToString();

                var linkIds = new[] {
                    TaskDialogCommandLinkId.CommandLink1,
                    TaskDialogCommandLinkId.CommandLink2,
                    TaskDialogCommandLinkId.CommandLink3,
                    TaskDialogCommandLinkId.CommandLink4,
                };

                for (int i = 0; i < showCount; i++)
                {
                    var p = presets[pageStart + i];
                    picker.AddCommandLink(linkIds[i],
                        p.Name, $"{p.Description} ({p.Steps.Count} steps)");
                }

                if (hasMore)
                {
                    picker.AddCommandLink(linkIds[showCount],
                        "More presets...", $"Show next page ({remaining - showCount} more)");
                }

                var result = picker.Show();

                if (result == TaskDialogResult.CommandLink1) selected = presets[pageStart];
                else if (result == TaskDialogResult.CommandLink2 && showCount >= 2) selected = presets[pageStart + 1];
                else if (result == TaskDialogResult.CommandLink3 && showCount >= 3) selected = presets[pageStart + 2];
                else if (result == TaskDialogResult.CommandLink4)
                {
                    if (hasMore && showCount == 3) { pageStart += 3; continue; }
                    else if (showCount >= 4) selected = presets[pageStart + 3];
                }
                else return Result.Cancelled;
            }

            return WorkflowEngine.ExecutePreset(selected, commandData, elements);
        }
    }

    /// <summary>
    /// List all available workflow presets (built-in + user-defined JSON files).
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ListWorkflowPresetsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var presets = WorkflowEngine.GetAvailablePresets();
            var report = new StringBuilder();
            report.AppendLine("Available Workflow Presets");
            report.AppendLine(new string('═', 45));

            foreach (var p in presets)
            {
                report.AppendLine($"\n  {p.Name} ({(p.IsBuiltIn ? "Built-in" : "User")})");
                report.AppendLine($"  {p.Description}");
                report.AppendLine($"  Steps ({p.Steps.Count}):");
                for (int i = 0; i < p.Steps.Count; i++)
                    report.AppendLine($"    {i + 1,2}. {p.Steps[i].Label}");
            }

            TaskDialog.Show("Workflow Presets", report.ToString());
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Create a new workflow preset from a template, or save the current MasterSetup
    /// sequence as a named JSON preset for future reuse.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateWorkflowPresetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            // Offer to create from built-in templates
            TaskDialog dlg = new TaskDialog("Create Workflow Preset");
            dlg.MainInstruction = "Create a new workflow preset";
            dlg.MainContent =
                "Choose a template to start from.\n" +
                "The preset will be saved as a JSON file in the data/ directory.";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Project Kickoff", "Full setup: params → materials → types → tags → schedules → views");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Daily QA Sync", "Incremental: tag new → validate → audit → compliance");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Document Package", "Batch views → sheets → drawing register → BOQ");

            var result = dlg.Show();
            WorkflowPreset preset;
            if (result == TaskDialogResult.CommandLink1)
                preset = WorkflowEngine.GetBuiltInPreset("ProjectKickoff");
            else if (result == TaskDialogResult.CommandLink2)
                preset = WorkflowEngine.GetBuiltInPreset("DailyQA");
            else if (result == TaskDialogResult.CommandLink3)
                preset = WorkflowEngine.GetBuiltInPreset("DocumentPackage");
            else
                return Result.Cancelled;

            // Save to data/ directory
            string dataDir = StingToolsApp.DataPath;
            if (!Directory.Exists(dataDir))
                Directory.CreateDirectory(dataDir);

            string fileName = $"WORKFLOW_{preset.Name.ToUpperInvariant().Replace(" ", "_")}.json";
            string path = Path.Combine(dataDir, fileName);

            var json = JsonConvert.SerializeObject(preset, Formatting.Indented);
            File.WriteAllText(path, json);

            TaskDialog.Show("Workflow Preset Created",
                $"Saved: {fileName}\n" +
                $"Steps: {preset.Steps.Count}\n\n" +
                "Edit the JSON file to customize steps, or run it with 'Run Workflow'.");

            StingLog.Info($"Workflow preset created: {path}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  WORKFLOW ENGINE — internal orchestration logic
    // ════════════════════════════════════════════════════════════════════════════

    public class WorkflowPreset
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("steps")]
        public List<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();

        /// <summary>LOG-06: When true, wraps all steps in a TransactionGroup and
        /// rolls back all changes if any non-optional step fails.</summary>
        [JsonProperty("rollback_on_failure")]
        public bool RollbackOnFailure { get; set; }

        /// <summary>GAP-06: When true, rolls back ALL changes if ANY step fails (including optional steps).
        /// Use for strict quality gates where partial results are unacceptable.</summary>
        [JsonProperty("rollback_on_optional_failure")]
        public bool RollbackOnOptionalFailure { get; set; }

        [JsonIgnore]
        public bool IsBuiltIn { get; set; }
    }

    public class WorkflowStep
    {
        [JsonProperty("commandTag")]
        public string CommandTag { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("optional")]
        public bool Optional { get; set; }

        [JsonProperty("condition")]
        public string Condition { get; set; }

        /// <summary>F2: Skip step if current compliance % exceeds this threshold.</summary>
        [JsonProperty("maxCompliancePct")]
        public int? MaxCompliancePct { get; set; }

        /// <summary>F2: Skip step if current compliance % is below this threshold.</summary>
        [JsonProperty("minCompliancePct")]
        public int? MinCompliancePct { get; set; }

        /// <summary>F2: Skip step if no elements have the STALE flag set.</summary>
        [JsonProperty("requiresStaleElements")]
        public bool RequiresStaleElements { get; set; }

        /// <summary>AE-01: Number of retry attempts for transient failures (max 3).</summary>
        [JsonProperty("retryCount")]
        public int RetryCount { get; set; } = 0;

        /// <summary>AE-01: Delay in milliseconds between retries.</summary>
        [JsonProperty("retryDelayMs")]
        public int RetryDelayMs { get; set; } = 500;

        /// <summary>AE-05: Skip step if data files haven't changed since last run.</summary>
        [JsonProperty("skipIfDataUnchanged")]
        public bool SkipIfDataUnchanged { get; set; }

        /// <summary>Phase 39: Skip step if model is not workshared.</summary>
        [JsonProperty("requiresWorksharedModel")]
        public bool RequiresWorksharedModel { get; set; }

        /// <summary>Phase 39: Skip step if total element count is outside range [min, max].</summary>
        [JsonProperty("minElementCount")]
        public int? MinElementCount { get; set; }

        /// <summary>Phase 39: Maximum element count for step applicability.</summary>
        [JsonProperty("maxElementCount")]
        public int? MaxElementCount { get; set; }

        /// <summary>Phase 39: Timeout in seconds for this step (default 300 = 5 min).</summary>
        [JsonProperty("timeoutSeconds")]
        public int TimeoutSeconds { get; set; } = 300;

        /// <summary>Phase 48: Skip step if the previous step was skipped.</summary>
        [JsonProperty("skipIfPreviousSkipped")]
        public bool SkipIfPreviousSkipped { get; set; }

        /// <summary>Phase 48: Skip step if warning health score is above this threshold.</summary>
        [JsonProperty("minWarningHealthScore")]
        public int? MinWarningHealthScore { get; set; }

        /// <summary>Phase 69: Fallback command if this step fails.</summary>
        [JsonProperty("fallbackStep")]
        public string FallbackStep { get; set; }

        /// <summary>Phase 69: Condition logic for multiple conditions: "AND" (all must pass) or "OR" (any must pass).</summary>
        [JsonProperty("conditionLogic")]
        public string ConditionLogic { get; set; } = "AND";

        /// <summary>Phase 69: Array of condition keys for compound condition evaluation.</summary>
        [JsonProperty("conditions")]
        public List<string> Conditions { get; set; }

        /// <summary>Phase 69: Parallel execution group. Steps with same group number run concurrently.</summary>
        [JsonProperty("parallelGroup")]
        public int? ParallelGroup { get; set; }

        /// <summary>Phase 69: Minimum data drop level required (1-4). Skip if current DD is below.</summary>
        [JsonProperty("minDataDrop")]
        public int? MinDataDrop { get; set; }
    }

    internal static class WorkflowEngine
    {
        // Phase 48: Last workflow memory for "Repeat Last" feature
        private static string _lastWorkflowName;
        private static string _lastWorkflowResult;
        private static DateTime _lastWorkflowTime;

        /// <summary>Phase 48: Name of the last successfully executed workflow preset.</summary>
        public static string LastWorkflowName => _lastWorkflowName;

        /// <summary>Phase 48: Result summary of last workflow execution.</summary>
        public static string LastWorkflowResult => _lastWorkflowResult;

        /// <summary>Phase 48: Timestamp of last workflow execution.</summary>
        public static DateTime LastWorkflowTime => _lastWorkflowTime;

        /// <summary>Phase 48: Pre-flight model check — verifies model is suitable for workflow execution.
        /// Returns (canProceed, issues) where issues lists any blocking conditions.</summary>
        public static (bool canProceed, List<string> issues) PreFlightCheck(Document doc, WorkflowPreset preset)
        {
            var issues = new List<string>();
            if (doc == null) { issues.Add("No document is open."); return (false, issues); }

            // Check element count thresholds
            try
            {
                int elementCount = new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount();
                foreach (var step in preset.Steps)
                {
                    if (step.MinElementCount.HasValue && elementCount < step.MinElementCount.Value)
                        issues.Add($"Step '{step.Label}' requires min {step.MinElementCount} elements (model has {elementCount})");
                    if (step.MaxElementCount.HasValue && elementCount > step.MaxElementCount.Value)
                        issues.Add($"Step '{step.Label}' limited to {step.MaxElementCount} elements (model has {elementCount})");
                }
            }
            catch (Exception ex) { StingLog.Warn($"PreFlight element count: {ex.Message}"); }

            // Check worksharing requirements
            bool isWorkshared = doc.IsWorkshared;
            foreach (var step in preset.Steps)
            {
                if (step.RequiresWorksharedModel && !isWorkshared && !step.Optional)
                    issues.Add($"Step '{step.Label}' requires workshared model");
            }

            // Check data file availability
            if (string.IsNullOrEmpty(StingToolsApp.DataPath) || !Directory.Exists(StingToolsApp.DataPath))
                issues.Add("STING data directory not found — template/schedule/material commands will fail");

            // Phase 56b GAP-1: Validate all command tags resolve to actual commands
            foreach (var step in preset.Steps)
            {
                if (ResolveCommand(step.CommandTag) == null)
                {
                    var closest = GetClosestCommandTags(step.CommandTag, 5);
                    string suggestion = closest.Count > 0
                        ? $" Did you mean: {string.Join(", ", closest)}"
                        : "";
                    issues.Add($"Step '{step.Label}': command tag '{step.CommandTag}' not found — step will fail.{suggestion}");
                }
            }

            return (issues.Count == 0, issues);
        }

        /// <summary>
        /// Execute a workflow preset with progress reporting and cancellation.
        /// </summary>
        public static Result ExecutePreset(WorkflowPreset preset,
            ExternalCommandData commandData, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null)
            {
                TaskDialog.Show("Workflow", "No document is open.");
                return Result.Failed;
            }
            Document doc = ctx.Doc;
            bool useTransactionGroup = preset.RollbackOnFailure || preset.RollbackOnOptionalFailure;
            StingLog.Info($"Workflow '{preset.Name}': starting {preset.Steps.Count} steps" +
                (useTransactionGroup ? " (rollback on failure)" : ""));

            var report = new StringBuilder();
            report.AppendLine($"Workflow: {preset.Name}");
            report.AppendLine(new string('═', 50));

            int stepNum = 0;
            int passed = 0;
            int failed = 0;
            int skipped = 0;
            bool cancelled = false;
            // Phase 39: Collect per-step results for audit trail
            var stepResults = new List<WorkflowStepResult>();
            double complianceBefore = 0;
            try { var scan = ComplianceScan.Scan(doc); complianceBefore = scan.CompliancePercent; }
            catch (Exception ex) { StingLog.Warn($"Pre-workflow compliance scan failed: {ex.Message}"); }
            var totalSw = Stopwatch.StartNew();

            // PERF-03: Cache stale-element check — avoid full scan per step
            bool? _cachedHasStale = null;
            bool cachedHasStale()
            {
                if (_cachedHasStale.HasValue) return _cachedHasStale.Value;
                try
                {
                    // WF-01: Pre-filter to taggable categories to avoid scanning
                    // all non-type elements (views, sheets, annotations, etc.)
                    var wfCats = SharedParamGuids.AllCategoryEnums;
                    FilteredElementCollector staleCollector = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType();
                    if (wfCats != null && wfCats.Length > 0)
                        staleCollector.WherePasses(new ElementMulticategoryFilter(
                            new List<BuiltInCategory>(wfCats)));
                    _cachedHasStale = staleCollector.Any(e =>
                        {
                            try { var p = e.LookupParameter(ParamRegistry.STALE); return p != null && p.AsInteger() == 1; }
                            catch (Exception ex) { StingLog.Warn($"Stale check element {e?.Id}: {ex.Message}"); return false; }
                        });
                }
                catch (Exception ex) { StingLog.Warn($"Stale element check failed: {ex.Message}"); _cachedHasStale = false; }
                return _cachedHasStale.Value;
            }

            // PERF-04: Cache compliance percentage — scan once, reuse across steps
            double? _cachedCompliancePct = complianceBefore;
            double cachedCompliancePct()
            {
                if (_cachedCompliancePct.HasValue) return _cachedCompliancePct.Value;
                try { var cs = ComplianceScan.Scan(doc); _cachedCompliancePct = cs?.CompliancePercent ?? 0; }
                catch (Exception ex) { StingLog.Warn($"Compliance scan failed: {ex.Message}"); _cachedCompliancePct = 0; }
                return _cachedCompliancePct.Value;
            }

            // LOG-06: Wrap in TransactionGroup when rollback_on_failure is enabled
            TransactionGroup tg = null;
            if (useTransactionGroup)
            {
                tg = new TransactionGroup(doc, $"STING Workflow: {preset.Name}");
                tg.Start();
            }

            bool previousStepSkipped = false;  // Phase 48: Track if previous step was skipped

            try
            {
                foreach (var step in preset.Steps)
                {
                    stepNum++;

                    if (EscapeChecker.IsEscapePressed())
                    {
                        report.AppendLine($"  {stepNum,2}. {step.Label} — CANCELLED (Escape)");
                        StingLog.Info($"Workflow step {stepNum}: cancelled by user");
                        cancelled = true;
                        break;
                    }

                    // Phase 48: skipIfPreviousSkipped condition
                    if (step.SkipIfPreviousSkipped && previousStepSkipped)
                    {
                        skipped++;
                        previousStepSkipped = true;
                        report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (previous step was skipped)");
                        continue;
                    }

                    if (!string.IsNullOrEmpty(step.Condition))
                    {
                        if (step.Condition == "workshared" && !doc.IsWorkshared)
                        {
                            skipped++;
                            report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (not workshared)");
                            continue;
                        }
                        // GAP-02: Extended condition engine for workflow steps
                        if (step.Condition == "has_links")
                        {
                            bool hasLinks = new FilteredElementCollector(doc)
                                .OfClass(typeof(RevitLinkInstance)).GetElementCount() > 0;
                            if (!hasLinks) { skipped++; report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (no linked models)"); continue; }
                        }
                        if (step.Condition == "has_cad_imports")
                        {
                            bool hasCad = new FilteredElementCollector(doc)
                                .OfClass(typeof(ImportInstance)).GetElementCount() > 0;
                            if (!hasCad) { skipped++; report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (no CAD imports)"); continue; }
                        }
                        if (step.Condition == "has_stale")
                        {
                            if (!cachedHasStale()) { skipped++; report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (no stale elements)"); continue; }
                        }
                        // Phase 39: WorkflowStep.RequiresWorksharedModel condition
                        if (step.RequiresWorksharedModel && !doc.IsWorkshared)
                        {
                            skipped++; report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (not workshared)"); continue;
                        }
                        // Phase 39: Element count range condition
                        if (step.MinElementCount.HasValue || step.MaxElementCount.HasValue)
                        {
                            int elemCount = new FilteredElementCollector(doc)
                                .WhereElementIsNotElementType().GetElementCount();
                            if (step.MinElementCount.HasValue && elemCount < step.MinElementCount.Value)
                            { skipped++; report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED ({elemCount} elements < min {step.MinElementCount.Value})"); continue; }
                            if (step.MaxElementCount.HasValue && elemCount > step.MaxElementCount.Value)
                            { skipped++; report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED ({elemCount} elements > max {step.MaxElementCount.Value})"); continue; }
                        }

                        // Phase 47: Warning-aware workflow conditions
                        if (step.Condition == "has_warnings")
                        {
                            try
                            {
                                int warnCount = doc.GetWarnings()?.Count ?? 0;
                                if (warnCount == 0) { skipped++; report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (no warnings)"); continue; }
                            }
                            catch (Exception ex) { StingLog.Warn($"has_warnings check: {ex.Message}"); }
                        }
                        if (step.Condition == "has_critical_warnings")
                        {
                            try
                            {
                                var warnReport = WarningsEngine.ScanWarnings(doc);
                                int critical = warnReport.BySeverity.GetValueOrDefault(WarningSeverity.Critical);
                                if (critical == 0) { skipped++; report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (no critical warnings)"); continue; }
                            }
                            catch (Exception ex) { StingLog.Warn($"has_critical_warnings check: {ex.Message}"); }
                        }
                        if (step.Condition == "has_open_issues")
                        {
                            try
                            {
                                string projDir = Path.GetDirectoryName(doc.PathName ?? "") ?? "";
                                string issuesPath = Path.Combine(projDir, "_bim_manager", "issues.json");
                                if (!File.Exists(issuesPath)) { skipped++; report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (no issues file)"); continue; }
                                string raw = File.ReadAllText(issuesPath);
                                int openCount = raw.Split(new[] { "\"OPEN\"" }, StringSplitOptions.None).Length - 1;
                                if (openCount == 0) { skipped++; report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (no open issues)"); continue; }
                            }
                            catch (Exception ex) { StingLog.Warn($"has_open_issues check: {ex.Message}"); }
                        }

                        if (step.Condition == "has_untagged")
                        {
                            bool hasUntagged = false;
                            try
                            {
                                var catEnums = SharedParamGuids.AllCategoryEnums;
                                var coll = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                                if (catEnums != null && catEnums.Length > 0)
                                    coll.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));
                                hasUntagged = coll.Any(e => string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.TAG1)));
                            }
                            catch (Exception ex) { StingLog.Warn($"has_untagged condition check: {ex.Message}"); }
                            if (!hasUntagged) { skipped++; report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (no untagged elements)"); continue; }
                        }

                        // Phase 66c: Additional workflow condition operators
                        if (step.Condition == "has_placeholders")
                        {
                            bool hasPlaceholders = false;
                            try
                            {
                                var catEnums = SharedParamGuids.AllCategoryEnums;
                                var coll = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                                if (catEnums != null && catEnums.Length > 0)
                                    coll.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));
                                hasPlaceholders = coll.Any(e =>
                                {
                                    string t = ParameterHelpers.GetString(e, ParamRegistry.TAG1);
                                    return !string.IsNullOrEmpty(t) && TagConfig.TagHasPlaceholders(t);
                                });
                            }
                            catch (Exception ex) { StingLog.Warn($"has_placeholders check: {ex.Message}"); }
                            if (!hasPlaceholders) { skipped++; report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (no placeholder tokens)"); continue; }
                        }
                        if (step.Condition == "has_container_gaps")
                        {
                            try
                            {
                                var scan = ComplianceScan.Scan(doc);
                                double containerPct = scan?.ContainerCompletePct ?? 100;
                                if (containerPct >= 95)
                                { skipped++; report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (containers {containerPct:F0}% complete)"); continue; }
                            }
                            catch (Exception ex) { StingLog.Warn($"has_container_gaps check: {ex.Message}"); }
                        }
                        if (step.Condition == "compliance_above_90")
                        {
                            double pct = cachedCompliancePct();
                            if (pct >= 90)
                            { skipped++; report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (compliance {pct:F0}% ≥ 90%)"); continue; }
                        }
                        if (step.Condition == "compliance_below_50")
                        {
                            double pct = cachedCompliancePct();
                            if (pct >= 50)
                            { skipped++; report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (compliance {pct:F0}% ≥ 50%)"); continue; }
                        }
                    }

                    // Phase 69: Compound condition evaluation (AND/OR logic)
                    if (step.Conditions != null && step.Conditions.Count > 0)
                    {
                        bool isOr = string.Equals(step.ConditionLogic, "OR", StringComparison.OrdinalIgnoreCase);
                        var results = new List<bool>();
                        foreach (var cond in step.Conditions)
                        {
                            results.Add(EvaluateSingleCondition(doc, cond, cachedCompliancePct, cachedHasStale));
                        }

                        bool compoundResult = isOr ? results.Any(r => r) : results.All(r => r);
                        if (!compoundResult)
                        {
                            skipped++;
                            string logic = isOr ? "OR" : "AND";
                            report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (compound {logic}: {string.Join(", ", step.Conditions)})");
                            previousStepSkipped = true;
                            stepResults.Add(new WorkflowStepResult { CommandTag = step.CommandTag, Label = step.Label, Status = "SKIPPED" });
                            continue;
                        }
                    }

                    // Phase 69: Data drop level gate
                    if (step.MinDataDrop.HasValue)
                    {
                        int currentDD = CalculateCurrentDataDrop(doc, cachedCompliancePct());
                        if (currentDD < step.MinDataDrop.Value)
                        {
                            skipped++;
                            report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (current DD{currentDD} < required DD{step.MinDataDrop.Value})");
                            previousStepSkipped = true;
                            stepResults.Add(new WorkflowStepResult { CommandTag = step.CommandTag, Label = step.Label, Status = "SKIPPED" });
                            continue;
                        }
                    }

                    // AE-05 / GAP-09: Skip if data files unchanged (sidecar file for workshared compatibility)
                    if (step.SkipIfDataUnchanged)
                    {
                        try
                        {
                            string currentHash = ComputeDataHash();
                            string storedHash = LoadDataHashSidecar(doc);
                            if (!string.IsNullOrEmpty(storedHash) && currentHash == storedHash)
                            {
                                StingLog.Info($"WorkflowEngine: skipping '{step.Label}' — data files unchanged");
                                report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (data files unchanged)");
                                skipped++;
                                continue;
                            }
                        }
                        catch (Exception dhEx) { StingLog.Warn($"Data hash check: {dhEx.Message}"); }
                    }

                    // F2 / G5.2: Evaluate compliance threshold conditions
                    // PERF-04: Use cached compliance percentage instead of re-scanning each step
                    if (step.MaxCompliancePct.HasValue || step.MinCompliancePct.HasValue
                        || step.RequiresStaleElements)
                    {
                        try
                        {
                            double pct = cachedCompliancePct();
                            if (step.MaxCompliancePct.HasValue && pct > step.MaxCompliancePct.Value)
                            {
                                StingLog.Info($"WorkflowEngine: skipping '{step.Label}' — compliance {pct:F0}% > max {step.MaxCompliancePct.Value}%");
                                report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (compliance {pct:F0}% above {step.MaxCompliancePct.Value}%)");
                                skipped++;
                                continue;
                            }
                            if (step.MinCompliancePct.HasValue && pct < step.MinCompliancePct.Value)
                            {
                                StingLog.Info($"WorkflowEngine: skipping '{step.Label}' — compliance {pct:F0}% < min {step.MinCompliancePct.Value}%");
                                report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (compliance {pct:F0}% below {step.MinCompliancePct.Value}%)");
                                skipped++;
                                continue;
                            }
                            if (step.RequiresStaleElements)
                            {
                                if (!cachedHasStale())
                                {
                                    StingLog.Info($"WorkflowEngine: skipping '{step.Label}' — no stale elements found");
                                    report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (no stale elements)");
                                    skipped++;
                                    continue;
                                }
                            }
                        }
                        catch (Exception condEx)
                        {
                            StingLog.Warn($"WorkflowEngine condition eval for '{step.Label}': {condEx.Message}");
                        }
                    }

                    var sw = Stopwatch.StartNew();
                    try
                    {
                        // AE-01: Retry logic for transient step failures
                        int maxRetries = Math.Min(step.RetryCount, 3);
                        Result stepResult = Result.Failed;
                        for (int attempt = 0; attempt <= maxRetries; attempt++)
                        {
                            if (attempt > 0)
                            {
                                StingLog.Info($"Workflow step {stepNum} retry {attempt}/{maxRetries}: {step.Label}");
                                // PERF-08: Spin-wait with Escape detection instead of Thread.Sleep
                                var retrySw = Stopwatch.StartNew();
                                while (retrySw.ElapsedMilliseconds < step.RetryDelayMs)
                                {
                                    if (EscapeChecker.IsEscapePressed())
                                    {
                                        StingLog.Info($"Workflow step {stepNum} retry cancelled by Escape");
                                        cancelled = true;
                                        break;
                                    }
                                    System.Threading.Thread.Sleep(50); // 50ms poll interval
                                }
                                if (cancelled) break;
                            }
                            try
                            {
                                stepResult = RunCommandByTag(step.CommandTag, commandData, elements);
                                if (stepResult == Result.Succeeded || stepResult == Result.Cancelled) break;
                            }
                            catch (Exception retryEx)
                            {
                                // LOGIC-02: Mark stepResult as Failed on exception so it's never
                                // left at its previous value if exception is caught and retried.
                                stepResult = Result.Failed;
                                StingLog.Warn($"Workflow step {stepNum} attempt {attempt}: {retryEx.Message}");
                                if (attempt == maxRetries) throw;
                            }
                        }
                        sw.Stop();

                        // AG-05 FIX: Post-execution timeout check. Can't abort Revit commands mid-execution
                        // but can warn and treat as failure when a step exceeds its timeout.
                        if (step.TimeoutSeconds > 0 && sw.Elapsed.TotalSeconds > step.TimeoutSeconds)
                        {
                            StingLog.Warn($"Workflow step {stepNum} '{step.Label}' exceeded timeout " +
                                $"({sw.Elapsed.TotalSeconds:F0}s > {step.TimeoutSeconds}s)");
                            if (!step.Optional)
                            {
                                stepResult = Result.Failed;
                                report.AppendLine($"  {stepNum,2}. {step.Label} — TIMEOUT ({sw.Elapsed.TotalSeconds:F0}s > {step.TimeoutSeconds}s limit)");
                            }
                        }

                        string status = stepResult == Result.Succeeded ? "OK" :
                                         stepResult == Result.Cancelled ? "SKIP" : "WARN";
                        report.AppendLine($"  {stepNum,2}. {step.Label} — {status} ({sw.Elapsed.TotalSeconds:F1}s)");

                        // Phase 39: Record per-step result for audit trail
                        stepResults.Add(new WorkflowStepResult
                        {
                            CommandTag = step.CommandTag,
                            Label = step.Label,
                            Status = status,
                            DurationMs = sw.ElapsedMilliseconds
                        });

                        if (stepResult == Result.Succeeded)
                            passed++;
                        else if (step.Optional)
                            skipped++;
                        else
                            failed++;

                        // C-03 FIX: Reset previousStepSkipped after each executed step.
                        // Previously never reset to false, causing cascade-skip to permanently
                        // lock after the first skipped step.
                        previousStepSkipped = (stepResult != Result.Succeeded && stepResult != Result.Cancelled);

                        StingLog.Info($"Workflow step {stepNum}: {step.Label} — {status} ({sw.Elapsed.TotalSeconds:F1}s)");

                        // LOGIC-01: After each successful step that modifies data, invalidate ALL
                        // cached checks so next step's threshold/condition checks reflect current state.
                        // Previously only compliance was invalidated — stale cache was left stale.
                        if (stepResult == Result.Succeeded)
                        {
                            _cachedCompliancePct = null;
                            _cachedHasStale = null; // Force re-check for stale elements
                        }

                        // LOG-06: If rollback enabled and a non-optional step failed, stop
                        if (preset.RollbackOnFailure && stepResult == Result.Failed && !step.Optional)
                        {
                            report.AppendLine($"\n  *** Non-optional step failed — rolling back all changes ***");
                            break;
                        }
                        // GAP-06: If rollback_on_optional_failure, stop on ANY failure including optional
                        if (preset.RollbackOnOptionalFailure && stepResult == Result.Failed)
                        {
                            failed++; // count optional failures too
                            report.AppendLine($"\n  *** Step failed (rollback_on_optional_failure) — rolling back all changes ***");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        report.AppendLine($"  {stepNum,2}. {step.Label} — FAILED: {ex.Message}");
                        StingLog.Error($"Workflow step {stepNum}: {step.Label}", ex);

                        // Phase 39: Record failed step with error detail
                        stepResults.Add(new WorkflowStepResult
                        {
                            CommandTag = step.CommandTag, Label = step.Label,
                            Status = "FAILED", DurationMs = sw.ElapsedMilliseconds,
                            ErrorMessage = ex.Message
                        });

                        if (step.Optional)
                            skipped++;
                        else
                        {
                            failed++;
                            if (preset.RollbackOnFailure)
                            {
                                report.AppendLine($"\n  *** Non-optional step threw exception — rolling back all changes ***");
                                break;
                            }
                        }
                    }
                }
            }
            finally
            {
                // LOG-06: Commit or rollback the TransactionGroup
                if (tg != null)
                {
                    try
                    {
                        if (failed > 0 || cancelled)
                        {
                            tg.RollBack();
                            report.AppendLine("  TransactionGroup: ROLLED BACK");
                            StingLog.Warn($"Workflow '{preset.Name}': rolled back due to {failed} failure(s){(cancelled ? ", cancelled by user" : "")}");
                        }
                        else
                        {
                            tg.Assimilate();
                            report.AppendLine("  TransactionGroup: COMMITTED");
                        }
                    }
                    catch (Exception tgEx)
                    {
                        StingLog.Warn($"Workflow TransactionGroup cleanup: {tgEx.Message}");
                        try { tg.RollBack(); } catch (Exception rbEx) { StingLog.Warn($"TransactionGroup rollback failed: {rbEx.Message}"); }
                    }
                    tg.Dispose();
                }
            }

            totalSw.Stop();

            // FIX-DEEP02: Invalidate caches after workflow chain completes.
            // Chained tag commands (BatchTag, TagAndCombine) update SEQ counters;
            // the auto-tagger cache must reflect the post-chain state.
            try
            {
                ComplianceScan.InvalidateCache();
                StingAutoTagger.InvalidateContext();
            }
            catch (Exception ex) { StingLog.Warn($"Post-workflow cache invalidation failed: {ex.Message}"); }

            // FIX-B09: Check compliance gate after workflow chain completes
            try { TagConfig.CheckComplianceGate(doc, $"Workflow:{preset.Name}"); }
            catch (Exception ex) { StingLog.Warn($"Post-workflow compliance gate check failed: {ex.Message}"); }

            report.AppendLine(new string('─', 50));
            report.AppendLine($"  Complete: {passed}/{preset.Steps.Count} steps OK");
            report.AppendLine($"  Skipped: {skipped}, Failed: {failed}");
            if (cancelled) report.AppendLine("  ⚠ Cancelled by user (Escape)");
            report.AppendLine($"  Duration: {totalSw.Elapsed.TotalSeconds:F1}s");

            TaskDialog td = new TaskDialog($"Workflow: {preset.Name}");
            td.MainInstruction = $"{preset.Name}: {passed}/{preset.Steps.Count} steps complete";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"Workflow '{preset.Name}' complete: {passed}/{preset.Steps.Count} OK, " +
                $"{failed} failed, elapsed={totalSw.Elapsed.TotalSeconds:F1}s");

            // Phase 48: Persist last-workflow memory for "Repeat Last" feature
            _lastWorkflowName = preset.Name;
            _lastWorkflowResult = $"{passed}/{preset.Steps.Count} OK, {failed} failed";
            _lastWorkflowTime = DateTime.Now;
            try { TagConfig.SetConfigValue("LAST_WORKFLOW_NAME", preset.Name); }
            catch (Exception ex) { StingLog.Warn($"Last workflow persistence: {ex.Message}"); }

            // LOG-13: Persist run record as JSONL (one JSON object per line)
            try
            {
                double complianceAfter = 0;
                try
                {
                    ComplianceScan.InvalidateCache(); StingAutoTagger.InvalidateContext();
                    var scan = ComplianceScan.Scan(doc);
                    complianceAfter = scan.CompliancePercent;

                    // R4-C WF-GAP-04: Record trend snapshot after workflow, not just on document open
                    ComplianceTrendTracker.RecordSnapshot(doc, scan);
                }
                catch (Exception ex) { StingLog.Warn($"Post-workflow compliance scan failed: {ex.Message}"); }

                // Phase 39: Capture username from environment for audit trail
                string userName = "";
                try { userName = Environment.UserName ?? ""; }
                catch (Exception ex) { StingLog.Warn($"Username capture: {ex.Message}"); }

                var record = new WorkflowRunRecord
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    PresetName = preset.Name,
                    TotalSteps = preset.Steps.Count,
                    Passed = passed,
                    Failed = failed,
                    Skipped = skipped,
                    DurationSeconds = Math.Round(totalSw.Elapsed.TotalSeconds, 1),
                    Cancelled = cancelled,
                    ComplianceBefore = Math.Round(complianceBefore, 1),
                    ComplianceAfter = Math.Round(complianceAfter, 1),
                    StepResults = stepResults,
                    UserName = userName
                };
                SaveRunRecord(record, doc);
                // GAP-09: Save data hash sidecar after workflow to mark data as processed
                SaveDataHashSidecar(doc);

                // Phase 49: Log to coordination log for audit trail
                try
                {
                    WarningsEngine.LogCoordinationAction(doc,
                        $"Workflow: {preset.Name}",
                        "Workflow",
                        $"Passed: {passed}, Skipped: {skipped}, Failed: {failed}. " +
                        $"Compliance: {complianceBefore:F0}% → {complianceAfter:F0}%",
                        failed > 0 ? "HIGH" : "MEDIUM");
                }
                catch (Exception coordEx) { StingLog.Warn($"Coord log: {coordEx.Message}"); }
            }
            catch (Exception logEx)
            {
                StingLog.Warn($"Workflow log save failed: {logEx.Message}");
            }

            return passed > 0 ? Result.Succeeded : Result.Failed;
        }

        /// <summary>
        /// NG11/AL-07: Public accessor for ResolveCommand — used by auto-run workflow on open.
        /// </summary>
        public static IExternalCommand GetCommandInstance(string tag) => ResolveCommand(tag);

        /// <summary>GAP-09: Load data hash from sidecar file (.sting_data_hash.json) alongside .rvt.</summary>
        private static string LoadDataHashSidecar(Document doc)
        {
            try
            {
                string rvtPath = doc?.PathName;
                if (string.IsNullOrEmpty(rvtPath)) return "";
                string sidecarPath = Path.ChangeExtension(rvtPath, ".sting_data_hash.json");
                if (!File.Exists(sidecarPath)) return "";
                return File.ReadAllText(sidecarPath).Trim();
            }
            catch (Exception ex) { StingLog.Warn($"LoadDataHashSidecar: {ex.Message}"); return ""; }
        }

        /// <summary>GAP-09: Save data hash to sidecar file for workshared model compatibility.</summary>
        internal static void SaveDataHashSidecar(Document doc)
        {
            try
            {
                string rvtPath = doc?.PathName;
                if (string.IsNullOrEmpty(rvtPath)) return;
                string sidecarPath = Path.ChangeExtension(rvtPath, ".sting_data_hash.json");
                string hash = ComputeDataHash();
                if (!string.IsNullOrEmpty(hash))
                    File.WriteAllText(sidecarPath, hash);
            }
            catch (Exception ex) { StingLog.Warn($"SaveDataHashSidecar: {ex.Message}"); }
        }

        /// <summary>AE-05: Compute hash of data directory for change detection.</summary>
        private static string ComputeDataHash()
        {
            string dataPath = StingToolsApp.DataPath;
            if (!Directory.Exists(dataPath)) return "";
            try
            {
                var files = Directory.GetFiles(dataPath, "*.*", SearchOption.TopDirectoryOnly)
                    .OrderBy(f => f).ToArray();
                long totalSize = 0;
                DateTime maxWrite = DateTime.MinValue;
                foreach (var f in files)
                {
                    var fi = new FileInfo(f);
                    totalSize += fi.Length;
                    if (fi.LastWriteTimeUtc > maxWrite) maxWrite = fi.LastWriteTimeUtc;
                }
                return $"{files.Length}_{totalSize}_{maxWrite:yyyyMMddHHmmss}";
            }
            catch (Exception ex) { StingLog.Warn($"ComputeDataHash: {ex.Message}"); return ""; }
        }

        /// <summary>
        /// Run a command by its StingCommandHandler dispatch tag, mapped to IExternalCommand classes.
        /// </summary>
        private static Result RunCommandByTag(string tag, ExternalCommandData data, ElementSet elems)
        {
            IExternalCommand cmd = ResolveCommand(tag);
            if (cmd == null)
            {
                // M-05 FIX: Log error (not just warn) with clear diagnostic message
                // so workflow preset JSON typos are immediately obvious
                StingLog.Error($"WorkflowEngine: unknown command tag '{tag}' — check workflow preset JSON for typos. " +
                    "This step will be reported as FAILED in the workflow report.");
                return Result.Failed;
            }

            string msg = "";
            return cmd.Execute(data, ref msg, elems);
        }

        /// <summary>
        /// Map command tags to IExternalCommand instances.
        /// Covers the most commonly used pipeline commands.
        /// </summary>
        private static IExternalCommand ResolveCommand(string tag)
        {
            switch (tag)
            {
                // Setup
                case "LoadParams": return new Tags.LoadSharedParamsCommand();
                case "MasterSetup": return new Temp.MasterSetupCommand();
                case "ProjectSetup": return new Temp.ProjectSetupCommand();

                // Materials
                case "CreateBLEMaterials": return new Temp.CreateBLEMaterialsCommand();
                case "CreateMEPMaterials": return new Temp.CreateMEPMaterialsCommand();

                // Families
                case "CreateWalls": return new Temp.CreateWallsCommand();
                case "CreateFloors": return new Temp.CreateFloorsCommand();
                case "CreateCeilings": return new Temp.CreateCeilingsCommand();
                case "CreateRoofs": return new Temp.CreateRoofsCommand();
                case "CreateDucts": return new Temp.CreateDuctsCommand();
                case "CreatePipes": return new Temp.CreatePipesCommand();

                // Schedules
                case "FullAutoPopulate": return new Temp.FullAutoPopulateCommand();
                case "BatchSchedules": return new Temp.BatchSchedulesCommand();
                case "EvaluateFormulas": return new Temp.FormulaEvaluatorCommand();

                // Tagging
                case "AutoTag": return new Tags.AutoTagCommand();
                case "BatchTag": return new Tags.BatchTagCommand();
                case "TagAndCombine": return new Tags.TagAndCombineCommand();
                case "TagNewOnly": return new Tags.TagNewOnlyCommand();
                case "TagChanged": return new Tags.TagChangedCommand();
                case "FamilyStagePopulate": return new Tags.FamilyStagePopulateCommand();
                case "CombineParams": return new Tags.CombineParametersCommand();
                case "BuildTags": return new Tags.BuildTagsCommand();

                // Validation
                case "ValidateTags": return new Tags.ValidateTagsCommand();
                case "PreTagAudit": return new Tags.PreTagAuditCommand();
                case "ValidateTemplate": return new Temp.ValidateTemplateCommand();

                // Templates
                case "CreateFilters": return new Temp.CreateFiltersCommand();
                case "CreateWorksets": return new Temp.CreateWorksetsCommand();
                case "ViewTemplates": return new Temp.ViewTemplatesCommand();
                case "AutoAssignTemplates": return new Temp.AutoAssignTemplatesCommand();
                case "AutoFixTemplate": return new Temp.AutoFixTemplateCommand();

                // Styles
                case "CreateFillPatterns": return new Temp.CreateFillPatternsCommand();
                case "CreateLineStyles": return new Temp.CreateLineStylesCommand();
                case "CreateObjectStyles": return new Temp.CreateObjectStylesCommand();
                case "CreateTextStyles": return new Temp.CreateTextStylesCommand();
                case "CreateDimStyles": return new Temp.CreateDimensionStylesCommand();
                case "CreateVGOverrides": return new Temp.CreateVGOverridesCommand();
                case "ApplyFilters": return new Temp.ApplyFiltersToViewsCommand();

                // Docs
                case "BatchCreateViews": return new Docs.BatchCreateViewsCommand();
                case "BatchCreateSheets": return new Docs.BatchCreateSheetsCommand();
                case "DrawingRegister": return new Docs.DrawingRegisterCommand();
                case "AutoNumberSheets": return new Docs.AutoNumberSheetsCommand();

                // Phase 63: New command resolutions
                case "SpatialConnectivityAudit": return new Temp.SpatialConnectivityAuditCommand();
                case "NamingAudit": return new Temp.NamingConventionAuditCommand();
                case "CrossModelClash": return new Temp.CrossModelClashCommand();
                case "MEPClearance": return new Temp.MEPClearanceValidationCommand();
                case "AutoAssignTemplates": return new Temp.AutoAssignTemplatesCommand();
                case "BatchPrintSheets": return new Docs.BatchPrintSheetsCommand();

                // Data Pipeline
                case "DynamicBindings": return new Temp.DynamicBindingsCommand();
                case "BOQExport": return new Temp.BOQExportCommand();
                case "BatchFamilyParams": return new Temp.BatchAddFamilyParamsCommand();
                case "FamilyParamProcessor": return new Temp.FamilyParameterProcessorCommand();

                // Legends
                case "AutoCreateLegends": return new Tags.AutoCreateLegendsCommand();

                // BIM Manager
                case "CreateBEP": return new BIMManager.CreateBEPCommand();
                case "UpdateBEP": return new BIMManager.UpdateBEPCommand();
                case "ExportBEP": return new BIMManager.ExportBEPCommand();
                case "COBieExport": return new BIMManager.COBieExportCommand();
                case "DocumentBriefcase": return new BIMManager.DocumentBriefcaseCommand();

                // Workflow
                case "WorkflowTrend": return new WorkflowTrendCommand();

                // Revision Management (GAP-009)
                case "CreateRevision": return new BIMManager.CreateRevisionCommand();
                case "RevisionDashboard": return new BIMManager.RevisionDashboardCommand();
                case "AutoRevisionCloud": return new BIMManager.AutoRevisionCloudCommand();
                case "AutoRevisionOnTagChange": return new BIMManager.AutoRevisionOnTagChangeCommand();
                case "RevisionTagIntegration": return new BIMManager.RevisionTagIntegrationCommand();
                case "RevisionExport": return new BIMManager.RevisionExportCommand();

                // Additional tagging pipeline (G5)
                case "AutoPopulate": return new Temp.AutoPopulateCommand();
                case "CombineParameters": return new Tags.CombineParametersCommand();
                case "RetagStale": return new Organise.RetagStaleCommand();
                case "AnomalyAutoFix": return new Organise.AnomalyAutoFixCommand();
                case "ResolveAllIssues": return new Tags.ResolveAllIssuesCommand();
                case "SmartPlaceTags": return new Tags.SmartPlaceTagsCommand();
                case "ArrangeTags": return new Tags.ArrangeTagsCommand();
                case "DiscComplianceReport": return new Tags.CompletenessDashboardCommand();

                // FIX-7.2: Previously missing pipeline and IoT command tags
                case "SystemParamPush":      return new Tags.BatchSystemPushCommand();
                case "RepairDuplicateSeq":   return new Tags.RepairDuplicateSeqCommand();
                case "TagSelected":          return new Organise.TagSelectedCommand();
                case "ReTag":                return new Organise.ReTagCommand();
                case "FixDuplicates":        return new Organise.FixDuplicateTagsCommand();
                case "RenumberTags":         return new Organise.RenumberTagsCommand();
                case "CopyTags":             return new Organise.CopyTagsCommand();
                case "Tag3D":                return new Tags.Tag3DCommand();
                case "CheckData":            return new Temp.CheckDataCommand();
                case "LoadSharedParams":     return new Tags.LoadSharedParamsCommand();
                case "PurgeSharedParams":    return new Tags.PurgeSharedParamsCommand();
                case "AssetCondition":       return new Temp.AssetConditionCommand();
                case "MaintenanceSchedule":  return new Temp.MaintenanceScheduleCommand();
                case "WarrantyTracker":      return new Temp.WarrantyTrackerCommand();
                case "HandoverPackage":      return new Temp.HandoverPackageCommand();
                case "DataIntegrityCheck":   return new Temp.DataIntegrityCheckCommand();
                case "StandardsDashboard":   return new Temp.StandardsDashboardCommand();
                case "TagSheets":            return new Tags.TagSheetsCommand();
                case "MapSheets":            return new Tags.MapSheetsCommand();

                // Phase 47: Warnings Manager workflow commands
                case "WarningsDashboard":    return new WarningsDashboardCommand();
                case "WarningsAutoFix":      return new WarningsAutoFixCommand();
                case "WarningsExport":       return new WarningsExportCommand();
                case "WarningsBaseline":     return new WarningsBaselineCommand();
                case "WarningsCompliance":   return new WarningsComplianceCommand();

                // Phase 47: BIM Coordination Center
                case "BIMCoordinationCenter": return new BIMCoordinationCenterCommand();

                // Phase 47: Additional tagging and compliance commands
                case "CompletenessDashboard": return new Tags.CompletenessDashboardCommand();
                case "TagRegisterExport":    return new Organise.TagRegisterExportCommand();
                case "AuditTagsCSV":         return new Organise.AuditTagsCSVCommand();
                case "ModelHealthDashboard": return new BIMManager.ModelHealthDashboardCommand();

                // BIM Coordination Center dispatch targets
                case "FullComplianceDashboard": return new BIMManager.FullComplianceDashboardCommand();
                case "ExportModelHealth":       return new BIMManager.ExportModelHealthCommand();
                case "RaiseIssue":              return new BIMManager.RaiseIssueCommand();
                case "UpdateIssue":             return new BIMManager.UpdateIssueCommand();
                case "SelectIssueElements":     return new BIMManager.SelectIssueElementsCommand();
                case "IssueDashboard":          return new BIMManager.IssueDashboardCommand();
                case "BCFExport":               return new BIMManager.BCFExportCommand();
                case "BCFImport":               return new BIMManager.BCFImportCommand();
                case "RevisionCompare":         return new BIMManager.RevisionCompareCommand();
                case "TrackElementRevisions":   return new BIMManager.TrackElementRevisionsCommand();
                case "IssueSheetsForRevision":  return new BIMManager.IssueSheetsForRevisionCommand();
                case "RevisionNamingEnforce":   return new BIMManager.RevisionNamingEnforceCommand();
                case "BulkRevisionStamp":       return new BIMManager.BulkRevisionStampCommand();
                case "PlatformSync":            return new BIMManager.PlatformSyncCommand();
                case "CDEPackage":              return new BIMManager.CDEPackageCommand();
                case "CDEStatus":               return new BIMManager.CDEStatusCommand();
                case "ValidateDocNaming":       return new BIMManager.ValidateDocNamingCommand();
                case "CreateTransmittal":       return new BIMManager.CreateTransmittalCommand();
                case "ExportToExcel":           return new BIMManager.ExportToExcelCommand();
                case "ImportFromExcel":         return new BIMManager.ImportFromExcelCommand();
                case "ExcelRoundTrip":          return new BIMManager.ExcelRoundTripCommand();
                case "IFCExport":               return new Temp.IFCExportCommand();
                case "ACCPublish":              return new BIMManager.ACCPublishCommand();
                case "SharePointExport":        return new BIMManager.SharePointExportCommand();
                case "WorkflowPreset":          return new WorkflowPresetCommand();
                case "CreateWorkflowPreset":    return new CreateWorkflowPresetCommand();
                case "ListWorkflowPresets":     return new ListWorkflowPresetsCommand();
                case "AddDocument":             return new BIMManager.AddDocumentCommand();
                case "DocumentRegister":        return new BIMManager.DocumentRegisterCommand();
                case "StageComplianceGate":     return new BIMManager.StageComplianceGateCommand();
                case "WarningsSelectElements":  return new WarningsSelectElementsCommand();
                case "WarningsSuppress":        return new WarningsSuppressCommand();

                // 4D/5D Scheduling
                case "AutoSchedule4D":      return new BIMManager.AutoSchedule4DCommand();
                case "AutoCost5D":          return new BIMManager.AutoCost5DCommand();
                case "ViewTimeline4D":      return new BIMManager.ViewTimeline4DCommand();
                case "CostReport5D":        return new BIMManager.CostReport5DCommand();
                case "CashFlow5D":          return new BIMManager.CashFlow5DCommand();
                case "ExportSchedule4D":    return new BIMManager.ExportSchedule4DCommand();
                case "ImportMSProject":     return new BIMManager.ImportMSProjectCommand();
                case "MilestoneRegister":   return new BIMManager.MilestoneRegisterCommand();
                case "PhaseSummary":        return new BIMManager.PhaseSummaryCommand();

                // Phase 55: New workflow command resolutions
                case "ScheduleAudit":           return new Temp.ScheduleAuditCommand();
                case "SchemaValidate":          return new Temp.SchemaValidateCommand();
                case "SheetComplianceCheck":    return new Docs.SheetComplianceCheckCommand();
                case "SheetNamingCheck":        return new Docs.SheetNamingCheckCommand();
                case "TemplateAudit":           return new Temp.TemplateAuditCommand();
                case "TemplateComplianceScore": return new Temp.TemplateComplianceScoreCommand();
                case "ClashDetection":          return new Temp.ClashDetectionCommand();
                case "BatchSystemPush":         return new Tags.BatchSystemPushCommand();
                case "ExportSheetRegister":     return new Docs.ExportSheetRegisterCommand();
                case "COBieHandoverExport":     return new Docs.COBieHandoverExportCommand();
                case "GenerateBEP":             return new BIMManager.GenerateBEPCommand();
                case "WarningsMonitor":         return new WarningsMonitorCommand();

                // Phase 66: Additional command resolutions for comprehensive workflow coverage
                case "DeleteUnusedViews":       return new Docs.DeleteUnusedViewsCommand();
                case "ExportCSV":               return new Temp.ExportCSVCommand();
                case "SheetOrganizer":          return new Docs.SheetOrganizerCommand();
                case "ViewOrganizer":           return new Docs.ViewOrganizerCommand();
                case "SyncOverrides":           return new Temp.SyncTemplateOverridesCommand();
                case "DataDropReadiness":       return new Temp.DataDropReadinessCommand();
                case "WeeklyCoordinatorReport": return new Temp.WeeklyCoordinatorReportCommand();
                case "ExportSchedulesToExcel":  return new BIMManager.ExportSchedulesToExcelCommand();
                case "COBieImport":             return new BIMManager.COBieImportCommand();
                case "UserProductivityReport":  return new Temp.UserProductivityReportCommand();
                case "FederatedCompliance":     return new Temp.FederatedComplianceScanCommand();
                case "ApprovalWorkflow":        return new Temp.ApprovalWorkflowCommand();
                case "RevisionSchedule":        return new BIMManager.RevisionScheduleCommand();
                case "AssignNumbers":           return new Tags.AssignNumbersCommand();
                case "SetSeqScheme":            return new Tags.SetSeqSchemeCommand();
                case "ExportTagMap":            return new Tags.ExportTagMapCommand();
                case "ImportTagMap":            return new Tags.ImportTagMapCommand();
                case "BatchPlaceTags":          return new Tags.BatchPlaceTagsCommand();

                // Phase 67: Additional command tag resolutions
                case "TagSelector":             return new Select.TagSelectorCommand();
                case "ExportTagPositions":      return new Tags.ExportTagPositionsCommand();

                default: return null;
            }
        }

        /// <summary>Returns the closest matching valid command tags for a given invalid tag.</summary>
        private static List<string> GetClosestCommandTags(string invalidTag, int maxResults)
        {
            // All valid command tags from the ResolveCommand switch
            var allTags = new[]
            {
                "LoadParams", "MasterSetup", "ProjectSetup", "CreateBLEMaterials", "CreateMEPMaterials",
                "CreateWalls", "CreateFloors", "CreateCeilings", "CreateRoofs", "CreateDucts", "CreatePipes",
                "FullAutoPopulate", "BatchSchedules", "EvaluateFormulas",
                "AutoTag", "BatchTag", "TagAndCombine", "TagNewOnly", "TagChanged", "FamilyStagePopulate",
                "CombineParams", "BuildTags", "ValidateTags", "PreTagAudit", "ValidateTemplate",
                "CreateFilters", "CreateWorksets", "ViewTemplates", "AutoAssignTemplates", "AutoFixTemplate",
                "CreateFillPatterns", "CreateLineStyles", "CreateObjectStyles", "CreateTextStyles",
                "CreateDimStyles", "CreateVGOverrides", "ApplyFilters",
                "BatchCreateViews", "BatchCreateSheets", "DrawingRegister", "AutoNumberSheets",
                "SpatialConnectivityAudit", "NamingAudit", "CrossModelClash", "MEPClearance", "BatchPrintSheets",
                "DynamicBindings", "BOQExport", "BatchFamilyParams", "FamilyParamProcessor",
                "AutoCreateLegends", "CreateBEP", "UpdateBEP", "ExportBEP", "COBieExport", "DocumentBriefcase",
                "WorkflowTrend", "CreateRevision", "RevisionDashboard", "AutoRevisionCloud",
                "AutoRevisionOnTagChange", "RevisionTagIntegration", "RevisionExport",
                "AutoPopulate", "CombineParameters", "RetagStale", "AnomalyAutoFix", "ResolveAllIssues",
                "SmartPlaceTags", "ArrangeTags", "DiscComplianceReport",
                "SystemParamPush", "RepairDuplicateSeq", "TagSelected", "ReTag", "FixDuplicates",
                "RenumberTags", "CopyTags", "Tag3D", "CheckData", "LoadSharedParams", "PurgeSharedParams",
                "AssetCondition", "MaintenanceSchedule", "WarrantyTracker", "HandoverPackage",
                "DataIntegrityCheck", "StandardsDashboard", "TagSheets", "MapSheets",
                "WarningsDashboard", "WarningsAutoFix", "WarningsExport", "WarningsBaseline",
                "WarningsCompliance", "BIMCoordinationCenter", "CompletenessDashboard", "TagRegisterExport",
                "AuditTagsCSV", "ModelHealthDashboard", "FullComplianceDashboard", "ExportModelHealth",
                "RaiseIssue", "UpdateIssue", "SelectIssueElements", "IssueDashboard",
                "BCFExport", "BCFImport", "RevisionCompare", "TrackElementRevisions",
                "IssueSheetsForRevision", "RevisionNamingEnforce", "BulkRevisionStamp",
                "PlatformSync", "CDEPackage", "CDEStatus", "ValidateDocNaming", "CreateTransmittal",
                "ExportToExcel", "ImportFromExcel", "ExcelRoundTrip", "IFCExport",
                "ACCPublish", "SharePointExport", "WorkflowPreset", "CreateWorkflowPreset",
                "ListWorkflowPresets", "AddDocument", "DocumentRegister", "StageComplianceGate",
                "WarningsSelectElements", "WarningsSuppress",
                "AutoSchedule4D", "AutoCost5D", "ViewTimeline4D", "CostReport5D", "CashFlow5D",
                "ExportSchedule4D", "ImportMSProject", "MilestoneRegister", "PhaseSummary",
                "ScheduleAudit", "SchemaValidate", "SheetComplianceCheck", "SheetNamingCheck",
                "TemplateAudit", "TemplateComplianceScore", "ClashDetection", "BatchSystemPush",
                "ExportSheetRegister", "COBieHandoverExport", "GenerateBEP", "WarningsMonitor",
                "DeleteUnusedViews", "ExportCSV", "SheetOrganizer", "ViewOrganizer", "SyncOverrides",
                "DataDropReadiness", "WeeklyCoordinatorReport", "ExportSchedulesToExcel", "COBieImport",
                "UserProductivityReport", "FederatedCompliance", "ApprovalWorkflow", "RevisionSchedule",
                "AssignNumbers", "SetSeqScheme", "ExportTagMap", "ImportTagMap", "BatchPlaceTags",
                "TagSelector", "ExportTagPositions"
            };

            string lowerInvalid = invalidTag.ToLowerInvariant();
            return allTags
                .Select(t => new { Tag = t, Dist = LevenshteinDistance(lowerInvalid, t.ToLowerInvariant()) })
                .OrderBy(x => x.Dist)
                .Take(maxResults)
                .Select(x => x.Tag)
                .ToList();
        }

        /// <summary>Simple Levenshtein distance for command tag suggestion.</summary>
        private static int LevenshteinDistance(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
            if (string.IsNullOrEmpty(b)) return a.Length;
            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            return d[a.Length, b.Length];
        }

        /// <summary>FIX-7.1: Public wrapper so NLPCommandProcessorCommand can call it.</summary>
        public static IExternalCommand ResolveCommandPublic(string tag) => ResolveCommand(tag);

        // ── Phase 69: Compound condition evaluation ─────────────────────

        /// <summary>Evaluate a single named condition against the current document state.</summary>
        private static bool EvaluateSingleCondition(Document doc, string condition,
            Func<double> cachedCompliancePct, Func<bool> cachedHasStale)
        {
            try
            {
                switch (condition)
                {
                    case "has_stale": return cachedHasStale();
                    case "has_warnings": return (doc.GetWarnings()?.Count ?? 0) > 0;
                    case "has_critical_warnings":
                        var wr = WarningsEngine.ScanWarnings(doc);
                        return wr.BySeverity.GetValueOrDefault(WarningSeverity.Critical) > 0;
                    case "has_open_issues":
                        string iPath = Path.Combine(Path.GetDirectoryName(doc.PathName ?? "") ?? "", "_bim_manager", "issues.json");
                        if (!File.Exists(iPath)) return false;
                        return File.ReadAllText(iPath).Contains("\"OPEN\"");
                    case "has_untagged":
                        var cats = SharedParamGuids.AllCategoryEnums;
                        var coll = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                        if (cats != null && cats.Length > 0) coll.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(cats)));
                        return coll.Any(e => string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.TAG1)));
                    case "has_placeholders":
                        var cats2 = SharedParamGuids.AllCategoryEnums;
                        var coll2 = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                        if (cats2 != null && cats2.Length > 0) coll2.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(cats2)));
                        return coll2.Any(e => { string t = ParameterHelpers.GetString(e, ParamRegistry.TAG1); return !string.IsNullOrEmpty(t) && TagConfig.TagHasPlaceholders(t); });
                    case "has_container_gaps":
                        var scan = ComplianceScan.Scan(doc);
                        return (scan?.ContainerCompletePct ?? 100) < 95;
                    case "has_links":
                        return new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).GetElementCount() > 0;
                    case "compliance_above_90": return cachedCompliancePct() >= 90;
                    case "compliance_below_50": return cachedCompliancePct() < 50;
                    case "workshared": return doc.IsWorkshared;
                    default:
                        StingLog.Warn($"WorkflowEngine: unknown condition '{condition}'");
                        return true; // Unknown conditions pass by default
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"WorkflowEngine: condition '{condition}' failed: {ex.Message}");
                return true; // On error, don't skip the step
            }
        }

        /// <summary>
        /// Calculate current ISO 19650 data drop level based on compliance percentage.
        /// DD1=30%, DD2=60%, DD3=85%, DD4=95%.
        /// </summary>
        public static int CalculateCurrentDataDrop(Document doc, double compliancePct)
        {
            if (compliancePct >= 95) return 4; // DD4: Handover
            if (compliancePct >= 85) return 3; // DD3: Detailed Design
            if (compliancePct >= 60) return 2; // DD2: Concept Design
            if (compliancePct >= 30) return 1; // DD1: Brief
            return 0; // Pre-DD1
        }

        /// <summary>Get data drop compliance thresholds (configurable per project).</summary>
        public static (int shared, int published) GetDataDropGates(int dataDrop)
        {
            return dataDrop switch
            {
                1 => (30, 50),
                2 => (60, 75),
                3 => (80, 90),
                4 => (95, 98),
                _ => (70, 90),
            };
        }

        /// <summary>Get all available presets (built-in + user JSON files).</summary>
        public static List<WorkflowPreset> GetAvailablePresets()
        {
            var presets = new List<WorkflowPreset>();

            // Built-in presets
            presets.Add(GetBuiltInPreset("ProjectKickoff"));
            presets.Add(GetBuiltInPreset("DailyQA"));
            presets.Add(GetBuiltInPreset("DocumentPackage"));
            presets.Add(GetBuiltInPreset("BEPPackage"));
            presets.Add(GetBuiltInPreset("PostTaggingQA"));
            presets.Add(GetBuiltInPreset("MorningHealthCheck"));
            presets.Add(GetBuiltInPreset("HandoverReadiness"));
            presets.Add(GetBuiltInPreset("WeeklyDataDrop"));
            presets.Add(GetBuiltInPreset("CoordinationMeetingPrep"));
            presets.Add(GetBuiltInPreset("ClashCoordination"));
            presets.Add(GetBuiltInPreset("EndOfStageGate"));
            presets.Add(GetBuiltInPreset("QuickFixCycle"));
            presets.Add(GetBuiltInPreset("ModelAuditDeep"));
            presets.Add(GetBuiltInPreset("MEPCoordination"));
            presets.Add(GetBuiltInPreset("CDE_Submission"));
            presets.Add(GetBuiltInPreset("DesignReviewPrep"));
            // WF-GAP-01: Discipline-specific presets
            presets.Add(GetBuiltInPreset("Healthcare_NHS"));
            presets.Add(GetBuiltInPreset("DataCentre"));
            presets.Add(GetBuiltInPreset("CommercialOffice"));
            presets.Add(GetBuiltInPreset("Residential"));
            presets.Add(GetBuiltInPreset("Education"));
            // Phase 63: BIM coordinator automation presets
            presets.Add(GetBuiltInPreset("IssueResolution"));
            presets.Add(GetBuiltInPreset("ClientReviewPrep"));
            presets.Add(GetBuiltInPreset("RegulatoryScan"));

            // Phase 66: New BIM coordinator automation presets
            presets.Add(GetBuiltInPreset("EndOfDaySync"));
            presets.Add(GetBuiltInPreset("FederatedModelAudit"));
            presets.Add(GetBuiltInPreset("PreMeetingPrep"));

            // Remove any null entries from failed lookups
            presets.RemoveAll(p => p == null);

            // User-defined JSON files
            string dataDir = StingToolsApp.DataPath;
            if (Directory.Exists(dataDir))
            {
                foreach (string file in Directory.GetFiles(dataDir, "WORKFLOW_*.json"))
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var preset = JsonConvert.DeserializeObject<WorkflowPreset>(json);
                        if (preset != null && preset.Steps.Count > 0)
                        {
                            preset.IsBuiltIn = false;
                            // Avoid duplicating built-in names
                            if (!presets.Any(p => p.Name == preset.Name))
                                presets.Add(preset);
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Failed to load workflow preset '{file}': {ex.Message}");
                    }
                }
            }

            return presets;
        }

        /// <summary>Get a built-in workflow preset by name.</summary>
        public static WorkflowPreset GetBuiltInPreset(string name)
        {
            switch (name)
            {
                case "ProjectKickoff":
                    return new WorkflowPreset
                    {
                        Name = "Project Kickoff",
                        Description = "Full project setup from blank template — zero manual steps",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "LoadParams", Label = "Load Shared Parameters (200+ params)" },
                            new WorkflowStep { CommandTag = "CreateBLEMaterials", Label = "Create BLE Materials (815)" },
                            new WorkflowStep { CommandTag = "CreateMEPMaterials", Label = "Create MEP Materials (464)" },
                            new WorkflowStep { CommandTag = "CreateWalls", Label = "Create Wall Types" },
                            new WorkflowStep { CommandTag = "CreateFloors", Label = "Create Floor Types" },
                            new WorkflowStep { CommandTag = "CreateCeilings", Label = "Create Ceiling Types" },
                            new WorkflowStep { CommandTag = "CreateRoofs", Label = "Create Roof Types" },
                            new WorkflowStep { CommandTag = "CreateDucts", Label = "Create Duct Types" },
                            new WorkflowStep { CommandTag = "CreatePipes", Label = "Create Pipe Types" },
                            new WorkflowStep { CommandTag = "BatchSchedules", Label = "Batch Create Schedules (168)" },
                            new WorkflowStep { CommandTag = "EvaluateFormulas", Label = "Evaluate Formulas (199)" },
                            new WorkflowStep { CommandTag = "CreateFilters", Label = "Create View Filters (28+)" },
                            new WorkflowStep { CommandTag = "CreateWorksets", Label = "Create Worksets (35)", Condition = "workshared", Optional = true },
                            new WorkflowStep { CommandTag = "ViewTemplates", Label = "Create View Templates (23)" },
                            new WorkflowStep { CommandTag = "CreateFillPatterns", Label = "Create Fill Patterns" },
                            new WorkflowStep { CommandTag = "CreateLineStyles", Label = "Create Line Styles" },
                            new WorkflowStep { CommandTag = "CreateObjectStyles", Label = "Create Object Styles" },
                            new WorkflowStep { CommandTag = "CreateTextStyles", Label = "Create Text Styles" },
                            new WorkflowStep { CommandTag = "CreateDimStyles", Label = "Create Dimension Styles" },
                            new WorkflowStep { CommandTag = "ApplyFilters", Label = "Apply Filters to Templates" },
                            new WorkflowStep { CommandTag = "CreateVGOverrides", Label = "Apply VG Overrides" },
                            new WorkflowStep { CommandTag = "BatchFamilyParams", Label = "Batch Family Params (4,686)" },
                            new WorkflowStep { CommandTag = "AutoAssignTemplates", Label = "Auto-Assign Templates (5-layer)" },
                            new WorkflowStep { CommandTag = "AutoFixTemplate", Label = "Auto-Fix Template Health" },
                            new WorkflowStep { CommandTag = "TagAndCombine", Label = "Tag & Combine (full pipeline)" },
                            new WorkflowStep { CommandTag = "AutoCreateLegends", Label = "Auto-Create Legends" },
                            new WorkflowStep { CommandTag = "TagSheets", Label = "Tag Sheets (ISO 19650 doc codes)" },
                            new WorkflowStep { CommandTag = "CreateRevision", Label = "Create Baseline Revision (P01)" },
                        }
                    };

                case "DailyQA":
                    return new WorkflowPreset
                    {
                        Name = "Daily QA Sync",
                        Description = "Adaptive daily sync — skips steps already meeting compliance thresholds. Full pipeline: tag new, delta-sync changed, retag stale, sync native params, evaluate formulas, update containers, validate, fix templates.",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            // GAP-07: RetagStale as first step to fix stale elements before any other tagging
                            new WorkflowStep { CommandTag = "RetagStale", Label = "Retag stale elements first", Optional = true, RequiresStaleElements = true },
                            new WorkflowStep { CommandTag = "PreTagAudit", Label = "Pre-tag dry-run audit (skip if already compliant)", Optional = true, MaxCompliancePct = 95 },
                            new WorkflowStep { CommandTag = "TagNewOnly", Label = "Tag new elements only" },
                            new WorkflowStep { CommandTag = "TagChanged", Label = "Update changed element tokens (delta sync)" },
                            new WorkflowStep { CommandTag = "AutoPopulate", Label = "Sync Revit native params → STING shared" },
                            new WorkflowStep { CommandTag = "EvaluateFormulas", Label = "Evaluate 199 dependency formulas" },
                            new WorkflowStep { CommandTag = "CombineParameters", Label = "Update all tag containers" },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "Validate ISO 19650 compliance" },
                            new WorkflowStep { CommandTag = "ValidateTemplate", Label = "Validate data integrity (45 checks)" },
                            new WorkflowStep { CommandTag = "AutoAssignTemplates", Label = "Re-assign templates to new views" },
                            new WorkflowStep { CommandTag = "AutoFixTemplate", Label = "Auto-fix template issues" },
                            new WorkflowStep { CommandTag = "AutoRevisionOnTagChange", Label = "Auto-revision check (score-based)" },
                            new WorkflowStep { CommandTag = "TagSheets", Label = "Tag sheets with ISO 19650 document codes", Optional = true },
                        }
                    };

                case "DocumentPackage":
                    return new WorkflowPreset
                    {
                        Name = "Document Package",
                        Description = "Create views, sheets, drawing register, and BOQ export",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "BatchCreateViews", Label = "Batch Create Views" },
                            new WorkflowStep { CommandTag = "BatchCreateSheets", Label = "Batch Create Sheets" },
                            new WorkflowStep { CommandTag = "AutoNumberSheets", Label = "Auto-Number Sheets" },
                            new WorkflowStep { CommandTag = "AutoAssignTemplates", Label = "Assign View Templates" },
                            new WorkflowStep { CommandTag = "TagSheets", Label = "Tag sheets with ISO 19650 document codes" },
                            new WorkflowStep { CommandTag = "DrawingRegister", Label = "Generate Drawing Register" },
                            new WorkflowStep { CommandTag = "BOQExport", Label = "Export Bill of Quantities" },
                        }
                    };

                case "BEPPackage":
                    return new WorkflowPreset
                    {
                        Name = "BEP Package",
                        Description = "ISO 19650 BIM Execution Plan generation pipeline",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "CreateBEP", Label = "Create BEP from Wizard" },
                            new WorkflowStep { CommandTag = "UpdateBEP", Label = "Enrich BEP with Model Data" },
                            new WorkflowStep { CommandTag = "ExportBEP", Label = "Export BEP to XLSX" },
                            new WorkflowStep { CommandTag = "COBieExport", Label = "COBie V2.4 Export" },
                            new WorkflowStep { CommandTag = "DocumentBriefcase", Label = "Document Briefcase" },
                        }
                    };

                case "PostTaggingQA":
                    return new WorkflowPreset
                    {
                        Name = "Post-Tagging QA",
                        Description = "Validate tagging results: ISO compliance, token completeness, containers, register export",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "PreTagAudit", Label = "Pre-Tag Audit (dry run)" },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "Validate Tags (ISO 19650)" },
                            new WorkflowStep { CommandTag = "CompletenessDashboard", Label = "Completeness Dashboard" },
                            new WorkflowStep { CommandTag = "TagRegisterExport", Label = "Export Tag Register (CSV)" },
                            new WorkflowStep { CommandTag = "ValidateTemplate", Label = "Validate BIM Template (45 checks)" },
                        }
                    };

                // Phase 47: Morning Health Check — BIM coordinator daily morning routine
                case "MorningHealthCheck":
                    return new WorkflowPreset
                    {
                        Name = "Morning Health Check",
                        Description = "BIM coordinator daily morning routine: stale fix, warnings triage, compliance check, issue review",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "RetagStale", Label = "1. Fix stale elements", Optional = true, RequiresStaleElements = true },
                            new WorkflowStep { CommandTag = "WarningsAutoFix", Label = "2. Auto-fix model warnings", Optional = true, Condition = "has_warnings" },
                            new WorkflowStep { CommandTag = "TagNewOnly", Label = "3. Tag new elements", MaxCompliancePct = 98 },
                            new WorkflowStep { CommandTag = "PreTagAudit", Label = "4. Pre-tag audit", Optional = true, MaxCompliancePct = 95 },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "5. Validate ISO 19650 compliance" },
                            new WorkflowStep { CommandTag = "AutoAssignTemplates", Label = "6. Re-assign view templates", Optional = true },
                            new WorkflowStep { CommandTag = "TagSheets", Label = "7. Tag sheets", Optional = true },
                            new WorkflowStep { CommandTag = "AutoRevisionOnTagChange", Label = "8. Auto-revision check", Optional = true },
                        }
                    };

                // Phase 47: Handover Readiness — pre-handover validation and export
                case "HandoverReadiness":
                    return new WorkflowPreset
                    {
                        Name = "Handover Readiness",
                        Description = "ISO 19650 handover preparation: full validation, COBie export, drawing register, compliance gate",
                        IsBuiltIn = true,
                        RollbackOnFailure = false,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "RetagStale", Label = "1. Fix stale elements", Optional = true, RequiresStaleElements = true },
                            new WorkflowStep { CommandTag = "TagAndCombine", Label = "2. Full tag pipeline", MaxCompliancePct = 95 },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "3. ISO 19650 validation" },
                            new WorkflowStep { CommandTag = "ValidateTemplate", Label = "4. BIM template validation (45 checks)" },
                            new WorkflowStep { CommandTag = "COBieExport", Label = "5. COBie V2.4 export", MinCompliancePct = 70 },
                            new WorkflowStep { CommandTag = "DrawingRegister", Label = "6. Drawing register" },
                            new WorkflowStep { CommandTag = "BOQExport", Label = "7. BOQ export" },
                            new WorkflowStep { CommandTag = "UpdateBEP", Label = "8. Update BEP with model data" },
                            new WorkflowStep { CommandTag = "CreateRevision", Label = "9. Create handover revision" },
                        }
                    };

                // Phase 47: Weekly Data Drop — ISO 19650 information exchange
                case "WeeklyDataDrop":
                    return new WorkflowPreset
                    {
                        Name = "Weekly Data Drop",
                        Description = "ISO 19650 weekly information exchange: validate, export, package for CDE",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "RetagStale", Label = "1. Fix stale elements", Optional = true, RequiresStaleElements = true },
                            new WorkflowStep { CommandTag = "ResolveAllIssues", Label = "2. Resolve placeholder tokens", MaxCompliancePct = 95, Optional = true },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "3. Validate ISO 19650" },
                            new WorkflowStep { CommandTag = "TagRegisterExport", Label = "4. Export asset register CSV" },
                            new WorkflowStep { CommandTag = "COBieExport", Label = "5. COBie V2.4 export", MinCompliancePct = 60 },
                            new WorkflowStep { CommandTag = "AutoNumberSheets", Label = "6. Auto-number sheets" },
                            new WorkflowStep { CommandTag = "DrawingRegister", Label = "7. Drawing register" },
                            new WorkflowStep { CommandTag = "CreateRevision", Label = "8. Create weekly revision" },
                        }
                    };

                // Phase 49: Coordination Meeting Prep — prepare for BIM coordination meeting
                case "CoordinationMeetingPrep":
                    return new WorkflowPreset
                    {
                        Name = "Coordination Meeting Prep",
                        Description = "Prepare model for BIM coordination meeting: compliance check, warnings triage, issue summary, export reports",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "RetagStale", Label = "1. Fix stale elements", Optional = true, RequiresStaleElements = true },
                            new WorkflowStep { CommandTag = "WarningsAutoFix", Label = "2. Auto-fix model warnings", Optional = true, Condition = "has_warnings" },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "3. Validate ISO 19650 compliance" },
                            new WorkflowStep { CommandTag = "CompletenessDashboard", Label = "4. Generate compliance dashboard" },
                            new WorkflowStep { CommandTag = "WarningsExport", Label = "5. Export warnings report", Optional = true, Condition = "has_warnings" },
                            new WorkflowStep { CommandTag = "TagRegisterExport", Label = "6. Export asset register" },
                            new WorkflowStep { CommandTag = "ModelHealthDashboard", Label = "7. Model health check" },
                        }
                    };

                // Phase 49: Clash Coordination — resolve clashes and coordination issues
                case "ClashCoordination":
                    return new WorkflowPreset
                    {
                        Name = "Clash Coordination",
                        Description = "Cross-discipline clash detection and coordination: warnings, clashes, BCF, issue creation",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "ClashDetection", Label = "1. Run clash detection" },
                            new WorkflowStep { CommandTag = "WarningsDashboard", Label = "2. Warnings dashboard" },
                            new WorkflowStep { CommandTag = "BCFExport", Label = "3. Export clashes as BCF", Optional = true },
                            new WorkflowStep { CommandTag = "RaiseIssue", Label = "4. Create coordination issues", Optional = true, Condition = "has_critical_warnings" },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "5. Validate tags after fixes" },
                        }
                    };

                // Phase 49: End-of-Stage Gate — RIBA stage transition validation
                case "EndOfStageGate":
                    return new WorkflowPreset
                    {
                        Name = "End of Stage Gate",
                        Description = "RIBA stage transition gate: comprehensive validation, COBie, BEP update, compliance report",
                        IsBuiltIn = true,
                        RollbackOnFailure = false,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "RetagStale", Label = "1. Fix all stale elements", RequiresStaleElements = true, Optional = true },
                            new WorkflowStep { CommandTag = "ResolveAllIssues", Label = "2. Resolve all placeholder tokens" },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "3. Full ISO 19650 validation" },
                            new WorkflowStep { CommandTag = "ValidateTemplate", Label = "4. BIM template validation (45 checks)" },
                            new WorkflowStep { CommandTag = "StageComplianceGate", Label = "5. RIBA stage compliance gate" },
                            new WorkflowStep { CommandTag = "COBieExport", Label = "6. COBie V2.4 export", MinCompliancePct = 80 },
                            new WorkflowStep { CommandTag = "ExportBEP", Label = "7. Export BEP" },
                            new WorkflowStep { CommandTag = "DrawingRegister", Label = "8. Drawing register" },
                            new WorkflowStep { CommandTag = "TagRegisterExport", Label = "9. Asset register export" },
                            new WorkflowStep { CommandTag = "BOQExport", Label = "10. BOQ export" },
                            new WorkflowStep { CommandTag = "CreateRevision", Label = "11. Create stage revision" },
                        }
                    };

                // Phase 49: Quick Fix Cycle — rapid model quality improvement
                case "QuickFixCycle":
                    return new WorkflowPreset
                    {
                        Name = "Quick Fix Cycle",
                        Description = "Rapid quality improvement: auto-fix warnings, resolve tokens, re-tag stale, validate",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "WarningsAutoFix", Label = "1. Auto-fix warnings", Optional = true, Condition = "has_warnings" },
                            new WorkflowStep { CommandTag = "RetagStale", Label = "2. Re-tag stale elements", RequiresStaleElements = true, Optional = true },
                            new WorkflowStep { CommandTag = "AnomalyAutoFix", Label = "3. Fix tag anomalies" },
                            new WorkflowStep { CommandTag = "ResolveAllIssues", Label = "4. Resolve placeholders", MaxCompliancePct = 95, Optional = true },
                            new WorkflowStep { CommandTag = "CombineParameters", Label = "5. Update containers" },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "6. Validate results" },
                        }
                    };

                // Phase 55: New BIM coordinator automation presets
                case "ModelAuditDeep":
                    return new WorkflowPreset
                    {
                        Name = "Model Audit Deep",
                        Description = "Comprehensive model health: warnings, templates, parameters, schedules, compliance",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "WarningsDashboard", Label = "1. Warning analysis" },
                            new WorkflowStep { CommandTag = "TemplateAudit", Label = "2. Template audit" },
                            new WorkflowStep { CommandTag = "ValidateTemplate", Label = "3. Data pipeline validation (45 checks)" },
                            new WorkflowStep { CommandTag = "ScheduleAudit", Label = "4. Schedule audit" },
                            new WorkflowStep { CommandTag = "SchemaValidate", Label = "5. Material schema validation" },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "6. Tag validation" },
                            new WorkflowStep { CommandTag = "SheetComplianceCheck", Label = "7. Sheet compliance" },
                            new WorkflowStep { CommandTag = "FullComplianceDashboard", Label = "8. Full compliance dashboard" },
                        }
                    };

                case "MEPCoordination":
                    return new WorkflowPreset
                    {
                        Name = "MEP Coordination",
                        Description = "MEP system validation: clashes, system integrity, sizing, tagging",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "ClashDetection", Label = "1. Clash detection" },
                            new WorkflowStep { CommandTag = "BatchSystemPush", Label = "2. MEP system push" },
                            new WorkflowStep { CommandTag = "RetagStale", Label = "3. Re-tag moved elements", RequiresStaleElements = true, Optional = true },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "4. Validate tags" },
                            new WorkflowStep { CommandTag = "WarningsAutoFix", Label = "5. Auto-fix warnings", Condition = "has_warnings", Optional = true },
                            new WorkflowStep { CommandTag = "CompletenessDashboard", Label = "6. Compliance check" },
                        }
                    };

                case "CDE_Submission":
                    return new WorkflowPreset
                    {
                        Name = "CDE Submission",
                        Description = "ISO 19650 CDE submission package: validate, export, register, transmittal",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "RetagStale", Label = "1. Fix stale elements", RequiresStaleElements = true, Optional = true },
                            new WorkflowStep { CommandTag = "ResolveAllIssues", Label = "2. Resolve placeholders", MaxCompliancePct = 98, Optional = true },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "3. Final tag validation" },
                            new WorkflowStep { CommandTag = "SheetNamingCheck", Label = "4. Sheet naming check" },
                            new WorkflowStep { CommandTag = "ValidateDocNaming", Label = "5. Document naming check" },
                            new WorkflowStep { CommandTag = "TagRegisterExport", Label = "6. Export tag register" },
                            new WorkflowStep { CommandTag = "ExportSheetRegister", Label = "7. Export sheet register" },
                            new WorkflowStep { CommandTag = "CreateTransmittal", Label = "8. Create transmittal" },
                        }
                    };

                case "DesignReviewPrep":
                    return new WorkflowPreset
                    {
                        Name = "Design Review Prep",
                        Description = "Pre-design review: template check, view preparation, annotation cleanup",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "AutoAssignTemplates", Label = "1. Auto-assign view templates" },
                            new WorkflowStep { CommandTag = "WarningsAutoFix", Label = "2. Auto-fix warnings", Condition = "has_warnings", Optional = true },
                            new WorkflowStep { CommandTag = "SheetNamingCheck", Label = "3. Sheet naming audit" },
                            new WorkflowStep { CommandTag = "TemplateComplianceScore", Label = "4. Template compliance scores" },
                            new WorkflowStep { CommandTag = "CompletenessDashboard", Label = "5. Tag completeness report" },
                        }
                    };

                // ═══ WF-GAP-01: Discipline-specific workflow presets ═══

                case "Healthcare_NHS":
                    return new WorkflowPreset
                    {
                        Name = "Healthcare NHS",
                        Description = "NHS/HTM compliance: medical gas tagging, infection zones, room data sheets, COBie for CAFM",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "RetagStale", Label = "1. Retag stale elements", Optional = true, RequiresStaleElements = true },
                            new WorkflowStep { CommandTag = "BatchTag", Label = "2. Full batch tag (all disciplines)" },
                            new WorkflowStep { CommandTag = "BatchSystemPush", Label = "3. MEP system parameter push (medical gas/LTHW/CHW)" },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "4. Validate ISO 19650 compliance" },
                            new WorkflowStep { CommandTag = "RoomSpaceAudit", Label = "5. Room audit (infection zones, department, area)" },
                            new WorkflowStep { CommandTag = "COBieExport", Label = "6. COBie V2.4 export (NHS preset)" },
                            new WorkflowStep { CommandTag = "AssetCondition", Label = "7. Asset condition survey (ISO 15686)", Optional = true },
                            new WorkflowStep { CommandTag = "MaintenanceSchedule", Label = "8. Maintenance schedule (SFG20/HTM)", Optional = true },
                            new WorkflowStep { CommandTag = "HandoverManual", Label = "9. FM handover manual" },
                            new WorkflowStep { CommandTag = "FullComplianceDashboard", Label = "10. Full compliance dashboard" },
                        }
                    };

                case "DataCentre":
                    return new WorkflowPreset
                    {
                        Name = "Data Centre",
                        Description = "Data centre: cable tray capacity, power distribution, cooling zones, Uptime Institute compliance",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "RetagStale", Label = "1. Retag stale elements", Optional = true, RequiresStaleElements = true },
                            new WorkflowStep { CommandTag = "BatchTag", Label = "2. Full batch tag (MEP-focused)" },
                            new WorkflowStep { CommandTag = "BatchSystemPush", Label = "3. MEP system push (power/cooling/data)" },
                            new WorkflowStep { CommandTag = "ClashDetection", Label = "4. Clash detection (cable tray vs duct)" },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "5. Validate tags" },
                            new WorkflowStep { CommandTag = "MEPSizingCheck", Label = "6. MEP sizing check" },
                            new WorkflowStep { CommandTag = "AutoCost5D", Label = "7. 5D cost estimate" },
                            new WorkflowStep { CommandTag = "COBieExport", Label = "8. COBie export (Data Centre preset)" },
                            new WorkflowStep { CommandTag = "ExportModelHealth", Label = "9. Model health export" },
                            new WorkflowStep { CommandTag = "FullComplianceDashboard", Label = "10. Compliance dashboard" },
                        }
                    };

                case "CommercialOffice":
                    return new WorkflowPreset
                    {
                        Name = "Commercial Office",
                        Description = "Commercial office: BCO Guide compliance, BREEAM data, lease demise tagging, occupancy schedules",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "RetagStale", Label = "1. Retag stale elements", Optional = true, RequiresStaleElements = true },
                            new WorkflowStep { CommandTag = "TagAndCombine", Label = "2. Tag & combine (full pipeline)" },
                            new WorkflowStep { CommandTag = "RoomSpaceAudit", Label = "3. Room/space audit (BCO/NIA)" },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "4. Validate ISO 19650 compliance" },
                            new WorkflowStep { CommandTag = "AutoSchedule4D", Label = "5. 4D schedule generation" },
                            new WorkflowStep { CommandTag = "AutoCost5D", Label = "6. 5D cost estimate" },
                            new WorkflowStep { CommandTag = "COBieExport", Label = "7. COBie export (Commercial preset)" },
                            new WorkflowStep { CommandTag = "TagRegisterExport", Label = "8. Asset register export" },
                            new WorkflowStep { CommandTag = "FullComplianceDashboard", Label = "9. Compliance dashboard" },
                        }
                    };

                case "Residential":
                    return new WorkflowPreset
                    {
                        Name = "Residential",
                        Description = "Residential: dwelling units, building regs Part L/M/B, plot numbering, sales schedules",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "RetagStale", Label = "1. Retag stale elements", Optional = true, RequiresStaleElements = true },
                            new WorkflowStep { CommandTag = "TagAndCombine", Label = "2. Tag & combine (full pipeline)" },
                            new WorkflowStep { CommandTag = "RoomSpaceAudit", Label = "3. Room audit (dwelling areas, Part M)" },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "4. Validate tags" },
                            new WorkflowStep { CommandTag = "SheetNamingCheck", Label = "5. Sheet naming compliance" },
                            new WorkflowStep { CommandTag = "AutoNumberSheets", Label = "6. Auto-number sheets by discipline" },
                            new WorkflowStep { CommandTag = "BOQExport", Label = "7. BOQ export" },
                            new WorkflowStep { CommandTag = "FullComplianceDashboard", Label = "8. Compliance dashboard" },
                        }
                    };

                case "Education":
                    return new WorkflowPreset
                    {
                        Name = "Education",
                        Description = "Education: BB103 area data, DfE Output Specification, safeguarding zones, FF&E schedules",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "RetagStale", Label = "1. Retag stale elements", Optional = true, RequiresStaleElements = true },
                            new WorkflowStep { CommandTag = "BatchTag", Label = "2. Full batch tag" },
                            new WorkflowStep { CommandTag = "RoomSpaceAudit", Label = "3. Room audit (BB103 area compliance)" },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "4. Validate ISO 19650 compliance" },
                            new WorkflowStep { CommandTag = "COBieExport", Label = "5. COBie export (Education preset)" },
                            new WorkflowStep { CommandTag = "HandoverManual", Label = "6. FM handover manual" },
                            new WorkflowStep { CommandTag = "FullComplianceDashboard", Label = "7. Compliance dashboard" },
                        }
                    };

                // Phase 63: BIM coordinator automation presets
                case "IssueResolution":
                    return new WorkflowPreset
                    {
                        Name = "IssueResolution",
                        Description = "Issue resolution cycle: retag stale → fix anomalies → resolve issues → validate → compliance gate",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "RetagStale", Label = "1. Retag stale elements", Optional = true, RequiresStaleElements = true },
                            new WorkflowStep { CommandTag = "WarningsAutoFix", Label = "2. Auto-fix warnings", Optional = true },
                            new WorkflowStep { CommandTag = "AnomalyAutoFix", Label = "3. Fix tag anomalies" },
                            new WorkflowStep { CommandTag = "ResolveAllIssues", Label = "4. Resolve all ISO issues" },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "5. Validate compliance" },
                            new WorkflowStep { CommandTag = "FullComplianceDashboard", Label = "6. Compliance dashboard" },
                        }
                    };

                case "ClientReviewPrep":
                    return new WorkflowPreset
                    {
                        Name = "ClientReviewPrep",
                        Description = "Client review preparation: clean model → validate → naming → sheets → presentation → report",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "WarningsAutoFix", Label = "1. Auto-fix warnings" },
                            new WorkflowStep { CommandTag = "AutoAssignTemplates", Label = "2. Auto-assign view templates" },
                            new WorkflowStep { CommandTag = "SheetNamingCheck", Label = "3. Sheet naming compliance" },
                            new WorkflowStep { CommandTag = "BatchPrintSheets", Label = "4. Batch print sheets to PDF" },
                            new WorkflowStep { CommandTag = "DrawingRegister", Label = "5. Drawing register" },
                            new WorkflowStep { CommandTag = "FullComplianceDashboard", Label = "6. Final compliance" },
                        }
                    };

                case "RegulatoryScan":
                    return new WorkflowPreset
                    {
                        Name = "RegulatoryScan",
                        Description = "Regulatory compliance scan: Part B fire + Part L energy + Part M access + BS standards",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "1. Tag compliance" },
                            new WorkflowStep { CommandTag = "WarningsDashboard", Label = "2. Warning audit" },
                            new WorkflowStep { CommandTag = "WarningsCompliance", Label = "3. Standards compliance report" },
                            new WorkflowStep { CommandTag = "SpatialConnectivityAudit", Label = "4. Room connectivity (egress)" },
                            new WorkflowStep { CommandTag = "TemplateComplianceScore", Label = "5. Template compliance" },
                            new WorkflowStep { CommandTag = "FullComplianceDashboard", Label = "6. Full compliance" },
                        }
                    };

                // Phase 66: BIM coordinator daily efficiency presets
                case "EndOfDaySync":
                    return new WorkflowPreset
                    {
                        Name = "End of Day Sync",
                        Description = "End-of-day compliance sync — validates, exports registers, captures revision baseline for overnight comparison",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "RetagStale", Label = "1. Re-tag stale elements", Condition = "has_stale" },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "2. Validate tag completeness" },
                            new WorkflowStep { CommandTag = "WarningsBaseline", Label = "3. Save warning baseline snapshot" },
                            new WorkflowStep { CommandTag = "TagRegisterExport", Label = "4. Export tag register CSV" },
                            new WorkflowStep { CommandTag = "ExportSheetRegister", Label = "5. Export sheet register CSV" },
                            new WorkflowStep { CommandTag = "ExportModelHealth", Label = "6. Export model health report" },
                            new WorkflowStep { CommandTag = "WarningsExport", Label = "7. Export warnings CSV" },
                            new WorkflowStep { CommandTag = "CreateRevision", Label = "8. Create end-of-day revision snapshot" },
                        }
                    };

                case "FederatedModelAudit":
                    return new WorkflowPreset
                    {
                        Name = "Federated Model Audit",
                        Description = "Cross-model compliance audit — scans linked models, detects clashes, validates naming conventions, exports federated report",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "FederatedCompliance", Label = "1. Scan linked model compliance", Condition = "has_links" },
                            new WorkflowStep { CommandTag = "CrossModelClash", Label = "2. Cross-model clash detection", Condition = "has_links" },
                            new WorkflowStep { CommandTag = "NamingAudit", Label = "3. Naming convention audit" },
                            new WorkflowStep { CommandTag = "MEPClearance", Label = "4. MEP clearance validation" },
                            new WorkflowStep { CommandTag = "SpatialConnectivityAudit", Label = "5. Room connectivity check" },
                            new WorkflowStep { CommandTag = "WarningsDashboard", Label = "6. Warning analysis" },
                            new WorkflowStep { CommandTag = "WeeklyCoordinatorReport", Label = "7. Generate coordinator report" },
                        }
                    };

                case "PreMeetingPrep":
                    return new WorkflowPreset
                    {
                        Name = "Pre-Meeting Preparation",
                        Description = "BIM coordination meeting prep — auto-generates agenda from open issues, validates compliance, exports key metrics for presentation",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "RetagStale", Label = "1. Clear stale elements", Condition = "has_stale" },
                            new WorkflowStep { CommandTag = "WarningsAutoFix", Label = "2. Auto-fix warnings", Optional = true },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "3. Validate compliance" },
                            new WorkflowStep { CommandTag = "WarningsDashboard", Label = "4. Warning summary" },
                            new WorkflowStep { CommandTag = "IssueDashboard", Label = "5. Open issues summary" },
                            new WorkflowStep { CommandTag = "RevisionDashboard", Label = "6. Revision status" },
                            new WorkflowStep { CommandTag = "WeeklyCoordinatorReport", Label = "7. Generate HTML report" },
                        }
                    };

                default:
                    StingLog.Warn($"WorkflowEngine: Unknown built-in preset '{name}'");
                    return null!;
            }
        }

        /// <summary>WF-GAP-01: Get workflow preset appropriate for the project type.
        /// Reads PROJECT_TYPE from project_config.json. Returns discipline-specific preset
        /// or falls back to DailyQA for unknown types.</summary>
        public static WorkflowPreset GetWorkflowForProjectType(string projectType)
        {
            if (string.IsNullOrEmpty(projectType)) return GetBuiltInPreset("DailyQA");
            string pt = projectType.Trim();

            // Map project types to discipline-specific presets
            if (pt.IndexOf("Health", StringComparison.OrdinalIgnoreCase) >= 0 ||
                pt.IndexOf("NHS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                pt.IndexOf("Hospital", StringComparison.OrdinalIgnoreCase) >= 0)
                return GetBuiltInPreset("Healthcare_NHS");

            if (pt.IndexOf("Data Cent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                pt.IndexOf("DataCent", StringComparison.OrdinalIgnoreCase) >= 0)
                return GetBuiltInPreset("DataCentre");

            if (pt.IndexOf("Office", StringComparison.OrdinalIgnoreCase) >= 0 ||
                pt.IndexOf("Commercial", StringComparison.OrdinalIgnoreCase) >= 0 ||
                pt.IndexOf("Retail", StringComparison.OrdinalIgnoreCase) >= 0)
                return GetBuiltInPreset("CommercialOffice");

            if (pt.IndexOf("Residen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                pt.IndexOf("Housing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                pt.IndexOf("Dwelling", StringComparison.OrdinalIgnoreCase) >= 0)
                return GetBuiltInPreset("Residential");

            if (pt.IndexOf("Educat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                pt.IndexOf("School", StringComparison.OrdinalIgnoreCase) >= 0 ||
                pt.IndexOf("University", StringComparison.OrdinalIgnoreCase) >= 0)
                return GetBuiltInPreset("Education");

            return GetBuiltInPreset("DailyQA"); // fallback
        }

        // ── LOG-13: JSONL run record persistence with rotation ────────────

        private const string LogFileName = "STING_WORKFLOW_LOG.jsonl";
        private const long MaxLogSizeBytes = 500 * 1024; // 500 KB

        /// <summary>
        /// LOG-13: Get the log file path alongside the project file (or data dir fallback).
        /// </summary>
        private static string GetLogPath(Document doc)
        {
            string dir = null;
            try
            {
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                    dir = Path.GetDirectoryName(doc.PathName);
            }
            catch (Exception ex) { StingLog.Warn($"Workflow log path resolution failed: {ex.Message}"); }
            if (string.IsNullOrEmpty(dir))
                dir = StingToolsApp.DataPath ?? Path.GetTempPath();
            return Path.Combine(dir, LogFileName);
        }

        /// <summary>
        /// LOG-13: Append a single run record as one JSON line. Rotates file when > 500 KB.
        /// </summary>
        private static void SaveRunRecord(WorkflowRunRecord record, Document doc)
        {
            string path = GetLogPath(doc);
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Rotate if file exceeds size limit
            try
            {
                if (File.Exists(path))
                {
                    var fi = new FileInfo(path);
                    if (fi.Length > MaxLogSizeBytes)
                    {
                        string archiveName = $"STING_WORKFLOW_LOG_{DateTime.UtcNow:yyyy-MM}.jsonl";
                        string archivePath = Path.Combine(dir, archiveName);
                        // If archive already exists, append old content to it
                        if (File.Exists(archivePath))
                            File.AppendAllText(archivePath, File.ReadAllText(path));
                        else
                            File.Move(path, archivePath);
                        StingLog.Info($"WorkflowEngine: rotated log to {archiveName}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"WorkflowEngine: log rotation failed: {ex.Message}");
            }

            // Append single JSON line
            string line = JsonConvert.SerializeObject(record, Formatting.None);
            File.AppendAllText(path, line + Environment.NewLine);
        }

        /// <summary>
        /// LOG-13: Load the most recent run records (up to maxRecords) from JSONL file.
        /// </summary>
        internal static List<WorkflowRunRecord> LoadRunRecords(Document doc, int maxRecords = 100)
        {
            var records = new List<WorkflowRunRecord>();
            string path = GetLogPath(doc);
            if (!File.Exists(path)) return records;

            try
            {
                string[] lines = File.ReadAllLines(path);
                // Take last N lines
                int start = Math.Max(0, lines.Length - maxRecords);
                for (int i = start; i < lines.Length; i++)
                {
                    string line = lines[i]?.Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    try
                    {
                        var rec = JsonConvert.DeserializeObject<WorkflowRunRecord>(line);
                        if (rec != null)
                            records.Add(rec);
                    }
                    catch (Exception ex) { StingLog.Warn($"Workflow run record parse failed: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"WorkflowEngine: failed to load run records: {ex.Message}");
            }
            return records;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  WORKFLOW RUN RECORD — JSON-serializable execution log entry (LOG-13)
    // ════════════════════════════════════════════════════════════════════════════

    public class WorkflowRunRecord
    {
        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }

        [JsonProperty("preset")]
        public string PresetName { get; set; }

        [JsonProperty("total_steps")]
        public int TotalSteps { get; set; }

        [JsonProperty("passed")]
        public int Passed { get; set; }

        [JsonProperty("failed")]
        public int Failed { get; set; }

        [JsonProperty("skipped")]
        public int Skipped { get; set; }

        [JsonProperty("duration_s")]
        public double DurationSeconds { get; set; }

        [JsonProperty("cancelled")]
        public bool Cancelled { get; set; }

        [JsonProperty("compliance_before")]
        public double ComplianceBefore { get; set; }

        [JsonProperty("compliance_after")]
        public double ComplianceAfter { get; set; }

        /// <summary>Phase 39: Per-step results for audit trail and failure diagnostics.</summary>
        [JsonProperty("step_results")]
        public List<WorkflowStepResult> StepResults { get; set; } = new List<WorkflowStepResult>();

        /// <summary>Phase 39: User who ran the workflow (from Revit username or environment).</summary>
        [JsonProperty("user")]
        public string UserName { get; set; }
    }

    /// <summary>Phase 39: Per-step execution result for audit trail.</summary>
    public class WorkflowStepResult
    {
        [JsonProperty("tag")]
        public string CommandTag { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; } // OK, FAILED, SKIPPED, CANCELLED

        [JsonProperty("duration_ms")]
        public long DurationMs { get; set; }

        [JsonProperty("error")]
        public string ErrorMessage { get; set; }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  WORKFLOW TREND COMMAND — compliance history analysis (LOG-13)
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// LOG-13: Display workflow compliance trend from JSONL run log.
    /// Shows recent run history with pass rates and duration trends.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WorkflowTrendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            Document doc = ctx?.Doc;

            var records = WorkflowEngine.LoadRunRecords(doc, 100);
            if (records.Count == 0)
            {
                TaskDialog.Show("Workflow Trend",
                    "No workflow run records found.\n\n" +
                    "Run a workflow preset to start collecting history.");
                return Result.Cancelled;
            }

            var report = new StringBuilder();
            report.AppendLine("Workflow Run History");
            report.AppendLine(new string('═', 60));
            report.AppendLine($"  Total runs: {records.Count}");
            report.AppendLine();

            // Summary by preset
            var byPreset = new Dictionary<string, (int runs, int totalPassed, int totalSteps, double totalDur)>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var r in records)
            {
                if (!byPreset.TryGetValue(r.PresetName, out var agg))
                    agg = (0, 0, 0, 0);
                byPreset[r.PresetName] = (agg.runs + 1, agg.totalPassed + r.Passed,
                    agg.totalSteps + r.TotalSteps, agg.totalDur + r.DurationSeconds);
            }

            report.AppendLine("  By Preset:");
            foreach (var kvp in byPreset)
            {
                var (runs, tp, ts, td) = kvp.Value;
                double passRate = ts > 0 ? (double)tp / ts * 100.0 : 0;
                report.AppendLine($"    {kvp.Key}: {runs} runs, " +
                    $"{passRate:F0}% pass rate, avg {td / runs:F1}s");
            }

            // GAP-08: Compliance trend analysis
            var withCompliance = records.Where(r => r.ComplianceBefore > 0 || r.ComplianceAfter > 0).ToList();
            if (withCompliance.Count >= 2)
            {
                report.AppendLine();
                report.AppendLine("  Compliance Trend:");
                double firstAfter = withCompliance.First().ComplianceAfter;
                double lastAfter = withCompliance.Last().ComplianceAfter;
                double delta = lastAfter - firstAfter;
                string direction = delta > 0 ? "improving" : delta < 0 ? "declining" : "stable";
                report.AppendLine($"    First run: {firstAfter:F1}%  →  Last run: {lastAfter:F1}%  ({direction}, {delta:+0.0;-0.0}%)");

                // Average compliance improvement per run
                double totalImprovement = withCompliance.Sum(r => r.ComplianceAfter - r.ComplianceBefore);
                double avgImprovement = totalImprovement / withCompliance.Count;
                report.AppendLine($"    Avg improvement per run: {avgImprovement:+0.1;-0.1}%");
            }

            // Last 10 runs detail
            report.AppendLine();
            report.AppendLine("  Recent Runs (newest first):");
            report.AppendLine($"  {"Date",-20} {"Preset",-20} {"Result",-12} {"Duration",8}");
            report.AppendLine($"  {new string('-', 20)} {new string('-', 20)} {new string('-', 12)} {new string('-', 8)}");

            int showCount = Math.Min(records.Count, 10);
            for (int i = records.Count - 1; i >= records.Count - showCount; i--)
            {
                var r = records[i];
                string date = r.Timestamp;
                try { date = DateTime.Parse(r.Timestamp).ToString("yyyy-MM-dd HH:mm"); } catch (Exception ex) { StingLog.Warn($"Timestamp parse failed: {ex.Message}"); }
                string result = r.Cancelled ? "CANCELLED" :
                    r.Failed > 0 ? $"WARN ({r.Failed})" : "OK";
                report.AppendLine($"  {date,-20} {r.PresetName,-20} {result,-12} {r.DurationSeconds,7:F1}s");
            }

            TaskDialog.Show("Workflow Trend", report.ToString());
            StingLog.Info($"WorkflowTrend: displayed {records.Count} run records");
            return Result.Succeeded;
        }
    }
}
