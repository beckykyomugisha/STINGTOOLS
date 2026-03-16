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
    }

    internal static class WorkflowEngine
    {
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
            StingLog.Info($"Workflow '{preset.Name}': starting {preset.Steps.Count} steps" +
                (preset.RollbackOnFailure ? " (rollback on failure)" : ""));

            var report = new StringBuilder();
            report.AppendLine($"Workflow: {preset.Name}");
            report.AppendLine(new string('═', 50));

            int stepNum = 0;
            int passed = 0;
            int failed = 0;
            int skipped = 0;
            bool cancelled = false;
            double complianceBefore = 0;
            try { var scan = ComplianceScan.Scan(doc); complianceBefore = scan.CompliancePercent; }
            catch { }
            var totalSw = Stopwatch.StartNew();

            // LOG-06: Wrap in TransactionGroup when rollback_on_failure is enabled
            TransactionGroup tg = null;
            if (preset.RollbackOnFailure)
            {
                tg = new TransactionGroup(doc, $"STING Workflow: {preset.Name}");
                tg.Start();
            }

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


                    if (!string.IsNullOrEmpty(step.Condition))
                    {
                        if (step.Condition == "workshared" && !doc.IsWorkshared)
                        {
                            skipped++;
                            report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (not workshared)");
                            continue;
                        }
                    }

                    // F2 / G5.2: Evaluate compliance threshold conditions
                    if (step.MaxCompliancePct.HasValue || step.MinCompliancePct.HasValue
                        || step.RequiresStaleElements)
                    {
                        try
                        {
                            var scan = ComplianceScan.Scan(doc);
                            if (scan != null)
                            {
                                double pct = scan.CompliancePercent;
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
                            }
                            if (step.RequiresStaleElements)
                            {
                                bool hasStale = new FilteredElementCollector(doc)
                                    .WhereElementIsNotElementType()
                                    .Any(e =>
                                    {
                                        try { var p = e.LookupParameter(ParamRegistry.STALE); return p != null && p.AsInteger() == 1; }
                                        catch { return false; }
                                    });
                                if (!hasStale)
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
                        Result stepResult = RunCommandByTag(step.CommandTag, commandData, elements);
                        sw.Stop();
                        string status = stepResult == Result.Succeeded ? "OK" :
                                         stepResult == Result.Cancelled ? "SKIP" : "WARN";
                        report.AppendLine($"  {stepNum,2}. {step.Label} — {status} ({sw.Elapsed.TotalSeconds:F1}s)");

                        if (stepResult == Result.Succeeded)
                            passed++;
                        else if (step.Optional)
                            skipped++;
                        else
                            failed++;

                        StingLog.Info($"Workflow step {stepNum}: {step.Label} — {status} ({sw.Elapsed.TotalSeconds:F1}s)");

                        // LOG-06: If rollback enabled and a non-optional step failed, stop
                        if (preset.RollbackOnFailure && stepResult == Result.Failed && !step.Optional)
                        {
                            report.AppendLine($"\n  *** Non-optional step failed — rolling back all changes ***");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        report.AppendLine($"  {stepNum,2}. {step.Label} — FAILED: {ex.Message}");
                        StingLog.Error($"Workflow step {stepNum}: {step.Label}", ex);

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
                        try { tg.RollBack(); } catch { }
                    }
                    tg.Dispose();
                }
            }

            totalSw.Stop();

            report.AppendLine(new string('─', 50));
            report.AppendLine($"  Complete: {passed}/{preset.Steps.Count} steps OK");
            report.AppendLine($"  Skipped: {skipped}, Failed: {failed}");
            report.AppendLine($"  Duration: {totalSw.Elapsed.TotalSeconds:F1}s");

            TaskDialog td = new TaskDialog($"Workflow: {preset.Name}");
            td.MainInstruction = $"{preset.Name}: {passed}/{preset.Steps.Count} steps complete";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"Workflow '{preset.Name}' complete: {passed}/{preset.Steps.Count} OK, " +
                $"{failed} failed, elapsed={totalSw.Elapsed.TotalSeconds:F1}s");

            // LOG-13: Persist run record as JSONL (one JSON object per line)
            try
            {
                double complianceAfter = 0;
                try { ComplianceScan.InvalidateCache(); var scan = ComplianceScan.Scan(doc); complianceAfter = scan.CompliancePercent; }
                catch { }

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
                    ComplianceAfter = Math.Round(complianceAfter, 1)
                };
                SaveRunRecord(record, doc);
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

        /// <summary>
        /// Run a command by its StingCommandHandler dispatch tag, mapped to IExternalCommand classes.
        /// </summary>
        private static Result RunCommandByTag(string tag, ExternalCommandData data, ElementSet elems)
        {
            IExternalCommand cmd = ResolveCommand(tag);
            if (cmd == null)
            {
                StingLog.Warn($"WorkflowEngine: unknown command tag '{tag}'");
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

                default: return null;
            }
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
                            new WorkflowStep { CommandTag = "TagNewOnly", Label = "Tag new elements only" },
                            new WorkflowStep { CommandTag = "TagChanged", Label = "Update changed element tokens (delta sync)" },
                            new WorkflowStep { CommandTag = "RetagStale", Label = "Retag stale elements", Optional = true, RequiresStaleElements = true },
                            new WorkflowStep { CommandTag = "AutoPopulate", Label = "Sync Revit native params → STING shared" },
                            new WorkflowStep { CommandTag = "EvaluateFormulas", Label = "Evaluate 199 dependency formulas" },
                            new WorkflowStep { CommandTag = "CombineParameters", Label = "Update all tag containers" },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "Validate ISO 19650 compliance" },
                            new WorkflowStep { CommandTag = "ValidateTemplate", Label = "Validate data integrity (45 checks)" },
                            new WorkflowStep { CommandTag = "AutoAssignTemplates", Label = "Re-assign templates to new views" },
                            new WorkflowStep { CommandTag = "AutoFixTemplate", Label = "Auto-fix template issues" },
                            new WorkflowStep { CommandTag = "AutoRevisionOnTagChange", Label = "Auto-revision check (score-based)" },
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

                default:
                    return new WorkflowPreset { Name = name, Description = "Unknown preset" };
            }
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
            catch { }
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
                    catch { }
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
                try { date = DateTime.Parse(r.Timestamp).ToString("yyyy-MM-dd HH:mm"); } catch { }
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
