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
    //    - Atomic TransactionGroup with rollback on failure
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
    /// Wrapped in TransactionGroup for atomic rollback.
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
            Document doc = commandData.Application.ActiveUIDocument.Document;
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

            using (TransactionGroup tg = new TransactionGroup(doc, $"STING Workflow: {preset.Name}"))
            {
                tg.Start();

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

                // Handle cancellation or failures — offer rollback
                if (cancelled || failed > 0)
                {
                    report.AppendLine(new string('─', 50));
                    report.AppendLine(cancelled
                        ? $"  CANCELLED at step {stepNum} ({passed} OK, {failed} failed, {skipped} skipped)"
                        : $"  {passed}/{stepNum} OK, {failed} failed, {skipped} skipped");
                    report.AppendLine($"  Duration: {totalSw.Elapsed.TotalSeconds:F1}s");

                    TaskDialog rollDlg = new TaskDialog($"Workflow: {preset.Name}");
                    rollDlg.MainInstruction = cancelled ? $"Cancelled at step {stepNum}" : $"{failed} step(s) failed";
                    rollDlg.MainContent = report.ToString() + "\n\nKeep completed steps or rollback all?";
                    rollDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        "Keep results", $"Commit {passed} completed steps");
                    rollDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        "Rollback all", "Undo all workflow changes");

                    if (rollDlg.Show() == TaskDialogResult.CommandLink2)
                    {
                        tg.RollBack();
                        TaskDialog.Show("Workflow", "All workflow changes rolled back.");
                        return Result.Cancelled;
                    }
                }

                tg.Assimilate();
            }

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
        /// DISP-01: Map ALL command tags to IExternalCommand instances.
        /// Comprehensive dispatch covering all 234+ commands matching
        /// StingCommandHandler tags for full workflow orchestration.
        /// </summary>
        private static IExternalCommand ResolveCommand(string tag)
        {
            switch (tag)
            {
                // ════════════════════════════════════════════════════════
                // SELECT — Category selectors
                // ════════════════════════════════════════════════════════
                case "SelectLighting": return new Select.SelectLightingCommand();
                case "SelectElectrical": return new Select.SelectElectricalCommand();
                case "SelectMechanical": return new Select.SelectMechanicalCommand();
                case "SelectPlumbing": return new Select.SelectPlumbingCommand();
                case "SelectAirTerminals": return new Select.SelectAirTerminalsCommand();
                case "SelectFurniture": return new Select.SelectFurnitureCommand();
                case "SelectDoors": return new Select.SelectDoorsCommand();
                case "SelectWindows": return new Select.SelectWindowsCommand();
                case "SelectRooms": return new Select.SelectRoomsCommand();
                case "SelectSprinklers": return new Select.SelectSprinklersCommand();
                case "SelectPipes": return new Select.SelectPipesCommand();
                case "SelectDucts": return new Select.SelectDuctsCommand();
                case "SelectConduits": return new Select.SelectConduitsCommand();
                case "SelectCableTrays": return new Select.SelectCableTraysCommand();
                case "SelectAllTaggable": return new Select.SelectAllTaggableCommand();

                // SELECT — State selectors
                case "SelectUntagged": return new Select.SelectUntaggedCommand();
                case "SelectTagged": return new Select.SelectTaggedCommand();
                case "SelectEmptyMark": return new Select.SelectEmptyMarkCommand();
                case "SelectPinned": return new Select.SelectPinnedCommand();
                case "SelectUnpinned": return new Select.SelectUnpinnedCommand();

                // SELECT — Spatial selectors
                case "SelectByLevel": return new Select.SelectByLevelCommand();
                case "SelectByRoom": return new Select.SelectByRoomCommand();

                // SELECT — Bulk operations
                case "BulkParamWrite": return new Select.BulkParamWriteCommand();

                // SELECT — Color By Parameter
                case "ColorByParameter": return new Select.ColorByParameterCommand();
                case "ClearColorOverrides": return new Select.ClearColorOverridesCommand();
                case "SaveColorPreset": return new Select.SaveColorPresetCommand();
                case "LoadColorPreset": return new Select.LoadColorPresetCommand();
                case "CreateFiltersFromColors": return new Select.CreateFiltersFromColorsCommand();

                // ════════════════════════════════════════════════════════
                // TAGS — Core tagging commands
                // ════════════════════════════════════════════════════════
                case "AutoTag": return new Tags.AutoTagCommand();
                case "BatchTag": return new Tags.BatchTagCommand();
                case "TagAndCombine": return new Tags.TagAndCombineCommand();
                case "TagNewOnly": return new Tags.TagNewOnlyCommand();
                case "TagChanged": return new Tags.TagChangedCommand();
                case "TagFormatMigration": return new Tags.TagFormatMigrationCommand();
                case "FamilyStagePopulate": return new Tags.FamilyStagePopulateCommand();
                case "BuildTags": return new Tags.BuildTagsCommand();
                case "AssignNumbers": return new Tags.AssignNumbersCommand();
                case "CombineParams":
                case "CombineParameters": return new Tags.CombineParametersCommand();
                case "CombinePreFlight": return new Tags.CombinePreFlightCommand();

                // TAGS — Setup / Config
                case "LoadParams":
                case "LoadSharedParams": return new Tags.LoadSharedParamsCommand();
                case "ConfigEditor": return new Tags.ConfigEditorCommand();
                case "TagConfig": return new Tags.TagConfigCommand();
                case "SyncParamSchema": return new Tags.SyncParameterSchemaCommand();
                case "AddParamRemap": return new Tags.AddParamRemapCommand();
                case "AuditParamSchema": return new Tags.AuditParameterSchemaCommand();

                // TAGS — Token writers
                case "SetDisc": return new Tags.SetDiscCommand();
                case "SetLoc": return new Tags.SetLocCommand();
                case "SetZone": return new Tags.SetZoneCommand();
                case "SetStatus": return new Tags.SetStatusCommand();

                // TAGS — Validation / QA
                case "ValidateTags": return new Tags.ValidateTagsCommand();
                case "PreTagAudit": return new Tags.PreTagAuditCommand();
                case "ResolveAllIssues": return new Tags.ResolveAllIssuesCommand();
                case "CompletenessDashboard": return new Tags.CompletenessDashboardCommand();

                // TAGS — Smart Tag Placement
                case "SmartPlaceTags": return new Tags.SmartPlaceTagsCommand();
                case "ArrangeTags": return new Tags.ArrangeTagsCommand();
                case "BatchPlaceTags": return new Tags.BatchPlaceTagsCommand();
                case "RemoveAnnotationTags": return new Tags.RemoveAnnotationTagsCommand();
                case "LearnTagPlacement": return new Tags.LearnTagPlacementCommand();
                case "ApplyTagTemplate": return new Tags.ApplyTagTemplateCommand();
                case "TagOverlapAnalysis": return new Tags.TagOverlapAnalysisCommand();
                case "BatchTagTextSize": return new Tags.BatchTagTextSizeCommand();
                case "SetTagCatLineWeight": return new Tags.SetTagCategoryLineWeightCommand();

                // TAGS — Rich TAG7 display
                case "RichTagNote": return new Tags.RichTagNoteCommand();
                case "ExportRichTagReport": return new Tags.ExportRichTagReportCommand();
                case "ViewTag7Sections": return new Tags.ViewTag7SectionsCommand();
                case "SwitchTag7Preset": return new Tags.SwitchTag7PresetCommand();
                case "RichSegmentNote": return new Tags.RichSegmentNoteCommand();
                case "ViewSegments": return new Tags.ViewSegmentsCommand();

                // TAGS — Presentation Mode
                case "SetPresentationMode": return new Tags.SetPresentationModeCommand();
                case "ViewLabelSpec": return new Tags.ViewLabelSpecCommand();
                case "ExportLabelGuide": return new Tags.ExportLabelGuideCommand();
                case "SetTag7HeadingStyle": return new Tags.SetTag7HeadingStyleCommand();

                // TAGS — Paragraph Depth
                case "SetParagraphDepth": return new Tags.SetParagraphDepthCommand();
                case "ToggleWarningVisibility": return new Tags.ToggleWarningVisibilityCommand();

                // TAGS — System Param Push
                case "SystemParamPush": return new Tags.SystemParamPushCommand();
                case "BatchSystemPush": return new Tags.BatchSystemPushCommand();
                case "SelectSystemElements": return new Tags.SelectSystemElementsCommand();

                // TAGS — Tag Family Creator
                case "CreateTagFamilies": return new Tags.CreateTagFamiliesCommand();
                case "LoadTagFamilies": return new Tags.LoadTagFamiliesCommand();
                case "ConfigureTagLabels": return new Tags.ConfigureTagLabelsCommand();
                case "AuditTagFamilies": return new Tags.AuditTagFamiliesCommand();

                // TAGS — Legend Builder (31 commands)
                case "CreateColorLegend": return new Tags.CreateColorLegendCommand();
                case "ExportColorLegendHtml": return new Tags.ExportColorLegendHtmlCommand();
                case "AutoCreateLegends": return new Tags.AutoCreateLegendsCommand();
                case "LegendFromView": return new Tags.LegendFromViewCommand();
                case "PlaceLegendOnSheet": return new Tags.PlaceLegendOnSheetCommand();
                case "SheetContextLegend": return new Tags.SheetContextLegendCommand();
                case "PlaceLegendOnAllSheets": return new Tags.PlaceLegendOnAllSheetsCommand();
                case "BatchSheetContextLegends": return new Tags.BatchSheetContextLegendsCommand();
                case "CreateTagLegend": return new Tags.CreateTagLegendCommand();
                case "SheetTagLegend": return new Tags.SheetTagLegendCommand();
                case "BatchTagLegends": return new Tags.BatchTagLegendsCommand();
                case "UpdateLegend": return new Tags.UpdateLegendCommand();
                case "DeleteStaleLegend": return new Tags.DeleteStaleLegendCommand();
                case "OneClickLegendPipeline": return new Tags.OneClickLegendPipelineCommand();
                case "MepSystemLegend": return new Tags.MepSystemLegendCommand();
                case "MaterialLegend": return new Tags.MaterialLegendCommand();
                case "CompoundTypeLegend": return new Tags.CompoundTypeLegendCommand();
                case "EquipmentLegend": return new Tags.EquipmentLegendCommand();
                case "FireRatingLegend": return new Tags.FireRatingLegendCommand();
                case "MasterLegendPipeline": return new Tags.MasterLegendPipelineCommand();
                case "FilterLegend": return new Tags.FilterLegendCommand();
                case "TemplateLegend": return new Tags.TemplateLegendCommand();
                case "VGCategoryLegend": return new Tags.VGCategoryLegendCommand();
                case "BatchTemplateLegend": return new Tags.BatchTemplateLegendCommand();
                case "FlexibleLegend": return new Tags.FlexibleLegendCommand();
                case "LegendFromPreset": return new Tags.LegendFromPresetCommand();
                case "ComponentTypeLegend": return new Tags.ComponentTypeLegendCommand();
                case "ColorReferenceLegend": return new Tags.ColorReferenceLegendCommand();
                case "LegendSyncAudit": return new Tags.LegendSyncAuditCommand();
                case "StatusLegend": return new Tags.StatusLegendCommand();
                case "WorksetLegend": return new Tags.WorksetLegendCommand();

                // ════════════════════════════════════════════════════════
                // ORGANISE — Tag operations
                // ════════════════════════════════════════════════════════
                case "TagSelected": return new Organise.TagSelectedCommand();
                case "ReTag": return new Organise.ReTagCommand();
                case "DeleteTags": return new Organise.DeleteTagsCommand();
                case "RenumberTags": return new Organise.RenumberTagsCommand();
                case "CopyTags": return new Organise.CopyTagsCommand();
                case "SwapTags": return new Organise.SwapTagsCommand();
                case "FixDuplicates": return new Organise.FixDuplicateTagsCommand();
                case "FindDuplicates": return new Organise.FindDuplicateTagsCommand();

                // ORGANISE — Leaders
                case "ToggleLeaders": return new Organise.ToggleLeadersCommand();
                case "AddLeaders": return new Organise.AddLeadersCommand();
                case "RemoveLeaders": return new Organise.RemoveLeadersCommand();
                case "AlignTags":
                case "AlignTagsH":
                case "AlignTagsV": return new Organise.AlignTagsCommand();
                case "ResetTagPositions": return new Organise.ResetTagPositionsCommand();
                case "ToggleTagOrientation": return new Organise.ToggleTagOrientationCommand();
                case "SnapLeaderElbow": return new Organise.SnapLeaderElbowCommand();
                case "AutoAlignLeaderText": return new Organise.AutoAlignLeaderTextCommand();
                case "FlipTags": return new Organise.FlipTagsCommand();
                case "AlignTagText": return new Organise.AlignTagTextCommand();
                case "PinTags": return new Organise.PinTagsCommand();
                case "NudgeTags": return new Organise.NudgeTagsCommand();
                case "AttachLeader": return new Organise.AttachLeaderCommand();
                case "SelectTagsWithLeaders": return new Organise.SelectTagsWithLeadersCommand();

                // ORGANISE — Appearance
                case "ColorTagsByDiscipline": return new Organise.ColorTagsByDisciplineCommand();
                case "SetTagTextColor": return new Organise.SetTagTextColorCommand();
                case "SetLeaderColor": return new Organise.SetLeaderColorCommand();
                case "SplitTagLeaderColor": return new Organise.SplitTagLeaderColorCommand();
                case "ClearAnnotationColors": return new Organise.ClearAnnotationColorsCommand();
                case "TagAppearance": return new Organise.TagAppearanceCommand();
                case "SetTagBox": return new Organise.SetTagBoxAppearanceCommand();
                case "QuickTagStyle": return new Organise.QuickTagStyleCommand();
                case "SetTagLineWeight": return new Organise.SetTagLineWeightCommand();
                case "ColorTagsByParam": return new Organise.ColorTagsByParameterCommand();
                case "SwapTagType": return new Organise.SwapTagTypeCommand();

                // ORGANISE — Analysis
                case "TagStats": return new Organise.TagStatsCommand();
                case "AuditTagsCSV": return new Organise.AuditTagsCSVCommand();
                case "SelectByDiscipline": return new Organise.SelectByDisciplineCommand();
                case "TagRegisterExport": return new Organise.TagRegisterExportCommand();
                case "HighlightInvalid": return new Organise.HighlightInvalidCommand();
                case "ClearOverrides": return new Organise.ClearOverridesCommand();

                // ORGANISE — Advanced Automation
                case "AnomalyAutoFix": return new Organise.AnomalyAutoFixCommand();

                // ════════════════════════════════════════════════════════
                // DOCS — Documentation commands
                // ════════════════════════════════════════════════════════
                case "SheetOrganizer": return new Docs.SheetOrganizerCommand();
                case "ViewOrganizer": return new Docs.ViewOrganizerCommand();
                case "SheetIndex": return new Docs.SheetIndexCommand();
                case "Transmittal": return new Docs.TransmittalCommand();
                case "DeleteUnusedViews": return new Docs.DeleteUnusedViewsCommand();
                case "SheetNamingCheck": return new Docs.SheetNamingCheckCommand();
                case "AutoNumberSheets": return new Docs.AutoNumberSheetsCommand();
                case "AlignViewports": return new Docs.AlignViewportsCommand();
                case "RenumberViewports": return new Docs.RenumberViewportsCommand();
                case "TextCase": return new Docs.TextCaseCommand();
                case "SumAreas": return new Docs.SumAreasCommand();

                // DOCS — View Automation
                case "DuplicateView": return new Docs.DuplicateViewCommand();
                case "BatchRenameViews": return new Docs.BatchRenameViewsCommand();
                case "CopyViewSettings": return new Docs.CopyViewSettingsCommand();
                case "AutoPlaceViewports": return new Docs.AutoPlaceViewportsCommand();
                case "CropToContent": return new Docs.CropToContentCommand();
                case "BatchAlignViewports": return new Docs.BatchAlignViewportsCommand();

                // DOCS — Documentation Automation
                case "BatchCreateViews": return new Docs.BatchCreateViewsCommand();
                case "BatchCreateSheets": return new Docs.BatchCreateSheetsCommand();
                case "CreateDependentViews": return new Docs.CreateDependentViewsCommand();
                case "ScopeBoxManager": return new Docs.ScopeBoxManagerCommand();
                case "ViewTemplateAssigner": return new Docs.ViewTemplateAssignerCommand();
                case "DocumentationPackage": return new Docs.DocumentationPackageCommand();
                case "BatchCreateSections": return new Docs.BatchCreateSectionsCommand();
                case "BatchCreateElevations": return new Docs.BatchCreateElevationsCommand();
                case "DrawingRegister": return new Docs.DrawingRegisterCommand();
                case "ProjectBrowserOrganizer": return new Docs.ProjectBrowserOrganizerCommand();
                case "RevisionCloudAuto": return new Docs.RevisionCloudAutoCreateCommand();

                // ════════════════════════════════════════════════════════
                // TEMP — Setup
                // ════════════════════════════════════════════════════════
                case "MasterSetup": return new Temp.MasterSetupCommand();
                case "ProjectSetup": return new Temp.ProjectSetupCommand();
                case "CreateParameters": return new Temp.CreateParametersCommand();
                case "CheckData": return new Temp.CheckDataCommand();

                // TEMP — Materials
                case "CreateBLEMaterials": return new Temp.CreateBLEMaterialsCommand();
                case "CreateMEPMaterials": return new Temp.CreateMEPMaterialsCommand();

                // TEMP — Family types
                case "CreateWalls": return new Temp.CreateWallsCommand();
                case "CreateFloors": return new Temp.CreateFloorsCommand();
                case "CreateCeilings": return new Temp.CreateCeilingsCommand();
                case "CreateRoofs": return new Temp.CreateRoofsCommand();
                case "CreateDucts": return new Temp.CreateDuctsCommand();
                case "CreatePipes": return new Temp.CreatePipesCommand();
                case "CreateCableTrays": return new Temp.CreateCableTraysCommand();
                case "CreateConduits": return new Temp.CreateConduitsCommand();

                // TEMP — Schedules
                case "FullAutoPopulate": return new Temp.FullAutoPopulateCommand();
                case "BatchSchedules": return new Temp.BatchSchedulesCommand();
                case "MaterialSchedules": return new Temp.CreateMaterialSchedulesCommand();
                case "AutoPopulate": return new Temp.AutoPopulateCommand();
                case "EvaluateFormulas":
                case "FormulaEvaluator": return new Temp.FormulaEvaluatorCommand();
                case "ExportCSV": return new Temp.ExportCSVCommand();

                // TEMP — Corporate Schedules
                case "CorporateTitleBlock": return new Temp.CorporateTitleBlockScheduleCommand();
                case "DrawingRegisterSchedule": return new Temp.DrawingRegisterScheduleCommand();

                // TEMP — Schedule Enhancements
                case "ScheduleAudit": return new Temp.ScheduleAuditCommand();
                case "ScheduleCompare": return new Temp.ScheduleCompareCommand();
                case "ScheduleDuplicate": return new Temp.ScheduleDuplicateCommand();
                case "ScheduleRefresh": return new Temp.ScheduleRefreshCommand();
                case "ScheduleFieldMgr": return new Temp.ScheduleFieldManagerCommand();
                case "ScheduleColor": return new Temp.ScheduleColorCommand();
                case "ScheduleStats": return new Temp.ScheduleStatsCommand();
                case "ScheduleDelete": return new Temp.ScheduleDeleteCommand();
                case "ScheduleReport": return new Temp.ScheduleReportCommand();

                // TEMP — Templates / Views
                case "CreateFilters": return new Temp.CreateFiltersCommand();
                case "ApplyFilters": return new Temp.ApplyFiltersToViewsCommand();
                case "CreateWorksets": return new Temp.CreateWorksetsCommand();
                case "ViewTemplates": return new Temp.ViewTemplatesCommand();
                case "CreateLinePatterns": return new Temp.CreateLinePatternsCommand();
                case "CreatePhases": return new Temp.CreatePhasesCommand();

                // TEMP — Template Manager
                case "TemplateSetupWizard": return new Temp.TemplateSetupWizardCommand();
                case "AutoAssignTemplates": return new Temp.AutoAssignTemplatesCommand();
                case "TemplateAudit": return new Temp.TemplateAuditCommand();
                case "TemplateDiff": return new Temp.TemplateDiffCommand();
                case "TemplateComplianceScore": return new Temp.TemplateComplianceScoreCommand();
                case "AutoFixTemplate": return new Temp.AutoFixTemplateCommand();
                case "SyncTemplateOverrides": return new Temp.SyncTemplateOverridesCommand();
                case "CloneTemplate": return new Temp.CloneTemplateCommand();
                case "BatchVGReset": return new Temp.BatchVGResetCommand();

                // TEMP — Styles
                case "CreateFillPatterns": return new Temp.CreateFillPatternsCommand();
                case "CreateLineStyles": return new Temp.CreateLineStylesCommand();
                case "CreateObjectStyles": return new Temp.CreateObjectStylesCommand();
                case "CreateTextStyles": return new Temp.CreateTextStylesCommand();
                case "CreateDimStyles":
                case "CreateDimensionStyles": return new Temp.CreateDimensionStylesCommand();
                case "CreateVGOverrides": return new Temp.CreateVGOverridesCommand();

                // TEMP — Data Pipeline
                case "ValidateTemplate": return new Temp.ValidateTemplateCommand();
                case "DynamicBindings": return new Temp.DynamicBindingsCommand();
                case "SchemaValidate": return new Temp.SchemaValidateCommand();
                case "BOQExport": return new Temp.BOQExportCommand();
                case "TemplateVGAudit": return new Temp.TemplateVGAuditCommand();
                case "ExportIfcPropertyMap": return new Temp.ExportIfcPropertyMapCommand();
                case "ValidateBepCompliance": return new Temp.ValidateBepComplianceCommand();
                case "BatchFamilyParams":
                case "BatchAddFamilyParams": return new Temp.BatchAddFamilyParamsCommand();
                case "CreateTemplateSchedules": return new Temp.CreateTemplateSchedulesCommand();

                // TEMP — Advanced Automation
                case "ClashDetect": return new Temp.ClashDetectionCommand();
                case "IFCExport": return new Temp.IFCExportCommand();
                case "ExcelImport": return new Temp.ExcelBOQImportCommand();
                case "KeynoteSync": return new Temp.KeynoteSyncCommand();

                // ════════════════════════════════════════════════════════
                // CORE — Workflow / AutoTagger
                // ════════════════════════════════════════════════════════
                case "RunWorkflow": return new Core.WorkflowPresetCommand();
                case "ListWorkflows": return new Core.ListWorkflowPresetsCommand();
                case "CreateWorkflow": return new Core.CreateWorkflowPresetCommand();
                case "AutoTaggerToggle": return new Core.AutoTaggerToggleCommand();

                default: return null;
            }
        }

        /// <summary>Get all available presets (built-in + user JSON files).</summary>
        public static List<WorkflowPreset> GetAvailablePresets()
        {
            var presets = new List<WorkflowPreset>();

            // Built-in presets (WF-06: added ProjectHandover)
            presets.Add(GetBuiltInPreset("ProjectKickoff"));
            presets.Add(GetBuiltInPreset("DailyQA"));
            presets.Add(GetBuiltInPreset("DocumentPackage"));
            presets.Add(GetBuiltInPreset("ProjectHandover"));

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

                // WF-06: ProjectHandover 9-step built-in preset
                case "ProjectHandover":
                    return new WorkflowPreset
                    {
                        Name = "Project Handover",
                        Description = "Final handover: resolve all issues, validate, export registers and BOQ",
                        IsBuiltIn = true,
                        Steps = new List<WorkflowStep>
                        {
                            new WorkflowStep { CommandTag = "ResolveAllIssues", Label = "Resolve All Tag Issues (100% compliance)" },
                            new WorkflowStep { CommandTag = "ValidateTags", Label = "Validate ISO 19650 Compliance" },
                            new WorkflowStep { CommandTag = "ValidateTemplate", Label = "Validate Data Integrity (45 checks)" },
                            new WorkflowStep { CommandTag = "SheetNamingCheck", Label = "ISO 19650 Sheet Naming Check" },
                            new WorkflowStep { CommandTag = "TagRegisterExport", Label = "Export Asset Register (40+ columns)" },
                            new WorkflowStep { CommandTag = "AuditTagsCSV", Label = "Export Full Tag Audit CSV" },
                            new WorkflowStep { CommandTag = "BOQExport", Label = "Export Bill of Quantities (XLSX)" },
                            new WorkflowStep { CommandTag = "DrawingRegister", Label = "Generate Drawing Register" },
                            new WorkflowStep { CommandTag = "ValidateBepCompliance", Label = "Validate BEP Compliance" },
                        }
                    };

                default:
                    return new WorkflowPreset { Name = name, Description = "Unknown preset" };
            }
        }
    }
}
