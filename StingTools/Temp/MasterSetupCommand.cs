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
    public class MasterSetupCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
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

            Document doc = commandData.Application.ActiveUIDocument.Document;
            StingLog.Info("Master Setup: starting full automation workflow");
            var report = new StringBuilder();
            report.AppendLine("STING Master Setup Results");
            report.AppendLine(new string('═', 45));

            int stepNum = 0;
            int passed = 0;
            int failed = 0;
            var totalSw = Stopwatch.StartNew();

            using (TransactionGroup tg = new TransactionGroup(doc, "STING Master Setup"))
            {
                tg.Start();

                // Step 1: Load shared parameters (critical — other steps depend on it)
                passed += RunStep(ref stepNum, report, "Load Shared Parameters",
                    () => RunCommand(new Tags.LoadSharedParamsCommand(), commandData, elements));

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

                // Step 2: Create BLE Materials
                passed += RunStep(ref stepNum, report, "Create BLE Materials",
                    () => RunCommand(new CreateBLEMaterialsCommand(), commandData, elements));

                // Step 3: Create MEP Materials
                passed += RunStep(ref stepNum, report, "Create MEP Materials",
                    () => RunCommand(new CreateMEPMaterialsCommand(), commandData, elements));

                // Step 4: Create compound types (Walls, Floors, Ceilings, Roofs)
                passed += RunStep(ref stepNum, report, "Create Wall Types",
                    () => RunCommand(new CreateWallsCommand(), commandData, elements));
                passed += RunStep(ref stepNum, report, "Create Floor Types",
                    () => RunCommand(new CreateFloorsCommand(), commandData, elements));
                passed += RunStep(ref stepNum, report, "Create Ceiling Types",
                    () => RunCommand(new CreateCeilingsCommand(), commandData, elements));
                passed += RunStep(ref stepNum, report, "Create Roof Types",
                    () => RunCommand(new CreateRoofsCommand(), commandData, elements));

                // Step 5: Create MEP types (Ducts, Pipes)
                passed += RunStep(ref stepNum, report, "Create Duct Types",
                    () => RunCommand(new CreateDuctsCommand(), commandData, elements));
                passed += RunStep(ref stepNum, report, "Create Pipe Types",
                    () => RunCommand(new CreatePipesCommand(), commandData, elements));

                // Step 6: Batch create schedules
                passed += RunStep(ref stepNum, report, "Batch Create Schedules",
                    () => RunCommand(new BatchSchedulesCommand(), commandData, elements));

                // Step 7: Evaluate formulas (199 dependency-ordered formulas)
                passed += RunStep(ref stepNum, report, "Evaluate Formulas (199 definitions)",
                    () => RunCommand(new FormulaEvaluatorCommand(), commandData, elements));

                // Step 8: Tag & Combine (full pipeline: populate + tag + combine + TAG7 narrative)
                passed += RunStep(ref stepNum, report, "Tag & Combine (full pipeline + TAG7)",
                    () => RunCommand(new Tags.TagAndCombineCommand(), commandData, elements));

                // Step 9: Create view filters
                passed += RunStep(ref stepNum, report, "Create View Filters",
                    () => RunCommand(new CreateFiltersCommand(), commandData, elements));

                // Step 10: Create worksets (only if worksharing enabled)
                if (doc.IsWorkshared)
                {
                    passed += RunStep(ref stepNum, report, "Create Worksets",
                        () => RunCommand(new CreateWorksetsCommand(), commandData, elements));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Worksets — SKIPPED (not workshared)");
                }

                // Step 11: Create view templates
                passed += RunStep(ref stepNum, report, "Create View Templates",
                    () => RunCommand(new ViewTemplatesCommand(), commandData, elements));

                // Step 12: Fill patterns + line styles + object styles
                passed += RunStep(ref stepNum, report, "Create Fill Patterns",
                    () => RunCommand(new CreateFillPatternsCommand(), commandData, elements));
                passed += RunStep(ref stepNum, report, "Create Line Styles",
                    () => RunCommand(new CreateLineStylesCommand(), commandData, elements));
                passed += RunStep(ref stepNum, report, "Configure Object Styles",
                    () => RunCommand(new CreateObjectStylesCommand(), commandData, elements));

                // Step 13: Text styles + dimension styles
                passed += RunStep(ref stepNum, report, "Create Text Styles",
                    () => RunCommand(new CreateTextStylesCommand(), commandData, elements));
                passed += RunStep(ref stepNum, report, "Create Dimension Styles",
                    () => RunCommand(new CreateDimensionStylesCommand(), commandData, elements));

                // Step 14: Apply filters to templates + VG overrides
                passed += RunStep(ref stepNum, report, "Apply Filters to Templates",
                    () => RunCommand(new ApplyFiltersToViewsCommand(), commandData, elements));
                passed += RunStep(ref stepNum, report, "Apply VG Overrides (5-layer)",
                    () => RunCommand(new CreateVGOverridesCommand(), commandData, elements));

                // Step 15: Batch family parameters from CSV
                passed += RunStep(ref stepNum, report, "Batch Family Params (CSV-driven)",
                    () => RunCommand(new BatchAddFamilyParamsCommand(), commandData, elements));

                // Step 16: Auto-assign templates + auto-fix
                passed += RunStep(ref stepNum, report, "Auto-Assign Templates (5-layer)",
                    () => RunCommand(new AutoAssignTemplatesCommand(), commandData, elements));
                passed += RunStep(ref stepNum, report, "Auto-Fix Template Health",
                    () => RunCommand(new AutoFixTemplateCommand(), commandData, elements));

                // Step 17: Auto-create legends (discipline, system, filter)
                passed += RunStep(ref stepNum, report, "Auto-Create Legends (discipline + system)",
                    () => RunCommand(new Tags.AutoCreateLegendsCommand(), commandData, elements));

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
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"Master Setup complete: {passed}/{stepNum} passed, " +
                $"elapsed={totalSw.Elapsed.TotalSeconds:F1}s");

            return passed > 0 ? Result.Succeeded : Result.Failed;
        }

        /// <summary>Execute an IExternalCommand without capturing ref message in a lambda.</summary>
        private static Result RunCommand(IExternalCommand cmd,
            ExternalCommandData data, ElementSet elems)
        {
            string msg = "";
            return cmd.Execute(data, ref msg, elems);
        }

        private static int RunStep(ref int stepNum, StringBuilder report,
            string label, Func<Result> action)
        {
            stepNum++;
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
