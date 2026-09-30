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
    /// then executes a comprehensive automation pipeline.
    /// Each step runs in its own transaction.
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
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            UIApplication uiApp = ctx.App;
            UIDocument uidoc = ctx.UIDoc;
            Document doc = ctx.Doc;

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

            StingLog.Info("Project Setup Wizard: starting comprehensive automation" +
                          (data.FastMode ? " (FAST mode)" : "") +
                          (data.SuspendUIUpdates ? " + UI-suspend" : ""));
            var report = new StringBuilder();
            report.AppendLine("STING Project Setup Wizard Results");
            report.AppendLine(new string('═', 55));

            // Apply title block selections to TagConfig BEFORE any sheet/template step reads them.
            ApplyTitleBlockSelections(doc, data);

            // Pre-scan document for fast-mode skip decisions (counts of existing materials/schedules/templates).
            var preScan = data.FastMode ? PreScan(doc) : null;
            if (preScan != null)
                report.AppendLine($"  Fast-mode pre-scan: {preScan.Materials} materials, {preScan.Schedules} schedules, {preScan.Templates} templates, {preScan.Views} views");

            // Suspend UI: switch to a lightweight drafting view so Revit does not regen the active
            // view on every transaction (single biggest win on heavy models).
            View originalView = null;
            if (data.SuspendUIUpdates)
                originalView = TrySuspendUI(uidoc);

            int stepNum = 0;
            int passed = 0;
            int failed = 0;
            int skipped = 0;
            var totalSw = Stopwatch.StartNew();

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

                // Step: Rename scope boxes (must run before grids so scoping uses new names)
                if (data.RenameScopeBoxes && data.ScopeBoxRenames != null && data.ScopeBoxRenames.Count > 0)
                {
                    passed += RunStep(ref stepNum, report,
                        $"Rename Scope Boxes ({data.ScopeBoxRenames.Count})",
                        () => RenameScopeBoxes(doc, data));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Rename Scope Boxes — SKIPPED");
                    skipped++;
                }
                // Renames whose new name STING does not read were not applied (see
                // ScopeBoxRenamePattern). Say which, so the person is not left thinking
                // the boxes now drive LOC / ZONE.
                if (data.RenameScopeBoxes && data.ScopeBoxRenamesRefused != null && data.ScopeBoxRenamesRefused.Count > 0)
                {
                    report.AppendLine($"      {data.ScopeBoxRenamesRefused.Count} rename(s) not applied:");
                    foreach (var r in data.ScopeBoxRenamesRefused.Take(10)) report.AppendLine($"        • {r}");
                    if (data.ScopeBoxRenamesRefused.Count > 10)
                        report.AppendLine($"        • … {data.ScopeBoxRenamesRefused.Count - 10} more");
                }

                // Step: Create Grids
                if (data.CreateGrids && (data.GridHCount > 0 || data.GridVCount > 0))
                {
                    int gridTotal = data.GridHCount + data.GridVCount;
                    string label = $"Create Grids ({gridTotal} lines" +
                        (data.AlignToScopeBoxOrientation ? ", scope-box aligned" : "") +
                        (data.AssignGridsToScopeBoxes ? ", scoped" : "") + ")";
                    passed += RunStep(ref stepNum, report, label,
                        () => CreateGrids(doc, data));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Grids — SKIPPED (not selected)");
                    skipped++;
                }

                // Step: Two sections per scope box (centred, both directions), through
                // the drawing-type producer (DTW-74) like the grid sections below.
                if (data.TwoSectionsPerScopeBox && data.ScopeBoxSelection != null && data.ScopeBoxSelection.Count > 0)
                {
                    var sbSecDetail = new StringBuilder();
                    passed += RunStep(ref stepNum, report,
                        $"Scope-Box Sections ({data.ScopeBoxSelection.Count * 2} views, drawing types)",
                        () => ProduceScopeBoxSections(doc, data, sbSecDetail));
                    report.Append(sbSecDetail);
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Scope-Box Sections — SKIPPED");
                    skipped++;
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
                    skipped++;
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
                    skipped++;
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Enable Worksharing — SKIPPED (not selected)");
                    skipped++;
                }

                // Critical check: if levels failed, offer rollback
                if (passed < 2)
                {
                    StingLog.Warn("Project Setup: foundation phase had failures");
                    TaskDialog critDlg = new TaskDialog("Project Setup — Warning");
                    critDlg.MainInstruction = "Foundation steps had issues";
                    critDlg.MainContent =
                        "Some foundation steps (project info / levels) did not succeed.\n" +
                        "Continue with remaining automation or stop here?";
                    critDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        "Continue anyway", "Proceed with remaining steps");
                    critDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        "Stop here", "Stop setup — use Ctrl+Z to undo completed steps individually");
                    if (critDlg.Show() == TaskDialogResult.CommandLink2)
                    {
                        TaskDialog.Show("Project Setup",
                            "Setup stopped. Completed steps are committed.\n" +
                            "Use Ctrl+Z (Undo) to revert individual steps if needed.");
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
                    skipped++;
                }

                // Step: Create BLE + MEP Materials
                if (data.CreateMaterials)
                {
                    // Fast-mode: if materials already populated (>=1000 of expected 1,279), skip the
                    // bulk imports. These two steps alone dominate setup time on empty projects.
                    if (preScan != null && preScan.Materials >= 1000)
                    {
                        stepNum += 2;
                        report.AppendLine($"  {stepNum - 1,2}. Create BLE Materials — SKIPPED (fast: {preScan.Materials} materials present)");
                        report.AppendLine($"  {stepNum,2}. Create MEP Materials — SKIPPED (fast)");
                        skipped += 2;
                    }
                    else
                    {
                        passed += RunStep(ref stepNum, report, "Create BLE Materials (815)",
                            () => RunCommand(new CreateBLEMaterialsCommand(), commandData, elements));
                        passed += RunStep(ref stepNum, report, "Create MEP Materials (464)",
                            () => RunCommand(new CreateMEPMaterialsCommand(), commandData, elements));
                    }
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Materials — SKIPPED");
                    skipped++;
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
                    skipped++;
                }

                // Step: Batch Create Schedules
                if (data.CreateSchedules)
                {
                    // Fast-mode: if >=140 schedules already exist (out of 168 target), skip the bulk import.
                    if (preScan != null && preScan.Schedules >= 140)
                    {
                        stepNum += 2;
                        report.AppendLine($"  {stepNum - 1,2}. Batch Create Schedules — SKIPPED (fast: {preScan.Schedules} schedules present)");
                        report.AppendLine($"  {stepNum,2}. Create Template Schedules — SKIPPED (fast)");
                        skipped += 2;
                    }
                    else
                    {
                        passed += RunStep(ref stepNum, report, "Batch Create Schedules (168)",
                            () => RunCommand(new BatchSchedulesCommand(), commandData, elements));
                        passed += RunStep(ref stepNum, report, "Create Template Schedules (13)",
                            () => RunCommand(new CreateTemplateSchedulesCommand(), commandData, elements));
                    }
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Schedules — SKIPPED");
                    skipped++;
                }

                // ════════════════════════════════════════════════════
                // PHASE 3: STANDARDS
                // ════════════════════════════════════════════════════
                report.AppendLine("\n── Phase 3: Standards ──");

                // Fast-mode: if templates are already populated (≥20 STING templates), skip the whole pipeline.
                bool skipTemplatePipeline = preScan != null && preScan.Templates >= 20;
                if (skipTemplatePipeline)
                    report.AppendLine($"  (fast mode: {preScan.Templates} view templates present — skipping Phase 3 standards)");

                if (data.UseLatestTemplateSetup && !skipTemplatePipeline)
                {
                    // Latest 15-step TemplateSetupWizard ordering (matches TemplateManagerCommands.cs)
                    report.AppendLine("  (latest Template Setup pipeline)");
                    passed += RunStep(ref stepNum, report, "Fill Patterns (12 ISO)",
                        () => RunCommand(new CreateFillPatternsCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Line Patterns (10 ISO 128)",
                        () => RunCommand(new CreateLinePatternsCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Line Styles (16)",
                        () => RunCommand(new CreateLineStylesCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Object Styles (40)",
                        () => RunCommand(new CreateObjectStylesCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Text Styles (12 ISO 3098)",
                        () => RunCommand(new CreateTextStylesCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Dimension Styles (7)",
                        () => RunCommand(new CreateDimensionStylesCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "View Filters (28+)",
                        () => RunCommand(new CreateFiltersCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "View Templates (23 w/ VG)",
                        () => RunCommand(new ViewTemplatesCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Apply Filters to Templates",
                        () => RunCommand(new ApplyFiltersToViewsCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "VG Overrides (5-layer)",
                        () => RunCommand(new CreateVGOverridesCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Batch Family Parameters (CSV)",
                        () => RunCommand(new BatchAddFamilyParamsCommand(), commandData, elements));
                    passed += RunStep(ref stepNum, report, "Template Metadata Schedules",
                        () => RunCommand(new CreateTemplateSchedulesCommand(), commandData, elements));
                }
                else if (skipTemplatePipeline)
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Template Pipeline — SKIPPED (fast: templates already populated)");
                    skipped++;
                }
                else
                {
                    // Legacy granular steps — user unchecked "Use latest Template Setup"
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
                        skipped++;
                    }

                    if (data.CreateFilters)
                    {
                        passed += RunStep(ref stepNum, report, "Create View Filters (28+)",
                            () => RunCommand(new CreateFiltersCommand(), commandData, elements));
                    }
                    else
                    {
                        stepNum++;
                        report.AppendLine($"  {stepNum,2}. Create Filters — SKIPPED");
                        skipped++;
                    }

                    if (data.CreateTemplates)
                    {
                        passed += RunStep(ref stepNum, report, "Create View Templates",
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
                        skipped++;
                    }
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
                    skipped++;
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
                    skipped++;
                }

                // Step: Drawing production — the same workflow as SETUP → DRAWING
                // PRODUCTION. Before Phase 4 so the sheets it creates find STING
                // title blocks and view types rather than whatever loaded first.
                if (data.RunDrawingProductionSetup)
                {
                    // Unattended: its own report dialog used to stop the wizard half-way,
                    // and a failed required step still read "OK" because any passing step
                    // made the workflow Succeed. The outcome is folded into this report.
                    var dps = new Commands.Drawing.DrawingProductionSetupCommand { Unattended = true };
                    passed += RunStep(ref stepNum, report, "Set Up Drawing Production (workflow)",
                        () => RunCommand(dps, commandData, elements));
                    if (dps.LastOutcome != null)
                    {
                        report.AppendLine($"      {dps.LastOutcome.Summary}");
                        if (dps.LastOutcome.IsFailure)
                            foreach (var line in (dps.LastOutcome.Report ?? "").Split('\n')
                                         .Select(l => l.TrimEnd('\r'))
                                         .Where(l => l.IndexOf("FAIL", StringComparison.OrdinalIgnoreCase) >= 0)
                                         .Take(8))
                                report.AppendLine($"      {line.Trim()}");
                    }
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Set Up Drawing Production — SKIPPED");
                    skipped++;
                }

                // ════════════════════════════════════════════════════
                // PHASE 4: DOCUMENTATION
                // ════════════════════════════════════════════════════
                report.AppendLine("\n── Phase 4: Documentation ──");

                // Step: Sheet-number policy. Here, not in Set Project Information:
                // that runs in Phase 1, before Load Shared Parameters binds the
                // parameter on a fresh project. Before Create Sheets, which reads it.
                if (data.SheetNumberPolicy != null)
                {
                    // Detail goes after RunStep's header line, not before it.
                    var policyDetail = new StringBuilder();
                    passed += RunStep(ref stepNum, report,
                        $"Sheet-Number Policy ({Core.Drawing.SheetNumberPolicy.ToParameterValue(data.SheetNumberPolicy.Value)})",
                        () => WriteSheetNumberPolicy(doc, data.SheetNumberPolicy.Value, policyDetail));
                    report.Append(policyDetail);
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Sheet-Number Policy — SKIPPED (not changed in the wizard)");
                    skipped++;
                }

                // Step: Produce each discipline's plan drawings (and sheets) per level.
                // Through DrawingProducer, from the drawing types each ticked
                // discipline's PLAN / RCP routes to: the views and sheets are stamped,
                // so Doctor, Renumber, Heal TBs and Produce & Export see them, and a
                // later drawing-type production (or a re-run of this wizard) reuses
                // them instead of making a second set. The view template / style pack
                // comes from the drawing type, so no auto-assign pass follows.
                if (data.CreateViews || data.CreateSheets)
                {
                    var produceDetail = new StringBuilder();
                    passed += RunStep(ref stepNum, report,
                        $"Produce Discipline Drawings ({data.Disciplines.Count} disciplines, " +
                        (data.CreateSheets ? "views + sheets" : "views only") + ")",
                        () => ProduceDisciplineDrawings(doc, data, produceDetail));
                    report.Append(produceDetail);
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Produce Discipline Drawings — SKIPPED");
                    skipped++;
                }

                // Dependents, sections and elevations go through the drawing-type
                // producer like the plans above (DTW-34). They used to run the legacy
                // CreateDependentViews / BatchCreateSections / BatchCreateElevations
                // commands, whose unstamped views Doctor, Renumber and Produce never
                // saw, and which a later drawing-type production duplicated. These
                // call the producer directly, not the production commands' dialogs.

                // Step: Dependent views, one per scope box, of each produced plan type
                if (data.CreateDependents)
                {
                    var depDetail = new StringBuilder();
                    passed += RunStep(ref stepNum, report, "Produce Dependent Views From Scope Boxes (drawing types)",
                        () => ProduceScopeBoxDependents(doc, data, depDetail));
                    report.Append(depDetail);
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Dependents — SKIPPED");
                    skipped++;
                }

                // Step: Building sections along grid lines
                if (data.CreateSections)
                {
                    var secDetail = new StringBuilder();
                    passed += RunStep(ref stepNum, report, "Produce Building Sections Along Grids (drawing types)",
                        () => ProduceGridSections(doc, data, secDetail));
                    report.Append(secDetail);
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Sections — SKIPPED");
                    skipped++;
                }

                // Step: Exterior elevations
                if (data.CreateElevations)
                {
                    var elevDetail = new StringBuilder();
                    passed += RunStep(ref stepNum, report, "Produce Exterior Elevations (drawing types)",
                        () => ProduceExteriorElevations(doc, elevDetail));
                    report.Append(elevDetail);
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Create Elevations — SKIPPED");
                    skipped++;
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

                // Template assignment. Every view Phase 4 produces — plans, dependents,
                // sections, elevations — takes its drawing type's template, so a pass
                // is only needed when Phase 4 produced nothing. Auto-fix is always run
                // for template health.
                bool doTemplatePost = data.UseLatestTemplateSetup || data.CreateTemplates;
                bool phase4Produced = data.CreateViews || data.CreateSheets
                    || data.CreateSections || data.CreateElevations || data.CreateDependents;
                if (doTemplatePost && !phase4Produced)
                {
                    // Phase 4 didn't create views, so no first pass ran — do it now.
                    passed += RunStep(ref stepNum, report,
                        "Auto-Assign Templates to Views (by view type + name + phase + level)",
                        () => RunCommand(new AutoAssignTemplatesCommand(), commandData, elements));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Auto-Assign Templates — SKIPPED (Phase 4's drawings take their drawing type's template)");
                    skipped++;
                }
                if (doTemplatePost)
                {
                    passed += RunStep(ref stepNum, report,
                        "Auto-Fix Template Health (fill missing settings per view type)",
                        () => RunCommand(new AutoFixTemplateCommand(), commandData, elements));
                }
                else
                {
                    stepNum++;
                    report.AppendLine($"  {stepNum,2}. Auto-Fix Template Health — SKIPPED");
                    skipped++;
                }

                // Full auto-populate: tokens + dimensions + MEP + formulas + tags + combine.
                // Fast-mode: skip when the model is essentially empty (no taggable instances yet) —
                // the user will re-run this from the dock panel after modelling.
                if (data.LoadParams)
                {
                    bool emptyModel = data.FastMode && HasNoTaggableInstances(doc);
                    if (emptyModel)
                    {
                        stepNum++;
                        report.AppendLine($"  {stepNum,2}. Full Auto-Populate — SKIPPED (fast: no taggable instances yet)");
                        skipped++;
                    }
                    else
                    {
                        passed += RunStep(ref stepNum, report, "Full Auto-Populate (tokens+dims+MEP+formulas+tags)",
                            () => RunCommand(new FullAutoPopulateCommand(), commandData, elements));
                    }

                    // Avoid running BatchAddFamilyParamsCommand twice — Phase 3 already ran it in latest mode.
                    if (!data.UseLatestTemplateSetup)
                    {
                        passed += RunStep(ref stepNum, report, "Batch Family Parameters (CSV)",
                            () => RunCommand(new BatchAddFamilyParamsCommand(), commandData, elements));
                    }
                }

                // Set starting view
                passed += RunStep(ref stepNum, report, "Set Starting View",
                    () => SetStartingView(doc));

                // Restore original active view (reverse of TrySuspendUI at the top of Execute).
                if (originalView != null)
                {
                    try { uidoc.ActiveView = originalView; }
                    catch (Exception ex2) { StingLog.Warn($"Restore active view failed: {ex2.Message}"); }
                }

                // ── Finalize ─────────────────────────────────────────

                failed = stepNum - passed - skipped;
                totalSw.Stop();

                report.AppendLine(new string('─', 55));
                report.AppendLine($"  {passed}/{stepNum} succeeded" +
                    (skipped > 0 ? $", {skipped} skipped" : "") +
                    (failed > 0 ? $", {failed} FAILED" : ""));
                report.AppendLine($"  Duration: {totalSw.Elapsed.TotalSeconds:F1}s");
                if (failed > 0)
                    report.AppendLine("  Use Ctrl+Z to undo individual steps if needed.");

            // GAP-006: Persist wizard settings to project_config.json
            // Update TagConfig with wizard LOC/ZONE codes before saving
            if (data.LocCodes.Count > 0)
                TagConfig.LocCodes = data.LocCodes;
            if (data.ZoneCodes.Count > 0)
                TagConfig.ZoneCodes = data.ZoneCodes;

            // Prefer project-adjacent path to prevent config bleed between projects (Phase 15b)
            string configDir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName)
                : null;
            string configPath = !string.IsNullOrEmpty(configDir)
                ? Path.Combine(configDir, "project_config.json")
                : Path.Combine(StingToolsApp.DataPath ?? "", "project_config.json");

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
        /// <summary>
        /// K-11c — the three shared parameters that duplicate the project code, written
        /// as derived stamps from <c>ProjectInformation.Number</c>.
        /// <para>
        /// Kept as three separate parameters on purpose. Merging or retiring any of them
        /// would break a title-block label or schedule that reads it by name, and under
        /// K-11e we cannot establish which families do — a <c>.rfa</c> label is invisible
        /// to static analysis. Deriving them removes the drift without touching families.
        /// </para>
        /// </summary>
        private static readonly string[] DerivedProjectCodeParams =
        {
            "PRJ_PROJECT_COD_TXT",        // feeds {project} in sheet numbers
            "PRJ_ORG_PROJECT_CODE_TXT",   // feeds the template engine / ISO document numbers
            "PRJ_NR_TXT",                 // title-block label + schedule column
        };

        private static void StampDerivedProjectCode(ProjectInfo pi, string number)
        {
            if (pi == null || string.IsNullOrWhiteSpace(number)) return;
            foreach (var name in DerivedProjectCodeParams)
            {
                try
                {
                    var p = pi.LookupParameter(name);
                    if (p == null)
                    {
                        StingLog.Warn(
                            $"K-11c: '{name}' is not bound, so the project code cannot be stamped into it. "
                          + "Run Load Shared Parameters and re-run Project Setup.");
                        continue;
                    }
                    if (p.IsReadOnly || p.StorageType != StorageType.String) continue;

                    string current = p.AsString() ?? "";
                    if (string.Equals(current, number, StringComparison.Ordinal)) continue;

                    // Divergence is worth saying out loud even though we are about to fix
                    // it: a value that was hand-edited away from Number is a decision
                    // someone made, and overwriting it silently hides that.
                    if (!string.IsNullOrWhiteSpace(current))
                        StingLog.Warn(
                            $"K-11c: '{name}' held '{current}' but ProjectInformation.Number is "
                          + $"'{number}'. Number is the single source, so the stamp is being "
                          + "overwritten. If '{current}' was deliberate, the two have been out of "
                          + "step and every document number issued from it disagrees with the "
                          + "folder tree.");

                    p.Set(number);
                }
                catch (Exception ex) { StingLog.Warn($"K-11c stamp '{name}': {ex.Message}"); }
            }
        }

        /// <summary>
        /// Record the sheet-number policy on Project Information. Writes only a
        /// change (SheetNumberPolicy.ValueToWrite). An unbound parameter is a
        /// WARN with the reason in the report, not a silent success — a project
        /// that asked for ISO numbering and got none would find out at issue.
        /// </summary>
        private static Result WriteSheetNumberPolicy(Document doc, Core.Drawing.SheetNumberPolicyKind chosen,
            StringBuilder report)
        {
            var p = doc.ProjectInformation?.LookupParameter(Core.Drawing.SheetNumberPolicy.PolicyParameterName);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.String)
            {
                string why = $"{Core.Drawing.SheetNumberPolicy.PolicyParameterName} is not a writable text parameter "
                    + "on Project Information — enable Load Shared Parameters, or run it, then set the policy again.";
                report.AppendLine("      " + why);
                StingLog.Warn("ProjectSetup: " + why);
                return Result.Failed;
            }
            string stored = p.AsString();
            string value = Core.Drawing.SheetNumberPolicy.ValueToWrite(stored, chosen);
            if (value == null)
            {
                report.AppendLine($"      unchanged ('{stored}')");
                return Result.Succeeded;
            }
            if (!string.IsNullOrWhiteSpace(stored) && !Core.Drawing.SheetNumberPolicy.IsRecognised(stored))
                report.AppendLine($"      replaced unrecognised value '{stored.Trim()}' (it was read as per drawing type)");
            bool set;
            using (var tx = new Transaction(doc, "STING Set Sheet-Number Policy"))
            {
                tx.Start();
                set = p.Set(value);
                if (set) tx.Commit(); else tx.RollBack();
            }
            if (!set)
            {
                string why = $"Revit refused to write '{value}' to {Core.Drawing.SheetNumberPolicy.PolicyParameterName}.";
                report.AppendLine("      " + why);
                StingLog.Warn("ProjectSetup: " + why);
                return Result.Failed;
            }
            report.AppendLine($"      '{stored}' → '{value}'");
            StingLog.Info($"ProjectSetup: sheet-number policy '{value}'");
            return Result.Succeeded;
        }

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
                    {
                        pi.Number = data.ProjectNumber;
                        // K-11c: ProjectInformation.Number is the SINGLE SOURCE for the
                        // project code. The three shared parameters that also carry it are
                        // written here as DERIVED STAMPS so they cannot drift apart at
                        // setup. None of the three is merged or retired — families and
                        // title-block labels read them by name and must stay untouched.
                        StampDerivedProjectCode(pi, data.ProjectNumber);
                    }
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

                    // HC-08 — Write healthcare facility type profile to ProjectInformation
                    // when the Healthcare discipline was selected in the wizard.
                    if (!string.IsNullOrWhiteSpace(data.HealthcareFacilityProfile))
                    {
                        try
                        {
                            ParameterHelpers.SetString(pi, "PRJ_ORG_HEALTH_PACK_PROFILE_TXT",
                                data.HealthcareFacilityProfile, overwrite: true);
                            StingLog.Info($"ProjectSetup: Healthcare facility profile set to {data.HealthcareFacilityProfile}");
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Could not set PRJ_ORG_HEALTH_PACK_PROFILE_TXT: {ex.Message}");
                        }
                    }

                    // Standards regional preset — write PROJECT_REGION so the
                    // choice persists on the .rvt; the in-process singleton
                    // and per-project sidecar are updated below (after the
                    // transaction commits).
                    if (!string.IsNullOrWhiteSpace(data.Region))
                    {
                        try
                        {
                            Parameter regionParam = pi.LookupParameter(ParamRegistry.PROJECT_REGION);
                            if (regionParam != null && !regionParam.IsReadOnly)
                                regionParam.Set(data.Region);
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Could not set PROJECT_REGION on ProjectInfo: {ex.Message}");
                        }
                    }

                    tx.Commit();
                }

                // Apply the regional preset on the in-process singleton so
                // every Standards-aware command that runs immediately after
                // the wizard already sees the new electrical / HVAC / fire
                // bindings (avoids the next-doc-open race). Also write the
                // sidecar so the choice survives even on projects where
                // PROJECT_REGION isn't bound to ProjectInformation.
                if (!string.IsNullOrWhiteSpace(data.Region))
                {
                    try
                    {
                        StingTools.Standards.ProjectStandardsManager.Instance
                            .ApplyRegionalPreset(data.Region);
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"ApplyRegionalPreset({data.Region}) skipped: {ex.Message}");
                    }
                    ProjectRegionSidecar.Write(doc, data.Region);
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
                catch (Exception ex2)
                {
                    StingLog.Warn($"Could not set FIRE_RATING: {ex2.Message}");
                }
            }

            // Store electrical voltage/phase for panel schedules
            if (data.Disciplines.Contains("E"))
            {
                try
                {
                    // ELC_VOLTAGE was never defined, so the chosen supply was lost. The
                    // whole choice ("400V 3-phase") is kept, not just its leading digits.
                    Parameter vParam = pi.LookupParameter(ParamRegistry.PRJ_ELC_SUPPLY_VOLTAGE_TXT);
                    string v = string.Join(" ", new[] { data.ElecConfig.Voltage, data.ElecConfig.PhaseSystem }
                        .Where(x => !string.IsNullOrWhiteSpace(x)));
                    if (vParam != null && !vParam.IsReadOnly && vParam.StorageType == StorageType.String
                        && !string.IsNullOrWhiteSpace(v))
                        vParam.Set(v);
                }
                catch (Exception ex2)
                {
                    StingLog.Warn($"Could not set PRJ_ELC_SUPPLY_VOLTAGE_TXT: {ex2.Message}");
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
                        ApplyLevelStoryFlag(newLevel, def.IsStory);
                        created++;
                        existingByName[def.Name] = newLevel;
                        existingByElev[roundedM] = newLevel;
                        StingLog.Info($"Created level '{def.Name}' at {def.ElevationMeters:F2}m (type={def.LevelType}, story={def.IsStory})");
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

        /// <summary>Set LEVEL_IS_BUILDING_STORY on a level (best-effort).</summary>
        private static void ApplyLevelStoryFlag(Level level, bool isStory)
        {
            try
            {
                Parameter p = level.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY);
                if (p != null && !p.IsReadOnly)
                    p.Set(isStory ? 1 : 0);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Could not set story flag on '{level.Name}': {ex.Message}");
            }
        }

        /// <summary>
        /// Rename scope boxes according to the wizard's rename map.
        /// Skips entries where the target name already exists on a different scope box.
        /// </summary>
        private static Result RenameScopeBoxes(Document doc, ProjectSetupData data)
        {
            try
            {
                var scopeBoxes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                    .WhereElementIsNotElementType()
                    .ToList();

                var byCurrent = scopeBoxes
                    .GroupBy(e => e.Name, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                var takenNames = new HashSet<string>(scopeBoxes.Select(e => e.Name), StringComparer.Ordinal);

                int renamed = 0, skipped = 0;
                using (Transaction tx = new Transaction(doc, "STING Rename Scope Boxes"))
                {
                    tx.Start();
                    foreach (var kv in data.ScopeBoxRenames)
                    {
                        if (!byCurrent.TryGetValue(kv.Key, out Element sb))
                        {
                            skipped++;
                            continue;
                        }
                        string newName = kv.Value?.Trim();
                        if (string.IsNullOrEmpty(newName) || string.Equals(newName, sb.Name, StringComparison.Ordinal))
                        {
                            skipped++;
                            continue;
                        }
                        // Prevent collisions
                        if (takenNames.Contains(newName))
                        {
                            StingLog.Warn($"Scope-box rename skipped: '{newName}' already used.");
                            skipped++;
                            continue;
                        }
                        try
                        {
                            string old = sb.Name;
                            sb.Name = newName;
                            takenNames.Remove(old);
                            takenNames.Add(newName);
                            renamed++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Scope-box rename '{kv.Key}' → '{newName}' failed: {ex.Message}");
                            skipped++;
                        }
                    }
                    tx.Commit();
                }

                // Patch the wizard's selection list so subsequent steps use the new names
                if (data.ScopeBoxSelection != null && data.ScopeBoxRenames.Count > 0)
                {
                    for (int i = 0; i < data.ScopeBoxSelection.Count; i++)
                    {
                        if (data.ScopeBoxRenames.TryGetValue(data.ScopeBoxSelection[i], out string n))
                            data.ScopeBoxSelection[i] = n;
                    }
                }

                StingLog.Info($"Scope boxes: {renamed} renamed, {skipped} skipped");
                return renamed > 0 ? Result.Succeeded : (skipped > 0 ? Result.Succeeded : Result.Failed);
            }
            catch (Exception ex)
            {
                StingLog.Error("RenameScopeBoxes failed", ex);
                return Result.Failed;
            }
        }

        /// <summary>
        /// "Two building sections per scope box", through the drawing-type producer
        /// (DTW-74): for each checked box, the section the box gives along its long side
        /// (the cut DrawingProducer takes for a Section rule on a box, DTW-52) and the one
        /// perpendicular to it, both through the box's centre over its full height, of the
        /// section drawing type the architectural discipline routes to (structural if
        /// none). The context tags "ScopeBox-&lt;box&gt;-Long" / "-Cross" are stable, so a
        /// re-run finds the stamped views and reuses them; Doctor, Renumber and Heal see
        /// them like any produced drawing. Sheets follow the wizard's "Create sheets".
        /// </summary>
        private static Result ProduceScopeBoxSections(Document doc, ProjectSetupData data, StringBuilder detail)
        {
            var dt = RouteFirst(doc, "SECTION", "Section", "A", "S");
            if (dt == null)
            {
                detail.AppendLine("      No drawing type routes from A / SECTION or S / SECTION — nothing produced (add a routing rule).");
                return Result.Failed;
            }
            var selected = new HashSet<string>(data.ScopeBoxSelection ?? new List<string>(), StringComparer.Ordinal);
            var boxes = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType()
                .Where(e => selected.Contains(e.Name))
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (boxes.Count == 0)
            {
                detail.AppendLine("      None of the checked scope boxes is in the model — nothing to produce.");
                return Result.Failed;
            }

            var opts = new Core.Drawing.ProduceOptions
            {
                CreateSheet = data.CreateSheets,
                PlaceOnSheet = data.CreateSheets,
                RunAnnotation = true,
                Idempotent = true,
            };
            int views = 0, sheets = 0;
            var warnings = new List<string>();
            detail.AppendLine($"      A / SECTION → {dt.Id}");
            Core.Drawing.DrawingTypePresentation.Prewarm(doc);
            using (Core.Drawing.DrawingProducer.PrimeBatchScope(doc))
            using (var tg = new TransactionGroup(doc, "STING Project Setup — Scope-Box Sections"))
            {
                tg.Start();
                foreach (var box in boxes)
                {
                    var cuts = new[]
                    {
                        (Which: "Long",  Bounds: Core.Drawing.DrawingProducer.BuildLongSectionBoxFromScopeBox(doc, box, warnings)),
                        (Which: "Cross", Bounds: Core.Drawing.DrawingProducer.BuildCrossSectionBoxFromScopeBox(doc, box, warnings)),
                    };
                    foreach (var cut in cuts)
                    {
                        if (cut.Bounds == null) { warnings.Add($"{box.Name} ({cut.Which}): no section could be cut from the box."); continue; }
                        string tag = $"ScopeBox-{box.Name}-{cut.Which}";
                        using (var t = new Transaction(doc, $"STING Section {tag}"))
                        {
                            t.Start();
                            try
                            {
                                var dctx = new Core.Drawing.DrawingContext { CustomBounds = cut.Bounds, Tag = tag };
                                var pr = Core.Drawing.DrawingProducer.ProduceAllViews(doc, dt, dctx, opts);
                                warnings.AddRange(pr.Warnings);
                                if (t.Commit() == TransactionStatus.Committed)
                                {
                                    views += pr.ViewIds.Count;
                                    if (pr.SheetId != ElementId.InvalidElementId && !pr.SheetReused) sheets++;
                                }
                                else warnings.Add($"{tag}: the transaction did not commit.");
                            }
                            catch (Exception ex)
                            {
                                if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                                warnings.Add($"{tag}: {ex.Message} — rolled back.");
                            }
                        }
                    }
                }
                tg.Assimilate();
            }

            detail.AppendLine($"      {views} section(s) from {boxes.Count} scope box(es) (existing stamped views reused), " +
                              $"{sheets} new sheet(s){(data.CreateSheets ? "" : " — sheets not requested")}.");
            AppendWarnings(detail, warnings, "scope-box sections");
            return views > 0 ? Result.Succeeded : Result.Failed;
        }

        private static XYZ Normalise(XYZ v)
        {
            if (v == null || v.IsZeroLength()) return XYZ.BasisX;
            return v.Normalize();
        }

        /// <summary>
        /// Create structural grids from wizard data. When scope-box integration is enabled,
        /// grids are rotated to the primary scope box's orientation (handles tilted boxes),
        /// origined at that scope box's centre, and optionally scoped to the checked boxes.
        /// </summary>
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

            // Default grid frame (world-aligned at origin)
            XYZ origin = XYZ.Zero;
            XYZ axisX = XYZ.BasisX;
            XYZ axisY = XYZ.BasisY;

            // Resolve scope boxes to align to + scope into
            var selectedNames = new HashSet<string>(
                data.ScopeBoxSelection ?? new List<string>(), StringComparer.Ordinal);
            var selectedScopeBoxes = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType()
                .Where(e => selectedNames.Contains(e.Name))
                .ToList();

            // Use first checked scope box as the alignment reference
            if (data.AlignToScopeBoxOrientation && selectedScopeBoxes.Count > 0)
            {
                Element refSb = selectedScopeBoxes[0];
                BoundingBoxXYZ bb = refSb.get_BoundingBox(null);
                if (bb != null)
                {
                    Transform bt = bb.Transform ?? Transform.Identity;
                    axisX = Normalise(bt.BasisX);
                    axisY = Normalise(bt.BasisY);
                    XYZ localCentre = (bb.Min + bb.Max) * 0.5;
                    origin = bt.OfPoint(localCentre);
                    StingLog.Info($"Grids aligned to scope box '{refSb.Name}' " +
                                  $"(X={axisX.X:F3},{axisX.Y:F3}, centre={origin.X:F2},{origin.Y:F2})");
                }
            }

            // Compute overhang so grids extend past the grid block
            double hHalf = (data.GridHCount > 1 ? (data.GridHCount - 1) * hSpacingFt : 0) * 0.5;
            double vHalf = (data.GridVCount > 1 ? (data.GridVCount - 1) * vSpacingFt : 0) * 0.5;

            using (Transaction tx = new Transaction(doc, "STING Create Grids"))
            {
                tx.Start();

                // Horizontal grids (lettered A, B, C...) — run along axisX, stepped along axisY
                for (int i = 0; i < data.GridHCount; i++)
                {
                    string name = GetGridLetter(i);
                    if (existingNames.Contains(name)) continue;

                    double offsetY = i * hSpacingFt - hHalf;
                    XYZ start = origin + axisY * offsetY + axisX * (-vLengthFt * 0.55);
                    XYZ end   = origin + axisY * offsetY + axisX * ( vLengthFt * 0.55);

                    try
                    {
                        Grid grid = Grid.Create(doc, Line.CreateBound(start, end));
                        grid.Name = name;
                        AssignScopeBoxToGrid(grid, selectedScopeBoxes, data.AssignGridsToScopeBoxes);
                        created++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Grid creation failed '{name}': {ex.Message}");
                    }
                }

                // Vertical grids (numbered 1, 2, 3...) — run along axisY, stepped along axisX
                for (int i = 0; i < data.GridVCount; i++)
                {
                    string name = (i + 1).ToString();
                    if (existingNames.Contains(name)) continue;

                    double offsetX = i * vSpacingFt - vHalf;
                    XYZ start = origin + axisX * offsetX + axisY * (-hLengthFt * 0.55);
                    XYZ end   = origin + axisX * offsetX + axisY * ( hLengthFt * 0.55);

                    try
                    {
                        Grid grid = Grid.Create(doc, Line.CreateBound(start, end));
                        grid.Name = name;
                        AssignScopeBoxToGrid(grid, selectedScopeBoxes, data.AssignGridsToScopeBoxes);
                        created++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Grid creation failed '{name}': {ex.Message}");
                    }
                }

                tx.Commit();
            }

            StingLog.Info($"Grids: {created} created" +
                          (data.AlignToScopeBoxOrientation ? " (scope-box aligned)" : "") +
                          (data.AssignGridsToScopeBoxes ? ", scoped" : ""));
            return created > 0 ? Result.Succeeded : Result.Failed;
        }

        /// <summary>
        /// Set a grid's scope box (DATUM_VOLUME_OF_INTEREST). When multiple boxes are checked,
        /// the first one is used — scope boxes are 1:1 with datums in Revit.
        /// </summary>
        private static void AssignScopeBoxToGrid(Grid grid, List<Element> selectedScopeBoxes, bool enabled)
        {
            if (!enabled || selectedScopeBoxes == null || selectedScopeBoxes.Count == 0) return;
            try
            {
                Parameter p = grid.get_Parameter(BuiltInParameter.DATUM_VOLUME_OF_INTEREST);
                if (p != null && !p.IsReadOnly)
                    p.Set(selectedScopeBoxes[0].Id);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Could not scope grid '{grid.Name}': {ex.Message}");
            }
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
        /// Documentation phase: each ticked discipline's plan drawings, per level, through
        /// DrawingProducer. The drawing types are the ones the discipline's PLAN (and RCP)
        /// routes to — DrawingDispatcher's answer, the same one every other production
        /// path uses. So the views and sheets carry the drawing-type stamp (Doctor,
        /// Renumber, Heal TBs and Produce &amp; Export see them), the template / style pack /
        /// crop / annotation come from the type, and a re-run — of the wizard or of any
        /// drawing-type production — finds them by stamp and reuses them.
        ///
        /// This replaced a hand-rolled "{Discipline} Plan - {Level}" loop plus
        /// BatchCreateSheets, whose unstamped output every drawing-type command ignored
        /// and a later production duplicated.
        /// </summary>
        private static Result ProduceDisciplineDrawings(Document doc, ProjectSetupData data, StringBuilder detail)
        {
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();
            if (levels.Count == 0)
            {
                detail.AppendLine("      The model has no levels — nothing to produce.");
                return Result.Failed;
            }

            var routing = Commands.Drawing.BatchProduceCommons.RoutePerLevel(doc, data.Disciplines ?? new List<string>());
            foreach (var pick in routing.Picks)
                detail.AppendLine($"      {pick.Discipline,-3} {pick.DocType,-4} → {pick.Type.Id}");
            foreach (var disc in routing.Unrouted)
                detail.AppendLine($"      {disc,-3} no drawing type routes from {disc} / PLAN — nothing produced for it (add a routing rule to produce it).");
            foreach (var n in routing.NotPerLevel)
                detail.AppendLine($"      {n} is not a per-level plan — not produced here.");

            var types = routing.Types;
            if (types.Count == 0)
            {
                detail.AppendLine("      No ticked discipline routes to a plan drawing type.");
                return Result.Failed;
            }

            var opts = new Core.Drawing.ProduceOptions
            {
                CreateSheet = data.CreateSheets,
                PlaceOnSheet = data.CreateSheets,
                RunAnnotation = true,
                Idempotent = true,
            };
            int views = 0, sheets = 0;
            var warnings = new List<string>();
            Core.Drawing.DrawingTypePresentation.Prewarm(doc);
            using (Core.Drawing.DrawingProducer.PrimeBatchScope(doc))
                Commands.Drawing.ProduceViewsPerLevelCommand.Produce(doc, types, levels, opts, null,
                    ref views, ref sheets, warnings);

            detail.AppendLine($"      {views} view(s) across {levels.Count} level(s) (existing stamped views reused, not duplicated), " +
                              $"{sheets} new sheet(s){(data.CreateSheets ? "" : " — sheets not requested")}.");
            var distinct = warnings.Distinct().ToList();
            foreach (var w in distinct) StingLog.Warn($"Project Setup produce: {w}");
            if (distinct.Count > 0)
            {
                detail.AppendLine($"      {distinct.Count} warning(s):");
                foreach (var w in distinct.Take(8)) detail.AppendLine($"        • {w}");
                if (distinct.Count > 8) detail.AppendLine($"        • … {distinct.Count - 8} more in the STING log");
            }
            return views > 0 ? Result.Succeeded : Result.Failed;
        }

        /// <summary>
        /// "Create dependent views from scope boxes", through the drawing-type producer:
        /// for each plan drawing type the ticked disciplines route to, each scope box
        /// gets a view that is a dependent of that type's parent plan on the level,
        /// cropped to the box, stamped with the type and the box (so Doctor, Renumber and
        /// a later Produce From Scope Boxes find it and reuse it). Which boxes:
        ///   plain boxes            every plan type, every level;
        ///   STING-AREA::a[::lvl]   every plan type, the named level (else every level);
        ///   STING::type[::lvl]     its own type only, on its level (else every level);
        ///   seed / building / zone boxes are footprints and templates, not drawing areas.
        /// </summary>
        private static Result ProduceScopeBoxDependents(Document doc, ProjectSetupData data, StringBuilder detail)
        {
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();
            var boxes = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType()
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (boxes.Count == 0)
            {
                detail.AppendLine("      No scope boxes in the model — nothing to produce.");
                return Result.Cancelled;
            }
            if (levels.Count == 0)
            {
                detail.AppendLine("      The model has no levels — nothing to produce.");
                return Result.Failed;
            }

            var planTypes = Commands.Drawing.BatchProduceCommons
                .RoutePerLevel(doc, data.Disciplines ?? new List<string>()).Types;

            // (drawing type, level, box, tag) in the order they will be produced.
            var jobs = new List<(Core.Drawing.DrawingType Type, Level Level, Element Box, string Tag)>();
            var notes = new List<string>();
            foreach (var box in boxes)
            {
                string name = box.Name ?? "";
                switch (Core.Drawing.ScopeBoxNames.Classify(name))
                {
                    case Core.Drawing.ScopeBoxKind.Plain:
                        foreach (var dt in planTypes)
                            foreach (var lvl in levels) jobs.Add((dt, lvl, box, null));
                        break;

                    case Core.Drawing.ScopeBoxKind.Area:
                    {
                        if (!Core.Drawing.ScopeBoxNames.TryParseArea(name, out _, out var lvlSeg, out var why))
                        { notes.Add($"{name}: {why} — skipped."); break; }
                        var onLevels = MatchLevels(levels, lvlSeg);
                        if (onLevels.Count == 0) { notes.Add($"{name}: level '{lvlSeg}' is not in the model — skipped."); break; }
                        foreach (var dt in planTypes)
                            foreach (var lvl in onLevels) jobs.Add((dt, lvl, box, null));
                        break;
                    }

                    case Core.Drawing.ScopeBoxKind.DrawingType:
                    {
                        if (!Core.Drawing.ScopeBoxBinder.TryParseName(name, out var bnd, out var why))
                        { notes.Add($"{name}: {why ?? "not a valid STING:: name"} — skipped."); break; }
                        var dt = Core.Drawing.DrawingTypeRegistry.Get(doc, bnd.DrawingTypeId);
                        if (dt == null) { notes.Add($"{name}: drawing type '{bnd.DrawingTypeId}' is not in the catalogue — skipped."); break; }
                        var onLevels = MatchLevels(levels, bnd.LevelCode);
                        if (onLevels.Count == 0) { notes.Add($"{name}: level '{bnd.LevelCode}' is not in the model — skipped."); break; }
                        foreach (var lvl in onLevels) jobs.Add((dt, lvl, box, bnd.Tag));
                        break;
                    }

                    default:
                        break;   // seed, building and zone boxes are not drawing areas
                }
            }

            if (jobs.Count == 0)
            {
                detail.AppendLine(planTypes.Count == 0
                    ? "      No ticked discipline routes to a plan drawing type, and no STING:: box names one."
                    : "      No scope box is a drawing area (seed, building and zone boxes are not).");
                foreach (var n in notes.Take(8)) detail.AppendLine($"        • {n}");
                return Result.Failed;
            }

            var opts = new Core.Drawing.ProduceOptions
            {
                CreateSheet = data.CreateSheets,
                PlaceOnSheet = data.CreateSheets,
                RunAnnotation = true,
                Idempotent = true,
                DuplicateOption = ViewDuplicateOption.AsDependent,
            };
            int views = 0, sheets = 0;
            var warnings = new List<string>(notes);
            Core.Drawing.DrawingTypePresentation.Prewarm(doc);
            using (Core.Drawing.DrawingProducer.PrimeBatchScope(doc))
            using (var tg = new TransactionGroup(doc, "STING Project Setup — Dependent Views"))
            {
                tg.Start();
                foreach (var job in jobs)
                {
                    using (var t = new Transaction(doc, $"STING Dependent {job.Box.Name} {job.Level.Name}"))
                    {
                        t.Start();
                        try
                        {
                            var dctx = new Core.Drawing.DrawingContext { Level = job.Level, ScopeBox = job.Box, Tag = job.Tag };
                            var pr = Core.Drawing.DrawingProducer.ProduceAllViews(doc, job.Type, dctx, opts);
                            warnings.AddRange(pr.Warnings);
                            if (t.Commit() == TransactionStatus.Committed)
                            {
                                views += pr.ViewIds.Count;
                                if (pr.SheetId != ElementId.InvalidElementId && !pr.SheetReused) sheets++;
                            }
                            else warnings.Add($"{job.Box.Name} / {job.Level.Name} ({job.Type.Id}): the transaction did not commit.");
                        }
                        catch (Exception ex)
                        {
                            if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                            warnings.Add($"{job.Box.Name} / {job.Level.Name} ({job.Type.Id}): {ex.Message} — rolled back.");
                        }
                    }
                }
                tg.Assimilate();
            }

            detail.AppendLine($"      {views} view(s) from {boxes.Count} scope box(es) (existing stamped views reused), " +
                              $"{sheets} new sheet(s){(data.CreateSheets ? "" : " — sheets not requested")}.");
            AppendWarnings(detail, warnings, "dependents");
            return views > 0 ? Result.Succeeded : Result.Failed;
        }

        /// <summary>Levels a box's level segment names; every level when it names none.</summary>
        private static List<Level> MatchLevels(List<Level> levels, string segment)
        {
            if (string.IsNullOrWhiteSpace(segment)) return levels;
            string Squash(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray());
            return levels.Where(l =>
                    string.Equals(l.Name, segment, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Squash(l.Name), Squash(segment), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// "Create building sections from grids", through the drawing-type producer: one
        /// section along each straight grid line, of the section drawing type the
        /// architectural discipline routes to (structural if none). The context tag is
        /// "Grid-&lt;name&gt;", the one DOCS → Produce Sections uses, so either reuses
        /// the other's views rather than making a second set.
        /// </summary>
        private static Result ProduceGridSections(Document doc, ProjectSetupData data, StringBuilder detail)
        {
            var dt = RouteFirst(doc, "SECTION", "Section", "A", "S");
            if (dt == null)
            {
                detail.AppendLine("      No drawing type routes from A / SECTION or S / SECTION — nothing produced (add a routing rule).");
                return Result.Failed;
            }
            var grids = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>()
                .Where(g => g.Curve is Line)
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (grids.Count == 0)
            {
                detail.AppendLine("      No straight grid lines in the model — nothing to produce.");
                return Result.Cancelled;
            }
            var levelElevs = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .Select(l => l.Elevation).ToList();
            const double mToFt = 1.0 / 0.3048;
            double depthFt = new Core.Drawing.SectionProductionConfig().DepthMm / 304.8;

            var opts = new Core.Drawing.ProduceOptions
            {
                CreateSheet = data.CreateSheets,
                PlaceOnSheet = data.CreateSheets,
                RunAnnotation = true,
                Idempotent = true,
            };
            int views = 0, sheets = 0;
            var warnings = new List<string>();
            detail.AppendLine($"      A / SECTION → {dt.Id}");
            Core.Drawing.DrawingTypePresentation.Prewarm(doc);
            using (Core.Drawing.DrawingProducer.PrimeBatchScope(doc))
            using (var tg = new TransactionGroup(doc, "STING Project Setup — Sections"))
            {
                tg.Start();
                foreach (var g in grids)
                {
                    var c = (Line)g.Curve;
                    var a = c.GetEndPoint(0); var b = c.GetEndPoint(1);
                    var origin = (a + b) * 0.5;
                    double bottom = (levelElevs.Count > 0 ? levelElevs.Min() : origin.Z) - 3.0 * mToFt;
                    double top = Math.Max(origin.Z + 30.0 * mToFt,
                        (levelElevs.Count > 0 ? levelElevs.Max() : origin.Z) + 5.0 * mToFt);
                    var bb = Core.Drawing.DrawingProducer.BuildSectionBox(
                        origin: origin, cutDirection: c.Direction,
                        halfWidthFt: (b - a).GetLength() * 0.5 + 5.0 * mToFt,
                        bottomZ: bottom, topZ: top, depthFt: depthFt);

                    using (var t = new Transaction(doc, $"STING Section Grid-{g.Name}"))
                    {
                        t.Start();
                        try
                        {
                            var dctx = new Core.Drawing.DrawingContext { CustomBounds = bb, Tag = "Grid-" + g.Name };
                            var pr = Core.Drawing.DrawingProducer.ProduceAllViews(doc, dt, dctx, opts);
                            warnings.AddRange(pr.Warnings);
                            if (t.Commit() == TransactionStatus.Committed)
                            {
                                views += pr.ViewIds.Count;
                                if (pr.SheetId != ElementId.InvalidElementId && !pr.SheetReused) sheets++;
                            }
                            else warnings.Add($"Grid {g.Name}: the transaction did not commit.");
                        }
                        catch (Exception ex)
                        {
                            if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                            warnings.Add($"Grid {g.Name}: {ex.Message} — rolled back.");
                        }
                    }
                }
                tg.Assimilate();
            }

            detail.AppendLine($"      {views} section(s) along {grids.Count} grid line(s) (existing stamped views reused), " +
                              $"{sheets} new sheet(s){(data.CreateSheets ? "" : " — sheets not requested")}.");
            AppendWarnings(detail, warnings, "sections");
            return views > 0 ? Result.Succeeded : Result.Failed;
        }

        /// <summary>
        /// "Create 4 exterior elevations", as drawing-type views: the elevation type the
        /// architectural discipline routes to, one view per face (N / E / S / W) around
        /// the walls' extent. DTW-80: produced by the same routine DOCS → Exterior
        /// Elevations runs (ProduceExteriorElevationsCommand.Produce), views only, so each
        /// face carries the producer's own context tag "Exterior-&lt;Face&gt;" and either
        /// path reuses the other's views. A view stamped with the raw tag the wizard used
        /// to write ("exterior::face::&lt;Face&gt;") is adopted, not duplicated. Sheets for
        /// elevations are laid out by DOCS → Exterior Elevations.
        /// </summary>
        private static Result ProduceExteriorElevations(Document doc, StringBuilder detail)
        {
            var dt = RouteFirst(doc, "ELEVATION", "Elevation", "A");
            if (dt == null || (dt.Name ?? dt.Id ?? "").IndexOf("interior", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                detail.AppendLine("      No exterior elevation drawing type routes from A / ELEVATION — nothing produced (add a routing rule).");
                return Result.Failed;
            }
            // The markers' host: a level with a floor plan, the one nearest ground — the
            // rule DOCS → Exterior Elevations applies to the levels ticked there.
            var host = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && v.GenLevel != null)
                .Select(v => v.GenLevel)
                .OrderBy(l => Math.Abs(l.Elevation)).ThenBy(l => l.Elevation)
                .FirstOrDefault();
            if (host == null)
            {
                detail.AppendLine("      No floor plan to host the elevation markers — produce the plans first.");
                return Result.Failed;
            }

            var opts = new Core.Drawing.ProduceOptions
            {
                CreateSheet = false,
                PlaceOnSheet = false,
                RunAnnotation = true,
                Idempotent = true,
            };
            int views = 0, sheets = 0;
            var warnings = new List<string>();
            detail.AppendLine($"      A / ELEVATION → {dt.Id} (markers on {host.Name})");
            Core.Drawing.DrawingTypePresentation.Prewarm(doc);
            string blocker;
            using (Core.Drawing.DrawingProducer.PrimeBatchScope(doc))
                blocker = Commands.Drawing.ProduceExteriorElevationsCommand.Produce(
                    doc, new List<Core.Drawing.DrawingType> { dt }, host,
                    new Core.Drawing.ElevationProductionConfig(), opts, dt.PackageId,
                    ref views, ref sheets, warnings);
            if (blocker != null)
            {
                detail.AppendLine($"      {blocker}");
                AppendWarnings(detail, warnings, "elevations");
                return Result.Cancelled;
            }

            detail.AppendLine($"      {views} elevation view(s) (existing stamped views reused, not duplicated). " +
                              "Views only — lay out elevation sheets with DOCS → Exterior Elevations.");
            AppendWarnings(detail, warnings, "elevations");
            return views > 0 ? Result.Succeeded : Result.Failed;
        }

        /// <summary>The first drawing type (of <paramref name="purpose"/>) that one of the disciplines routes <paramref name="docType"/> to.</summary>
        private static Core.Drawing.DrawingType RouteFirst(Document doc, string docType, string purpose, params string[] disciplines)
        {
            foreach (var disc in disciplines)
            {
                Core.Drawing.DrawingType dt = null;
                try { dt = Core.Drawing.DrawingDispatcher.Resolve(doc, disc, null, docType); }
                catch (Exception ex) { StingLog.Warn($"Project Setup route {disc}/{docType}: {ex.Message}"); }
                if (dt != null && string.Equals(dt.Purpose, purpose, StringComparison.OrdinalIgnoreCase)) return dt;
            }
            return null;
        }

        private static void AppendWarnings(StringBuilder detail, List<string> warnings, string what)
        {
            var distinct = warnings.Distinct().ToList();
            foreach (var w in distinct) StingLog.Warn($"Project Setup {what}: {w}");
            if (distinct.Count == 0) return;
            detail.AppendLine($"      {distinct.Count} warning(s):");
            foreach (var w in distinct.Take(8)) detail.AppendLine($"        • {w}");
            if (distinct.Count > 8) detail.AppendLine($"        • … {distinct.Count - 8} more in the STING log");
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

        // ══════════════════════════════════════════════════════════════
        // Title block + performance helpers
        // ══════════════════════════════════════════════════════════════

        /// <summary>Quick check: does the document contain any instances of the primary
        /// taggable MEP/architectural categories? Used to short-circuit the very expensive
        /// FullAutoPopulate step on a fresh project.</summary>
        private static bool HasNoTaggableInstances(Document doc)
        {
            try
            {
                // Sample a handful of representative categories — full list is 22 BuiltInCategories
                // but any one of these is a good signal the model has real content.
                var bics = new[]
                {
                    BuiltInCategory.OST_Walls,
                    BuiltInCategory.OST_Doors,
                    BuiltInCategory.OST_DuctCurves,
                    BuiltInCategory.OST_PipeCurves,
                    BuiltInCategory.OST_LightingFixtures,
                    BuiltInCategory.OST_ElectricalFixtures,
                    BuiltInCategory.OST_Rooms
                };
                foreach (var bic in bics)
                {
                    int count = new FilteredElementCollector(doc)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .GetElementCount();
                    if (count > 0) return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"HasNoTaggableInstances: {ex.Message}");
                return false;
            }
        }

        /// <summary>Snapshot of existing document state for fast-mode skip decisions.</summary>
        private class DocPreScan
        {
            public int Materials;
            public int Schedules;
            public int Templates;
            public int Views;
        }

        /// <summary>Quick scan of the document so fast-mode can skip steps that are already done.</summary>
        private static DocPreScan PreScan(Document doc)
        {
            try
            {
                var scan = new DocPreScan
                {
                    Materials = new FilteredElementCollector(doc).OfClass(typeof(Material)).GetElementCount(),
                    Schedules = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule))
                        .Cast<ViewSchedule>().Count(vs => !vs.IsTemplate && !vs.IsTitleblockRevisionSchedule),
                    Templates = new FilteredElementCollector(doc).OfClass(typeof(View))
                        .Cast<View>().Count(v => v.IsTemplate),
                    Views = new FilteredElementCollector(doc).OfClass(typeof(View))
                        .Cast<View>().Count(v => !v.IsTemplate)
                };
                StingLog.Info($"ProjectSetup pre-scan: materials={scan.Materials}, schedules={scan.Schedules}, templates={scan.Templates}, views={scan.Views}");
                return scan;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ProjectSetup pre-scan failed: {ex.Message}");
                return new DocPreScan();
            }
        }

        /// <summary>Apply wizard title block selections to the document + TagConfig so
        /// downstream sheet-creation commands honour the user's choice. Activates selected
        /// title block symbols so they are ready to place.</summary>
        private static void ApplyTitleBlockSelections(Document doc, ProjectSetupData data)
        {
            try
            {
                var allTbs = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .Cast<FamilySymbol>()
                    .ToList();

                // Resolve "Family : Type" (from wizard dropdowns) → FamilySymbol.
                FamilySymbol Resolve(string familyAndType)
                {
                    if (string.IsNullOrEmpty(familyAndType)) return null;
                    return allTbs.FirstOrDefault(fs =>
                        string.Equals($"{fs.FamilyName} : {fs.Name}", familyAndType, StringComparison.OrdinalIgnoreCase));
                }

                // Build a disc → FamilySymbol map to activate + publish.
                var discMap = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
                if (data.TitleBlockByDiscipline != null)
                {
                    foreach (var kv in data.TitleBlockByDiscipline)
                    {
                        var fs = Resolve(kv.Value);
                        if (fs != null) discMap[kv.Key] = fs;
                    }
                }
                var defaultTb = Resolve(data.TitleBlockName);

                // Activate selected symbols so sheet creation can use them immediately.
                using (Transaction tx = new Transaction(doc, "STING Activate Title Blocks"))
                {
                    tx.Start();
                    foreach (var fs in discMap.Values.Concat(new[] { defaultTb }).Where(x => x != null).Distinct())
                    {
                        try { if (!fs.IsActive) fs.Activate(); }
                        catch (Exception ex) { StingLog.Warn($"Activate title block '{fs.FamilyName}': {ex.Message}"); }
                    }
                    tx.Commit();
                }

                // Publish to TagConfig for downstream sheet-creation commands.
                if (defaultTb != null)
                    TagConfig.PreferredTitleBlockFamily = defaultTb.FamilyName;
                TitleBlockRouter.ByDiscipline = discMap.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.Id,
                    StringComparer.OrdinalIgnoreCase);
                TitleBlockRouter.DefaultId = defaultTb?.Id ?? ElementId.InvalidElementId;

                StingLog.Info($"Title block routing: default='{defaultTb?.FamilyName ?? "(first available)"}', " +
                              $"disc-overrides={discMap.Count}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ApplyTitleBlockSelections failed: {ex.Message}");
            }
        }

        /// <summary>Switch the active view to a lightweight drafting view so Revit does not
        /// regenerate the active view on every transaction. Returns the original view so
        /// it can be restored at the end of Execute.</summary>
        private static View TrySuspendUI(UIDocument uidoc)
        {
            try
            {
                Document doc = uidoc.Document;
                View original = uidoc.ActiveView;
                if (original == null || original is ViewSchedule) return original;

                // Find or create a hidden drafting view to park on during setup.
                ViewDrafting parking = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewDrafting))
                    .Cast<ViewDrafting>()
                    .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, "STING_Setup_Parking", StringComparison.OrdinalIgnoreCase));

                if (parking == null)
                {
                    ViewFamilyType vft = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);
                    if (vft == null) return original;

                    using (Transaction tx = new Transaction(doc, "STING Create Setup Parking View"))
                    {
                        tx.Start();
                        parking = ViewDrafting.Create(doc, vft.Id);
                        try { parking.Name = "STING_Setup_Parking"; } catch { }
                        tx.Commit();
                    }
                }

                uidoc.ActiveView = parking;
                StingLog.Info($"UI suspended: parked on '{parking.Name}', will restore '{original.Name}' at end");
                return original;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TrySuspendUI failed: {ex.Message}");
                return null;
            }
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
                // Failed says FAILED: a required workflow step or a refused write is not a warning.
                string status = result == Result.Succeeded ? "OK" : result == Result.Failed ? "FAILED" : "WARN";
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
