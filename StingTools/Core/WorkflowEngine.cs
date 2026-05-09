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
using StingTools.UI;

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

        // HIGH-05: Cache built-in presets list — rebuilt only when data path changes or explicitly cleared
        private static List<WorkflowPreset> _cachedBuiltInPresets;
        private static string _cachedBuiltInPresetsDataPath;

        // MED-02: Static readonly to avoid re-allocating on every GetClosestCommandTags call
        private static readonly string[] _allKnownCommandTags = new[]
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
            "TagSelector", "ExportTagPositions",
            // Phase 92: Speckle workflow tags + SpeckleSnapshot preset aliases
            "SpeckleSend", "SpeckleReceive", "SpeckleDiff",
            "ComplianceSnapshot", "WarningsSummary"
        };

        /// <summary>Invalidate the built-in presets cache (e.g. after JSON file changes).</summary>
        public static void InvalidatePresetsCache() { _cachedBuiltInPresets = null; }

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
            // PERF: Cache element count — doesn't change between workflow steps
            int? cachedElemCount = null;
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

            // Plugin hook: notify third-party plugins before workflow execution
            try { StingPluginHooks.InvokeBeforeWorkflow(preset.Name); }
            catch (Exception ex) { StingLog.Warn($"StingPluginHooks.BeforeWorkflow: {ex.Message}"); }

            // Show progress dialog so user can see step progress and click Cancel
            var progress = StingProgressDialog.Show($"Workflow: {preset.Name}", preset.Steps.Count);

            // TAG-WORKFLOW-PARALLEL-01: Topo-sort the step list by
            // (parallelGroup, originalIndex) so independent groups stay
            // contiguous and dependent groups follow their predecessors. The
            // execution loop still runs sequentially because the Revit API is
            // single-threaded — but ordering the groups lets MarkBlocked
            // prune downstream steps when an upstream group fails.
            List<WorkflowStep> orderedSteps = preset.Steps;
            BIMManager.WorkflowDagPlanner.PlanEntry[] planArr = null;
            HashSet<int> succeededGroups = null;
            HashSet<int> failedGroups = null;
            try
            {
                var planList = BIMManager.WorkflowDagPlanner.Plan(preset.Steps);
                if (planList.Count == preset.Steps.Count)
                {
                    orderedSteps = planList.Select(p => preset.Steps[p.OriginalIndex]).ToList();
                    planArr = planList.ToArray();
                    succeededGroups = new HashSet<int>();
                    failedGroups = new HashSet<int>();
                }
            }
            catch (Exception planEx) { StingLog.Warn($"WorkflowDagPlanner.Plan: {planEx.Message}"); }

            try
            {
                int planIdx = 0;
                foreach (var step in orderedSteps)
                {
                    stepNum++;
                    int currentGroup = planArr != null && planIdx < planArr.Length ? planArr[planIdx].Group : stepNum;
                    planIdx++;

                    // TAG-WORKFLOW-PARALLEL-01: If an upstream group failed and
                    // no later group succeeded between it and the current group,
                    // mark this step as blocked rather than running it. The
                    // existing SkipIfPreviousSkipped flag handles the per-step
                    // case; this handles the cross-group case.
                    if (failedGroups != null && failedGroups.Count > 0)
                    {
                        int? lastFailed = failedGroups.OrderBy(g => g).Cast<int?>().LastOrDefault(g => g.HasValue && g.Value < currentGroup);
                        if (lastFailed.HasValue && currentGroup > lastFailed.Value)
                        {
                            bool hasRecovery = succeededGroups != null
                                && succeededGroups.Any(g => g >= lastFailed.Value && g < currentGroup);
                            if (!hasRecovery && !step.Optional)
                            {
                                report.AppendLine($"  {stepNum,2}. {step.Label} — BLOCKED (upstream group {lastFailed.Value} failed)");
                                stepResults.Add(new WorkflowStepResult { CommandTag = step.CommandTag, Label = step.Label, Status = "BLOCKED" });
                                skipped++;
                                continue;
                            }
                        }
                    }

                    // Phase 74: Local helper — records skip with audit trail + cascade flag
                    void RecordSkip(string reason)
                    {
                        skipped++;
                        previousStepSkipped = true;
                        report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED ({reason})");
                        stepResults.Add(new WorkflowStepResult { CommandTag = step.CommandTag, Label = step.Label, Status = "SKIPPED" });
                    }

                    if (progress.IsCancelled || EscapeChecker.IsEscapePressed())
                    {
                        report.AppendLine($"  {stepNum,2}. {step.Label} — CANCELLED");
                        StingLog.Info($"Workflow step {stepNum}: cancelled by user");
                        cancelled = true;
                        break;
                    }

                    // Update progress dialog with current step info (increment counter by 1)
                    progress.Increment($"Step {stepNum}/{preset.Steps.Count}: {step.Label}");

                    // Phase 48: skipIfPreviousSkipped condition
                    if (step.SkipIfPreviousSkipped && previousStepSkipped)
                    {
                        RecordSkip("previous step was skipped");
                        continue;
                    }

                    // Phase 85: RequiresWorksharedModel moved outside Condition block so it always runs
                    if (step.RequiresWorksharedModel && !doc.IsWorkshared)
                    { RecordSkip("not workshared"); continue; }

                    if (!string.IsNullOrEmpty(step.Condition))
                    {
                        // Normalize condition to lowercase for case-insensitive matching
                        string cond = step.Condition.Trim().ToLowerInvariant();
                        // WF-03: Removed duplicate "workshared" condition check — already handled
                        // by RequiresWorksharedModel guard above (line 563).
                        // GAP-02: Extended condition engine for workflow steps
                        if (cond == "has_links")
                        {
                            bool hasLinks = new FilteredElementCollector(doc)
                                .OfClass(typeof(RevitLinkInstance)).GetElementCount() > 0;
                            if (!hasLinks) { RecordSkip("no linked models"); continue; }
                        }
                        if (cond == "has_cad_imports")
                        {
                            bool hasCad = new FilteredElementCollector(doc)
                                .OfClass(typeof(ImportInstance)).GetElementCount() > 0;
                            if (!hasCad) { RecordSkip("no CAD imports"); continue; }
                        }
                        if (cond == "has_stale")
                        {
                            if (!cachedHasStale()) { RecordSkip("no stale elements"); continue; }
                        }
                        // Phase 39: Element count range condition (cached — count doesn't change between steps)
                        if (step.MinElementCount.HasValue || step.MaxElementCount.HasValue)
                        {
                            cachedElemCount ??= new FilteredElementCollector(doc)
                                .WhereElementIsNotElementType().GetElementCount();
                            int elemCount = cachedElemCount.Value;
                            if (step.MinElementCount.HasValue && elemCount < step.MinElementCount.Value)
                            { RecordSkip($"{elemCount} elements < min {step.MinElementCount.Value}"); continue; }
                            if (step.MaxElementCount.HasValue && elemCount > step.MaxElementCount.Value)
                            { RecordSkip($"{elemCount} elements > max {step.MaxElementCount.Value}"); continue; }
                        }

                        // Phase 47: Warning-aware workflow conditions
                        if (cond == "has_warnings")
                        {
                            try
                            {
                                int warnCount = doc.GetWarnings()?.Count ?? 0;
                                if (warnCount == 0) { RecordSkip("no warnings"); continue; }
                            }
                            catch (Exception ex) { StingLog.Warn($"has_warnings check: {ex.Message}"); }
                        }
                        if (cond == "has_critical_warnings")
                        {
                            try
                            {
                                var warnReport = WarningsEngine.ScanWarnings(doc);
                                int critical = warnReport.BySeverity.GetValueOrDefault(WarningSeverity.Critical);
                                if (critical == 0) { RecordSkip("no critical warnings"); continue; }
                            }
                            catch (Exception ex) { StingLog.Warn($"has_critical_warnings check: {ex.Message}"); }
                        }
                        if (cond == "has_open_issues")
                        {
                            try
                            {
                                string projDir = Path.GetDirectoryName(doc.PathName ?? "") ?? "";
                                string issuesPath = Path.Combine(projDir, "_bim_manager", "issues.json");
                                if (!File.Exists(issuesPath)) { RecordSkip("no issues file"); continue; }
                                // WE-HIGH-01: Use JSON parsing instead of naive string split for accuracy
                                var issuesArr = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(issuesPath));
                                int openCount = issuesArr.Count(i => (string)i["status"] == "OPEN");
                                if (openCount == 0) { RecordSkip("no open issues"); continue; }
                            }
                            catch (Exception ex) { StingLog.Warn($"has_open_issues check: {ex.Message}"); }
                        }
                        // Phase 75: has_overdue_issues — skip if no SLA-breaching issues
                        if (cond == "has_overdue_issues")
                        {
                            try
                            {
                                bool hasOverdue = EvaluateSingleCondition(doc, "has_overdue_issues", cachedCompliancePct, cachedHasStale);
                                if (!hasOverdue) { RecordSkip("no overdue issues"); continue; }
                            }
                            catch (Exception ex) { StingLog.Warn($"has_overdue_issues check: {ex.Message}"); }
                        }

                        // HIGH-04: has_untagged and has_placeholders share a single collector pass
                        if (cond == "has_untagged" || cond == "has_placeholders")
                        {
                            bool hasUntagged = false;
                            bool hasPlaceholders = false;
                            try
                            {
                                var catEnums = SharedParamGuids.AllCategoryEnums;
                                var coll = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                                if (catEnums != null && catEnums.Length > 0)
                                    coll.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));
                                foreach (var e in coll)
                                {
                                    string t = ParameterHelpers.GetString(e, ParamRegistry.TAG1);
                                    if (string.IsNullOrEmpty(t)) hasUntagged = true;
                                    else if (TagConfig.TagHasPlaceholders(t)) hasPlaceholders = true;
                                    if (hasUntagged && hasPlaceholders) break; // both found — early exit
                                }
                            }
                            catch (Exception ex) { StingLog.Warn($"{cond} condition check: {ex.Message}"); }
                            if (cond == "has_untagged" && !hasUntagged) { RecordSkip("no untagged elements"); continue; }
                            if (cond == "has_placeholders" && !hasPlaceholders) { RecordSkip("no placeholder tokens"); continue; }
                        }
                        if (cond == "has_container_gaps")
                        {
                            try
                            {
                                var scan = ComplianceScan.Scan(doc);
                                double containerPct = scan?.ContainerCompletePct ?? 100;
                                if (containerPct >= 95)
                                { RecordSkip($"containers {containerPct:F0}% complete"); continue; }
                            }
                            catch (Exception ex) { StingLog.Warn($"has_container_gaps check: {ex.Message}"); }
                        }
                        if (cond == "compliance_above_90")
                        {
                            double pct = cachedCompliancePct();
                            if (pct >= 90)
                            { RecordSkip($"compliance {pct:F0}% ≥ 90%"); continue; }
                        }
                        if (cond == "compliance_below_50")
                        {
                            double pct = cachedCompliancePct();
                            if (pct >= 50)
                            { RecordSkip($"compliance {pct:F0}% ≥ 50%"); continue; }
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
                            string logic = isOr ? "OR" : "AND";
                            RecordSkip($"compound {logic}: {string.Join(", ", step.Conditions)}");
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
                    }

                    // Phase 69: Data drop level gate
                    if (step.MinDataDrop.HasValue)
                    {
                        int currentDD = CalculateCurrentDataDrop(doc, cachedCompliancePct());
                        if (currentDD < step.MinDataDrop.Value)
                        {
                            RecordSkip($"current DD{currentDD} < required DD{step.MinDataDrop.Value}");
                            continue;
                        }
                    }

                    // WE-CRIT-01 FIX: Phase 68 conditions moved out of MinDataDrop block and using RecordSkip()
                    if (step.Condition != null)
                    {
                        string cond68 = step.Condition.Trim().ToLowerInvariant();
                        if (cond68 == "has_spatial_warnings")
                        {
                            try
                            {
                                var warnReport = WarningsEngine.ScanWarnings(doc);
                                int spatial = warnReport.ByCategory.GetValueOrDefault(WarningCategory.Spatial);
                                if (spatial == 0) { RecordSkip("no spatial warnings"); continue; }
                            }
                            catch (Exception ex) { StingLog.Warn($"has_spatial_warnings check: {ex.Message}"); }
                        }
                        if (cond68 == "has_mep_warnings")
                        {
                            try
                            {
                                var warnReport = WarningsEngine.ScanWarnings(doc);
                                int mep = warnReport.ByCategory.GetValueOrDefault(WarningCategory.MEP);
                                if (mep == 0) { RecordSkip("no MEP warnings"); continue; }
                            }
                            catch (Exception ex) { StingLog.Warn($"has_mep_warnings check: {ex.Message}"); }
                        }
                        if (cond68 == "tag_compliance_below_threshold")
                        {
                            double pct = cachedCompliancePct();
                            double threshold = step.MinCompliancePct ?? 90;
                            if (pct >= threshold)
                            { RecordSkip($"compliance {pct:F0}% meets threshold {threshold:F0}%"); continue; }
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
                                RecordSkip("data files unchanged");
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
                                RecordSkip($"compliance {pct:F0}% above {step.MaxCompliancePct.Value}%");
                                continue;
                            }
                            if (step.MinCompliancePct.HasValue && pct < step.MinCompliancePct.Value)
                            {
                                RecordSkip($"compliance {pct:F0}% below {step.MinCompliancePct.Value}%");
                                continue;
                            }
                            if (step.RequiresStaleElements)
                            {
                                if (!cachedHasStale())
                                {
                                    RecordSkip("no stale elements");
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
                                    if (progress.IsCancelled || EscapeChecker.IsEscapePressed())
                                    {
                                        StingLog.Info($"Workflow step {stepNum} retry cancelled by user");
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
                                         stepResult == Result.Cancelled ? "SKIP" :
                                         stepResult == Result.Failed ? "FAIL" : "WARN";
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
                        {
                            passed++;
                            succeededGroups?.Add(currentGroup);
                        }
                        else if (step.Optional)
                            skipped++;
                        else
                        {
                            failed++;
                            failedGroups?.Add(currentGroup);
                        }

                        // B03 FIX: Only set previousStepSkipped for actual skips, not failures.
                        // Failed steps should NOT trigger SkipIfPreviousSkipped cascade —
                        // that flag is specifically for skipped (condition-gated) steps.
                        // Executed steps (whether succeeded or failed) reset the skip flag.
                        previousStepSkipped = false;

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
                        // Phase 86: Don't double-count — step was already counted as skipped (optional) or failed above
                        if (preset.RollbackOnOptionalFailure && stepResult == Result.Failed)
                        {
                            // Reclassify: optional failure was counted as 'skipped' above, move to 'failed'
                            if (step.Optional) { skipped = Math.Max(0, skipped - 1); failed++; }
                            report.AppendLine($"\n  *** Step failed (rollback_on_optional_failure) — rolling back all changes ***");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();

                        // WF-01: Attempt fallback step if configured before counting as failed
                        bool fallbackSucceeded = false;
                        if (!string.IsNullOrEmpty(step.FallbackStep))
                        {
                            try
                            {
                                StingLog.Info($"Workflow step {stepNum} '{step.Label}' failed — attempting fallback '{step.FallbackStep}'");
                                var fallbackCmd = ResolveCommand(step.FallbackStep);
                                if (fallbackCmd != null)
                                {
                                    string fbMsg = "";
                                    var fbResult = fallbackCmd.Execute(commandData, ref fbMsg, elements);
                                    if (fbResult == Result.Succeeded)
                                    {
                                        fallbackSucceeded = true;
                                        report.AppendLine($"  {stepNum,2}. {step.Label} — FALLBACK OK via '{step.FallbackStep}' ({sw.Elapsed.TotalSeconds:F1}s)");
                                        StingLog.Info($"Workflow step {stepNum} fallback '{step.FallbackStep}' succeeded");
                                        stepResults.Add(new WorkflowStepResult
                                        {
                                            CommandTag = step.CommandTag, Label = step.Label,
                                            Status = "FALLBACK_OK", DurationMs = sw.ElapsedMilliseconds
                                        });
                                        passed++;
                                    }
                                }
                                else
                                {
                                    StingLog.Warn($"Workflow step {stepNum} fallback '{step.FallbackStep}' could not be resolved");
                                }
                            }
                            catch (Exception fbEx)
                            {
                                StingLog.Warn($"Workflow step {stepNum} fallback '{step.FallbackStep}' also failed: {fbEx.Message}");
                            }
                        }

                        if (!fallbackSucceeded)
                        {
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

            // Close progress dialog
            try { progress.Close(); } catch (Exception ex) { StingLog.Warn($"Progress dialog close: {ex.Message}"); }

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

            // Plugin hook: notify third-party plugins after workflow completion
            try { StingPluginHooks.InvokeAfterWorkflow(preset.Name, failed == 0 && !cancelled); }
            catch (Exception ex) { StingLog.Warn($"StingPluginHooks.AfterWorkflow: {ex.Message}"); }

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

                // INT-04 / H7 — push the run record to Planscape so the server
                // workflow trend graph reflects local activity. Fire-and-forget;
                // the local jsonl record is the source of truth and we never
                // block a workflow on the network.
                try
                {
                    string cfgPath = System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(doc.PathName) ?? "",
                        "_BIM_COORD", "planscape_link.json");
                    Guid serverProjectId = StingTools.BIMManager.PlatformSyncCommand.LoadPlanscapeProjectId(cfgPath);
                    if (serverProjectId != Guid.Empty)
                    {
                        var client = StingTools.BIMManager.PlanscapeServerClient.Instance;
                        _ = client.LogWorkflowRunAsync(
                            serverProjectId,
                            preset.Name ?? "",
                            preset.Steps.Count,
                            passed,
                            failed,
                            skipped,
                            record.DurationSeconds,
                            record.ComplianceBefore,
                            record.ComplianceAfter);
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"WorkflowEngine: server push skipped — {ex.Message}");
                }

                // Pack 122 / Gap B — stamp the last-run scalars onto ES so the
                // morning briefing, dock panel, and Idling SLA scanner can read
                // workflow state without parsing STING_WORKFLOW_LOG.jsonl.
                try
                {
                    string status = cancelled ? "Cancelled" :
                                    failed > 0 ? "Failed" : "Succeeded";
                    using (var t = new Transaction(doc, "STING ES: stamp workflow last-run"))
                    {
                        t.Start();
                        StingTools.Core.Storage.StingWorkflowStateSchema
                            .StampLastRun(doc, preset.Name ?? "", status);
                        t.Commit();
                    }
                }
                catch (Exception esEx) { StingLog.Warn($"WorkflowState ES stamp: {esEx.Message}"); }

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
                {
                    // DI-04: Atomic write via temp file + File.Replace to prevent corruption on crash
                    string tmpPath = sidecarPath + ".tmp";
                    File.WriteAllText(tmpPath, hash);
                    if (File.Exists(sidecarPath))
                        File.Replace(tmpPath, sidecarPath, sidecarPath + ".bak");
                    else
                        File.Move(tmpPath, sidecarPath);
                }
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
                // WF-05: Include file names in hash to detect renames/additions/deletions
                var sb = new System.Text.StringBuilder();
                foreach (var f in files)
                {
                    var fi = new FileInfo(f);
                    totalSize += fi.Length;
                    if (fi.LastWriteTimeUtc > maxWrite) maxWrite = fi.LastWriteTimeUtc;
                    sb.Append(fi.Name).Append(':').Append(fi.Length).Append(';');
                }
                sb.Append(files.Length).Append('_').Append(totalSize).Append('_')
                  .Append(maxWrite.ToString("yyyyMMddHHmmss"));
                // Produce a SHA256 digest of the composite string for a more robust hash
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
                    byte[] hashBytes = sha.ComputeHash(bytes);
                    return BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 16);
                }
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
                // Try plugin hook custom commands before failing
                try
                {
                    var uiApp = data?.Application;
                    if (uiApp != null)
                    {
                        var (found, result) = StingPluginHooks.TryExecuteCommand(tag, uiApp);
                        if (found)
                        {
                            StingLog.Info($"WorkflowEngine: custom command '{tag}' executed via plugin hook: {result}");
                            return result != null && result.StartsWith("Error:") ? Result.Failed : Result.Succeeded;
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"WorkflowEngine: plugin hook command '{tag}' lookup failed: {ex.Message}"); }

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

                // Electrical Panel Schedules (Commands.Panels)
                case "Panel_BatchSchedules":    return new Commands.Panels.BatchPanelSchedulesCommand();
                case "Panel_Audit":             return new Commands.Panels.PanelScheduleAuditCommand();
                case "Panel_ExportToExcel":     return new Commands.Panels.ExportPanelSchedulesToExcelCommand();
                case "Panel_ImportFromExcel":   return new Commands.Panels.ImportPanelSchedulesFromExcelCommand();
                case "Panel_FillSpares":        return new Commands.Panels.FillEmptySlotsWithSparesCommand();
                case "Panel_FillSparesAll":     return new Commands.Panels.FillSparesAllSchedulesCommand();
                case "Panel_FillSpaces":        return new Commands.Panels.FillEmptySlotsWithSpacesCommand();
                case "Panel_SpacesToSpares":    return new Commands.Panels.ConvertSpacesToSparesCommand();
                case "Panel_ClearSparesSpaces": return new Commands.Panels.ClearSparesAndSpacesCommand();

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
                // Phase 74: Removed duplicate "AutoAssignTemplates" case (already at line 1096)
                case "BatchPrintSheets": return new Docs.ExportCenterPdfCommand(); // redirects to Export Centre (PDF preset)
                case "ExportCenter":     return new Docs.ExportCenterCommand();
                case "ExportCenterPDF":  return new Docs.ExportCenterPdfCommand();

                // Data Pipeline
                case "DynamicBindings": return new Temp.DynamicBindingsCommand();
                case "BOQExport": return new Temp.BOQExportCommand();

                // Phase 108j — BOQ × BCC workflow integration
                case "BOQRefresh":             return new BOQ.BOQRefreshCommand();
                case "BOQSnapshotSave":        return new BOQ.BOQSnapshotSaveCommand();
                case "BOQSnapshotCompare":     return new BOQ.BOQSnapshotCompareCommand();
                case "BOQExportProfessional":  return new BOQ.BOQProfessionalExportCommand();
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
                case "SmartNumbering":
                case "GraitecNumbering":     return new Organise.SmartNumberingCommand();
                case "ModelHealthDashboard": return new BIMManager.ModelHealthDashboardCommand();

                // Phase 72: Doc/Schedule Automation
                case "DrawingRegisterSync":     return new Docs.DrawingRegisterSyncCommand();
                case "CrossScheduleValidate":   return new Docs.CrossScheduleValidateCommand();
                case "PrintQueue":              return new Docs.PrintQueueCommand();
                case "DocumentPackage":         return new Docs.DocumentPackageCommand();

                // Phase 73: Workflow Maturity
                case "CommissioningWorkflow":   return new CommissioningWorkflowCommand();
                case "HandoverValidation":      return new HandoverValidationCommand();
                case "SustainabilityWorkflow":  return new SustainabilityWorkflowCommand();

                // Phase 74: Deep Review Enhancements
                case "DailyPlanner":            return new DailyPlannerCommand();
                case "DeliverableMatrix":       return new DeliverableMatrixCommand();
                case "WarningPrediction":       return new WarningPredictionCommand();
                // CRIT-05: These are handled inline in StingCommandHandler — not usable in workflows
                case "AcousticAnalysis":        throw new NotSupportedException("AcousticAnalysis is an inline dispatch handler and cannot be used in workflow presets.");
                case "BREEAMAssessment":        throw new NotSupportedException("BREEAMAssessment is an inline dispatch handler and cannot be used in workflow presets.");
                case "LifecycleAssessment":     throw new NotSupportedException("LifecycleAssessment is an inline dispatch handler and cannot be used in workflow presets.");
                case "MEPPressureDrop":         throw new NotSupportedException("MEPPressureDrop is an inline dispatch handler and cannot be used in workflow presets.");
                case "StructuralDeepAnalysis":  throw new NotSupportedException("StructuralDeepAnalysis is an inline dispatch handler and cannot be used in workflow presets.");

                // BIM Coordination Center dispatch targets
                case "FullComplianceDashboard": return new BIMManager.FullComplianceDashboardCommand();
                case "ExportModelHealth":       return new BIMManager.ExportModelHealthCommand();
                case "RaiseIssue":              return new BIMManager.RaiseIssueCommand();
                case "UpdateIssue":             return new BIMManager.UpdateIssueCommand();
                case "SelectIssueElements":     return new BIMManager.SelectIssueElementsCommand();
                case "LinkIssueElements":       return new BIMManager.SelectIssueElementsCommand(); // alias — BCC dispatches this tag
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
                case "ClashDetection":          return new Core.Clash.ClashRunCommand();
                // Phase 5 clash engine — BCC Clash-tab buttons route through
                // BIMCoordinationCenterCommand.DispatchCoordAction which uses
                // WorkflowEngine.GetCommandInstance to resolve a Tag to an
                // IExternalCommand. The StingCommandHandler.Execute switch
                // (which I extended in rec-4) handles the SAME tags from the
                // dockable-panel path, but BCC doesn't go through that switch
                // — so the tags have to be registered here too or every
                // Clash-tab button shows "Action 'X' is not handled".
                case "ClashRun":                return new Core.Clash.ClashRunCommand();
                case "ClashBcfExport":          return new Core.Clash.ClashBcfExportCommand();
                case "ClashSessionRefresh":     return new Core.Clash.ClashSessionRefreshCommand();
                case "ClashSessionClear":       return new Core.Clash.ClashSessionClearCommand();
                case "ClashMatrixEdit":         return new Core.Clash.ClashMatrixEditCommand();
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
                case "DataDropReadiness":       return new BIMManager.DataDropReadinessCommand();
                case "WeeklyCoordinatorReport": return new BIMManager.WeeklyCoordinatorReportCommand();
                case "ExportSchedulesToExcel":  return new BIMManager.ExportSchedulesToExcelCommand();
                case "COBieImport":             return new BIMManager.COBieImportCommand();
                case "UserProductivityReport":  return new BIMManager.UserProductivityReportCommand();
                // FederatedComplianceScanCommand and ApprovalWorkflowCommand not yet implemented
                // case "FederatedCompliance":     return new BIMManager.FederatedComplianceScanCommand();
                // case "ApprovalWorkflow":        return new BIMManager.ApprovalWorkflowCommand();
                case "RevisionSchedule":        return new BIMManager.RevisionScheduleCommand();
                case "AssignNumbers":           return new Tags.AssignNumbersCommand();
                case "SetSeqScheme":            return new Tags.SetSeqSchemeCommand();
                case "ExportTagMap":            return new BIMManager.ExportTagMapCommand();
                case "ImportTagMap":            return new BIMManager.ImportTagMapCommand();
                case "BatchPlaceTags":          return new Tags.BatchPlaceTagsCommand();

                // Phase 67: Additional command tag resolutions
                case "TagSelector":             return new Select.TagSelectorCommand();
                case "ExportTagPositions":      return new Tags.ExportTagPositionsCommand();

                // Phase 74: Missing resolutions that break sector-specific workflow presets
                case "RoomSpaceAudit":          return new Temp.RoomAuditCommand();
                case "HandoverManual":          return new Docs.HandoverManualCommand();
                case "MEPSizingCheck":          return new Temp.MEPSizingCheckCommand();

                // Phase 92: Speckle workflow steps + semantic aliases used by SpeckleSnapshot preset.
                // "ComplianceSnapshot" and "WarningsSummary" are user-facing names — there are no
                // dedicated commands, so they alias onto the existing dashboards (matches CLAUDE.md
                // note: "steps = [SpeckleDiff, SpeckleSend, ComplianceSnapshot, WarningsSummary]").
                case "SpeckleSend":             return new BIMManager.SpeckleSendCommand();
                case "SpeckleReceive":          return new BIMManager.SpeckleReceiveCommand();
                case "SpeckleDiff":             return new BIMManager.SpeckleDiffCommand();
                case "ComplianceSnapshot":      return new Tags.CompletenessDashboardCommand();
                case "WarningsSummary":         return new WarningsDashboardCommand();

                // Phase 96: QR code tags dispatched from BCC Overview "QR CODES" section
                // and the Planscape-native-hub → "Generate QR Link" quick share button.
                // All four aliases land on the same ReadOnly QRCodeCommand.
                case "QRCode":
                case "GenerateQRCode":
                case "GenerateQRSheet":
                case "PrintQRTags":
                case "PlanscapeQR":              return new Tags.QRCodeCommand();

                // Phase 96: BCC-Perm-01 fix — ExportPermissionMatrix was resolvable from the
                // dock panel (StingCommandHandler) but not from the BCC action path, so the
                // Permission Groups "Export Matrix" button was silently running
                // ExportModelHealth instead. Added here so both dispatch paths hit the
                // real role/folder CSV exporter.
                case "ExportPermissionMatrix": return new BIMManager.ExportPermissionMatrixCommand();

                // Phase 96: Code Legend button dispatched from BCC Overview + Document Manager
                // share bar. Same double-path issue as QR — only wired in StingCommandHandler
                // so BCC's ExternalEvent path produced "Action 'CodeLegend' is not handled."
                case "CodeLegend":             return new Tags.CodeLegendCommand();

                // Phase 98: 4D/5D scheduling commands dispatched from BCC 4D/5D tab. Same
                // double-path gap as QR/CodeLegend — only wired in StingCommandHandler so
                // BCC's ExternalEvent path produced "Action 'X' is not handled".
                case "WorkingCalendar":        return new BIMManager.WorkingCalendarCommand();
                case "SaveWorkingCalendar":    return new BIMManager.WorkingCalendarCommand();
                case "NavisworksTimeLiner":    return new BIMManager.NavisworksTimeLinerExportCommand();
                case "ElementCostTrace":       return new BIMManager.ElementCostTraceCommand();

                // BCC Platform tab → Planscape Connect / member management buttons.
                // Same double-path issue as QR/CodeLegend: only wired in StingCommandHandler
                // so BCC's ExternalEvent path produced "Action 'PlanscapeConnect' is not handled".
                case "PlanscapeExportTeam":
                case "PlanscapeExportConfig":     return new BIMManager.ExportCoordLogCommand();
                case "PlanscapeShareReport":      return new BIMManager.GenerateDashboardCommand();
                case "LoadFamilyLibrary":         return new Temp.FamilyLibraryLoaderCommand();

                // Phase 104: GAP-analysis commands (GapAnalysisFixCommands.cs) dispatched from BCC
                // action bar via WarningsManager.DispatchCoordAction. Previously only resolvable via
                // StingCommandHandler, so BCC ExternalEvent path produced "Action 'X' is not handled".
                case "ExportDashboardHTML":    return new BIMManager.ExportDashboardHTMLCommand();
                case "AutoMeetingMinutes":     return new BIMManager.AutoMeetingMinutesCommand();
                case "BEPStageValidation":     return new BIMManager.BEPStageValidationCommand();
                case "IssueRevisionLink":      return new BIMManager.IssueRevisionLinkCommand();
                case "TagRevisionDiff":        return new BIMManager.TagRevisionDiffCommand();
                case "AutoScheduleMeetings":   return new BIMManager.AutoScheduleMeetingsCommand();
                case "COBieExtendedImport":    return new BIMManager.COBieExtendedImportCommand();
                // "LinkIssueElements" alias defined above at ~line 1405 — removed the duplicate
                // case here (CS0152). The alias still resolves via the earlier case.
                // WF-02: EscalateOverdueActions is an internal method in WarningsManager, not an IExternalCommand.
                // Removed: return null caused NRE in RunCommandByTag. Falls through to default null
                // which is handled by the plugin hook fallback + error logging in RunCommandByTag.

                // Phase 167 — Planscape BCC dispatch gap: same double-path issue as
                // QR/CodeLegend/WorkingCalendar. BIMCoordinationCenter.ShowPlatformDetail
                // dispatches PlanscapeConnect / PlanscapeSyncNow via DispatchAction(),
                // which routes through ProcessAction → DispatchCoordAction →
                // WorkflowEngine.GetCommandInstance. PlanscapeDisconnect /
                // PlanscapeOpenWebDashboard already short-circuit inline in
                // ProcessAction; cases here keep the resolver complete so any future
                // caller (or the dictionary alias path) lands on a real command.
                case "PlanscapeConnect":
                case "PlanscapeAddMember":
                case "PlanscapeRemoveMember":
                case "PlanscapeLinkProject":
                case "PlanscapeTestConnection":    return new BIMManager.PlanscapeConnectCommand();

                case "PlanscapeSyncNow":           return new BIMManager.PlatformSyncCommand();
                case "PublishModelToPlanscape":    return new BIMManager.PublishModelCommand();

                case "PlanscapeDisconnect":
                case "PlanscapeUnlinkProject":
                case "PlanscapeClearCredentials":  return new BIMManager.PlanscapeDisconnectCommand();

                case "PlanscapeOpenWebDashboard":
                case "PlanscapeOpenBrowser":       return new BIMManager.PlanscapeOpenWebCommand();

                // ── Phase 175: Design Options ──
                case "DesignOptions_Inspect":             return new Commands.DesignOptions.DesignOptionsInspectCommand();
                case "DesignOptions_MoveTo":              return new Commands.DesignOptions.MoveToOptionCommand();
                case "DesignOptions_LockView":            return new Commands.DesignOptions.LockViewToOptionCommand();
                case "DesignOptions_ResetView":           return new Commands.DesignOptions.ResetViewOptionVisibilityCommand();
                case "DesignOptions_CloneSchedule":       return new Commands.DesignOptions.ClonePerOptionScheduleCommand();
                case "DesignOptions_IsolationView":       return new Commands.DesignOptions.CreateIsolationViewCommand();
                case "DesignOptions_PrimaryClashView":    return new Commands.DesignOptions.CreatePrimaryOnlyClashViewCommand();
                case "DesignOptions_Audit":               return new Commands.DesignOptions.AuditOptionsCommand();
                case "DesignOptions_BatchLinkVisibility": return new Commands.DesignOptions.BatchSetLinkOptionVisibilityCommand();
                case "DesignOptions_Dashboard":           return new Commands.DesignOptions.OptionsDashboardCommand();
                case "DesignOptions_ExportComparison":    return new Commands.DesignOptions.ExportOptionComparisonCommand();

                // ── Healthcare Pack H-1..H-30 ──
                case "Healthcare_RunAllValidators":  return new Commands.Healthcare.HealthcareRunAllValidatorsCommand();
                case "Healthcare_PressureAudit":     return new Commands.Healthcare.HealthcarePressureAuditCommand();
                case "Healthcare_WaterSafety":       return new Commands.Healthcare.HealthcareWaterSafetyCommand();
                case "Healthcare_EesBranch":         return new Commands.Healthcare.HealthcareEesBranchAuditCommand();
                case "Healthcare_RadShield":         return new Commands.Healthcare.HealthcareRadShieldAuditCommand();
                case "Healthcare_AdvancedRadShield": return new Commands.Healthcare.HealthcareAdvancedRadShieldCommand();
                case "Healthcare_RdsCompleteness":   return new Commands.Healthcare.HealthcareRdsCompletenessCommand();
                case "Healthcare_IoTStaleness":      return new Commands.Healthcare.HealthcareIoTStalenessCommand();
                case "Healthcare_StructuralLoad":    return new Commands.Healthcare.HealthcareStructuralLoadCommand();
                case "Healthcare_Acoustic":          return new Commands.Healthcare.HealthcareAcousticCommand();
                case "Healthcare_EndoscopeTrace":    return new Commands.Healthcare.HealthcareEndoscopeTraceCommand();
                case "Healthcare_EesResilience":     return new Commands.Healthcare.HealthcareEesResilienceCommand();
                case "Healthcare_RtlsCoverage":      return new Commands.Healthcare.HealthcareRtlsCoverageCommand();
                case "Healthcare_WasteFlow":         return new Commands.Healthcare.HealthcareWasteFlowCommand();

                case "Healthcare_IssueRDS":          return new Commands.Healthcare.IssueRoomDataSheetCommand();
                case "Healthcare_BatchRDS":          return new Commands.Healthcare.BatchIssueRoomDataSheetsCommand();

                case "Healthcare_MgasAudit":         return new Commands.MedGas.MgasNetworkAuditCommand();
                case "Healthcare_MgasVerify":        return new Commands.MedGas.MgasVerifyCommand();

                case "Healthcare_AdjacencyAudit":    return new Commands.Adjacency.AdjacencyAuditCommand();

                case "Healthcare_RadCalcChest":      return new Commands.Radiation.RadCalcChestRoomCommand();
                case "Healthcare_RadCalcCt":         return new Commands.Radiation.RadCalcCtRoomCommand();
                case "Healthcare_RadCalcLinac":      return new Commands.Radiation.RadCalcLinacVaultCommand();
                case "Healthcare_MriZoneAudit":      return new Commands.Radiation.MriZoneAuditCommand();

                case "Healthcare_IoTRegistry":       return new Commands.Twin.IoTRegistryCommand();

                case "Healthcare_AntiLigature":      return new Commands.Healthcare.Specialist.AntiLigatureAuditCommand();
                case "Healthcare_HybridOr":          return new Commands.Healthcare.Specialist.HybridOrCheckCommand();
                case "Healthcare_PharmacyUsp":       return new Commands.Healthcare.Specialist.PharmacyUspAuditCommand();
                case "Healthcare_BehaviouralHealth": return new Commands.Healthcare.Specialist.BehaviouralHealthAuditCommand();
                case "Healthcare_Mortuary":          return new Commands.Healthcare.Specialist.MortuaryAuditCommand();
                case "Healthcare_MaternityNicu":    return new Commands.Healthcare.Specialist.MaternityNicuAuditCommand();
                case "Healthcare_Hsdu":              return new Commands.Healthcare.Specialist.HsduAuditCommand();
                case "Healthcare_Dialysis":          return new Commands.Healthcare.Specialist.DialysisAuditCommand();
                case "Healthcare_Hbo":               return new Commands.Healthcare.Specialist.HboAuditCommand();

                default: return null;
            }
        }

        /// <summary>Returns the closest matching valid command tags for a given invalid tag.</summary>
        private static List<string> GetClosestCommandTags(string invalidTag, int maxResults)
        {
            // MED-02: Uses static readonly _allKnownCommandTags — no per-call allocation
            string lowerInvalid = invalidTag.ToLowerInvariant();
            return _allKnownCommandTags
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
            // HIGH-06: Single-row DP — avoids (m+1)×(n+1) 2D array allocation
            int bLen = b.Length;
            int[] prev = new int[bLen + 1];
            int[] curr = new int[bLen + 1];
            for (int j = 0; j <= bLen; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= bLen; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
                }
                // swap rows
                var tmp = prev; prev = curr; curr = tmp;
            }
            return prev[bLen];
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
                    case "has_overdue_issues":
                    {
                        // HIGH-03: Load issues.json once, shared between has_open_issues and has_overdue_issues
                        string issuePath = Path.Combine(Path.GetDirectoryName(doc.PathName ?? "") ?? "", "_bim_manager", "issues.json");
                        if (!File.Exists(issuePath)) return false;
                        JArray cachedIssues = JArray.Parse(File.ReadAllText(issuePath));
                        if (condition == "has_open_issues")
                            return cachedIssues.Any(i => (string)i["status"] == "OPEN");
                        // has_overdue_issues
                        var slaHrs = new Dictionary<string, int>
                            { { "CRITICAL", 4 }, { "HIGH", 24 }, { "MEDIUM", 168 }, { "LOW", 336 } };
                        foreach (var oi in cachedIssues)
                        {
                            if (oi["status"]?.ToString() != "OPEN") continue;
                            string pri = oi["priority"]?.ToString() ?? "MEDIUM";
                            if (!DateTime.TryParse(oi["date_raised"]?.ToString() ?? oi["created_date"]?.ToString(), out var created)) continue;
                            int ageH = (int)(DateTime.Now - created).TotalHours;
                            int threshold = slaHrs.GetValueOrDefault(pri, 336);
                            if (ageH > threshold) return true;
                        }
                        return false;
                    }
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
                    case "compliance_above_80": return cachedCompliancePct() >= 80;
                    case "compliance_below_70": return cachedCompliancePct() < 70;
                    case "workshared": return doc.IsWorkshared;
                    case "has_high_severity_warnings":
                        var wrHigh = WarningsEngine.ScanWarnings(doc);
                        return wrHigh.BySeverity.GetValueOrDefault(WarningSeverity.Critical) > 0
                            || wrHigh.BySeverity.GetValueOrDefault(WarningSeverity.High) > 0;
                    case "has_cad_imports":
                        return new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).GetElementCount() > 0;
                    case "has_rooms":
                        return new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
                            .WhereElementIsNotElementType().GetElementCount() > 0;
                    case "has_sheets":
                        return new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).GetElementCount() > 0;
                    default:
                        // WF-001 FIX: Unknown conditions now return false (fail-safe).
                        // Previously returned true, silently executing gated steps on typos.
                        StingLog.Warn($"WorkflowEngine: unknown condition '{condition}' — step will be SKIPPED (fail-safe)");
                        return false;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"WorkflowEngine: condition '{condition}' failed: {ex.Message}");
                return false; // Fail-safe: skip step on condition evaluation error (consistent with WF-001)
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
            string dataDir = StingToolsApp.DataPath;

            // HIGH-05: Return cached built-in list when data path is unchanged
            if (_cachedBuiltInPresets != null && _cachedBuiltInPresetsDataPath == dataDir)
            {
                // Still append fresh user-defined JSON files on top of the cached built-ins
                var cachedResult = new List<WorkflowPreset>(_cachedBuiltInPresets);
                AppendUserPresets(cachedResult, dataDir);
                return cachedResult;
            }

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

            // Phase 68: Enhanced coordinator productivity presets
            presets.Add(GetBuiltInPreset("COBieReadiness"));
            presets.Add(GetBuiltInPreset("DrawingIssue"));
            presets.Add(GetBuiltInPreset("SpatialQA"));

            // Phase 92: Speckle snapshot round-trip preset
            presets.Add(GetBuiltInPreset("SpeckleSnapshot"));

            // Remove any null entries from failed lookups
            presets.RemoveAll(p => p == null);

            // HIGH-05: Cache the built-in list so subsequent calls skip all GetBuiltInPreset() work
            _cachedBuiltInPresets = new List<WorkflowPreset>(presets);
            _cachedBuiltInPresetsDataPath = dataDir;

            // Remove any null entries from failed lookups
            presets.RemoveAll(p => p == null);

            // HIGH-05: Cache the built-in list so subsequent calls skip all GetBuiltInPreset() work
            _cachedBuiltInPresets = new List<WorkflowPreset>(presets);
            _cachedBuiltInPresetsDataPath = dataDir;

            // Remove any null entries from failed lookups
            presets.RemoveAll(p => p == null);

            // User-defined JSON files
            // Append user-defined JSON presets on top
            AppendUserPresets(presets, dataDir);

            return presets;
        }

        // HIGH-05: Extracted helper so both cached and uncached paths share the same JSON-loading logic
        private static void AppendUserPresets(List<WorkflowPreset> presets, string dataDir)
        {
            if (!Directory.Exists(dataDir)) return;
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

                // Phase 108j — BOQ × BCC integration preset.
                // Monthly cost review loop wiring the BOQ Cost Manager to
                // the BIM Coordination Center: refresh costs, snapshot,
                // compare to previous, validate containers (for COBie
                // coherence), dashboard + weekly report for the meeting.
                case "MonthlyCostReview":
                    return new WorkflowPreset
                    {
                        Name = "Monthly Cost Review",
                        Description = "BOQ × BCC integration — refresh rates, take a BOQ snapshot, compare to previous, raise issues on >10% category deltas, update Model Health, export report for the meeting.",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "BOQRefresh",           Label = "1. Refresh BOQ rates + parameters" },
                            new WorkflowStep { CommandTag = "ValidateTags",         Label = "2. Validate tag compliance (BOQ quality gate)" },
                            new WorkflowStep { CommandTag = "BOQSnapshotSave",      Label = "3. Save BOQ snapshot for this review cycle" },
                            new WorkflowStep { CommandTag = "BOQSnapshotCompare",   Label = "4. Compare to previous snapshot", Optional = true },
                            new WorkflowStep { CommandTag = "ComplianceGateCheck",  Label = "5. Compliance gate check",        Optional = true },
                            new WorkflowStep { CommandTag = "ModelHealthDashboard", Label = "6. Update Model Health dashboard" },
                            new WorkflowStep { CommandTag = "IssueDashboard",       Label = "7. Snapshot open issues list" },
                            new WorkflowStep { CommandTag = "WeeklyCoordinatorReport", Label = "8. Generate HTML cost-review report" },
                            new WorkflowStep { CommandTag = "BOQExportProfessional", Label = "9. Export Tender BOQ (priced copy for meeting)", Optional = true },
                        }
                    };

                // Phase 68: BIM coordinator productivity presets
                case "COBieReadiness":
                    return new WorkflowPreset
                    {
                        Name = "COBie Readiness",
                        Description = "Prepare model for COBie V2.4 export — validates tags, containers, types, and spatial data",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "RetagStale", Label = "1. Re-tag stale elements", Condition = "has_stale" },
                            new WorkflowStep { CommandTag = "ResolveAllIssues", Label = "2. Resolve placeholder tokens", Condition = "has_placeholders" },
                            new WorkflowStep { CommandTag = "CombineParameters", Label = "3. Write discipline containers", Condition = "has_container_gaps" },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "4. Validate ISO 19650 tags" },
                            new WorkflowStep { CommandTag = "SchemaValidate", Label = "5. Validate material schema" },
                            new WorkflowStep { CommandTag = "COBieExport", Label = "6. Export COBie V2.4 spreadsheet", MinCompliancePct = 85 },
                            new WorkflowStep { CommandTag = "TagRegisterExport", Label = "7. Export asset register CSV" },
                        }
                    };

                case "DrawingIssue":
                    return new WorkflowPreset
                    {
                        Name = "Drawing Issue",
                        Description = "Prepare drawings for issue — check naming, templates, print to PDF",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "AutoAssignTemplates", Label = "1. Auto-assign view templates" },
                            new WorkflowStep { CommandTag = "SheetNamingCheck", Label = "2. Check sheet naming compliance" },
                            new WorkflowStep { CommandTag = "AutoFixWarnings", Label = "3. Auto-fix annotation warnings", Condition = "has_warnings" },
                            new WorkflowStep { CommandTag = "SheetComplianceCheck", Label = "4. ISO sheet compliance check" },
                            new WorkflowStep { CommandTag = "BatchPrintSheets", Label = "5. Batch print to PDF" },
                            new WorkflowStep { CommandTag = "ExportSheetRegister", Label = "6. Export sheet register" },
                            new WorkflowStep { CommandTag = "CreateRevision", Label = "7. Create revision record" },
                        }
                    };

                case "SpatialQA":
                    return new WorkflowPreset
                    {
                        Name = "Spatial QA",
                        Description = "Validate rooms, areas, and spatial data for FM handover",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "RoomAudit", Label = "1. Audit rooms (unnamed/unplaced/unbounded)" },
                            new WorkflowStep { CommandTag = "SpatialConnectivityAudit", Label = "2. Validate room connectivity" },
                            new WorkflowStep { CommandTag = "AutoFixWarnings", Label = "3. Fix room enclosure warnings", Condition = "has_warnings" },
                            new WorkflowStep { CommandTag = "FamilyStagePopulate", Label = "4. Re-populate spatial tokens" },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "5. Validate updated tags" },
                            new WorkflowStep { CommandTag = "CompletenessDashboard", Label = "6. Show compliance dashboard" },
                        }
                    };

                // Phase 92: Speckle snapshot round-trip preset
                case "SpeckleSnapshot":
                    return new WorkflowPreset
                    {
                        Name = "SpeckleSnapshot",
                        Description = "Diff model against last snapshot, push to Speckle, capture compliance and warnings.",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "SpeckleDiff",         Label = "1. Diff against last Speckle snapshot" },
                            new WorkflowStep { CommandTag = "SpeckleSend",         Label = "2. Export tagged elements to snapshot" },
                            new WorkflowStep { CommandTag = "ComplianceSnapshot",  Label = "3. Capture compliance snapshot" },
                            new WorkflowStep { CommandTag = "WarningsSummary",     Label = "4. Capture warnings summary" },
                        }
                    };

                default:
                    StingLog.Warn($"WorkflowEngine: Unknown built-in preset '{name}'");
                    return new WorkflowPreset { Name = name, Description = $"Unknown preset: {name}", Steps = new List<WorkflowStep>() };
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
        /// LOG-13: Get the log file path inside the unified project root's
        /// _data folder so it never appears as a sibling of the .rvt.
        /// </summary>
        private static string GetLogPath(Document doc)
        {
            // Folder consolidation: prefer <root>/_data/workflow_log.jsonl
            try
            {
                string consolidated = ProjectFolderEngine.GetDataPath(doc);
                if (!string.IsNullOrEmpty(consolidated))
                    return Path.Combine(consolidated, "workflow_log.jsonl");
            }
            catch (Exception ex) { StingLog.Warn($"GetLogPath consolidated: {ex.Message}"); }

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
