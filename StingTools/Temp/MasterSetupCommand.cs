using System;
using System.Diagnostics;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Temp
{
    /// <summary>
    /// One-click "Master Setup" command for maximum automation workflow.
    /// Runs the full STING template setup sequence in order:
    ///   1.  Load shared parameters (Pass 1 + Pass 2)
    ///   2.  Create BLE materials (815 building elements)
    ///   3.  Create MEP materials (464 MEP elements)
    ///   4.  Create wall/floor/ceiling/roof types from CSV
    ///   5.  Create duct/pipe types from CSV
    ///   6.  Batch create schedules (168 definitions)
    ///   7.  Evaluate formulas (199 dependency-ordered formulas)
    ///   8.  Tag &amp; Combine (full pipeline: populate + tag + combine all containers)
    ///   9.  Create view filters (28+ with parameter rules)
    ///  10.  Create worksets (35 ISO 19650)
    ///  11.  Create view templates (23 with VG overrides)
    ///  12.  Fill patterns, line styles, object styles
    ///  13.  Text styles, dimension styles
    ///  14.  Apply filters + VG overrides (5-layer intelligence)
    ///  15.  Batch family parameters (4,686 from CSV)
    ///  16.  Auto-assign templates + auto-fix health
    ///  17.  Auto-create legends (discipline + system + filter)
    ///
    /// Wrapped in a TransactionGroup for atomic rollback: if any critical step
    /// fails, the user can choose to rollback all changes or keep partial results.
    /// Each step reports its result with timing information.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MasterSetupCommand : IExternalCommand, Core.IPanelCommand
    {
        public Result Execute(UIApplication app)
        {
            try
            {
                return ExecuteCore(app);
            }
            catch (Exception ex)
            {
                StingLog.Error("MasterSetupCommand crashed", ex);
                try { TaskDialog.Show("Master Setup", $"Critical error: {ex.GetType().Name}\n{ex.Message}"); } catch { }
                return Result.Failed;
            }
        }

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.SafeApp();
                return ExecuteCore(uiApp);
            }
            catch (Exception ex)
            {
                StingLog.Error("MasterSetupCommand crashed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private Result ExecuteCore(UIApplication uiApp)
        {
            TaskDialog confirm = new TaskDialog("STING Master Setup");
            confirm.MainInstruction = "Run full project setup?";
            confirm.MainContent =
                "This will execute the complete STING template automation:\n\n" +
                "  1.  Load shared parameters (universal + discipline)\n" +
                "  2.  Create BLE materials (815 definitions)\n" +
                "  3.  Create MEP materials (464 definitions)\n" +
                "  4.  Create wall/floor/ceiling/roof types\n" +
                "  5.  Create duct/pipe types\n" +
                "  6.  Batch create schedules (168 definitions)\n" +
                "  7.  Evaluate formulas (199 dependency-ordered)\n" +
                "  8.  Tag & Combine (full pipeline + TAG7 narrative)\n" +
                "  9.  Create view filters (28+ with parameter rules)\n" +
                " 10.  Create worksets (35 ISO 19650)\n" +
                " 11.  Create view templates (23 with VG overrides)\n" +
                " 12.  Fill patterns, line styles, object styles\n" +
                " 13.  Text styles, dimension styles\n" +
                " 14.  Apply filters + VG overrides (5-layer intelligence)\n" +
                " 15.  Batch family parameters (4,686 from CSV)\n" +
                " 16.  Auto-assign templates + auto-fix health\n" +
                " 17.  Auto-create legends (discipline + system)\n\n" +
                "All steps are grouped atomically — if critical steps fail,\n" +
                "you can rollback all changes.\n\n" +
                "This may take several minutes for a new project.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            Document doc = uiApp.ActiveUIDocument.Document;
            StingLog.Info("Master Setup: starting full automation workflow");
            var report = new StringBuilder();
            report.AppendLine("STING Master Setup Results");
            report.AppendLine(new string('═', 45));

            int stepNum = 0;
            int passed = 0;
            int failed = 0;
            var totalSw = Stopwatch.StartNew();

            // Step 0: Load project_config.json so tag format, LOC/ZONE codes,
            // and discipline mappings reflect user's project settings
            string configPath = StingToolsApp.FindDataFile("project_config.json");
            if (!string.IsNullOrEmpty(configPath))
            {
                TagConfig.LoadFromFile(configPath);
                StingLog.Info($"Master Setup: loaded project config from {configPath}");
                report.AppendLine($"   0. Load project_config.json — OK");
            }
            else
            {
                TagConfig.LoadDefaults();
                StingLog.Info("Master Setup: project_config.json not found, using defaults");
                report.AppendLine($"   0. Load project_config.json — SKIPPED (using defaults)");
            }

            using (TransactionGroup tg = new TransactionGroup(doc, "STING Master Setup"))
            {
                tg.Start();

                // Step 1: Load shared parameters (critical — other steps depend on it)
                passed += RunStep(ref stepNum, report, "Load Shared Parameters",
                    () => RunCommand(new Tags.LoadSharedParamsCommand()));

                // If parameter loading failed, offer rollback
                if (passed == 0)
                {
                    StingLog.Error("Master Setup: critical step 1 failed — offering rollback");
                    TaskDialog critFail = new TaskDialog("Master Setup — Critical Failure");
                    critFail.MainInstruction = "Shared parameter loading failed";
                    critFail.MainContent =
                        "Step 1 (Load Shared Parameters) failed. Subsequent steps\n" +
                        "depend on these parameters. Continue anyway or rollback?";
                    critFail.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        "Continue anyway", "Attempt remaining steps despite the failure");
                    critFail.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        "Rollback all", "Undo all changes and abort");
                    if (critFail.Show() == TaskDialogResult.CommandLink2)
                    {
                        tg.RollBack();
                        TaskDialog.Show("Master Setup", "All changes rolled back.");
                        return Result.Cancelled;
                    }
                }

                // Helper: run a step and handle cancellation (-1 return)
                bool userCancelled = false;
                int DoStep(string label, Func<Result> action)
                {
                    if (userCancelled) return 0;
                    int result = RunStep(ref stepNum, report, label, action);
                    if (result == -1) { userCancelled = true; return 0; }
                    return result;
                }

                // Step 2: Create BLE Materials
                passed += DoStep("Create BLE Materials",
                    () => RunCommand(new CreateBLEMaterialsCommand()));

                // Step 3: Create MEP Materials
                passed += DoStep("Create MEP Materials",
                    () => RunCommand(new CreateMEPMaterialsCommand()));

                // Step 4: Create compound types (Walls, Floors, Ceilings, Roofs)
                passed += DoStep("Create Wall Types",
                    () => RunCommand(new CreateWallsCommand()));
                passed += DoStep("Create Floor Types",
                    () => RunCommand(new CreateFloorsCommand()));
                passed += DoStep("Create Ceiling Types",
                    () => RunCommand(new CreateCeilingsCommand()));
                passed += DoStep("Create Roof Types",
                    () => RunCommand(new CreateRoofsCommand()));

                // Step 5: Create MEP types (Ducts, Pipes)
                passed += DoStep("Create Duct Types",
                    () => RunCommand(new CreateDuctsCommand()));
                passed += DoStep("Create Pipe Types",
                    () => RunCommand(new CreatePipesCommand()));

                // Issue #8: Regenerate model after large batch operations to ensure consistency
                try { doc.Regenerate(); } catch { }

                // Step 6: Batch create schedules
                passed += DoStep("Batch Create Schedules",
                    () => RunCommand(new BatchSchedulesCommand()));

                // Step 7: Evaluate formulas (199 dependency-ordered formulas)
                passed += DoStep("Evaluate Formulas (199 definitions)",
                    () => RunCommand(new FormulaEvaluatorCommand()));

                // Issue #8: Regenerate after schedules + formulas before tagging
                try { doc.Regenerate(); } catch { }

                // Step 8: Tag & Combine (full pipeline: populate + tag + combine + TAG7 narrative)
                passed += DoStep("Tag & Combine (full pipeline + TAG7)",
                    () => RunCommand(new Tags.TagAndCombineCommand()));

                // Step 9: Create view filters
                passed += DoStep("Create View Filters",
                    () => RunCommand(new CreateFiltersCommand()));

                // Step 10: Create worksets (only if worksharing enabled)
                if (doc.IsWorkshared)
                {
                    passed += DoStep("Create Worksets",
                        () => RunCommand(new CreateWorksetsCommand()));
                }
                else if (!userCancelled)
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Worksets — SKIPPED (not workshared)");
                }

                // Step 11: Create view templates
                passed += DoStep("Create View Templates",
                    () => RunCommand(new ViewTemplatesCommand()));

                // Step 12: Fill patterns + line styles + object styles
                passed += DoStep("Create Fill Patterns",
                    () => RunCommand(new CreateFillPatternsCommand()));
                passed += DoStep("Create Line Styles",
                    () => RunCommand(new CreateLineStylesCommand()));
                passed += DoStep("Configure Object Styles",
                    () => RunCommand(new CreateObjectStylesCommand()));

                // Step 13: Text styles + dimension styles
                passed += DoStep("Create Text Styles",
                    () => RunCommand(new CreateTextStylesCommand()));
                passed += DoStep("Create Dimension Styles",
                    () => RunCommand(new CreateDimensionStylesCommand()));

                // Step 14: Apply filters to templates + VG overrides
                passed += DoStep("Apply Filters to Templates",
                    () => RunCommand(new ApplyFiltersToViewsCommand()));
                passed += DoStep("Apply VG Overrides (5-layer)",
                    () => RunCommand(new CreateVGOverridesCommand()));

                // Step 15: Batch family parameters from CSV
                passed += DoStep("Batch Family Params (CSV-driven)",
                    () => RunCommand(new BatchAddFamilyParamsCommand()));

                // Step 16: Auto-assign templates + auto-fix
                passed += DoStep("Auto-Assign Templates (5-layer)",
                    () => RunCommand(new AutoAssignTemplatesCommand()));
                passed += DoStep("Auto-Fix Template Health",
                    () => RunCommand(new AutoFixTemplateCommand()));

                // Step 17: Auto-create legends (discipline, system, filter)
                passed += DoStep("Auto-Create Legends (discipline + system)",
                    () => RunCommand(new Tags.AutoCreateLegendsCommand()));

                // Handle user cancellation — offer rollback of partial results
                if (userCancelled)
                {
                    totalSw.Stop();
                    StingLog.Info($"Master Setup: cancelled by user at step {stepNum}");
                    report.AppendLine(new string('─', 45));
                    report.AppendLine($"  CANCELLED at step {stepNum} ({passed} completed)");

                    TaskDialog cancelDlg = new TaskDialog("Master Setup — Cancelled");
                    cancelDlg.MainInstruction = $"Cancelled at step {stepNum}";
                    cancelDlg.MainContent = report.ToString() +
                        "\n\nKeep completed steps or rollback all?";
                    cancelDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        "Keep results", $"Commit {passed} completed steps");
                    cancelDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        "Rollback all", "Undo all changes");

                    if (cancelDlg.Show() == TaskDialogResult.CommandLink2)
                    {
                        tg.RollBack();
                        TaskDialog.Show("Master Setup", "All changes rolled back.");
                        return Result.Cancelled;
                    }

                    tg.Assimilate();
                    TaskDialog.Show("Master Setup",
                        $"Kept {passed} completed steps.\nRemaining steps were cancelled.");
                    return passed > 0 ? Result.Succeeded : Result.Cancelled;
                }

                failed = stepNum - passed;
                totalSw.Stop();

                // If failures occurred, offer rollback
                if (failed > 0)
                {
                    report.AppendLine(new string('─', 45));
                    report.AppendLine($"  {passed}/{stepNum} succeeded, {failed} failed");
                    report.AppendLine($"  Duration: {totalSw.Elapsed.TotalSeconds:F1}s");

                    TaskDialog rollbackDlg = new TaskDialog("Master Setup — Failures Detected");
                    rollbackDlg.MainInstruction = $"{failed} step(s) failed";
                    rollbackDlg.MainContent = report.ToString() +
                        "\n\nKeep partial results or rollback all changes?";
                    rollbackDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        "Keep results", $"Commit {passed} successful steps");
                    rollbackDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        "Rollback all", "Undo all changes from this session");

                    if (rollbackDlg.Show() == TaskDialogResult.CommandLink2)
                    {
                        tg.RollBack();
                        StingLog.Info("Master Setup: user chose rollback after failures");
                        TaskDialog.Show("Master Setup", "All changes rolled back.");
                        return Result.Cancelled;
                    }
                }

                tg.Assimilate();
            }

            report.AppendLine(new string('─', 45));
            report.AppendLine($"  Complete: {passed}/{stepNum} steps succeeded");
            report.AppendLine($"  Duration: {totalSw.Elapsed.TotalSeconds:F1}s");
            if (failed > 0)
                report.AppendLine($"  Failed: {failed} — check StingTools.log for details");

            TaskDialog td = new TaskDialog("STING Master Setup");
            td.MainInstruction = $"Master Setup: {passed}/{stepNum} steps complete";
            string reportText = report.ToString();
            if (reportText.Length > 1500)
            {
                td.MainContent = reportText.Substring(0, 1500) + "\n…(see expanded)";
                td.ExpandedContent = reportText;
            }
            else
            {
                td.MainContent = reportText;
            }
            td.Show();

            StingLog.Info($"Master Setup complete: {passed}/{stepNum} passed, " +
                $"elapsed={totalSw.Elapsed.TotalSeconds:F1}s");

            return passed > 0 ? Result.Succeeded : Result.Failed;
        }

        /// <summary>Execute an IExternalCommand, preferring IPanelCommand path when available.</summary>
        private static Result RunCommand(IExternalCommand cmd)
        {
            // Prefer IPanelCommand — gets real UIApplication, no null commandData
            if (cmd is Core.IPanelCommand panelCmd)
                return panelCmd.Execute(UI.StingCommandHandler.CurrentApp);

            string msg = "";
            var elems = new ElementSet();
            return cmd.Execute(null, ref msg, elems);
        }

        private static int RunStep(ref int stepNum, StringBuilder report,
            string label, Func<Result> action)
        {
            stepNum++;

            // Check for user cancellation between steps
            if (EscapeChecker.IsEscapePressed())
            {
                report.AppendLine($"  {stepNum,2}. {label} — CANCELLED (Escape pressed)");
                StingLog.Info($"Master Setup step {stepNum}: {label} — skipped (user cancelled)");
                return -1; // Signal cancellation
            }

            var sw = Stopwatch.StartNew();
            try
            {
                Result result = action();
                sw.Stop();
                string status = result == Result.Succeeded ? "OK" : "WARN";
                report.AppendLine($"  {stepNum,2}. {label} — {status} ({sw.Elapsed.TotalSeconds:F1}s)");
                StingLog.Info($"Master Setup step {stepNum}: {label} — {status} ({sw.Elapsed.TotalSeconds:F1}s)");
                return result == Result.Succeeded ? 1 : 0;
            }
            catch (Exception ex)
            {
                sw.Stop();
                report.AppendLine($"  {stepNum,2}. {label} — FAILED: {ex.Message}");
                StingLog.Error($"Master Setup step {stepNum}: {label}", ex);
                return 0;
            }
        }
    }
}
