using System;
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
    ///   1. Load shared parameters (Pass 1 + Pass 2)
    ///   2. Create BLE materials (815 building elements)
    ///   3. Create MEP materials (464 MEP elements)
    ///   4. Create wall/floor/ceiling/roof types from CSV
    ///   5. Create duct/pipe types from CSV
    ///   6. Batch create schedules (168 definitions)
    ///   7. Auto-populate tag tokens (DISC, PROD, SYS, FUNC, LVL)
    ///   8. Create view filters (6 discipline filters)
    ///   9. Create worksets (27 standard worksets)
    ///  10. Create view templates (7 discipline templates)
    ///
    /// Each step reports its result. Failed steps do not block subsequent steps.
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
                "  7.  Auto-populate tag tokens\n" +
                "  8.  Create view filters\n" +
                "  9.  Create worksets\n" +
                " 10.  Create view templates\n\n" +
                "This may take several minutes for a new project.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            StingLog.Info("Master Setup: starting full automation workflow");
            var report = new StringBuilder();
            report.AppendLine("STING Master Setup Results");
            report.AppendLine(new string('═', 45));

            int stepNum = 0;
            int passed = 0;
            int failed = 0;

            // Step 1: Load shared parameters
            passed += RunStep(ref stepNum, report, "Load Shared Parameters",
                () => new Tags.LoadSharedParamsCommand().Execute(commandData, ref message, elements));

            // Step 2: Create BLE Materials
            passed += RunStep(ref stepNum, report, "Create BLE Materials",
                () => new CreateBLEMaterialsCommand().Execute(commandData, ref message, elements));

            // Step 3: Create MEP Materials
            passed += RunStep(ref stepNum, report, "Create MEP Materials",
                () => new CreateMEPMaterialsCommand().Execute(commandData, ref message, elements));

            // Step 4: Create compound types (Walls, Floors, Ceilings, Roofs)
            passed += RunStep(ref stepNum, report, "Create Wall Types",
                () => new CreateWallsCommand().Execute(commandData, ref message, elements));
            passed += RunStep(ref stepNum, report, "Create Floor Types",
                () => new CreateFloorsCommand().Execute(commandData, ref message, elements));
            passed += RunStep(ref stepNum, report, "Create Ceiling Types",
                () => new CreateCeilingsCommand().Execute(commandData, ref message, elements));
            passed += RunStep(ref stepNum, report, "Create Roof Types",
                () => new CreateRoofsCommand().Execute(commandData, ref message, elements));

            // Step 5: Create MEP types (Ducts, Pipes)
            passed += RunStep(ref stepNum, report, "Create Duct Types",
                () => new CreateDuctsCommand().Execute(commandData, ref message, elements));
            passed += RunStep(ref stepNum, report, "Create Pipe Types",
                () => new CreatePipesCommand().Execute(commandData, ref message, elements));

            // Step 6: Batch create schedules
            passed += RunStep(ref stepNum, report, "Batch Create Schedules",
                () => new BatchSchedulesCommand().Execute(commandData, ref message, elements));

            // Step 7: Auto-populate tag tokens
            passed += RunStep(ref stepNum, report, "Auto-Populate Tags",
                () => new AutoPopulateCommand().Execute(commandData, ref message, elements));

            // Step 8: Create view filters
            passed += RunStep(ref stepNum, report, "Create View Filters",
                () => new CreateFiltersCommand().Execute(commandData, ref message, elements));

            // Step 9: Create worksets (only if worksharing enabled)
            Document doc = commandData.Application.ActiveUIDocument.Document;
            if (doc.IsWorkshared)
            {
                passed += RunStep(ref stepNum, report, "Create Worksets",
                    () => new CreateWorksetsCommand().Execute(commandData, ref message, elements));
            }
            else
            {
                stepNum++;
                report.AppendLine($"  {stepNum,2}. Create Worksets — SKIPPED (not workshared)");
            }

            // Step 10: Create view templates
            passed += RunStep(ref stepNum, report, "Create View Templates",
                () => new ViewTemplatesCommand().Execute(commandData, ref message, elements));

            failed = stepNum - passed;

            report.AppendLine(new string('─', 45));
            report.AppendLine($"  Complete: {passed}/{stepNum} steps succeeded");
            if (failed > 0)
                report.AppendLine($"  Failed: {failed} — check StingTools.log for details");

            TaskDialog td = new TaskDialog("STING Master Setup");
            td.MainInstruction = $"Master Setup: {passed}/{stepNum} steps complete";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"Master Setup complete: {passed}/{stepNum} passed");

            return passed > 0 ? Result.Succeeded : Result.Failed;
        }

        private static int RunStep(ref int stepNum, StringBuilder report,
            string label, Func<Result> action)
        {
            stepNum++;
            try
            {
                Result result = action();
                string status = result == Result.Succeeded ? "OK" : "WARN";
                report.AppendLine($"  {stepNum,2}. {label} — {status}");
                StingLog.Info($"Master Setup step {stepNum}: {label} — {status}");
                return result == Result.Succeeded ? 1 : 0;
            }
            catch (Exception ex)
            {
                report.AppendLine($"  {stepNum,2}. {label} — FAILED: {ex.Message}");
                StingLog.Error($"Master Setup step {stepNum}: {label}", ex);
                return 0;
            }
        }
    }
}
