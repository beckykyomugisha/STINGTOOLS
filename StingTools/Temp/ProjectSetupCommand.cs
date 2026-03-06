using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Temp
{
    /// <summary>
    /// Project Setup Wizard command — launches a 7-page WPF dialog that collects
    /// all project setup information including discipline-specific configuration,
    /// then executes a comprehensive automation pipeline in a single
    /// TransactionGroup for atomic rollback.
    ///
    /// Execution order (dependency-aware):
    ///   Phase 0: Pre-flight (collect data via WPF wizard)
    ///   Phase 1: Foundation (project info → levels → grids → worksharing)
    ///   Phase 2: Infrastructure (params → materials → types → schedules)
    ///   Phase 3: Standards (styles → filters → templates → VG overrides)
    ///   Phase 4: Documentation (views → dependents → sheets → sections → elevations)
    ///   Phase 5: Intelligence (auto-assign → auto-fix → starting view)
    ///
    /// Each step uses existing IExternalCommand classes where available.
    /// New capabilities: Level.Create, Grid.Create, ProjectInformation, worksharing.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ProjectSetupCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uidoc = uiApp.ActiveUIDocument;
            Document doc = uidoc.Document;

            // ── Phase 0: Pre-fetch Revit data and launch wizard ──────

            // Collect title block names
            var titleBlocks = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .Select(fs => $"{fs.FamilyName} : {fs.Name}")
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            // Launch WPF wizard
            var wizard = new ProjectSetupWizard();

            // Set Revit main window as owner for proper modal behavior
            try
            {
                IntPtr revitHandle = uiApp.MainWindowHandle;
                var helper = new WindowInteropHelper(wizard) { Owner = revitHandle };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ProjectSetup: could not set window owner: {ex.Message}");
            }

            wizard.PrePopulate(doc, titleBlocks);

            bool? result = wizard.ShowDialog();
            if (result != true || !wizard.RunRequested || wizard.SetupData == null)
                return Result.Cancelled;

            ProjectSetupData data = wizard.SetupData;

            // ── Execute automation pipeline ──────────────────────────

            StingLog.Info("Project Setup Wizard: starting comprehensive automation");
            var report = new StringBuilder();
            report.AppendLine("STING Project Setup Wizard Results");
            report.AppendLine(new string('═', 55));

            int stepNum = 0;
            int passed = 0;
            int failed = 0;
            var totalSw = Stopwatch.StartNew();

            using (TransactionGroup tg = new TransactionGroup(doc, "STING Project Setup"))
            {
                tg.Start();

                // ════════════════════════════════════════════════════
                // PHASE 1: FOUNDATION
                // ════════════════════════════════════════════════════
                report.AppendLine("\n── Phase 1: Foundation ──");

                // Step: Set Display Units
                passed += RunStep(ref stepNum, report,
                    $"Set Display Units ({data.UnitSystem})",
                    () => SetProjectUnits(doc, data.UnitSystem));

                // Step: Set Project Information
                passed += RunStep(ref stepNum, report, "Set Project Information",
                    () => SetProjectInformation(doc, data));

                // Step: Create/Update Levels
                passed += RunStep(ref stepNum, report,
                    $"Create Levels ({data.Levels.Count} definitions)",
                    () => CreateLevels(doc, data.Levels));

                // Step: Create Grids
                if (data.CreateGrids && (data.GridHCount > 0 || data.GridVCount > 0))
                {
                    int gridTotal = data.GridHCount + data.GridVCount;
                    passed += RunStep(ref stepNum, report,
                        $"Create Grids ({gridTotal} lines)",
                        () => CreateGrids(doc, data));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Grids — SKIPPED (not selected)");
                }

                // Step: Set True North
                if (data.TrueNorthAngle != 0)
                {
                    passed += RunStep(ref stepNum, report,
                        $"Set True North ({data.TrueNorthAngle:F1}°)",
                        () => SetTrueNorth(doc, data.TrueNorthAngle));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Set True North — SKIPPED (0°)");
                }

                // Step: Enable Worksharing
                if (data.EnableWorksharing && !doc.IsWorkshared)
                {
                    passed += RunStep(ref stepNum, report, "Enable Worksharing",
                        () => EnableWorksharing(doc));
                }
                else if (data.EnableWorksharing && doc.IsWorkshared)
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Enable Worksharing — SKIPPED (already workshared)");
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Enable Worksharing — SKIPPED (not selected)");
                }

                // Critical check: if levels failed, offer rollback
                if (passed < 2)
                {
                    StingLog.Warn("Project Setup: foundation phase had failures");
                    TaskDialog critDlg = new TaskDialog("Project Setup — Warning");
                    critDlg.MainInstruction = "Foundation steps had issues";
                    critDlg.MainContent =
                        "Some foundation steps (project info / levels) did not succeed.\n" +
                        "Continue with remaining automation or rollback?";
                    critDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        "Continue anyway", "Proceed with remaining steps");
                    critDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        "Rollback all", "Undo all changes");
                    if (critDlg.Show() == TaskDialogResult.CommandLink2)
                    {
                        tg.RollBack();
                        TaskDialog.Show("Project Setup", "All changes rolled back.");
                        return Result.Cancelled;
                    }
                }

                // ════════════════════════════════════════════════════
                // PHASE 2: INFRASTRUCTURE
                // ════════════════════════════════════════════════════
                report.AppendLine("\n── Phase 2: Infrastructure ──");

                // Step: Load Shared Parameters (critical for all subsequent tagging)
                if (data.LoadParams)
                {
                    passed += RunStep(ref stepNum, report, "Load Shared Parameters (200+)",
                        () => RunCommand(new Tags.LoadSharedParamsCommand(), commandData, elements));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Load Shared Parameters — SKIPPED");
                }

                // Step: Create BLE + MEP Materials
                if (data.CreateMaterials)
                {
                    passed += RunStep(ref stepNum, report, "Create BLE Materials (815)",
                        () => RunCommand(new CreateBLEMaterialsCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Create MEP Materials (464)",
                        () => RunCommand(new CreateMEPMaterialsCommand(), commandData, elements));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Materials — SKIPPED");
                }

                // Step: Create Family Types (walls, floors, ceilings, roofs, ducts, pipes)
                if (data.CreateFamilyTypes)
                {
                    passed += RunStep(ref stepNum, report, "Create Wall Types",
                        () => RunCommand(new CreateWallsCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Create Floor Types",
                        () => RunCommand(new CreateFloorsCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Create Ceiling Types",
                        () => RunCommand(new CreateCeilingsCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Create Roof Types",
                        () => RunCommand(new CreateRoofsCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Create Duct Types",
                        () => RunCommand(new CreateDuctsCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Create Pipe Types",
                        () => RunCommand(new CreatePipesCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Create Cable Tray Types",
                        () => RunCommand(new CreateCableTraysCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Create Conduit Types",
                        () => RunCommand(new CreateConduitsCommand(), commandData, elements));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Family Types — SKIPPED");
                }

                // Step: Batch Create Schedules
                if (data.CreateSchedules)
                {
                    passed += RunStep(ref stepNum, report, "Batch Create Schedules (168)",
                        () => RunCommand(new BatchSchedulesCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Create Template Schedules (13)",
                        () => RunCommand(new CreateTemplateSchedulesCommand(), commandData, elements));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Schedules — SKIPPED");
                }

                // ════════════════════════════════════════════════════
                // PHASE 3: STANDARDS
                // ════════════════════════════════════════════════════
                report.AppendLine("\n── Phase 3: Standards ──");

                // Step: Create Styles (fill patterns, line patterns, line styles, text/dim styles)
                if (data.CreateStyles)
                {
                    passed += RunStep(ref stepNum, report, "Create Fill Patterns",
                        () => RunCommand(new CreateFillPatternsCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Create Line Patterns (10 ISO 128)",
                        () => RunCommand(new CreateLinePatternsCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Create Line Styles",
                        () => RunCommand(new CreateLineStylesCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Create Object Styles",
                        () => RunCommand(new CreateObjectStylesCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Create Text Styles",
                        () => RunCommand(new CreateTextStylesCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Create Dimension Styles",
                        () => RunCommand(new CreateDimensionStylesCommand(), commandData, elements));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Styles — SKIPPED");
                }

                // Step: Create View Filters
                if (data.CreateFilters)
                {
                    passed += RunStep(ref stepNum, report, "Create View Filters (28+)",
                        () => RunCommand(new CreateFiltersCommand(), commandData, elements));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Filters — SKIPPED");
                }

                // Step: Create View Templates
                if (data.CreateTemplates)
                {
                    passed += RunStep(ref stepNum, report, "Create View Templates (23)",
                        () => RunCommand(new ViewTemplatesCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Apply Filters to Templates",
                        () => RunCommand(new ApplyFiltersToViewsCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Apply VG Overrides (5-layer)",
                        () => RunCommand(new CreateVGOverridesCommand(), commandData, elements));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Templates — SKIPPED");
                }

                // Step: Create Phases (report only — API limitation)
                if (data.CreatePhases)
                {
                    passed += RunStep(ref stepNum, report, "Create Phases (audit only — API limitation)",
                        () => RunCommand(new CreatePhasesCommand(), commandData, elements));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Phases — SKIPPED");
                }

                // Step: Create Worksets
                if (data.EnableWorksharing && doc.IsWorkshared)
                {
                    passed += RunStep(ref stepNum, report, "Create Worksets (35 ISO 19650)",
                        () => RunCommand(new CreateWorksetsCommand(), commandData, elements));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Worksets — SKIPPED (not workshared)");
                }

                // ════════════════════════════════════════════════════
                // PHASE 4: DOCUMENTATION
                // ════════════════════════════════════════════════════
                report.AppendLine("\n── Phase 4: Documentation ──");

                // Step: Create Views (plans + RCPs per level per discipline)
                if (data.CreateViews)
                {
                    passed += RunStep(ref stepNum, report,
                        $"Create Views ({data.Disciplines.Count} disciplines x {data.Levels.Count} levels)",
                        () => CreateDisciplineViews(doc, data));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Views — SKIPPED");
                }

                // Step: Create Dependent Views
                if (data.CreateDependents)
                {
                    passed += RunStep(ref stepNum, report, "Create Dependent Views",
                        () => RunCommand(new Docs.CreateDependentViewsCommand(), commandData, elements));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Dependents — SKIPPED");
                }

                // Step: Create Sheets
                if (data.CreateSheets)
                {
                    passed += RunStep(ref stepNum, report, "Create Sheets",
                        () => RunCommand(new Docs.BatchCreateSheetsCommand(), commandData, elements));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Sheets — SKIPPED");
                }

                // Step: Create Sections from Grids
                if (data.CreateSections)
                {
                    passed += RunStep(ref stepNum, report, "Create Building Sections",
                        () => RunCommand(new Docs.BatchCreateSectionsCommand(), commandData, elements));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Sections — SKIPPED");
                }

                // Step: Create Elevations
                if (data.CreateElevations)
                {
                    passed += RunStep(ref stepNum, report, "Create 4 Exterior Elevations",
                        () => RunCommand(new Docs.BatchCreateElevationsCommand(), commandData, elements));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Elevations — SKIPPED");
                }

                // Step: Organize project browser and create sheet index
                if (data.CreateViews || data.CreateSheets)
                {
                    passed += RunStep(ref stepNum, report, "Organize Project Browser",
                        () => RunCommand(new Docs.ProjectBrowserOrganizerCommand(), commandData, elements));
                    if (data.CreateSheets)
                    {
                        passed += RunStep(ref stepNum, report, "Create Sheet Index Schedule",
                            () => RunCommand(new Docs.SheetIndexCommand(), commandData, elements));
                    }
                }

                // ════════════════════════════════════════════════════
                // PHASE 5: INTELLIGENCE
                // ════════════════════════════════════════════════════
                report.AppendLine("\n── Phase 5: Intelligence ──");

                // Auto-assign templates to all views
                if (data.CreateTemplates)
                {
                    passed += RunStep(ref stepNum, report, "Auto-Assign Templates (5-layer)",
                        () => RunCommand(new AutoAssignTemplatesCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Auto-Fix Template Health",
                        () => RunCommand(new AutoFixTemplateCommand(), commandData, elements));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Auto-Assign Templates — SKIPPED");
                }

                // Full auto-populate: tokens + dimensions + MEP + formulas + tags + combine
                if (data.LoadParams)
                {
                    passed += RunStep(ref stepNum, report, "Full Auto-Populate (tokens+dims+MEP+formulas+tags)",
                        () => RunCommand(new FullAutoPopulateCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Batch Family Parameters (CSV)",
                        () => RunCommand(new BatchAddFamilyParamsCommand(), commandData, elements));
                }

                // Set starting view
                passed += RunStep(ref stepNum, report, "Set Starting View",
                    () => SetStartingView(doc));

                // ── Finalize ─────────────────────────────────────────

                failed = stepNum - passed;
                totalSw.Stop();

                if (failed > 0)
                {
                    report.AppendLine(new string('─', 55));
                    report.AppendLine($"  {passed}/{stepNum} succeeded, {failed} failed");
                    report.AppendLine($"  Duration: {totalSw.Elapsed.TotalSeconds:F1}s");

                    TaskDialog rollbackDlg = new TaskDialog("Project Setup — Failures Detected");
                    rollbackDlg.MainInstruction = $"{failed} step(s) had issues";
                    rollbackDlg.MainContent = report.ToString() +
                        "\n\nKeep results or rollback all changes?";
                    rollbackDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        "Keep results", $"Commit {passed} successful steps");
                    rollbackDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        "Rollback all", "Undo everything");

                    if (rollbackDlg.Show() == TaskDialogResult.CommandLink2)
                    {
                        tg.RollBack();
                        StingLog.Info("Project Setup: user chose rollback after failures");
                        TaskDialog.Show("Project Setup", "All changes rolled back.");
                        return Result.Cancelled;
                    }
                }

                tg.Assimilate();
            }

            // GAP-006: Persist wizard settings to project_config.json
            // Update TagConfig with wizard LOC/ZONE codes before saving
            if (data.LocCodes.Count > 0)
                TagConfig.LocCodes = data.LocCodes;
            if (data.ZoneCodes.Count > 0)
                TagConfig.ZoneCodes = data.ZoneCodes;

            string configPath = Path.Combine(StingToolsApp.DataPath ?? "", "project_config.json");

            // UX-02: Warn before overwriting existing project_config.json
            if (File.Exists(configPath))
            {
                TaskDialog overwriteDlg = new TaskDialog("Overwrite Configuration?");
                overwriteDlg.MainInstruction = "project_config.json already exists";
                overwriteDlg.MainContent = $"Path: {configPath}\n\n" +
                    "Overwriting will replace existing LOC codes, ZONE codes, and discipline settings.\n" +
                    "The previous file will be backed up with a .bak extension.";
                overwriteDlg.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
                if (overwriteDlg.Show() == TaskDialogResult.Cancel)
                {
                    report.AppendLine("\n  Config save skipped (user cancelled overwrite)");
                }
                else
                {
                    // Create backup before overwriting
                    try { File.Copy(configPath, configPath + ".bak", true); }
                    catch (Exception bex) { StingLog.Warn($"Config backup failed: {bex.Message}"); }
                }
            }

            if (TagConfig.SaveToFile(configPath))
            {
                // ENH-002: Immediately reload settings so TagConfig uses persisted values
                TagConfig.LoadFromFile(configPath);
                report.AppendLine($"\n  Settings saved and reloaded from project_config.json");
                StingLog.Info($"Project Setup: config persisted and reloaded from {configPath}");
            }

            report.AppendLine(new string('═', 55));
            report.AppendLine($"  Complete: {passed}/{stepNum} steps succeeded");
            report.AppendLine($"  Duration: {totalSw.Elapsed.TotalSeconds:F1}s");
            if (failed > 0)
                report.AppendLine($"  Issues: {failed} — check StingTools.log for details");

            TaskDialog td = new TaskDialog("STING Project Setup");
            td.MainInstruction = $"Project Setup: {passed}/{stepNum} steps complete";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"Project Setup complete: {passed}/{stepNum} passed, " +
                $"elapsed={totalSw.Elapsed.TotalSeconds:F1}s");

            return passed > 0 ? Result.Succeeded : Result.Failed;
        }

        // ══════════════════════════════════════════════════════════════
        // Phase 1 implementation: Foundation
        // ══════════════════════════════════════════════════════════════

        /// <summary>Set display units for the project (Millimeters, Meters, or Imperial).</summary>
        private static Result SetProjectUnits(Document doc, string unitSystem)
        {
            try
            {
                using (Transaction tx = new Transaction(doc, "STING Set Project Units"))
                {
                    tx.Start();
                    Units units = doc.GetUnits();

                    switch (unitSystem)
                    {
                        case "Meters":
                            SetFormatOption(units, SpecTypeId.Length, UnitTypeId.Meters, 0.001);
                            SetFormatOption(units, SpecTypeId.Area, UnitTypeId.SquareMeters, 0.01);
                            SetFormatOption(units, SpecTypeId.Volume, UnitTypeId.CubicMeters, 0.001);
                            break;
                        case "Imperial":
                            SetFormatOption(units, SpecTypeId.Length, UnitTypeId.FeetFractionalInches, 0.0);
                            SetFormatOption(units, SpecTypeId.Area, UnitTypeId.SquareFeet, 0.01);
                            SetFormatOption(units, SpecTypeId.Volume, UnitTypeId.CubicFeet, 0.01);
                            break;
                        default: // Millimeters
                            SetFormatOption(units, SpecTypeId.Length, UnitTypeId.Millimeters, 1);
                            SetFormatOption(units, SpecTypeId.Area, UnitTypeId.SquareMeters, 0.01);
                            SetFormatOption(units, SpecTypeId.Volume, UnitTypeId.CubicMeters, 0.001);
                            break;
                    }

                    doc.SetUnits(units);
                    tx.Commit();
                }

                StingLog.Info($"Project units set to: {unitSystem}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error($"SetProjectUnits failed for '{unitSystem}'", ex);
                return Result.Failed;
            }
        }

        private static void SetFormatOption(Units units, ForgeTypeId specType,
            ForgeTypeId unitType, double accuracy)
        {
            try
            {
                FormatOptions fo = new FormatOptions(unitType);
                if (accuracy > 0)
                    fo.Accuracy = accuracy;
                units.SetFormatOptions(specType, fo);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SetFormatOption failed for {specType}: {ex.Message}");
            }
        }

        /// <summary>Set the angle from Project North to True North.</summary>
        private static Result SetTrueNorth(Document doc, double angleDegrees)
        {
            try
            {
                using (Transaction tx = new Transaction(doc, "STING Set True North"))
                {
                    tx.Start();
                    ProjectLocation pl = doc.ActiveProjectLocation;
                    ProjectPosition currentPos = pl.GetProjectPosition(XYZ.Zero);
                    double angleRadians = angleDegrees * Math.PI / 180.0;
                    ProjectPosition newPos = new ProjectPosition(
                        currentPos.EastWest, currentPos.NorthSouth,
                        currentPos.Elevation, angleRadians);
                    pl.SetProjectPosition(XYZ.Zero, newPos);
                    tx.Commit();
                }
                StingLog.Info($"True North set to {angleDegrees:F1}°");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SetTrueNorth failed", ex);
                return Result.Failed;
            }
        }

        /// <summary>Set Revit Project Information from wizard data.</summary>
        private static Result SetProjectInformation(Document doc, ProjectSetupData data)
        {
            try
            {
                using (Transaction tx = new Transaction(doc, "STING Set Project Information"))
                {
                    tx.Start();
                    ProjectInfo pi = doc.ProjectInformation;

                    if (!string.IsNullOrEmpty(data.ProjectName))
                        pi.Name = data.ProjectName;
                    if (!string.IsNullOrEmpty(data.ProjectNumber))
                        pi.Number = data.ProjectNumber;
                    if (!string.IsNullOrEmpty(data.ClientName))
                        pi.ClientName = data.ClientName;
                    if (!string.IsNullOrEmpty(data.Organisation))
                        pi.OrganizationName = data.Organisation;
                    if (!string.IsNullOrEmpty(data.Author))
                        pi.Author = data.Author;
                    if (!string.IsNullOrEmpty(data.BuildingName))
                        pi.BuildingName = data.BuildingName;
                    if (!string.IsNullOrEmpty(data.Address))
                        pi.Address = data.Address;
                    if (!string.IsNullOrEmpty(data.Status))
                        pi.Status = data.Status;

                    // Write LOC/ZONE codes to Project Information shared params (if bound)
                    if (data.LocCodes.Count > 0)
                    {
                        string locStr = string.Join(",", data.LocCodes);
                        try
                        {
                            Parameter locParam = pi.LookupParameter(ParamRegistry.LOC);
                            if (locParam != null && !locParam.IsReadOnly)
                                locParam.Set(locStr);
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Could not set LOC on ProjectInfo: {ex.Message}");
                        }
                    }
                    if (data.ZoneCodes.Count > 0)
                    {
                        string zoneStr = string.Join(",", data.ZoneCodes);
                        try
                        {
                            Parameter zoneParam = pi.LookupParameter(ParamRegistry.ZONE);
                            if (zoneParam != null && !zoneParam.IsReadOnly)
                                zoneParam.Set(zoneStr);
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Could not set ZONE on ProjectInfo: {ex.Message}");
                        }
                    }

                    // Write discipline-specific design data to Project Information params
                    WriteDisciplineDesignParams(pi, data);

                    tx.Commit();
                }

                StingLog.Info($"Project Info set: '{data.ProjectName}' #{data.ProjectNumber}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SetProjectInformation failed", ex);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Write discipline-specific design parameters to Project Information.
        /// These are stored as STING shared parameters for downstream use by
        /// tagging, scheduling, and template intelligence.
        /// </summary>
        private static void WriteDisciplineDesignParams(ProjectInfo pi, ProjectSetupData data)
        {
            // Store active disciplines as comma-separated string
            try
            {
                Parameter discParam = pi.LookupParameter("ASS_DISCIPLINES_TXT");
                if (discParam != null && !discParam.IsReadOnly)
                    discParam.Set(string.Join(",", data.Disciplines));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Could not set ASS_DISCIPLINES_TXT: {ex.Message}");
            }

            // Store active system codes for tag intelligence
            try
            {
                Parameter sysParam = pi.LookupParameter("ASS_SYSTEMS_TXT");
                if (sysParam != null && !sysParam.IsReadOnly)
                    sysParam.Set(string.Join(",", data.GetActiveSystemCodes()));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Could not set ASS_SYSTEMS_TXT: {ex.Message}");
            }

            // Store fire rating for structural/architectural intelligence
            if (data.Disciplines.Contains("FP") && !string.IsNullOrEmpty(data.FireConfig.FireRating))
            {
                try
                {
                    Parameter frParam = pi.LookupParameter("FIRE_RATING");
                    if (frParam != null && !frParam.IsReadOnly)
                        frParam.Set(data.FireConfig.FireRating);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Could not set FIRE_RATING: {ex.Message}");
                }
            }

            // Store electrical voltage/phase for panel schedules
            if (data.Disciplines.Contains("E"))
            {
                try
                {
                    Parameter vParam = pi.LookupParameter("ELC_VOLTAGE");
                    if (vParam != null && !vParam.IsReadOnly)
                    {
                        string v = data.ElecConfig.Voltage;
                        // Extract numeric voltage
                        string numV = new string(v.TakeWhile(c => char.IsDigit(c)).ToArray());
                        if (!string.IsNullOrEmpty(numV))
                            vParam.Set(numV);
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Could not set ELC_VOLTAGE: {ex.Message}");
                }
            }
        }

        /// <summary>Create building levels from wizard definitions. Preserves existing levels.</summary>
        private static Result CreateLevels(Document doc, List<LevelDefinition> definitions)
        {
            if (definitions.Count == 0)
                return Result.Failed;

            // Build index of existing levels by name and elevation
            var existingByName = new Dictionary<string, Level>(StringComparer.OrdinalIgnoreCase);
            var existingByElev = new Dictionary<double, Level>();

            foreach (Level lv in new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>())
            {
                existingByName[lv.Name] = lv;
                double elevM = Math.Round(
                    UnitUtils.ConvertFromInternalUnits(lv.Elevation, UnitTypeId.Meters), 2);
                if (!existingByElev.ContainsKey(elevM))
                    existingByElev[elevM] = lv;
            }

            int created = 0;
            int renamed = 0;
            int skipped = 0;

            using (Transaction tx = new Transaction(doc, "STING Create Levels"))
            {
                tx.Start();

                foreach (var def in definitions)
                {
                    double elevFeet = UnitUtils.ConvertToInternalUnits(
                        def.ElevationMeters, UnitTypeId.Meters);
                    double roundedM = Math.Round(def.ElevationMeters, 2);

                    // Check if level already exists at this name
                    if (existingByName.TryGetValue(def.Name, out Level existing))
                    {
                        skipped++;
                        continue;
                    }

                    // Check if level exists at same elevation but different name
                    if (existingByElev.TryGetValue(roundedM, out Level atElev))
                    {
                        // Rename existing level to match wizard name
                        try
                        {
                            atElev.Name = def.Name;
                            renamed++;
                            existingByName[def.Name] = atElev;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Could not rename level at {roundedM}m to '{def.Name}': {ex.Message}");
                            skipped++;
                        }
                        continue;
                    }

                    // Create new level
                    try
                    {
                        Level newLevel = Level.Create(doc, elevFeet);
                        newLevel.Name = def.Name;
                        created++;
                        existingByName[def.Name] = newLevel;
                        existingByElev[roundedM] = newLevel;
                        StingLog.Info($"Created level '{def.Name}' at {def.ElevationMeters:F2}m");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"Level creation failed '{def.Name}' at {def.ElevationMeters:F2}m", ex);
                    }
                }

                tx.Commit();
            }

            StingLog.Info($"Levels: {created} created, {renamed} renamed, {skipped} existing");
            return (created + renamed > 0 || skipped > 0) ? Result.Succeeded : Result.Failed;
        }

        /// <summary>Create structural grids from wizard data.</summary>
        private static Result CreateGrids(Document doc, ProjectSetupData data)
        {
            // Build index of existing grids
            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(Grid))
                    .Select(g => g.Name));

            int created = 0;

            // Grid length in internal units
            double hLengthFt = UnitUtils.ConvertToInternalUnits(
                data.GridHLength > 0 ? data.GridHLength : 50, UnitTypeId.Meters);
            double vLengthFt = UnitUtils.ConvertToInternalUnits(
                data.GridVLength > 0 ? data.GridVLength : 30, UnitTypeId.Meters);
            double hSpacingFt = UnitUtils.ConvertToInternalUnits(
                data.GridHSpacing > 0 ? data.GridHSpacing : 6.0, UnitTypeId.Meters);
            double vSpacingFt = UnitUtils.ConvertToInternalUnits(
                data.GridVSpacing > 0 ? data.GridVSpacing : 6.0, UnitTypeId.Meters);

            using (Transaction tx = new Transaction(doc, "STING Create Grids"))
            {
                tx.Start();

                // Horizontal grids (lettered A, B, C...) — lines running in X direction
                for (int i = 0; i < data.GridHCount; i++)
                {
                    string name = GetGridLetter(i);
                    if (existingNames.Contains(name))
                        continue;

                    double y = i * hSpacingFt;
                    XYZ start = new XYZ(-vLengthFt * 0.1, y, 0);
                    XYZ end = new XYZ(vLengthFt * 1.1, y, 0);

                    try
                    {
                        Line line = Line.CreateBound(start, end);
                        Grid grid = Grid.Create(doc, line);
                        grid.Name = name;
                        created++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Grid creation failed '{name}': {ex.Message}");
                    }
                }

                // Vertical grids (numbered 1, 2, 3...) — lines running in Y direction
                for (int i = 0; i < data.GridVCount; i++)
                {
                    string name = (i + 1).ToString();
                    if (existingNames.Contains(name))
                        continue;

                    double x = i * vSpacingFt;
                    XYZ start = new XYZ(x, -hLengthFt * 0.1, 0);
                    XYZ end = new XYZ(x, hLengthFt * 1.1, 0);

                    try
                    {
                        Line line = Line.CreateBound(start, end);
                        Grid grid = Grid.Create(doc, line);
                        grid.Name = name;
                        created++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Grid creation failed '{name}': {ex.Message}");
                    }
                }

                tx.Commit();
            }

            StingLog.Info($"Grids: {created} created");
            return created > 0 ? Result.Succeeded : Result.Failed;
        }

        /// <summary>Enable worksharing on the document.</summary>
        private static Result EnableWorksharing(Document doc)
        {
            if (doc.IsWorkshared)
                return Result.Succeeded;

            try
            {
                doc.EnableWorksharing("Shared Levels and Grids", "Workset1");
                StingLog.Info("Worksharing enabled");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("EnableWorksharing failed", ex);
                return Result.Failed;
            }
        }

        // ══════════════════════════════════════════════════════════════
        // Phase 4: Documentation — discipline-aware view creation
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Create floor plans and RCPs per level per discipline.
        /// Uses intelligent naming: "{Discipline} - {Level Name}" pattern.
        /// </summary>
        private static Result CreateDisciplineViews(Document doc, ProjectSetupData data)
        {
            // Get the default floor plan and RCP view family types
            var vfts = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .ToList();

            var floorPlanType = vfts.FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan);
            var rcpType = vfts.FirstOrDefault(v => v.ViewFamily == ViewFamily.CeilingPlan);

            if (floorPlanType == null)
            {
                StingLog.Error("No floor plan ViewFamilyType found");
                return Result.Failed;
            }

            // Get all levels
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            if (levels.Count == 0)
            {
                StingLog.Error("No levels found for view creation");
                return Result.Failed;
            }

            // Build name index for dedup
            var existingNames = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate)
                    .Select(v => v.Name),
                StringComparer.OrdinalIgnoreCase);

            // Discipline code to long name mapping (all valid STING disc codes)
            var discNames = new Dictionary<string, string>
            {
                { "A", "Architectural" }, { "S", "Structural" },
                { "M", "Mechanical" }, { "E", "Electrical" },
                { "P", "Plumbing" }, { "FP", "Fire Protection" },
                { "LV", "Low Voltage" }, { "G", "Generic" }
            };

            int created = 0;

            using (Transaction tx = new Transaction(doc, "STING Create Discipline Views"))
            {
                tx.Start();

                foreach (string disc in data.Disciplines)
                {
                    if (!discNames.TryGetValue(disc, out string discName))
                        discName = disc;

                    foreach (Level level in levels)
                    {
                        // Floor Plan
                        string planName = $"{discName} Plan - {level.Name}";
                        if (!existingNames.Contains(planName))
                        {
                            try
                            {
                                ViewPlan plan = ViewPlan.Create(doc, floorPlanType.Id, level.Id);
                                plan.Name = planName;
                                existingNames.Add(planName);
                                created++;
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"View creation failed '{planName}': {ex.Message}");
                            }
                        }

                        // Reflected Ceiling Plan (disciplines that need ceiling views)
                        if (rcpType != null &&
                            (disc == "A" || disc == "M" || disc == "E" || disc == "LV"))
                        {
                            string rcpName = $"{discName} RCP - {level.Name}";
                            if (!existingNames.Contains(rcpName))
                            {
                                try
                                {
                                    ViewPlan rcp = ViewPlan.Create(doc, rcpType.Id, level.Id);
                                    rcp.Name = rcpName;
                                    existingNames.Add(rcpName);
                                    created++;
                                }
                                catch (Exception ex)
                                {
                                    StingLog.Warn($"View creation failed '{rcpName}': {ex.Message}");
                                }
                            }
                        }
                    }
                }

                tx.Commit();
            }

            StingLog.Info($"Discipline views: {created} created");
            return created > 0 ? Result.Succeeded : Result.Failed;
        }

        // ══════════════════════════════════════════════════════════════
        // Phase 5: Intelligence
        // ══════════════════════════════════════════════════════════════

        /// <summary>Set the starting view to the first floor plan at Ground/GF level, or first available plan.</summary>
        private static Result SetStartingView(Document doc)
        {
            try
            {
                StartingViewSettings svs = StartingViewSettings.GetStartingViewSettings(doc);

                // Try to find Ground Floor or Level 00 plan
                var floorPlans = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan)
                    .OrderBy(v => v.Name)
                    .ToList();

                string[] groundKeywords = { "ground", "gf", "level 00", "l00", "00", "ground floor" };

                ViewPlan startView = floorPlans.FirstOrDefault(v =>
                    groundKeywords.Any(kw => v.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0))
                    ?? floorPlans.FirstOrDefault();

                if (startView != null)
                {
                    using (Transaction tx = new Transaction(doc, "STING Set Starting View"))
                    {
                        tx.Start();
                        svs.ViewId = startView.Id;
                        tx.Commit();
                    }
                    StingLog.Info($"Starting view set to: {startView.Name}");
                    return Result.Succeeded;
                }

                StingLog.Warn("No suitable starting view found");
                return Result.Failed;
            }
            catch (Exception ex)
            {
                StingLog.Error("SetStartingView failed", ex);
                return Result.Failed;
            }
        }

        // ══════════════════════════════════════════════════════════════
        // Helpers
        // ══════════════════════════════════════════════════════════════

        /// <summary>Convert grid index to letter (0→A, 1→B, ..., 25→Z, 26→AA).</summary>
        private static string GetGridLetter(int index)
        {
            string result = "";
            while (index >= 0)
            {
                result = (char)('A' + (index % 26)) + result;
                index = (index / 26) - 1;
            }
            return result;
        }

        /// <summary>Execute an IExternalCommand delegate.</summary>
        private static Result RunCommand(IExternalCommand cmd,
            ExternalCommandData data, ElementSet elems)
        {
            string msg = "";
            return cmd.Execute(data, ref msg, elems);
        }

        /// <summary>Execute a step with timing and error handling.</summary>
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
                StingLog.Info($"Project Setup step {stepNum}: {label} — {status} ({sw.Elapsed.TotalSeconds:F1}s)");
                return result == Result.Succeeded ? 1 : 0;
            }
            catch (Exception ex)
            {
                sw.Stop();
                report.AppendLine($"  {stepNum,2}. {label} — FAILED: {ex.Message}");
                StingLog.Error($"Project Setup step {stepNum}: {label}", ex);
                return 0;
            }
        }
    }
}
