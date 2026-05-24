using System;
using System.Diagnostics;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

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
    /// Each step runs in its own transaction with timing information.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MasterSetupCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try { return ExecuteCore(commandData, ref message, elements); }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("MasterSetupCommand crashed", ex);
                try { TaskDialog.Show("STING Tools", $"Master Setup failed:\n{ex.Message}"); } catch (Exception ex2) { StingLog.Warn($"TaskDialog fallback: {ex2.Message}"); }
                return Result.Failed;
            }
        }

        private Result ExecuteCore(ExternalCommandData commandData,
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
                " 17.  Auto-create legends (discipline + system)\n" +
                " 18.  Generate BEP + Export XLSX (ISO 19650)\n\n" +
                "Each step runs independently.\n" +
                "Use Ctrl+Z to undo individual steps if needed.\n\n" +
                "This may take several minutes for a new project.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // AE-03: Idempotency check — warn if Master Setup was already run on this project
            try
            {
                string prevTimestamp = ParameterHelpers.GetString(doc.ProjectInformation, "STING_MASTER_SETUP_TS");
                if (!string.IsNullOrEmpty(prevTimestamp))
                {
                    TaskDialog idempotencyDlg = new TaskDialog("STING Master Setup");
                    idempotencyDlg.MainInstruction = "Master Setup was previously run";
                    idempotencyDlg.MainContent =
                        $"Last run: {prevTimestamp}\n\n" +
                        "Running again will re-apply all setup steps.\n" +
                        "Already-existing items (parameters, materials, types) will be skipped,\n" +
                        "but tags and schedules may be regenerated.\n\n" +
                        "Continue anyway?";
                    idempotencyDlg.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                    if (idempotencyDlg.Show() == TaskDialogResult.No)
                        return Result.Cancelled;
                }
            }
            catch (Exception ex) { StingLog.Warn($"AE-03: Idempotency check: {ex.Message}"); }

            StingLog.Info("Master Setup: starting full automation workflow");
            var report = new StringBuilder();
            report.AppendLine("STING Master Setup Results");
            report.AppendLine(new string('═', 45));

            int stepNum = 0;
            int passed = 0;
            int failed = 0;
            int skipped = 0;
            var totalSw = Stopwatch.StartNew();
            using var _perfOp = PerformanceTracker.Track("MasterSetup");

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
                // TEMP-03: Log as WARNING (not just Info) so users know defaults are active
                StingLog.Warn("Master Setup: project_config.json not found — using built-in defaults. " +
                    "Run 'Project Setup Wizard' to create project-specific configuration.");
                report.AppendLine($"   0. Load project_config.json — WARNING (not found, using defaults)");
                report.AppendLine($"      Run 'Project Setup Wizard' first for project-specific LOC/ZONE codes");
            }

            // Step 1: Load shared parameters (critical — other steps depend on it)
            passed += RunStep(ref stepNum, report, "Load Shared Parameters",
                () => RunCommand(new Tags.LoadSharedParamsCommand(), commandData, elements));

            // If parameter loading failed, abort
            if (passed == 0)
            {
                StingLog.Error("Master Setup: critical step 1 failed — aborting");
                TaskDialog critFail = new TaskDialog("Master Setup — Critical Failure");
                critFail.MainInstruction = "Shared parameter loading failed";
                critFail.MainContent =
                    "Step 1 (Load Shared Parameters) failed. Subsequent steps\n" +
                    "depend on these parameters. Continue anyway or stop?";
                critFail.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Continue anyway", "Attempt remaining steps despite the failure");
                critFail.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Stop", "Abort setup (use Ctrl+Z to undo parameter binding)");
                if (critFail.Show() == TaskDialogResult.CommandLink2)
                {
                    TaskDialog.Show("Master Setup", "Setup aborted. Use Ctrl+Z to undo.");
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
                if (result == -2) { skipped++; return 0; }
                return result;
            }

            // Step 2: Create BLE Materials
            passed += DoStep("Create BLE Materials",
                () => RunCommand(new CreateBLEMaterialsCommand(), commandData, elements));

            // Step 3: Create MEP Materials
            passed += DoStep("Create MEP Materials",
                () => RunCommand(new CreateMEPMaterialsCommand(), commandData, elements));

            // Step 4: Create compound types (Walls, Floors, Ceilings, Roofs)
            passed += DoStep("Create Wall Types",
                () => RunCommand(new CreateWallsCommand(), commandData, elements));
            passed += DoStep("Create Floor Types",
                () => RunCommand(new CreateFloorsCommand(), commandData, elements));
            passed += DoStep("Create Ceiling Types",
                () => RunCommand(new CreateCeilingsCommand(), commandData, elements));
            passed += DoStep("Create Roof Types",
                () => RunCommand(new CreateRoofsCommand(), commandData, elements));

            // Step 5: Create MEP types (Ducts, Pipes)
            passed += DoStep("Create Duct Types",
                () => RunCommand(new CreateDuctsCommand(), commandData, elements));
            passed += DoStep("Create Pipe Types",
                () => RunCommand(new CreatePipesCommand(), commandData, elements));

            // Step 6: Batch create schedules
            passed += DoStep("Batch Create Schedules",
                () => RunCommand(new BatchSchedulesCommand(), commandData, elements));

            // Step 7: Evaluate formulas (199 dependency-ordered formulas)
            passed += DoStep("Evaluate Formulas (199 definitions)",
                () => RunCommand(new FormulaEvaluatorCommand(), commandData, elements));

            // Step 8: Tag & Combine (full pipeline: populate + tag + combine + TAG7 narrative)
            passed += DoStep("Tag & Combine (full pipeline + TAG7)",
                () => RunCommand(new Tags.TagAndCombineCommand(), commandData, elements));

            // Step 9: Create view filters
            passed += DoStep("Create View Filters",
                () => RunCommand(new CreateFiltersCommand(), commandData, elements));

            // Step 10: Create worksets (only if worksharing enabled)
            if (doc.IsWorkshared)
            {
                passed += DoStep("Create Worksets",
                    () => RunCommand(new CreateWorksetsCommand(), commandData, elements));
            }
            else if (!userCancelled)
            {
                stepNum++;
                report.AppendLine($"  {stepNum,2}. Create Worksets — SKIPPED (not workshared)");
            }

            // Step 11: Create view templates
            passed += DoStep("Create View Templates",
                () => RunCommand(new ViewTemplatesCommand(), commandData, elements));

            // Step 12: Fill patterns + line styles + object styles
            passed += DoStep("Create Fill Patterns",
                () => RunCommand(new CreateFillPatternsCommand(), commandData, elements));
            passed += DoStep("Create Line Styles",
                () => RunCommand(new CreateLineStylesCommand(), commandData, elements));
            passed += DoStep("Configure Object Styles",
                () => RunCommand(new CreateObjectStylesCommand(), commandData, elements));

            // Step 13: Text styles + dimension styles
            passed += DoStep("Create Text Styles",
                () => RunCommand(new CreateTextStylesCommand(), commandData, elements));
            passed += DoStep("Create Dimension Styles",
                () => RunCommand(new CreateDimensionStylesCommand(), commandData, elements));

            // Step 14: Apply filters to templates + VG overrides
            passed += DoStep("Apply Filters to Templates",
                () => RunCommand(new ApplyFiltersToViewsCommand(), commandData, elements));
            passed += DoStep("Apply VG Overrides (5-layer)",
                () => RunCommand(new CreateVGOverridesCommand(), commandData, elements));

            // Step 15: Batch family parameters from CSV
            passed += DoStep("Batch Family Params (CSV-driven)",
                () => RunCommand(new BatchAddFamilyParamsCommand(), commandData, elements));

            // Step 16: Auto-assign templates + auto-fix
            passed += DoStep("Auto-Assign Templates (5-layer)",
                () => RunCommand(new AutoAssignTemplatesCommand(), commandData, elements));
            passed += DoStep("Auto-Fix Template Health",
                () => RunCommand(new AutoFixTemplateCommand(), commandData, elements));

            // Step 17: Auto-create legends (discipline, system, filter)
            passed += DoStep("Auto-Create Legends (discipline + system)",
                () => RunCommand(new Tags.AutoCreateLegendsCommand(), commandData, elements));

            // Step 18: Tag sheets with ISO 19650 document codes
            passed += DoStep("Tag Sheets (ISO 19650 doc codes)",
                () => RunCommand(new Tags.TagSheetsCommand(), commandData, elements));

            // Step 19: Generate BEP (ISO 19650 BIM Execution Plan)
            passed += DoStep("Generate BEP + Export XLSX",
                () => RunCommand(new BIMManager.CreateBEPCommand(), commandData, elements));

            // Step 20: Healthcare Pack setup (HC-09) — only runs if facility type profile is set.
            try
            {
                var hcDoc = commandData?.Application?.ActiveUIDocument?.Document;
                var pi = hcDoc?.ProjectInformation;
                var healthProfile = pi?.LookupParameter("PRJ_ORG_HEALTH_PACK_PROFILE_TXT")?.AsString();
                if (!string.IsNullOrEmpty(healthProfile))
                {
                    StingLog.Info($"MasterSetup Step 20: Healthcare Pack detected (profile={healthProfile}); loading shared params + COBie healthcare overlay.");
                    passed += DoStep($"Load Healthcare Shared Params (profile: {healthProfile})",
                        () => RunCommand(new Tags.LoadSharedParamsCommand(), commandData, elements));

                    // Apply COBie healthcare overlay using the HEALTHCARE_NHS or HEALTHCARE_PRIVATE preset
                    string cobiePreset = healthProfile.StartsWith("PRIVATE", StringComparison.OrdinalIgnoreCase)
                        ? "HEALTHCARE_PRIVATE"
                        : "HEALTHCARE_NHS";
                    StingLog.Info($"MasterSetup Step 20: applying COBie preset '{cobiePreset}'");
                    passed += DoStep($"COBie Healthcare Overlay ({cobiePreset})",
                        () =>
                        {
                            var cmd = new BIMManager.COBieExportCommand();
                            StingCommandHandler.SetExtraParam("COBiePresetKey", cobiePreset);
                            return RunCommand(cmd, commandData, elements);
                        });
                }
                else
                {
                    StingLog.Info("MasterSetup Step 20: PRJ_ORG_HEALTH_PACK_PROFILE_TXT not set — skipping Healthcare Pack steps.");
                    skipped++;
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("MasterSetup Step 20 (Healthcare Pack) failed", ex);
            }

            // Step 21: PBR texture pipeline — seed `_BIM_COORD/textures/`
            // and surface the provider catalogue so authors can drop packs
            // or use Pbr_BrowseLibrary immediately.
            try
            {
                stepNum++;
                string tex = StingTools.Core.Materials.Providers.TextureProviderRegistry.ProjectTexturesRoot(doc);
                if (!string.IsNullOrEmpty(tex))
                {
                    // Template provider-override JSON so authors can layer
                    // a custom in-house library without writing the schema
                    // from scratch. Only seed if absent (don't clobber).
                    string bimCoord = System.IO.Directory.GetParent(tex)?.FullName;
                    if (!string.IsNullOrEmpty(bimCoord))
                    {
                        string providerOverridePath = System.IO.Path.Combine(bimCoord, "texture_providers.json");
                        if (!System.IO.File.Exists(providerOverridePath))
                        {
                            System.IO.File.WriteAllText(providerOverridePath,
                                "{\n" +
                                "  \"_doc\": \"STING project-scoped texture provider override. Entries match the corporate STING_TEXTURE_PROVIDERS.json by id (yours wins). Suffix-rule lists merge (your suffixes append). Reload via the dock panel's 'Reload Providers' button.\",\n" +
                                "  \"providers\": [\n" +
                                "    /* Example — uncomment + edit to add an in-house library:\n" +
                                "    {\n" +
                                "      \"id\": \"in-house\",\n" +
                                "      \"name\": \"In-house textures\",\n" +
                                "      \"kind\": \"folder-watch\",\n" +
                                "      \"license\": \"proprietary\",\n" +
                                "      \"cost\": \"internal\",\n" +
                                "      \"description\": \"Drop packs into _BIM_COORD/textures/in-house/. STING auto-detects PBR maps by suffix.\",\n" +
                                "      \"ingestFolder\": \"in-house\",\n" +
                                "      \"enabledByDefault\": true\n" +
                                "    }\n" +
                                "    */\n" +
                                "  ],\n" +
                                "  \"mapSuffixRules\": {\n" +
                                "    /* Example — uncomment + edit to add project-specific suffix conventions:\n" +
                                "    \"baseColor\": [\"_color\", \"_dif\"]\n" +
                                "    */\n" +
                                "  }\n" +
                                "}\n");
                            StingLog.Info($"MasterSetup Step 21: seeded provider override template at {providerOverridePath}");
                        }
                    }
                    string readme = System.IO.Path.Combine(tex, "README.txt");
                    if (!System.IO.File.Exists(readme))
                    {
                        System.IO.File.WriteAllText(readme,
                            "STING PBR Texture Drop Zone\n" +
                            "===========================\n\n" +
                            "Drop pack folders here. Folder name should match the Revit material\n" +
                            "name for the 'Bulk Apply PBR' command to auto-match. Suffix\n" +
                            "conventions recognised by the ingester include:\n" +
                            "  _basecolor / _albedo / _diffuse  → base color\n" +
                            "  _normal / _norm / _nrm           → normal\n" +
                            "  _roughness / _rough / _rgh       → roughness\n" +
                            "  _metalness / _metallic / _metal  → metalness\n" +
                            "  _ao / _ambientocclusion          → AO\n" +
                            "  _bump / _bmp / _height           → bump\n" +
                            "  _displacement / _disp            → displacement\n" +
                            "  _opacity / _alpha / _mask        → opacity\n" +
                            "  _emission / _emissive / _emit    → emission\n\n" +
                            "Use 'Browse PBR library…' in the Material Hub to pull CC0 packs\n" +
                            "from Poly Haven or ambientCG directly into this folder.\n");
                    }
                    report.AppendLine($"  21. PBR textures folder ready — {tex}");
                    passed++;
                    StingLog.Info($"MasterSetup Step 21: PBR textures root → {tex}");
                }
                else
                {
                    report.AppendLine($"  21. PBR textures — SKIPPED (project not saved)");
                    skipped++;
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("MasterSetup Step 21 (PBR textures) failed", ex);
                report.AppendLine($"  21. PBR textures — FAILED ({ex.Message})");
            }

            // Handle user cancellation
            if (userCancelled)
            {
                totalSw.Stop();
                StingLog.Info($"Master Setup: cancelled by user at step {stepNum}");
                report.AppendLine(new string('─', 45));
                report.AppendLine($"  CANCELLED at step {stepNum} ({passed} completed)");
                TaskDialog.Show("Master Setup",
                    report.ToString() + "\n\nCompleted steps are committed.\nUse Ctrl+Z to undo.");
                return passed > 0 ? Result.Succeeded : Result.Cancelled;
            }

            failed = stepNum - passed - skipped;
            totalSw.Stop();
            report.AppendLine(new string('─', 45));
            report.AppendLine($"  Complete: {passed}/{stepNum} steps succeeded");
            report.AppendLine($"  Duration: {totalSw.Elapsed.TotalSeconds:F1}s");
            if (skipped > 0)
                report.AppendLine($"  Skipped: {skipped} (optional steps not applicable)");
            if (failed > 0)
                report.AppendLine($"  Failed: {failed} — check StingTools.log for details");

            // GAP-1C: Run post-setup validation to catch configuration issues
            try
            {
                var validator = new ValidateTemplateCommand();
                string valMsg = "";
                var valResult = validator.Execute(commandData, ref valMsg, elements);
                string valStatus = valResult == Result.Succeeded ? "PASSED" : "ISSUES DETECTED";
                report.AppendLine($"  Post-validation: {valStatus}");
                StingLog.Info($"Master Setup: post-setup validation {valStatus}");
            }
            catch (Exception valEx)
            {
                StingLog.Error($"Master Setup post-validation failed: {valEx.Message}");
                report.AppendLine($"  Post-validation: skipped ({valEx.Message})");
            }

            var panel = UI.StingResultPanel.Create("Master Setup Complete")
                .SetSubtitle($"{passed}/{stepNum} steps succeeded in {totalSw.Elapsed.TotalSeconds:F1}s")
                .SetOverallPct(stepNum > 0 ? passed * 100.0 / stepNum : 0)
                .SetRawText(report.ToString());

            panel.AddSection("SETUP RESULTS")
                .Metric("Steps completed", $"{passed}/{stepNum}")
                .Metric("Duration", $"{totalSw.Elapsed.TotalSeconds:F1}s");
            if (failed > 0)
                panel.MetricError("Failed steps", failed.ToString(), "check StingTools.log");
            else
                panel.Info("All steps completed successfully.");

            panel.Show();

            StingLog.Info($"Master Setup complete: {passed}/{stepNum} passed, " +
                $"elapsed={totalSw.Elapsed.TotalSeconds:F1}s");

            // AE-03: Write setup timestamp for idempotency detection
            try
            {
                using (var tsTx = new Transaction(doc, "STING Master Setup Timestamp"))
                {
                    tsTx.Start();
                    ParameterHelpers.SetString(doc.ProjectInformation, "STING_MASTER_SETUP_TS",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), overwrite: true);
                    tsTx.Commit();
                }
            }
            catch (Exception ex) { StingLog.Warn($"AE-03: Timestamp write: {ex.Message}"); }

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
                string status = result == Result.Succeeded ? "OK"
                    : result == Result.Cancelled ? "SKIPPED"
                    : "WARN";
                report.AppendLine($"  {stepNum,2}. {label} — {status} ({sw.Elapsed.TotalSeconds:F1}s)");
                StingLog.Info($"Master Setup step {stepNum}: {label} — {status} ({sw.Elapsed.TotalSeconds:F1}s)");
                return result == Result.Succeeded ? 1
                    : result == Result.Cancelled ? -2  // Distinguish SKIPPED from FAILED
                    : 0;
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
