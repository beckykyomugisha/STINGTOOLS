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

            // Let user pick a preset
            TaskDialog picker = new TaskDialog("Run Workflow Preset");
            picker.MainInstruction = "Select a workflow preset to run";
            var sb = new StringBuilder();
            for (int i = 0; i < Math.Min(presets.Count, 4); i++)
            {
                var p = presets[i];
                sb.AppendLine($"  {p.Name}: {p.Description} ({p.Steps.Count} steps)");
            }
            picker.MainContent = sb.ToString();

            if (presets.Count >= 1)
                picker.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    presets[0].Name, $"{presets[0].Description} ({presets[0].Steps.Count} steps)");
            if (presets.Count >= 2)
                picker.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    presets[1].Name, $"{presets[1].Description} ({presets[1].Steps.Count} steps)");
            if (presets.Count >= 3)
                picker.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    presets[2].Name, $"{presets[2].Description} ({presets[2].Steps.Count} steps)");
            if (presets.Count >= 4)
                picker.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                    presets[3].Name, $"{presets[3].Description} ({presets[3].Steps.Count} steps)");

            var result = picker.Show();
            WorkflowPreset selected = null;
            if (result == TaskDialogResult.CommandLink1) selected = presets[0];
            else if (result == TaskDialogResult.CommandLink2 && presets.Count >= 2) selected = presets[1];
            else if (result == TaskDialogResult.CommandLink3 && presets.Count >= 3) selected = presets[2];
            else if (result == TaskDialogResult.CommandLink4 && presets.Count >= 4) selected = presets[3];
            else return Result.Cancelled;

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
            StingLog.Info($"Workflow '{preset.Name}': starting {preset.Steps.Count} steps");

            var report = new StringBuilder();
            report.AppendLine($"Workflow: {preset.Name}");
            report.AppendLine(new string('═', 50));

            int stepNum = 0;
            int passed = 0;
            int failed = 0;
            int skipped = 0;
            bool cancelled = false;
            var totalSw = Stopwatch.StartNew();

            foreach (var step in preset.Steps)
            {
                stepNum++;

                // Check cancellation between steps
                if (EscapeChecker.IsEscapePressed())
                {
                    cancelled = true;
                    report.AppendLine($"  {stepNum,2}. {step.Label} — CANCELLED (Escape)");
                    StingLog.Info($"Workflow step {stepNum}: cancelled by user");
                    break;
                }

                // Evaluate condition (workshared check etc.)
                if (!string.IsNullOrEmpty(step.Condition))
                {
                    if (step.Condition == "workshared" && !doc.IsWorkshared)
                    {
                        skipped++;
                        report.AppendLine($"  {stepNum,2}. {step.Label} — SKIPPED (not workshared)");
                        continue;
                    }
                }

                // Execute via command dispatch
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
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    report.AppendLine($"  {stepNum,2}. {step.Label} — FAILED: {ex.Message}");
                    StingLog.Error($"Workflow step {stepNum}: {step.Label}", ex);

                    if (step.Optional)
                        skipped++;
                    else
                        failed++;
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

            return passed > 0 ? Result.Succeeded : Result.Failed;
        }

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

                // Legends
                case "AutoCreateLegends": return new Tags.AutoCreateLegendsCommand();

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
                        }
                    };

                case "DailyQA":
                    return new WorkflowPreset
                    {
                        Name = "Daily QA Sync",
                        Description = "Incremental sync — tag new elements, validate, audit",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "TagNewOnly", Label = "Tag New Elements Only" },
                            new WorkflowStep { CommandTag = "TagChanged", Label = "Update Changed Elements" },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "Validate ISO 19650 Compliance" },
                            new WorkflowStep { CommandTag = "ValidateTemplate", Label = "Validate Data Integrity (45 checks)" },
                            new WorkflowStep { CommandTag = "AutoAssignTemplates", Label = "Re-Assign Templates (new views)" },
                            new WorkflowStep { CommandTag = "AutoFixTemplate", Label = "Auto-Fix Template Issues" },
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

                default:
                    return new WorkflowPreset { Name = name, Description = "Unknown preset" };
            }
        }
    }
}
