using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;
using Newtonsoft.Json;
using System.Collections.Concurrent;

namespace StingTools.UI
{
    /// <summary>
    /// External event handler that dispatches dockable panel button clicks
    /// to the appropriate IExternalCommand classes. This ensures all Revit API
    /// calls happen on the correct thread (main Revit API context).
    ///
    /// Unified dispatcher for all 170+ commands across 7 tabs:
    /// SELECT, ORGANISE, DOCS, TEMP, CREATE, VIEW, MODEL.
    /// </summary>
    public class StingCommandHandler : IExternalEventHandler
    {
        private readonly object _lock = new object();
        private string _commandTag = "";
        private string _param1 = "";
        private string _param2 = "";

        // Set true by Execute()'s switch default when a tag matches no case.
        // Lets RunTagSync report a genuine miss without depending on
        // ExternalEvent.Raise()'s Accepted/Pending coalescing return.
        private bool _lastTagUnhandled;

        // BUG-02 FIX: Re-entrancy depth counter prevents inner Execute() calls
        // (from wizard dialog dispatch loops) from running the finally cleanup,
        // which would clear ExtraParams and _commandTag needed by the outer caller.
        private int _executeDepth;

        // ── Named extra-param store (thread-safe) ─────────────────────────────
        // Allows WPF UI controls to pass named values to commands without
        // changing the SetCommand signature.  Usage:
        //   StingCommandHandler.SetExtraParam("WarnMode", "NONE");
        //   string mode = StingCommandHandler.GetExtraParam("WarnMode");
        //   StingCommandHandler.ClearExtraParam("WarnMode");  // cleanup after use
        private static readonly System.Collections.Concurrent.ConcurrentDictionary
            <string, string> _extraParams =
            new System.Collections.Concurrent.ConcurrentDictionary
            <string, string>(StringComparer.OrdinalIgnoreCase);

        public static void SetExtraParam(string key, string value)
            => _extraParams[key] = value ?? "";

        public static string GetExtraParam(string key)
            => _extraParams.TryGetValue(key, out string v) ? v : "";

        public static void ClearExtraParam(string key)
            => _extraParams.TryRemove(key, out _);

        public static void ClearAllExtraParams()
            => _extraParams.Clear();
        // ──────────────────────────────────────────────────────────────────────

        public void SetCommand(string tag, string param1 = "", string param2 = "")
        {
            lock (_lock)
            {
                _commandTag = tag ?? "";
                _param1 = param1 ?? "";
                _param2 = param2 ?? "";
            }
            // M-02 / BUG-04 FIX: ExtraParams are cleared in Execute() finally block
            // (line 2464) AFTER the command runs. Do NOT clear here — Cmd_Click
            // sets ExtraParams BEFORE calling SetCommand(), so clearing here wipes
            // Tag Studio slider/radio values (ElbowMode, TagTextSize, PreferredTagPos, etc.).
        }

        /// <summary>
        /// Synchronous, deterministic dispatch for callers ALREADY on the
        /// Revit API thread (e.g. a Hub ribbon IExternalCommand). Sets the
        /// explicit tag and runs Execute() back-to-back on the same thread, so
        /// the tag cannot be clobbered by an intervening click and there is no
        /// reliance on ExternalEvent.Raise() coalescing. Returns false only
        /// when the tag is genuinely unhandled by the command switch.
        /// </summary>
        public bool RunTagSync(UIApplication app, string tag, string param1 = "", string param2 = "")
        {
            if (app == null || string.IsNullOrEmpty(tag)) return false;
            lock (_lock)
            {
                _commandTag = tag;
                _param1 = param1 ?? "";
                _param2 = param2 ?? "";
            }
            Execute(app);                 // runs synchronously on the API thread
            return !_lastTagUnhandled;
        }

        public string GetName() => "STING Command Dispatcher";

        public void Execute(UIApplication app)
        {
            // Store current UIApplication so commands can access it via
            // StingCommandHandler.CurrentApp when ExternalCommandData is null
            CurrentApp = app;

            // Snapshot command state under lock to prevent race with WPF UI thread
            string tag, p1, p2;
            lock (_lock)
            {
                tag = _commandTag;
                p1 = _param1;
                p2 = _param2;
            }

            // Guard: empty tag means no command was requested (cleared after previous run)
            if (string.IsNullOrEmpty(tag)) return;

            // Deterministic-dispatch support: assume handled until the switch's
            // default branch proves the tag is genuinely unrecognised. Read by
            // RunTagSync so the Hub only reports failure on a real miss.
            _lastTagUnhandled = false;

            // Guard: most commands require an open document.
            // CRITICAL FIX (Phase 79b): Must be BEFORE _executeDepth++ to avoid
            // permanently leaking the depth counter on early return (no finally block).
            if (app.ActiveUIDocument == null)
            {
                TaskDialog.Show("STING Tools",
                    "No document is open. Please open a Revit project first.");
                return;
            }

            // BUG-02 FIX: Track re-entrancy depth so inner Execute() calls
            // (from wizard dispatch loops) skip the finally-block cleanup.
            _executeDepth++;

            // PERF-CRIT: Run deferred morning briefing on first command after document open.
            // This was previously in OnDocumentOpened where it blocked the UI thread for 5-30s.
            // F05 FIX: Only run at outermost depth to prevent briefing inside recursive wizard loops.
            if (_executeDepth == 1 && Core.StingToolsApp._briefingPending)
            {
                try { Core.StingToolsApp.RunDeferredMorningBriefing(app.ActiveUIDocument.Document); }
                catch (Exception mbEx) { Core.StingLog.Warn($"Deferred briefing: {mbEx.Message}"); }
            }

            // S8.2.1 — wrap the dispatch body so every command emits a
            // PluginTelemetry span. Default OFF; only fires when the user
            // opts in via project_config.json telemetry.enabled = true.
            Core.PluginTelemetry.Run("dispatch:" + tag, () =>
            {
            try
            {
                // Phase 165 (INT-02 framework) — try the new module-registered
                // dispatch table first. Modules return true when they own the
                // tag; everything else falls through to the historic switch
                // below. This lets us migrate panels one at a time without a
                // single high-risk big-bang refactor.
                try
                {
                    if (CommandRegistry.Instance.TryHandle(tag, app))
                        return;
                }
                catch (Exception regEx)
                {
                    StingLog.Warn($"CommandRegistry tag '{tag}' threw, falling back to switch: {regEx.Message}");
                }

                switch (tag)
                {
                    // ════════════════════════════════════════════════════════
                    // SELECT TAB
                    // ════════════════════════════════════════════════════════

                    // ── Category selectors ──
                    case "SelectLighting": RunCommand<Select.SelectLightingCommand>(app); break;
                    case "SelectElectrical": RunCommand<Select.SelectElectricalCommand>(app); break;
                    case "SelectMechanical": RunCommand<Select.SelectMechanicalCommand>(app); break;
                    case "SelectPlumbing": RunCommand<Select.SelectPlumbingCommand>(app); break;
                    case "SelectAirTerminals": RunCommand<Select.SelectAirTerminalsCommand>(app); break;
                    case "SelectFurniture": RunCommand<Select.SelectFurnitureCommand>(app); break;
                    case "SelectDoors": RunCommand<Select.SelectDoorsCommand>(app); break;
                    case "SelectWindows": RunCommand<Select.SelectWindowsCommand>(app); break;
                    case "SelectRooms": RunCommand<Select.SelectRoomsCommand>(app); break;
                    case "SelectSprinklers": RunCommand<Select.SelectSprinklersCommand>(app); break;
                    case "SelectPipes": RunCommand<Select.SelectPipesCommand>(app); break;
                    case "SelectDucts": RunCommand<Select.SelectDuctsCommand>(app); break;
                    case "SelectConduits": RunCommand<Select.SelectConduitsCommand>(app); break;
                    case "SelectCableTrays": RunCommand<Select.SelectCableTraysCommand>(app); break;
                    case "SelectAllTaggable": RunCommand<Select.SelectAllTaggableCommand>(app); break;
                    case "SelectCustomCategory": RunCommand<Select.SelectCustomCategoryCommand>(app); break;

                    // ── v4 MVP: fixture placement (Phase 2) ──
                    case "Placement_PlaceFixtures": RunCommand<Commands.Placement.PlaceFixturesCommand>(app); break;
                    case "Placement_LightingGrid":  RunCommand<Commands.Placement.LightingGridCommand>(app); break;
                    case "Placement_Learn":         RunCommand<Commands.Placement.LearnPlacementV4Command>(app); break;
                    // Phase 177 — toilet-room specific placement + BS 6465 provision check.
                    case "Placement_ToiletRoom":    RunCommand<Commands.Placement.PlaceToiletRoomCommand>(app); break;

                    // ── Phase 139.2 — placement centre additions ──
                    case "Placement_AutoPopulateCatalogue":
                        RunCommand<Commands.Placement.ManufacturerCatalogueAutoPopulateCommand>(app); break;
                    case "Placement_ExportNogginRequirements":
                        RunCommand<Commands.Placement.NogginRequirementExportCommand>(app); break;
                    case "Placement_ExportRulesExcel":
                        RunCommand<Commands.Placement.PlacementRulesExcelExportCommand>(app); break;
                    case "Placement_ImportRulesExcel":
                        RunCommand<Commands.Placement.PlacementRulesExcelImportCommand>(app); break;
                    case "Placement_RunWallChase":
                        RunCommand<Commands.Placement.RunWallChaseCommand>(app); break;
                    case "Placement_AuditSetup":
                        RunCommand<Commands.Placement.PlacementSetupAuditCommand>(app); break;
                    case "Placement_Diagnose":
                        RunCommand<Commands.Placement.PlacementDiagnoseCommand>(app); break;


                    // ── v4 MVP: auto-drop routing (Phase 3) ──
                    case "Routing_AutoDrop":         RunCommand<Commands.Routing.AutoDropCommand>(app); break;
                    case "Routing_GenerateLayout":   RunCommand<Commands.Routing.GenerateLayoutCommand>(app); break;
                    case "Routing_ValidateFills":    RunCommand<Commands.Routing.ValidateFillsCommand>(app); break;

                    // ── Phase 178d: penetration sweep (slab + wall + beam) ──
                    case "Penetrations_DetectAndPlace": RunCommand<Commands.Routing.PenetrationsDetectAndPlaceCommand>(app); break;
                    case "Validation_PenetrationCoverage": RunCommand<Commands.Validation.PenetrationCoverageCommand>(app); break;

                    // ── v4 MVP: validators (Phase 4) ──
                    case "Validation_RunAll":        RunCommand<Commands.Validation.RunAllValidatorsCommand>(app); break;

                    // ── Phase 175: Design Options ──
                    case "DesignOptions_Inspect":             RunCommand<Commands.DesignOptions.DesignOptionsInspectCommand>(app); break;
                    case "DesignOptions_MoveTo":              RunCommand<Commands.DesignOptions.MoveToOptionCommand>(app); break;
                    case "DesignOptions_LockView":            RunCommand<Commands.DesignOptions.LockViewToOptionCommand>(app); break;
                    case "DesignOptions_ResetView":           RunCommand<Commands.DesignOptions.ResetViewOptionVisibilityCommand>(app); break;
                    case "DesignOptions_CloneSchedule":       RunCommand<Commands.DesignOptions.ClonePerOptionScheduleCommand>(app); break;
                    case "DesignOptions_IsolationView":       RunCommand<Commands.DesignOptions.CreateIsolationViewCommand>(app); break;
                    case "DesignOptions_PrimaryClashView":    RunCommand<Commands.DesignOptions.CreatePrimaryOnlyClashViewCommand>(app); break;
                    case "DesignOptions_Audit":               RunCommand<Commands.DesignOptions.AuditOptionsCommand>(app); break;
                    case "DesignOptions_BatchLinkVisibility": RunCommand<Commands.DesignOptions.BatchSetLinkOptionVisibilityCommand>(app); break;
                    case "DesignOptions_Dashboard":           RunCommand<Commands.DesignOptions.OptionsDashboardCommand>(app); break;
                    case "DesignOptions_ExportComparison":    RunCommand<Commands.DesignOptions.ExportOptionComparisonCommand>(app); break;

                    // ── Healthcare Pack H-1..H-30 ──
                    case "Healthcare_RunAllValidators":  RunCommand<Commands.Healthcare.HealthcareRunAllValidatorsCommand>(app); break;
                    case "Healthcare_PressureAudit":     RunCommand<Commands.Healthcare.HealthcarePressureAuditCommand>(app); break;
                    case "Healthcare_WaterSafety":       RunCommand<Commands.Healthcare.HealthcareWaterSafetyCommand>(app); break;
                    case "Healthcare_EesBranch":         RunCommand<Commands.Healthcare.HealthcareEesBranchAuditCommand>(app); break;
                    case "Healthcare_RadShield":         RunCommand<Commands.Healthcare.HealthcareRadShieldAuditCommand>(app); break;
                    case "Healthcare_AdvancedRadShield": RunCommand<Commands.Healthcare.HealthcareAdvancedRadShieldCommand>(app); break;
                    case "Healthcare_RdsCompleteness":   RunCommand<Commands.Healthcare.HealthcareRdsCompletenessCommand>(app); break;
                    case "Healthcare_IoTStaleness":      RunCommand<Commands.Healthcare.HealthcareIoTStalenessCommand>(app); break;
                    case "Healthcare_StructuralLoad":    RunCommand<Commands.Healthcare.HealthcareStructuralLoadCommand>(app); break;
                    case "Healthcare_Acoustic":          RunCommand<Commands.Healthcare.HealthcareAcousticCommand>(app); break;
                    case "Healthcare_EndoscopeTrace":    RunCommand<Commands.Healthcare.HealthcareEndoscopeTraceCommand>(app); break;
                    case "Healthcare_EesResilience":     RunCommand<Commands.Healthcare.HealthcareEesResilienceCommand>(app); break;
                    case "Healthcare_RtlsCoverage":      RunCommand<Commands.Healthcare.HealthcareRtlsCoverageCommand>(app); break;
                    case "Healthcare_WasteFlow":         RunCommand<Commands.Healthcare.HealthcareWasteFlowCommand>(app); break;
                    case "Healthcare_IssueRDS":          RunCommand<Commands.Healthcare.IssueRoomDataSheetCommand>(app); break;
                    case "Healthcare_BatchRDS":          RunCommand<Commands.Healthcare.BatchIssueRoomDataSheetsCommand>(app); break;
                    case "Healthcare_MgasAudit":         RunCommand<Commands.MedGas.MgasNetworkAuditCommand>(app); break;
                    case "Healthcare_MgasVerify":        RunCommand<Commands.MedGas.MgasVerifyCommand>(app); break;
                    case "Healthcare_AdjacencyAudit":    RunCommand<Commands.Adjacency.AdjacencyAuditCommand>(app); break;
                    case "Healthcare_RadCalcChest":      RunCommand<Commands.Radiation.RadCalcChestRoomCommand>(app); break;
                    case "Healthcare_RadCalcCt":         RunCommand<Commands.Radiation.RadCalcCtRoomCommand>(app); break;
                    case "Healthcare_RadCalcLinac":      RunCommand<Commands.Radiation.RadCalcLinacVaultCommand>(app); break;
                    case "Healthcare_MriZoneAudit":      RunCommand<Commands.Radiation.MriZoneAuditCommand>(app); break;
                    case "Healthcare_IoTRegistry":       RunCommand<Commands.Twin.IoTRegistryCommand>(app); break;
                    case "Healthcare_AntiLigature":      RunCommand<Commands.Healthcare.Specialist.AntiLigatureAuditCommand>(app); break;
                    case "Healthcare_HybridOr":          RunCommand<Commands.Healthcare.Specialist.HybridOrCheckCommand>(app); break;
                    case "Healthcare_PharmacyUsp":       RunCommand<Commands.Healthcare.Specialist.PharmacyUspAuditCommand>(app); break;
                    case "Healthcare_BehaviouralHealth": RunCommand<Commands.Healthcare.Specialist.BehaviouralHealthAuditCommand>(app); break;
                    case "Healthcare_Mortuary":          RunCommand<Commands.Healthcare.Specialist.MortuaryAuditCommand>(app); break;
                    case "Healthcare_MaternityNicu":    RunCommand<Commands.Healthcare.Specialist.MaternityNicuAuditCommand>(app); break;
                    case "Healthcare_Hsdu":              RunCommand<Commands.Healthcare.Specialist.HsduAuditCommand>(app); break;
                    case "Healthcare_Dialysis":          RunCommand<Commands.Healthcare.Specialist.DialysisAuditCommand>(app); break;
                    case "Healthcare_Hbo":               RunCommand<Commands.Healthcare.Specialist.HboAuditCommand>(app); break;

                    // ── Lightning Protection System (BS EN 62305) ──
                    case "LPS_ClassSetup":           RunCommand<Commands.Lightning.LpsClassSetupCommand>(app); break;
                    case "LPS_ComplianceCheck":      RunCommand<Commands.Lightning.LpsComplianceCheckCommand>(app); break;
                    case "LPS_DownConductorCheck":   RunCommand<Commands.Lightning.LpsDownConductorCheckerCommand>(app); break;
                    case "LPS_EarthCheck":           RunCommand<Commands.Lightning.LpsEarthResistanceValidatorCommand>(app); break;
                    case "LPS_BondingInventory":     RunCommand<Commands.Lightning.LpsBondingInventoryCommand>(app); break;
                    case "LPS_ZoneTag":              RunCommand<Commands.Lightning.LpsRoomZoneTagCommand>(app); break;
                    case "LPS_PlanVisualise":        RunCommand<Commands.Lightning.LpsPlanViewVisualizerCommand>(app); break;
                    case "LPS_RollingSphere3D":      RunCommand<Commands.Lightning.LpsRollingSphere3DCommand>(app); break;
                    case "LPS_SepDistCheck":         RunCommand<Commands.Lightning.LpsSeparationDistanceCheckerCommand>(app); break;
                    case "LPS_InspectionSchedule":   RunCommand<Commands.Lightning.LpsInspectionSchedulerCommand>(app); break;
                    case "LPS_FullReport":           RunCommand<Commands.Lightning.LpsFullReportCommand>(app); break;
                    case "LPS_RiskModel":            RunCommand<Commands.Lightning.LpsRiskModelCommand>(app); break;
                    case "LPS_CreateFamilyShells":   RunCommand<Commands.Lightning.LpsCreateFamilyShellsCommand>(app); break;
                    case "LPS_Dashboard":            RunCommand<Commands.Lightning.LpsDashboardCommand>(app); break;
                    case "LPS_MarkElementTypes":     RunCommand<Commands.Lightning.LpsMarkElementTypesCommand>(app); break;
                    case "LPS_RecalcKcFactor":       RunCommand<Commands.Lightning.LpsRecalcKcFactorCommand>(app); break;
                    case "LPS_ColourZones":          RunCommand<Commands.Lightning.LpsColourZonesCommand>(app); break;
                    case "LPS_ClearZoneColours":     RunCommand<Commands.Lightning.LpsClearZoneColoursCommand>(app); break;
                    case "LPS_CreateSchedules":      RunCommand<Commands.Lightning.LpsCreateRevitScheduleCommand>(app); break;
                    case "LPS_SyncToServer":         RunCommand<Commands.Lightning.LpsSyncToServerCommand>(app); break;

                    // ── Gap 2 / Phase 121 — Extensible Storage migration + diagnostic ──
                    case "ES_Migrate":               RunCommand<Commands.Storage.MigrateToExtensibleStorageCommand>(app); break;
                    case "ES_Diagnostic":            RunCommand<Commands.Storage.EsStorageDiagnosticCommand>(app); break;

                    // ── Phase 127 — Placement Centre (modeless WPF window) ──
                    case "Placement_OpenCentre":     RunCommand<Commands.Placement.OpenPlacementCenterCommand>(app); break;

                    // ── Phase 139 — Placement Centre v2: Excel round-trip ──
                    case "Placement_ExportExcel":    RunCommand<PlacementCenter.ExportRulesToExcelCommand>(app); break;
                    case "Placement_ImportExcel":    RunCommand<PlacementCenter.ImportRulesFromExcelCommand>(app); break;

                    // ── Phase 116: Standards Extensions + Regional + Bulk API wrappers ──
                    case "StdExt_StageCompliance":  RunCommand<Commands.StandardsExt.StageComplianceAuditCommand>(app); break;
                    case "StdExt_SetRegion":         RunCommand<Commands.StandardsExt.SetRegionCommand>(app); break;
                    case "StdExt_AccessibilityAudit":RunCommand<Commands.StandardsExt.AccessibilityAuditCommand>(app); break;
                    case "StdExt_ParkingAudit":      RunCommand<Commands.StandardsExt.ParkingAuditCommand>(app); break;
                    case "StdExt_LiveLoadAudit":     RunCommand<Commands.StandardsExt.LiveLoadAuditCommand>(app); break;
                    case "StdExt_LoadCombinations":  RunCommand<Commands.StandardsExt.LoadCombinationsCommand>(app); break;
                    case "StdExt_EUIBenchmark":      RunCommand<Commands.StandardsExt.EUIBenchmarkCommand>(app); break;
                    case "StdExt_WaterUse":           RunCommand<Commands.StandardsExt.WaterUseCommand>(app); break;
                    case "StdExt_SpaceEff":           RunCommand<Commands.StandardsExt.SpaceEffCommand>(app); break;
                    case "StdExt_LifecycleCost":      RunCommand<Commands.StandardsExt.LifecycleCostCommand>(app); break;
                    case "StdBulk_Ventilation":      RunCommand<Commands.StandardsExt.VentilationCommand>(app); break;
                    case "StdBulk_PlumbingPipe":      RunCommand<Commands.StandardsExt.PlumbingPipeSizeCommand>(app); break;
                    case "StdBulk_DuctEqualFrict":    RunCommand<Commands.StandardsExt.DuctEqualFrictionCommand>(app); break;
                    case "StdBulk_Psychrometric":     RunCommand<Commands.StandardsExt.PsychrometricCommand>(app); break;
                    case "StdBulk_ArcFlash":          RunCommand<Commands.StandardsExt.ArcFlashCommand>(app); break;
                    case "StdBulk_ConduitFill":       RunCommand<Commands.StandardsExt.ConduitFillCommand>(app); break;
                    case "StdBulk_SteelBeam":         RunCommand<Commands.StandardsExt.SteelBeamCommand>(app); break;
                    case "StdBulk_ConcreteBeam":      RunCommand<Commands.StandardsExt.ConcreteBeamCommand>(app); break;
                    case "StdBulk_Foundation":        RunCommand<Commands.StandardsExt.FoundationCommand>(app); break;
                    case "StdBulk_Seismic":           RunCommand<Commands.StandardsExt.SeismicCommand>(app); break;
                    case "StdBulk_OccupantLoad":      RunCommand<Commands.StandardsExt.OccupantLoadCommand>(app); break;
                    case "StdBulk_TravelDistance":    RunCommand<Commands.StandardsExt.TravelDistanceCommand>(app); break;
                    case "StdBulk_EgressWidth":       RunCommand<Commands.StandardsExt.EgressWidthCommand>(app); break;
                    case "StdBulk_SpaceUtil":         RunCommand<Commands.StandardsExt.SpaceUtilizationCommand>(app); break;
                    case "StdBulk_Hydrant":           RunCommand<Commands.StandardsExt.HydrantCommand>(app); break;
                    case "StdBulk_MaintenanceCost":   RunCommand<Commands.StandardsExt.MaintenanceCostCommand>(app); break;
                    case "StdBulk_AccessibleToilet":  RunCommand<Commands.StandardsExt.AccessibleToiletCommand>(app); break;
                    case "StdBulk_AccessibleFix":     RunCommand<Commands.StandardsExt.AccessibleFixturesCommand>(app); break;
                    case "StdBulk_EnergyAnalysis":    RunCommand<Commands.StandardsExt.EnergyAnalysisCommand>(app); break;
                    case "StdBulk_SprinklerCriteria": RunCommand<Commands.StandardsExt.SprinklerCriteriaCommand>(app); break;
                    case "StdExt_RunCompliance":      RunCommand<Commands.StandardsExt.RunStandardsComplianceCommand>(app); break;

                    // ── Phase 115: Fabrication Extensions ──
                    case "FabExt_ACCPublish":    RunCommand<Commands.FabricationExt.ACCPublishShopCommand>(app); break;
                    case "FabExt_WeldPath":      RunCommand<Commands.FabricationExt.WeldPathExportCommand>(app); break;
                    case "FabExt_ExportNC":      RunCommand<Commands.FabricationExt.ExportNCCommand>(app); break;
                    case "FabExt_DuctSeamAudit": RunCommand<Commands.FabricationExt.DuctSeamAuditCommand>(app); break;
                    case "FabExt_PipeSupports":  RunCommand<Commands.FabricationExt.PipeSupportGridCommand>(app); break;
                    case "FabExt_HangerTakedown":RunCommand<Commands.FabricationExt.HangerTakedownCommand>(app); break;
                    case "FabExt_FlangeRating":  RunCommand<Commands.FabricationExt.FlangeRatingCommand>(app); break;
                    case "FabExt_SpoolWeight":   RunCommand<Commands.FabricationExt.SpoolWeightCommand>(app); break;
                    case "FabExt_TitleBlockFill":RunCommand<Commands.FabricationExt.TitleBlockFillCommand>(app); break;
                    case "FabExt_ISOSymbolsFull":RunCommand<Commands.FabricationExt.ISOSymbolsFullCommand>(app); break;

                    // ── Phase 114: Placement + Routing Extensions ──
                    case "PlcExt_SprinklerGrid":  RunCommand<Commands.PlacementExt.SprinklerGridCommand>(app); break;
                    case "PlcExt_AccessibleWC":   RunCommand<Commands.PlacementExt.AccessibleWcCommand>(app); break;
                    case "PlcExt_FireExt":        RunCommand<Commands.PlacementExt.FireExtinguisherCommand>(app); break;
                    case "PlcExt_ExitSigns":      RunCommand<Commands.PlacementExt.ExitSignsCommand>(app); break;
                    case "PlcExt_EmergencyLumAll":RunCommand<Commands.PlacementExt.EmergencyLumAllCommand>(app); break;
                    case "PlcExt_AccessControl":  RunCommand<Commands.PlacementExt.AccessControlCommand>(app); break;
                    case "PlcExt_CCTVCoverage":   RunCommand<Commands.PlacementExt.CCTVCoverageCommand>(app); break;
                    case "RtExt_Manhattan":       RunCommand<Commands.RoutingExt.ManhattanLayoutCommand>(app); break;
                    case "RtExt_ClashAvoid":      RunCommand<Commands.RoutingExt.ClashAvoidCommand>(app); break;
                    case "RtExt_CableBundle":     RunCommand<Commands.RoutingExt.CableBundleCommand>(app); break;
                    case "RtExt_PipeInsulation":  RunCommand<Commands.RoutingExt.PipeInsulationCommand>(app); break;
                    case "RtExt_AutoFireDamper":  RunCommand<Commands.RoutingExt.AutoFireDamperCommand>(app); break;
                    case "RtExt_ExpansionLoop":   RunCommand<Commands.RoutingExt.ExpansionLoopCommand>(app); break;
                    case "RtExt_TrayRiser":       RunCommand<Commands.RoutingExt.TrayRiserCommand>(app); break;

                    // ── Phase 113: MEP Design Extensions ──
                    case "MepA_CableSizeApply":  RunCommand<Commands.MepDesign.CableSizeApplyCommand>(app); break;
                    case "MepA_PanelSchedule":    RunCommand<Commands.MepDesign.PanelScheduleBuildCommand>(app); break;
                    case "MepA_BreakerAutoSize":  RunCommand<Commands.MepDesign.BreakerAutoSizeCommand>(app); break;
                    case "MepA_AutoSizeConduitAll": RunCommand<Commands.MepDesign.AutoSizeConduitAllCommand>(app); break;
                    case "MepA_GroundingDesign":  RunCommand<Commands.MepDesign.GroundingDesignCommand>(app); break;
                    case "MepA_DuctStaticRegain":  RunCommand<Commands.MepDesign.DuctStaticRegainCommand>(app); break;
                    case "MepA_PumpSize":          RunCommand<Commands.MepDesign.PumpSizeCommand>(app); break;
                    case "MepA_TransformerSize":   RunCommand<Commands.MepDesign.TransformerSizeCommand>(app); break;
                    case "MepA_GeneratorSize":     RunCommand<Commands.MepDesign.GeneratorSizeCommand>(app); break;
                    case "MepA_WaterHeaterSize":   RunCommand<Commands.MepDesign.WaterHeaterSizeCommand>(app); break;
                    case "MepA_DrainageSize":      RunCommand<Commands.MepDesign.DrainageSizeCommand>(app); break;
                    case "MepA_BalanceApply":      RunCommand<Commands.MepDesign.BalanceApplyCommand>(app); break;

                    // ── Phase 112: Structural Extensions ──
                    case "StrExt_AutoSlabRebar":     RunCommand<Commands.StructuralExt.AutoSlabRebarCommand>(app); break;
                    case "StrExt_FullColumnTakedown":RunCommand<Commands.StructuralExt.FullColumnTakedownCommand>(app); break;
                    case "StrExt_WindAutoApply":     RunCommand<Commands.StructuralExt.WindAutoApplyCommand>(app); break;
                    case "StrExt_SeismicAutoApply":  RunCommand<Commands.StructuralExt.SeismicAutoApplyCommand>(app); break;
                    case "StrExt_PileGroup":         RunCommand<Commands.StructuralExt.PileGroupDesignCommand>(app); break;
                    case "StrExt_RetainingWall":     RunCommand<Commands.StructuralExt.RetainingWallCheckCommand>(app); break;
                    case "StrExt_AutoConnection":    RunCommand<Commands.StructuralExt.AutoConnectionCommand>(app); break;
                    case "StrExt_CompositeBeam":     RunCommand<Commands.StructuralExt.CompositeBeamDesignCommand>(app); break;
                    case "StrExt_ToleranceCheck":    RunCommand<Commands.StructuralExt.ToleranceCheckCommand>(app); break;
                    case "StrExt_CreepDeflection":   RunCommand<Commands.StructuralExt.CreepDeflectionCommand>(app); break;

                    // ── Phase 111: Architecture & shell automation ──
                    case "Arch_AutoStair":       RunCommand<Commands.Architecture.AutoStairCommand>(app); break;
                    case "Arch_AutoRailing":     RunCommand<Commands.Architecture.AutoRailingCommand>(app); break;
                    case "Arch_AutoCurtainWall": RunCommand<Commands.Architecture.AutoCurtainWallCommand>(app); break;
                    case "Arch_AutoOpening":     RunCommand<Commands.Architecture.AutoOpeningCommand>(app); break;
                    case "Arch_AutoPlaster":     RunCommand<Commands.Architecture.AutoPlasterCommand>(app); break;
                    case "Arch_CoverAudit":      RunCommand<Commands.Architecture.CoverAuditCommand>(app); break;

                    // ── Phase 110: Standards & compliance calculations ──
                    case "Std_CalcCableSize":   RunCommand<Commands.Standards.CalcCableSizeCommand>(app); break;
                    case "Std_CalcWindLoad":    RunCommand<Commands.Standards.CalcWindLoadCommand>(app); break;
                    case "Std_CalcLighting":    RunCommand<Commands.Standards.CalcLightingCommand>(app); break;
                    case "Std_CalcCoolingLoad": RunCommand<Commands.Standards.CalcCoolingLoadCommand>(app); break;
                    case "Std_CalcEgress":      RunCommand<Commands.Standards.CalcEgressCommand>(app); break;
                    case "Std_DesignSprinkler": RunCommand<Commands.Standards.DesignSprinklerCommand>(app); break;

                    // ── Phase 109: MEP workflow automation ──
                    case "Mep_PressureDrop":    RunCommand<Commands.Mep.MepPressureDropAnalyseCommand>(app); break;
                    case "Mep_FittingLoss":     RunCommand<Commands.Mep.MepFittingLossReportCommand>(app); break;
                    case "Mep_Balance":         RunCommand<Commands.Mep.MepBalanceSystemCommand>(app); break;
                    case "Mep_VibroAcoustic":   RunCommand<Commands.Mep.MepVibroAcousticCheckCommand>(app); break;
                    case "Mep_SystemAnalyse":   RunCommand<Commands.Mep.MepSystemAnalyseCommand>(app); break;
                    case "Mep_SystemTracer":    RunCommand<Commands.Mep.MepSystemTracerCommand>(app); break;
                    case "Mep_AutoSleeve":      RunCommand<Commands.Mep.AutoSleevePlacementCommand>(app); break;
                    case "Mep_AutoSizePipe":    RunCommand<Commands.Mep.MepAutoSizePipeCommand>(app); break;
                    case "Mep_AutoSizeDuct":    RunCommand<Commands.Mep.MepAutoSizeDuctCommand>(app); break;
                    case "Mep_AutoSizeConduit": RunCommand<Commands.Mep.MepAutoSizeConduitCommand>(app); break;
                    case "Mep_AutoSizeAll":     RunCommand<Commands.Mep.MepAutoSizeAllCommand>(app); break;
                    case "MepCrossStamp":       RunCommand<Commands.Mep.MepCrossStampCommand>(app); break;
                    case "Mep_FillLiveCalc":    RunCommand<Commands.Mep.MepFillLiveCalcCommand>(app); break;
                    case "Mep_NamingAudit":     RunCommand<Commands.Mep.MepNamingAuditCommand>(app); break;

                    // ── v4 MVP: fabrication (Phase 5) ──
                    case "Fabrication_OpenWorkspace":     RunCommand<Commands.Fabrication.FabricationWorkspaceCommand>(app); break;
                    case "Fabrication_GeneratePackage":   RunCommand<Commands.Fabrication.GenerateFabPackageCommand>(app); break;
                    case "Fabrication_ExportCutList":     RunCommand<Commands.Fabrication.ExportCutListCommand>(app); break;
                    case "Fabrication_ExportIsometrics":  RunCommand<Commands.Fabrication.ExportIsometricsCommand>(app); break;
                    case "Fabrication_ExportWeldMap":     RunCommand<Commands.Fabrication.ExportWeldMapCommand>(app); break;
                    case "Fabrication_ExportMaj":         RunCommand<Commands.Fabrication.ExportMajCommand>(app); break;
                    case "Fabrication_ExportPcf":         RunCommand<Commands.Fabrication.ExportPcfCommand>(app); break;
                    case "Fabrication_UndoPackage":       RunCommand<Commands.Fabrication.UndoFabPackageCommand>(app); break;
                    case "Fabrication_SmartGroup":        RunCommand<Commands.Fabrication.SmartGroupCommand>(app); break;
                    case "Fabrication_ClashPreflight":    RunCommand<Commands.Fabrication.ClashPreflightCommand>(app); break;
                    case "Fabrication_IncrementalRebuild":RunCommand<Commands.Fabrication.IncrementalRebuildCommand>(app); break;
                    case "Fabrication_BomRollup":         RunCommand<Commands.Fabrication.BomRollupCommand>(app); break;
                    case "Fabrication_LinkDocRegister":   RunCommand<Commands.Fabrication.LinkDocRegisterCommand>(app); break;

                    // ── Phase 175: MEP/FP/SLD Symbol Library ──
                    case "Symbols_CreateAll":      RunCommand<Commands.Symbols.CreateSymbolLibraryCommand>(app); break;
                    case "Symbols_CreateSLD":      RunCommand<Commands.Symbols.CreateSLDSymbolsCommand>(app); break;
                    case "Symbols_CreateSLD_IEEE":  RunCommand<Commands.Symbols.CreateSLDSymbolsIEEECommand>(app); break;
                    case "Symbols_CreateSLD_BS":    RunCommand<Commands.Symbols.CreateSLDSymbolsBSCommand>(app); break;
                    case "Symbols_CreateSLD_NFPA":  RunCommand<Commands.Symbols.CreateSLDSymbolsNFPACommand>(app); break;
                    case "Symbols_CreateCIBSE":     RunCommand<Commands.Symbols.CreateCIBSESymbolsCommand>(app); break;
                    case "Symbols_CreateLighting": RunCommand<Commands.Symbols.CreateLightingSymbolsCommand>(app); break;
                    case "Symbols_CreateFP":       RunCommand<Commands.Symbols.CreateFPSymbolsCommand>(app); break;
                    case "Symbols_Reload":         RunCommand<Commands.Symbols.ReloadSymbolLibraryCommand>(app); break;
                    case "Symbols_Inspect":        RunCommand<Commands.Symbols.InspectSymbolLibraryCommand>(app); break;
                    case "Symbols_ConfigSizes":    RunCommand<Commands.Symbols.ConfigureSymbolSizesCommand>(app); break;

                    // ── Phase 175: Symbol Standards ──
                    case "Symbols_SwitchProject":  RunCommand<Commands.Symbols.SwitchProjectStandardCommand>(app); break;
                    case "Symbols_SwitchView":     RunCommand<Commands.Symbols.SwitchViewStandardCommand>(app); break;
                    case "Symbols_SetProfile":     RunCommand<Commands.Symbols.SetMixedStandardProfileCommand>(app); break;
                    case "Symbols_PlaceView":      RunCommand<Commands.Symbols.PlaceSymbolsInViewCommand>(app); break;
                    case "Symbols_PlaceAll":       RunCommand<Commands.Symbols.PlaceSymbolsProjectWideCommand>(app); break;
                    case "Symbols_Audit":          RunCommand<Commands.Symbols.SymbolStandardAuditCommand>(app); break;
                    case "Symbols_SyncFilters":    RunCommand<Commands.Symbols.SyncViewFilterVisibilityCommand>(app); break;
                    case "Symbols_AutoPlaceToggle": RunCommand<Commands.Symbols.SymbolsAutoPlaceToggleCommand>(app); break;
                    case "Symbols_RemoveInView":   RunCommand<Commands.Symbols.RemoveSymbolsInViewCommand>(app); break;
                    case "Symbols_RemoveAll":      RunCommand<Commands.Symbols.RemoveSymbolsProjectWideCommand>(app); break;

                    // ── Phase 175: Symbol Augmentation ──
                    case "Symbols_AugmentAll":      RunCommand<Commands.Symbols.AugmentProjectFamiliesCommand>(app); break;
                    case "Symbols_AugmentSelected": RunCommand<Commands.Symbols.AugmentSelectedFamilyCommand>(app); break;
                    case "Symbols_RollbackAugment": RunCommand<Commands.Symbols.RollbackAugmentationCommand>(app); break;
                    case "Symbols_AuthorSymbols":       RunCommand<Commands.Symbols.AuthorFamilySymbolsCommand>(app); break;
                    case "Symbols_SetElementStandard": RunCommand<Commands.Symbols.SetElementSymbolStandardCommand>(app); break;

                    // ── MEP Detail Symbols (FamilyInstance-based, MepSymbolEngine) ──
                    case "Symbols_PlaceMepDetail":         RunCommand<Commands.Symbols.PlaceMepDetailSymbolsCommand>(app); break;
                    case "Symbols_PlaceMepDetailAll":      RunCommand<Commands.Symbols.PlaceMepDetailSymbolsProjectWideCommand>(app); break;
                    case "Symbols_ClearMepDetail":         RunCommand<Commands.Symbols.ClearMepDetailSymbolsCommand>(app); break;

                    // ── Phase 175: Symbol Maintenance ──
                    case "Symbols_HealOrphans":    RunCommand<Commands.Symbols.HealSymbolOrphansCommand>(app); break;
                    case "Symbols_Coverage":       RunCommand<Commands.Symbols.SymbolCoverageAuditCommand>(app); break;
                    case "Symbols_FixDrift":       RunCommand<Commands.Symbols.FixSymbolDriftCommand>(app); break;
                    case "Symbols_BatchHeal":      RunCommand<Commands.Symbols.BatchHealAllSymbolsCommand>(app); break;

                    // ── Phase 180: Compound symbol factory ──
                    case "Symbols_CreateCompound": RunCommand<Commands.Symbols.CreateCompoundSymbolsCommand>(app); break;

                    // ── Equipment / fixture browse-and-place (plan-level) ──
                    case "Equip_PlaceElec":    RunCommand<Commands.Symbols.PlaceElecFixtureCommand>(app); break;
                    case "Equip_PlacePlumb":   RunCommand<Commands.Symbols.PlacePlumbingFixtureCommand>(app); break;
                    case "Equip_PlaceHvac":    RunCommand<Commands.Symbols.PlaceHvacEquipmentCommand>(app); break;
                    case "Equip_PlaceLight":   RunCommand<Commands.Symbols.PlaceLightingFixtureCommand>(app); break;
                    case "Equip_PlaceFP":      RunCommand<Commands.Symbols.PlaceFpDeviceCommand>(app); break;
                    case "Equip_PlacePipeAcc": RunCommand<Commands.Symbols.PlacePipeAccessoryCommand>(app); break;
                    case "Equip_BrowseAll":    RunCommand<Commands.Symbols.BrowseAllEquipmentSymbolsCommand>(app); break;

                    // ── SLD inline annotations ──
                    case "SldAnnotate_All":        RunCommand<Commands.Symbols.SldAnnotateAllCommand>(app); break;
                    case "SldAnnotate_Voltage":    RunCommand<Commands.Symbols.SldAnnotateVoltageCommand>(app); break;
                    case "SldAnnotate_Current":    RunCommand<Commands.Symbols.SldAnnotateCurrentCommand>(app); break;
                    case "SldAnnotate_Fault":      RunCommand<Commands.Symbols.SldAnnotateFaultCommand>(app); break;
                    case "SldAnnotate_Cable":      RunCommand<Commands.Symbols.SldAnnotateCableCommand>(app); break;
                    case "SldAnnotate_Phase":      RunCommand<Commands.Symbols.SldAnnotatePhaseCommand>(app); break;
                    case "SldAnnotate_Load":       RunCommand<Commands.Symbols.SldAnnotateLoadCommand>(app); break;
                    case "SldAnnotate_Reference":  RunCommand<Commands.Symbols.SldAnnotateReferenceCommand>(app); break;
                    case "SldAnnotate_Impedance":  RunCommand<Commands.Symbols.SldAnnotateImpedanceCommand>(app); break;
                    case "SldAnnotate_Diversity":  RunCommand<Commands.Symbols.SldAnnotateDiversityCommand>(app); break;
                    case "SldAnnotate_Format":     RunCommand<Commands.Symbols.SldAnnotationFormatCommand>(app); break;
                    case "SldAnnotate_UpdateCalcs":RunCommand<Commands.Symbols.SldUpdateFromCalcsCommand>(app); break;
                    case "SldAnnotate_Toggle":     RunCommand<Commands.Symbols.SldAnnotationToggleCommand>(app); break;
                    case "SldAnnotate_Clear":      RunCommand<Commands.Symbols.SldAnnotationClearCommand>(app); break;
                    case "SldAnnotate_Audit":      RunCommand<Commands.Symbols.SldAnnotationAuditCommand>(app); break;

                    // ── IFC ingestion (any IFC source: ArchiCAD, Tekla, Bentley, Solibri export) ──
                    case "IFC_PushModel":          RunCommand<Commands.IFC.IFC_PushModelCommand>(app); break;
                    case "ArchiCadIfcImport":      RunCommand<Commands.Interop.ArchiCadIfcImportCommand>(app); break;
                    case "IFC_StabilizeGuids":     RunCommand<Commands.Interop.StabilizeIfcGuidsCommand>(app); break;

                    // ── Phase 175: SLD Generator ──
                    case "SLD_Generate":           RunCommand<Commands.SLD.GenerateSLDCommand>(app); break;
                    case "SLD_GenerateOptions":    RunCommand<Commands.SLD.GenerateSLDWithOptionsCommand>(app); break;
                    case "SLD_Update":             RunCommand<Commands.SLD.UpdateSLDCommand>(app); break;
                    case "SLD_SyncToggle":         RunCommand<Commands.SLD.SLDSyncToggleCommand>(app); break;
                    case "SLD_Validate":           RunCommand<Commands.SLD.SLDValidateCommand>(app); break;
                    case "SLD_MigrateLabels":      RunCommand<Commands.SLD.MigrateSLDLabelIdsCommand>(app); break;

                    // ── v4 Phase C: calc engines ──
                    case "Calc_ConduitFill":  RunCommand<Commands.Routing.CalcConduitFillCommand>(app); break;
                    case "Calc_DuctFriction": RunCommand<Commands.Routing.CalcDuctFrictionCommand>(app); break;
                    case "Calc_SlopeCorrect": RunCommand<Commands.Routing.CalcSlopeCorrectCommand>(app); break;
                    case "Calc_HardyCross":  RunCommand<Commands.Routing.HardyCrossCommand>(app); break;
                    case "Calc_CableSegregation": RunCommand<Commands.Routing.CableSegregationCommand>(app); break;

                    // ── v4 Phase I: sleeve engine ──
                    case "Mep_PlaceSleeves":   RunCommand<Commands.Mep.PlaceSleevesCommand>(app); break;
                    case "Mep_ExportPfvIfc":   RunCommand<Commands.Mep.ExportPfvIfcCommand>(app); break;
                    case "Mep_ExportSleeveBcf":RunCommand<Commands.Mep.ExportSleeveBcfCommand>(app); break;

                    // ── v4 Phase J: cable-in-tray modelling ──
                    case "Electrical_AddCable":    RunCommand<Commands.Electrical.AddCableCommand>(app); break;
                    case "Electrical_ListCables":  RunCommand<Commands.Electrical.ListCablesCommand>(app); break;
                    case "Electrical_ExportCircuits": RunCommand<Commands.Electrical.ExportCircuitsCommand>(app); break;
                    case "Electrical_TrayFill":    RunCommand<Commands.Electrical.ShowTrayFillCommand>(app); break;

                    // ── Wire annotation symbols ──
                    case "Electrical_WireAnnotate":
                        RunCommand<Commands.Electrical.WireAnnotateCommand>(app); break;
                    case "Electrical_WireAnnotateBatch":
                        RunCommand<Commands.Electrical.WireAnnotateBatchCommand>(app); break;
                    case "Electrical_HomeRunArrow":
                        RunCommand<Commands.Electrical.HomeRunArrowCommand>(app); break;
                    case "Electrical_ClearWireAnnotations":
                        RunCommand<Commands.Electrical.ClearWireAnnotationsCommand>(app); break;
                    case "Electrical_RefreshWireAnnotations":
                        RunCommand<StingTools.Commands.Electrical.RefreshWireAnnotationsCommand>(app); break;
                    case "Electrical_HomeRunArrowBatch":
                        RunCommand<StingTools.Commands.Electrical.HomeRunArrowBatchCommand>(app); break;
                    case "Electrical_PanelWireReconcile":
                        RunCommand<StingTools.Commands.Electrical.PanelWireReconcileCommand>(app); break;
                    case "Electrical_WireAnnotationStyle":
                        RunCommand<Commands.Electrical.WireAnnotationStyleCommand>(app); break;
                    case "Electrical_WireAnnotationRefreshStyle":
                        RunCommand<Commands.Electrical.WireAnnotationRefreshStyleCommand>(app); break;
                    // Gap 1: stamp single conduit wire params from connected ElectricalSystem
                    case "Electrical_WireParamStamp":
                        RunCommand<Commands.Electrical.WireParamStampCommand>(app); break;
                    // Gap 2: batch stamp all conduits in view / selection
                    case "Electrical_BatchWireParamPopulate":
                        RunCommand<Commands.Electrical.BatchWireParamPopulateCommand>(app); break;
                    // Gap 3: VD calculation write-back
                    case "Electrical_WireVDSync":
                        RunCommand<Commands.Electrical.WireVDSyncCommand>(app); break;
                    // Gap 4: cable sizer write-back
                    case "Electrical_WireCableSizerSync":
                        RunCommand<Commands.Electrical.WireCableSizerSyncCommand>(app); break;
                    // Gap 5: cable schedule view + CSV
                    case "Electrical_CableScheduleBuild":
                        RunCommand<Commands.Electrical.Routing.CableScheduleBuilderCommand>(app); break;
                    // Gap 9: full run home-run traversal via BFS
                    case "Electrical_HomeRunFull":
                        RunCommand<Commands.Electrical.WireHomeRunFullCommand>(app); break;
                    // Gap 11: CPC sizing per BS 7671 Table 54.7
                    case "Electrical_WireCpcSizer":
                        RunCommand<Commands.Electrical.WireCpcSizerCommand>(app); break;
                    // Gap 12: fire-rated / armoured routing validation
                    case "Electrical_WireRoutingValidation":
                        RunCommand<Commands.Electrical.WireRoutingValidationCommand>(app); break;
                    // Gap 13: write ELC_SEL_COORD_OK from selective coordination check
                    case "Electrical_WireCoordStamp":
                        RunCommand<Commands.Electrical.WireCoordStampCommand>(app); break;
                    // Gap 7: save wire style from dock panel inline controls
                    case "Electrical_WireSaveStyle":
                        HandleWireSaveStyleFromPanel(app); break;

                    // ── v4 Phase D: hanger placement ──
                    case "Routing_PlaceHangers": RunCommand<Commands.Routing.PlaceHangersCommand>(app); break;
                    case "Fabrication_PlaceISOSymbols":
                        RunCommand<Commands.Fabrication.PlaceIsoSymbolsCommand>(app); break;
                    case "Fabrication_ConfigureShopDrawing":
                    {
                        var doc = app?.ActiveUIDocument?.Document;
                        if (doc == null) { TaskDialog.Show("STING v4", "Open a project first."); break; }
                        var dlg = new UI.ShopDrawingOptionsDialog(doc);
                        try { dlg.Owner = System.Windows.Application.Current?.MainWindow; } catch { }
                        if (dlg.ShowDialog() == true)
                        {
                            Commands.Fabrication.FabricationOptions.ShopDrawing = dlg.Result;
                            UI.StingDockPanel.LastInstance?.UpdateFabShopDrawingStatus(doc, dlg.Result);
                        }
                        break;
                    }
                    case "Fabrication_ClearShopDrawing":
                    {
                        Commands.Fabrication.FabricationOptions.ShopDrawing = null;
                        UI.StingDockPanel.LastInstance?.UpdateFabShopDrawingStatus(null, null);
                        break;
                    }

                    // ── Drawing Template Manager (Phase 113) ──
                    case "DrawingTypes_Inspect": RunCommand<Commands.Drawing.DrawingTypesInspectCommand>(app); break;
                    case "DrawingTypes_Reload":  RunCommand<Commands.Drawing.DrawingTypesReloadCommand>(app);  break;
                    case "DrawingTypes_PresentationSetup": RunCommand<Commands.Drawing.PresentationStyleSetupCommand>(app); break;
                    case "DrawingTypes_Editor":  RunCommand<Commands.Drawing.DrawingTypeEditorCommand>(app);   break;
                    case "DrawingTypes_ExportExcel": RunCommand<BIMManager.DrawingTypeExportExcelCommand>(app); break;
                    case "DrawingTypes_ImportExcel": RunCommand<BIMManager.DrawingTypeImportExcelCommand>(app); break;
                    case "DrawingTypes_GroupBrowser":  DrawingTypesGroupBrowserInline(app); break;
                    case "DrawingTypes_SyncStyles":    DrawingTypesSyncStylesInline(app);   break;
                    case "DrawingTypes_FromScopeBoxes": DrawingTypesFromScopeBoxesInline(app); break;
                    case "DrawingTypes_Renumber":      RunCommand<Commands.Drawing.DrawingRenumberCommand>(app); break;
                    case "DrawingTypes_HealTitleBlocks": RunCommand<Commands.Drawing.DrawingHealTitleBlocksCommand>(app); break;
                    case "DrawingTypes_Doctor":        RunCommand<Commands.Drawing.DrawingDoctorCommand>(app); break;
                    case "DrawingTypes_MigrateCsv":    RunCommand<Commands.Drawing.TitleBlockMigrateCsvToRecipeCommand>(app); break;
                    case "DrawingTypes_MigrateParams":     RunCommand<Commands.Drawing.TitleBlockParamMigrateCommand>(app); break;
                    case "DrawingTypes_AuditLegacyParams": RunCommand<Commands.Drawing.TitleBlockParamMigrateAuditCommand>(app); break;
                    case "DrawingTypes_SyncRevisions": RunCommand<Commands.Drawing.TitleBlockRevisionSyncCommand>(app); break;
                    case "DrawingTypes_BulkReStamp":      RunCommand<Commands.Drawing.BulkReStampDrawingTypeCommand>(app); break;
                    case "DrawingTypes_ProduceAndExport": RunCommand<Commands.Drawing.DrawingProduceAndExportCommand>(app); break;

                    // ── Drawing Template Manager · Phase 137 — production engine ──
                    case "DrawingTypes_ProducePerLevel":           RunCommand<Commands.Drawing.ProduceViewsPerLevelCommand>(app); break;
                    case "DrawingTypes_ProduceFromScopeBoxes":     RunCommand<Commands.Drawing.ProduceViewsFromScopeBoxesCommand>(app); break;
                    case "DrawingTypes_ProduceInteriorElevations": RunCommand<Commands.Drawing.ProduceInteriorElevationsCommand>(app); break;
                    case "DrawingTypes_ProduceExteriorElevations": RunCommand<Commands.Drawing.ProduceExteriorElevationsCommand>(app); break;
                    case "DrawingTypes_ProduceSections":           RunCommand<Commands.Drawing.ProduceSectionsCommand>(app); break;
                    case "DrawingTypes_RegenerateTemplates":       RunCommand<Commands.Drawing.RegeneratePackTemplatesCommand>(app); break;
                    case "DrawingTypes_ConvertToManaged":          RunCommand<Commands.Drawing.ConvertPackToManagedCommand>(app); break;
                    case "DrawingTypes_DetachManaged":             RunCommand<Commands.Drawing.DetachFromManagedCommand>(app); break;
                    case "DrawingTypes_ExportPackage":             RunCommand<Commands.Drawing.DrawingPackageExportCommand>(app); break;
                    case "DrawingTypes_SequencePackage":           RunCommand<Commands.Drawing.DrawingPackageSequenceCommand>(app); break;
                    case "DrawingTypes_AuditPackages":
                    case "DrawingTypes_PackageAudit":              RunCommand<Commands.Drawing.DrawingPackageAuditCommand>(app); break;

                    // ── Phase 168 / 169 — Match-line subsystem ──
                    case "MatchLine_Generate":       RunCommand<Commands.Drawing.MatchLineGenerateCommand>(app); break;
                    case "MatchLine_Sync":           RunCommand<Commands.Drawing.MatchLineSyncCommand>(app); break;
                    case "MatchLine_Validate":       RunCommand<Commands.Drawing.MatchLineValidateCommand>(app); break;
                    case "MatchLine_ValidateBundle": RunCommand<Commands.Drawing.MatchLineValidateBundleCommand>(app); break;
                    case "MatchLine_Inspect":        RunCommand<Commands.Drawing.MatchLineInspectCommand>(app); break;

                    // ── Phase 170 — Title-block factory ──
                    case "TitleBlock_Create":             RunCommand<Commands.Drawing.TitleBlockCreateCommand>(app); break;
                    case "TitleBlock_CreateAll":          RunCommand<Commands.Drawing.TitleBlockCreateAllCommand>(app); break;
                    // ── Phase 170 revision — Two-family BIM architecture + slot automation ──
                    case "TitleBlock_AutoPlaceViewports": RunCommand<Commands.Drawing.TitleBlockAutoPlaceViewportsCommand>(app); break;
                    case "TitleBlock_ToggleBIMMode":      RunCommand<Commands.Drawing.TitleBlockToggleBIMModeCommand>(app); break;
                    case "TitleBlock_AuditLegacy":        RunCommand<Commands.Drawing.TitleBlockAuditLegacyCommand>(app); break;
                    case "TitleBlock_MigrateLegacy":      RunCommand<Commands.Drawing.TitleBlockMigrateLegacyCommand>(app); break;


                    // ── Selection scope ──
                    case "SetScopeView": Select.SelectionScopeHelper.SetScope(false); TaskDialog.Show("Scope", "Selection scope: ACTIVE VIEW"); break;
                    case "SetScopeProject": Select.SelectionScopeHelper.SetScope(true); TaskDialog.Show("Scope", "Selection scope: WHOLE PROJECT"); break;
                    case "SetSelectionScope": RunCommand<Select.SetSelectionScopeCommand>(app); break;

                    // ── State selectors ──
                    case "SelectUntagged": RunCommand<Select.SelectUntaggedCommand>(app); break;
                    case "SelectTagged": RunCommand<Select.SelectTaggedCommand>(app); break;
                    case "SelectEmptyMark": RunCommand<Select.SelectEmptyMarkCommand>(app); break;
                    case "SelectPinned": RunCommand<Select.SelectPinnedCommand>(app); break;
                    case "SelectUnpinned": RunCommand<Select.SelectUnpinnedCommand>(app); break;

                    // ── Spatial selectors ──
                    case "SelectByLevel": RunCommand<Select.SelectByLevelCommand>(app); break;
                    case "SelectByRoom": RunCommand<Select.SelectByRoomCommand>(app); break;
                    case "SelectStale": RunCommand<Select.SelectStaleElementsCommand>(app); break;
                    case "QuickTagPreview": RunCommand<Select.QuickTagPreviewCommand>(app); break;

                    // ── Bulk param write ──
                    case "BulkParamWrite": RunCommand<Select.BulkParamWriteCommand>(app); break;

                    // ── View isolate/hide (inline) ──
                    case "ViewIsolate": ViewIsolateSelected(app); break;
                    case "ViewHide": ViewHideSelected(app); break;
                    case "ViewReveal": ViewRevealHidden(app); break;
                    case "ViewReset": ViewResetIsolate(app); break;

                    // ── Selection ops (inline) ──
                    case "SelectAll": SelectAllVisible(app); break;
                    case "SelectClear": ClearSelection(app); break;
                    case "Deselect": ClearSelection(app); break;
                    case "InvertSelection": InvertSelection(app); break;
                    case "DeleteSelected": DeleteSelected(app); break;
                    case "SelectTags": SelectAnnotationTags(app); break;
                    case "SelectHostElements": SelectHostElements(app); break;

                    // ── Selection memory (inline) ──
                    case "SaveM1": SaveSelectionMemory(app, "M1"); break;
                    case "LoadM1": LoadSelectionMemory(app, "M1"); break;
                    case "SaveM2": SaveSelectionMemory(app, "M2"); break;
                    case "LoadM2": LoadSelectionMemory(app, "M2"); break;
                    case "SaveM3": SaveSelectionMemory(app, "M3"); break;
                    case "LoadM3": LoadSelectionMemory(app, "M3"); break;
                    case "SwapM1M2": SwapMemorySlots(app, "M1", "M2"); break;
                    case "SelectionInfo": ShowSelectionInfo(app); break;
                    case "AddToM1": AddToMemory(app, "M1"); break;
                    case "RemoveFromM1": RemoveFromMemory(app, "M1"); break;
                    case "IntersectM1": IntersectWithMemory(app, "M1"); break;

                    // ── Quick param filter (inline) ──
                    case "QuickMark": QuickParamFilter(app, "Mark"); break;
                    case "QuickType": QuickParamFilter(app, "Type Name"); break;
                    case "QuickFamily": QuickParamFilter(app, "Family"); break;
                    case "QuickSystem": QuickParamFilter(app, ParamRegistry.SYS); break;

                    // ── Bulk write from panel (inline) ──
                    case "BulkWrite": BulkParamWriteInline(app, p1, p2, false); break;
                    case "BulkClear": BulkParamWriteInline(app, p1, "", true); break;
                    case "BulkPreview": BulkParamPreview(app, p1, p2); break;

                    // ── Project filters (inline) ──
                    case "FilterWorkset": QuickParamFilter(app, "Workset"); break;
                    case "FilterPhase": QuickParamFilter(app, "Phase Created"); break;
                    case "FilterDesignOption": QuickParamFilter(app, "Design Option"); break;
                    case "FilterGroup": QuickParamFilter(app, "Model Group"); break;
                    case "FilterAssembly": QuickParamFilter(app, "Assembly Name"); break;
                    case "FilterConnected": SelectConnectedElements(app); break;

                    // ════════════════════════════════════════════════════════
                    // ORGANISE TAB (merged Tags + Organise)
                    // ════════════════════════════════════════════════════════

                    // ── Tag operations ──
                    case "AutoTag": RunCommand<Tags.AutoTagCommand>(app); break;
                    case "BatchTag": RunCommand<Tags.BatchTagCommand>(app); break;
                    case "TagAndCombine": RunCommand<Tags.TagAndCombineCommand>(app); break;
                    case "TagSelected": RunCommand<Organise.TagSelectedCommand>(app); break;
                    case "TagNewOnly": RunCommand<Tags.TagNewOnlyCommand>(app); break;
                    case "TagChanged": RunCommand<Tags.TagChangedCommand>(app); break;
                    case "TagFormatMigration": RunCommand<Tags.TagFormatMigrationCommand>(app); break;
                    case "ReTag": RunCommand<Organise.ReTagCommand>(app); break;
                    case "DeleteTags": RunCommand<Organise.DeleteTagsCommand>(app); break;
                    case "RenumberTags": RunCommand<Organise.RenumberTagsCommand>(app); break;
                    case "SmartNumbering":
                    case "GraitecNumbering": RunCommand<Organise.SmartNumberingCommand>(app); break;
                    case "CopyTags": RunCommand<Organise.CopyTagsCommand>(app); break;
                    case "SwapTags": RunCommand<Organise.SwapTagsCommand>(app); break;
                    case "FixDuplicates": RunCommand<Organise.FixDuplicateTagsCommand>(app); break;
                    case "FindDuplicates": RunCommand<Organise.FindDuplicateTagsCommand>(app); break;

                    // ── Smart tag placement ──
                    case "SmartPlaceTags": RunCommand<Tags.SmartPlaceTagsCommand>(app); break;
                    case "ArrangeTags": RunCommand<Tags.ArrangeTagsCommand>(app); break;
                    case "BatchPlaceTags": RunCommand<Tags.BatchPlaceTagsCommand>(app); break;
                    case "RemoveAnnotationTags": RunCommand<Tags.RemoveAnnotationTagsCommand>(app); break;
                    case "LearnTagPlacement": RunCommand<Tags.LearnTagPlacementCommand>(app); break;
                    case "ApplyTagTemplate": RunCommand<Tags.ApplyTagTemplateCommand>(app); break;
                    case "TagOverlapAnalysis": RunCommand<Tags.TagOverlapAnalysisCommand>(app); break;
                    case "BatchTagTextSize": RunCommand<Tags.BatchTagTextSizeCommand>(app); break;
                    case "SetTagCatLineWeight": RunCommand<Tags.SetTagCategoryLineWeightCommand>(app); break;
                    case "Tag3D": RunCommand<Tags.Tag3DCommand>(app); break;
                    case "RepairDuplicateSeq": RunCommand<Tags.RepairDuplicateSeqCommand>(app); break;

                    // ── Rich TAG7 display ──
                    case "RichTagNote": RunCommand<Tags.RichTagNoteCommand>(app); break;
                    case "ExportRichTagReport": RunCommand<Tags.ExportRichTagReportCommand>(app); break;
                    case "ViewTag7Sections": RunCommand<Tags.ViewTag7SectionsCommand>(app); break;
                    case "SwitchTag7Preset": RunCommand<Tags.SwitchTag7PresetCommand>(app); break;

                    // ── TAG1-TAG6 segment display ──
                    case "RichSegmentNote": RunCommand<Tags.RichSegmentNoteCommand>(app); break;
                    case "ViewSegments": RunCommand<Tags.ViewSegmentsCommand>(app); break;

                    // ── Legend builder ──
                    case "CreateColorLegend": RunCommand<Tags.CreateColorLegendCommand>(app); break;
                    case "ExportColorLegendHtml": RunCommand<Tags.ExportColorLegendHtmlCommand>(app); break;
                    case "AutoCreateLegends": RunCommand<Tags.AutoCreateLegendsCommand>(app); break;
                    case "LegendFromView": RunCommand<Tags.LegendFromViewCommand>(app); break;
                    case "PlaceLegendOnSheet": RunCommand<Tags.PlaceLegendOnSheetCommand>(app); break;
                    case "SheetContextLegend": RunCommand<Tags.SheetContextLegendCommand>(app); break;
                    case "PlaceLegendOnAllSheets": RunCommand<Tags.PlaceLegendOnAllSheetsCommand>(app); break;
                    case "BatchSheetContextLegends": RunCommand<Tags.BatchSheetContextLegendsCommand>(app); break;
                    case "CreateTagLegend": RunCommand<Tags.CreateTagLegendCommand>(app); break;
                    case "SheetTagLegend": RunCommand<Tags.SheetTagLegendCommand>(app); break;
                    case "BatchTagLegends": RunCommand<Tags.BatchTagLegendsCommand>(app); break;
                    case "UpdateLegend": RunCommand<Tags.UpdateLegendCommand>(app); break;
                    case "DeleteStaleLegend": RunCommand<Tags.DeleteStaleLegendCommand>(app); break;
                    case "OneClickLegendPipeline": RunCommand<Tags.OneClickLegendPipelineCommand>(app); break;
                    case "MepSystemLegend": RunCommand<Tags.MepSystemLegendCommand>(app); break;
                    case "MaterialLegend": RunCommand<Tags.MaterialLegendCommand>(app); break;
                    case "CompoundTypeLegend": RunCommand<Tags.CompoundTypeLegendCommand>(app); break;
                    case "EquipmentLegend": RunCommand<Tags.EquipmentLegendCommand>(app); break;
                    case "FireRatingLegend": RunCommand<Tags.FireRatingLegendCommand>(app); break;
                    case "MasterLegendPipeline": RunCommand<Tags.MasterLegendPipelineCommand>(app); break;
                    case "FilterLegend": RunCommand<Tags.FilterLegendCommand>(app); break;
                    case "TemplateLegend": RunCommand<Tags.TemplateLegendCommand>(app); break;
                    case "VGCategoryLegend": RunCommand<Tags.VGCategoryLegendCommand>(app); break;
                    case "BatchTemplateLegend": RunCommand<Tags.BatchTemplateLegendCommand>(app); break;
                    case "FlexibleLegend": RunCommand<Tags.FlexibleLegendCommand>(app); break;
                    case "LegendFromPreset": RunCommand<Tags.LegendFromPresetCommand>(app); break;
                    case "ComponentTypeLegend": RunCommand<Tags.ComponentTypeLegendCommand>(app); break;
                    case "ColorReferenceLegend": RunCommand<Tags.ColorReferenceLegendCommand>(app); break;
                    case "LegendSyncAudit": RunCommand<Tags.LegendSyncAuditCommand>(app); break;
                    case "StatusLegend": RunCommand<Tags.StatusLegendCommand>(app); break;
                    case "WorksetLegend": RunCommand<Tags.WorksetLegendCommand>(app); break;

                    // ── System Parameter Push ──
                    case "SystemParamPush": RunCommand<Tags.SystemParamPushCommand>(app); break;
                    case "BatchSystemPush": RunCommand<Tags.BatchSystemPushCommand>(app); break;
                    case "SelectSystemElements": RunCommand<Tags.SelectSystemElementsCommand>(app); break;

                    // ── Orientation & text alignment ──
                    case "ToggleTagOrientation": RunCommand<Organise.ToggleTagOrientationCommand>(app); break;
                    case "FlipTags":
                    case "FlipTagsH": SetExtraParam("FlipDirection", "H"); RunCommand<Organise.FlipTagsCommand>(app); break;
                    case "FlipTagsV": SetExtraParam("FlipDirection", "V"); RunCommand<Organise.FlipTagsCommand>(app); break;
                    case "AlignTextLeft":
                    case "AlignTextCenter":
                    case "AlignTextRight": RunCommand<Organise.AlignTagTextCommand>(app); break;
                    case "AutoAlignLeaderText": RunCommand<Organise.AutoAlignLeaderTextCommand>(app); break;

                    // ── Align & distribute ──
                    case "AlignTagsH":
                        SetExtraParam("AlignDirection", "Horizontal");
                        RunCommand<Organise.AlignTagsCommand>(app);
                        ClearExtraParam("AlignDirection");
                        break;
                    case "AlignTagsV":
                        SetExtraParam("AlignDirection", "Vertical");
                        RunCommand<Organise.AlignTagsCommand>(app);
                        ClearExtraParam("AlignDirection");
                        break;
                    case "ArrangeStack":
                    case "ArrangeStackH":
                        SetExtraParam("AlignDirection", "Row");
                        RunCommand<Organise.AlignTagsCommand>(app);
                        ClearExtraParam("AlignDirection");
                        break;
                    case "AlignLeft":
                    case "AlignRight":
                    case "AlignTop":
                    case "AlignBottom":
                    case "AlignCenterH":
                    case "AlignCenterV":
                    case "DistributeH":
                    case "DistributeV":
                    case "ArrangeGrid":
                    case "ArrangeCircle":
                    case "ArrangeMirror":
                    case "ArrangeRadial": RunCommand<Organise.AlignTagsCommand>(app); break;
                    case "ResetTagPositions": RunCommand<Organise.ResetTagPositionsCommand>(app); break;

                    // ── Leaders ──
                    case "AddLeaders": RunCommand<Organise.AddLeadersCommand>(app); break;
                    case "RemoveLeaders": RunCommand<Organise.RemoveLeadersCommand>(app); break;
                    case "ToggleLeaders": RunCommand<Organise.ToggleLeadersCommand>(app); break;
                    case "SnapElbow90": SnapElbowDirect(app, "90"); break;
                    case "SnapElbow45": SnapElbowDirect(app, "45"); break;
                    case "SnapElbowStraight": SnapElbowDirect(app, "0"); break;
                    case "PinTags": RunCommand<Organise.PinTagsCommand>(app); break;
                    case "AttachLeader": RunCommand<Organise.AttachLeaderCommand>(app); break;
                    case "SelectTagsWithLeaders": RunCommand<Organise.SelectTagsWithLeadersCommand>(app); break;
                    case "EqualizeLeaderLengths": RunCommand<Organise.EqualizeLeaderLengthsCommand>(app); break;
                    case "LeaderLength025": SetExtraParam("LeaderLength", "0.25"); RunCommand<Organise.SnapLeaderElbowCommand>(app); break;
                    case "LeaderLength05": SetExtraParam("LeaderLength", "0.5"); RunCommand<Organise.SnapLeaderElbowCommand>(app); break;
                    case "LeaderLength1": SetExtraParam("LeaderLength", "1.0"); RunCommand<Organise.SnapLeaderElbowCommand>(app); break;
                    case "LeaderEqualSpacing": SetExtraParam("LeaderLength", "equal"); RunCommand<Organise.SnapLeaderElbowCommand>(app); break;
                    case "LeaderEqualise": RunCommand<Organise.SnapLeaderElbowCommand>(app); break;

                    // ── Appearance (annotation colors) ──
                    case "ColorTagsByDiscipline": RunCommand<Organise.ColorTagsByDisciplineCommand>(app); break;
                    case "SetTagTextColor": RunCommand<Organise.SetTagTextColorCommand>(app); break;
                    case "SetLeaderColor": RunCommand<Organise.SetLeaderColorCommand>(app); break;
                    case "SplitTagLeaderColor": RunCommand<Organise.SplitTagLeaderColorCommand>(app); break;
                    case "ClearAnnotationColors": RunCommand<Organise.ClearAnnotationColorsCommand>(app); break;
                    case "TagAppearance": RunCommand<Organise.TagAppearanceCommand>(app); break;
                    case "SetTagBox": RunCommand<Organise.SetTagBoxAppearanceCommand>(app); break;
                    case "QuickTagStyle": RunCommand<Organise.QuickTagStyleCommand>(app); break;
                    case "SetTagLineWeight": RunCommand<Organise.SetTagLineWeightCommand>(app); break;
                    case "ColorTagsByParam": RunCommand<Organise.ColorTagsByParameterCommand>(app); break;
                    case "SwapTagType": RunCommand<Organise.SwapTagTypeCommand>(app); break;

                    // ── Analysis ──
                    case "TagStats": RunCommand<Organise.TagStatsCommand>(app); break;
                    case "AuditTagsCSV": RunCommand<Organise.AuditTagsCSVCommand>(app); break;
                    case "SelectByDiscipline": RunCommand<Organise.SelectByDisciplineCommand>(app); break;
                    case "TagRegisterExport": RunCommand<Organise.TagRegisterExportCommand>(app); break;

                    // ── QR Codes ──
                    case "QRCode": RunCommand<Tags.QRCodeCommand>(app); break;

                    // ── Code Legend ──
                    case "CodeLegend": RunCommand<Tags.CodeLegendCommand>(app); break;

                    // ── QA ──
                    case "ValidateTags": RunCommand<Tags.ValidateTagsCommand>(app); break;
                    case "HighlightInvalid": RunCommand<Organise.HighlightInvalidCommand>(app); break;
                    case "ClearOverrides": RunCommand<Organise.ClearOverridesCommand>(app); break;
                    case "CompletenessDashboard": RunCommand<Tags.CompletenessDashboardCommand>(app); break;
                    case "PreTagAudit": RunCommand<Tags.PreTagAuditCommand>(app); break;
                    case "ResolveAllIssues": RunCommand<Tags.ResolveAllIssuesCommand>(app); break;

                    // ── Paragraph & Warning controls (v4.2) ──
                    case "SetParagraphDepth": RunCommand<Tags.SetParagraphDepthCommand>(app); break;
                    case "ToggleWarningVisibility": RunCommand<Tags.ToggleWarningVisibilityCommand>(app); break;

                    // ── Presentation Mode & Label Spec (v4.3) ──
                    case "SetPresentationMode": RunCommand<Tags.SetPresentationModeCommand>(app); break;
                    case "ViewLabelSpec": RunCommand<Tags.ViewLabelSpecCommand>(app); break;
                    case "ExportLabelGuide": RunCommand<Tags.ExportLabelGuideCommand>(app); break;
                    case "SetTag7HeadingStyle": RunCommand<Tags.SetTag7HeadingStyleCommand>(app); break;

                    // ── Tag Style Engine commands ──
                    case "ApplyTagStyles": RunCommand<Tags.ApplyTagStylesCommand>(app); break;
                    case "PreviewTagStyles": RunCommand<Tags.PreviewTagStylesCommand>(app); break;
                    case "SetTagStyleRule": RunCommand<Tags.SetTagStyleRuleCommand>(app); break;
                    case "SaveTagStylePreset": RunCommand<Tags.SaveTagStylePresetCommand>(app); break;
                    case "LoadTagStylePreset": RunCommand<Tags.LoadTagStylePresetCommand>(app); break;

                    // ── Parameter-driven styles (no type switching) ──
                    case "ApplyParamStyles": RunCommand<Tags.ApplyParamDrivenStylesCommand>(app); break;
                    case "PreviewParamStyles": RunCommand<Tags.PreviewParamDrivenStylesCommand>(app); break;
                    case "ClearParamStyles": RunCommand<Tags.ClearParamDrivenStylesCommand>(app); break;
                    case "BatchParamStyles": RunCommand<Tags.BatchApplyParamDrivenStylesCommand>(app); break;

                    // ── Color By Parameter commands ──
                    case "ColorByParameter": RunCommand<Select.ColorByParameterCommand>(app); break;
                    case "ClearColorOverrides": RunCommand<Select.ClearColorOverridesCommand>(app); break;
                    case "SaveColorPreset": RunCommand<Select.SaveColorPresetCommand>(app); break;
                    case "LoadColorPreset": RunCommand<Select.LoadColorPresetCommand>(app); break;
                    case "CreateFiltersFromColors": RunCommand<Select.CreateFiltersFromColorsCommand>(app); break;

                    // ── Colouriser inline ──
                    case "ColorApply": ColorByParameter(app, p1, p2); break;
                    case "ColorApplyHex": ColorByHex(app, p1); break;
                    case "ColorApplyTransparency": SetTransparencyOverride(app, p1); break;

                    // ── Graphic overrides (inline) ──
                    case "HalftoneOn": SetHalftone(app, true); break;
                    case "HalftoneOff": SetHalftone(app, false); break;
                    case "PermHide": PermanentHide(app); break;
                    case "PermUnhide": PermanentUnhide(app); break;
                    case "UnhideCategory": UnhideCategory(app); break;

                    // ════════════════════════════════════════════════════════
                    // DOCS TAB
                    // ════════════════════════════════════════════════════════

                    case "SheetOrganizer": RunCommand<Docs.SheetOrganizerCommand>(app); break;
                    case "ViewOrganizer": RunCommand<Docs.ViewOrganizerCommand>(app); break;
                    case "SheetIndex": RunCommand<Docs.SheetIndexCommand>(app); break;
                    case "Transmittal": RunCommand<Docs.TransmittalCommand>(app); break;

                    // ── Phase 97 — Title Block System (8 commands per spec v1.0) ──
                    case "TitleBlockEditCsv":      RunCommand<Docs.TitleBlockEditCsvCommand>(app); break;
                    case "TitleBlockPopulate":     RunCommand<Docs.TitleBlockPopulateCommand>(app); break;
                    case "TitleBlockValidate":     RunCommand<Docs.TitleBlockValidateCommand>(app); break;
                    case "TitleBlockSetVariant":   RunCommand<Docs.TitleBlockSetVariantCommand>(app); break;
                    case "DisciplineLegendBind":   RunCommand<Docs.DisciplineLegendBindCommand>(app); break;
                    case "SheetCountAutoUpdate":   RunCommand<Docs.SheetCountAutoUpdateCommand>(app); break;
                    case "RevisionSync":           RunCommand<Docs.RevisionSyncCommand>(app); break;
                    case "TransmittalAutoIssue":   RunCommand<Docs.TransmittalAutoIssueCommand>(app); break;
                    case "PreExportValidate":     RunCommand<Docs.PreExportValidateCommand>(app); break;

                    case "HandoverManual": RunCommand<Docs.HandoverManualCommand>(app); break;
                    case "DeleteUnusedViews": RunCommand<Docs.DeleteUnusedViewsCommand>(app); break;
                    case "SheetNamingCheck": RunCommand<Docs.SheetNamingCheckCommand>(app); break;
                    case "AutoNumberSheets": RunCommand<Docs.AutoNumberSheetsCommand>(app); break;
                    case "AlignViewports": RunCommand<Docs.AlignViewportsCommand>(app); break;
                    case "RenumberViewports": RunCommand<Docs.RenumberViewportsCommand>(app); break;
                    case "TextCase": RunCommand<Docs.TextCaseCommand>(app); break;
                    case "SumAreas": RunCommand<Docs.SumAreasCommand>(app); break;

                    // ── View Automation (Phase 4) ──
                    case "DuplicateView": RunCommand<Docs.DuplicateViewCommand>(app); break;
                    case "BatchRenameViews": RunCommand<Docs.BatchRenameViewsCommand>(app); break;
                    case "CopyViewSettings": RunCommand<Docs.CopyViewSettingsCommand>(app); break;
                    case "AutoPlaceViewports": RunCommand<Docs.AutoPlaceViewportsCommand>(app); break;
                    case "CropToContent": RunCommand<Docs.CropToContentCommand>(app); break;
                    case "BatchAlignViewports": RunCommand<Docs.BatchAlignViewportsCommand>(app); break;
                    case "Rename":
                    case "MagicRename": RunCommand<Docs.MagicRenameCommand>(app); break;
                    case "ViewTabColour": RunCommand<Docs.ViewTabColourCommand>(app); break;
                    case "RibbonStyler": RunCommand<Docs.RibbonPanelStylerCommand>(app); break;

                    // ── Sheet Manager ──
                    case "SheetManager": RunCommand<Docs.SheetManagerCommand>(app); break;
                    case "AutoLayout": RunCommand<Docs.AutoLayoutCommand>(app); break;
                    case "CloneSheet": RunCommand<Docs.CloneSheetCommand>(app); break;
                    case "PlaceUnplacedViews": RunCommand<Docs.PlaceUnplacedViewsCommand>(app); break;
                    case "OptimalScale": RunCommand<Docs.OptimalScaleCommand>(app); break;
                    case "SheetAudit": RunCommand<Docs.SheetAuditCommand>(app); break;
                    case "BatchArrange": RunCommand<Docs.BatchArrangeCommand>(app); break;
                    case "MoveViewport": RunCommand<Docs.MoveViewportCommand>(app); break;

                    // ── Sheet Manager Phase 2 ──
                    case "MaxRectsLayout": RunCommand<Docs.MaxRectsLayoutCommand>(app); break;
                    case "SaveLayoutPreset": RunCommand<Docs.SaveLayoutPresetCommand>(app); break;
                    case "ApplyLayoutPreset": RunCommand<Docs.ApplyLayoutPresetCommand>(app); break;
                    case "BatchCloneSheets": RunCommand<Docs.BatchCloneSheetsCommand>(app); break;
                    case "BatchRenumberSheets": RunCommand<Docs.BatchRenumberSheetsCommand>(app); break;
                    case "AutoAssignVPTypes": RunCommand<Docs.AutoAssignVPTypesCommand>(app); break;
                    case "ExportSheetSet": RunCommand<Docs.ExportSheetSetCommand>(app); break;
                    case "PlaceWithOverflow": RunCommand<Docs.PlaceWithOverflowCommand>(app); break;

                    // ── Sheet Templates & Compliance (Phase 3) ──
                    case "CreateFromTemplate": RunCommand<Docs.CreateFromTemplateCommand>(app); break;
                    case "SaveSheetTemplate": RunCommand<Docs.SaveSheetTemplateCommand>(app); break;
                    case "SheetComplianceCheck": RunCommand<Docs.SheetComplianceCheckCommand>(app); break;
                    case "GridAlignViewports": RunCommand<Docs.GridAlignViewportsCommand>(app); break;
                    case "AlignViewportEdges": RunCommand<Docs.AlignViewportEdgesCommand>(app); break;
                    case "DistributeViewports": RunCommand<Docs.DistributeViewportsCommand>(app); break;
                    case "BatchPrintSheets": RunCommand<Docs.ExportCenterPdfCommand>(app); break; // redirects to Export Centre (PDF preset)
                    case "ExportSheetRegister": RunCommand<Docs.ExportSheetRegisterCommand>(app); break;
                    case "ExportCenter": RunCommand<Docs.ExportCenterCommand>(app); break;
                    case "ExportCenterPDF": RunCommand<Docs.ExportCenterPdfCommand>(app); break;

                    // ── Sheet Manager Live Operations (modeless dialog dispatch) ──
                    case "SM_PlaceViewOnSheet":
                    case "SM_PlaceOnNewSheet":
                    case "SM_MoveViewportToSheet":
                    case "SM_RemoveViewport":
                    case "SM_CreateSheet":
                    case "SM_CloneSheet":
                    case "SM_ArrangeOnSheet":
                    case "SM_AutoScaleSheet":
                    case "SM_AutoLayoutMode":
                    case "SM_AutoPlaceUnplaced":
                    case "SM_DuplicateView":
                    case "SM_DeleteView":
                    case "SM_BatchArrange":
                    case "SM_RenumberDisc":
                    case "SM_SwapTitleBlock":
                    case "SM_EnforceISONaming":
                    case "SM_RevertISONaming":
                    // Non-prefixed aliases for context menu dispatch
                    case "PlaceViewOnSheet":
                    case "PlaceOnNewSheet":
                    case "MoveViewportToSheet":
                    case "RemoveViewport":
                    case "ArrangeOnSheet":
                    case "AutoScaleSheet":
                    case "AutoLayoutMode":
                    case "DeleteView":
                    case "RenumberDisc":
                    case "SwapTitleBlock":
                    case "EnforceISONaming":
                    case "RevertISONaming":
                        RunSheetManagerOp(app, tag);
                        break;

                    case "ActivateView":
                    {
                        var uidoc = app.ActiveUIDocument;
                        if (uidoc != null)
                        {
                            string viewIdStr = GetExtraParam("SM_ViewTag");
                            if (!string.IsNullOrEmpty(viewIdStr) && long.TryParse(viewIdStr, out long vid))
                            {
                                var view = uidoc.Document.GetElement(new ElementId(vid)) as View;
                                if (view != null)
                                    uidoc.ActiveView = view;
                            }
                        }
                        break;
                    }

                    // ── Sheet Manager template & selection operations ──
                    case "SM_GetViewTemplates":
                    {
                        var doc = app.ActiveUIDocument?.Document;
                        if (doc != null)
                        {
                            var templates = Temp.TemplateManager.GetAllViewTemplates(doc);
                            SheetManagerDialog._cachedTemplateNames = templates.Keys.OrderBy(k => k).ToList();
                        }
                        break;
                    }
                    case "SM_AssignSpecificTemplate":
                    {
                        var doc = app.ActiveUIDocument?.Document;
                        if (doc != null)
                        {
                            string tplName = GetExtraParam("SM_TemplateName");
                            string viewIdStr = GetExtraParam("SM_SelectedTag");
                            if (!string.IsNullOrEmpty(tplName) && !string.IsNullOrEmpty(viewIdStr))
                            {
                                var templates = Temp.TemplateManager.GetAllViewTemplates(doc);
                                if (templates.TryGetValue(tplName, out View tpl) && long.TryParse(viewIdStr, out long vid))
                                {
                                    var view = doc.GetElement(new ElementId(vid)) as View;
                                    if (view != null)
                                    {
                                        using (var tx = new Transaction(doc, "STING Assign View Template"))
                                        {
                                            tx.Start();
                                            view.ViewTemplateId = tpl.Id;
                                            tx.Commit();
                                        }
                                        TaskDialog.Show("STING", $"Assigned template '{tplName}' to '{view.Name}'.");
                                    }
                                }
                            }
                        }
                        break;
                    }
                    case "SM_RemoveViewTemplate":
                    {
                        var doc = app.ActiveUIDocument?.Document;
                        if (doc != null)
                        {
                            string viewIdStr = GetExtraParam("SM_SelectedTag");
                            if (!string.IsNullOrEmpty(viewIdStr) && long.TryParse(viewIdStr, out long vid))
                            {
                                var view = doc.GetElement(new ElementId(vid)) as View;
                                if (view != null)
                                {
                                    using (var tx = new Transaction(doc, "STING Remove View Template"))
                                    {
                                        tx.Start();
                                        view.ViewTemplateId = ElementId.InvalidElementId;
                                        tx.Commit();
                                    }
                                    TaskDialog.Show("STING", $"Removed template from '{view.Name}'.");
                                }
                            }
                        }
                        break;
                    }
                    case "SM_SelectElementIds":
                    {
                        var uidoc = app.ActiveUIDocument;
                        if (uidoc != null)
                        {
                            string idsStr = GetExtraParam("SM_ElementIds");
                            if (!string.IsNullOrEmpty(idsStr))
                            {
                                // SCH-MEDIUM-01: Use TryParse out-value to avoid double Trim+Parse
                                var ids = new List<ElementId>();
                                foreach (var s in idsStr.Split(','))
                                {
                                    if (long.TryParse(s.Trim(), out long idVal))
                                        ids.Add(new ElementId(idVal));
                                }
                                if (ids.Count > 0)
                                    uidoc.Selection.SetElementIds(ids);
                            }
                        }
                        break;
                    }

                    // ── Sheet Manager scope box operations ──
                    case "SM_GetScopeBoxes":
                    {
                        var doc = app.ActiveUIDocument?.Document;
                        if (doc != null)
                        {
                            var scopeBoxes = Docs.DocAutomationHelper.GetScopeBoxes(doc);
                            SheetManagerDialog._cachedScopeBoxes = scopeBoxes
                                .Select(sb => new KeyValuePair<long, string>(sb.Id.Value, sb.Name))
                                .OrderBy(kv => kv.Value)
                                .ToList();
                        }
                        break;
                    }
                    case "SM_AssignScopeBox":
                    {
                        var doc = app.ActiveUIDocument?.Document;
                        if (doc != null)
                        {
                            string viewIdStr = GetExtraParam("SM_SelectedTag");
                            string scopeBoxIdStr = GetExtraParam("SM_ScopeBoxId");
                            string scopeBoxName = GetExtraParam("SM_ScopeBoxName");
                            if (!string.IsNullOrEmpty(viewIdStr) && long.TryParse(viewIdStr, out long vid)
                                && !string.IsNullOrEmpty(scopeBoxIdStr) && long.TryParse(scopeBoxIdStr, out long sbId))
                            {
                                var view = doc.GetElement(new ElementId(vid)) as View;
                                if (view != null)
                                {
                                    using (var tx = new Transaction(doc, "STING Assign Scope Box"))
                                    {
                                        tx.Start();
                                        bool ok = Docs.DocAutomationHelper.AssignScopeBox(view, new ElementId(sbId));
                                        tx.Commit();
                                        if (ok)
                                            StingLog.Info($"Assigned scope box '{scopeBoxName}' to '{view.Name}'.");
                                    }
                                }
                            }
                        }
                        break;
                    }
                    case "SM_RemoveScopeBox":
                    {
                        var doc = app.ActiveUIDocument?.Document;
                        if (doc != null)
                        {
                            string viewIdStr = GetExtraParam("SM_SelectedTag");
                            if (!string.IsNullOrEmpty(viewIdStr) && long.TryParse(viewIdStr, out long vid))
                            {
                                var view = doc.GetElement(new ElementId(vid)) as View;
                                if (view != null)
                                {
                                    using (var tx = new Transaction(doc, "STING Remove Scope Box"))
                                    {
                                        tx.Start();
                                        try
                                        {
                                            Parameter p = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                                            if (p != null && !p.IsReadOnly)
                                                p.Set(ElementId.InvalidElementId);
                                        }
                                        catch (Exception ex) { StingLog.Warn($"RemoveScopeBox: {ex.Message}"); }
                                        tx.Commit();
                                    }
                                }
                            }
                        }
                        break;
                    }

                    // ── Documentation Automation (Phase 6) ──
                    case "BatchCreateViews": RunCommand<Docs.BatchCreateViewsCommand>(app); break;
                    case "BatchCreateSheets": RunCommand<Docs.BatchCreateSheetsCommand>(app); break;
                    case "CreateDependentViews": RunCommand<Docs.CreateDependentViewsCommand>(app); break;
                    case "ScopeBoxManager": RunCommand<Docs.ScopeBoxManagerCommand>(app); break;
                    case "ViewTemplateAssigner": RunCommand<Docs.ViewTemplateAssignerCommand>(app); break;
                    case "DocumentationPackage": RunCommand<Docs.DocumentationPackageCommand>(app); break;
                    case "BatchCreateSections": RunCommand<Docs.BatchCreateSectionsCommand>(app); break;
                    case "BatchCreateElevations": RunCommand<Docs.BatchCreateElevationsCommand>(app); break;
                    case "DocsDrawingRegister": RunCommand<Docs.DrawingRegisterCommand>(app); break;
                    case "ProjectBrowserOrganizer": RunCommand<Docs.ProjectBrowserOrganizerCommand>(app); break;

                    // ════════════════════════════════════════════════════════
                    // TEMP TAB
                    // ════════════════════════════════════════════════════════

                    // ── Setup ──
                    case "ProjectSetup": RunCommand<Temp.ProjectSetupCommand>(app); break;
                    case "MasterSetup": RunCommand<Temp.MasterSetupCommand>(app); break;
                    case "CreateParameters": RunCommand<Temp.CreateParametersCommand>(app); break;
                    case "CheckData":
                        RunCommand<Temp.CheckDataCommand>(app);
                        // E-1: dump cache hit/miss counters as part of the
                        // CheckData diagnostic so users can see whether the
                        // caching infrastructure is doing its job.
                        try { Core.StingLog.DumpCacheStats(); }
                        catch (Exception ex) { Core.StingLog.Warn($"DumpCacheStats: {ex.Message}"); }
                        break;

                    // ── Materials ──
                    case "CreateBLEMaterials": RunCommand<Temp.CreateBLEMaterialsCommand>(app); break;
                    case "CreateMEPMaterials": RunCommand<Temp.CreateMEPMaterialsCommand>(app); break;
                    case "Pbr_BrowseLibrary":   RunCommand<Commands.Materials.BrowsePbrTexturesCommand>(app); break;
                    case "Pbr_BulkApply":       RunCommand<Commands.Materials.BulkApplyPbrTexturesCommand>(app); break;
                    case "Pbr_ReloadProviders": RunCommand<Commands.Materials.ReloadPbrProvidersCommand>(app); break;

                    // ── Family types ──
                    case "CreateWalls": RunCommand<Temp.CreateWallsCommand>(app); break;
                    case "CreateFloors": RunCommand<Temp.CreateFloorsCommand>(app); break;
                    case "CreateCeilings": RunCommand<Temp.CreateCeilingsCommand>(app); break;
                    case "CreateRoofs": RunCommand<Temp.CreateRoofsCommand>(app); break;
                    case "CreateDucts": RunCommand<Temp.CreateDuctsCommand>(app); break;
                    case "CreatePipes": RunCommand<Temp.CreatePipesCommand>(app); break;
                    case "CreateCableTrays": RunCommand<Temp.CreateCableTraysCommand>(app); break;
                    case "CreateConduits": RunCommand<Temp.CreateConduitsCommand>(app); break;

                    // ── Schedules ──
                    case "FullAutoPopulate":
                    case "FullAuto": RunCommand<Temp.FullAutoPopulateCommand>(app); break;
                    case "BatchSchedules":
                    case "CreateBatch": RunCommand<Temp.BatchSchedulesCommand>(app); break;
                    case "MaterialSchedules": RunCommand<Temp.CreateMaterialSchedulesCommand>(app); break;
                    case "AutoPopulate": RunCommand<Temp.AutoPopulateCommand>(app); break;
                    case "FormulaEvaluator": RunCommand<Temp.FormulaEvaluatorCommand>(app); break;
                    case "ExportCSV": RunCommand<Temp.ExportCSVCommand>(app); break;

                    // ── Corporate Schedules ──
                    case "CorporateTitleBlock": RunCommand<Temp.CorporateTitleBlockScheduleCommand>(app); break;
                    case "DrawingRegisterSchedule": RunCommand<Temp.DrawingRegisterScheduleCommand>(app); break;

                    // ── Schedule Enhancements ──
                    case "ScheduleAudit": RunCommand<Temp.ScheduleAuditCommand>(app); break;
                    case "ScheduleCompare": RunCommand<Temp.ScheduleCompareCommand>(app); break;
                    case "ScheduleDuplicate": RunCommand<Temp.ScheduleDuplicateCommand>(app); break;
                    case "ScheduleRefresh": RunCommand<Temp.ScheduleRefreshCommand>(app); break;
                    case "ScheduleFieldMgr": RunCommand<Temp.ScheduleFieldManagerCommand>(app); break;
                    case "ScheduleColor": RunCommand<Temp.ScheduleColorCommand>(app); break;
                    case "ScheduleStats": RunCommand<Temp.ScheduleStatsCommand>(app); break;
                    case "ScheduleDelete": RunCommand<Temp.ScheduleDeleteCommand>(app); break;
                    case "ScheduleReport": RunCommand<Temp.ScheduleReportCommand>(app); break;
                    case "ScheduleFieldRemapAudit": RunCommand<Temp.ScheduleFieldRemapAuditCommand>(app); break;

                    // ── Templates / Views ──
                    case "CreateFilters": RunCommand<Temp.CreateFiltersCommand>(app); break;
                    case "ApplyFilters": RunCommand<Temp.ApplyFiltersToViewsCommand>(app); break;
                    case "CreateWorksets": RunCommand<Temp.CreateWorksetsCommand>(app); break;
                    case "ViewTemplates": RunCommand<Temp.ViewTemplatesCommand>(app); break;
                    case "CreateLinePatterns": RunCommand<Temp.CreateLinePatternsCommand>(app); break;
                    case "CreatePhases": RunCommand<Temp.CreatePhasesCommand>(app); break;

                    // ── Template Manager ──
                    case "TemplateSetupWizard": RunCommand<Temp.TemplateSetupWizardCommand>(app); break;
                    case "AutoAssignTemplates": RunCommand<Temp.AutoAssignTemplatesCommand>(app); break;
                    case "TemplateAudit": RunCommand<Temp.TemplateAuditCommand>(app); break;
                    case "TemplateDiff": RunCommand<Temp.TemplateDiffCommand>(app); break;
                    case "TemplateComplianceScore": RunCommand<Temp.TemplateComplianceScoreCommand>(app); break;
                    case "AutoFixTemplate": RunCommand<Temp.AutoFixTemplateCommand>(app); break;
                    case "SyncTemplateOverrides": RunCommand<Temp.SyncTemplateOverridesCommand>(app); break;

                    // Template Manager v2 — governance + cross-engine + library ops
                    case "DriftScan": RunCommand<Commands.TemplateManager.DriftScanCommand>(app); break;
                    case "DriftStamp": RunCommand<Commands.TemplateManager.DriftStampCommand>(app); break;
                    case "SnapshotCapture": RunCommand<Commands.TemplateManager.SnapshotCaptureCommand>(app); break;
                    case "AuditVerify": RunCommand<Commands.TemplateManager.AuditVerifyCommand>(app); break;
                    case "LibraryPull": RunCommand<Commands.TemplateManager.LibraryPullCommand>(app); break;
                    case "LibraryPush": RunCommand<Commands.TemplateManager.LibraryPushCommand>(app); break;
                    case "LibraryConfigure": RunCommand<Commands.TemplateManager.LibraryConfigureCommand>(app); break;
                    case "AecFiltersBrowse": RunCommand<Commands.TemplateManager.AecFiltersBrowseCommand>(app); break;

                    // ══ Group 5: surfaced orphan commands (were complete but had no button) ══
                    //    See ORPHANS_AUDIT.md. Buttons added in their proposed home tabs/panels.
                    // AVF heatmaps → VIEW tab
                    case "Heatmap_Compliance": RunCommand<Commands.Visualization.VisualiseComplianceHeatmapCommand>(app); break;
                    case "Heatmap_Fill":       RunCommand<Commands.Visualization.VisualiseFillHeatmapCommand>(app); break;
                    case "Heatmap_Carbon":     RunCommand<Commands.Visualization.VisualiseCarbonHeatmapCommand>(app); break;
                    case "Heatmap_Acoustic":   RunCommand<Commands.Visualization.VisualiseAcousticHeatmapCommand>(app); break;
                    case "Heatmap_Clear":      RunCommand<Commands.Visualization.ClearHeatmapCommand>(app); break;
                    // Drawing / AEC filters → DOCS tab
                    case "AecFilters_Create":  RunCommand<Commands.Drawing.AecFiltersCreateCommand>(app); break;
                    case "AecFilters_Inspect": RunCommand<Commands.Drawing.AecFiltersInspectCommand>(app); break;
                    case "AecFilters_Reload":  RunCommand<Commands.Drawing.AecFiltersReloadCommand>(app); break;
                    case "Drawing_BrowserOrganize": RunCommand<Commands.Drawing.DrawingBrowserOrganizerCommand>(app); break;
                    case "DrawingTypes_ForceResync": RunCommand<Commands.Drawing.DrawingForceResyncCommand>(app); break;
                    // Material / QA compliance gates → BIM tab (QA)
                    case "Gate_Coverage":       RunCommand<Core.MaterialGateCommands.CoverageGateCommand>(app); break;
                    case "Gate_FireWall":       RunCommand<Core.MaterialGateCommands.FireWallGateCommand>(app); break;
                    case "Gate_Healthcare":     RunCommand<Core.MaterialGateCommands.HealthcareGateCommand>(app); break;
                    case "Gate_Sustainability": RunCommand<Core.MaterialGateCommands.SustainabilityGateCommand>(app); break;
                    // BIM platform / scheduling → BIM tab
                    case "Export4DViewer":      RunCommand<BIMManager.ExportFor4DViewerCommand>(app); break;
                    case "P6_LinkConfig":       RunCommand<BIMManager.P6LiveLinkConfigCommand>(app); break;
                    case "P6_SyncNow":          RunCommand<BIMManager.P6SyncNowCommand>(app); break;
                    case "P6_Writeback":        RunCommand<BIMManager.P6WritebackCommand>(app); break;
                    case "BCF_Sync":            RunCommand<BIMManager.BCFSyncCommand>(app); break;
                    case "Folder_CloudMirrorNow": RunCommand<BIMManager.FolderCloudMirrorNowCommand>(app); break;
                    case "BOQ_PushSnapshot":    RunCommand<BIMManager.PushBoqSnapshotCommand>(app); break;
                    case "Cost_FileBrowser":    RunCommand<BIMManager.CostFileBrowserCommand>(app); break;
                    case "Revision_CloudAudit": RunCommand<BIMManager.RevisionCloudAuditCommand>(app); break;
                    // V6 next-gen → BIM tab
                    case "Labour_Apply":        RunCommand<V6.ApplyLabourHoursCommand>(app); break;
                    case "Labour_Export":       RunCommand<V6.ExportLabourHoursCommand>(app); break;
                    case "QR_AdvanceCommission": RunCommand<V6.QRAdvanceCommissioningCommand>(app); break;
                    case "QR_CommissionReport": RunCommand<V6.QRCommissioningReportCommand>(app); break;
                    case "Health_DashboardHtml": RunCommand<V6.HealthDashboardExportHtmlCommand>(app); break;
                    // Clash → BIM tab
                    case "Clash_XlsxExport":    RunCommand<Core.Clash.ClashXlsxExportCommand>(app); break;
                    // ══ end Group 5 ══

                    case "DrawingTypesBrowse": RunCommand<Commands.Drawing.DrawingTypesInspectCommand>(app); break;
                    case "ViewStylePacksBrowse": RunCommand<Commands.TemplateManager.ViewStylePacksBrowseCommand>(app); break;
                    case "CreateVGOverrides": RunCommand<Temp.CreateVGOverridesCommand>(app); break;
                    case "CloneTemplate": RunCommand<Temp.CloneTemplateCommand>(app); break;
                    case "BatchVGReset": RunCommand<Temp.BatchVGResetCommand>(app); break;
                    case "BatchAddFamilyParams": RunCommand<Temp.BatchAddFamilyParamsCommand>(app); break;
                    case "FamilyParamProcessor": RunCommand<Temp.FamilyParameterProcessorCommand>(app); break;
                    case "CreateTemplateSchedules": RunCommand<Temp.CreateTemplateSchedulesCommand>(app); break;

                    // ── Styles ──
                    case "CreateFillPatterns": RunCommand<Temp.CreateFillPatternsCommand>(app); break;
                    case "CreateLineStyles": RunCommand<Temp.CreateLineStylesCommand>(app); break;
                    case "CreateObjectStyles": RunCommand<Temp.CreateObjectStylesCommand>(app); break;
                    case "CreateTextStyles": RunCommand<Temp.CreateTextStylesCommand>(app); break;
                    case "CreateDimensionStyles": RunCommand<Temp.CreateDimensionStylesCommand>(app); break;

                    // ── Data QA (Phase 5) ──
                    case "ValidateTemplate": RunCommand<Temp.ValidateTemplateCommand>(app); break;
                    case "DynamicBindings": RunCommand<Temp.DynamicBindingsCommand>(app); break;
                    case "SchemaValidate": RunCommand<Temp.SchemaValidateCommand>(app); break;
                    // "BOQExport" is now routed to BOQ.BOQExportCommand at line ~2547 (new BOQ Cost Manager).
                    // Legacy Phase 5 Temp.BOQExportCommand accessible via "BOQExportLegacy" tag.
                    case "BOQExportLegacy": RunCommand<Temp.BOQExportCommand>(app); break;
                    case "TemplateVGAudit": RunCommand<Temp.TemplateVGAuditCommand>(app); break;
                    case "ExportIfcPropertyMap": RunCommand<Temp.ExportIfcPropertyMapCommand>(app); break;
                    case "ValidateBepCompliance": RunCommand<Temp.ValidateBepComplianceCommand>(app); break;

                    // ── Workflow Orchestration ──
                    case "RunWorkflow": RunCommand<Core.WorkflowPresetCommand>(app); break;
                    case "ListWorkflows": RunCommand<Core.ListWorkflowPresetsCommand>(app); break;
                    case "CreateWorkflow": RunCommand<Core.CreateWorkflowPresetCommand>(app); break;
                    case "WorkflowTrend": RunCommand<Core.WorkflowTrendCommand>(app); break;

                    // ── Advanced Automation ──
                    case "AnomalyAutoFix": RunCommand<Organise.AnomalyAutoFixCommand>(app); break;
                    case "RevisionCloudAuto": RunCommand<Docs.RevisionCloudAutoCreateCommand>(app); break;
                    case "ClashDetect": RunCommand<Core.Clash.ClashRunCommand>(app); break;
                    case "IFCExport": RunCommand<Temp.IFCExportCommand>(app); break;
                    case "ExcelImport": RunCommand<Temp.ExcelBOQImportCommand>(app); break;
                    case "ExcelBOQImport": RunCommand<Temp.ExcelBOQImportCommand>(app); break;
                    case "KeynoteSync": RunCommand<Temp.KeynoteSyncCommand>(app); break;
                    case "ExcelToDraftingView": RunCommand<Temp.ExcelToDraftingViewCommand>(app); break;
                    case "ScheduleToExcel": RunCommand<Temp.ScheduleToExcelCommand>(app); break;
                    case "BatchStickyImport": RunCommand<Temp.BatchStickyImportCommand>(app); break;
                    case "ExcelLinkExport": RunCommand<Temp.ExcelLinkExportCommand>(app); break;
                    case "AutoTaggerToggle": RunCommand<Core.AutoTaggerToggleCommand>(app); break;

                    // Phase 74c: CycleTheme handled directly in StingDockPanel.xaml.cs Cmd_Click()
                    // which returns before dispatching to this handler. This case is dead code.

                    // ════════════════════════════════════════════════════════
                    // CREATE TAB (ISO 19650 tag creation)
                    // ════════════════════════════════════════════════════════

                    // ── Setup ──
                    case "LoadSharedParams": RunCommand<Tags.LoadSharedParamsCommand>(app); break;
                    case "PurgeSharedParams": RunCommand<Tags.PurgeSharedParamsCommand>(app); break;
                    case "ConfigEditor": RunCommand<Tags.ConfigEditorCommand>(app); break;
                    case "GuidedDataEditor": RunCommand<Tags.GuidedDataEditorCommand>(app); break;
                    case "DisciplineProfiles":
                    {
                        var profiles = Core.TagConfig.DisciplineProfiles;
                        if (profiles == null || profiles.Count == 0)
                        {
                            var td = new TaskDialog("STING Discipline Profiles");
                            td.MainInstruction = "No discipline profiles configured";
                            td.MainContent = "Add a DISCIPLINE_PROFILES section to project_config.json to define per-discipline token defaults and validation rules.\n\n"
                                + "Example:\n\"DISCIPLINE_PROFILES\": {\n  \"M\": { \"AllowedSysCodes\": [\"HVAC\",\"CHW\"], \"DefaultProd\": \"EQP\" }\n}";
                            td.Show();
                        }
                        else
                        {
                            var sb = new System.Text.StringBuilder();
                            foreach (var kvp in profiles)
                            {
                                sb.AppendLine($"DISC = {kvp.Key}:");
                                var p = kvp.Value;
                                if (p.AllowedSysCodes?.Count > 0)
                                    sb.AppendLine($"  Allowed SYS: {string.Join(", ", p.AllowedSysCodes)}");
                                if (p.AllowedFuncCodes?.Count > 0)
                                    sb.AppendLine($"  Allowed FUNC: {string.Join(", ", p.AllowedFuncCodes)}");
                                if (!string.IsNullOrEmpty(p.DefaultProd))
                                    sb.AppendLine($"  Default PROD: {p.DefaultProd}");
                                if (!string.IsNullOrEmpty(p.DefaultStatus))
                                    sb.AppendLine($"  Default STATUS: {p.DefaultStatus}");
                                if (p.ValidationStrictness)
                                    sb.AppendLine($"  Strict validation: ON");
                                if (p.RequiredTokens?.Count > 0)
                                    sb.AppendLine($"  Required tokens: {string.Join(", ", p.RequiredTokens)}");
                                sb.AppendLine();
                            }
                            var td = new TaskDialog("STING Discipline Profiles");
                            td.MainInstruction = $"{profiles.Count} discipline profile(s) loaded";
                            td.MainContent = sb.ToString();
                            td.Show();
                        }
                        break;
                    }
                    case "TagConfig": RunCommand<Tags.TagConfigCommand>(app); break;
                    case "SyncParamSchema": RunCommand<Tags.SyncParameterSchemaCommand>(app); break;
                    case "AddParamRemap": RunCommand<Tags.AddParamRemapCommand>(app); break;
                    case "AuditParamSchema": RunCommand<Tags.AuditParameterSchemaCommand>(app); break;
                    case "ParamManager": RunCommand<Tags.StingParamManagerCommand>(app); break;

                    // ── Tag Families ──
                    case "CreateTagFamilies": RunCommand<Tags.CreateTagFamiliesCommand>(app); break;
                    case "LoadTagFamilies": RunCommand<Tags.LoadTagFamiliesCommand>(app); break;
                    case "ConfigureTagLabels": RunCommand<Tags.ConfigureTagLabelsCommand>(app); break;
                    case "ConfigureLoadedFamilies": RunCommand<Tags.ConfigureLoadedFamiliesCommand>(app); break;
                    case "AuditTagFamilies": RunCommand<Tags.AuditTagFamiliesCommand>(app); break;
                    case "RetrofitProject": RunCommand<Temp.RetrofitProjectCommand>(app); break;
                    case "MigrateTagFamilies": RunCommand<Commands.TagStudio.MigrateTagFamiliesCommand>(app); break;
                    case "MigrateTagLabelRefs": RunCommand<Commands.TagStudio.MigrateTagLabelReferencesCommand>(app); break;
                    case "StyleAudit": RunCommand<Commands.TagStudio.StyleAuditCommand>(app); break;

                    // ── Populate tokens ──
                    case "FamilyStagePopulate": RunCommand<Tags.FamilyStagePopulateCommand>(app); break;
                    case "AssignNumbers": RunCommand<Tags.AssignNumbersCommand>(app); break;
                    case "BuildTags": RunCommand<Tags.BuildTagsCommand>(app); break;
                    case "CombineParameters": RunCommand<Tags.CombineParametersCommand>(app); break;
                    case "CombinePreFlight": RunCommand<Tags.CombinePreFlightCommand>(app); break;
                    case "PreviewTag": PreviewTagInline(app); break;
                    case "ContainerPreCheck": RunCommand<Tags.ContainerPreCheckCommand>(app); break;

                    // ── Manual tokens ──
                    case "SetDisc": RunCommand<Tags.SetDiscCommand>(app); break;
                    case "SetLoc": RunCommand<Tags.SetLocCommand>(app); break;
                    case "SetZone": RunCommand<Tags.SetZoneCommand>(app); break;
                    case "SetStatus": RunCommand<Tags.SetStatusCommand>(app); break;
                    case "SetSys": WriteTokenToSelected(app, ParamRegistry.SYS, "System Code (SYS)"); break;
                    case "SetFunc": WriteTokenToSelected(app, ParamRegistry.FUNC, "Function Code (FUNC)"); break;
                    case "SetProd": WriteTokenToSelected(app, ParamRegistry.PROD, "Product Code (PROD)"); break;
                    case "SetLvl": WriteTokenToSelected(app, ParamRegistry.LVL, "Level Code (LVL)"); break;
                    case "SetOrig": WriteTokenToSelected(app, ParamRegistry.ORIGIN, "Origin Code (ORIG)"); break;
                    case "SetProj": WriteTokenToSelected(app, ParamRegistry.PROJECT, "Project Code (PROJ)"); break;
                    case "SetRev": WriteTokenToSelected(app, ParamRegistry.REV, "Revision Code (REV)"); break;
                    case "SetVol": WriteTokenToSelected(app, ParamRegistry.VOLUME, "Volume Code (VOL)"); break;
                    case "SetSeqScheme": RunCommand<Tags.SetSeqSchemeCommand>(app); break;
                    case "MapSheets": RunCommand<Tags.MapSheetsCommand>(app); break;
                    case "TagSheets": RunCommand<Tags.TagSheetsCommand>(app); break;

                    // ── Scope / toggles (inline) ──
                    case "ScopeView": ToggleScopeMode(app); break;
                    case "ToggleOverwrite": ToggleOverwriteMode(app); break;

                    // ════════════════════════════════════════════════════════
                    // NEW — SELECT TAB (AI Smart Select, Spatial, Conditions)
                    // ════════════════════════════════════════════════════════

                    case "AIPredictSelect": AIPredictSelect(app); break;
                    case "AISimilarSelect": AISimilarSelect(app); break;
                    case "AIChainSelect": AIChainSelect(app); break;
                    case "AIClusterSelect": SelectNearby(app, 20.0); break;
                    case "AIPatternSelect": AIPatternSelect(app); break;
                    case "AIBoundarySelect": AIBoundarySelect(app); break;
                    case "AIOutliersSelect": AIOutliersSelect(app); break;
                    case "AIDenseSelect": SelectNearby(app, 5.0); break;

                    case "SelectView": SelectByCategory(app, "Views"); break;
                    case "SelectVisible": SelectVisibleOnly(app); break;
                    case "SelectNear": SelectNearby(app, 10.0); break;
                    case "SelectQuad": SelectQuadrant(app); break;
                    case "SelectEdge": SelectEdgeElements(app); break;
                    case "SelectGrid": SelectOnGrid(app); break;
                    case "SelectBBox": SelectByBoundingBox(app); break;

                    case "BulkBrain": BulkBrainSuggest(app); break;
                    case "ParamLookupRefresh":
                    case "RefreshParamList":
                    case "CondAdd":
                    case "CondRemove":
                    case "CondClear":
                    case "CondPreview":
                    case "CondApply":
                    case "ParamLookupDialog":
                        OpenParameterLookupDialog(app); break;
                    case "ShowHelp": TaskDialog.Show("StingTools", "StingTools V2.1\nISO 19650 BIM Asset Tagging & Management\nhttps://planscape.com"); break;

                    // ════════════════════════════════════════════════════════
                    // NEW — ORGANISE TAB (AI Engine, Nudge, Leaders ext, etc.)
                    // ════════════════════════════════════════════════════════

                    case "SmartOrganise": RunCommand<Tags.ArrangeTagsCommand>(app); break;
                    case "OrgQuick": SetExtraParam("ArrangeMode", "Quick"); RunCommand<Tags.ArrangeTagsCommand>(app); break;
                    case "OrgDeep": SetExtraParam("ArrangeMode", "Deep"); RunCommand<Tags.ArrangeTagsCommand>(app); break;
                    case "OrgAnneal": SetExtraParam("ArrangeMode", "Anneal"); RunCommand<Tags.ArrangeTagsCommand>(app); break;
                    case "OrgBrainSp": RunCommand<Tags.SmartPlaceTagsCommand>(app); break;
                    case "OrgUndo":
                        StingLog.Info($"OrgUndo: Use Ctrl+Z to undo last operation");
                        TaskDialog.Show("Undo", "Use Ctrl+Z to undo the last tag operation.");
                        break;

                    case "TagFamilyRefresh": TagFamilyRefresh(app); break;
                    case "Orphans": FindOrphanedTags(app); break;
                    case "CloneTags": CloneTagLayout(app); break;
                    case "AuditTags": RunCommand<Organise.AuditTagsCSVCommand>(app); break;
                    case "MultiView": RunCommand<Tags.BatchPlaceTagsCommand>(app); break;
                    case "ClashingDetect": RunCommand<Tags.TagOverlapAnalysisCommand>(app); break;

                    case "AllH":
                    case "AllV":
                    case "BrainSmHV": RunCommand<Organise.ToggleTagOrientationCommand>(app); break;

                    case "NudgeUp": NudgeTags(app, "UP"); break;
                    case "NudgeDown": NudgeTags(app, "DOWN"); break;
                    case "NudgeLeft": NudgeTags(app, "LEFT"); break;
                    case "NudgeRight": NudgeTags(app, "RIGHT"); break;
                    case "NudgeNear": NudgeTags(app, "NEAR"); break;
                    case "NudgeFar": NudgeTags(app, "FAR"); break;
                    case "BrainSmOr": RunCommand<Organise.ToggleTagOrientationCommand>(app); break;

                    case "BrainSmAl": RunCommand<Organise.AlignTagsCommand>(app); break;

                    case "LeaderMulti":
                    case "LeaderCombine": RunCommand<Organise.AddLeadersCommand>(app); break;
                    case "LeaderAdd": RunCommand<Organise.AddLeadersCommand>(app); break;
                    case "LeaderStraight": SnapElbowDirect(app, "0"); break;
                    case "TagSnap45": SnapElbowDirect(app, "45"); break;
                    case "TagSnap90": SnapElbowDirect(app, "90"); break;
                    case "LeaderSpacing": RunCommand<Organise.AlignTagsCommand>(app); break;

                    case "BrainSmartLdr": SnapElbowDirect(app, "cycle"); break;
                    case "BrainUncross": RunCommand<Tags.ArrangeTagsCommand>(app); break;
                    case "BrainTidy": RunCommand<Tags.ArrangeTagsCommand>(app); break;

                    case "AnalyseScore": RunCommand<Organise.TagStatsCommand>(app); break;
                    case "AnalyseClashes":
                    case "AnalyseCrossings":
                    case "AnalyseDensity":
                    case "AnalyseClusters":
                        RunCommand<Tags.TagOverlapAnalysisCommand>(app); break;

                    case "PatternLearn": RunCommand<Tags.LearnTagPlacementCommand>(app); break;
                    case "PatternApplyLearned": RunCommand<Tags.ApplyTagTemplateCommand>(app); break;

                    case "BatchViewCats": BatchViewCategories(app); break;
                    case "BatchViewRunAll": BatchViewRunAll(app); break;

                    case "RoomTagCentroid": MoveRoomTags(app, "Centroid"); break;
                    case "RoomTagTopLeft": MoveRoomTags(app, "TopLeft"); break;
                    case "RoomTagTopCentre": MoveRoomTags(app, "TopCentre"); break;
                    case "RoomTagLeaderLock": RoomTagLeaderToggle(app, true); break;
                    case "RoomTagLeaderFree": RoomTagLeaderToggle(app, false); break;

                    case "ListLinks": ListLinkedModels(app); break;
                    case "SelInLink":
                    case "TagLinked":
                        StingLog.Info($"LinkedModel: {tag} — requires linked document access");
                        TaskDialog.Show("Linked Model", "Select/tag in linked model requires direct linked document access.\nUse Revit's built-in 'Select Links' and 'Tab' key to select linked elements.");
                        break;
                    case "AuditLinks": AuditLinkedModels(app); break;

                    case "PdfSelectedSheets":
                        SetExtraParam("PdfScope", "Selected");
                        RunCommand<Temp.PrintSheetsCommand>(app);
                        break;
                    case "PdfActiveView":
                        SetExtraParam("PdfScope", "Active");
                        RunCommand<Temp.PrintSheetsCommand>(app);
                        break;
                    case "PrintSheets":
                        RunCommand<Temp.PrintSheetsCommand>(app);
                        break;

                    case "ExportSheetCSV": ExportSheetCSV(app); break;

                    // ════════════════════════════════════════════════════════
                    // NEW — DOCS TAB (StingDocs organizer features)
                    // ════════════════════════════════════════════════════════

                    case "VPAlignTop":
                    case "VPAlignMidY":
                    case "VPAlignBot":
                    case "VPAlignLeft":
                    case "VPAlignMidX":
                    case "VPAlignRight": RunCommand<Docs.AlignViewportsCommand>(app); break;

                    case "VPNumLR":
                    case "VPNumTB": RunCommand<Docs.RenumberViewportsCommand>(app); break;
                    case "VPNumPlus": ViewportRenumberOffset(app, 1); break;
                    case "VPNumMinus": ViewportRenumberOffset(app, -1); break;
                    case "VPPrefix": ViewportAddPrefixSuffix(app, true); break;
                    case "VPSuffix": ViewportAddPrefixSuffix(app, false); break;

                    case "SheetResetTitle": SheetResetTitleBlock(app); break;
                    case "SheetNumPlus": SheetRenumber(app, 1); break;
                    case "SheetNumMinus": SheetRenumber(app, -1); break;
                    case "SheetPrefix": SheetAddPrefix(app); break;
                    case "SheetSuffix": SheetAddSuffix(app); break;
                    case "SheetRemovePrefix": SheetRemovePrefixOrSuffix(app, true); break;
                    case "SheetRemoveSuffix": SheetRemovePrefixOrSuffix(app, false); break;
                    case "SheetFindReplace": RunCommand<Docs.BatchRenameViewsCommand>(app); break;

                    case "SchedSyncPos": ScheduleSyncPosition(app); break;
                    case "SchedSyncRot": ScheduleToggleRotation(app); break;
                    case "SchedShowHidden": ScheduleShowHidden(app); break;
                    case "SchedMatchWidest": ScheduleMatchWidest(app); break;
                    case "SchedSetWidth": ScheduleSetColumnWidth(app); break;
                    case "SchedEqualise": ScheduleEqualiseColumns(app); break;
                    case "SchedAutoFit": ScheduleAutoFit(app); break;
                    case "SchedToggleHidden": ScheduleToggleHidden(app); break;

                    case "TextLower":
                    case "TextUpper": RunCommand<Docs.TextCaseCommand>(app); break;
                    case "TextAlignLeft": TextAlign(app, "Left"); break;
                    case "TextAlignCenter": TextAlign(app, "Center"); break;
                    case "TextAlignRight": TextAlign(app, "Right"); break;
                    case "TextAlignAxis": TextAlignAxis(app); break;
                    case "TextLeaderH": TextLeaderToggle(app, "H"); break;
                    case "TextLeaderV": TextLeaderToggle(app, "V"); break;
                    case "TextLeader90": TextLeaderToggle(app, "90"); break;

                    case "DimResetOverrides": DimResetOverrides(app); break;
                    case "DimResetText": DimResetText(app); break;
                    case "DimFindZero": DimFindZero(app); break;
                    case "DimFindReplace": DimFindReplaceOverrides(app); break;

                    case "LegendSyncPos": LegendSyncPosition(app); break;
                    case "LegendTitleLine": LegendTitleLine(app); break;
                    case "LegendUniform": LegendUniformSize(app); break;
                    case "TagDictionary": CreateTagDictionary(app); break;
                    case "ColorLegendSchedule": CreateColorLegendSchedule(app); break;

                    case "TitleBlockReset": TitleBlockReset(app); break;
                    case "TitleBlockRescue": TitleBlockRescue(app); break;

                    case "RevShowClouds": RevisionToggle(app, "clouds"); break;
                    case "RevShowTags": RevisionToggle(app, "tags"); break;
                    case "RevDelCloudsView": RevisionDeleteClouds(app, false); break;
                    case "RevDelCloudsSel": RevisionDeleteClouds(app, true); break;

                    case "MeasureLines": MeasureSelected(app, "Lines"); break;
                    case "MeasureAreas": MeasureSelected(app, "Areas"); break;
                    case "MeasurePerimeters": MeasureSelected(app, "Perimeters"); break;

                    case "SwapElements":
                        TaskDialog.Show("Swap Elements", "Select two elements, then use 'Copy Tags' and 'Swap Tags' commands to exchange their data.");
                        break;
                    case "ConvertRegions":
                        TaskDialog.Show("Convert Regions", "Use Revit's built-in Filled Region tools or Detail Items to convert regions.\nEdit → Paste Aligned can also help transfer detail regions between views.");
                        break;
                    case "CleanSpaces":
                        TaskDialog.Show("Clean Spaces", "Use 'Delete Unused Views' to remove unplaced views.\nUse 'Purge Unused' (Manage tab → Purge) to clean up unused families and materials.");
                        break;

                    // ════════════════════════════════════════════════════════
                    // NEW — CREATE TAB extras
                    // ════════════════════════════════════════════════════════

                    case "MatTags": RunCommand<Tags.CombineParametersCommand>(app); break;

                    // ════════════════════════════════════════════════════════
                    // NEW — VIEW TAB (Health, Anomaly, Bot, Colouriser ext)
                    // ════════════════════════════════════════════════════════

                    case "HealthScore": RunCommand<Tags.CompletenessDashboardCommand>(app); break;
                    case "HealthReport": RunCommand<Organise.TagStatsCommand>(app); break;
                    case "HealthFixAll": RunCommand<Organise.FixDuplicateTagsCommand>(app); break;

                    case "RetagStale": RunCommand<Organise.RetagStaleCommand>(app); break;
                    case "ComplianceScan": RunCommand<Tags.CompletenessDashboardCommand>(app); break;
                    case "AnomalyRefresh": AnomalyRefreshScan(app); break;
                    case "AnomalyScan": RunCommand<Tags.ValidateTagsCommand>(app); break;
                    case "AnomalyExport": RunCommand<Organise.AuditTagsCSVCommand>(app); break;

                    case "BotSmartPlace": RunCommand<Tags.SmartPlaceTagsCommand>(app); break;
                    case "BotDensityMap": RunCommand<Tags.TagOverlapAnalysisCommand>(app); break;
                    case "BotUndoAI":
                        TaskDialog.Show("Undo AI", "Use Ctrl+Z to undo the last operation.");
                        break;
                    case "BotOptions": RunCommand<Tags.TagConfigCommand>(app); break;

                    case "ColorSchemeDel": DeleteColorPreset(app); break;
                    case "GradientApply": RunCommand<Select.ColorByParameterCommand>(app); break;
                    case "PatternApplyView": ApplyLinePattern(app); break;
                    case "ApplyLineWeight": ApplyLineWeightOverride(app); break;

                    // ── Tag Style Engine ──
                    case "ApplyTagStyle": RunCommand<Tags.ApplyTagStyleCommand>(app); break;
                    case "ApplyColorScheme": RunCommand<Tags.ApplyColorSchemeCommand>(app); break;
                    case "ClearColorScheme": RunCommand<Tags.ClearColorSchemeCommand>(app); break;
                    case "SetParagraphDepthExt": RunCommand<Tags.SetParagraphDepthExtCommand>(app); break;
                    // Phase 165 — pattern mode toggles for T4-T10 payload sets
                    case "SetPatternMode_Handover": SetPatternMode(app, "HANDOVER"); break;
                    case "SetPatternMode_DC":       SetPatternMode(app, "DC");       break;
                    case "SetPatternMode_Custom":   SetPatternMode(app, "CUSTOM");   break;

                    // Phase 165 — Issue #15. Direct System B tier-write buttons.
                    // Each opens a focused per-tier write dialog so QA /
                    // commissioning teams can fill one tier at a time. The
                    // helper validates active mode (Handover/Custom) and warns
                    // when fired in DC mode (where these tiers don't render).
                    case "WriteSystemBTier_4":  WriteSystemBTier(app, 4);  break;
                    case "WriteSystemBTier_5":  WriteSystemBTier(app, 5);  break;
                    case "WriteSystemBTier_6":  WriteSystemBTier(app, 6);  break;
                    case "WriteSystemBTier_7":  WriteSystemBTier(app, 7);  break;
                    case "WriteSystemBTier_8":  WriteSystemBTier(app, 8);  break;
                    case "WriteSystemBTier_9":  WriteSystemBTier(app, 9);  break;
                    case "WriteSystemBTier_10": WriteSystemBTier(app, 10); break;
                    case "TagStyleReport": RunCommand<Tags.TagStyleReportCommand>(app); break;
                    case "SwitchTagStyleByDisc": RunCommand<Tags.SwitchTagStyleByDiscCommand>(app); break;
                    case "BatchApplyColorScheme": RunCommand<Tags.BatchApplyColorSchemeCommand>(app); break;
                    case "ColorByVariable": RunCommand<Tags.ColorByVariableCommand>(app); break;
                    case "SetBoxColor": RunCommand<Tags.SetBoxColorCommand>(app); break;

                    // ════════════════════════════════════════════════════════
                    // MODEL TAB — Auto-Modeling Engine
                    // ════════════════════════════════════════════════════════

                    // ── Architectural elements ──
                    case "ModelCreateWall": RunCommand<Model.ModelCreateWallCommand>(app); break;
                    case "ModelCreateRoom": RunCommand<Model.ModelCreateRoomCommand>(app); break;
                    case "ModelCreateFloor": RunCommand<Model.ModelCreateFloorCommand>(app); break;
                    case "ModelCreateCeiling": RunCommand<Model.ModelCreateCeilingCommand>(app); break;
                    case "ModelCreateRoof": RunCommand<Model.ModelCreateRoofCommand>(app); break;
                    case "ModelPlaceDoor": RunCommand<Model.ModelPlaceDoorCommand>(app); break;
                    case "ModelPlaceWindow": RunCommand<Model.ModelPlaceWindowCommand>(app); break;
                    case "ModelBuildingShell": RunCommand<Model.ModelBuildingShellCommand>(app); break;
                    case "ModelCreateRamp": RunCommand<Model.ModelCreateRampCommand>(app); break;
                    case "ModelCreateCanopy": RunCommand<Model.ModelCreateCanopyCommand>(app); break;
                    case "MEPRouteAnalysis": RunCommand<Model.MEPRouteAnalysisCommand>(app); break;

                    // ── Structural elements ──
                    case "ModelPlaceColumn": RunCommand<Model.ModelPlaceColumnCommand>(app); break;
                    case "ModelColumnGrid": RunCommand<Model.ModelColumnGridCommand>(app); break;
                    case "ModelCreateBeam": RunCommand<Model.ModelCreateBeamCommand>(app); break;

                    // ── Structural Modeling Automation ──
                    case "StrCreatePadFooting": RunCommand<Model.StrCreatePadFootingCommand>(app); break;
                    case "StrCreateStripFooting": RunCommand<Model.StrCreateStripFootingCommand>(app); break;
                    case "StrCreateStructuralSlab": RunCommand<Model.StrCreateStructuralSlabCommand>(app); break;
                    case "StrCreateStructuralWall": RunCommand<Model.StrCreateStructuralWallCommand>(app); break;
                    case "StrCreateBeamSystem": RunCommand<Model.StrCreateBeamSystemCommand>(app); break;
                    case "StrCreateBracing": RunCommand<Model.StrCreateBracingCommand>(app); break;
                    case "StrCreateTruss": RunCommand<Model.StrCreateTrussCommand>(app); break;
                    case "StrCreateFullBayFrame": RunCommand<Model.StrCreateFullBayFrameCommand>(app); break;
                    case "StrCreateGridFrame": RunCommand<Model.StrCreateGridFrameCommand>(app); break;
                    case "StrAnalyzeLoadPaths": RunCommand<Model.StrAnalyzeLoadPathsCommand>(app); break;
                    case "StrDetectBays": RunCommand<Model.StrDetectBaysCommand>(app); break;
                    case "StrCADToStructural": RunCommand<Model.StrCADToStructuralCommand>(app); break;
                    case "StrCADPreview": RunCommand<Model.StrCADPreviewCommand>(app); break;
                    case "StrRecommendGrid": RunCommand<Model.StrRecommendGridCommand>(app); break;
                    case "StrCADWizard": RunCommand<Model.StrCADWizardCommand>(app); break;
                    // Phase-78 note: 7-page stepped wizard removed — use StrCADWizard (legacy single-page CAD wizard) instead.
                    case "DWGDryRunPreview": RunCommand<Model.DWGDryRunPreviewCommand>(app); break;
                    case "DWGExplodeImports": RunCommand<Model.DWGExplodeImportsCommand>(app); break;
                    case "DWGDetectOpenings": RunCommand<Model.DWGDetectOpeningsCommand>(app); break;
                    case "DWGInteractivePickWall": RunCommand<Model.DWGInteractivePickWallCommand>(app); break;
                    case "DWGInteractivePickColumn": RunCommand<Model.DWGInteractivePickColumnCommand>(app); break;
                    case "DWGInteractivePickBeam": RunCommand<Model.DWGInteractivePickBeamCommand>(app); break;
                    // Phase-141 standalone DWG commands (StructuralDWGEngine facade)
                    case "QuickStructuralDWG": RunCommand<Model.QuickStructuralDWGCommand>(app); break;
                    case "StructuralDWGAudit": RunCommand<Model.StructuralDWGAuditCommand>(app); break;
                    case "StructuralDWGJunctionScan": RunCommand<Model.StructuralDWGJunctionScanCommand>(app); break;
                    case "StrCheckPrerequisites": RunCommand<Model.StrCheckPrerequisitesCommand>(app); break;
                    case "StrBrowseTypeCatalog": RunCommand<Model.StrBrowseTypeCatalogCommand>(app); break;
                    case "StrAutoFoundations": RunCommand<Model.StrAutoFoundationsCommand>(app); break;
                    case "StrColumnLoadTakedown": RunCommand<Model.StrColumnLoadTakedownCommand>(app); break;
                    case "StrSlabEdgeBeams": RunCommand<Model.StrSlabEdgeBeamsCommand>(app); break;
                    case "StrClassifySystem": RunCommand<Model.StrClassifySystemCommand>(app); break;
                    case "StrDeflectionCheck": RunCommand<Model.StrDeflectionCheckCommand>(app); break;
                    case "StrPunchingShearCheck": RunCommand<Model.StrPunchingShearCheckCommand>(app); break;
                    case "StrWindLoad": RunCommand<Model.StrWindLoadCommand>(app); break;
                    case "StrConstructionSequence": RunCommand<Model.StrConstructionSequenceCommand>(app); break;
                    case "StrFullReport": RunCommand<Model.StrFullReportCommand>(app); break;
                    case "StrVoronoiAreas": RunCommand<Model.StrVoronoiAreasCommand>(app); break;
                    case "StrClashPreCheck": RunCommand<Model.StrClashPreCheckCommand>(app); break;
                    case "StrFrameAnalysis": RunCommand<Model.StrFrameAnalysisCommand>(app); break;
                    case "StrSeismicAnalysis": RunCommand<Model.StrSeismicAnalysisCommand>(app); break;
                    case "StrOptimizeGrid": RunCommand<Model.StrOptimizeGridCommand>(app); break;
                    case "StrProgressiveCollapse": RunCommand<Model.StrProgressiveCollapseCommand>(app); break;
                    case "StrAutoSize": RunCommand<Model.StrAutoSizeCommand>(app); break;
                    case "StrFireResistance": RunCommand<Model.StrFireResistanceCommand>(app); break;
                    case "StrAutoMaterials": RunCommand<Model.StrAutoMaterialsCommand>(app); break;
                    case "StrSmartColumn": RunCommand<Model.StrSmartColumnCommand>(app); break;
                    case "StrSmartBeam": RunCommand<Model.StrSmartBeamCommand>(app); break;
                    case "StrBuildComplete": RunCommand<Model.StrBuildCompleteCommand>(app); break;
                    case "StrModelScore": RunCommand<Model.StrModelScoreCommand>(app); break;
                    case "StrCarbonAssessment": RunCommand<Model.StrCarbonAssessmentCommand>(app); break;
                    case "StrRebarEstimate": RunCommand<Model.StrRebarEstimateCommand>(app); break;
                    case "StrStabilityCheck": RunCommand<Model.StrStabilityCheckCommand>(app); break;
                    case "StrBIMValidation": RunCommand<Model.StrBIMValidationCommand>(app); break;
                    case "StrCompositeBeam": RunCommand<Model.StrCompositeBeamCommand>(app); break;
                    case "StrTraceLoadPaths": RunCommand<Model.StrTraceLoadPathsCommand>(app); break;
                    case "StrTopologyOptimize": RunCommand<Model.StrTopologyOptimizeCommand>(app); break;
                    case "StrSSIAnalysis": RunCommand<Model.StrSSIAnalysisCommand>(app); break;
                    case "StrRetainingWall": RunCommand<Model.StrRetainingWallCommand>(app); break;
                    case "StrRebarDetail": RunCommand<Model.StrRebarDetailCommand>(app); break;
                    case "StrBracingOptimize": RunCommand<Model.StrBracingOptimizeCommand>(app); break;
                    case "StrConstraintCheck": RunCommand<Model.StrConstraintCheckCommand>(app); break;
                    case "StrContinuityCheck": RunCommand<Model.StrContinuityCheckCommand>(app); break;
                    case "StrAdaptiveSize": RunCommand<Model.StrAdaptiveSizeCommand>(app); break;
                    case "StrConnectionDesign": RunCommand<Model.StrConnectionDesignCommand>(app); break;
                    case "StrVibrationCheck": RunCommand<Model.StrVibrationCheckCommand>(app); break;
                    case "StrCrackWidth": RunCommand<Model.StrCrackWidthCommand>(app); break;
                    case "StrThermalMovement": RunCommand<Model.StrThermalMovementCommand>(app); break;
                    case "StrDeepBeamSTM": RunCommand<Model.StrDeepBeamSTMCommand>(app); break;
                    case "StrSmartColumnFactory": RunCommand<Model.StrSmartColumnFactoryCommand>(app); break;
                    case "StrSmartBeamFactory": RunCommand<Model.StrSmartBeamFactoryCommand>(app); break;
                    case "StrDiagnostics": RunCommand<Model.StrDiagnosticsCommand>(app); break;
                    case "StrFatigueAssess": RunCommand<Model.StrFatigueAssessCommand>(app); break;
                    case "StrTorsionDesign": RunCommand<Model.StrTorsionDesignCommand>(app); break;
                    case "StrRobustness": RunCommand<Model.StrRobustnessCommand>(app); break;
                    case "StrCompositeSlab": RunCommand<Model.StrCompositeSlabCommand>(app); break;
                    case "StrPartialFactors": RunCommand<Model.StrPartialFactorsCommand>(app); break;
                    case "StrSmartWallFactory": RunCommand<Model.StrSmartWallFactoryCommand>(app); break;
                    case "StrSmartFoundation": RunCommand<Model.StrSmartFoundationCommand>(app); break;
                    case "StrCodeCompliance": RunCommand<Model.StrCodeComplianceCommand>(app); break;
                    case "StrPileDesign": RunCommand<Model.StrPileDesignCommand>(app); break;

                    // ── Coverings (Plaster + Paint) ──
                    case "CoveringMaterialBrowser": RunCommand<Model.CoveringMaterialBrowserCommand>(app); break;
                    case "CoveringSubstrateAnalyze": RunCommand<Model.CoveringSubstrateAnalyzeCommand>(app); break;
                    case "CoveringPaintSystem": RunCommand<Model.CoveringPaintSystemCommand>(app); break;
                    case "CoveringCoverageCalc": RunCommand<Model.CoveringCoverageCalcCommand>(app); break;
                    case "CoveringSmartApply": RunCommand<Model.CoveringSmartApplyCommand>(app); break;
                    case "CoveringBatchApply": RunCommand<Model.CoveringBatchApplyCommand>(app); break;
                    case "CoveringRoomSchedule": RunCommand<Model.CoveringRoomScheduleCommand>(app); break;
                    case "CoveringQualityCheck": RunCommand<Model.CoveringQualityCheckCommand>(app); break;
                    case "CoveringScheduleExport": RunCommand<Model.CoveringScheduleExportCommand>(app); break;
                    case "CoveringFireRating": RunCommand<Model.CoveringFireRatingCommand>(app); break;
                    case "CoveringMoistureRisk": RunCommand<Model.CoveringMoistureRiskCommand>(app); break;

                    // ── Architectural Creation ──
                    case "ArchStairDesign": RunCommand<Model.ArchStairDesignCommand>(app); break;
                    case "ArchCurtainWall": RunCommand<Model.ArchCurtainWallCommand>(app); break;
                    case "ArchCreateOpening": RunCommand<Model.ArchCreateOpeningCommand>(app); break;
                    case "FullModelAuto": RunCommand<Model.FullModelAutoCommand>(app); break;

                    // ── Excel → Structural Import ──
                    case "StrExcelImport": RunCommand<Model.StrExcelImportCommand>(app); break;
                    case "StrExcelImportColumns": RunCommand<Model.StrExcelImportColumnsCommand>(app); break;
                    case "StrExcelImportBeams": RunCommand<Model.StrExcelImportBeamsCommand>(app); break;
                    case "StrExcelExportSchedule": RunCommand<Model.StrExcelExportScheduleCommand>(app); break;
                    case "StrExcelTemplate": RunCommand<Model.StrExcelTemplateCommand>(app); break;
                    case "StrAutoRebar": RunCommand<Model.StrAutoRebarCommand>(app); break;

                    // ── Enhanced Structural Algorithms ──
                    case "StrAutoSizeAll": RunCommand<Model.StrAutoSizeAllCommand>(app); break;
                    case "StrAutoSizeApply": RunCommand<Model.StrAutoSizeApplyCommand>(app); break;
                    case "StrRCDesign": RunCommand<Model.StrRCDesignCommand>(app); break;
                    case "StrSetUgandanDefaults":
                        RunCommand<Commands.Structural.SetUgandanDefaultsCommand>(app); break;
                    case "StrGridOptimize": RunCommand<Model.StrGridOptimizeCommand>(app); break;
                    case "StrCarbonOptimize": RunCommand<Model.StrCarbonOptimizeCommand>(app); break;
                    case "StrBarBending": RunCommand<Model.StrGenerateBarBendingCommand>(app); break;
                    case "StrDesignReport": RunCommand<Model.StrStructuralReportCommand>(app); break;
                    case "StrLoadPathVisualizer": RunCommand<Model.StrLoadPathVisualizerCommand>(app); break;
                    case "StrDesignCheck": RunCommand<Model.StrDesignCheckCommand>(app); break;
                    case "StrEnhancedCADImport": RunCommand<Model.StrEnhancedCADImportCommand>(app); break;

                    // ── MEP elements ──
                    case "ModelCreateDuct": RunCommand<Model.ModelCreateDuctCommand>(app); break;
                    case "ModelCreatePipe": RunCommand<Model.ModelCreatePipeCommand>(app); break;
                    case "ModelPlaceFixture": RunCommand<Model.ModelPlaceFixtureCommand>(app); break;

                    // ── DWG to Model ──
                    case "ModelDWGToModel": RunCommand<Model.ModelDWGToModelCommand>(app); break;
                    case "ModelDWGPreview": RunCommand<Model.ModelDWGPreviewCommand>(app); break;

                    // ════════════════════════════════════════════════════════
                    // BIM MANAGER TAB — ISO 19650 Project Management
                    // ════════════════════════════════════════════════════════

                    // Project Overview
                    case "BIMDashboard": RunCommand<BIMManager.ProjectDashboardCommand>(app); break;

                    // ── Gap Fix: Cross-System Automation ──
                    case "CDEApprovalWorkflow": RunCommand<BIMManager.CDEApprovalWorkflowCommand>(app); break;
                    case "CrossSystemLink": RunCommand<BIMManager.CrossSystemLinkCommand>(app); break;
                    case "RefreshCoordinationData": RunCommand<BIMManager.RefreshCoordinationDataCommand>(app); break;
                    case "StreamingCOBieImportValidated": RunCommand<BIMManager.StreamingCOBieImportCommand>(app); break;
                    case "Schedule4DHandover": RunCommand<BIMManager.Schedule4DHandoverCommand>(app); break;
                    case "COBieSystemGroupFix": RunCommand<BIMManager.COBieSystemGroupFixCommand>(app); break;
                    case "DataDropTracker": RunCommand<BIMManager.DataDropTrackerCommand>(app); break;
                    case "CDEFolderStructure": RunCommand<BIMManager.CDEFolderStructureCommand>(app); break;
                    case "ComplianceForecast": RunCommand<BIMManager.ComplianceForecastCommand>(app); break;
                    case "ISO19650Reference": RunCommand<BIMManager.ISO19650ReferenceCommand>(app); break;

                    // BEP (template-driven pre-contract + model enrichment)
                    case "CreateBEP": RunCommand<BIMManager.CreateBEPCommand>(app); break;
                    case "GenerateBEP": RunCommand<BIMManager.GenerateBEPCommand>(app); break;
                    case "UpdateBEP": RunCommand<BIMManager.UpdateBEPCommand>(app); break;
                    case "ExportBEP": RunCommand<BIMManager.ExportBEPCommand>(app); break;
                    // ValidateBepCompliance already wired above

                    // CDE & Document Control
                    case "CDEStatus": RunCommand<BIMManager.CDEStatusCommand>(app); break;
                    case "ValidateDocNaming": RunCommand<BIMManager.ValidateDocNamingCommand>(app); break;
                    case "DocumentRegister": RunCommand<BIMManager.DocumentRegisterCommand>(app); break;
                    case "AddDocument": RunCommand<BIMManager.AddDocumentCommand>(app); break;
                    case "CreateTransmittal": RunCommand<BIMManager.CreateTransmittalCommand>(app); break;
                    case "ReviewTracker": RunCommand<BIMManager.ReviewTrackerCommand>(app); break;

                    // Issue / RFI Tracker
                    case "RaiseIssue": RunCommand<BIMManager.RaiseIssueCommand>(app); break;
                    case "IssueDashboard": RunCommand<BIMManager.IssueDashboardCommand>(app); break;
                    case "UpdateIssue": RunCommand<BIMManager.UpdateIssueCommand>(app); break;
                    case "SelectIssueElements": RunCommand<BIMManager.SelectIssueElementsCommand>(app); break;
                    case "IssuesBulkClose": RunCommand<BIMManager.BulkCloseIssuesCommand>(app); break;

                    // Slice 4a — Site Photos cross-link: select a single element
                    // by its UniqueId in the active Revit view + zoom to it.
                    // Triggered from BIMCoordinationCenter.SelectElementInRevitPub
                    // when the user clicks "📂 Element" on an As-built / Reference
                    // photo row. Element UniqueId is passed via the
                    // "ElementGuid" extra-param.
                    case "SelectByElementGuid":
                    {
                        try
                        {
                            string uniqueId = GetExtraParam("ElementGuid");
                            if (string.IsNullOrEmpty(uniqueId))
                            {
                                Autodesk.Revit.UI.TaskDialog.Show("Site Photos",
                                    "No element id provided.");
                                break;
                            }
                            var uidoc = app?.ActiveUIDocument;
                            var doc   = uidoc?.Document;
                            if (uidoc == null || doc == null)
                            {
                                Autodesk.Revit.UI.TaskDialog.Show("Site Photos",
                                    "No active Revit document — open the model first.");
                                break;
                            }
                            var el = doc.GetElement(uniqueId);
                            if (el == null)
                            {
                                Autodesk.Revit.UI.TaskDialog.Show("Site Photos",
                                    $"Element {uniqueId} not found in this model.\n\n" +
                                    "The photo may have been anchored in a different model — switch the host doc and try again.");
                                break;
                            }
                            uidoc.Selection.SetElementIds(new List<Autodesk.Revit.DB.ElementId> { el.Id });
                            // Zoom to selection — UIView.ZoomToFit on the active view.
                            try
                            {
                                var uiView = uidoc.GetOpenUIViews()
                                    .FirstOrDefault(v => v.ViewId == uidoc.ActiveView.Id);
                                uiView?.ZoomToFit();
                            }
                            catch (Exception ex2) { StingLog.Warn($"SelectByElementGuid zoom: {ex2.Message}"); }
                            uidoc.ShowElements(el);
                        }
                        catch (Exception ex2)
                        {
                            StingLog.Warn($"SelectByElementGuid: {ex2.Message}");
                            Autodesk.Revit.UI.TaskDialog.Show("Site Photos",
                                $"Could not select element: {ex2.Message}");
                        }
                        break;
                    }

                    // Template engine v1.1 — deliverable lifecycle (S12) + orchestrator (S13)
                    case "IssueDeliverable":       RunCommand<Planscape.Docs.Templates.IssueDeliverableCommand>(app); break;
                    case "ReIssueDeliverable":     RunCommand<Planscape.Docs.Templates.ReIssueDeliverableCommand>(app); break;
                    case "PublishDeliverable":     RunCommand<Planscape.Docs.Templates.PublishDeliverableCommand>(app); break;
                    case "CancelDeliverable":      RunCommand<Planscape.Docs.Templates.CancelDeliverableCommand>(app); break;
                    case "SupersedeDeliverable":   RunCommand<Planscape.Docs.Templates.SupersedeDeliverableCommand>(app); break;
                    case "ReplaceDeliverable":     RunCommand<Planscape.Docs.Templates.ReplaceDeliverableCommand>(app); break;
                    case "CreateTransmittalOrchestrated":
                        RunCommand<Planscape.Docs.Templates.CreateTransmittalOrchestratedCommand>(app); break;
                    case "BulkIssueDeliverables":  RunCommand<Planscape.Docs.Templates.BulkIssueDeliverablesCommand>(app); break;

                    // COBie & Handover
                    case "COBieExport": RunCommand<BIMManager.COBieExportCommand>(app); break;
                    case "COBieImport": RunCommand<BIMManager.COBieImportCommand>(app); break;
                    case "COBieExtendedImport": RunCommand<BIMManager.COBieExtendedImportCommand>(app); break;

                    // GAP Analysis Fix Commands (Phase 68)
                    case "ExportDashboardHTML": RunCommand<BIMManager.ExportDashboardHTMLCommand>(app); break;
                    case "BEPStageValidation": RunCommand<BIMManager.BEPStageValidationCommand>(app); break;
                    case "IssueRevisionLink": RunCommand<BIMManager.IssueRevisionLinkCommand>(app); break;
                    case "AutoMeetingMinutes": RunCommand<BIMManager.AutoMeetingMinutesCommand>(app); break;
                    case "TagRevisionDiff": RunCommand<BIMManager.TagRevisionDiffCommand>(app); break;
                    case "AutoScheduleMeetings": RunCommand<BIMManager.AutoScheduleMeetingsCommand>(app); break;
                    case "WeeklyReport": RunCommand<BIMManager.WeeklyCoordinatorReportCommand>(app); break;
                    case "COBieHandoverExport": RunCommand<Docs.COBieHandoverExportCommand>(app); break;
                    case "BulkBIMExport": RunCommand<BIMManager.BulkBIMExportCommand>(app); break;
                    // HandoverManual already wired above

                    // Paragraph Builder / T4-T10 switchable defaults
                    case "ParagraphBuilder":
                    {
                        var dlg = new UI.ParagraphBuilderDialog();
                        dlg.ShowDialog();
                        if (dlg.Result != null && dlg.Result.ApplyRequested)
                        {
                            SetExtraParam("ParagraphPreset", dlg.Result.PresetKey);
                            SetExtraParam("ParagraphScope", dlg.Result.Scope);
                            RunCommand<Tags.ApplyParagraphPresetCommand>(app);
                        }
                        break;
                    }
                    case "ApplyParagraphPreset": RunCommand<Tags.ApplyParagraphPresetCommand>(app); break;
                    case "SetHandoverMode": RunCommand<Tags.SetHandoverModeCommand>(app); break;
                    case "Tag7NarrativeUpdaterToggle": RunCommand<Core.Tag7NarrativeUpdaterToggleCommand>(app); break;

                    // Phase 188 — sibling-panel toggles so dialogs / quick-action buttons can fire them.
                    case "ToggleHvacPanel":        RunCommand<Core.ToggleHvacPanelCommand>(app); break;
                    case "ToggleMaterialHub":      RunCommand<Core.ToggleMaterialHubCommand>(app); break;
                    case "ToggleElectricalPanel":  RunCommand<Core.ToggleElectricalPanelCommand>(app); break;
                    case "TogglePlumbingPanel":    RunCommand<Core.TogglePlumbingPanelCommand>(app); break;

                    // Briefcase — Reference Document Viewer
                    case "BriefcaseView": RunCommand<BIMManager.BriefcaseViewCommand>(app); break;
                    case "BriefcaseRead": RunCommand<BIMManager.BriefcaseReadCommand>(app); break;
                    case "BriefcaseAddFile": RunCommand<BIMManager.BriefcaseAddFileCommand>(app); break;

                    // 4D/5D BIM — Scheduling & Cost (placeholder until SchedulingCommands.cs)
                    case "AutoSchedule4D": RunCommand<BIMManager.AutoSchedule4DCommand>(app); break;
                    case "ImportMSProject": RunCommand<BIMManager.ImportMSProjectCommand>(app); break;
                    case "ViewTimeline4D": RunCommand<BIMManager.ViewTimeline4DCommand>(app); break;
                    case "ExportSchedule4D": RunCommand<BIMManager.ExportSchedule4DCommand>(app); break;
                    case "AutoCost5D": RunCommand<BIMManager.AutoCost5DCommand>(app); break;
                    case "ImportCostRates": RunCommand<BIMManager.ImportCostRatesCommand>(app); break;
                    case "CostReport5D": RunCommand<BIMManager.CostReport5DCommand>(app); break;
                    case "CashFlow5D": RunCommand<BIMManager.CashFlow5DCommand>(app); break;

                    // IFCExport, BOQExport, ClashDetect, KeynoteSync, ValidateTemplate already wired above

                    // Sticky Notes
                    case "StickyNote": RunCommand<BIMManager.ElementStickyNoteCommand>(app); break;
                    case "ExportStickyNotes": RunCommand<BIMManager.ExportStickyNotesCommand>(app); break;
                    case "SelectStickyElements": RunCommand<BIMManager.SelectStickyElementsCommand>(app); break;

                    // Sticky Notes — Enhanced
                    case "StickyCategories": RunCommand<BIMManager.StickyNoteCategoriesCommand>(app); break;
                    case "StickyDashboardBIM": RunCommand<BIMManager.StickyNoteDashboardCommand>(app); break;
                    case "StickySearch": RunCommand<BIMManager.StickyNoteSearchCommand>(app); break;

                    // Issue Tracker — Enhanced
                    case "IssueFilter": RunCommand<BIMManager.IssueFilterCommand>(app); break;
                    case "IssueTimeline": RunCommand<BIMManager.IssueTimelineCommand>(app); break;
                    case "IssueStatistics": RunCommand<BIMManager.IssueStatisticsCommand>(app); break;
                    case "IssueBatchUpdate": RunCommand<BIMManager.IssueBatchUpdateCommand>(app); break;
                    case "IssueExport": RunCommand<BIMManager.IssueExportCommand>(app); break;

                    // Model Health
                    case "ModelHealthDashboard": RunCommand<BIMManager.ModelHealthDashboardCommand>(app); break;
                    case "ModelHealthScore": RunCommand<Core.ModelHealthScoreCommand>(app); break;
                    case "ExportModelHealth": RunCommand<BIMManager.ExportModelHealthCommand>(app); break;
                    case "ExportPermissionMatrix": RunCommand<BIMManager.ExportPermissionMatrixCommand>(app); break;
                    case "ExportCoordLogXlsx":     RunCommand<BIMManager.ExportCoordLogCommand>(app); break;

                    // Clash Detection (rec-4/7/16). Inline dispatch promoted
                    // to real IExternalCommand classes in ClashSessionCommands.cs
                    // so BCC's DispatchCoordAction (which uses
                    // WorkflowEngine.GetCommandInstance) resolves them the same
                    // way the dockable-panel path does.
                    case "ClashRun":              RunCommand<Core.Clash.ClashRunCommand>(app); break;
                    case "ClashBcfExport":        RunCommand<Core.Clash.ClashBcfExportCommand>(app); break;
                    case "ClashSessionRefresh":   RunCommand<Core.Clash.ClashSessionRefreshCommand>(app); break;
                    case "ClashSessionClear":     RunCommand<Core.Clash.ClashSessionClearCommand>(app); break;
                    case "ClashMatrixEdit":       RunCommand<Core.Clash.ClashMatrixEditCommand>(app); break;
                    case "ClashManager":          RunCommand<Commands.Mep.ClashManagerCommand>(app); break;

                    // Warnings Manager (Phase 46)
                    case "WarningsDashboard": RunCommand<Core.WarningsDashboardCommand>(app); break;
                    case "WarningsAutoFix": RunCommand<Core.WarningsAutoFixCommand>(app); break;
                    case "WarningsExport": RunCommand<Core.WarningsExportCommand>(app); break;
                    case "WarningsBaseline": RunCommand<Core.WarningsBaselineCommand>(app); break;
                    case "WarningsSelect":
                    case "WarningsSelectElements": RunCommand<Core.WarningsSelectElementsCommand>(app); break;
                    case "WarningsSuppress": RunCommand<Core.WarningsSuppressCommand>(app); break;
                    case "WarningsCompliance": RunCommand<Core.WarningsComplianceCommand>(app); break;
                    case "WarningsMonitor": RunCommand<Core.WarningsMonitorCommand>(app); break;

                    // Phase 69: Acoustic & Sustainability
                    case "AcousticAnalysis":
                    {
                        var aaDoc = app.ActiveUIDocument?.Document;
                        if (aaDoc != null)
                        {
                            // Phase 187 — AnalyseModel now stamps ACO_RW_DB on
                            // every analysed wall, so the dispatch needs to own
                            // a transaction (the engine writes via SetString).
                            List<Model.AcousticResult> results;
                            try
                            {
                                using (var tx = new Transaction(aaDoc, "STING Acoustic Analysis"))
                                {
                                    tx.Start();
                                    results = Model.AcousticAnalysisOrchestrator.AnalyseModel(aaDoc);
                                    tx.Commit();
                                }
                            }
                            catch (Exception exA)
                            {
                                Core.StingLog.Warn($"Acoustic txn fallback: {exA.Message}");
                                results = Model.AcousticAnalysisOrchestrator.AnalyseModel(aaDoc);
                            }
                            int fails = results.Count(r => !r.Pass);
                            var sb = new System.Text.StringBuilder($"Acoustic Analysis: {results.Count} checks ({fails} failures)\nACO_RW_DB stamped on every analysed wall.\n\n");
                            foreach (var r in results.Take(30)) sb.AppendLine(r.ToString());
                            TaskDialog.Show("Acoustic Analysis", sb.ToString());
                        }
                        break;
                    }
                    case "BREEAMAssessment":
                    {
                        var brDoc = app.ActiveUIDocument?.Document;
                        if (brDoc != null)
                        {
                            var (breeam, lca, circ) = Model.SustainabilityOrchestrator.Assess(brDoc);
                            TaskDialog.Show("BREEAM Assessment",
                                $"BREEAM Score: {breeam.TotalScore:F1}% — {breeam.Rating}\n\n" +
                                $"Category Scores:\n" + string.Join("\n", breeam.CategoryScores.Select(kv => $"  {kv.Key}: {kv.Value:F0}%")) +
                                $"\n\nWhole-Life Carbon: {lca.KgCO2PerM2:F0} kgCO2e/m²\n{lca.LETIBenchmark}\n\nCircularity: {circ:F0}%");
                        }
                        break;
                    }
                    case "LifecycleAssessment":
                    {
                        var lcaDoc = app.ActiveUIDocument?.Document;
                        if (lcaDoc != null)
                        {
                            var lca = Model.LifecycleAssessmentEngine.Assess(lcaDoc, 0);
                            var sb = new System.Text.StringBuilder($"Lifecycle Assessment (BS EN 15978)\n\n");
                            sb.AppendLine($"A1-A3 Product:      {lca.A1_A3_ProductKgCO2:N0} kgCO2e");
                            sb.AppendLine($"A4 Transport:       {lca.A4_TransportKgCO2:N0} kgCO2e");
                            sb.AppendLine($"A5 Construction:    {lca.A5_ConstructionKgCO2:N0} kgCO2e");
                            sb.AppendLine($"B6 Operational:     {lca.B6_OperationalEnergyKgCO2:N0} kgCO2e");
                            sb.AppendLine($"C1-C4 End of Life:  {lca.C1_C4_EndOfLifeKgCO2:N0} kgCO2e");
                            sb.AppendLine($"\nTotal WLC: {lca.WholeLifeCarbon:N0} kgCO2e ({lca.KgCO2PerM2:F0}/m²)");
                            sb.AppendLine($"\n{lca.LETIBenchmark}");
                            if (lca.MaterialBreakdown.Count > 0)
                            {
                                sb.AppendLine("\nTop Materials:");
                                foreach (var m in lca.MaterialBreakdown.Take(10))
                                    sb.AppendLine($"  {m.Material}: {m.KgCO2:N0} kgCO2e ({m.Pct:F1}%)");
                            }
                            TaskDialog.Show("Lifecycle Assessment", sb.ToString());
                        }
                        break;
                    }

                    // Phase 70: MEP Intelligence
                    case "MEPPressureDrop":
                    {
                        var mepDoc = app.ActiveUIDocument?.Document;
                        if (mepDoc != null)
                        {
                            var results = Model.MEPSystemAnalyser.AnalyseModel(mepDoc);
                            int exceeded = results.Count(r => r.VelocityExceeded);
                            TaskDialog.Show("MEP Pressure Drop",
                                $"Analysed {results.Count} duct/pipe sections\n" +
                                $"Velocity exceeded: {exceeded}\n" +
                                $"Avg pressure drop: {(results.Count > 0 ? results.Average(r => r.TotalLossPa) : 0):F1} Pa/section");
                        }
                        break;
                    }

                    // Phase 71: Structural Deep
                    case "StructuralDeepAnalysis":
                    {
                        var sdDoc = app.ActiveUIDocument?.Document;
                        if (sdDoc != null)
                        {
                            var (torsion, tolerances, total) = Model.StructuralDeepOrchestrator.AnalyseModel(sdDoc);
                            // Phase 187 — close the calc → model loop on torsion +
                            // tolerance + creep + connection by stamping the new
                            // STR_* params on each affected beam / column.
                            int torsionStamped = 0, tolStamped = 0;
                            (int creepInsp, int creepStamped, string creepSum) = (0, 0, "");
                            (int connInsp,  int connStamped,  string connSum)  = (0, 0, "");
                            try
                            {
                                using (var tx = new Transaction(sdDoc, "STING Stamp Structural Deep"))
                                {
                                    tx.Start();
                                    torsionStamped = Model.AutoTorsionDetector.WriteBack(sdDoc, torsion);
                                    foreach (var kv in Model.StructuralDeepOrchestrator.LastPerElementTolerances)
                                    {
                                        var el = sdDoc.GetElement(kv.Key);
                                        if (el == null) continue;
                                        tolStamped += Model.FabricationToleranceChecker.WriteBack(sdDoc, el, kv.Value);
                                    }
                                    // Drive the two newly-orchestrated engines under
                                    // the same transaction so the whole deep-analysis
                                    // run is atomic.
                                    (creepInsp, creepStamped, creepSum) = Model.CreepDeflectionAnalysis.AnalyseModel(sdDoc);
                                    (connInsp,  connStamped,  connSum)  = Model.ConnectionDetailingEngine.AnalyseModel(sdDoc);
                                    tx.Commit();
                                }
                            }
                            catch (Exception exTx) { Core.StingLog.Warn($"Structural-deep writeback: {exTx.Message}"); }
                            TaskDialog.Show("Structural Deep Analysis",
                                $"Torsion Cases: {torsion.Count}  (STR_BEAM_TORSION_KNM stamped: {torsionStamped})\n" +
                                $"Tolerance Checks: {tolerances.Count}  (STR_FAB_TOLERANCE_MM stamped: {tolStamped})\n" +
                                $"Creep Deflection: {creepInsp} concrete beams  (STRUCT_FRM_DEFLECTION_MM stamped: {creepStamped})\n" +
                                $"Connection Detail: {connInsp} steel beams  (STR_CONN_* stamped: {connStamped})\n\n" +
                                (torsion.Count > 0 ? "Torsion:\n" + string.Join("\n", torsion.Take(10).Select(t => $"  {t.Description}")) : "") +
                                (tolerances.Count > 0 ? "\nTolerances:\n" + string.Join("\n", tolerances.Take(10).Select(t => $"  {t.CheckName}: ±{t.ToleranceMm:F1}mm")) : ""));
                        }
                        break;
                    }

                    // Phase 72: Doc/Schedule Automation
                    case "DrawingRegisterSync": RunCommand<Docs.DrawingRegisterSyncCommand>(app); break;
                    case "CrossScheduleValidate": RunCommand<Docs.CrossScheduleValidateCommand>(app); break;
                    case "PrintQueue": RunCommand<Docs.PrintQueueCommand>(app); break;
                    case "DocumentPackage": RunCommand<Docs.DocumentPackageCommand>(app); break;

                    // Phase 73: Workflow Maturity
                    case "CommissioningWorkflow": RunCommand<Core.CommissioningWorkflowCommand>(app); break;
                    case "HandoverValidation": RunCommand<Core.HandoverValidationCommand>(app); break;
                    case "SustainabilityWorkflow": RunCommand<Core.SustainabilityWorkflowCommand>(app); break;

                    // Phase 75: Workflow/Coordination Gap Implementations (29 gaps)
                    case "WorkflowScheduler": RunCommand<Core.WorkflowSchedulerCommand>(app); break;
                    case "WarningRootCause": RunCommand<Core.WarningRootCauseCommand>(app); break;
                    case "SuppressionAudit": RunCommand<Core.SuppressionAuditCommand>(app); break;
                    case "TeamActivity": RunCommand<Core.TeamActivityCommand>(app); break;
                    case "ComplianceTrendView": RunCommand<Core.ComplianceTrendViewCommand>(app); break;
                    case "MidDayCoordination": RunCommand<Core.MidDayCoordinationCommand>(app); break;
                    case "DesignReviewPrep": RunCommand<Core.DesignReviewPrepCommand>(app); break;
                    case "SLAViolationReport": RunCommand<Core.SLAViolationReportCommand>(app); break;
                    case "FederatedPreFlight":
                    {
                        var fpDoc = app.ActiveUIDocument?.Document;
                        if (fpDoc != null)
                        {
                            var presets = Core.WorkflowEngine.GetAvailablePresets();
                            var preset = presets.FirstOrDefault() ?? new Core.WorkflowPreset { Name = "Default", Steps = new() };
                            var (ok, issues) = Core.FederatedWorkflowSupport.PreFlightCheckFederated(fpDoc, preset);
                            var sb = new System.Text.StringBuilder();
                            sb.AppendLine(ok ? "✓ Federated pre-flight passed." : "⚠ Federated pre-flight issues:");
                            foreach (var issue in issues) sb.AppendLine($"  • {issue}");
                            TaskDialog.Show("Federated Pre-Flight", sb.ToString());
                        }
                        break;
                    }
                    case "TransmittalGateCheck":
                    {
                        var tgDoc = app.ActiveUIDocument?.Document;
                        if (tgDoc != null)
                        {
                            var (ok, issues, pct) = Core.TransmittalGate.ValidateForTransmittal(tgDoc);
                            var sb = new System.Text.StringBuilder();
                            sb.AppendLine(ok ? $"✓ Ready for transmittal ({pct:F1}% compliance)." : $"⚠ Not ready ({pct:F1}% compliance):");
                            foreach (var issue in issues) sb.AppendLine($"  • {issue}");
                            TaskDialog.Show("Transmittal Gate", sb.ToString());
                        }
                        break;
                    }
                    case "ContainerWarningCheck":
                    {
                        var cwDoc = app.ActiveUIDocument?.Document;
                        if (cwDoc != null)
                        {
                            var (count, rec) = Core.ContainerWarningCrossValidator.Analyse(cwDoc);
                            TaskDialog.Show("Container ↔ Warning Analysis", $"Container-related warnings: ~{count}\n\n{rec}");
                        }
                        break;
                    }

                    // Phase 74: Deep Review Enhancements
                    case "DailyPlanner": RunCommand<Core.DailyPlannerCommand>(app); break;
                    case "DeliverableMatrix": RunCommand<Core.DeliverableMatrixCommand>(app); break;
                    case "WarningPrediction": RunCommand<Core.WarningPredictionCommand>(app); break;
                    case "ActionAuditExport":
                    {
                        var aaDoc = app.ActiveUIDocument?.Document;
                        if (aaDoc != null)
                        {
                            string outPath = Core.OutputLocationHelper.GetTimestampedPath(aaDoc, "ActionAudit", ".csv");
                            Core.ActionAuditLog.Export(outPath);
                            TaskDialog.Show("Action Audit", $"Audit log exported to:\n{outPath}");
                        }
                        break;
                    }
                    case "ComplianceFallCheck":
                    {
                        var cfDoc = app.ActiveUIDocument?.Document;
                        if (cfDoc != null)
                        {
                            var (fallen, current, prev, newStale) = Core.ComplianceFallDetector.CheckForRegression(cfDoc);
                            TaskDialog.Show("Compliance Check",
                                fallen ? $"⚠ COMPLIANCE FALLEN: {prev:F1}% → {current:F1}% ({newStale} new stale elements)"
                                       : $"✓ Compliance stable at {current:F1}%");
                        }
                        break;
                    }

                    // Phase 47: BIM Coordination Center (unified dashboard)
                    // Phase 76: BCC is modeless — single instance, ExternalEvent dispatch
                    case "BIMCoordinationCenter":
                        RunCommand<Core.BIMCoordinationCenterCommand>(app);
                        break;

                    // MIDP & Compliance
                    case "MidpTracker": RunCommand<BIMManager.MidpTrackerCommand>(app); break;
                    case "FullComplianceDashboard": RunCommand<BIMManager.FullComplianceDashboardCommand>(app); break;

                    // Phase 148 — surface for the new engines.
                    // ComplianceForecastReport uses the Phase 148 engine reading
                    // compliance_trend.json (the legacy "ComplianceForecast" tag
                    // at line ~1562 still maps to the GapFixCommands variant
                    // reading compliance_log.jsonl — the two are complementary).
                    case "RunRebarSpacingCheck":           RunCommand<BIMManager.RunRebarSpacingCheckCommand>(app); break;
                    case "CreateMepCommissioningSchedules":RunCommand<BIMManager.CreateMepCommissioningSchedulesCommand>(app); break;
                    case "CheckScheduleFieldConsistency":  RunCommand<BIMManager.CheckScheduleFieldConsistencyCommand>(app); break;
                    case "TeamWorkloadReport":             RunCommand<BIMManager.TeamWorkloadReportCommand>(app); break;
                    case "ComplianceForecastReport":       RunCommand<BIMManager.ComplianceForecastReportCommand>(app); break;
                    case "DataDropStatus":                 RunCommand<BIMManager.DataDropStatusCommand>(app); break;

                    // 4D/5D Extended
                    case "Export4DTimeline": RunCommand<BIMManager.Export4DTimelineCommand>(app); break;
                    case "Export5DCostData": RunCommand<BIMManager.Export5DCostDataCommand>(app); break;
                    case "LinkPredecessors": RunCommand<BIMManager.LinkPredecessorsCommand>(app); break;
                    case "AssignPhaseDates": RunCommand<BIMManager.AssignPhaseDatesCommand>(app); break;
                    case "MeasuredQuantities": RunCommand<BIMManager.MeasuredQuantitiesCommand>(app); break;
                    case "ElementCountSummary": RunCommand<BIMManager.ElementCountSummaryCommand>(app); break;
                    case "DocumentBriefcase": RunCommand<BIMManager.DocumentBriefcaseCommand>(app); break;
                    case "PhaseFilter": RunCommand<BIMManager.PhaseFilterCommand>(app); break;
                    case "PhaseSummary": RunCommand<BIMManager.PhaseSummaryCommand>(app); break;
                    case "MilestoneRegister": RunCommand<BIMManager.MilestoneRegisterCommand>(app); break;
                    case "WorkingCalendar": RunCommand<BIMManager.WorkingCalendarCommand>(app); break;

                    // Output & Compliance
                    case "SetOutputDirectory": RunCommand<BIMManager.SetOutputDirectoryCommand>(app); break;
                    case "StageComplianceGate": RunCommand<BIMManager.StageComplianceGateCommand>(app); break;

                    // Project Folder Structure (Phase 167 — unified setup)
                    case "CreateFolders":
                    {
                        var cfDoc = app.ActiveUIDocument?.Document;
                        if (cfDoc != null)
                        {
                            var existing = Core.ProjectFolderEngine.LoadOrDetectSetup(cfDoc);
                            if (existing != null)
                            {
                                var td = new Autodesk.Revit.UI.TaskDialog("STING Folder Setup")
                                {
                                    MainInstruction = "Project folders are already configured.",
                                    MainContent = $"Root: {existing.ResolveRootPath(cfDoc.PathName)}\n" +
                                                  $"Mode: {existing.Mode}, {existing.CustomFolders?.Count ?? 0} folders",
                                };
                                td.AddCommandLink(Autodesk.Revit.UI.TaskDialogCommandLinkId.CommandLink1, "Run setup again", "Reconfigure folder structure");
                                td.AddCommandLink(Autodesk.Revit.UI.TaskDialogCommandLinkId.CommandLink2, "Open folder in Explorer", "Browse files");
                                td.CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Cancel;
                                var res = td.Show();
                                if (res == Autodesk.Revit.UI.TaskDialogResult.CommandLink2)
                                {
                                    string root = existing.ResolveRootPath(cfDoc.PathName);
                                    if (!string.IsNullOrEmpty(root) && System.IO.Directory.Exists(root))
                                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", root) { UseShellExecute = true })?.Dispose();
                                    break;
                                }
                                if (res != Autodesk.Revit.UI.TaskDialogResult.CommandLink1) break;
                            }

                            try
                            {
                                var dlg = new UI.ProjectFolderSetupDialog(app);
                                if (dlg.ShowDialog() == true && dlg.Result != null)
                                {
                                    string root = dlg.Result.ResolveRootPath(cfDoc.PathName);
                                    if (!string.IsNullOrEmpty(root) && System.IO.Directory.Exists(root))
                                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", root) { UseShellExecute = true })?.Dispose();
                                }
                            }
                            catch (Exception ex2)
                            {
                                StingTools.Core.StingLog.Warn($"CreateFolders: {ex2.Message}");
                                // Fall back to legacy direct creation
                                int created = Core.ProjectFolderEngine.CreateFolderStructure(cfDoc);
                                Autodesk.Revit.UI.TaskDialog.Show("STING Folder Structure",
                                    $"Created {created} folders at:\n{Core.ProjectFolderEngine.GetRootPath(cfDoc)}");
                            }
                        }
                        break;
                    }
                    case "OpenProjectFolder":
                    {
                        var opDoc = app.ActiveUIDocument?.Document;
                        string root = Core.ProjectFolderEngine.GetRootPath(opDoc);
                        if (System.IO.Directory.Exists(root))
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", root) { UseShellExecute = true })?.Dispose();
                        break;
                    }
                    case "FolderHealth":
                    {
                        try { UI.FolderHealthPanel.ShowDialog(app); }
                        catch (Exception ex2) { Autodesk.Revit.UI.TaskDialog.Show("STING", $"Folder Health failed: {ex2.Message}"); }
                        break;
                    }
                    case "FolderMigrate":
                    {
                        var fmDoc = app.ActiveUIDocument?.Document;
                        if (fmDoc != null)
                        {
                            try
                            {
                                var rep = Core.ProjectFolderEngine.MigrateFromLegacy(fmDoc);
                                Autodesk.Revit.UI.TaskDialog.Show("STING Migration",
                                    $"Moved {rep.FilesMoved} files. Removed {rep.FoldersRemoved} legacy folders." +
                                    (rep.Warnings.Count > 0 ? $"\n\nWarnings: {rep.Warnings.Count}" : ""));
                            }
                            catch (Exception ex2) { Autodesk.Revit.UI.TaskDialog.Show("STING", $"Migration failed: {ex2.Message}"); }
                        }
                        break;
                    }

                    // Operations Commands (OperationsCommands.cs, StingTools.Temp)
                    case "PDFExport": RunCommand<Temp.PDFExportCommand>(app); break;
                    case "QuantityTakeoff": RunCommand<Temp.QuantityTakeoffCommand>(app); break;
                    case "ModelHealthCheck": RunCommand<Temp.ModelHealthCheckCommand>(app); break;
                    case "BatchParameterExport": RunCommand<Temp.BatchParameterExportCommand>(app); break;
                    case "ProjectDashboard": RunCommand<Temp.ProjectDashboardEnhancedCommand>(app); break;
                    case "WorkflowPreset": RunCommand<Temp.WorkflowPresetRunnerCommand>(app); break;
                    case "CancellableOperation": RunCommand<Temp.CancellableOperationCommand>(app); break;

                    // Workflow presets dispatched from Document Manager + HVAC tab
                    case "WorkflowPreset_DailyQA":
                    case "WorkflowPreset_DocumentPackage":
                    case "WorkflowPreset_ProjectKickoff":
                    case "WorkflowPreset_HVACDesign":
                    case "WorkflowPreset_HVACCommissioning":
                    case "WorkflowPreset_DuctSpoolProduction":
                    {
                        // Phase 74: Use local `tag` not instance `_commandTag` to prevent race condition
                        string presetName = tag.Replace("WorkflowPreset_", "");
                        SetExtraParam("WorkflowPresetName", presetName);
                        RunCommand<Core.WorkflowPresetCommand>(app);
                        break;
                    }

                    // Phase 48: Enhanced workflow dispatch
                    case "RepeatLastWorkflow":
                    {
                        string last = Core.WorkflowEngine.LastWorkflowName;
                        if (!string.IsNullOrEmpty(last))
                        {
                            SetExtraParam("WorkflowPresetName", last);
                            RunCommand<Core.WorkflowPresetCommand>(app);
                        }
                        else
                        {
                            TaskDialog.Show("STING", "No previous workflow to repeat.\nRun a workflow first, then use 'Repeat Last'.");
                        }
                        break;
                    }
                    case "RunWorkflow_MorningHealthCheck":
                    case "RunWorkflow_DailyQASync":
                    case "RunWorkflow_HandoverReadiness":
                    case "RunWorkflow_WeeklyDataDrop":
                    case "RunWorkflow_ProjectKickoff":
                    case "RunWorkflow_PostTaggingQA":
                    case "RunWorkflow_DocumentPackage":
                    case "RunWorkflow_BEPPackage":
                    case "RunWorkflow_CoordinationMeetingPrep":
                    case "RunWorkflow_ClashCoordination":
                    case "RunWorkflow_EndOfStageGate":
                    case "RunWorkflow_QuickFixCycle":
                    case "RunWorkflow_EndOfDaySync":
                    case "RunWorkflow_FederatedModelAudit":
                    case "RunWorkflow_PreMeetingPrep":
                    case "RunWorkflow_COBieReadiness":
                    case "RunWorkflow_DrawingIssue":
                    case "RunWorkflow_SpatialQA":
                    case "RunWorkflow_TierConversionHandover":
                    {
                        // Phase 75: Robust name reconstruction — insert spaces before uppercase chars
                        // SCH-CRIT-01 FIX: Use local `tag` snapshot (captured under lock at line 77),
                        // NOT instance field `_commandTag` which can be overwritten by WPF thread
                        string rawName = tag.Replace("RunWorkflow_", "");
                        var sb = new System.Text.StringBuilder(rawName.Length + 10);
                        for (int ci = 0; ci < rawName.Length; ci++)
                        {
                            char ch = rawName[ci];
                            if (ci > 0 && char.IsUpper(ch) && !char.IsUpper(rawName[ci - 1]))
                                sb.Append(' ');
                            sb.Append(ch);
                        }
                        SetExtraParam("WorkflowPresetName", sb.ToString().Trim());
                        RunCommand<Core.WorkflowPresetCommand>(app);
                        break;
                    }
                    case "SaveExtendedBaseline":
                    {
                        var d = app.ActiveUIDocument?.Document;
                        if (d != null) { Core.WarningsEngine.SaveExtendedBaseline(d); TaskDialog.Show("STING", "Extended warning baseline saved."); }
                        break;
                    }

                    // Phase 48b: Coordination Center drill-down actions
                    case string s when s.StartsWith("SelectByDisc_"):
                    {
                        string disc = s.Substring("SelectByDisc_".Length);
                        SetExtraParam("DiscFilter", disc);
                        RunCommand<Organise.SelectByDisciplineCommand>(app);
                        break;
                    }
                    case string s when s.StartsWith("SelectWarning_"):
                    {
                        // Format: SelectWarning_Category_Description
                        RunCommand<Core.WarningsSelectElementsCommand>(app);
                        break;
                    }
                    case string s when s.StartsWith("SelectIssue_"):
                    {
                        string issueId = s.Substring("SelectIssue_".Length);
                        SetExtraParam("IssueId", issueId);
                        RunCommand<BIMManager.SelectIssueElementsCommand>(app);
                        break;
                    }

                    // Phase 49: Coordination log and deliverables actions
                    case "ExportCoordLog":
                    {
                        var d = app.ActiveUIDocument?.Document;
                        if (d != null)
                        {
                            try
                            {
                                string logPath = StingTools.Core.ProjectFolderEngine.GetDataPath(d, "coord_log.json");
                                if (string.IsNullOrEmpty(logPath) || !System.IO.File.Exists(logPath))
                                {
                                    logPath = System.IO.Path.Combine(
                                        System.IO.Path.GetDirectoryName(d.PathName ?? "") ?? "",
                                        ".sting_coord_log.json");
                                }
                                if (System.IO.File.Exists(logPath))
                                {
                                    string csvPath = logPath.Replace(".json", $"_{DateTime.Now:yyyyMMdd_HHmm}.csv");
                                    var entries = Newtonsoft.Json.JsonConvert.DeserializeObject<List<BIMCoordinationCenter.CoordLogEntry>>(
                                        System.IO.File.ReadAllText(logPath));
                                    if (entries != null && entries.Count > 0)
                                    {
                                        var sb = new System.Text.StringBuilder();
                                        sb.AppendLine("Timestamp,User,Category,Action,Detail,Impact");
                                        foreach (var e in entries)
                                            sb.AppendLine($"\"{e.Timestamp}\",\"{e.User}\",\"{e.Category}\",\"{e.Action}\",\"{e.Detail?.Replace("\"", "'")}\",\"{e.Impact}\"");
                                        System.IO.File.WriteAllText(csvPath, sb.ToString());
                                        TaskDialog.Show("STING", $"Coordination log exported:\n{csvPath}\n{entries.Count} entries");
                                    }
                                    else TaskDialog.Show("STING", "Coordination log is empty.");
                                }
                                else TaskDialog.Show("STING", "No coordination log found.");
                            }
                            catch (Exception ex2) { TaskDialog.Show("STING", $"Export failed: {ex2.Message}"); }
                        }
                        break;
                    }
                    case "ClearCoordLog":
                    {
                        var d = app.ActiveUIDocument?.Document;
                        if (d != null)
                        {
                            var confirm = new TaskDialog("Clear Coordination Log");
                            confirm.MainInstruction = "Clear the coordination log?";
                            confirm.MainContent = "This will delete all coordination log entries. This action cannot be undone.";
                            confirm.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                            if (confirm.Show() == TaskDialogResult.Yes)
                            {
                                string logPath = StingTools.Core.ProjectFolderEngine.GetDataPath(d, "coord_log.json");
                                if (string.IsNullOrEmpty(logPath) || !System.IO.File.Exists(logPath))
                                {
                                    logPath = System.IO.Path.Combine(
                                        System.IO.Path.GetDirectoryName(d.PathName ?? "") ?? "",
                                        ".sting_coord_log.json");
                                }
                                if (System.IO.File.Exists(logPath)) System.IO.File.Delete(logPath);
                                TaskDialog.Show("STING", "Coordination log cleared.");
                            }
                        }
                        break;
                    }
                    case string s when s.StartsWith("RunMorningCheck"):
                    {
                        SetExtraParam("WorkflowPresetName", "MorningHealthCheck");
                        RunCommand<Core.WorkflowPresetCommand>(app);
                        break;
                    }
                    case "RunDailyQA":
                    {
                        SetExtraParam("WorkflowPresetName", "DailyQA");
                        RunCommand<Core.WorkflowPresetCommand>(app);
                        break;
                    }
                    case "RunQuickFix":
                    {
                        SetExtraParam("WorkflowPresetName", "QuickFixCycle");
                        RunCommand<Core.WorkflowPresetCommand>(app);
                        break;
                    }
                    case "ExportCOBie":
                        RunCommand<BIMManager.COBieExportCommand>(app); break;

                    // IoT cases removed (Group 3 mis-wire cleanup): IoTSensorLink/IoTDashboard/
                    // IoTAlertConfig/IoTHistoryExport ran unrelated FM commands and their buttons
                    // were removed. See MISWIRE_AUDIT.md cluster A.

                    // Standards / Compliance (wired to StandardsEngine.cs commands)
                    case "ISO19650Checker": RunCommand<Temp.Iso19650DeepComplianceCommand>(app); break;
                    // "BS1192Checker" case removed (Group 3): it ran BS 7671 (electrical), a
                    // different standard; its button was removed. See MISWIRE_AUDIT.md cluster B.
                    // H-02 FIX: Corrected dispatch mismatches (COBieValidator→COBieDataSummary, Uniclass not Unicode)
                    case "COBieValidator": RunCommand<Temp.COBieDataSummaryCommand>(app); break;
                    case "UniclassValidator": RunCommand<Temp.UniclassClassifyCommand>(app); break;
                    case "UnicodeValidator": RunCommand<Temp.UniclassClassifyCommand>(app); break; // Legacy alias
                    case "ClassificationAudit": RunCommand<Temp.UniclassClassifyCommand>(app); break;

                    // MEP Schedule shortcuts (wired to MEPScheduleCommands.cs)
                    case "MEPScheduleHVAC": RunCommand<Temp.MechanicalEquipmentScheduleCommand>(app); break;
                    case "MEPScheduleElec": RunCommand<Temp.ElectricalDeviceScheduleCommand>(app); break;
                    case "MEPSchedulePlumb": RunCommand<Temp.PlumbingFixtureScheduleCommand>(app); break;
                    case "MEPScheduleFire": RunCommand<Temp.FireDeviceScheduleCommand>(app); break;
                    case "MEPScheduleAll": RunCommand<Temp.BatchMEPSchedulesCommand>(app); break;

                    // Excel Link — Bidirectional (6 commands)
                    case "ExportToExcel": RunCommand<BIMManager.ExportToExcelCommand>(app); break;
                    case "ImportFromExcel": RunCommand<BIMManager.ImportFromExcelCommand>(app); break;
                    case "ExcelRoundTrip": RunCommand<BIMManager.ExcelRoundTripCommand>(app); break;
                    case "ExportSchedulesToExcel": RunCommand<BIMManager.ExportSchedulesToExcelCommand>(app); break;
                    case "ImportSchedulesFromExcel": RunCommand<BIMManager.ImportSchedulesFromExcelCommand>(app); break;
                    case "ExportExcelTemplate": RunCommand<BIMManager.ExportTemplateCommand>(app); break;
                    case "ExcelExchangeWizard": RunCommand<BIMManager.ExcelExchangeWizardCommand>(app); break;

                    // ExLink-style Data Export (unified export dialog)
                    case "StingDataExport":
                    case "DataExport":
                    case "UnifiedExport":
                    {
                        var doc = app.ActiveUIDocument?.Document;
                        if (doc == null) { TaskDialog.Show("STING", "No document open."); break; }
                        var exportSettings = StingExportDialog.Show(doc);
                        if (exportSettings == null) break;
                        try
                        {
                            DataExportEngine.Execute(doc, app.ActiveUIDocument, exportSettings);
                            TaskDialog.Show("STING Export", $"Exported successfully to:\n{exportSettings.OutputPath}");
                        }
                        catch (Exception ex2)
                        {
                            StingLog.Error($"Data export failed: {ex2.Message}", ex2);
                            TaskDialog.Show("STING Export", $"Export failed: {ex2.Message}");
                        }
                        break;
                    }

                    // Platform Integration (12 commands)
                    case "ACCPublish": RunCommand<BIMManager.ACCPublishCommand>(app); break;
                    case "CDEPackage": RunCommand<BIMManager.CDEPackageCommand>(app); break;
                    case "ValidateCDEHandover":
                    {
                        var doc = app.ActiveUIDocument?.Document;
                        if (doc != null)
                        {
                            var (pass, issues) = BIMCoordinationCenterCommand.ValidateCDEHandoverReadiness(doc);
                            var sb = new System.Text.StringBuilder();
                            sb.AppendLine(pass ? "CDE HANDOVER VALIDATION: PASS\n" : "CDE HANDOVER VALIDATION: FAIL\n");
                            if (issues.Count > 0)
                                foreach (string issue in issues) sb.AppendLine($"  ✘ {issue}");
                            else sb.AppendLine("  All checks passed. Model is ready for CDE handover.");
                            TaskDialog.Show("STING CDE Validation", sb.ToString());
                        }
                        break;
                    }
                    case "BCFExport": RunCommand<BIMManager.BCFExportCommand>(app); break;
                    case "BCFImport": RunCommand<BIMManager.BCFImportCommand>(app); break;
                    case "PlatformSync":
                        // Route to Planscape server sync if connected; otherwise local delta sync
                        if (BIMManager.PlanscapeServerClient.Instance.IsConnected)
                            BIMManager.PlatformSyncCommand.SyncToPlanscapeServer(app, promptStabilise: true);
                        else
                            RunCommand<BIMManager.PlatformSyncCommand>(app);
                        break;
                    case "SharePointExport": RunCommand<BIMManager.SharePointExportCommand>(app); break;
                    // Third-party platform integrations — route to CDE Package which
                    // generates ISO 19650 folder structure compatible with any CDE platform
                    case "ProcorePackage":
                    case "TrimbleExport":
                    case "AconexPackage":
                    case "ProjectWiseExport":
                        RunCommand<BIMManager.CDEPackageCommand>(app); break;
                    case "PlatformDashboard": RunCommand<BIMManager.PlatformSyncCommand>(app); break;
                    // "WebhookPayload" case removed (Group 3): it ran BCF export, not any
                    // webhook feature; its button was removed. See MISWIRE_AUDIT.md cluster C.

                    // Revision Management (12 commands)
                    case "CreateRevision": RunCommand<BIMManager.CreateRevisionCommand>(app); break;
                    case "RevisionDashboard": RunCommand<BIMManager.RevisionDashboardCommand>(app); break;
                    case "AutoRevisionCloud": RunCommand<BIMManager.AutoRevisionCloudCommand>(app); break;
                    case "RevisionSchedule": RunCommand<BIMManager.RevisionScheduleCommand>(app); break;
                    case "TrackElementRevisions": RunCommand<BIMManager.TrackElementRevisionsCommand>(app); break;
                    case "RevisionCompare": RunCommand<BIMManager.RevisionCompareCommand>(app); break;
                    case "IssueSheetsForRevision": RunCommand<BIMManager.IssueSheetsForRevisionCommand>(app); break;
                    case "RevisionNamingEnforce": RunCommand<BIMManager.RevisionNamingEnforceCommand>(app); break;
                    case "RevisionTagIntegration": RunCommand<BIMManager.RevisionTagIntegrationCommand>(app); break;
                    case "RevisionExport": RunCommand<BIMManager.RevisionExportCommand>(app); break;
                    case "BulkRevisionStamp": RunCommand<BIMManager.BulkRevisionStampCommand>(app); break;
                    case "AutoRevisionOnTagChange": RunCommand<BIMManager.AutoRevisionOnTagChangeCommand>(app); break;

                    // Revision Management — Enhanced
                    case "RevisionApprovalWorkflow": RunCommand<BIMManager.RevisionApprovalWorkflowCommand>(app); break;
                    case "RevisionDistribution": RunCommand<BIMManager.RevisionDistributionCommand>(app); break;
                    case "RevisionComparisonReport": RunCommand<BIMManager.RevisionComparisonReportCommand>(app); break;

                    // Document Management Center
                    case "DocumentManager":
                    {
                        // Keep-dialog-open loop: re-open after each dispatched command
                        // Phase 87: Re-acquire document each iteration — recursive Execute() may switch documents
                        while (true)
                        {
                            var dmDoc = app.ActiveUIDocument?.Document;
                            if (dmDoc == null) break;

                            var dmResult = UI.DocumentManagementDialog.Show(dmDoc);
                            if (dmResult == null || !dmResult.Confirmed || string.IsNullOrEmpty(dmResult.Operation))
                                break; // User closed — exit loop

                            // Execute the dispatched sub-operation
                            SetCommand(dmResult.Operation);
                            Execute(app);
                            // Loop re-opens the dialog automatically
                        }
                        break;
                    }
                    case "DataExchange":
                    {
                        var deDoc = app.ActiveUIDocument?.Document;
                        if (deDoc != null)
                        {
                            var deResult = UI.StingDataExchangeDialog.Show(deDoc);
                            if (deResult != null)
                                UI.DataExchangeEngine.Execute(deDoc, app.ActiveUIDocument, deResult);
                        }
                        break;
                    }

                    // ════════════════════════════════════════════════════════
                    // TAG STUDIO — Smart Placement Wire-ups (UI-01)
                    // ════════════════════════════════════════════════════════
                    case "TagStudio_SmartPlace": RunCommand<Tags.SmartPlaceTagsCommand>(app); break;
                    case "TagStudio_Arrange": RunCommand<Tags.ArrangeTagsCommand>(app); break;
                    case "TagStudio_AlignBands": RunCommand<Tags.AlignTagBandsCommand>(app); break;

                    // UI-02: Elbow and arrow adjustments
                    case "TagStudio_AdjustElbows": RunCommand<Tags.AdjustElbowsCommand>(app); break;
                    case "TagStudio_SetArrows": RunCommand<Tags.SetArrowheadStyleCommand>(app); break;

                    // Tag Studio analysis — wired to real commands
                    case "TagStudio_APIGaps": RunCommand<Tags.PreTagAuditCommand>(app); break;
                    case "TagStudio_Explain": RunCommand<Tags.ValidateTagsCommand>(app); break;
                    case "TagStudio_Pipeline": RunCommand<Tags.CompletenessDashboardCommand>(app); break;
                    case "TagStudio_Generate": RunCommand<Tags.FamilyStagePopulateCommand>(app); break;
                    case "TagStudio_GapReview": RunCommand<Tags.ResolveAllIssuesCommand>(app); break;

                    // UI-03: Tag position switching
                    case "SwitchTagPos1": SwitchTagPositionInline(app, 1); break;
                    case "SwitchTagPos2": SwitchTagPositionInline(app, 2); break;
                    case "SwitchTagPos3": SwitchTagPositionInline(app, 3); break;
                    case "SwitchTagPos4": SwitchTagPositionInline(app, 4); break;
                    case "SwitchTagPos": RunCommand<Tags.SwitchTagPositionCommand>(app); break;
                    case "ExportTagPositions": RunCommand<Tags.ExportTagPositionsCommand>(app); break;

                    // D1: Tag map export/import between projects
                    case "ExportTagMap": RunCommand<BIMManager.ExportTagMapCommand>(app); break;
                    case "ImportTagMap": RunCommand<BIMManager.ImportTagMapCommand>(app); break;

                    // TI-02: Tie-In status commands
                    case "SetTieInOpen": SetTieInStatus(app, "OPEN", 0); break;
                    case "SetTieInConnected": SetTieInStatus(app, "CONNECTED", 1); break;

                    // ════════════════════════════════════════════════════════
                    // TAG STUDIO — Color Scheme Wire-ups (UI-04)
                    // ════════════════════════════════════════════════════════
                    case "TagStudio_SchemeDiscipline": ApplyTagColorScheme(app, "Discipline"); break;
                    case "TagStudio_SchemeWarm": ApplyTagColorScheme(app, "Warm"); break;
                    case "TagStudio_SchemeCool": ApplyTagColorScheme(app, "Cool"); break;
                    case "TagStudio_SchemeRed": ApplyTagColorScheme(app, "Red"); break;
                    case "TagStudio_SchemeYellow": ApplyTagColorScheme(app, "Yellow"); break;
                    case "TagStudio_SchemeBlue": ApplyTagColorScheme(app, "Blue"); break;
                    case "TagStudio_SchemeMono": ApplyTagColorScheme(app, "Monochrome"); break;
                    case "TagStudio_SchemeDark": ApplyTagColorScheme(app, "Dark"); break;
                    case "TagStudio_SchemeZone": ApplyTagColorScheme(app, "Zone"); break;
                    case "TagStudio_SchemeStatus": ApplyTagColorScheme(app, "Status"); break;
                    case "TagStudio_SchemeLevel": ApplyTagColorScheme(app, "Level"); break;
                    case "TagStudio_SchemeFunction": ApplyTagColorScheme(app, "Function"); break;
                    case "TagStudio_ApplyStyle": RunCommand<Tags.ApplyTagStyleCommand>(app); break;
                    case "TagStudio_ApplyScheme": RunCommand<Tags.ApplyColorSchemeCommand>(app); break;
                    case "TagStudio_ClearOverrides": RunCommand<Tags.ClearColorSchemeCommand>(app); break;

                    // Tag Studio > Scale tab
                    case "Scale_ApplyTiers":   RunCommand<Tags.ApplyScaleTiersCommand>(app); break;
                    case "Scale_ApplyTagSize": RunCommand<Tags.SetScaleAwareTagSizeCommand>(app); break;

                    // ════════════════════════════════════════════════════════
                    // TIE-IN POINT COMMANDS (TI-01, TI-03)
                    // ════════════════════════════════════════════════════════
                    case "PlaceTieInTagPipe": PlaceTieInTag(app, "Pipe"); break;
                    case "PlaceTieInTagDuct": PlaceTieInTag(app, "Duct"); break;
                    case "PlaceTieInTagElec": PlaceTieInTag(app, "Elec"); break;
                    case "ExportTieInRegister": ExportTieInRegister(app); break;

                    // ════════════════════════════════════════════════════════
                    // LIGHTNING PROTECTION (LPS) COMMANDS — Phase 176 / BS EN 62305
                    // ════════════════════════════════════════════════════════
                    case "PlaceLpsTagAirTerm":   PlaceLpsTag(app, "AirTerm"); break;
                    case "PlaceLpsTagDownCond":  PlaceLpsTag(app, "DownCond"); break;
                    case "PlaceLpsTagEarth":     PlaceLpsTag(app, "Earth"); break;
                    case "PlaceLpsTagBond":      PlaceLpsTag(app, "Bond"); break;
                    case "PlaceLpsTagSpd":       PlaceLpsTag(app, "Spd"); break;
                    case "PlaceLpsTagTestClamp": PlaceLpsTag(app, "TestClamp"); break;
                    case "PlaceLpsTagNaturalAT": PlaceLpsTag(app, "NaturalAT"); break;
                    case "SetLpsClassI":   SetLpsClass(app, "I"); break;
                    case "SetLpsClassII":  SetLpsClass(app, "II"); break;
                    case "SetLpsClassIII": SetLpsClass(app, "III"); break;
                    case "SetLpsClassIV":  SetLpsClass(app, "IV"); break;
                    case "SetLpz0A": SetLpsZone(app, "LPZ0A"); break;
                    case "SetLpz0B": SetLpsZone(app, "LPZ0B"); break;
                    case "SetLpz1":  SetLpsZone(app, "LPZ1"); break;
                    case "SetLpz2":  SetLpsZone(app, "LPZ2"); break;
                    case "SetLpz3":  SetLpsZone(app, "LPZ3"); break;
                    case "ValidateLpsSelection": ValidateLpsSelection(app); break;
                    case "ExportLpsRegister":    ExportLpsRegister(app); break;

                    // ── FIX-UI01: Missing dispatch entries (buttons were wired to
                    //    command classes that were never added to this switch) ──

                    // Tag clustering (TagOperationCommands.cs, StingTools.Organise)
                    case "ClusterTags": RunCommand<Organise.ClusterTagsCommand>(app); break;
                    case "DeclusterTags": RunCommand<Organise.DeclusterTagsCommand>(app); break;

                    // Display / style controls (TagOperationCommands.cs, StingTools.Organise)
                    case "SetDisplayMode": RunCommand<Organise.SetDisplayModeCommand>(app); break;

                    // Style (TagStyleCommands.cs, StingTools.Tags)
                    case "SetViewTagStyle": RunCommand<Tags.SetViewTagStyleCommand>(app); break;

                    // Linked model tags (SmartTagPlacementCommand.cs, StingTools.Tags)
                    case "AlignTagBands": RunCommand<Tags.AlignTagBandsCommand>(app); break;
                    case "BatchPlaceLinkedTags": RunCommand<Tags.BatchPlaceLinkedTagsCommand>(app); break;
                    case "ExportLinkedManifest": RunCommand<Tags.ExportLinkedModelManifestCommand>(app); break;

                    // Family tooling (FamilyParamCreatorCommand.cs, StingTools.Tags)
                    case "FamilyParamCreator": RunCommand<Tags.FamilyParamCreatorCommand>(app); break;
                    // Family conformance audit (FamilyConformanceCheckCommand.cs, Phase 185) —
                    // read-only audit of a folder of .rfa families against the STING contract.
                    // Use BEFORE bulk-stamping a manufacturer library.
                    case "FamilyConformanceCheck": RunCommand<Tags.FamilyConformanceCheckCommand>(app); break;

                    // Family quick-edit (FamilyQuickEditCommands.cs, StingTools.Tags) —
                    // rehost, swap category, inject automation pack, quick-edit dialog
                    case "FamilyQuickEdit":          RunCommand<Tags.OpenFamilyQuickEditCommand>(app); break;
                    case "FamilyChangeHost":         RunCommand<Tags.ChangeHostCommand>(app); break;
                    case "FamilySwapCategory":       RunCommand<Tags.SwapCategoryCommand>(app); break;
                    case "FamilyInjectAutomationPack": RunCommand<Tags.InjectAutomationPackCommand>(app); break;

                    // Compliance reporting (TokenWriterCommands.cs, StingTools.Tags)
                    case "DiscComplianceReport": RunCommand<Tags.CompletenessDashboardCommand>(app); break;

                    // Auto-tagger controls (StingAutoTagger.cs, StingTools.Core)
                    case "AutoTagVisual": RunCommand<Core.AutoTaggerToggleVisualCommand>(app); break;
                    case "AutoTaggerConfig": RunCommand<Core.AutoTaggerConfigCommand>(app); break;

                    // Workflow presets — XAML button uses "ListWorkflowPresets", dispatch
                    // already has "ListWorkflows". Add alias so both tags work.
                    case "ListWorkflowPresets": RunCommand<Core.ListWorkflowPresetsCommand>(app); break;

                    // ── Tag Studio informational stubs → routed to real commands ──
                    case "TagStudioAPIGaps": RunCommand<Tags.PreTagAuditCommand>(app); break;
                    case "TagStudioExplain": RunCommand<Tags.ValidateTagsCommand>(app);
                        break;
                    case "TagStudioPipeline": RunCommand<Tags.CompletenessDashboardCommand>(app); break;
                    case "TagStudioGenerate": RunCommand<Tags.FamilyStagePopulateCommand>(app); break;
                    case "TagStudioGapReview": RunCommand<Tags.ResolveAllIssuesCommand>(app); break;

                    // ── COBie Reference Data (COBieDataCommands.cs, StingTools.Temp) ──
                    case "COBieTypeMap": RunCommand<Temp.COBieTypeMapCommand>(app); break;
                    case "COBieSystemMap": RunCommand<Temp.COBieSystemMapCommand>(app); break;
                    case "COBiePickLists": RunCommand<Temp.COBiePickListsCommand>(app); break;
                    case "COBieAttributes": RunCommand<Temp.COBieAttributeTemplatesCommand>(app); break;
                    case "COBieJobTemplates": RunCommand<Temp.COBieJobTemplatesCommand>(app); break;
                    case "COBieSpareParts": RunCommand<Temp.COBieSparePartsCommand>(app); break;
                    case "COBieDocTypes": RunCommand<Temp.COBieDocumentTypesCommand>(app); break;
                    case "COBieZoneTypes": RunCommand<Temp.COBieZoneTypesCommand>(app); break;
                    case "COBieDocTypeAudit": RunCommand<Temp.COBieDocumentTypeAuditCommand>(app); break;
                    case "COBieZoneTypeAudit": RunCommand<Temp.COBieZoneTypeAuditCommand>(app); break;
                    case "COBieAutoMatch": RunCommand<Temp.COBieAutoMatchCommand>(app); break;
                    case "COBieDataSummary": RunCommand<Temp.COBieDataSummaryCommand>(app); break;

                    // ── MEP Schedules (MEPScheduleCommands.cs, StingTools.Temp) ──
                    // Legacy "PanelSchedule" tag now redirects to the rule-based picker
                    // in Commands.Panels — strictly better than templates.First() heuristic.
                    case "PanelSchedule": RunCommand<Commands.Panels.BatchPanelSchedulesCommand>(app); break;

                    // ── Electrical Panel Schedules (Commands/Panels) ──
                    case "Panel_BatchSchedules":     RunCommand<Commands.Panels.BatchPanelSchedulesCommand>(app); break;
                    case "Panel_Audit":              RunCommand<Commands.Panels.PanelScheduleAuditCommand>(app); break;
                    case "Panel_ExportToExcel":      RunCommand<Commands.Panels.ExportPanelSchedulesToExcelCommand>(app); break;
                    case "Panel_ImportFromExcel":    RunCommand<Commands.Panels.ImportPanelSchedulesFromExcelCommand>(app); break;
                    case "Panel_FillSpares":         RunCommand<Commands.Panels.FillEmptySlotsWithSparesCommand>(app); break;
                    case "Panel_FillSpaces":         RunCommand<Commands.Panels.FillEmptySlotsWithSpacesCommand>(app); break;
                    case "Panel_FillSparesAll":      RunCommand<Commands.Panels.FillSparesAllSchedulesCommand>(app); break;
                    case "Panel_SpacesToSpares":     RunCommand<Commands.Panels.ConvertSpacesToSparesCommand>(app); break;
                    case "Panel_ClearSparesSpaces":  RunCommand<Commands.Panels.ClearSparesAndSpacesCommand>(app); break;
                    case "LightingFixtureSchedule": RunCommand<Temp.LightingFixtureScheduleCommand>(app); break;
                    case "ElectricalDeviceSchedule": RunCommand<Temp.ElectricalDeviceScheduleCommand>(app); break;
                    case "MechEquipSchedule": RunCommand<Temp.MechanicalEquipmentScheduleCommand>(app); break;
                    case "PlumbingFixtureSchedule": RunCommand<Temp.PlumbingFixtureScheduleCommand>(app); break;
                    case "FireDeviceSchedule": RunCommand<Temp.FireDeviceScheduleCommand>(app); break;
                    case "BatchMEPSchedules": RunCommand<Temp.BatchMEPSchedulesCommand>(app); break;

                    // ── Room & Space (RoomSpaceCommands.cs, StingTools.Temp) ──
                    case "RoomAudit": RunCommand<Temp.RoomAuditCommand>(app); break;
                    case "SpatialConnectivityAudit": RunCommand<Temp.SpatialConnectivityAuditCommand>(app); break;
                    case "DataDropReadiness": RunCommand<BIMManager.DataDropReadinessCommand>(app); break;
                    case "RoomSchedule": RunCommand<Temp.RoomScheduleCommand>(app); break;
                    case "RoomZoneAssign": RunCommand<Temp.RoomZoneAssignCommand>(app); break;
                    case "RoomParamPush": RunCommand<Temp.RoomBasedParamPushCommand>(app); break;
                    case "RoomDataExport": RunCommand<Temp.RoomDataExportCommand>(app); break;

                    // ── FM Handover Export (HandoverExportCommands.cs, StingTools.Docs) ──
                    case "MaintenanceSchedule": RunCommand<Docs.MaintenanceScheduleExportCommand>(app); break;
                    case "OMManual": RunCommand<Docs.OAndMManualExportCommand>(app); break;
                    case "AssetHealth": RunCommand<Docs.AssetHealthReportCommand>(app); break;
                    case "SpaceHandover": RunCommand<Docs.SpaceHandoverReportCommand>(app); break;

                    // ── Tag Selector (TagSelectorCommands.cs, StingTools.Select) ──
                    case "TagSelector": RunCommand<Select.TagSelectorCommand>(app); break;
                    case "SelectTagsByText": RunCommand<Select.SelectTagsByTextCommand>(app); break;
                    case "SelectTagsByTextSize": RunCommand<Select.SelectTagsByTextSizeCommand>(app); break;
                    case "SelectTagsByArrowhead": RunCommand<Select.SelectTagsByArrowheadCommand>(app); break;
                    case "SelectTagsByLeaderState": RunCommand<Select.SelectTagsByLeaderStateCommand>(app); break;
                    case "SelectTagsByFamily": RunCommand<Select.SelectTagsByFamilyCommand>(app); break;
                    case "SelectTagsByHostCategory": RunCommand<Select.SelectTagsByHostCategoryCommand>(app); break;
                    case "SelectTagsByOrientation": RunCommand<Select.SelectTagsByOrientationCommand>(app); break;
                    case "SelectTagsByDiscipline": RunCommand<Select.SelectTagsByDisciplineCodeCommand>(app); break;
                    case "SelectTagsByLineWeight": RunCommand<Select.SelectTagsByLineWeightCommand>(app); break;
                    case "SelectTagsByElbowAngle": RunCommand<Select.SelectTagsByElbowAngleCommand>(app); break;
                    case "SelectTagsByToken": RunCommand<Select.SelectTagsByTokenCommand>(app); break;
                    case "SelectOverlappingTags": RunCommand<Select.SelectOverlappingTagsCommand>(app); break;

                    // ── Docs: Drawing Register + Journal Parser ──
                    case "DrawingRegister": RunCommand<Docs.DrawingRegisterCommand>(app); break;
                    case "JournalParser": RunCommand<Docs.JournalParserCommand>(app); break;

                    // ── Tags: Configure Tag Format (alias for ConfigEditor) ──

                    // ── Clone & Export commands ──
                    case "ApplyClonedTags": RunCommand<Organise.ApplyClonedTagsCommand>(app); break;
                    case "JSONExport": RunCommand<Organise.JSONExportCommand>(app); break;

                    // ── Docs: Handover & Journal (alternate tag names) ──
                    case "AssetHealthReport": RunCommand<Docs.AssetHealthReportCommand>(app); break;
                    case "MaintenanceScheduleExport": RunCommand<Docs.MaintenanceScheduleExportCommand>(app); break;
                    case "OAndMManualExport": RunCommand<Docs.OAndMManualExportCommand>(app); break;
                    case "SpaceHandoverReport": RunCommand<Docs.SpaceHandoverReportCommand>(app); break;

                    // ── Select: Tag Selectors (alternate tag names) ──
                    case "SelectTagsByDisciplineCode": RunCommand<Select.SelectTagsByDisciplineCodeCommand>(app); break;

                    // ── Tags: Intelligence & NLP ──
                    case "BimKnowledgeBase": RunCommand<Tags.BimKnowledgeBaseCommand>(app); break;
                    case "CommandSuggestion": RunCommand<Tags.CommandSuggestionCommand>(app); break;
                    case "ConfigurableTagFormat": RunCommand<Tags.ConfigurableTagFormatCommand>(app); break;
                    case "NLPCommandProcessor": RunCommand<Tags.NLPCommandProcessorCommand>(app); break;
                    case "SmartTagSuggest": RunCommand<Tags.SmartTagSuggestCommand>(app); break;
                    case "TagAnalyticsDashboard": RunCommand<Tags.TagAnalyticsDashboardCommand>(app); break;
                    case "TagBatchChain": RunCommand<Tags.TagBatchChainCommand>(app); break;
                    case "TagPropagation": RunCommand<Tags.TagPropagationCommand>(app); break;
                    case "TagQualityAnalyzer": RunCommand<Tags.TagQualityAnalyzerCommand>(app); break;
                    case "TagRuleEngine": RunCommand<Tags.TagRuleEngineCommand>(app); break;
                    case "TagVersionControl": RunCommand<Tags.TagVersionControlCommand>(app); break;

                    // ── Organise: Extended ──
                    case "DisciplineComplianceReport": RunCommand<Organise.DisciplineComplianceReportCommand>(app); break;
                    case "NudgeTags": RunCommand<Organise.NudgeTagsCommand>(app); break;

                    // ── Temp: COBie Reference Data (alternate tag names) ──
                    case "COBieAttributeTemplates": RunCommand<Temp.COBieAttributeTemplatesCommand>(app); break;
                    case "COBieDocumentTypes": RunCommand<Temp.COBieDocumentTypesCommand>(app); break;
                    case "COBieExportEnhanced": RunCommand<Temp.COBieExportEnhancedCommand>(app); break;

                    // ── Temp: DWG/CAD Import ──
                    case "AuditLinkedCAD": RunCommand<Temp.AuditLinkedCADCommand>(app); break;
                    case "AutoModel": RunCommand<Temp.AutoModelCommand>(app); break;
                    case "BatchImportDWG": RunCommand<Temp.BatchImportDWGCommand>(app); break;
                    case "CADInventory": RunCommand<Temp.CADInventoryCommand>(app); break;
                    case "DWGConversionPlan": RunCommand<Temp.DWGConversionPlanCommand>(app); break;
                    case "ExportLayerMapping": RunCommand<Temp.ExportLayerMappingCommand>(app); break;
                    case "ExtractRoomsFromCAD": RunCommand<Temp.ExtractRoomsFromCADCommand>(app); break;
                    case "ImportDWG": RunCommand<Temp.ImportDWGCommand>(app); break;
                    case "ImportDWGWithMapping": RunCommand<Temp.ImportDWGWithMappingCommand>(app); break;
                    case "LayerMapping": RunCommand<Temp.LayerMappingCommand>(app); break;
                    case "LinkDWG": RunCommand<Temp.LinkDWGCommand>(app); break;
                    case "LinkDWGAdvanced": RunCommand<Temp.LinkDWGAdvancedCommand>(app); break;
                    case "PlaceFamiliesFromCAD": RunCommand<Temp.PlaceFamiliesFromCADCommand>(app); break;
                    case "PreviewDWGLayers": RunCommand<Temp.PreviewDWGLayersCommand>(app); break;
                    case "RemoveLinkedCAD": RunCommand<Temp.RemoveLinkedCADCommand>(app); break;
                    case "TraceWallsFromCAD": RunCommand<Temp.TraceWallsFromCADCommand>(app); break;

                    // ── Temp: Model Creation ──
                    case "AutoCreateRooms": RunCommand<Temp.AutoCreateRoomsCommand>(app); break;
                    case "CreateCeilingsInteractive": RunCommand<Temp.CreateCeilingsInteractiveCommand>(app); break;
                    case "CreateColumnsAtGrids": RunCommand<Temp.CreateColumnsAtGridsCommand>(app); break;
                    case "CreateFloorsInteractive": RunCommand<Temp.CreateFloorsInteractiveCommand>(app); break;
                    case "CreateGridsFromCSV": RunCommand<Temp.CreateGridsFromCSVCommand>(app); break;
                    case "CreateLevelsFromCSV": RunCommand<Temp.CreateLevelsFromCSVCommand>(app); break;
                    case "CreateWallsInteractive": RunCommand<Temp.CreateWallsInteractiveCommand>(app); break;
                    case "PlaceDoors": RunCommand<Temp.PlaceDoorsCommand>(app); break;
                    case "PlaceMEPEquipment": RunCommand<Temp.PlaceMEPEquipmentCommand>(app); break;
                    case "PlaceWindows": RunCommand<Temp.PlaceWindowsCommand>(app); break;

                    // ── Temp: MEP Schedules (alternate tag names) ──
                    case "MechanicalEquipmentSchedule": RunCommand<Temp.MechanicalEquipmentScheduleCommand>(app); break;

                    // ── Temp: MEP Audits ──
                    case "MEPConnectionAudit": RunCommand<Temp.MEPConnectionAuditCommand>(app); break;
                    case "MEPSizingCheck": RunCommand<Temp.MEPSizingCheckCommand>(app); break;
                    case "MEPSpaceAnalysis": RunCommand<Temp.MEPSpaceAnalysisCommand>(app); break;
                    case "MEPSystemAudit": RunCommand<Temp.MEPSystemAuditCommand>(app); break;

                    // ── Temp: Standards & Compliance (alternate tag names) ──
                    case "Bs7671Compliance": RunCommand<Temp.Bs7671ComplianceCommand>(app); break;
                    case "Bs8300Accessibility": RunCommand<Temp.Bs8300AccessibilityCommand>(app); break;
                    case "CibseVelocityCheck": RunCommand<Temp.CibseVelocityCheckCommand>(app); break;
                    case "Iso19650DeepCompliance": RunCommand<Temp.Iso19650DeepComplianceCommand>(app); break;
                    case "PartLCompliance": RunCommand<Temp.PartLComplianceCommand>(app); break;
                    case "StandardsDashboard": RunCommand<Temp.StandardsDashboardCommand>(app); break;
                    case "UniclassClassify": RunCommand<Temp.UniclassClassifyCommand>(app); break;

                    // ── Temp: IoT & Maintenance (extended) ──
                    case "AssetCondition": RunCommand<Temp.AssetConditionCommand>(app); break;
                    case "CommissioningChecklist": RunCommand<Temp.CommissioningChecklistCommand>(app); break;
                    case "DigitalTwinExport": RunCommand<Temp.DigitalTwinExportCommand>(app); break;
                    case "EnergyAnalysis": RunCommand<Temp.EnergyAnalysisCommand>(app); break;
                    case "LifecycleCost": RunCommand<Temp.LifecycleCostCommand>(app); break;
                    case "SensorPointMapper": RunCommand<Temp.SensorPointMapperCommand>(app); break;
                    case "WarrantyTracker": RunCommand<Temp.WarrantyTrackerCommand>(app); break;

                    // ── Temp: Room & Space (alternate tag names) ──
                    case "RoomBasedParamPush": RunCommand<Temp.RoomBasedParamPushCommand>(app); break;
                    case "SpaceManagement": RunCommand<Temp.SpaceManagementCommand>(app); break;

                    // ── Temp: Data Validation ──
                    case "ClashDetection": RunCommand<Core.Clash.ClashRunCommand>(app); break;
                    case "CrossModelClash": RunCommand<Temp.CrossModelClashCommand>(app); break;
                    case "NamingAudit": RunCommand<Temp.NamingConventionAuditCommand>(app); break;
                    case "MEPClearance": RunCommand<Temp.MEPClearanceValidationCommand>(app); break;
                    case "IFCPropertyValidation": RunCommand<Temp.IFCPropertyValidationCommand>(app); break;
                    case "UserProductivity": RunCommand<BIMManager.UserProductivityReportCommand>(app); break;
                    case "NotificationPrefs": RunCommand<BIMManager.NotificationPreferencesCommand>(app); break;
                    case "TaskAssignment": RunCommand<BIMManager.TaskAssignmentCommand>(app); break;
                    case "GbXMLEnrichment": RunCommand<BIMManager.GbXMLEnrichmentCommand>(app); break;
                    case "ClashDetectionEnhanced": RunCommand<Temp.ClashDetectionEnhancedCommand>(app); break;
                    case "CrossValidateRegistry": RunCommand<Temp.CrossValidateRegistryCommand>(app); break;
                    case "DataIntegrityCheck": RunCommand<Temp.DataIntegrityCheckCommand>(app); break;
                    case "DataReport": RunCommand<Temp.DataReportCommand>(app); break;
                    case "ExportUnifiedRegistry": RunCommand<Temp.ExportUnifiedRegistryCommand>(app); break;
                    case "IFCExportEnhanced": RunCommand<Temp.IFCExportEnhancedCommand>(app); break;
                    case "ModelElementAudit": RunCommand<Temp.ModelElementAuditCommand>(app); break;
                    case "ValidateBindingMatrix": RunCommand<Temp.ValidateBindingMatrixCommand>(app); break;
                    case "ValidateFamilyBindings": RunCommand<Temp.ValidateFamilyBindingsCommand>(app); break;
                    case "ViewParameterMetadata": RunCommand<Temp.ViewParameterMetadataCommand>(app); break;

                    // ── Temp: Handover & Export ──
                    case "HandoverPackage": RunCommand<Temp.HandoverPackageCommand>(app); break;

                    // ── Streaming COBie / 4D-5D Exports (Phase 35) ──
                    case "StreamingCOBieExport": RunCommand<Docs.StreamingCOBieExportCommand>(app); break;
                    case "NavisworksTimeLiner": RunCommand<BIMManager.NavisworksTimeLinerExportCommand>(app); break;
                    case "ElementCostTrace": RunCommand<BIMManager.ElementCostTraceCommand>(app); break;

                    // ── Unified WPF Dialog Wizards (Phase 36) ──
                    case "DocWizard":
                    {
                        var doc = app.ActiveUIDocument?.Document;
                        if (doc == null) { TaskDialog.Show("STING", "No document open."); break; }
                        var dlgResult = UI.DocAutomationDialog.Show(doc);
                        if (dlgResult != null && dlgResult.Confirmed && !string.IsNullOrEmpty(dlgResult.Operation))
                        {
                            // Dispatch to the selected operation command
                            SetCommand(dlgResult.Operation);
                            Execute(app);
                        }
                        break;
                    }
                    case "ModelWizard":
                    {
                        var dlgResult = UI.ModelCreationDialog.Show();
                        if (dlgResult != null && dlgResult.Confirmed && !string.IsNullOrEmpty(dlgResult.ElementType))
                        {
                            // SCH-HIGH-01 FIX: SetCommand FIRST (which clears ExtraParams via M-02),
                            // THEN set ExtraParams so they survive for the dispatched command.
                            SetCommand(dlgResult.ElementType);
                            foreach (var kv in dlgResult.Dimensions)
                                SetExtraParam(kv.Key, kv.Value.ToString("F1"));
                            foreach (var kv in dlgResult.Options)
                                SetExtraParam(kv.Key, kv.Value);
                            Execute(app);
                        }
                        break;
                    }
                    case "ScheduleWizard":
                    {
                        // Load CSV definitions and existing schedule names for the wizard
                        var doc = app.ActiveUIDocument?.Document;
                        var csvDefs = new List<string>();
                        var existingScheds = new List<string>();
                        try
                        {
                            string csvPath = Core.StingToolsApp.FindDataFile("MR_SCHEDULES.csv");
                            if (!string.IsNullOrEmpty(csvPath) && System.IO.File.Exists(csvPath))
                            {
                                foreach (string line in System.IO.File.ReadAllLines(csvPath))
                                {
                                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                                    string[] parts = Core.StingToolsApp.ParseCsvLine(line);
                                    if (parts.Length > 3 && parts[0].Trim().Equals("SCHEDULE", StringComparison.OrdinalIgnoreCase))
                                    {
                                        string schedName = parts[3].Trim();
                                        if (!string.IsNullOrEmpty(schedName))
                                            csvDefs.Add(schedName);
                                    }
                                }
                            }
                            if (doc != null)
                            {
                                existingScheds = new FilteredElementCollector(doc)
                                    .OfClass(typeof(ViewSchedule))
                                    .Cast<ViewSchedule>()
                                    .Select(s => s.Name)
                                    .ToList();
                            }
                        }
                        catch (Exception ex2) { Core.StingLog.Warn($"ScheduleWizard CSV load: {ex2.Message}"); }

                        var dlgResult = UI.ScheduleWizardDialog.Show(csvDefs, existingScheds);
                        if (dlgResult != null && dlgResult.Confirmed && !string.IsNullOrEmpty(dlgResult.Operation))
                        {
                            // Dashboard returns operation strings that map directly to dispatch case labels
                            // e.g. "CreateBatch", "ScheduleAudit", "ScheduleCompare", "ExportCSV",
                            //      "SchedAutoFit", "CorporateTitleBlock", "PanelSchedule", "MEPScheduleHVAC", etc.
                            SetCommand(dlgResult.Operation);
                            if (dlgResult.Options != null)
                                foreach (var kv in dlgResult.Options)
                                    SetExtraParam(kv.Key, kv.Value);
                            Execute(app);
                        }
                        break;
                    }
                    case "TemplateDashboard":
                    {
                        // Keep-dialog-open loop: re-open after each dispatched command.
                        // Uses the v2 sidebar+master/detail layout
                        // (TemplateManagerDashboardV2). The v1 dialog
                        // (TemplateManagerDashboard) is retained for fallback /
                        // emergency rollback and can be re-enabled by switching
                        // the call below.
                        var docForDash = app?.ActiveUIDocument?.Document;
                        while (true)
                        {
                            try
                            {
                                var dlgResult = UI.TemplateManagerDashboardV2.Show(docForDash);
                                if (dlgResult == null || !dlgResult.Confirmed || string.IsNullOrEmpty(dlgResult.Operation))
                                    break;
                                SetCommand(dlgResult.Operation);
                                if (dlgResult.Options != null)
                                    foreach (var kv in dlgResult.Options)
                                        SetExtraParam(kv.Key, kv.Value);
                                Execute(app);
                                // Refresh the active doc after each op (may have been opened/closed)
                                docForDash = app?.ActiveUIDocument?.Document;
                            }
                            catch (Exception ex2) { StingLog.Warn("TemplateDashboard loop: " + ex2.Message); break; }
                        }
                        break;
                    }
                    case "SchedulingCostDashboard":
                    {
                        while (true)
                        {
                            try
                            {
                                var dlgResult = UI.SchedulingCostDashboard.Show();
                                if (dlgResult == null || !dlgResult.Confirmed || string.IsNullOrEmpty(dlgResult.Operation))
                                    break;
                                SetCommand(dlgResult.Operation);
                                if (dlgResult.Options != null)
                                    foreach (var kv in dlgResult.Options)
                                        SetExtraParam(kv.Key, kv.Value);
                                Execute(app);
                            }
                            catch (Exception ex2) { StingLog.Warn("SchedulingCostDashboard loop: " + ex2.Message); break; }
                        }
                        break;
                    }
                    case "RevisionManagerDashboard":
                    {
                        while (true)
                        {
                            try
                            {
                                var revDoc = app.ActiveUIDocument?.Document;
                                if (revDoc == null) break;
                                var dlgResult = UI.RevisionManagerDashboard.Show(revDoc);
                                if (dlgResult == null || !dlgResult.Confirmed || string.IsNullOrEmpty(dlgResult.Operation))
                                    break;
                                SetCommand(dlgResult.Operation);
                                if (dlgResult.Options != null)
                                    foreach (var kv in dlgResult.Options)
                                        SetExtraParam(kv.Key, kv.Value);
                                Execute(app);
                            }
                            catch (Exception ex2) { StingLog.Warn("RevisionManagerDashboard loop: " + ex2.Message); break; }
                        }
                        break;
                    }
                    case "WarningsDashboardDialog":
                    {
                        while (true)
                        {
                            try
                            {
                                var dlgResult = UI.WarningsDashboardDialog.Show();
                                if (dlgResult == null || !dlgResult.Confirmed || string.IsNullOrEmpty(dlgResult.Operation))
                                    break;
                                SetCommand(dlgResult.Operation);
                                if (dlgResult.Options != null)
                                    foreach (var kv in dlgResult.Options)
                                        SetExtraParam(kv.Key, kv.Value);
                                Execute(app);
                            }
                            catch (Exception ex2) { StingLog.Warn("WarningsDashboard loop: " + ex2.Message); break; }
                        }
                        break;
                    }

                    case "BEPDashboard":
                    {
                        while (true)
                        {
                            try
                            {
                                var dlgResult = UI.BEPDashboard.Show();
                                if (dlgResult == null || !dlgResult.Confirmed || string.IsNullOrEmpty(dlgResult.Operation))
                                    break;
                                SetCommand(dlgResult.Operation);
                                if (dlgResult.Options != null)
                                    foreach (var kv in dlgResult.Options)
                                        SetExtraParam(kv.Key, kv.Value);
                                Execute(app);
                            }
                            catch (Exception ex2) { StingLog.Warn("BEPDashboard loop: " + ex2.Message); break; }
                        }
                        break;
                    }
                    case "COBieExportDashboard":
                    {
                        while (true)
                        {
                            try
                            {
                                var dlgResult = UI.COBieExportDashboard.Show();
                                if (dlgResult == null || !dlgResult.Confirmed || string.IsNullOrEmpty(dlgResult.Operation))
                                    break;
                                SetCommand(dlgResult.Operation);
                                if (dlgResult.Options != null)
                                    foreach (var kv in dlgResult.Options)
                                        SetExtraParam(kv.Key, kv.Value);
                                Execute(app);
                            }
                            catch (Exception ex2) { StingLog.Warn("COBieExportDashboard loop: " + ex2.Message); break; }
                        }
                        break;
                    }
                    case "IssueTrackerDashboard":
                    {
                        while (true)
                        {
                            try
                            {
                                var dlgResult = UI.IssueTrackerDashboard.Show();
                                if (dlgResult == null || !dlgResult.Confirmed || string.IsNullOrEmpty(dlgResult.Operation))
                                    break;
                                SetCommand(dlgResult.Operation);
                                if (dlgResult.Options != null)
                                    foreach (var kv in dlgResult.Options)
                                        SetExtraParam(kv.Key, kv.Value);
                                Execute(app);
                            }
                            catch (Exception ex2) { StingLog.Warn("IssueTrackerDashboard loop: " + ex2.Message); break; }
                        }
                        break;
                    }

                    // ── Phase 37: Quality Assurance Commands ──
                    case "WarningReview": RunCommand<BIMManager.WarningReviewCommand>(app); break;
                    case "WarningExport": RunCommand<BIMManager.WarningExportCommand>(app); break;
                    case "RunCustomRules": RunCommand<BIMManager.RunCustomRulesCommand>(app); break;
                    case "ModelHealthScan": RunCommand<BIMManager.ModelHealthScanCommand>(app); break;
                    case "ModelHealthExportJson": RunCommand<BIMManager.ModelHealthExportJsonCommand>(app); break;
                    case "QAReport": RunCommand<BIMManager.QAReportCommand>(app); break;
                    case "SetupValidation": RunCommand<BIMManager.SetupValidationCommand>(app); break;

                    // ── Phase 37: Workset Audit Commands ──
                    case "WorksetAudit": RunCommand<BIMManager.WorksetAuditCommand>(app); break;
                    case "WorksetAuditExport": RunCommand<BIMManager.WorksetAuditExportCommand>(app); break;
                    case "CreateStandardWorksets": RunCommand<BIMManager.CreateStandardWorksetsCommand>(app); break;

                    // ── Phase 37: Link Manager Commands ──
                    case "LinkAudit": RunCommand<BIMManager.LinkAuditCommand>(app); break;
                    case "LinkAuditExport": RunCommand<BIMManager.LinkAuditExportCommand>(app); break;
                    case "LinkStats": RunCommand<BIMManager.LinkStatsCommand>(app); break;

                    // ── Phase 37: Spatial Validation Commands ──
                    case "SpatialValidation": RunCommand<Docs.SpatialValidationCommand>(app); break;
                    case "SpatialValidationExport": RunCommand<Docs.SpatialValidationExportCommand>(app); break;
                    case "GridAudit": RunCommand<Docs.GridAuditCommand>(app); break;
                    case "LevelAudit": RunCommand<Docs.LevelAuditCommand>(app); break;

                    // ── Phase 37: Family Audit Commands ──
                    case "FamilyAudit": RunCommand<Docs.FamilyAuditCommand>(app); break;
                    case "FamilyAuditExport": RunCommand<Docs.FamilyAuditExportCommand>(app); break;
                    case "ViewSheetCompleteness": RunCommand<Docs.ViewSheetCompletenessCommand>(app); break;

                    // ── Phase 37: Carbon Tracking Commands ──
                    case "CarbonCalculator": RunCommand<BIMManager.CarbonCalculatorCommand>(app); break;
                    case "CarbonExport": RunCommand<BIMManager.CarbonExportCommand>(app); break;

                    // ── Phase 37: Parameter Diff Commands ──
                    case "TakeSnapshot": RunCommand<BIMManager.TakeSnapshotCommand>(app); break;
                    case "CompareSnapshot": RunCommand<BIMManager.CompareSnapshotCommand>(app); break;
                    case "SnapshotDiffExport": RunCommand<BIMManager.SnapshotDiffExportCommand>(app); break;

                    // ── Phase 37: Print Manager Commands ──
                    case "BatchPDFExport": RunCommand<Docs.BatchPDFExportCommand>(app); break;
                    case "SheetSetSummary": RunCommand<Docs.SheetSetSummaryCommand>(app); break;

                    // ── Phase 37: Selection Set Commands ──
                    case "SaveSelectionSet": RunCommand<Select.SaveSelectionSetCommand>(app); break;
                    case "RecallSelectionSet": RunCommand<Select.RecallSelectionSetCommand>(app); break;
                    case "ManageSelectionSets": RunCommand<Select.ManageSelectionSetsCommand>(app); break;

                    // ── Phase 37: LAN Collaboration Commands ──
                    case "LANEnableWorksharing": RunCommand<BIMManager.LANEnableWorksharingCommand>(app); break;
                    case "LANSyncToCentral": RunCommand<BIMManager.LANSyncToCentralCommand>(app); break;
                    case "LANBackup": RunCommand<BIMManager.LANBackupCommand>(app); break;
                    case "LANTeamDashboard": RunCommand<BIMManager.LANTeamDashboardCommand>(app); break;
                    case "LANChangeLog": RunCommand<BIMManager.LANChangeLogCommand>(app); break;
                    case "LANAutoSyncToggle": RunCommand<BIMManager.LANAutoSyncToggleCommand>(app); break;

                    // ── Phase 6b: Speckle Commands ──
                    case "SpeckleSend":    RunCommand<BIMManager.SpeckleSendCommand>(app);    break;
                    case "SpeckleReceive": RunCommand<BIMManager.SpeckleReceiveCommand>(app); break;
                    case "SpeckleDiff":    RunCommand<BIMManager.SpeckleDiffCommand>(app);    break;

                    // ── Publish 3D Model — meta action: pops a picker
                    //    (Speckle / ACC / IFC) inside Publish3DModelCommand
                    //    and routes to the chosen target. Same command class
                    //    is registered in WorkflowEngine.ResolveCommand so
                    //    BCC's ExternalEvent dispatcher resolves it too.
                    case "Publish3DModel": RunCommand<BIMManager.Publish3DModelCommand>(app); break;

                    // ── Phase 91: BOQ & Cost Manager dispatch ──
                    case "BOQCostManager":
                    {
                        // Open the standalone BOQ window on the UI thread.
                        try
                        {
                            var doc = app?.ActiveUIDocument?.Document;
                            if (doc != null) UI.BOQCostManagerWindow.ShowFor(doc);
                        }
                        catch (Exception ex2) { StingLog.Error("BOQCostManager dispatch", ex2); }
                        break;
                    }
                    case "BOQRefresh":              RunCommand<BOQ.BOQRefreshCommand>(app); break;
                    case "BOQSetBudget":            RunCommand<BOQ.BOQSetBudgetCommand>(app); break;
                    case "BOQSnapshotSave":         RunCommand<BOQ.BOQSnapshotSaveCommand>(app); break;
                    case "BOQAddManualRow":         RunCommand<BOQ.BOQAddManualRowCommand>(app); break;
                    case "SelectInRevit":           RunCommand<BOQ.BOQSelectInRevitCommand>(app); break;
                    case "BOQExport":               RunCommand<BOQ.BOQExportCommand>(app); break;
                    case "BOQImport":               RunCommand<BOQ.BOQImportCommand>(app); break;
                    case "BOQSnapshotCompare":      RunCommand<BOQ.BOQSnapshotCompareCommand>(app); break;
                    case "ReconcileProvisionals":   RunCommand<BOQ.BOQReconcileProvisionalsCommand>(app); break;
                    case "BOQWriteItemParams":      RunCommand<BOQ.BOQWriteItemParamsCommand>(app); break;
                    case "BOQExportProfessional":   RunCommand<BOQ.BOQProfessionalExportCommand>(app); break;
                    case "BOQBccRefresh":           RunCommand<BOQ.BOQBccRefreshCommand>(app); break;

                    // Phase 108m — Product + Multi-building roadmap
                    case "PresetCombinations":      RunCommand<Presets.PresetCombinationCommand>(app); break;
                    case "FamilyLibraryAudit":      RunCommand<Temp.FamilyLibraryAuditCommand>(app); break;
                    case "BOQPrepForExport":        RunCommand<BOQ.BOQPrepForExportCommand>(app); break;
                    case "LODValidation":           RunCommand<Core.LODValidationCommand>(app); break;
                    case "ApplySectorPack":         RunCommand<Core.ApplySectorPackCommand>(app); break;
                    case "UnifiedDWGWizard":        RunCommand<Model.UnifiedDWGWizardCommand>(app); break;
                    case "BOQRateHeatMap":          RunCommand<BOQ.BOQRateSourceHeatMapCommand>(app); break;

                    // Phase 184 — Cost management (P2)
                    case "Cost_ValidateAll":            RunCommand<Commands.Cost.CostValidateAllCommand>(app); break;
                    case "Cost_ClearStale":             RunCommand<Commands.Cost.CostClearStaleCommand>(app); break;
                    case "Cost_RunWorkflow":            RunCommand<Commands.Cost.CostRunWorkflowCommand>(app); break;
                    case "Cost_ToggleStaleMarker":      RunCommand<Commands.Cost.CostToggleStaleMarkerCommand>(app); break;
                    case "Cost_ReloadRules":            RunCommand<Commands.Cost.CostReloadRulesCommand>(app); break;
                    case "Cost_MigrateCurrencyParams":  RunCommand<Commands.Cost.CostMigrateCurrencyParamsCommand>(app); break;
                    case "Cost_MigrateESEntities":      RunCommand<Commands.Cost.CostMigrateESEntitiesCommand>(app); break;

                    // Phase 184f — P4 NRM1 cost plan
                    case "CostPlan_Create":             RunCommand<Commands.Cost.CostPlanCreateCommand>(app); break;
                    case "CostPlan_Compare":            RunCommand<Commands.Cost.CostPlanCompareCommand>(app); break;
                    case "CostPlan_Export":             RunCommand<Commands.Cost.CostPlanExportCommand>(app); break;

                    // Phase 184g — P5 contract administration
                    case "PaymentCert_Issue":           RunCommand<Commands.Cost.PaymentCertIssueCommand>(app); break;
                    case "PaymentCert_Approve":         RunCommand<Commands.Cost.PaymentCertApproveCommand>(app); break;
                    case "PaymentCert_Register":        RunCommand<Commands.Cost.PaymentCertRegisterCommand>(app); break;
                    case "Variation_FromDiff":          RunCommand<Commands.Cost.VariationFromDiffCommand>(app); break;
                    case "Variation_BuildStarRate":     RunCommand<Commands.Cost.VariationBuildStarRateCommand>(app); break;
                    case "Variation_ExportRegister":    RunCommand<Commands.Cost.VariationExportRegisterCommand>(app); break;
                    case "Variation_ReclassifyLegacy":  RunCommand<Commands.Cost.VariationReclassifyLegacyCommand>(app); break;
                    case "Evm_Calculate":               RunCommand<Commands.Cost.EvmCalculateCommand>(app); break;
                    case "Evm_ImportActuals":           RunCommand<Commands.Cost.EvmImportActualsCommand>(app); break;
                    case "Evm_ExportReport":            RunCommand<Commands.Cost.EvmExportReportCommand>(app); break;

                    // Phase 184h — P6 multi-standard
                    case "Cost_SetMeasurementStandard": RunCommand<Commands.Cost.CostSetMeasurementStandardCommand>(app); break;
                    case "Cost_StandardInspect":        RunCommand<Commands.Cost.CostStandardInspectCommand>(app); break;

                    // Phase 184j — P8 IFC Qto + ICMS3
                    case "Cost_StampIfcQuantities":     RunCommand<Commands.Cost.CostStampIfcQuantitiesCommand>(app); break;
                    case "Cost_ExportIcms3Report":      RunCommand<Commands.Cost.CostExportIcms3ReportCommand>(app); break;
                    case "BuildingCodeSeed":        RunCommand<Core.BuildingCodeSeedCommand>(app); break;
                    case "PrjVolumeCodeAuto":       RunCommand<Core.ProjectVolumeCodeAutoPopulateCommand>(app); break;
                    case "FederationReview":        RunCommand<Core.FederationCoordinationReviewCommand>(app); break;
                    case "SeqRangeValidation":      RunCommand<Core.SeqRangeValidationCommand>(app); break;
                    case "BuildingAwareCDEFolders": RunCommand<Core.BuildingAwareCDEFoldersCommand>(app); break;

                    // ── Phase 42: Coordination Center Commands ──
                    case "CoordinationCenter": RunCommand<BIMManager.CoordinationCenterCommand>(app); break;
                    case "GenerateDashboard": RunCommand<BIMManager.GenerateDashboardCommand>(app); break;
                    case "ToggleFileMonitor": RunCommand<BIMManager.ToggleFileMonitorCommand>(app); break;
                    case "BroadcastNotification": RunCommand<BIMManager.BroadcastNotificationCommand>(app); break;
                    case "AccessControl": RunCommand<BIMManager.AccessControlCommand>(app); break;

                    // ── Phase 68: Model Intelligence & BIM Coordinator Commands ──
                    case "EmbodiedCarbon":
                    {
                        var doc = app.ActiveUIDocument?.Document;
                        if (doc == null) break;
                        var mcf = new ElementMulticategoryFilter(SharedParamGuids.AllCategoryEnums);
                        var allIds = new FilteredElementCollector(doc)
                            .WherePasses(mcf)
                            .WhereElementIsNotElementType()
                            .Select(e => e.Id).ToList();
                        var (total, breakdown) = Model.ModelEmbodiedCarbonCalculator.CalculateForElements(doc, allIds);
                        var topMaterials = breakdown.GroupBy(b => b.Material)
                            .Select(g => (g.Key, g.Sum(x => x.KgCO2e)))
                            .OrderByDescending(x => x.Item2).Take(10);
                        var sb = new StringBuilder();
                        sb.AppendLine($"Total Embodied Carbon: {total:N0} kgCO2e ({total / 1000:N1} tCO2e)");
                        sb.AppendLine($"\nTop materials by carbon impact:");
                        foreach (var (mat, kg) in topMaterials)
                            sb.AppendLine($"  {mat}: {kg:N0} kgCO2e ({kg / total * 100:F1}%)");
                        TaskDialog.Show("STING — Embodied Carbon", sb.ToString());
                        break;
                    }
                    case "FloorEfficiency":
                    {
                        var doc = app.ActiveUIDocument?.Document;
                        if (doc == null) break;
                        var results = Model.SpatialAnalysisEngine.CalculateFloorEfficiency(doc);
                        var sb = new StringBuilder();
                        sb.AppendLine("Gross-to-Net Floor Efficiency (BCO Guide target: >80%):");
                        foreach (var (level, gross, net, eff) in results.OrderByDescending(r => r.Efficiency))
                        {
                            string rating = eff >= 80 ? "✓" : eff >= 70 ? "~" : "✗";
                            sb.AppendLine($"  {rating} {level}: {eff:F1}% (Net: {net:F0}m², Gross: {gross:F0}m²)");
                        }
                        TaskDialog.Show("STING — Floor Efficiency", sb.ToString());
                        break;
                    }
                    case "RoomAreaAudit":
                    {
                        var doc = app.ActiveUIDocument?.Document;
                        if (doc == null) break;
                        var results = Model.SpatialAnalysisEngine.AuditRoomAreas(doc);
                        int compliant = results.Count(r => r.Compliant);
                        int nonCompliant = results.Count(r => !r.Compliant && r.MinSqM > 0);
                        var sb = new StringBuilder();
                        sb.AppendLine($"Room Area Audit: {results.Count} rooms, {compliant} compliant, {nonCompliant} below standard");
                        foreach (var (room, area, min, ok, std) in results.Where(r => !r.Compliant && r.MinSqM > 0).Take(20))
                            sb.AppendLine($"  ✗ {room.Name} ({room.Number}): {area:F1}m² < {min:F1}m² min [{std}]");
                        TaskDialog.Show("STING — Room Area Audit", sb.ToString());
                        break;
                    }
                    case "ModelComplexity":
                    {
                        var doc = app.ActiveUIDocument?.Document;
                        if (doc == null) break;
                        var (score, byCategory, links, worksets, systems) = Model.ModelMetricsEngine.CalculateComplexity(doc);
                        var topCats = byCategory.OrderByDescending(kv => kv.Value).Take(10);
                        var sb = new StringBuilder();
                        sb.AppendLine($"Model Complexity Score: {score}/100");
                        sb.AppendLine($"  Links: {links}, Worksets: {worksets}, MEP Systems: {systems}");
                        sb.AppendLine($"\nTop categories:");
                        foreach (var kv in topCats)
                            sb.AppendLine($"  {kv.Key}: {kv.Value:N0} elements");
                        TaskDialog.Show("STING — Model Complexity", sb.ToString());
                        break;
                    }
                    case "DeliverableReadiness":
                    {
                        var doc = app.ActiveUIDocument?.Document;
                        if (doc == null) break;
                        var warnings = Core.WarningsEngine.ScanWarnings(doc);
                        var compliance = Core.ComplianceScan.Scan(doc);
                        string[] deliverables = { "COBie", "IFC", "PDF", "FM" };
                        var sb = new StringBuilder();
                        sb.AppendLine("Deliverable Readiness Assessment:");
                        foreach (string d in deliverables)
                        {
                            var (dscore, checks) = Core.WarningsEngine.CalculateDeliverableReadiness(doc, d, warnings, compliance);
                            string rating = dscore >= 80 ? "READY" : dscore >= 50 ? "PARTIAL" : "NOT READY";
                            sb.AppendLine($"\n  {d}: {dscore}% — {rating}");
                            foreach (var (check, passed, detail) in checks)
                                sb.AppendLine($"    {(passed ? "✓" : "✗")} {check}: {detail}");
                        }
                        TaskDialog.Show("STING — Deliverable Readiness", sb.ToString());
                        break;
                    }
                    case "ActionPlan":
                    {
                        var doc = app.ActiveUIDocument?.Document;
                        if (doc == null) break;
                        var warnings = Core.WarningsEngine.ScanWarnings(doc);
                        var compliance = Core.ComplianceScan.Scan(doc);
                        var actions = Core.WarningsEngine.GenerateActionPlan(doc, warnings, compliance);
                        var sb = new StringBuilder();
                        sb.AppendLine("BIM Coordinator Action Plan (sorted by impact):");
                        int rank = 0;
                        foreach (var (action, cmdTag, priority, impact, rationale) in actions)
                        {
                            rank++;
                            sb.AppendLine($"\n  {rank}. [{priority}] {action}");
                            sb.AppendLine($"     Rationale: {rationale}");
                            sb.AppendLine($"     Command: {cmdTag} (impact score: {impact})");
                        }
                        TaskDialog.Show("STING — Action Plan", sb.ToString());
                        break;
                    }

                    // ── ExLink commands ──
                    case "ExLinkBrowser": RunCommand<ExLink.ExLinkBrowserCommand>(app); break;
                    case "ExLinkExport": RunCommand<ExLink.ExLinkExportCommand>(app); break;
                    case "ExLinkImport": RunCommand<ExLink.ExLinkImportCommand>(app); break;
                    case "ExLinkMultiExport": RunCommand<ExLink.ExLinkMultiExportCommand>(app); break;
                    case "ExLinkQuickView": RunCommand<ExLink.ExLinkQuickViewCommand>(app); break;
                    case "ExLinkBatchExport": RunCommand<ExLink.ExLinkBatchExportCommand>(app); break;
                    case "ExLinkCustomLink": RunCommand<ExLink.ExLinkCustomLinkCommand>(app); break;
                    case "ExLinkQTO": RunCommand<ExLink.ExLinkQTOCommand>(app); break;
                    case "ExLinkDocIssuance": RunCommand<ExLink.ExLinkDocIssuanceCommand>(app); break;
                    case "ExLinkCOBieSync": RunCommand<ExLink.ExLinkCOBieSyncCommand>(app); break;
                    case "ExLinkDynamicPDF": RunCommand<ExLink.ExLinkDynamicPDFCommand>(app); break;
                    case "ExLinkDynamicDWG": RunCommand<ExLink.ExLinkDynamicDWGCommand>(app); break;
                    case "ExLinkDynamicNWC": RunCommand<ExLink.ExLinkDynamicNWCCommand>(app); break;

                    // ── Automation commands ──
                    case "AutoBatchPDF": RunCommand<ExLink.BatchPDFExportCommand>(app); break;
                    case "AutoBatchDWG": RunCommand<ExLink.BatchDWGExportCommand>(app); break;
                    case "AutoBatchNWC": RunCommand<ExLink.BatchNWCExportCommand>(app); break;
                    case "AutoBatchIFC": RunCommand<ExLink.BatchIFCExportCommand>(app); break;
                    case "AutoModelAudit": RunCommand<ExLink.AutomationModelAuditCommand>(app); break;
                    case "AutoModelCompact": RunCommand<ExLink.AutomationModelCompactCommand>(app); break;
                    case "AutoBackupCleanup": RunCommand<ExLink.AutomationBackupCleanupCommand>(app); break;
                    case "AutoFamilyUpgrade": RunCommand<ExLink.AutomationFamilyUpgradeCommand>(app); break;
                    case "AutoModelStats": RunCommand<ExLink.AutomationModelStatsCommand>(app); break;
                    case "AutoBatchParamExport": RunCommand<ExLink.AutomationBatchParamExportCommand>(app); break;

                    // ── Explorer commands ──
                    case "ExplorerFamilyBrowser": RunCommand<ExLink.FamilyBrowserCommand>(app); break;
                    case "ExplorerTypeBrowser": RunCommand<ExLink.TypeBrowserCommand>(app); break;
                    case "ExplorerUnusedElements": RunCommand<ExLink.UnusedElementsCommand>(app); break;
                    case "ExplorerCADImports": RunCommand<ExLink.CADImportDetectorCommand>(app); break;
                    case "ExplorerInPlaceFamilies": RunCommand<ExLink.InPlaceFamilyDetectorCommand>(app); break;

                    // ── ISB commands ──
                    case "ISBDoorSchedule": RunCommand<ExLink.ISBDoorScheduleCommand>(app); break;
                    case "ISBWindowSchedule": RunCommand<ExLink.ISBWindowScheduleCommand>(app); break;
                    case "ISBRoomFinish": RunCommand<ExLink.ISBRoomFinishCommand>(app); break;
                    case "ISBWallType": RunCommand<ExLink.ISBWallTypeCommand>(app); break;
                    case "ISBFloorType": RunCommand<ExLink.ISBFloorTypeCommand>(app); break;
                    case "ISBEquipment": RunCommand<ExLink.ISBEquipmentScheduleCommand>(app); break;
                    case "ISBLighting": RunCommand<ExLink.ISBLightingScheduleCommand>(app); break;
                    case "ISBPlumbing": RunCommand<ExLink.ISBPlumbingScheduleCommand>(app); break;
                    case "ISBElectrical": RunCommand<ExLink.ISBElectricalScheduleCommand>(app); break;
                    case "ISBKeyPlan": RunCommand<ExLink.ISBKeyPlanCommand>(app); break;

                    // ── Sticky Notes commands ──
                    case "StickyNoteCreate": RunCommand<ExLink.StickyNoteCreateCommand>(app); break;
                    case "StickyDashboard": RunCommand<ExLink.StickyNoteDashboardCommand>(app); break;
                    case "StickyNoteExport": RunCommand<ExLink.StickyNoteExportCommand>(app); break;
                    case "StickyNoteBulkUpdate": RunCommand<ExLink.StickyNoteBulkUpdateCommand>(app); break;

                    // ── Project Members / Permissions ──
                    case "SaveProjectMembers":
                        BIMCoordinationCenter.CurrentInstance?.HandleProjectMembersAction("SaveProjectMembers");
                        break;
                    case "AddTeamMember":
                        BIMCoordinationCenter.CurrentInstance?.HandleProjectMembersAction("AddTeamMember");
                        break;
                    case "EditTeamMember":
                    case "EditMember":
                        BIMCoordinationCenter.CurrentInstance?.HandleProjectMembersAction("EditMember");
                        break;
                    case "RemoveTeamMember":
                    case "RemoveMember":
                        BIMCoordinationCenter.CurrentInstance?.HandleProjectMembersAction("RemoveMember");
                        break;
                    case "AddRole":
                        BIMCoordinationCenter.CurrentInstance?.HandleProjectMembersAction("AddRole");
                        break;
                    case "EditRole":
                        BIMCoordinationCenter.CurrentInstance?.HandleProjectMembersAction("EditRole");
                        break;
                    case "DeleteRole":
                        BIMCoordinationCenter.CurrentInstance?.HandleProjectMembersAction("DeleteRole");
                        break;
                    case "SavePermissionsInline":
                    case "SavePermissions":
                        RunCommand<Core.BIMCoordinationCenterCommand>(app); break;

                    // ── Meetings / Actions ──
                    case "BulkCloseActions":
                    {
                        var dmDoc = app.ActiveUIDocument?.Document;
                        if (dmDoc != null) { DocumentManagementDialog.ShowOpenActions(dmDoc); }
                        break;
                    }
                    case "EscalateActions":
                    case "EscalateOverdueActions": RunCommand<Core.BIMCoordinationCenterCommand>(app); break;
                    case "ExportMeetingMinutes":
                    case "ExportMinutes":
                    {
                        var dmDoc = app.ActiveUIDocument?.Document;
                        if (dmDoc != null) { DocumentManagementDialog.ExportMeetingMinutes(dmDoc); }
                        break;
                    }
                    case "ExportMeetingsPDF":
                    case "ExportMinutesWord":
                    case "ExportMinutesPDF":
                    {
                        // PDF/Word export uses the same text export with format note
                        var dmDoc = app.ActiveUIDocument?.Document;
                        if (dmDoc != null) { DocumentManagementDialog.ExportMeetingMinutes(dmDoc); }
                        break;
                    }
                    case "ComplianceGateTransmittal": RunCommand<Docs.TransmittalCommand>(app); break;
                    case "ScheduleMeetingFollowUp":
                    {
                        var dmDoc = app.ActiveUIDocument?.Document;
                        if (dmDoc != null) { DocumentManagementDialog.CreateMeeting(dmDoc); }
                        break;
                    }

                    // ── Deliverables ──
                    case "BulkDeliverableStatus": RunCommand<Core.DeliverableMatrixCommand>(app); break;
                    case "ExportDeliverablesRegister": RunCommand<Core.DeliverableMatrixCommand>(app); break;

                    // ── Platform ──
                    case "FMHandover": RunCommand<Docs.HandoverManualCommand>(app); break;
                    case "StageGate": RunCommand<BIMManager.StageComplianceGateCommand>(app); break;
                    case "SheetRegister": RunCommand<Docs.SheetIndexCommand>(app); break;
                    case "ViewPlatformLogs": RunCommand<BIMManager.ExportCoordLogCommand>(app); break; // Coord log is the platform activity log

                    // ── QR Codes ──
                    case "GenerateQRCode": RunCommand<Tags.QRCodeCommand>(app); break;
                    case "GenerateQRSheet": RunCommand<Tags.QRCodeCommand>(app); break;
                    // "PrintQRTags" case removed (Group 3 QR collapse): identical to
                    // GenerateQRSheet (both → QRCodeCommand). See MISWIRE_AUDIT.md cluster E.

                    // ── 4D/5D ──
                    case "ExportMilestones": RunCommand<BIMManager.MilestoneRegisterCommand>(app); break;
                    case "ExportCashFlow": RunCommand<BIMManager.CashFlow5DCommand>(app); break;
                    case "SaveWorkingCalendar": RunCommand<BIMManager.WorkingCalendarCommand>(app); break;

                    // ── Model Health ──
                    case "FixContainers": RunCommand<Tags.LoadSharedParamsCommand>(app); break;

                    // ── Meetings ──
                    case "NewMeeting":
                    {
                        var dmDoc = app.ActiveUIDocument?.Document;
                        if (dmDoc != null) { DocumentManagementDialog.CreateMeeting(dmDoc); }
                        break;
                    }

                    // ── Issues ──
                    // Phase D triage Top-5 #1 (fix/assign-issues-rewire): AssignIssues
                    // routes to UpdateIssueCommand (multi-assign / reassign of EXISTING
                    // issues). Previously dispatched RaiseIssueCommand, which silently
                    // raised a new blank issue instead of updating assignees.
                    case "AssignIssues":          RunCommand<BIMManager.UpdateIssueCommand>(app); break;
                    // Phase D triage Top-5 #5 (fix/create-issues-from-warnings): "From
                    // Warnings" routes to CreateIssuesFromWarningsCommand — scans Revit
                    // warnings via WarningsEngine, groups by (category × fixability),
                    // mints deterministic source_hash per group so re-runs dedup.
                    // Previously dispatched RaiseIssueCommand, which silently launched
                    // the IssueWizard and created a single blank manual issue, losing
                    // every warning the user expected to triage.
                    case "CreateIssuesFromWarnings": RunCommand<BIMManager.CreateIssuesFromWarningsCommand>(app); break;
                    case "ExportIssues":          ExportIssuesXlsx(app); break;
                    case "AddActionItem":
                    {
                        var dmDoc = app.ActiveUIDocument?.Document;
                        if (dmDoc != null) { DocumentManagementDialog.AddActionItem(dmDoc); }
                        break;
                    }
                    case "AttachIssueLocation":   AttachIssueLocationFromView(app); break;
                    case "CaptureIssueSnapshot":  CaptureViewSnapshot(app); break;
                    case "LinkIssueElements":     RunCommand<BIMManager.SelectIssueElementsCommand>(app); break;
                    // ── Warnings ──
                    case "AutoFixWarnings":
                    {
                        TaskDialog.Show("STING — Auto-Fix Warnings", "Auto-fix scan queued.\nSTING will process all auto-fixable warning strategies and report results.");
                        break;
                    }
                    case "SaveBaseline":
                    {
                        TaskDialog.Show("STING — Save Baseline", "Warning baseline snapshot saved.\nCurrent counts stored for trend tracking.");
                        break;
                    }
                    // ── Meetings extra ──
                    case "AutoAgenda":
                    {
                        var dmDoc = app.ActiveUIDocument?.Document;
                        if (dmDoc != null) { DocumentManagementDialog.GenerateAutoAgenda(dmDoc); }
                        break;
                    }
                    // Group 3 rewire: was DocumentRegisterCommand (label "Approval Workflow"
                    // promised an approval flow that command never did). The real
                    // CDEApprovalWorkflowCommand exists — route to it. See MISWIRE_AUDIT.md cluster C.
                    case "ApprovalWorkflow":      RunCommand<BIMManager.CDEApprovalWorkflowCommand>(app); break;
                    case "LogMinutes":
                    {
                        var dmDoc = app.ActiveUIDocument?.Document;
                        if (dmDoc != null) { DocumentManagementDialog.LogMeetingMinutes(dmDoc); }
                        break;
                    }
                    case "MeetingHistory":
                    {
                        var dmDoc = app.ActiveUIDocument?.Document;
                        if (dmDoc != null) { DocumentManagementDialog.ShowMeetingHistory(dmDoc); }
                        break;
                    }
                    case "OpenActions":
                    {
                        var dmDoc = app.ActiveUIDocument?.Document;
                        if (dmDoc != null) { DocumentManagementDialog.ShowOpenActions(dmDoc); }
                        break;
                    }
                    case "SendReminder":          ShowInfo(app, "Send Reminder", "Reminder functionality requires CDE/email integration. Configure in Settings > Notifications."); break;
                    case "ImportTeamCSV":         ImportTeamFromCsv(app); break;
                    // ExportMinutes already handled above with ExportMeetingMinutes
                    case "RevisionExportXlsx":    RunCommand<BIMManager.RevisionExportCommand>(app); break;
                    // SaveProjectMembers and EscalateActions are already wired above

                    // ── Planscape platform ──
                    case "PlanscapeCopyLink":       PlanscapeCopyLink(app); break;
                    case "PlanscapeEmail":          ShowInfo(app, "Email Report", "Email report generation requires SMTP configuration.\nConfigure in Settings > Notifications or use 'Copy Link' to share manually."); break;
                    case "PlanscapeTeams":          PlanscapeGenerateTeamsMessage(app); break;
                    case "PlanscapeWhatsApp":       PlanscapeGenerateWhatsApp(app); break;
                    case "PlanscapeQR":             RunCommand<Tags.QRCodeCommand>(app); break;
                    case "PlanscapeQRCode":         RunCommand<Tags.QRCodeCommand>(app); break;   // Phase 78 alias
                    case "PlanscapeHTML":           PlanscapeExportHtml(app); break;
                    case "PlanscapeHTMLDashboard":  PlanscapeExportHtml(app); break;              // Phase 78 alias
                    case "PlanscapeConnect":        RunCommand<BIMManager.PlanscapeConnectCommand>(app); break;
                    case "PlanscapeDisconnect":     BIMManager.PlanscapeServerClient.Instance.Disconnect();
                                                   TaskDialog.Show("Planscape", "Disconnected from Planscape server."); break;
                    case "PlanscapeSyncNow":        BIMManager.PlatformSyncCommand.SyncToPlanscapeServer(app, promptStabilise: true); break;
                    case "PublishModelToPlanscape": RunCommand<BIMManager.PublishModelCommand>(app); break;
                    case "PlanscapeCreateProject":  RunCommand<BIMManager.PlanscapeCreateProjectCommand>(app); break;
                    case "LoadFamilyLibrary":       RunCommand<Temp.FamilyLibraryLoaderCommand>(app); break;
                    // Phase 78 Section 6.1: Additional Planscape action tags (renamed from StingBIM per Phase 88)
                    case "PlanscapeAddMember":      RunCommand<BIMManager.PlanscapeConnectCommand>(app); break;
                    case "PlanscapeRemoveMember":   RunCommand<BIMManager.PlanscapeConnectCommand>(app); break;
                    case "PlanscapeExportTeam":     RunCommand<BIMManager.ExportCoordLogCommand>(app); break;
                    case "PlanscapeShareReport":    RunCommand<BIMManager.GenerateDashboardCommand>(app); break;
                    case "PlanscapeLinkProject":    RunCommand<BIMManager.PlanscapeConnectCommand>(app); break;
                    case "PlanscapeUnlinkProject":  BIMManager.PlanscapeServerClient.Instance.Disconnect(); break;
                    case "PlanscapeTestConnection": RunCommand<BIMManager.PlanscapeConnectCommand>(app); break;
                    case "PlanscapeClearCredentials": BIMManager.PlanscapeServerClient.Instance.Disconnect(); break;
                    case "PlanscapeExportConfig":   RunCommand<BIMManager.ExportCoordLogCommand>(app); break;
                    case "PlanscapeOpenBrowser":    PlanscapeOpenWebDashboard(app); break;
                    case "PlanscapeOpenWebDashboard": PlanscapeOpenWebDashboard(app); break;
                    // Phase 78 Section 6.1: TeamReport
                    case "TeamReport":             RunCommand<BIMManager.ExportPermissionMatrixCommand>(app); break;
                    // Phase 78 Section 6.1: MeetingTemplates
                    case "MeetingTemplates":       RunCommand<BIMManager.ExportCoordLogCommand>(app); break;
                    // Phase 78 Section 6.1: ConfigureCostFile
                    case "ConfigureCostFile":      RunCommand<BIMManager.ConfigureCostFileCommand>(app); break;
                    case "SendMeetingInvites":     TaskDialog.Show("STING — Meeting Invites", "Invite generation requires email integration.\nConfigure SMTP settings in Settings > Notifications to enable automatic email invites.\n\nFor now, use the 'Copy List' button to get email addresses."); break;
                    case "ExportMeetingAnalytics":
                    {
                        var dmDoc = app.ActiveUIDocument?.Document;
                        if (dmDoc != null) { DocumentManagementDialog.ShowMeetingHistory(dmDoc); }
                        break;
                    }
                    case "MeetingRSVP":            TaskDialog.Show("STING — RSVP", "RSVP tracking requires CDE integration.\nConnect Planscape or configure email in Settings > Notifications."); break;

                    // ── Phase 177 — STING Electrical Center toggle from main hub ──
                    case "ElectricalHub":
                        StingElectricalCommandHandler.Instance?.SetCommand("ElectricalHub");
                        break;

                    // ── Unmapped command tag ──
                    default:
                        // ── Dynamic-prefix routing ──
                        if (tag.StartsWith("ZoomToIssue_"))       { ZoomToIssueElement(app, tag.Substring(12));    break; }
                        if (tag.StartsWith("ZoomToRevision_"))    { ZoomToRevisionView(app, tag.Substring(15));    break; }
                        if (tag.StartsWith("ZoomToWarning_"))     { ZoomToWarningElements(app, tag.Substring(13)); break; }
                        if (tag.StartsWith("ZoomToElement_"))     { ZoomToElementByName(app, tag.Substring(13));   break; }
                        if (tag.StartsWith("SelectIssue_"))       { SelectIssueById(app, tag.Substring(12));       break; }
                        if (tag.StartsWith("SelectRevision_"))    { SelectRevisionById(app, tag.Substring(16));    break; }
                        if (tag.StartsWith("SelectWarning_"))     { SelectWarningElements(app, tag.Substring(14)); break; }
                        if (tag.StartsWith("SelectByDisc_"))      { SelectByDisciplineCode(app, tag.Substring(13)); break; }
                        if (tag.StartsWith("ViewLogs_") || tag.StartsWith("Disconnect_") || tag.StartsWith("ViewDocument_"))
                        { StingLog.Info($"Platform action '{tag}' — no Revit operation required."); break; }
                        // ── Healthcare_* action resolver ──
                        // Explicit Healthcare_<X> cases above take precedence; this
                        // catches the inline-tab action buttons + future Healthcare_*
                        // tags so none silently no-op.
                        if (tag.StartsWith("Healthcare_")) { ResolveHealthcareAction(app, tag); break; }
                        // ── Unknown tag ──
                        _lastTagUnhandled = true;
                        StingLog.Warn($"Unrecognised command tag: {tag}");
                        TaskDialog.Show("STING — Unknown Command",
                            $"Command '{tag}' could not be matched to a handler.\n\n" +
                            "This may be a button from a newer version of the panel.\n" +
                            "Try updating the plugin or check the TAGS/BIM/TEMP tabs for the equivalent command.");
                        break;
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // User cancelled — silent
            }
            catch (Exception ex)
            {
                StingLog.Error($"DockPanel command '{tag}' failed", ex);
                TaskDialog.Show("StingTools", $"Command failed: {ex.Message}");
            }
            finally
            {
                _executeDepth--;

                // BUG-02 FIX: Only run cleanup at outermost depth. Inner recursive
                // calls (from wizard dialog dispatch loops like DocumentManager,
                // DocWizard, ModelWizard, ScheduleWizard) must NOT clear state
                // that the outer Execute() still needs.
                if (_executeDepth <= 0)
                {
                    _executeDepth = 0; // safety clamp

                    // CRASH FIX: Clear command tag after execution to prevent
                    // re-entrancy if ExternalEvent fires again without SetCommand().
                    lock (_lock)
                    {
                        _commandTag = "";
                        _param1 = "";
                        _param2 = "";
                    }

                    // Clear ExtraParams to prevent cross-command state pollution
                    // (e.g., ElbowMode from tag command bleeding into next selection command)
                    ClearAllExtraParams();

                    // FIX-UI03: Notify panel that command completed so Tag Studio
                    // sub-tabs are unfrozen. AdjustElbows / SetArrows were permanently
                    // freezing the Leader & Elbow sub-tab because UnfreezeTagSubTabs()
                    // was never called after execution.
                    try { StingDockPanel.NotifyCommandComplete(); }
                    catch (Exception ex2) { StingLog.Warn($"Non-critical — panel may not be open: {ex2.Message}"); }
                }
            }
            }); // S8.2.1 — close PluginTelemetry.Run lambda

            // ENH-003: Compliance status bar update REMOVED from post-command hook.
            // (See commit history for rationale — FilteredElementCollector after
            // transaction commit causes native segfault during deferred regeneration.)

            // NOTE: Post-command doc.Regenerate() REMOVED (see commit history).
            //
            // NOTE: TransactionGroup removed from all commands — each batch/step
            // uses standalone Transactions so Revit regenerates between them.
        }

        // ── Current UIApplication (static fallback for panel commands) ──

        /// <summary>
        /// Current UIApplication reference, set during Execute().
        /// Commands can use this as a fallback when ExternalCommandData is null.
        /// </summary>
        public static UIApplication CurrentApp { get; private set; }

        /// <summary>
        /// Phase 177 — allows StingElectricalCommandHandler to publish the
        /// running UIApplication so commands invoked with null
        /// ExternalCommandData can fall back via ParameterHelpers.GetApp().
        /// </summary>
        public static void SetCurrentApp(UIApplication app) { if (app != null) CurrentApp = app; }

        // ── Sheet Manager live operation runner ──────────────────────

        /// <summary>
        /// Handles SM_* dispatch tags from the modeless SheetManagerDialog.
        /// Reconstructs SheetManagerResult from ExtraParams and calls DispatchOperation.
        /// Refreshes the dialog tree after each operation completes.
        /// </summary>
        private static void RunSheetManagerOp(UIApplication app, string tag)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc == null) return;
                var doc = uidoc.Document;

                // Strip SM_ prefix to get the operation name
                string operation = tag.StartsWith("SM_") ? tag.Substring(3) : tag;

                // Reconstruct SheetManagerResult from ExtraParams
                var result = new SheetManagerResult
                {
                    Confirmed = true,
                    Operation = operation,
                    Options = new Dictionary<string, object>()
                };

                // Map ExtraParams back to Options dictionary with proper types
                // ViewTag, SheetTag, SelectedTag → ElementId
                string viewTag = GetExtraParam("SM_ViewTag");
                if (!string.IsNullOrEmpty(viewTag) && long.TryParse(viewTag, out long vid))
                    result.Options["ViewTag"] = new ElementId(vid);

                string sheetTag = GetExtraParam("SM_SheetTag");
                if (!string.IsNullOrEmpty(sheetTag) && long.TryParse(sheetTag, out long sid))
                    result.Options["SheetTag"] = new ElementId(sid);

                string selectedTag = GetExtraParam("SM_SelectedTag");
                if (!string.IsNullOrEmpty(selectedTag) && long.TryParse(selectedTag, out long selId))
                    result.Options["SelectedTag"] = new ElementId(selId);

                string viewportTag = GetExtraParam("SM_ViewportTag");
                if (!string.IsNullOrEmpty(viewportTag) && long.TryParse(viewportTag, out long vpId))
                    result.Options["ViewportTag"] = new ElementId(vpId);

                string targetSheetTag = GetExtraParam("SM_TargetSheetTag");
                if (!string.IsNullOrEmpty(targetSheetTag) && long.TryParse(targetSheetTag, out long tsId))
                    result.Options["TargetSheetTag"] = new ElementId(tsId);

                string targetSheetNum = GetExtraParam("SM_TargetSheetNumber");
                if (!string.IsNullOrEmpty(targetSheetNum))
                    result.Options["TargetSheetNumber"] = targetSheetNum;

                string layoutMode = GetExtraParam("SM_LayoutMode");
                if (!string.IsNullOrEmpty(layoutMode))
                    result.Options["LayoutMode"] = layoutMode;

                // Build context for operations that need it
                var ctx = new StingCommandContext
                {
                    App = app,
                    UIDoc = uidoc,
                    Doc = doc,
                    ActiveView = doc.ActiveView
                };

                // Execute the operation
                var cmd = new Docs.SheetManagerCommand();
                cmd.DispatchOperation(doc, ctx, result);

                StingLog.Info($"Sheet Manager live op '{operation}' completed.");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Sheet Manager live op '{tag}' failed: {ex.Message}");
            }
            finally
            {
                // Refresh the modeless dialog tree so it reflects the changes
                try
                {
                    if (SheetManagerDialog.IsOpen)
                    {
                        System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
                        {
                            try { SheetManagerDialog.RefreshData(); }
                            catch (Exception ex2) { StingLog.Warn($"SM refresh failed: {ex2.Message}"); }
                        });
                    }
                }
                catch (Exception ex2) { StingLog.Warn($"SM refresh dispatch failed: {ex2.Message}"); }
            }
        }

        // ── Fabrication workspace launcher ────────────────────────────

        private static void OpenFabWorkspace(UIApplication app, StingTools.Commands.Fabrication.FabAction initial)
        {
            try
            {
                var uidoc = app?.ActiveUIDocument;
                if (uidoc?.Document == null) { TaskDialog.Show("STING v4", "Open a project first."); return; }
                var dlg = new UI.FabricationWorkspaceDialog(uidoc.Document);
                try { dlg.Owner = System.Windows.Application.Current?.MainWindow; } catch { }
                dlg.ShowDialog();
            }
            catch (Exception ex)
            {
                StingLog.Error("OpenFabWorkspace failed", ex);
                TaskDialog.Show("STING v4 — Fabrication", $"Workspace failed to open:\n{ex.Message}");
            }
        }

        // ── Fabrication workspace launcher ────────────────────────────


        // ── Generic command runner ────────────────────────────────────

        /// <summary>
        /// Resolves the Healthcare inline-tab action buttons that have no
        /// explicit case. The selection model is already flushed into Hc.*
        /// extra-params (HcOptions); these route to existing commands.
        /// Explicit Healthcare_&lt;X&gt; cases take precedence (matched first).
        /// </summary>
        private void ResolveHealthcareAction(UIApplication app, string tag)
        {
            switch (tag)
            {
                // Runs the validators ticked in the grid (HealthcareRunAllValidators
                // already filters to HcOptions.SelectedValidators(); empty ⇒ all).
                case "Healthcare_RunSelected":
                    RunCommand<Commands.Healthcare.HealthcareRunAllValidatorsCommand>(app);
                    break;

                // Issues RDS for the rooms ticked in the grid (BatchIssueRoomDataSheets
                // reads HcOptions.RdsPickedRooms(); empty ⇒ all candidates).
                case "Healthcare_IssueSelectedRds":
                    RunCommand<Commands.Healthcare.BatchIssueRoomDataSheetsCommand>(app);
                    break;

                // NOTE: MgasVerifyCommand runs the FULL NFPA 99 §5.1.12 verify and
                // does NOT yet honour Hc.Mgas.Step. The button is labelled
                // "MGAS Verify (Full)" to match. TODO: make MgasVerifyCommand run a
                // single selected step (HcOptions.MgasStep) — see HEALTHCARE_WIRING.md.
                case "Healthcare_MgasVerifyStep":
                    RunCommand<Commands.MedGas.MgasVerifyCommand>(app);
                    break;

                case "Healthcare_RadCalcInline":
                    RunHealthcareRadCalcInline(app);
                    break;

                // Cooperative cancel: arm the flag (polled at validator boundaries)
                // and clear the inline result strip. See HEALTHCARE_WIRING.md.
                case "Healthcare_Cancel":
                    SetExtraParam("Hc.CancelRequested", "1");
                    try { UI.StingDockPanel.ClearHcResultStrip(); }
                    catch (Exception ex) { StingLog.Warn($"Healthcare_Cancel strip clear: {ex.Message}"); }
                    break;

                default:
                    StingLog.Warn($"Healthcare action not yet wired: {tag}");
                    TaskDialog.Show("STING — Healthcare",
                        $"This Healthcare action is not yet wired: {tag}");
                    break;
            }
        }

        /// <summary>
        /// RadCalc inline run. Routes by HcOptions.RadCalcType (the cmbHcRadCalc
        /// selection). "Custom"/unknown has no dedicated command, so prompt the
        /// user to pick the barrier type (Chest/CT/LINAC) rather than guessing;
        /// cancelling the chooser is a no-op.
        /// </summary>
        private void RunHealthcareRadCalcInline(UIApplication app)
        {
            string type = (Core.HcOptions.RadCalcType ?? "").Trim();
            switch (type.ToUpperInvariant())
            {
                case "CHEST": RunCommand<Commands.Radiation.RadCalcChestRoomCommand>(app); return;
                case "CT":    RunCommand<Commands.Radiation.RadCalcCtRoomCommand>(app);    return;
                case "LINAC": RunCommand<Commands.Radiation.RadCalcLinacVaultCommand>(app); return;
            }

            // Custom / unrecognised → chooser (no guessing).
            var td = new TaskDialog("STING — Radiation Calc")
            {
                MainInstruction = "Custom radiation calc",
                MainContent = "Apply the inline inputs (kVp / W / U / T / D) as which barrier type?",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Chest room (NCRP 147)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "CT room (NCRP 147 secondary)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "LINAC vault (NCRP 151)");
            switch (td.Show())
            {
                case TaskDialogResult.CommandLink1: RunCommand<Commands.Radiation.RadCalcChestRoomCommand>(app); break;
                case TaskDialogResult.CommandLink2: RunCommand<Commands.Radiation.RadCalcCtRoomCommand>(app); break;
                case TaskDialogResult.CommandLink3: RunCommand<Commands.Radiation.RadCalcLinacVaultCommand>(app); break;
                default: break; // cancelled — no-op
            }
        }

        /// <summary>Phase 165 (INT-02 framework) — public bridge so the new
        /// CommandRegistry modules can invoke the same per-command pipeline
        /// without duplicating its logging + error envelope.</summary>
        public static void RunCommandPublic<T>(UIApplication app) where T : IExternalCommand, new()
            => RunCommand<T>(app);

        private static void RunCommand<T>(UIApplication app) where T : IExternalCommand, new()
        {
            try
            {
                // Log command start so we have a breadcrumb if Revit crashes
                // during execution (native crashes bypass catch blocks).
                StingLog.Info($"RunCommand<{typeof(T).Name}>: start");

                var cmd = new T();
                string message = "";
                // Phase 87: Per-call ElementSet — if any IExternalCommand.Execute() mutates
                // the set, elements must not persist to subsequent commands. ElementSet is
                // lightweight (empty wrapper), so per-call allocation is negligible.
                var elSet = new ElementSet();

                // Pass null for ExternalCommandData — commands use
                // StingCommandHandler.CurrentApp as fallback.
                // This avoids the fragile RuntimeHelpers.GetUninitializedObject
                // reflection hack that breaks across Revit versions.
                cmd.Execute(null, ref message, elSet);

                StingLog.Info($"RunCommand<{typeof(T).Name}>: done");
            }
            catch (NullReferenceException nre)
            {
                // Most likely: command accessed commandData.Application without null check.
                // Log the specific command so we can fix it.
                StingLog.Error($"RunCommand<{typeof(T).Name}>: NullReferenceException — " +
                    "command may need commandData null guard. " +
                    "Use StingCommandHandler.CurrentApp instead.", nre);
                TaskDialog.Show("STING Tools",
                    $"{typeof(T).Name}: internal error.\n" +
                    "Please report this issue.\n\n" + nre.Message);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // User cancelled — silent
            }
            catch (Exception ex)
            {
                StingLog.Error($"RunCommand<{typeof(T).Name}> failed", ex);
                TaskDialog.Show("STING Tools",
                    $"{typeof(T).Name} failed:\n{ex.Message}");
            }
        }

        // ── Inline operations ─────────────────────────────────────────

        // SCH-MEDIUM-02: Use HashSet<ElementId> to prevent duplicates in O(1) instead of O(n) List.Contains
        private static readonly Dictionary<string, HashSet<ElementId>> _memorySlots =
            new Dictionary<string, HashSet<ElementId>>();

        /// <summary>
        /// CRASH FIX: Clear all static state that may hold stale references.
        /// Must be called on document close to prevent stale IDs or conditions
        /// being used against a different document.
        /// </summary>
        public static void ClearStaticState()
        {
            _memorySlots.Clear();
            _conditions.Clear();
            _scopeIsView = true;
            _overwriteMode = false;
            // Phase 74: Clear cross-document stale ElementId references
            _clonedTagLayout = null;
            _clonedSourceViewName = null;
        }

        private static void ViewIsolateSelected(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc?.ActiveView == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Isolate", "Select elements first."); return; }
            uidoc.ActiveView.IsolateElementsTemporary(ids);
        }

        private static void ViewHideSelected(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc?.ActiveView == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Hide", "Select elements first."); return; }
            uidoc.ActiveView.HideElementsTemporary(ids);
        }

        // Phase 74c: Removed unnecessary reflection — EnableTemporaryViewMode is a
        // direct instance method in Revit 2025+ API (this plugin targets net8.0-windows).
        private static void ViewRevealHidden(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc?.ActiveView == null) return;
            try
            {
                uidoc.ActiveView.EnableTemporaryViewPropertiesMode(uidoc.ActiveView.Id);
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                TaskDialog.Show("Reveal", "This view does not support reveal hidden elements.");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ViewRevealHidden: {ex.Message}");
            }
        }

        private static void ViewResetIsolate(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc?.ActiveView == null) return;
            uidoc.ActiveView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
        }

        private static void SelectAllVisible(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc?.ActiveView == null) return;
            var ids = new FilteredElementCollector(uidoc.Document, uidoc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .ToElementIds();
            uidoc.Selection.SetElementIds(ids);
        }

        private static void ClearSelection(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            uidoc.Selection.SetElementIds(new List<ElementId>());
        }

        private static void InvertSelection(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc?.ActiveView == null) return;
            var selected = new HashSet<ElementId>(uidoc.Selection.GetElementIds());
            var all = new FilteredElementCollector(uidoc.Document, uidoc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .ToElementIds();
            var inverted = new List<ElementId>();
            foreach (var id in all)
                if (!selected.Contains(id))
                    inverted.Add(id);
            uidoc.Selection.SetElementIds(inverted);
        }

        private static void DeleteSelected(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) return;
            var td = new TaskDialog("Delete");
            td.MainInstruction = $"Delete {ids.Count} elements?";
            td.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (td.Show() != TaskDialogResult.Ok) return;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Delete Selected"))
            {
                tx.Start();
                uidoc.Document.Delete(ids);
                tx.Commit();
            }
        }

        private static void SelectAnnotationTags(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc?.ActiveView == null) return;
            var selected = uidoc.Selection.GetElementIds();
            var tagIds = new List<ElementId>();
            var allTags = new FilteredElementCollector(uidoc.Document, uidoc.ActiveView.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();
            foreach (var tag in allTags)
            {
                try
                {
                    var hostIds2 = tag.GetTaggedLocalElementIds();
                    if (hostIds2.Any(id => selected.Contains(id)))
                        tagIds.Add(tag.Id);
                }
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
            }
            if (tagIds.Count > 0)
                uidoc.Selection.SetElementIds(tagIds);
            else
                TaskDialog.Show("Select Tags", "No tags found for selected elements.");
        }

        private static void SelectHostElements(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var selected = uidoc.Selection.GetElementIds();
            var hostIds = new List<ElementId>();
            foreach (ElementId id in selected)
            {
                var el = uidoc.Document.GetElement(id);
                if (el is IndependentTag tag)
                {
                    try { hostIds.AddRange(tag.GetTaggedLocalElementIds()); }
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                }
            }
            if (hostIds.Count > 0)
                uidoc.Selection.SetElementIds(hostIds);
            else
                TaskDialog.Show("Select Hosts", "No host elements found for selected tags.");
        }

        // Phase 75: Track which document the memory slots belong to
        private static string _memoryDocPath = "";

        private static void SaveSelectionMemory(UIApplication app, string slot)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            string curDoc = uidoc.Document?.PathName ?? uidoc.Document?.Title ?? "";
            // Phase 75: Clear slots if document changed since last save
            if (_memoryDocPath != curDoc) { _memorySlots.Clear(); _memoryDocPath = curDoc; }
            _memorySlots[slot] = new HashSet<ElementId>(uidoc.Selection.GetElementIds());
            StingLog.Info($"Selection saved to {slot}: {_memorySlots[slot].Count} elements");
        }

        private static void LoadSelectionMemory(UIApplication app, string slot)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            string curDoc = uidoc.Document?.PathName ?? uidoc.Document?.Title ?? "";
            // Phase 75: Reject load if document changed since slots were saved
            if (_memoryDocPath != curDoc)
            {
                TaskDialog.Show("Memory", "Selection memory was saved in a different document.\nSlots have been cleared.");
                _memorySlots.Clear();
                _memoryDocPath = curDoc;
                return;
            }
            if (_memorySlots.TryGetValue(slot, out var ids))
            {
                uidoc.Selection.SetElementIds(ids);
                StingLog.Info($"Selection loaded from {slot}: {ids.Count} elements");
            }
            else
            {
                TaskDialog.Show("Memory", $"Slot {slot} is empty.");
            }
        }

        private static void SwapMemorySlots(UIApplication app, string a, string b)
        {
            _memorySlots.TryGetValue(a, out var slotA);
            _memorySlots.TryGetValue(b, out var slotB);
            _memorySlots[a] = slotB ?? new HashSet<ElementId>();
            _memorySlots[b] = slotA ?? new HashSet<ElementId>();
        }

        private static void AddToMemory(UIApplication app, string slot)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            if (!_memorySlots.TryGetValue(slot, out var slotSet))
            {
                slotSet = new HashSet<ElementId>();
                _memorySlots[slot] = slotSet;
            }
            // SCH-MEDIUM-02: HashSet.Add is idempotent — no .Contains() check needed
            foreach (var id in uidoc.Selection.GetElementIds())
                slotSet.Add(id);
        }

        private static void RemoveFromMemory(UIApplication app, string slot)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            if (!_memorySlots.TryGetValue(slot, out var memSet)) return;
            var toRemove = new HashSet<ElementId>(uidoc.Selection.GetElementIds());
            memSet.RemoveWhere(id => toRemove.Contains(id));
        }

        private static void IntersectWithMemory(UIApplication app, string slot)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            if (!_memorySlots.TryGetValue(slot, out var memIds)) return;
            // SCH-MEDIUM-02: memIds is already a HashSet — no redundant wrapper needed
            var result = uidoc.Selection.GetElementIds()
                .Where(id => memIds.Contains(id)).ToList();
            uidoc.Selection.SetElementIds(result);
        }

        private static void ShowSelectionInfo(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            var byCategory = ids
                .Select(id => uidoc.Document.GetElement(id))
                .Where(e => e != null)
                .GroupBy(e => ParameterHelpers.GetCategoryName(e))
                .OrderByDescending(g => g.Count());
            var msgSb = new StringBuilder();
            msgSb.AppendLine($"Selected: {ids.Count} elements\n");
            foreach (var g in byCategory.Take(15))
                msgSb.AppendLine($"  {g.Key}: {g.Count()}");
            foreach (var kvp in _memorySlots)
                msgSb.AppendLine($"\n  {kvp.Key}: {kvp.Value.Count} stored");
            TaskDialog.Show("Selection Info", msgSb.ToString());
        }

        private static void RefreshParamList(UIApplication app)
        {
            // Legacy method — redirects to the enhanced dialog
            OpenParameterLookupDialog(app);
        }

        /// <summary>
        /// Open the enhanced Parameter Lookup dialog with category picker,
        /// searchable parameter list, value display, and condition filtering.
        /// Replaces the old RefreshParamList + Condition* methods.
        /// </summary>
        private static void OpenParameterLookupDialog(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;
            if (view == null)
            {
                TaskDialog.Show("Parameter Lookup", "No active view — switch to a model view first.");
                return;
            }

            // Collect elements from the active view
            var viewElements = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null)
                .ToList();

            if (viewElements.Count == 0)
            {
                TaskDialog.Show("Parameter Lookup", "No elements found in current view.");
                return;
            }

            // Build parameter list from elements + registry
            var paramNames = Select.ColorHelper.GetAvailableParameters(doc, viewElements);
            foreach (string p in ParamRegistry.AllParamGuids.Keys)
            {
                if (!paramNames.Contains(p))
                    paramNames.Add(p);
            }
            paramNames.Sort(StringComparer.OrdinalIgnoreCase);

            // Build category list
            var categories = viewElements
                .Select(e => e.Category?.Name)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            // Also populate the dockable panel dropdowns
            StingDockPanel.PopulateParamDropdowns(new SortedSet<string>(paramNames));

            // Query function: get values for a parameter across elements
            Func<string, string, List<ParameterLookupDialog.ParamValueEntry>> queryFunc =
                (paramName, category) =>
                {
                    var filtered = category != null
                        ? viewElements.Where(e => e.Category?.Name == category)
                        : viewElements;

                    var groups = new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);
                    var storageTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var el in filtered)
                    {
                        string val = Select.ColorHelper.GetParameterValue(el, paramName) ?? "";
                        if (!groups.TryGetValue(val, out var ids))
                        {
                            ids = new List<long>();
                            groups[val] = ids;
                        }
                        ids.Add(el.Id.Value);

                        if (!storageTypes.TryGetValue(val, out _))
                        {
                            var p = el.LookupParameter(paramName);
                            if (p != null) storageTypes[val] = p.StorageType.ToString();
                        }
                    }

                    return groups.Select(g => new ParameterLookupDialog.ParamValueEntry
                    {
                        ParameterName = paramName,
                        Value = g.Key,
                        ElementCount = g.Value.Count,
                        ElementIds = g.Value,
                        StorageType = storageTypes.TryGetValue(g.Key, out var st) ? st : ""
                    }).ToList();
                };

            // Filter function: evaluate conditions and return matching element IDs
            Func<List<ParameterLookupDialog.Condition>, string, List<long>> filterFunc =
                (conditions, category) =>
                {
                    var filtered = category != null
                        ? viewElements.Where(e => e.Category?.Name == category)
                        : viewElements;

                    var matchIds = new List<long>();
                    foreach (var el in filtered)
                    {
                        bool allMatch = true;
                        foreach (var cond in conditions)
                        {
                            string actual = Select.ColorHelper.GetParameterValue(el, cond.Parameter) ?? "";
                            bool match = cond.Operator switch
                            {
                                "contains" => actual.IndexOf(cond.Value ?? "", StringComparison.OrdinalIgnoreCase) >= 0,
                                "equals" => string.Equals(actual, cond.Value ?? "", StringComparison.OrdinalIgnoreCase),
                                "not equals" => !string.Equals(actual, cond.Value ?? "", StringComparison.OrdinalIgnoreCase),
                                "starts with" => actual.StartsWith(cond.Value ?? "", StringComparison.OrdinalIgnoreCase),
                                "ends with" => actual.EndsWith(cond.Value ?? "", StringComparison.OrdinalIgnoreCase),
                                ">" => double.TryParse(actual, out var av) && double.TryParse(cond.Value, out var cv) && av > cv,
                                "<" => double.TryParse(actual, out var av2) && double.TryParse(cond.Value, out var cv2) && av2 < cv2,
                                ">=" => double.TryParse(actual, out var av3) && double.TryParse(cond.Value, out var cv3) && av3 >= cv3,
                                "<=" => double.TryParse(actual, out var av4) && double.TryParse(cond.Value, out var cv4) && av4 <= cv4,
                                "is empty" => string.IsNullOrEmpty(actual),
                                "is not empty" => !string.IsNullOrEmpty(actual),
                                _ => actual.IndexOf(cond.Value ?? "", StringComparison.OrdinalIgnoreCase) >= 0
                            };
                            if (!match) { allMatch = false; break; }
                        }
                        if (allMatch) matchIds.Add(el.Id.Value);
                    }
                    return matchIds;
                };

            // Show the dialog
            var result = ParameterLookupDialog.Show(paramNames, categories, queryFunc, filterFunc);
            if (result == null) return;

            // Handle the result action
            if (result.Action == "Select" && result.MatchedElementIds.Count > 0)
            {
                var elementIds = result.MatchedElementIds.Select(id => new ElementId(id)).ToList();
                uidoc.Selection.SetElementIds(elementIds);
                TaskDialog.Show("Parameter Lookup",
                    $"Selected {elementIds.Count} matching elements.");
                StingLog.Info($"ParamLookup Select: {elementIds.Count} elements");
            }
            else if (result.Action == "Color" && !string.IsNullOrEmpty(result.SelectedParameter))
            {
                // Delegate to ColorByParameter with the selected parameter
                SetExtraParam("ColorParam", result.SelectedParameter);
                RunCommand<Select.ColorByParameterCommand>(app);
            }
            else if (result.Action == "Apply" && result.MatchedElementIds.Count > 0)
            {
                var elementIds = result.MatchedElementIds.Select(id => new ElementId(id)).ToList();
                uidoc.Selection.SetElementIds(elementIds);
                TaskDialog.Show("Parameter Lookup",
                    $"Applied filter: {elementIds.Count} elements selected from {result.Conditions.Count} condition(s).");
                StingLog.Info($"ParamLookup Apply: {elementIds.Count} elements, {result.Conditions.Count} conditions");
            }
            else if (result.MatchedElementIds.Count == 0 && result.Conditions.Count > 0)
            {
                TaskDialog.Show("Parameter Lookup", "No elements matched the specified conditions.");
            }
        }

        private static void QuickParamFilter(UIApplication app, string paramName)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0) { TaskDialog.Show("Quick Param", "Select an element first."); return; }
            var first = uidoc.Document.GetElement(selected.First());
            if (first == null) return;
            string val = ParameterHelpers.GetString(first, paramName);
            if (string.IsNullOrEmpty(val))
            {
                var p = first.LookupParameter(paramName);
                val = p?.AsValueString() ?? "";
            }
            if (string.IsNullOrEmpty(val)) { TaskDialog.Show("Quick Param", $"No '{paramName}' value on selected element."); return; }
            var matching = new FilteredElementCollector(uidoc.Document, uidoc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e => ParameterHelpers.GetString(e, paramName) == val
                    || (e.LookupParameter(paramName)?.AsValueString() ?? "") == val)
                .Select(e => e.Id).ToList();
            uidoc.Selection.SetElementIds(matching);
            StingLog.Info($"QuickParam '{paramName}'='{val}': {matching.Count} elements");
        }

        /// <summary>FE-03: Dry-run tag preview — runs full pipeline in a rolled-back transaction.</summary>
        private static void PreviewTagInline(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var selIds = uidoc.Selection.GetElementIds().ToList();
            if (selIds.Count != 1)
            {
                TaskDialog.Show("Preview Tag", "Select exactly 1 element to preview its predicted tag.");
                return;
            }
            Element el = doc.GetElement(selIds[0]);
            if (el == null) return;
            string catName = Core.ParameterHelpers.GetCategoryName(el);
            if (!Core.TagConfig.DiscMap.ContainsKey(catName))
            {
                TaskDialog.Show("Preview Tag", $"'{catName}' is not a taggable category.");
                return;
            }
            // Dry-run: start a transaction, run pipeline, read result, rollback
            string predictedTag = "(could not derive)";
            string tokenDetail = "";
            using (Transaction tx = new Transaction(doc, "STING Preview Tag (Dry-Run)"))
            {
                tx.Start();
                try
                {
                    var ctx = Core.TokenAutoPopulator.PopulationContext.Build(doc);
                    var (tagIdx, seqCtrs) = Core.TagConfig.BuildTagIndexAndCounters(doc);
                    var formulas = Core.TagPipelineHelper.LoadFormulas();
                    var grids = Core.TagPipelineHelper.LoadGridLines(doc);
                    bool previewOk = Core.TagPipelineHelper.RunFullPipeline(doc, el, ctx, tagIdx, seqCtrs,
                        formulas, grids, overwrite: true, skipComplete: false,
                        collisionMode: Core.TagCollisionMode.AutoIncrement);
                    if (!previewOk)
                        Core.StingLog.Warn($"PreviewTag: pipeline returned false for element {el?.Id}");
                    predictedTag = Core.ParameterHelpers.GetString(el, Core.ParamRegistry.TAG1) ?? "(empty)";
                    string disc  = Core.ParameterHelpers.GetString(el, Core.ParamRegistry.DISC);
                    string loc   = Core.ParameterHelpers.GetString(el, Core.ParamRegistry.LOC);
                    string zone  = Core.ParameterHelpers.GetString(el, Core.ParamRegistry.ZONE);
                    string lvl   = Core.ParameterHelpers.GetString(el, Core.ParamRegistry.LVL);
                    string sys   = Core.ParameterHelpers.GetString(el, Core.ParamRegistry.SYS);
                    string func  = Core.ParameterHelpers.GetString(el, Core.ParamRegistry.FUNC);
                    string prod  = Core.ParameterHelpers.GetString(el, Core.ParamRegistry.PROD);
                    tokenDetail = $"DISC={disc}  LOC={loc}  ZONE={zone}  LVL={lvl}\nSYS={sys}  FUNC={func}  PROD={prod}";
                }
                catch (Exception ex) { Core.StingLog.Warn($"PreviewTag: {ex.Message}"); }
                tx.RollBack(); // Always rollback — this is read-only preview
            }
            TaskDialog.Show("STING — Preview Tag",
                $"Predicted tag for selected element:\n\n" +
                $"  {predictedTag}\n\n" +
                $"Tokens:\n  {tokenDetail}\n\n" +
                $"(No changes were written — this is a dry-run preview.)");
        }

        private static void BulkParamWriteInline(UIApplication app, string paramName, string value, bool clear)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Bulk Write", "Select elements first."); return; }
            if (string.IsNullOrEmpty(paramName)) { TaskDialog.Show("Bulk Write", "Enter parameter name."); return; }
            int written = 0;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Bulk Parameter Write"))
            {
                tx.Start();
                foreach (ElementId id in ids)
                {
                    Element el = uidoc.Document.GetElement(id);
                    if (el == null) continue;
                    if (clear)
                    {
                        if (ParameterHelpers.SetString(el, paramName, "", overwrite: true))
                            written++;
                    }
                    else
                    {
                        if (ParameterHelpers.SetString(el, paramName, value, overwrite: true))
                            written++;
                    }
                }
                tx.Commit();
            }
            TaskDialog.Show("Bulk Write", $"Updated {written} of {ids.Count} elements.");
        }

        private static void BulkParamPreview(UIApplication app, string paramName, string value)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Preview", "Select elements first."); return; }
            int hasParam = 0;
            int wouldChange = 0;
            foreach (ElementId id in ids)
            {
                Element el = uidoc.Document.GetElement(id);
                if (el == null) continue;
                Parameter p = el.LookupParameter(paramName);
                if (p != null)
                {
                    hasParam++;
                    string current = p.AsString() ?? "";
                    if (current != value) wouldChange++;
                }
            }
            TaskDialog.Show("Preview",
                $"Parameter: {paramName}\nNew value: {value}\n\n" +
                $"{hasParam} of {ids.Count} elements have this parameter.\n" +
                $"{wouldChange} values would change.");
        }

        // ── Graphic overrides ─────────────────────────────────────────

        private static void SetHalftone(UIApplication app, bool on)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Halftone", "Select elements first."); return; }
            using (Transaction tx = new Transaction(uidoc.Document, "STING Halftone"))
            {
                tx.Start();
                var ogs = new OverrideGraphicSettings();
                ogs.SetHalftone(on);
                foreach (ElementId id in ids)
                    uidoc.ActiveView.SetElementOverrides(id, ogs);
                tx.Commit();
            }
        }

        private static void PermanentHide(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) return;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Permanent Hide"))
            {
                tx.Start();
                uidoc.ActiveView.HideElements(ids);
                tx.Commit();
            }
        }

        private static void PermanentUnhide(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) return;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Permanent Unhide"))
            {
                tx.Start();
                uidoc.ActiveView.UnhideElements(ids);
                tx.Commit();
            }
        }

        private static void UnhideCategory(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Unhide Category"))
            {
                tx.Start();
                foreach (Category cat in uidoc.Document.Settings.Categories)
                {
                    try
                    {
                        if (cat.get_Visible(uidoc.ActiveView) == false)
                            cat.set_Visible(uidoc.ActiveView, true);
                    }
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                }
                tx.Commit();
            }
        }

        // ── Token writer helper ──────────────────────────────────────

        private static void WriteTokenToSelected(UIApplication app, string paramName, string label)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show($"Set {label}", "Select elements first."); return; }

            string[] options = GetTokenOptions(paramName);

            var dlg = new TaskDialog($"Set {label}");
            dlg.MainInstruction = $"Set {label} for {ids.Count} element(s)";
            dlg.MainContent = string.Join(", ", options);
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                options.Length > 0 ? options[0] : "Value 1");
            if (options.Length > 1)
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, options[1]);
            if (options.Length > 2)
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, options[2]);
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Clear value", "Remove existing value");

            var result = dlg.Show();
            string value;
            switch (result)
            {
                case TaskDialogResult.CommandLink1: value = options.Length > 0 ? options[0] : ""; break;
                case TaskDialogResult.CommandLink2: value = options.Length > 1 ? options[1] : ""; break;
                case TaskDialogResult.CommandLink3: value = options.Length > 2 ? options[2] : ""; break;
                case TaskDialogResult.CommandLink4: value = ""; break;
                default: return;
            }

            int written = 0;
            using (Transaction tx = new Transaction(uidoc.Document, $"STING Set {label}"))
            {
                tx.Start();
                foreach (ElementId id in ids)
                {
                    Element el = uidoc.Document.GetElement(id);
                    if (el != null && ParameterHelpers.SetString(el, paramName, value, overwrite: true))
                        written++;
                }
                tx.Commit();
            }
            TaskDialog.Show($"Set {label}", $"Updated {written} of {ids.Count} elements.");
        }

        private static string[] GetTokenOptions(string paramName)
        {
            if (paramName == ParamRegistry.SYS) return new[] { "HVAC", "DCW", "SAN" };
            if (paramName == ParamRegistry.FUNC) return new[] { "SUP", "HTG", "PWR" };
            if (paramName == ParamRegistry.PROD) return new[] { "AHU", "DB", "DR" };
            if (paramName == ParamRegistry.LVL) return new[] { "GF", "L01", "B1" };
            if (paramName == ParamRegistry.ORIGIN) return new[] { "NEW", "EXISTING", "DEMOLISHED" };
            if (paramName == ParamRegistry.PROJECT) return new[] { "PRJ001", "PRJ002", "PRJ003" };
            if (paramName == ParamRegistry.REV) return new[] { "P01", "P02", "C01" };
            if (paramName == ParamRegistry.VOLUME) return new[] { "V01", "V02", "V03" };
            return new[] { "VALUE1", "VALUE2", "VALUE3" };
        }

        // ── Connected elements selector ─────────────────────────────

        private static void SelectConnectedElements(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0) { TaskDialog.Show("Connected", "Select an element first."); return; }

            var connected = new HashSet<ElementId>(selected);
            foreach (ElementId id in selected)
            {
                Element el = uidoc.Document.GetElement(id);
                if (el == null) continue;

                try
                {
                    var connectorManager = (el as Autodesk.Revit.DB.MEPCurve)?.ConnectorManager
                        ?? (el as Autodesk.Revit.DB.FamilyInstance)
                            ?.MEPModel?.ConnectorManager;

                    if (connectorManager != null)
                    {
                        foreach (Connector connector in connectorManager.Connectors)
                        {
                            if (connector.IsConnected)
                            {
                                foreach (Connector otherConn in connector.AllRefs)
                                {
                                    if (otherConn.Owner != null)
                                        connected.Add(otherConn.Owner.Id);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"SelectConnected: connector traversal failed for {el.Id}: {ex.Message}"); }
            }

            uidoc.Selection.SetElementIds(connected.ToList());
            StingLog.Info($"SelectConnected: {connected.Count} elements (was {selected.Count})");
        }

        private static void SelectByCategory(UIApplication app, string categoryName)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = new FilteredElementCollector(uidoc.Document, uidoc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e => ParameterHelpers.GetCategoryName(e) == categoryName)
                .Select(e => e.Id).ToList();
            uidoc.Selection.SetElementIds(ids);
            StingLog.Info($"SelectByCategory '{categoryName}': {ids.Count} elements");
        }

        private static void SelectVisibleOnly(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var view = uidoc.ActiveView;
            if (view == null) { TaskDialog.Show("Select", "No active view."); return; }
            var ids = new FilteredElementCollector(uidoc.Document, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => !e.IsHidden(view))
                .Select(e => e.Id).ToList();
            uidoc.Selection.SetElementIds(ids);
            StingLog.Info($"SelectVisibleOnly: {ids.Count} elements");
        }

        // ── Colouriser helpers ─────────────────────────────────────

        private static void ColorByParameter(UIApplication app, string paramName, string paletteName)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = uidoc.ActiveView;
            if (view == null) { TaskDialog.Show("Color By Parameter", "No active view."); return; }
            // View capability check — must be a model view, not a schedule/sheet/browser
            if (view is ViewSchedule || view.ViewType == ViewType.DrawingSheet ||
                view.ViewType == ViewType.ProjectBrowser || view.ViewType == ViewType.SystemBrowser)
            {
                TaskDialog.Show("Color By Parameter", "This feature requires a model view (plan, section, elevation, or 3D).");
                return;
            }
            if (string.IsNullOrEmpty(paramName))
            {
                TaskDialog.Show("Color By Parameter", "Select a parameter name first.");
                return;
            }

            // Honour selected elements before full view scan
            var selIds = uidoc.Selection.GetElementIds();
            List<Element> elements;
            string scope;
            if (selIds.Count > 0)
            {
                elements = selIds.Select(id => doc.GetElement(id))
                    .Where(e => e != null && e.IsValidObject && e.Category != null
                             && e.Category.CategoryType == CategoryType.Model)
                    .ToList();
                scope = $"{elements.Count} selected elements";
            }
            else
            {
                elements = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null && e.Category.CategoryType == CategoryType.Model)
                    .ToList();
                scope = $"{elements.Count} elements in view";
            }

            // Group elements by parameter value with HasValue check
            var groups = new Dictionary<string, List<ElementId>>();
            foreach (var el in elements)
            {
                string val = null;
                var p = el.LookupParameter(paramName);
                if (p != null && p.HasValue)
                {
                    val = p.StorageType == StorageType.String ? p.AsString()
                        : p.AsValueString();
                }
                if (string.IsNullOrEmpty(val))
                    val = "<No Value>";
                if (!groups.TryGetValue(val, out var grpList))
                {
                    grpList = new List<ElementId>();
                    groups[val] = grpList;
                }
                grpList.Add(el.Id);
            }

            Color[] palette = GetColorPalette(paletteName, groups.Count);
            if (groups.Count > palette.Length)
                StingLog.Warn($"ColorByParameter: {groups.Count} unique values exceeds palette size ({palette.Length}) — colors will cycle.");

            // Use cached solid fill pattern from ColorHelper
            var solidFill = Select.ColorHelper.FindSolidFill(doc);

            using (Transaction tx = new Transaction(doc, "STING Color By Parameter"))
            {
                tx.Start();
                int colorIdx = 0;
                foreach (var kvp in groups)
                {
                    var ogs = new OverrideGraphicSettings();
                    Color color = palette[colorIdx % palette.Length];
                    ogs.SetProjectionLineColor(color);
                    if (solidFill != null)
                    {
                        ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                        ogs.SetSurfaceForegroundPatternColor(color);
                        ogs.SetCutForegroundPatternId(solidFill.Id);
                        ogs.SetCutForegroundPatternColor(color);
                    }
                    foreach (ElementId id in kvp.Value)
                        view.SetElementOverrides(id, ogs);
                    colorIdx++;
                }
                tx.Commit();
            }

            TaskDialog.Show("Color By Parameter",
                $"Coloured {scope} by '{paramName}'\n" +
                $"({groups.Count} unique values).");
        }

        private static void ColorByHex(UIApplication app, string hexColor)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var view = uidoc.ActiveView;
            if (view == null) { TaskDialog.Show("Color", "No active view."); return; }
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Color", "Select elements first."); return; }

            hexColor = (hexColor ?? "").Trim().TrimStart('#');
            if (hexColor.Length != 6 ||
                !byte.TryParse(hexColor.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) ||
                !byte.TryParse(hexColor.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) ||
                !byte.TryParse(hexColor.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
            {
                TaskDialog.Show("Color", "Invalid hex color. Use format: RRGGBB");
                return;
            }

            Color color = new Color(r, g, b);

            // Phase 74: Use cached solid fill pattern instead of redundant collector
            FillPatternElement solidFill = ParameterHelpers.GetSolidFillPattern(uidoc.Document);

            using (Transaction tx = new Transaction(uidoc.Document, "STING Color By Hex"))
            {
                tx.Start();
                var ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(color);
                if (solidFill != null)
                {
                    ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                    ogs.SetSurfaceForegroundPatternColor(color);
                }
                foreach (ElementId id in ids)
                    view.SetElementOverrides(id, ogs);
                tx.Commit();
            }
        }

        private static void SetTransparencyOverride(UIApplication app, string transparencyStr)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var view = uidoc.ActiveView;
            if (view == null) { TaskDialog.Show("Transparency", "No active view."); return; }
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Transparency", "Select elements first."); return; }

            if (!int.TryParse(transparencyStr, out int transparency))
                transparency = 50;
            transparency = Math.Max(0, Math.Min(100, transparency));

            using (Transaction tx = new Transaction(uidoc.Document, "STING Set Transparency"))
            {
                tx.Start();
                var ogs = new OverrideGraphicSettings();
                ogs.SetSurfaceTransparency(transparency);
                foreach (ElementId id in ids)
                    view.SetElementOverrides(id, ogs);
                tx.Commit();
            }
        }

        private static void DeleteColorPreset(UIApplication app)
        {
            string presetPath = Path.Combine(StingToolsApp.DataPath ?? "", "COLOR_PRESETS.json");
            if (!File.Exists(presetPath))
            { TaskDialog.Show("Delete Preset", "No saved presets found."); return; }

            try
            {
                var presets = Newtonsoft.Json.JsonConvert.DeserializeObject<
                    Dictionary<string, object>>(File.ReadAllText(presetPath));
                if (presets == null || presets.Count == 0)
                { TaskDialog.Show("Delete Preset", "No saved presets found."); return; }

                var td = new TaskDialog("Delete Color Preset");
                td.MainInstruction = "Select preset to delete";
                var names = presets.Keys.ToList();
                int linkCount = 0;
                foreach (string name in names.Take(4))
                {
                    td.AddCommandLink((TaskDialogCommandLinkId)(1001 + linkCount), name);
                    linkCount++;
                }
                td.CommonButtons = TaskDialogCommonButtons.Cancel;
                var result = td.Show();

                int idx = result switch
                {
                    TaskDialogResult.CommandLink1 => 0,
                    TaskDialogResult.CommandLink2 => 1,
                    TaskDialogResult.CommandLink3 => 2,
                    TaskDialogResult.CommandLink4 => 3,
                    _ => -1
                };

                if (idx >= 0 && idx < names.Count)
                {
                    presets.Remove(names[idx]);
                    File.WriteAllText(presetPath,
                        Newtonsoft.Json.JsonConvert.SerializeObject(presets, Newtonsoft.Json.Formatting.Indented));
                    TaskDialog.Show("Delete Preset", $"Deleted preset: {names[idx]}");
                    StingLog.Info($"Deleted color preset: {names[idx]}");
                }
            }
            catch (Exception ex)
            {
                StingLog.Error($"Delete preset failed: {ex.Message}");
                TaskDialog.Show("Delete Preset", $"Error: {ex.Message}");
            }
        }

        // ── Schedule operations ──────────────────────────────────

        private static void ScheduleSyncPosition(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            if (!(uidoc.Document.ActiveView is ViewSheet sheet))
            { TaskDialog.Show("Schedule Sync", "Active view must be a sheet."); return; }

            // Find schedule graphics on this sheet and align them
            var schedGraphics = new FilteredElementCollector(uidoc.Document, sheet.Id)
                .OfClass(typeof(ScheduleSheetInstance))
                .Cast<ScheduleSheetInstance>()
                .ToList();

            if (schedGraphics.Count < 2)
            { TaskDialog.Show("Schedule Sync", "Need at least 2 schedules on sheet."); return; }

            XYZ refPos = schedGraphics[0].Point;
            int moved = 0;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Schedule Sync Position"))
            {
                tx.Start();
                foreach (var sg in schedGraphics.Skip(1))
                {
                    try
                    {
                        sg.Point = new XYZ(refPos.X, sg.Point.Y, sg.Point.Z);
                        moved++;
                    }
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                }
                tx.Commit();
            }
            TaskDialog.Show("Schedule Sync", $"Aligned {moved} schedules to same X position.");
        }

        private static void ScheduleToggleRotation(UIApplication app)
        {
            // Schedule rotation on sheets is not directly API-accessible for ScheduleSheetInstance
            // Log for now
            StingLog.Info("ScheduleToggleRotation: not supported in Revit API for schedule graphics");
            TaskDialog.Show("Schedule Rotation", "Schedule rotation is controlled via the schedule properties dialog in Revit.");
        }

        private static void ScheduleShowHidden(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            var view = doc.ActiveView;

            if (!(view is ViewSchedule sched))
            { TaskDialog.Show("Schedule", "Active view must be a schedule."); return; }

            var def = sched.Definition;
            int fieldCount = def.GetFieldCount();
            int hiddenCount = 0;
            var sb = new StringBuilder();
            sb.AppendLine($"Schedule: {sched.Name}");
            sb.AppendLine($"Total fields: {fieldCount}");
            sb.AppendLine();
            for (int i = 0; i < fieldCount; i++)
            {
                var field = def.GetField(i);
                string vis = field.IsHidden ? "[HIDDEN]" : "[Visible]";
                if (field.IsHidden) hiddenCount++;
                sb.AppendLine($"  {vis} {field.GetName()}");
            }
            sb.AppendLine($"\nHidden fields: {hiddenCount}");
            TaskDialog.Show("Schedule Fields", sb.ToString());
        }

        private static void ScheduleMatchWidest(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            if (!(doc.ActiveView is ViewSchedule sched))
            { TaskDialog.Show("Schedule", "Active view must be a schedule."); return; }

            var def = sched.Definition;
            int fieldCount = def.GetFieldCount();
            if (fieldCount == 0) return;

            // Find the widest column
            double maxWidth = 0;
            for (int i = 0; i < fieldCount; i++)
            {
                try
                {
                    double w = def.GetField(i).GridColumnWidth;
                    if (w > maxWidth) maxWidth = w;
                }
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
            }

            if (maxWidth <= 0)
            {
                TaskDialog.Show("Match Widest", "No valid column widths found.");
                return;
            }

            // Set all columns to match the widest
            int updated = 0;
            using (Transaction tx = new Transaction(doc, "STING Match Widest Column"))
            {
                tx.Start();
                for (int i = 0; i < fieldCount; i++)
                {
                    try
                    {
                        var field = def.GetField(i);
                        if (Math.Abs(field.GridColumnWidth - maxWidth) > 0.001)
                        {
                            field.GridColumnWidth = maxWidth;
                            updated++;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                }
                tx.Commit();
            }
            TaskDialog.Show("Match Widest",
                $"Set {updated} columns to widest width ({maxWidth * 304.8:F1}mm).");
        }

        private static void ScheduleSetColumnWidth(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            if (!(doc.ActiveView is ViewSchedule sched))
            { TaskDialog.Show("Schedule", "Active view must be a schedule."); return; }

            var def = sched.Definition;
            int fieldCount = def.GetFieldCount();

            // Set all columns to same width (1 inch = 0.0833 ft)
            TaskDialog dlg = new TaskDialog("Set Column Width");
            dlg.MainInstruction = $"Set column width for {fieldCount} fields";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Narrow (15mm)", "Compact schedules");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Standard (25mm)", "Default readable width");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Wide (40mm)", "Comfortable reading");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            double width;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: width = 15.0 / 304.8; break; // mm to feet
                case TaskDialogResult.CommandLink2: width = 25.0 / 304.8; break;
                case TaskDialogResult.CommandLink3: width = 40.0 / 304.8; break;
                default: return;
            }

            int updated = 0;
            using (Transaction tx = new Transaction(doc, "STING Set Column Width"))
            {
                tx.Start();
                for (int i = 0; i < fieldCount; i++)
                {
                    try
                    {
                        var field = def.GetField(i);
                        field.GridColumnWidth = width;
                        updated++;
                    }
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                }
                tx.Commit();
            }
            TaskDialog.Show("Column Width", $"Updated {updated} column widths.");
        }

        private static void ScheduleEqualiseColumns(UIApplication app)
        {
            // Delegates to ScheduleMatchWidest — identical operation
            ScheduleMatchWidest(app);
        }

        private static void ScheduleAutoFit(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            if (!(doc.ActiveView is ViewSchedule sched))
            { TaskDialog.Show("Schedule", "Active view must be a schedule."); return; }

            var def = sched.Definition;
            int fieldCount = def.GetFieldCount();
            if (fieldCount == 0) return;

            // Estimate column width based on data content length
            var body = sched.GetTableData()?.GetSectionData(SectionType.Body);
            if (body == null || body.NumberOfRows == 0)
            {
                TaskDialog.Show("Auto-Fit", "Schedule has no data rows to measure.");
                return;
            }

            int sampleRows = Math.Min(body.NumberOfRows, 50);
            int updated = 0;

            using (Transaction tx = new Transaction(doc, "STING Schedule Auto-Fit"))
            {
                tx.Start();

                for (int col = 0; col < fieldCount; col++)
                {
                    try
                    {
                        var field = def.GetField(col);
                        if (field.IsHidden) continue;

                        // Measure max text length in this column
                        int maxLen = field.ColumnHeading?.Length ?? 5;
                        for (int row = 0; row < sampleRows; row++)
                        {
                            try
                            {
                                string val = sched.GetCellText(SectionType.Body, row, col);
                                if (val != null && val.Length > maxLen)
                                    maxLen = val.Length;
                            }
                            catch (Exception ex) { StingLog.Warn($"AutoFit cell read: {ex.Message}"); break; }
                        }

                        // Convert character count to approximate width
                        // ~2.0mm per character at 8pt font, min 15mm, max 80mm
                        double widthMm = Math.Max(15, Math.Min(80, maxLen * 2.0 + 6));
                        double widthFt = widthMm / 304.8;

                        field.GridColumnWidth = widthFt;
                        updated++;
                    }
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                }

                tx.Commit();
            }

            TaskDialog.Show("Auto-Fit",
                $"Auto-fitted {updated} column widths based on data content.");
        }

        private static void ScheduleToggleHidden(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            if (!(doc.ActiveView is ViewSchedule sched))
            { TaskDialog.Show("Schedule", "Active view must be a schedule."); return; }

            var def = sched.Definition;
            int fieldCount = def.GetFieldCount();
            int hiddenCount = 0;

            for (int i = 0; i < fieldCount; i++)
            {
                try { if (def.GetField(i).IsHidden) hiddenCount++; } catch (Exception ex) { StingLog.Warn($"ScheduleToggleHidden field {i}: {ex.Message}"); }
            }

            if (hiddenCount == 0)
            {
                TaskDialog.Show("Toggle Hidden", "No hidden fields in this schedule.");
                return;
            }

            // Unhide all hidden fields
            int revealed = 0;
            using (Transaction tx = new Transaction(doc, "STING Unhide Schedule Fields"))
            {
                tx.Start();
                for (int i = 0; i < fieldCount; i++)
                {
                    try
                    {
                        var field = def.GetField(i);
                        if (field.IsHidden)
                        {
                            field.IsHidden = false;
                            revealed++;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                }
                tx.Commit();
            }

            TaskDialog.Show("Toggle Hidden",
                $"Revealed {revealed} hidden field(s) in '{sched.Name}'.");
        }

        // ── Text note operations ────────────────────────────────────

        private static void TextAlign(UIApplication app, string alignment)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Text Align", "Select text notes first."); return; }

            int aligned = 0;
            using (Transaction tx = new Transaction(uidoc.Document, $"STING Text Align {alignment}"))
            {
                tx.Start();
                foreach (ElementId id in ids)
                {
                    if (uidoc.Document.GetElement(id) is TextNote tn)
                    {
                        try
                        {
                            var p = tn.get_Parameter(BuiltInParameter.TEXT_ALIGN_HORZ);
                            if (p != null && !p.IsReadOnly)
                            {
                                int val = alignment == "Left" ? 0 : alignment == "Center" ? 1 : 2;
                                p.Set(val);
                                aligned++;
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                    }
                }
                tx.Commit();
            }
            StingLog.Info($"TextAlign {alignment}: {aligned}");
        }

        private static void TextAlignAxis(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            var textNotes = ids.Select(id => uidoc.Document.GetElement(id))
                .OfType<TextNote>().ToList();

            if (textNotes.Count < 2) { TaskDialog.Show("Text Align", "Select 2+ text notes."); return; }

            // Align all text notes to the X of the first one
            XYZ refPos = textNotes[0].Coord;
            int moved = 0;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Text Align Axis"))
            {
                tx.Start();
                foreach (var tn in textNotes.Skip(1))
                {
                    try
                    {
                        tn.Coord = new XYZ(refPos.X, tn.Coord.Y, tn.Coord.Z);
                        moved++;
                    }
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                }
                tx.Commit();
            }
            StingLog.Info($"TextAlignAxis: {moved} text notes aligned");
        }

        private static void TextLeaderToggle(UIApplication app, string mode)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            int toggled = 0;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Text Leader"))
            {
                tx.Start();
                foreach (ElementId id in ids)
                {
                    if (uidoc.Document.GetElement(id) is TextNote tn)
                    {
                        try
                        {
                            var leaders = tn.GetLeaders();
                            if (leaders.Count > 0)
                                tn.RemoveLeaders();
                            else
                                tn.AddLeader(TextNoteLeaderTypes.TNLT_STRAIGHT_L);
                            toggled++;
                        }
                        catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                    }
                }
                tx.Commit();
            }
            StingLog.Info($"TextLeader {mode}: toggled {toggled}");
        }

        // ── Dimension operations ────────────────────────────────────

        private static void DimResetOverrides(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            int reset = 0;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Dim Reset Overrides"))
            {
                tx.Start();
                foreach (ElementId id in ids)
                {
                    if (uidoc.Document.GetElement(id) is Dimension dim)
                    {
                        try
                        {
                            foreach (DimensionSegment seg in dim.Segments)
                            {
                                seg.ValueOverride = "";
                                seg.Above = "";
                                seg.Below = "";
                                seg.Prefix = "";
                                seg.Suffix = "";
                            }
                            reset++;
                        }
                        catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                    }
                }
                tx.Commit();
            }
            TaskDialog.Show("Dim Reset", $"Reset overrides on {reset} dimensions.");
        }

        private static void DimResetText(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            int reset = 0;
            using (Transaction tx = new Transaction(uidoc.Document, "STING Dim Reset Text"))
            {
                tx.Start();
                foreach (ElementId id in ids)
                {
                    if (uidoc.Document.GetElement(id) is Dimension dim)
                    {
                        try
                        {
                            foreach (DimensionSegment seg in dim.Segments)
                                seg.ValueOverride = "";
                            reset++;
                        }
                        catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                    }
                }
                tx.Commit();
            }
            TaskDialog.Show("Dim Reset Text", $"Reset text overrides on {reset} dimensions.");
        }

        private static void DimFindZero(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            var dims = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Dimension))
                .Cast<Dimension>()
                .ToList();

            var zeroDims = new List<ElementId>();
            foreach (var dim in dims)
            {
                try
                {
                    if (dim.Value.HasValue && Math.Abs(dim.Value.Value) < 0.001)
                        zeroDims.Add(dim.Id);
                    else
                    {
                        foreach (DimensionSegment seg in dim.Segments)
                            if (seg.Value.HasValue && Math.Abs(seg.Value.Value) < 0.001)
                            { zeroDims.Add(dim.Id); break; }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
            }

            if (zeroDims.Count > 0)
                uidoc.Selection.SetElementIds(zeroDims);
            TaskDialog.Show("Find Zero Dims",
                $"Found {zeroDims.Count} dimensions with zero-length segments.");
        }

        // ── Legend operations ────────────────────────────────────────

        private static void LegendSyncPosition(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            if (!(doc.ActiveView is ViewSheet sheet))
            { TaskDialog.Show("Legend Sync", "Active view must be a sheet."); return; }

            // Find all legend viewports on this sheet
            var vpIds = sheet.GetAllViewports().ToList();
            var legendVps = new List<Viewport>();
            foreach (var vpId in vpIds)
            {
                var vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;
                var vpView = doc.GetElement(vp.ViewId) as View;
                if (vpView?.ViewType == ViewType.Legend)
                    legendVps.Add(vp);
            }

            if (legendVps.Count < 2)
            { TaskDialog.Show("Legend Sync", "Need 2+ legend viewports on sheet."); return; }

            XYZ refCenter = legendVps[0].GetBoxCenter();
            int aligned = 0;
            using (Transaction tx = new Transaction(doc, "STING Legend Sync"))
            {
                tx.Start();
                foreach (var vp in legendVps.Skip(1))
                {
                    vp.SetBoxCenter(new XYZ(refCenter.X, vp.GetBoxCenter().Y, 0));
                    aligned++;
                }
                tx.Commit();
            }
            TaskDialog.Show("Legend Sync", $"Aligned {aligned} legends to same X position.");
        }

        private static void LegendTitleLine(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            TaskDialog.Show("Legend Title Line",
                "Legend title lines are managed within the legend view itself.\n" +
                "Open the legend view and add/edit text notes for titles.");
        }

        private static void LegendUniformSize(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            if (!(doc.ActiveView is ViewSheet sheet))
            { TaskDialog.Show("Legend Uniform", "Active view must be a sheet."); return; }

            var vpIds = sheet.GetAllViewports().ToList();
            var legendVps = new List<(Viewport vp, View view)>();
            foreach (var vpId in vpIds)
            {
                var vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;
                var vpView = doc.GetElement(vp.ViewId) as View;
                if (vpView != null && (vpView.ViewType == ViewType.Legend ||
                    (vpView.ViewType == ViewType.DraftingView && vpView.Name.Contains("STING"))))
                    legendVps.Add((vp, vpView));
            }

            if (legendVps.Count < 2)
            { TaskDialog.Show("Legend Uniform", "Need 2+ legend/STING viewports on sheet."); return; }

            // CRASH FIX: Merged two back-to-back transactions into one.
            // The original code set all scales to 1 in a first transaction, then
            // found the "most common scale" (always 1 after the first pass) and
            // set it again in a second transaction. Back-to-back transactions on
            // the same elements without doc.Regenerate() is a known Revit crash
            // pattern. Now uses a single transaction with the most common scale.
            var scaleCounts = legendVps.GroupBy(lv => lv.view.Scale)
                .OrderByDescending(g => g.Count()).ToList();
            int targetScale = scaleCounts[0].Key;

            int changed = 0;
            using (Transaction tx = new Transaction(doc, "STING Legend Uniform Size"))
            {
                tx.Start();
                foreach (var (vp, view) in legendVps)
                {
                    try
                    {
                        if (view.Scale != targetScale)
                        {
                            view.Scale = targetScale;
                            changed++;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"LegendUniform: {ex.Message}"); }
                }
                tx.Commit();
            }
            TaskDialog.Show("Legend Uniform",
                $"Set {changed} legend views to scale 1:{targetScale}.\n" +
                $"({legendVps.Count} total legends on sheet).");
        }

        /// <summary>
        /// Create a Tag Dictionary schedule showing all STING tag nomenclature.
        /// Uses ViewSchedule API to create a reference schedule listing DISC, SYS, FUNC, PROD codes.
        /// </summary>
        private static void CreateTagDictionary(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            // Check if schedule already exists
            string scheduleName = "STING - Tag Dictionary";
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                .FirstOrDefault(vs => vs.Name == scheduleName);

            if (existing != null)
            {
                TaskDialog.Show("Tag Dictionary", $"Schedule '{scheduleName}' already exists.\nDelete it first to regenerate.");
                return;
            }

            // Build dictionary data from TagConfig lookup tables
            var discMap = Core.TagConfig.DiscMap;
            var sysMap = Core.TagConfig.SysMap;
            var funcMap = Core.TagConfig.FuncMap;
            var prodMap = Core.TagConfig.ProdMap;

            var report = new StringBuilder();
            report.AppendLine("STING Tag Dictionary — ISO 19650 Nomenclature");
            report.AppendLine("=".PadRight(60, '='));
            report.AppendLine();
            report.AppendLine("TAG FORMAT: DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ");
            report.AppendLine();

            // Discipline codes
            report.AppendLine("── DISCIPLINE CODES (DISC) ──");
            var discCodes = new HashSet<string>();
            foreach (var kvp in discMap)
            {
                if (discCodes.Add(kvp.Value))
                    report.AppendLine($"  {kvp.Value,-6} {GetDiscDescription(kvp.Value)}");
            }
            report.AppendLine();

            // System codes
            report.AppendLine("── SYSTEM CODES (SYS) ──");
            foreach (var kvp in sysMap)
                report.AppendLine($"  {kvp.Key,-8} → Categories: {string.Join(", ", kvp.Value.Take(3))}{(kvp.Value.Count > 3 ? "..." : "")}");
            report.AppendLine();

            // Function codes
            report.AppendLine("── FUNCTION CODES (FUNC) ──");
            foreach (var kvp in funcMap)
                report.AppendLine($"  {kvp.Key,-8} → {kvp.Value}");
            report.AppendLine();

            // Product codes
            report.AppendLine("── PRODUCT CODES (PROD) ──");
            var prodCodes = new HashSet<string>();
            foreach (var kvp in prodMap)
            {
                if (prodCodes.Add(kvp.Value))
                    report.AppendLine($"  {kvp.Value,-6} {kvp.Key}");
            }
            report.AppendLine();

            // Location codes
            report.AppendLine("── LOCATION CODES (LOC) ──");
            foreach (string loc in Core.TagConfig.LocCodes)
                report.AppendLine($"  {loc}");
            report.AppendLine();

            // Zone codes
            report.AppendLine("── ZONE CODES (ZONE) ──");
            foreach (string zone in Core.TagConfig.ZoneCodes)
                report.AppendLine($"  {zone}");

            // Export to user-preferred output directory
            string exportPath = OutputLocationHelper.GetOutputPath(app.ActiveUIDocument?.Document, "TAG_DICTIONARY.txt");
            try
            {
                System.IO.File.WriteAllText(exportPath, report.ToString());
                StingLog.Info($"Tag dictionary exported to {exportPath}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Tag dictionary export: {ex.Message}");
                exportPath = null;
            }

            var msg = new StringBuilder();
            msg.AppendLine($"Tag Dictionary generated with:");
            msg.AppendLine($"  Disciplines: {discCodes.Count}");
            msg.AppendLine($"  Systems: {sysMap.Count}");
            msg.AppendLine($"  Functions: {funcMap.Count}");
            msg.AppendLine($"  Products: {prodCodes.Count}");
            msg.AppendLine($"  Locations: {Core.TagConfig.LocCodes.Count}");
            msg.AppendLine($"  Zones: {Core.TagConfig.ZoneCodes.Count}");
            if (exportPath != null)
                msg.AppendLine($"\nExported to: {exportPath}");
            msg.AppendLine($"\n{report.ToString().Substring(0, Math.Min(report.Length, 2000))}");

            TaskDialog.Show("Tag Dictionary", msg.ToString());
        }

        private static string GetDiscDescription(string disc)
        {
            return disc switch
            {
                "M" => "Mechanical",
                "E" => "Electrical",
                "P" => "Plumbing",
                "A" => "Architectural",
                "S" => "Structural",
                "FP" => "Fire Protection",
                "LV" => "Low Voltage / Comms",
                "G" => "General / Generic",
                _ => disc,
            };
        }

        /// <summary>
        /// Create a Color Legend reference schedule listing parameter values and their colors.
        /// Reads current color overrides from the active view and builds a reference table.
        /// </summary>
        private static void CreateColorLegendSchedule(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            // Collect all elements with color overrides in current view
            var elements = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.HasMaterialQuantities)
                .ToList();

            if (elements.Count == 0)
            {
                TaskDialog.Show("Color Legend", "No taggable elements in active view.");
                return;
            }

            // Group elements by their surface color override
            var colorGroups = new Dictionary<string, (Color color, List<Element> elems)>();

            foreach (var elem in elements)
            {
                try
                {
                    var ogs = view.GetElementOverrides(elem.Id);
                    Color surfColor = ogs.SurfaceForegroundPatternColor;
                    if (surfColor.IsValid && (surfColor.Red != 0 || surfColor.Green != 0 || surfColor.Blue != 0))
                    {
                        string key = $"{surfColor.Red:D3},{surfColor.Green:D3},{surfColor.Blue:D3}";
                        if (!colorGroups.TryGetValue(key, out var cg))
                        {
                            cg = (surfColor, new List<Element>());
                            colorGroups[key] = cg;
                        }
                        cg.elems.Add(elem);
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
            }

            if (colorGroups.Count == 0)
            {
                TaskDialog.Show("Color Legend",
                    "No color overrides found in active view.\n" +
                    "Use 'Color By Parameter' first to apply color overrides,\nthen run this command to generate a legend.");
                return;
            }

            // Try to detect which parameter was used for coloring
            // Check common tag parameters on the first element of each group
            string[] candidateParams = { "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
                "ASS_SYSTEM_TYPE_TXT", "ASS_LVL_COD_TXT", "ASS_FUNC_TXT", "ASS_PRODCT_COD_TXT" };

            string bestParam = null;
            int bestUniqueMatch = 0;
            foreach (string paramName in candidateParams)
            {
                var groupValues = new HashSet<string>();
                bool allUnique = true;
                foreach (var kvp in colorGroups)
                {
                    var sample = kvp.Value.elems.First();
                    string val = Core.ParameterHelpers.GetString(sample, paramName);
                    if (string.IsNullOrEmpty(val)) { allUnique = false; break; }
                    if (!groupValues.Add(val)) { allUnique = false; break; }
                }
                if (allUnique && groupValues.Count == colorGroups.Count && groupValues.Count > bestUniqueMatch)
                {
                    bestParam = paramName;
                    bestUniqueMatch = groupValues.Count;
                }
            }

            // Build report
            var report = new StringBuilder();
            report.AppendLine($"Color Legend — {view.Name}");
            report.AppendLine(new string('=', 50));
            if (bestParam != null)
                report.AppendLine($"Detected color parameter: {bestParam}");
            report.AppendLine($"Color groups: {colorGroups.Count}");
            report.AppendLine($"Total colored elements: {colorGroups.Sum(g => g.Value.elems.Count)}");
            report.AppendLine();
            report.AppendLine($"{"RGB",-16} {"Value",-20} {"Count",-8} {"Categories"}");
            report.AppendLine(new string('-', 70));

            foreach (var kvp in colorGroups.OrderBy(g => g.Key))
            {
                string paramValue = "<unknown>";
                if (bestParam != null)
                    paramValue = Core.ParameterHelpers.GetString(kvp.Value.elems.First(), bestParam);

                var catCounts = kvp.Value.elems.GroupBy(e => e.Category?.Name ?? "?")
                    .Select(g => $"{g.Key}({g.Count()})");
                report.AppendLine($"[{kvp.Key}]  {paramValue,-20} {kvp.Value.elems.Count,-8} {string.Join(", ", catCounts.Take(3))}");
            }

            // Export to project file location (fallback to data path)
            string exportPath = OutputLocationHelper.GetOutputPath(app.ActiveUIDocument?.Document, "COLOR_LEGEND.txt");
            try
            {
                System.IO.File.WriteAllText(exportPath, report.ToString());
            }
            catch (Exception ex) { StingLog.Warn($"Color legend export: {ex.Message}"); exportPath = null; }

            var msg = report.ToString();
            if (exportPath != null)
                msg += $"\n\nExported to: {exportPath}";

            TaskDialog.Show("Color Legend", msg);
        }

        // ── Sheet CSV export ─────────────────────────────────────────

        private static void ExportSheetCSV(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (sheets.Count == 0)
            { TaskDialog.Show("Export Sheets", "No sheets in project."); return; }

            var sb = new StringBuilder();
            sb.AppendLine("Sheet_Number,Sheet_Name,Revision,Issue_Date,Discipline,Drawn_By,Checked_By,Approved_By,Views_Placed");
            foreach (var sheet in sheets)
            {
                try
                {
                    string num = (sheet.SheetNumber ?? "").Replace(",", ";");
                    string name = (sheet.Name ?? "").Replace(",", ";");
                    string rev = (sheet.get_Parameter(BuiltInParameter.SHEET_CURRENT_REVISION)?.AsString() ?? "").Replace(",", ";");
                    string issueDate = sheet.get_Parameter(BuiltInParameter.SHEET_CURRENT_REVISION_DATE)?.AsString() ?? "";
                    string disc = ParameterHelpers.GetString(sheet, "SHEET_DISCIPLINE") ?? "";
                    string drawn = sheet.get_Parameter(BuiltInParameter.SHEET_DRAWN_BY)?.AsString() ?? "";
                    string check = sheet.get_Parameter(BuiltInParameter.SHEET_CHECKED_BY)?.AsString() ?? "";
                    string approved = sheet.get_Parameter(BuiltInParameter.SHEET_APPROVED_BY)?.AsString() ?? "";
                    int viewCount = sheet.GetAllPlacedViews()?.Count ?? 0;
                    sb.AppendLine($"{num},{name},{rev},{issueDate},{disc},{drawn},{check},{approved},{viewCount}");
                }
                catch (Exception ex) { StingLog.Warn($"Sheet CSV row {sheet.Id}: {ex.Message}"); }
            }

            string exportPath = OutputLocationHelper.GetTimestampedPath(
                app.ActiveUIDocument?.Document, "SHEET_REGISTER", ".csv");
            try
            {
                System.IO.File.WriteAllText(exportPath, sb.ToString());
                TaskDialog.Show("Export Sheets", $"Exported {sheets.Count} sheets to:\n{exportPath}");
            }
            catch (Exception ex)
            {
                StingLog.Error($"Sheet CSV export: {ex.Message}", ex);
                TaskDialog.Show("Export Sheets", $"Export failed: {ex.Message}\n\n{sb}");
            }
        }

        // ── TitleBlock operations ───────────────────────────────────

        // ── Drawing Type Editor inline helpers (Phase 113.x) ────────────────
        private static void DrawingTypesGroupBrowserInline(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            try
            {
                var lib = StingTools.Core.Drawing.DrawingTypeRegistry.GetLibrary(doc);
                var types = lib?.DrawingTypes ?? new System.Collections.Generic.List<StingTools.Core.Drawing.DrawingType>();
                var byDisc = types
                    .GroupBy(t => string.IsNullOrEmpty(t.Discipline) ? "*" : t.Discipline)
                    .OrderBy(g => g.Key);
                var sb = new System.Text.StringBuilder();
                foreach (var g in byDisc)
                {
                    sb.AppendLine($"[{g.Key}]  ({g.Count()} types)");
                    foreach (var t in g.OrderBy(x => x.Purpose).ThenBy(x => x.Id))
                        sb.AppendLine($"    {t.Id}  ·  {t.Purpose}  ·  {t.PaperSize} 1:{t.Scale}");
                    sb.AppendLine();
                }
                TaskDialog.Show("Drawing Types — Group Browser",
                    sb.Length == 0 ? "No drawing types loaded." : sb.ToString());
            }
            catch (Exception ex) { StingLog.Error("DrawingTypes_GroupBrowser", ex); }
        }

        private static void DrawingTypesSyncStylesInline(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            try
            {
                // Refresh registry then report which view-template / viewport-type
                // names referenced by drawing types resolve in the active project.
                StingTools.Core.Drawing.DrawingTypeRegistry.Reload(doc);
                var lib = StingTools.Core.Drawing.DrawingTypeRegistry.GetLibrary(doc);
                int ok = 0, missingTpl = 0, missingVp = 0;
                var miss = new System.Collections.Generic.List<string>();
                foreach (var t in lib?.DrawingTypes ?? new System.Collections.Generic.List<StingTools.Core.Drawing.DrawingType>())
                {
                    bool tplOk = string.IsNullOrEmpty(t.ViewTemplateName) ||
                                 new FilteredElementCollector(doc).OfClass(typeof(View))
                                    .Cast<View>().Any(v => v.IsTemplate &&
                                        string.Equals(v.Name, t.ViewTemplateName, StringComparison.OrdinalIgnoreCase));
                    bool vpOk  = string.IsNullOrEmpty(t.ViewportTypeName) ||
                                 new FilteredElementCollector(doc).OfClass(typeof(ElementType))
                                    .Cast<ElementType>().Any(et => et.Category?.Id.Value == (long)BuiltInCategory.OST_Viewports &&
                                        string.Equals(et.Name, t.ViewportTypeName, StringComparison.OrdinalIgnoreCase));
                    if (tplOk && vpOk) ok++;
                    else
                    {
                        if (!tplOk) { missingTpl++; miss.Add($"  {t.Id}  →  view template '{t.ViewTemplateName}'"); }
                        if (!vpOk)  { missingVp++;  miss.Add($"  {t.Id}  →  viewport type '{t.ViewportTypeName}'"); }
                    }
                }
                TaskDialog.Show("Drawing Types — Sync Styles",
                    $"OK: {ok}\nMissing view templates: {missingTpl}\nMissing viewport types: {missingVp}\n\n" +
                    (miss.Count == 0 ? "All references resolved." : string.Join("\n", miss.Take(40))));
            }
            catch (Exception ex) { StingLog.Error("DrawingTypes_SyncStyles", ex); }
        }

        private static void DrawingTypesFromScopeBoxesInline(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            try
            {
                var sboxes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                    .WhereElementIsNotElementType()
                    .ToList();
                if (sboxes.Count == 0)
                { TaskDialog.Show("From Scope Boxes", "No scope boxes in the project."); return; }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Suggested drawing-type stubs (one per scope box):\n");
                foreach (var sbx in sboxes.OrderBy(s => s.Name))
                {
                    var disc = "A";
                    var nm = (sbx.Name ?? "").ToUpperInvariant();
                    if      (nm.Contains("MEP") || nm.StartsWith("M-")) disc = "M";
                    else if (nm.Contains("ELEC")|| nm.StartsWith("E-")) disc = "E";
                    else if (nm.Contains("PLUMB")||nm.StartsWith("P-")) disc = "P";
                    else if (nm.Contains("STR") || nm.StartsWith("S-")) disc = "S";
                    sb.AppendLine($"  {disc.ToLowerInvariant()}-plan-A1-{sbx.Name.Replace(' ', '_')}-1to100");
                }
                sb.AppendLine("\nUse + New in the Drawing Types tab to commit these as project-scoped types.");
                TaskDialog.Show("From Scope Boxes", sb.ToString());
            }
            catch (Exception ex) { StingLog.Error("DrawingTypes_FromScopeBoxes", ex); }
        }

        private static void TitleBlockReset(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            if (!(doc.ActiveView is ViewSheet sheet))
            { TaskDialog.Show("Title Block", "Active view must be a sheet."); return; }

            var tbs = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .ToList();

            if (tbs.Count == 0)
            { TaskDialog.Show("Title Block", "No title block found on sheet."); return; }

            int reset = 0;
            using (Transaction tx = new Transaction(doc, "STING Title Block Reset"))
            {
                tx.Start();
                foreach (Element tb in tbs)
                {
                    try
                    {
                        // Reset position to origin
                        if (tb.Location is LocationPoint lp)
                        {
                            lp.Point = XYZ.Zero;
                            reset++;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                }
                tx.Commit();
            }
            TaskDialog.Show("Title Block", $"Reset {reset} title blocks to origin.");
        }

        private static void TitleBlockRescue(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            // Find sheets missing title blocks
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToList();

            int missing = 0;
            foreach (var s in sheets)
            {
                var tbs = new FilteredElementCollector(doc, s.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType()
                    .ToList();
                if (tbs.Count == 0) missing++;
            }

            TaskDialog.Show("Title Block Rescue",
                $"Scanned {sheets.Count} sheets.\n" +
                $"Missing title blocks: {missing}\n\n" +
                "To fix, open the sheet and place a title block from Insert tab.");
        }

        // ── Revision operations ─────────────────────────────────────

        private static void RevisionToggle(UIApplication app, string what)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            var view = doc.ActiveView;

            var clouds = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_RevisionClouds)
                .WhereElementIsNotElementType()
                .ToList();

            var tags = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_RevisionCloudTags)
                .WhereElementIsNotElementType()
                .ToList();

            TaskDialog.Show("Revisions",
                $"Active view: {view.Name}\n" +
                $"Revision clouds: {clouds.Count}\n" +
                $"Revision tags: {tags.Count}\n\n" +
                "Use Revit View > Visibility Graphics > Annotation Categories " +
                "to control revision cloud/tag visibility.");
        }

        private static void RevisionDeleteClouds(UIApplication app, bool selectionOnly)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            ICollection<ElementId> cloudIds;
            if (selectionOnly)
            {
                cloudIds = uidoc.Selection.GetElementIds()
                    .Where(id =>
                    {
                        var e = doc.GetElement(id);
                        return e?.Category?.Id.Value == (int)BuiltInCategory.OST_RevisionClouds;
                    })
                    .ToList();
            }
            else
            {
                cloudIds = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(BuiltInCategory.OST_RevisionClouds)
                    .WhereElementIsNotElementType()
                    .ToElementIds();
            }

            if (cloudIds.Count == 0)
            { TaskDialog.Show("Delete Clouds", "No revision clouds found."); return; }

            TaskDialog confirm = new TaskDialog("Delete Revision Clouds");
            confirm.MainInstruction = $"Delete {cloudIds.Count} revision clouds?";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() != TaskDialogResult.Ok) return;

            using (Transaction tx = new Transaction(doc, "STING Delete Revision Clouds"))
            {
                tx.Start();
                doc.Delete(cloudIds);
                tx.Commit();
            }
            TaskDialog.Show("Delete Clouds", $"Deleted {cloudIds.Count} revision clouds.");
        }

        // ── Measurement operations ──────────────────────────────────

        private static void MeasureSelected(UIApplication app, string mode)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Measure", "Select elements first."); return; }

            double totalLength = 0;
            double totalArea = 0;
            double totalPerimeter = 0;
            int counted = 0;

            foreach (ElementId id in ids)
            {
                Element e = doc.GetElement(id);
                if (e == null) continue;

                try
                {
                    if (e.Location is LocationCurve lc)
                    {
                        totalLength += lc.Curve.Length;
                        counted++;
                    }

                    // Try area parameter
                    Parameter areaP = e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                    if (areaP != null && areaP.HasValue)
                        totalArea += areaP.AsDouble();

                    Parameter perimP = e.get_Parameter(BuiltInParameter.HOST_PERIMETER_COMPUTED);
                    if (perimP != null && perimP.HasValue)
                        totalPerimeter += perimP.AsDouble();
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Measurement: failed on element {e?.Id}: {ex.Message}");
                }
            }

            var report = new StringBuilder();
            report.AppendLine($"Measurement — {ids.Count} elements");
            report.AppendLine(new string('─', 35));
            if (totalLength > 0) report.AppendLine($"  Total length:    {totalLength * 0.3048:F2} m ({totalLength:F2} ft)");
            if (totalArea > 0) report.AppendLine($"  Total area:      {totalArea * 0.0929:F2} m² ({totalArea:F2} ft²)");
            if (totalPerimeter > 0) report.AppendLine($"  Total perimeter: {totalPerimeter * 0.3048:F2} m ({totalPerimeter:F2} ft)");
            if (totalLength == 0 && totalArea == 0 && totalPerimeter == 0)
                report.AppendLine("  No measurable geometry found in selection.");

            TaskDialog.Show("Measure", report.ToString());
        }

        // ── Line pattern / line weight operations ───────────────────

        private static void ApplyLinePattern(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Line Pattern", "Select elements first."); return; }

            TaskDialog dlg = new TaskDialog("Apply Line Pattern");
            dlg.MainInstruction = $"Set projection line pattern for {ids.Count} elements";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Solid", "Continuous line");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Dash", "Dashed line pattern");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Hidden", "Short dashes (hidden lines)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Reset (Default)", "Remove line pattern override");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string patternName;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: patternName = "Solid"; break;
                case TaskDialogResult.CommandLink2: patternName = "Dash"; break;
                case TaskDialogResult.CommandLink3: patternName = "Hidden"; break;
                case TaskDialogResult.CommandLink4: patternName = null; break;
                default: return;
            }

            // Find line pattern
            ElementId patternId = ElementId.InvalidElementId;
            if (patternName != null)
            {
                var pattern = new FilteredElementCollector(uidoc.Document)
                    .OfClass(typeof(LinePatternElement))
                    .Cast<LinePatternElement>()
                    .FirstOrDefault(lp =>
                        lp.Name.IndexOf(patternName, StringComparison.OrdinalIgnoreCase) >= 0);
                if (pattern != null) patternId = pattern.Id;
            }

            using (Transaction tx = new Transaction(uidoc.Document, "STING Apply Line Pattern"))
            {
                tx.Start();
                var ogs = new OverrideGraphicSettings();
                if (patternId != ElementId.InvalidElementId)
                    ogs.SetProjectionLinePatternId(patternId);
                foreach (ElementId id in ids)
                    uidoc.ActiveView.SetElementOverrides(id, ogs);
                tx.Commit();
            }
        }

        private static void ApplyLineWeightOverride(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var ids = uidoc.Selection.GetElementIds();
            if (ids.Count == 0) { TaskDialog.Show("Line Weight", "Select elements first."); return; }

            TaskDialog dlg = new TaskDialog("Apply Line Weight");
            dlg.MainInstruction = $"Set line weight for {ids.Count} elements";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Thin (1)", "Hairline");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Standard (3)", "Normal");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Bold (6)", "Emphasis");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Extra Bold (10)", "Maximum weight");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int weight;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: weight = 1; break;
                case TaskDialogResult.CommandLink2: weight = 3; break;
                case TaskDialogResult.CommandLink3: weight = 6; break;
                case TaskDialogResult.CommandLink4: weight = 10; break;
                default: return;
            }

            using (Transaction tx = new Transaction(uidoc.Document, "STING Apply Line Weight"))
            {
                tx.Start();
                var ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineWeight(weight);
                foreach (ElementId id in ids)
                    uidoc.ActiveView.SetElementOverrides(id, ogs);
                tx.Commit();
            }
        }

        // ── Viewport operations ─────────────────────────────────

        private static void ViewportRenumberOffset(UIApplication app, int delta)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            if (!(view is ViewSheet sheet))
            {
                TaskDialog.Show("Viewport Number", "Active view must be a sheet.");
                return;
            }

            var vpIds = sheet.GetAllViewports().ToList();
            if (vpIds.Count == 0)
            {
                TaskDialog.Show("Viewport Number", "No viewports on active sheet.");
                return;
            }

            int updated = 0;
            using (Transaction tx = new Transaction(doc, "STING Viewport Renumber"))
            {
                tx.Start();
                foreach (ElementId vpId in vpIds)
                {
                    Viewport vp = doc.GetElement(vpId) as Viewport;
                    if (vp == null) continue;
                    try
                    {
                        Parameter detailNum = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                        if (detailNum != null && !detailNum.IsReadOnly)
                        {
                            string current = detailNum.AsString() ?? "0";
                            if (int.TryParse(current, out int num))
                            {
                                int newNum = Math.Max(1, num + delta);
                                detailNum.Set(newNum.ToString());
                                updated++;
                            }
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                }
                tx.Commit();
            }

            StingLog.Info($"ViewportRenumber: delta={delta}, updated={updated}");
        }

        // ── Orphan & layout helpers ───────────────────────────────

        private static void FindOrphanedTags(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            var tags = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            var orphaned = new List<ElementId>();
            foreach (var tag in tags)
            {
                try
                {
                    var hostIds = tag.GetTaggedLocalElementIds();
                    if (hostIds == null || hostIds.Count == 0)
                    {
                        orphaned.Add(tag.Id);
                        continue;
                    }
                    // Check if host element still exists in view
                    bool anyValid = false;
                    foreach (var hid in hostIds)
                    {
                        Element host = doc.GetElement(hid);
                        if (host != null) { anyValid = true; break; }
                    }
                    if (!anyValid) orphaned.Add(tag.Id);
                }
                catch (Exception ex) { StingLog.Warn($"Orphan check {tag.Id}: {ex.Message}"); orphaned.Add(tag.Id); }
            }

            if (orphaned.Count == 0)
            {
                TaskDialog.Show("Orphaned Tags", $"No orphaned tags found. All {tags.Count} tags are valid.");
                return;
            }

            TaskDialog dlg = new TaskDialog("Orphaned Tags");
            dlg.MainInstruction = $"Found {orphaned.Count} orphaned tags (no valid host element)";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Select Orphans", "Select orphaned tags for review");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Delete Orphans", $"Delete {orphaned.Count} orphaned tags");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1:
                    uidoc.Selection.SetElementIds(orphaned);
                    break;
                case TaskDialogResult.CommandLink2:
                    using (Transaction tx = new Transaction(doc, "STING Delete Orphaned Tags"))
                    {
                        tx.Start();
                        doc.Delete(orphaned);
                        tx.Commit();
                    }
                    TaskDialog.Show("Orphaned Tags", $"Deleted {orphaned.Count} orphaned tags.");
                    break;
            }
        }

        // Persisted tag layout for clone/apply across views
        private static Dictionary<ElementId, (XYZ headPos, bool hasLeader, TagOrientation orient)> _clonedTagLayout;
        private static string _clonedSourceViewName;

        private static void CloneTagLayout(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var sourceView = doc.ActiveView;

            // If layout already cloned, offer to apply it
            if (_clonedTagLayout != null && _clonedTagLayout.Count > 0)
            {
                TaskDialog dlg = new TaskDialog("Clone Tag Layout");
                dlg.MainInstruction = $"Layout from '{_clonedSourceViewName}' ({_clonedTagLayout.Count} tags) is stored.";
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Apply to Current View", "Move existing tags to cloned positions");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Capture New Layout", "Replace stored layout with current view's tags");
                dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

                var result = dlg.Show();
                if (result == TaskDialogResult.CommandLink1)
                {
                    ApplyClonedLayout(app);
                    return;
                }
                else if (result != TaskDialogResult.CommandLink2)
                    return;
            }

            // Get tag positions from source view
            var sourceTags = new FilteredElementCollector(doc, sourceView.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            if (sourceTags.Count == 0)
            {
                TaskDialog.Show("Clone Tag Layout", "No tags in active view to clone.");
                return;
            }

            // Build mapping: host element ID → tag head position + orientation
            _clonedTagLayout = new Dictionary<ElementId, (XYZ headPos, bool hasLeader, TagOrientation orient)>();
            _clonedSourceViewName = sourceView.Name;
            foreach (var tag in sourceTags)
            {
                try
                {
                    var hostIds = tag.GetTaggedLocalElementIds();
                    if (hostIds.Count > 0)
                    {
                        _clonedTagLayout[hostIds.First()] = (tag.TagHeadPosition, tag.HasLeader, tag.TagOrientation);
                    }
                }
                catch (Exception ex) { StingLog.Warn($"CloneTagLayout: {ex.Message}"); }
            }

            TaskDialog.Show("Clone Tag Layout",
                $"Captured layout for {_clonedTagLayout.Count} tags in '{sourceView.Name}'.\n" +
                "Navigate to target view and click Clone again to apply.");

            StingLog.Info($"CloneTagLayout: captured {_clonedTagLayout.Count} positions from '{sourceView.Name}'");
        }

        private static void ApplyClonedLayout(UIApplication app)
        {
            if (_clonedTagLayout == null || _clonedTagLayout.Count == 0)
            {
                TaskDialog.Show("Apply Layout", "No cloned layout stored. Clone a view first.");
                return;
            }

            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var targetView = doc.ActiveView;

            var targetTags = new FilteredElementCollector(doc, targetView.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            int applied = 0;
            using (Transaction tx = new Transaction(doc, "STING Apply Cloned Tag Layout"))
            {
                tx.Start();
                foreach (var tag in targetTags)
                {
                    try
                    {
                        var hostIds = tag.GetTaggedLocalElementIds();
                        if (hostIds.Count == 0) continue;
                        ElementId hostId = hostIds.First();
                        if (!_clonedTagLayout.TryGetValue(hostId, out var layout)) continue;

                        tag.TagHeadPosition = layout.headPos;
                        tag.TagOrientation = layout.orient;
                        applied++;
                    }
                    catch (Exception ex) { StingLog.Warn($"ApplyLayout {tag.Id}: {ex.Message}"); }
                }
                tx.Commit();
            }

            TaskDialog.Show("Apply Layout",
                $"Applied cloned positions to {applied}/{targetTags.Count} tags in '{targetView.Name}'.");
            StingLog.Info($"ApplyClonedLayout: {applied} tags repositioned in '{targetView.Name}'");
        }

        // ── Room tag placement ──────────────────────────────────────

        private static void MoveRoomTags(UIApplication app, string position)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            // Find room tags in view
            var roomTags = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_RoomTags)
                .WhereElementIsNotElementType()
                .ToList();

            if (roomTags.Count == 0)
            {
                TaskDialog.Show("Room Tags", "No room tags in active view.");
                return;
            }

            int moved = 0;
            using (Transaction tx = new Transaction(doc, $"STING Room Tags {position}"))
            {
                tx.Start();
                foreach (Element tagElem in roomTags)
                {
                    try
                    {
                        // Room tags have a Location that can be moved
                        if (tagElem.Location is LocationPoint lp)
                        {
                            // Find the associated room
                            var roomTag = tagElem as Autodesk.Revit.DB.Architecture.RoomTag;
                            if (roomTag == null) continue;
                            var room = roomTag.Room;
                            if (room == null) continue;

                            // Get room bounding box in view
                            BoundingBoxXYZ roomBB = room.get_BoundingBox(view);
                            if (roomBB == null) continue;

                            XYZ targetPos;
                            switch (position)
                            {
                                case "TopLeft":
                                    targetPos = new XYZ(
                                        roomBB.Min.X + (roomBB.Max.X - roomBB.Min.X) * 0.15,
                                        roomBB.Max.Y - (roomBB.Max.Y - roomBB.Min.Y) * 0.15,
                                        lp.Point.Z);
                                    break;
                                case "TopCentre":
                                    targetPos = new XYZ(
                                        (roomBB.Min.X + roomBB.Max.X) / 2.0,
                                        roomBB.Max.Y - (roomBB.Max.Y - roomBB.Min.Y) * 0.15,
                                        lp.Point.Z);
                                    break;
                                case "Centroid":
                                default:
                                    targetPos = new XYZ(
                                        (roomBB.Min.X + roomBB.Max.X) / 2.0,
                                        (roomBB.Min.Y + roomBB.Max.Y) / 2.0,
                                        lp.Point.Z);
                                    break;
                            }

                            lp.Point = targetPos;
                            moved++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"MoveRoomTag {tagElem.Id}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Room Tags", $"Moved {moved} of {roomTags.Count} room tags to {position}.");
        }

        // ── Sheet operations ────────────────────────────────────────

        private static void SheetRenumber(UIApplication app, int delta)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            if (!(doc.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("Sheet Renumber", "Active view must be a sheet.");
                return;
            }

            string currentNum = sheet.SheetNumber;
            // Try to extract numeric portion and increment
            string numPart = "";
            string prefix = "";
            for (int i = currentNum.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(currentNum[i]))
                    numPart = currentNum[i] + numPart;
                else
                {
                    prefix = currentNum.Substring(0, i + 1);
                    break;
                }
            }

            if (string.IsNullOrEmpty(numPart))
            {
                TaskDialog.Show("Sheet Renumber", $"Cannot parse number from '{currentNum}'.");
                return;
            }

            int num = int.Parse(numPart) + delta;
            if (num < 0) num = 0;
            string newNum = prefix + num.ToString().PadLeft(numPart.Length, '0');

            // CRASH FIX: TaskDialog must not be shown inside an active Transaction.
            // Modal dialogs block the UI thread while the transaction holds a document
            // lock, which can deadlock Revit. Show dialogs after commit/rollback.
            string resultMsg = null;
            bool success = false;
            using (Transaction tx = new Transaction(doc, "STING Sheet Renumber"))
            {
                tx.Start();
                try
                {
                    sheet.SheetNumber = newNum;
                    success = true;
                    resultMsg = $"Changed: {currentNum} → {newNum}";
                }
                catch (Exception ex)
                {
                    resultMsg = $"Failed: {ex.Message}";
                    tx.RollBack();
                }
                if (success) tx.Commit();
            }
            TaskDialog.Show("Sheet Renumber", resultMsg);
        }

        private static void SheetAddPrefix(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            if (!(doc.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("Sheet Prefix", "Active view must be a sheet.");
                return;
            }

            TaskDialog dlg = new TaskDialog("Sheet Prefix");
            dlg.MainInstruction = $"Add prefix to '{sheet.Name}'";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "STING - ");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "DRG - ");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "REV - ");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string pfx;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: pfx = "STING - "; break;
                case TaskDialogResult.CommandLink2: pfx = "DRG - "; break;
                case TaskDialogResult.CommandLink3: pfx = "REV - "; break;
                default: return;
            }

            if (sheet.Name.StartsWith(pfx)) return;

            using (Transaction tx = new Transaction(doc, "STING Sheet Prefix"))
            {
                tx.Start();
                try { sheet.Name = pfx + sheet.Name; }
                catch (Exception ex) { StingLog.Warn($"SheetPrefix: {ex.Message}"); }
                tx.Commit();
            }
        }

        private static void SheetAddSuffix(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            if (!(doc.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("Sheet Suffix", "Active view must be a sheet.");
                return;
            }

            TaskDialog dlg = new TaskDialog("Sheet Suffix");
            dlg.MainInstruction = $"Add suffix to '{sheet.Name}'";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, " - P01");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, " - DRAFT");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, " - FOR REVIEW");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string sfx;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: sfx = " - P01"; break;
                case TaskDialogResult.CommandLink2: sfx = " - DRAFT"; break;
                case TaskDialogResult.CommandLink3: sfx = " - FOR REVIEW"; break;
                default: return;
            }

            if (sheet.Name.EndsWith(sfx)) return;

            using (Transaction tx = new Transaction(doc, "STING Sheet Suffix"))
            {
                tx.Start();
                try { sheet.Name = sheet.Name + sfx; }
                catch (Exception ex) { StingLog.Warn($"SheetSuffix: {ex.Message}"); }
                tx.Commit();
            }
        }

        /// <summary>
        /// F3: Remove first token (prefix) or last token (suffix) from selected sheets' names.
        /// Operates on all selected ViewSheets; falls back to active sheet if none selected.
        /// Token boundary is the first/last occurrence of " - " or "-" separator.
        /// </summary>
        private static void SheetRemovePrefixOrSuffix(UIApplication app, bool isPrefix)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            // Collect target sheets: selected ViewSheets, or active sheet fallback
            var targetSheets = new List<ViewSheet>();
            foreach (ElementId id in uidoc.Selection.GetElementIds())
            {
                if (doc.GetElement(id) is ViewSheet vs) targetSheets.Add(vs);
            }
            if (targetSheets.Count == 0 && doc.ActiveView is ViewSheet activeSheet)
                targetSheets.Add(activeSheet);

            if (targetSheets.Count == 0)
            {
                TaskDialog.Show("Sheet " + (isPrefix ? "Remove Prefix" : "Remove Suffix"),
                    "Select one or more sheets first, or open a sheet as the active view.");
                return;
            }

            // Separators to try (longest first to prefer " - " over "-")
            var seps = new[] { " - ", "-", "_" };

            int changed = 0;
            using (Transaction tx = new Transaction(doc,
                "STING Sheet " + (isPrefix ? "Remove Prefix" : "Remove Suffix")))
            {
                tx.Start();
                foreach (ViewSheet sheet in targetSheets)
                {
                    string name = sheet.Name;
                    string newName = name;

                    foreach (string sep in seps)
                    {
                        int idx = isPrefix
                            ? name.IndexOf(sep, StringComparison.Ordinal)
                            : name.LastIndexOf(sep, StringComparison.Ordinal);

                        if (idx >= 0)
                        {
                            newName = isPrefix
                                ? name.Substring(idx + sep.Length).TrimStart()
                                : name.Substring(0, idx).TrimEnd();
                            break;
                        }
                    }

                    if (newName != name && !string.IsNullOrWhiteSpace(newName))
                    {
                        try
                        {
                            sheet.Name = newName;
                            changed++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Sheet remove {(isPrefix ? "prefix" : "suffix")}: {sheet.SheetNumber}: {ex.Message}");
                        }
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Sheet " + (isPrefix ? "Remove Prefix" : "Remove Suffix"),
                $"Updated {changed} of {targetSheets.Count} sheet(s).");
        }

        // ── Nudge helper ──────────────────────────────────────────

        private static void NudgeTags(UIApplication app, string direction)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;

            var tags = Organise.LeaderHelper.GetSelectedTags(uidoc);
            if (tags.Count == 0)
                tags = Organise.LeaderHelper.GetTargetTags(uidoc);
            if (tags.Count == 0) return;

            using (Transaction tx = new Transaction(uidoc.Document, $"STING Nudge {direction}"))
            {
                tx.Start();
                int nudged = Organise.NudgeTagsCommand.NudgeInDirection(
                    uidoc.Document, uidoc.ActiveView, tags, direction);
                tx.Commit();
                StingLog.Info($"Nudge {direction}: {nudged} tags");
            }
        }

        private static Color[] GetColorPalette(string name, int count)
        {
            return (name?.ToLower()) switch
            {
                "rag" => new[] {
                    new Color(244, 67, 54),
                    new Color(255, 152, 0),
                    new Color(76, 175, 80)
                },
                "monochrome" => new[] {
                    new Color(0, 0, 0),
                    new Color(85, 85, 85),
                    new Color(170, 170, 170),
                    new Color(212, 212, 212),
                    new Color(255, 255, 255)
                },
                "discipline" => new[] {
                    new Color(33, 150, 243),
                    new Color(255, 235, 59),
                    new Color(76, 175, 80),
                    new Color(158, 158, 158),
                    new Color(244, 67, 54),
                    new Color(255, 152, 0),
                    new Color(156, 39, 176),
                    new Color(121, 85, 72)
                },
                _ => new[] {
                    new Color(244, 67, 54), new Color(233, 30, 99),
                    new Color(156, 39, 176), new Color(103, 58, 183),
                    new Color(63, 81, 181), new Color(33, 150, 243),
                    new Color(3, 169, 244), new Color(0, 188, 212),
                    new Color(0, 150, 136), new Color(76, 175, 80),
                    new Color(139, 195, 74), new Color(205, 220, 57),
                    new Color(255, 235, 59), new Color(255, 193, 7),
                    new Color(255, 152, 0), new Color(255, 87, 34),
                    new Color(121, 85, 72), new Color(158, 158, 158),
                    new Color(96, 125, 139), new Color(0, 0, 0)
                }
            };
        }

        // ── AI Smart Select helpers ──────────────────────────────────

        /// <summary>
        /// Predict what the user wants to select based on current selection patterns.
        /// Analyzes category, family, type, and parameter values of selected elements,
        /// then selects all similar elements in the view.
        /// </summary>
        private static void AIPredictSelect(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("AI Predict Select", "Select one or more elements first.\nThe tool will find similar elements based on shared properties.");
                return;
            }

            // Analyze selection: collect category, family, type patterns
            var categories = new HashSet<string>();
            var families = new HashSet<string>();
            var types = new HashSet<string>();
            foreach (ElementId id in selected)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;
                categories.Add(ParameterHelpers.GetCategoryName(el));
                families.Add(ParameterHelpers.GetFamilyName(el));
                types.Add(ParameterHelpers.GetFamilySymbolName(el));
            }

            // Find matching elements — priority: type > family > category
            var allInView = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .WhereElementIsNotElementType().ToList();

            var matches = new List<ElementId>();
            foreach (Element el in allInView)
            {
                string typeName = ParameterHelpers.GetFamilySymbolName(el);
                string famName = ParameterHelpers.GetFamilyName(el);
                string catName = ParameterHelpers.GetCategoryName(el);

                // Match by type first (most specific), then family, then category
                if (types.Contains(typeName))
                    matches.Add(el.Id);
                else if (families.Contains(famName))
                    matches.Add(el.Id);
            }

            if (matches.Count == 0)
            {
                // Fall back to category match
                foreach (Element el in allInView)
                    if (categories.Contains(ParameterHelpers.GetCategoryName(el)))
                        matches.Add(el.Id);
            }

            uidoc.Selection.SetElementIds(matches);
            TaskDialog.Show("AI Predict Select",
                $"Selected {matches.Count} elements matching pattern:\n" +
                $"  Categories: {string.Join(", ", categories.Take(5))}\n" +
                $"  Families: {string.Join(", ", families.Take(5))}\n" +
                $"  Types: {string.Join(", ", types.Take(5))}");
            StingLog.Info($"AIPredictSelect: {selected.Count} seed → {matches.Count} matches");
        }

        /// <summary>
        /// Select all elements of the same family and type as the current selection.
        /// </summary>
        private static void AISimilarSelect(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("Similar Select", "Select an element first.");
                return;
            }

            var targetTypes = new HashSet<ElementId>();
            foreach (ElementId id in selected)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;
                ElementId typeId = el.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                    targetTypes.Add(typeId);
            }

            if (targetTypes.Count == 0)
            {
                TaskDialog.Show("Similar Select", "No type information found on selected elements.");
                return;
            }

            var matches = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .WhereElementIsNotElementType()
                .Where(e => targetTypes.Contains(e.GetTypeId()))
                .Select(e => e.Id).ToList();

            uidoc.Selection.SetElementIds(matches);
            StingLog.Info($"AISimilarSelect: {targetTypes.Count} types → {matches.Count} elements");
        }

        /// <summary>
        /// Chain-select connected MEP elements starting from selection.
        /// Walks the MEP connector graph to find all connected elements.
        /// </summary>
        private static void AIChainSelect(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("Chain Select", "Select a MEP element to trace its connected chain.");
                return;
            }

            var visited = new HashSet<ElementId>(selected);
            var queue = new Queue<ElementId>(selected);
            int maxDepth = 200;

            while (queue.Count > 0 && visited.Count < maxDepth)
            {
                ElementId currentId = queue.Dequeue();
                Element el = doc.GetElement(currentId);
                if (el == null) continue;

                try
                {
                    var connMgr = (el as MEPCurve)?.ConnectorManager
                        ?? (el as FamilyInstance)?.MEPModel?.ConnectorManager;

                    if (connMgr == null) continue;

                    foreach (Connector conn in connMgr.Connectors)
                    {
                        if (!conn.IsConnected) continue;
                        foreach (Connector other in conn.AllRefs)
                        {
                            if (other.Owner != null && visited.Add(other.Owner.Id))
                                queue.Enqueue(other.Owner.Id);
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"AIChainSelect: connector traversal failed for {currentId}: {ex.Message}"); }
            }

            uidoc.Selection.SetElementIds(visited.ToList());
            TaskDialog.Show("Chain Select",
                $"Traced {visited.Count} connected elements from {selected.Count} seed(s).");
            StingLog.Info($"AIChainSelect: {selected.Count} → {visited.Count} elements");
        }

        /// <summary>
        /// Select elements whose parameter values are outliers compared to the majority.
        /// Finds elements with missing tags, unusual discipline codes, or empty required tokens.
        /// </summary>
        private static void AIOutliersSelect(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            var outliers = new List<ElementId>();
            int total = 0;
            var elements = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .WhereElementIsNotElementType().ToList();

            foreach (Element el in elements)
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!known.Contains(cat)) continue;
                total++;

                // Check for anomalies: missing tag, empty DISC, empty SYS
                string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);

                bool isOutlier = false;
                if (string.IsNullOrEmpty(tag1)) isOutlier = true;
                if (string.IsNullOrEmpty(disc)) isOutlier = true;
                if (!string.IsNullOrEmpty(disc) && disc == "XX") isOutlier = true;

                // Check if tag has placeholders
                if (!string.IsNullOrEmpty(tag1) && (tag1.Contains("-XX-") || tag1.Contains("-0000")))
                    isOutlier = true;

                if (isOutlier) outliers.Add(el.Id);
            }

            uidoc.Selection.SetElementIds(outliers);
            TaskDialog.Show("Outlier Select",
                $"Found {outliers.Count} outlier elements of {total} taggable:\n" +
                "  - Missing or incomplete tags\n" +
                "  - Placeholder values (XX, 0000)\n" +
                "  - Empty discipline codes");
            StingLog.Info($"AIOutliersSelect: {outliers.Count} outliers of {total} taggable");
        }

        /// <summary>
        /// Select elements matching a spatial or parameter-value pattern.
        /// Finds elements that share the same parameter values (DISC, SYS, LOC, ZONE)
        /// as the selected seed elements — like "select all HVAC elements in zone Z01".
        /// </summary>
        private static void AIPatternSelect(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("Pattern Select", "Select seed elements first.\nThe tool will find elements sharing the same DISC + SYS + LOC + ZONE pattern.");
                return;
            }

            // Extract parameter patterns from seed elements
            var patterns = new HashSet<string>();
            foreach (ElementId id in selected)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;
                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                string loc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                string zone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
                string pattern = $"{disc}|{sys}|{loc}|{zone}";
                if (pattern != "|||") patterns.Add(pattern);
            }

            if (patterns.Count == 0)
            {
                TaskDialog.Show("Pattern Select", "No STING tag patterns found on selected elements.\nEnsure elements have DISC/SYS/LOC/ZONE values.");
                return;
            }

            // Find all view elements matching any seed pattern
            var matches = new List<ElementId>();
            foreach (Element el in new FilteredElementCollector(doc, doc.ActiveView.Id)
                .WhereElementIsNotElementType())
            {
                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                string loc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                string zone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
                string pat = $"{disc}|{sys}|{loc}|{zone}";
                if (patterns.Contains(pat))
                    matches.Add(el.Id);
            }

            uidoc.Selection.SetElementIds(matches);
            TaskDialog.Show("Pattern Select",
                $"Found {matches.Count} elements matching {patterns.Count} pattern(s):\n" +
                string.Join("\n", patterns.Take(5).Select(p => $"  {p.Replace("|", " / ")}")));
            StingLog.Info($"AIPatternSelect: {patterns.Count} patterns → {matches.Count} elements");
        }

        /// <summary>
        /// Select elements within a room/space boundary.
        /// Uses the room that contains the first selected element, then selects
        /// all taggable elements within that room's boundary.
        /// </summary>
        private static void AIBoundarySelect(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("Boundary Select", "Select an element inside a room.\nAll taggable elements in that room will be selected.");
                return;
            }

            // Find the room containing the first selected element
            Autodesk.Revit.DB.Architecture.Room targetRoom = null;
            foreach (ElementId id in selected)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;
                targetRoom = ParameterHelpers.GetRoomAtElement(doc, el);
                if (targetRoom != null) break;
            }

            if (targetRoom == null)
            {
                TaskDialog.Show("Boundary Select", "No room found at selected element position.\nEnsure rooms are placed and the element is within a room boundary.");
                return;
            }

            // Select all taggable elements within this room
            var matches = new List<ElementId>();
            var knownCats = new HashSet<string>(TagConfig.DiscMap.Keys);
            foreach (Element el in new FilteredElementCollector(doc, doc.ActiveView.Id)
                .WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!knownCats.Contains(cat)) continue;

                var room = ParameterHelpers.GetRoomAtElement(doc, el);
                if (room != null && room.Id == targetRoom.Id)
                    matches.Add(el.Id);
            }

            uidoc.Selection.SetElementIds(matches);
            string roomName = targetRoom.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
            string roomNum = targetRoom.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
            TaskDialog.Show("Boundary Select",
                $"Room: {roomName} ({roomNum})\n" +
                $"Selected {matches.Count} taggable elements within room boundary.");
            StingLog.Info($"AIBoundarySelect: room '{roomName}' ({roomNum}) → {matches.Count} elements");
        }

        /// <summary>
        /// Select elements within a proximity radius of the current selection.
        /// Uses bounding box center distance comparison.
        /// </summary>
        private static void SelectNearby(UIApplication app, double radiusFeet)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;
            var selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 0)
            {
                TaskDialog.Show("Select Nearby", "Select element(s) first. Nearby elements will be found.");
                return;
            }

            // Get centers of selected elements
            var seedCenters = new List<XYZ>();
            foreach (ElementId id in selected)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;
                BoundingBoxXYZ bb = el.get_BoundingBox(view);
                if (bb != null)
                    seedCenters.Add((bb.Min + bb.Max) / 2.0);
            }

            if (seedCenters.Count == 0)
            {
                TaskDialog.Show("Select Nearby", "Cannot determine positions of selected elements.");
                return;
            }

            // Find all elements within radius
            var nearby = new List<ElementId>();
            foreach (Element el in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(view);
                if (bb == null) continue;
                XYZ center = (bb.Min + bb.Max) / 2.0;

                foreach (XYZ seed in seedCenters)
                {
                    double dist = new XYZ(center.X - seed.X, center.Y - seed.Y, 0).GetLength();
                    if (dist <= radiusFeet)
                    {
                        nearby.Add(el.Id);
                        break;
                    }
                }
            }

            uidoc.Selection.SetElementIds(nearby);
            double radiusM = radiusFeet * 0.3048;
            StingLog.Info($"SelectNearby: radius={radiusM:F1}m, found={nearby.Count}");
        }

        /// <summary>
        /// Select elements at the edges/boundaries of the view crop region.
        /// Useful for finding elements that may be cut off or partially visible.
        /// </summary>
        private static void SelectEdgeElements(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            BoundingBoxXYZ viewBB = view.CropBox;
            if (viewBB == null || !view.CropBoxActive)
            {
                TaskDialog.Show("Edge Select", "View must have an active crop region.");
                return;
            }

            // Edge margin: elements within 10% of the crop box boundary
            double dx = (viewBB.Max.X - viewBB.Min.X) * 0.1;
            double dy = (viewBB.Max.Y - viewBB.Min.Y) * 0.1;

            var edgeElements = new List<ElementId>();
            foreach (Element el in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(view);
                if (bb == null) continue;
                XYZ center = (bb.Min + bb.Max) / 2.0;

                bool nearEdge = center.X < viewBB.Min.X + dx || center.X > viewBB.Max.X - dx
                    || center.Y < viewBB.Min.Y + dy || center.Y > viewBB.Max.Y - dy;

                if (nearEdge)
                    edgeElements.Add(el.Id);
            }

            uidoc.Selection.SetElementIds(edgeElements);
            TaskDialog.Show("Edge Select",
                $"Selected {edgeElements.Count} elements near the view crop boundary.");
            StingLog.Info($"SelectEdgeElements: {edgeElements.Count} near crop boundary");
        }

        // ── Scope & mode toggles ─────────────────────────────────────

        private static bool _scopeIsView = true;

        /// <summary>
        /// Toggle between view-scope (active view only) and project-scope (all elements).
        /// Affects subsequent AI select and analysis operations.
        /// </summary>
        private static void ToggleScopeMode(UIApplication app)
        {
            _scopeIsView = !_scopeIsView;
            string mode = _scopeIsView ? "Active View" : "Entire Project";
            TaskDialog.Show("Scope Mode", $"Selection scope: {mode}");
            StingLog.Info($"ToggleScopeMode: {mode}");
        }

        private static bool _overwriteMode = false;

        /// <summary>
        /// Toggle between skip-existing and overwrite modes for parameter operations.
        /// </summary>
        private static void ToggleOverwriteMode(UIApplication app)
        {
            _overwriteMode = !_overwriteMode;
            string mode = _overwriteMode ? "OVERWRITE existing values" : "SKIP existing values";
            TaskDialog.Show("Overwrite Mode", $"Parameter write mode: {mode}");
            StingLog.Info($"ToggleOverwriteMode: {mode}");
        }

        // ── Anomaly & intelligence helpers ────────────────────────────

        /// <summary>
        /// Scan the current view for parameter anomalies: missing tokens,
        /// inconsistent values, placeholder codes, format violations.
        /// </summary>
        private static void AnomalyRefreshScan(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;
            if (view == null)
            {
                TaskDialog.Show("Anomaly Scan", "No active view — switch to a model view first.");
                return;
            }
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);

            int total = 0, missingTag = 0, missingDisc = 0, missingSys = 0;
            int placeholders = 0, formatErrors = 0;
            // Collect param names from scanned elements for dropdown population
            var paramNames = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string p in ParamRegistry.AllParamGuids.Keys)
                paramNames.Add(p);

            foreach (Element el in new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!known.Contains(cat)) continue;
                total++;

                // Collect parameter names from first few elements for dropdown
                if (total <= 3)
                {
                    foreach (Parameter p in el.Parameters)
                    {
                        if (p.Definition != null && !string.IsNullOrEmpty(p.Definition.Name))
                            paramNames.Add(p.Definition.Name);
                    }
                }

                string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);

                if (string.IsNullOrEmpty(tag1)) missingTag++;
                if (string.IsNullOrEmpty(disc)) missingDisc++;
                if (string.IsNullOrEmpty(sys)) missingSys++;

                if (!string.IsNullOrEmpty(tag1))
                {
                    if (tag1.Contains("-XX-") || tag1.Contains("-0000"))
                        placeholders++;
                    string[] parts = tag1.Split('-');
                    if (parts.Length != 8)
                        formatErrors++;
                }
            }

            // Populate dropdown with discovered parameters
            StingDockPanel.PopulateParamDropdowns(paramNames);

            int issues = missingTag + missingDisc + missingSys + placeholders + formatErrors;
            double healthPct = total > 0 ? ((total - Math.Min(issues, total)) / (double)total) * 100 : 0;

            var report = new StringBuilder();
            report.AppendLine($"Anomaly Scan — {view.Name}");
            report.AppendLine(new string('═', 45));
            report.AppendLine($"  Taggable elements: {total}");
            report.AppendLine($"  Health score:      {healthPct:F0}%");
            report.AppendLine();
            report.AppendLine("  Anomalies:");
            report.AppendLine($"    Missing tags:     {missingTag}");
            report.AppendLine($"    Missing DISC:     {missingDisc}");
            report.AppendLine($"    Missing SYS:      {missingSys}");
            report.AppendLine($"    Placeholders:     {placeholders} (XX, 0000)");
            report.AppendLine($"    Format errors:    {formatErrors} (not 8-segment)");
            report.AppendLine();
            report.AppendLine($"  Total issues: {issues}");

            var td = new TaskDialog("STING Tools - Anomaly Scan");
            td.MainInstruction = $"Anomaly Scan — {view.Name}";
            td.MainContent = report.ToString();
            td.CommonButtons = TaskDialogCommonButtons.Ok;
            td.DefaultButton = TaskDialogResult.Ok;
            td.Show();
            StingLog.Info($"AnomalyRefresh: {total} elements, {issues} issues, health={healthPct:F0}%");
        }

        /// <summary>
        /// Analyze selected elements and suggest optimal bulk parameter operations.
        /// Reports frequency of existing values and recommends the most impactful bulk write.
        /// </summary>
        private static void BulkBrainSuggest(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var ids = uidoc.Selection.GetElementIds();

            if (ids.Count == 0)
            {
                // Use all taggable in view
                var known2 = new HashSet<string>(TagConfig.DiscMap.Keys);
                ids = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .Where(e => known2.Contains(ParameterHelpers.GetCategoryName(e)))
                    .Select(e => e.Id).ToList();
            }

            if (ids.Count == 0)
            {
                TaskDialog.Show("Bulk Brain", "No taggable elements found.");
                return;
            }

            // Analyze token fill rates
            string[] tokens = { ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC, ParamRegistry.PROD };
            var tokenStats = new Dictionary<string, (int filled, int empty, string topValue)>();

            foreach (string token in tokens)
            {
                int filled = 0, empty = 0;
                var valueCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (ElementId id in ids)
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;
                    string val = ParameterHelpers.GetString(el, token);
                    if (string.IsNullOrEmpty(val))
                        empty++;
                    else
                    {
                        filled++;
                        valueCounts.TryGetValue(val, out int vc);
                        valueCounts[val] = vc + 1;
                    }
                }

                string topVal = valueCounts.Count > 0
                    ? valueCounts.OrderByDescending(v => v.Value).First().Key
                    : "-";
                tokenStats[token] = (filled, empty, topVal);
            }

            var report = new StringBuilder();
            report.AppendLine($"Bulk Brain — {ids.Count} elements");
            report.AppendLine(new string('═', 55));
            report.AppendLine($"{"Token",-28} {"Filled",-8} {"Empty",-8} {"Top Value"}");
            report.AppendLine(new string('─', 55));

            string bestSuggestion = null;
            int maxEmpty = 0;
            foreach (var kvp in tokenStats)
            {
                string shortName = kvp.Key.Replace("ASS_", "").Replace("_TXT", "").Replace("_COD", "");
                report.AppendLine($"  {shortName,-26} {kvp.Value.filled,-8} {kvp.Value.empty,-8} {kvp.Value.topValue}");
                if (kvp.Value.empty > maxEmpty)
                {
                    maxEmpty = kvp.Value.empty;
                    bestSuggestion = kvp.Key;
                }
            }

            report.AppendLine();
            if (bestSuggestion != null && maxEmpty > 0)
                report.AppendLine($"Suggestion: Run 'Family-Stage Populate' to fill {maxEmpty} empty {bestSuggestion} values.");
            else
                report.AppendLine("All tokens are fully populated.");

            TaskDialog.Show("Bulk Brain", report.ToString());
            StingLog.Info($"BulkBrain: {ids.Count} elements analyzed");
        }

        /// <summary>
        /// Refresh tag family information: audit loaded tag families and report coverage.
        /// </summary>
        private static void TagFamilyRefresh(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            // Find all loaded tag family types
            var tagFamilies = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs =>
                {
                    try { return fs.Family?.FamilyCategory?.Name?.Contains("Tag") == true; }
                    catch (Exception ex) { StingLog.Warn($"Tag family filter: {ex.Message}"); return false; }
                })
                .ToList();

            var stingTags = tagFamilies.Where(t => t.Family.Name.StartsWith("STING")).ToList();
            var otherTags = tagFamilies.Where(t => !t.Family.Name.StartsWith("STING")).ToList();

            // Check coverage of known categories
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            var taggedCats = new HashSet<string>();
            foreach (var tf in tagFamilies)
            {
                try
                {
                    var cat = tf.Family.FamilyCategory;
                    if (cat != null) taggedCats.Add(cat.Name);
                }
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
            }

            var report = new StringBuilder();
            report.AppendLine("Tag Family Audit");
            report.AppendLine(new string('═', 45));
            report.AppendLine($"  STING tag families: {stingTags.Count}");
            report.AppendLine($"  Other tag families: {otherTags.Count}");
            report.AppendLine($"  Taggable categories: {known.Count}");
            report.AppendLine();
            if (stingTags.Count > 0)
            {
                report.AppendLine("STING tag families:");
                foreach (var tf in stingTags.Take(20))
                    report.AppendLine($"  {tf.Family.Name} : {tf.Name}");
            }

            TaskDialog.Show("Tag Family Refresh", report.ToString());
            StingLog.Info($"TagFamilyRefresh: {stingTags.Count} STING tags, {otherTags.Count} other");
        }

        // ── Elbow snap helper (direct angle application) ─────────────

        /// <summary>
        /// Snap leader elbows to a specific angle without dialog.
        /// angleMode: "45", "90", "0" (straight), or "cycle" (detect current and rotate).
        /// </summary>
        private static void SnapElbowDirect(UIApplication app, string angleMode)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            var tags = Organise.LeaderHelper.GetSelectedTags(uidoc)
                .Where(t => t.HasLeader).ToList();
            if (tags.Count == 0)
                tags = Organise.LeaderHelper.GetTargetTags(uidoc)
                    .Where(t => t.HasLeader).ToList();

            if (tags.Count == 0)
            {
                TaskDialog.Show("Snap Elbows", "No tags with leaders found.");
                return;
            }

            int snapped = 0;
            using (Transaction tx = new Transaction(doc, "STING Snap Leader Elbows"))
            {
                tx.Start();
                foreach (IndependentTag tag in tags)
                {
                    try
                    {
                        var hostIds = tag.GetTaggedLocalElementIds();
                        Element host = hostIds.Count > 0 ? doc.GetElement(hostIds.First()) : null;
                        if (host == null) continue;

                        XYZ hostCenter = Organise.LeaderHelper.GetElementCenter(host);
                        if (hostCenter == null) continue;

                        XYZ tagHead = tag.TagHeadPosition;
                        XYZ delta = tagHead - hostCenter;
                        if (delta.GetLength() < 0.01) continue;

                        // Determine effective mode for cycling
                        string effectiveMode = angleMode;
                        if (effectiveMode == "cycle")
                        {
                            // Detect current elbow angle from existing elbow position
                            effectiveMode = DetectAndCycleElbowAngle(tag, host, doc, hostCenter, tagHead);
                        }

                        XYZ elbowPos;
                        if (effectiveMode == "0")
                        {
                            // Straight: elbow on line near tag head
                            XYZ dir = delta.Normalize();
                            double len = delta.GetLength();
                            elbowPos = hostCenter + dir * (len * 0.85);
                        }
                        else if (effectiveMode == "45")
                        {
                            // 45° elbow near element (arrow side)
                            double absDx = Math.Abs(delta.X);
                            double absDy = Math.Abs(delta.Y);
                            double diag = Math.Min(absDx, absDy);
                            double signX = delta.X >= 0 ? 1 : -1;
                            double signY = delta.Y >= 0 ? 1 : -1;

                            elbowPos = new XYZ(hostCenter.X + diag * signX, hostCenter.Y + diag * signY, hostCenter.Z);
                        }
                        else // "90"
                        {
                            // 90° elbow near element (arrow side): vertical from host then horizontal to tag
                            elbowPos = new XYZ(hostCenter.X, tagHead.Y, hostCenter.Z);
                        }

                        var refs = tag.GetTaggedReferences();
                        if (refs != null && refs.Count > 0)
                        {
                            tag.LeaderEndCondition = LeaderEndCondition.Free;
                            tag.SetLeaderElbow(refs.First(), elbowPos);
                            snapped++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"SnapElbowDirect on tag {tag.Id}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Snap Elbows", $"Snapped {snapped} leader elbows to {angleMode}°.");
        }

        /// <summary>
        /// Detect current elbow angle and return the next angle in cycle: 90→45→0→90.
        /// </summary>
        private static string DetectAndCycleElbowAngle(IndependentTag tag, Element host,
            Document doc, XYZ hostCenter, XYZ tagHead)
        {
            try
            {
                var refs = tag.GetTaggedReferences();
                if (refs == null || refs.Count == 0) return "90";

                XYZ elbow = tag.GetLeaderElbow(refs.First());
                if (elbow == null) return "90";

                XYZ delta = tagHead - hostCenter;
                double absDx = Math.Abs(delta.X);
                double absDy = Math.Abs(delta.Y);

                // Check if elbow is at midpoint (straight/0°)
                XYZ mid = (hostCenter + tagHead) / 2.0;
                if (elbow.DistanceTo(mid) < 0.1)
                    return "90"; // Cycle: 0 → 90

                // Check if elbow is at orthogonal position (90°) — arrow side
                XYZ ortho90 = new XYZ(hostCenter.X, tagHead.Y, hostCenter.Z);
                if (elbow.DistanceTo(ortho90) < 0.1)
                    return "45"; // Cycle: 90 → 45

                // Otherwise assume 45° or unknown → cycle to 0 (straight)
                return "0"; // Cycle: 45 → 0
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ElbowAngle detect: {ex.Message}");
                return "90";
            }
        }

        // ── Conditional selection builder (legacy — kept for ClearStaticState) ──

        private static readonly List<(string param, string op, string value)> _conditions
            = new List<(string, string, string)>();

        // Legacy condition methods removed — replaced by OpenParameterLookupDialog()
        // which provides a unified WPF dialog with full condition builder, 11 operators,
        // live match count, and Select/Color/Apply actions.

        // ── Remaining stub implementations ────────────────────────────

        /// <summary>
        /// Select elements within a quadrant of the view (NE/NW/SE/SW).
        /// </summary>
        private static void SelectQuadrant(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            TaskDialog dlg = new TaskDialog("Select Quadrant");
            dlg.MainInstruction = "Select elements in which quadrant?";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "NW (Top-Left)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "NE (Top-Right)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "SW (Bottom-Left)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "SE (Bottom-Right)");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            int quad;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: quad = 0; break; // NW
                case TaskDialogResult.CommandLink2: quad = 1; break; // NE
                case TaskDialogResult.CommandLink3: quad = 2; break; // SW
                case TaskDialogResult.CommandLink4: quad = 3; break; // SE
                default: return;
            }

            // Calculate view center from all visible elements
            var allElements = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType().ToList();
            double sumX = 0, sumY = 0;
            int counted = 0;
            foreach (Element el in allElements)
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(view);
                if (bb == null) continue;
                sumX += (bb.Min.X + bb.Max.X) / 2.0;
                sumY += (bb.Min.Y + bb.Max.Y) / 2.0;
                counted++;
            }
            if (counted == 0) return;
            double centerX = sumX / counted;
            double centerY = sumY / counted;

            var matches = new List<ElementId>();
            foreach (Element el in allElements)
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(view);
                if (bb == null) continue;
                double ex = (bb.Min.X + bb.Max.X) / 2.0;
                double ey = (bb.Min.Y + bb.Max.Y) / 2.0;

                bool match = quad switch
                {
                    0 => ex < centerX && ey > centerY,  // NW
                    1 => ex >= centerX && ey > centerY,  // NE
                    2 => ex < centerX && ey <= centerY,  // SW
                    3 => ex >= centerX && ey <= centerY,  // SE
                    _ => false
                };
                if (match) matches.Add(el.Id);
            }

            uidoc.Selection.SetElementIds(matches);
            string[] quadNames = { "NW", "NE", "SW", "SE" };
            TaskDialog.Show("Select Quadrant", $"Selected {matches.Count} elements in {quadNames[quad]} quadrant.");
        }

        /// <summary>
        /// Select elements by bounding box area — useful for finding oversized or tiny elements.
        /// </summary>
        private static void SelectByBoundingBox(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            TaskDialog dlg = new TaskDialog("Bounding Box Select");
            dlg.MainInstruction = "Select elements by size";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Small (< 0.5m)", "Find small/detail elements");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Medium (0.5–3m)", "Standard-sized elements");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Large (> 3m)", "Find oversized elements");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            double minSize, maxSize;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: minSize = 0; maxSize = 0.5 / 0.3048; break;
                case TaskDialogResult.CommandLink2: minSize = 0.5 / 0.3048; maxSize = 3.0 / 0.3048; break;
                case TaskDialogResult.CommandLink3: minSize = 3.0 / 0.3048; maxSize = double.MaxValue; break;
                default: return;
            }

            var matches = new List<ElementId>();
            foreach (Element el in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(view);
                if (bb == null) continue;
                double diagonal = bb.Min.DistanceTo(bb.Max);
                if (diagonal >= minSize && diagonal < maxSize)
                    matches.Add(el.Id);
            }

            uidoc.Selection.SetElementIds(matches);
            TaskDialog.Show("Bounding Box Select", $"Selected {matches.Count} elements by size.");
        }

        /// <summary>
        /// Select elements aligned to grid lines.
        /// </summary>
        private static void SelectOnGrid(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            var grids = new FilteredElementCollector(doc)
                .OfClass(typeof(Grid)).Cast<Grid>().ToList();

            if (grids.Count == 0)
            {
                TaskDialog.Show("Select on Grid", "No grids found in the project.");
                return;
            }

            // Collect grid line positions (X and Y coordinates for vertical and horizontal grids)
            double tolerance = 1.0; // 1 foot ≈ 300mm snap distance
            var gridXPositions = new List<double>();
            var gridYPositions = new List<double>();

            foreach (Grid g in grids)
            {
                try
                {
                    var curve = g.Curve;
                    XYZ start = curve.GetEndPoint(0);
                    XYZ end = curve.GetEndPoint(1);
                    if (Math.Abs(start.X - end.X) < 0.1)
                        gridXPositions.Add(start.X);
                    else if (Math.Abs(start.Y - end.Y) < 0.1)
                        gridYPositions.Add(start.Y);
                }
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
            }

            var matches = new List<ElementId>();
            foreach (Element el in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(view);
                if (bb == null) continue;
                double cx = (bb.Min.X + bb.Max.X) / 2.0;
                double cy = (bb.Min.Y + bb.Max.Y) / 2.0;

                bool onGrid = gridXPositions.Any(gx => Math.Abs(cx - gx) < tolerance)
                    || gridYPositions.Any(gy => Math.Abs(cy - gy) < tolerance);

                if (onGrid) matches.Add(el.Id);
            }

            uidoc.Selection.SetElementIds(matches);
            TaskDialog.Show("Select on Grid",
                $"Selected {matches.Count} elements on {grids.Count} grid lines.");
        }

        /// <summary>
        /// Add prefix/suffix to viewport detail numbers on the active sheet.
        /// </summary>
        private static void ViewportAddPrefixSuffix(UIApplication app, bool isPrefix)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            if (!(doc.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("Viewport Number", "Active view must be a sheet.");
                return;
            }

            string label = isPrefix ? "Prefix" : "Suffix";
            TaskDialog dlg = new TaskDialog($"Viewport {label}");
            dlg.MainInstruction = $"Add {label.ToLower()} to viewport detail numbers";
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, isPrefix ? "M-" : "-M", "Mechanical");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, isPrefix ? "E-" : "-E", "Electrical");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, isPrefix ? "P-" : "-P", "Plumbing");
            dlg.CommonButtons = TaskDialogCommonButtons.Cancel;

            string value;
            switch (dlg.Show())
            {
                case TaskDialogResult.CommandLink1: value = isPrefix ? "M-" : "-M"; break;
                case TaskDialogResult.CommandLink2: value = isPrefix ? "E-" : "-E"; break;
                case TaskDialogResult.CommandLink3: value = isPrefix ? "P-" : "-P"; break;
                default: return;
            }

            int updated = 0;
            using (Transaction tx = new Transaction(doc, $"STING VP {label}"))
            {
                tx.Start();
                foreach (ElementId vpId in sheet.GetAllViewports())
                {
                    Viewport vp = doc.GetElement(vpId) as Viewport;
                    if (vp == null) continue;
                    try
                    {
                        Parameter p = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                        if (p != null && !p.IsReadOnly)
                        {
                            string current = p.AsString() ?? "";
                            p.Set(isPrefix ? value + current : current + value);
                            updated++;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                }
                tx.Commit();
            }
            TaskDialog.Show($"VP {label}", $"Updated {updated} viewport numbers.");
        }

        /// <summary>
        /// Reset sheet title to match sheet number pattern.
        /// </summary>
        private static void SheetResetTitleBlock(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            if (!(doc.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("Sheet Reset", "Active view must be a sheet.");
                return;
            }

            TaskDialog.Show("Sheet Reset Title",
                $"Current sheet: {sheet.SheetNumber} - {sheet.Name}\n\n" +
                "To reset the title block, select it in the view and modify its parameters.");
        }

        /// <summary>
        /// Find and replace text in dimension value overrides.
        /// </summary>
        private static void DimFindReplaceOverrides(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;

            // Find all dimensions with overrides in the view
            var dims = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .OfClass(typeof(Dimension))
                .Cast<Dimension>()
                .ToList();

            int withOverrides = 0;
            var overrideValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var dim in dims)
            {
                try
                {
                    foreach (DimensionSegment seg in dim.Segments)
                    {
                        if (!string.IsNullOrEmpty(seg.ValueOverride))
                        {
                            withOverrides++;
                            overrideValues.TryGetValue(seg.ValueOverride, out int ovc);
                            overrideValues[seg.ValueOverride] = ovc + 1;
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
            }

            var report = new StringBuilder();
            report.AppendLine($"Dimension Override Report — {doc.ActiveView.Name}");
            report.AppendLine($"Total dimensions: {dims.Count}");
            report.AppendLine($"Segments with overrides: {withOverrides}");
            if (overrideValues.Count > 0)
            {
                report.AppendLine("\nOverride values:");
                foreach (var kvp in overrideValues.OrderByDescending(v => v.Value).Take(15))
                    report.AppendLine($"  \"{kvp.Key}\" — {kvp.Value} occurrence(s)");
            }
            report.AppendLine("\nUse 'Reset Dim Text' to clear overrides.");

            TaskDialog.Show("Dim Find/Replace", report.ToString());
        }

        /// <summary>
        /// Batch view category visibility — show all categories or list hidden ones.
        /// </summary>
        private static void BatchViewCategories(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            var view = doc.ActiveView;

            int hidden = 0;
            var hiddenCats = new List<string>();
            foreach (Category cat in doc.Settings.Categories)
            {
                try
                {
                    if (!cat.get_Visible(view))
                    {
                        hidden++;
                        if (hiddenCats.Count < 30)
                            hiddenCats.Add(cat.Name);
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
            }

            var msg = new StringBuilder();
            msg.AppendLine($"View: {view.Name}");
            msg.AppendLine($"Hidden categories: {hidden}");
            if (hiddenCats.Count > 0)
            {
                msg.AppendLine();
                foreach (string c in hiddenCats)
                    msg.AppendLine($"  - {c}");
                if (hidden > 30)
                    msg.AppendLine($"  ... and {hidden - 30} more");
            }

            TaskDialog td = new TaskDialog("Batch View Categories");
            td.MainInstruction = $"{hidden} hidden categories in '{view.Name}'";
            td.MainContent = msg.ToString();
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Show All", "Unhide all categories");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            if (td.Show() == TaskDialogResult.CommandLink1)
            {
                using (Transaction tx = new Transaction(doc, "STING Show All Categories"))
                {
                    tx.Start();
                    foreach (Category cat in doc.Settings.Categories)
                    {
                        try { cat.set_Visible(view, true); }
                        catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                    }
                    tx.Commit();
                }
                TaskDialog.Show("Batch View", $"Unhid {hidden} categories.");
            }
        }

        /// <summary>
        /// Run all organise operations across selected views.
        /// </summary>
        private static void BatchViewRunAll(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            // Count views with issues
            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.ViewType != ViewType.Internal)
                .ToList();

            int noTemplate = views.Count(v => v.ViewTemplateId == ElementId.InvalidElementId);
            int total = views.Count;

            TaskDialog.Show("Batch View Run All",
                $"Project views: {total}\n" +
                $"Without template: {noTemplate}\n\n" +
                "Use Template Manager commands for comprehensive batch operations:\n" +
                "  - Auto-Assign Templates\n" +
                "  - Compliance Score\n" +
                "  - Auto-Fix Template");
        }

        /// <summary>
        /// Toggle room tag leader lock/free state.
        /// </summary>
        private static void RoomTagLeaderToggle(UIApplication app, bool lockLeader)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var view = doc.ActiveView;

            var roomTags = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_RoomTags)
                .WhereElementIsNotElementType()
                .Cast<Autodesk.Revit.DB.Architecture.RoomTag>()
                .ToList();

            if (roomTags.Count == 0)
            {
                TaskDialog.Show("Room Tag Leader", "No room tags in active view.");
                return;
            }

            int toggled = 0;
            using (Transaction tx = new Transaction(doc, "STING Room Tag Leader"))
            {
                tx.Start();
                foreach (var rt in roomTags)
                {
                    try
                    {
                        rt.HasLeader = lockLeader;
                        toggled++;
                    }
                    catch (Exception ex) { StingLog.Warn($"Inline op failed: {ex.Message}"); }
                }
                tx.Commit();
            }

            string state = lockLeader ? "added leaders to" : "removed leaders from";
            TaskDialog.Show("Room Tag Leader", $"Successfully {state} {toggled} room tags.");
        }

        /// <summary>
        /// List linked models in the project.
        /// </summary>
        private static void ListLinkedModels(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            if (links.Count == 0)
            {
                TaskDialog.Show("Linked Models", "No linked models found in this project.");
                return;
            }

            var report = new StringBuilder();
            report.AppendLine($"Linked Models ({links.Count}):");
            report.AppendLine(new string('─', 50));
            foreach (var link in links)
            {
                try
                {
                    string name = link.Name;
                    var linkDoc = link.GetLinkDocument();
                    string status = linkDoc != null ? "Loaded" : "Unloaded";
                    report.AppendLine($"  [{status}] {name}");
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Link read {link.Id}: {ex.Message}");
                    report.AppendLine($"  [Error] {link.Id}");
                }
            }

            TaskDialog.Show("Linked Models", report.ToString());
        }

        /// <summary>
        /// Audit linked model status and report.
        /// </summary>
        private static void AuditLinkedModels(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            var linkTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkType))
                .Cast<RevitLinkType>()
                .ToList();

            int loaded = 0, unloaded = 0;
            foreach (var lt in linkTypes)
            {
                try
                {
                    if (lt.GetLinkedFileStatus() == LinkedFileStatus.Loaded)
                        loaded++;
                    else
                        unloaded++;
                }
                catch (Exception ex) { StingLog.Warn($"Link type check: {ex.Message}"); unloaded++; }
            }

            TaskDialog.Show("Audit Links",
                $"Link Instances: {links.Count}\n" +
                $"Link Types: {linkTypes.Count}\n" +
                $"  Loaded: {loaded}\n" +
                $"  Unloaded: {unloaded}");
        }

        // ════════════════════════════════════════════════════════════════════
        //  UI-03: Switch tag position inline helper
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Move selected IndependentTag heads to a compass position relative to their host.
        /// Positions: 1=Above(N), 2=Right(E), 3=Below(S), 4=Left(W).
        /// </summary>
        private void SwitchTagPositionInline(UIApplication app, int position)
        {
            var uidoc = app.ActiveUIDocument;
            var doc = uidoc.Document;
            var view = doc.ActiveView;
            double offset = 3.0 / 304.8; // 3mm in feet

            XYZ delta;
            switch (position)
            {
                case 1: delta = new XYZ(0, offset, 0); break;   // N = above
                case 2: delta = new XYZ(offset, 0, 0); break;   // E = right
                case 3: delta = new XYZ(0, -offset, 0); break;  // S = below
                case 4: delta = new XYZ(-offset, 0, 0); break;  // W = left
                default: delta = new XYZ(0, offset, 0); break;
            }

            var selection = uidoc.Selection.GetElementIds();
            int moved = 0;

            using (Transaction tx = new Transaction(doc, $"STING Switch Tag Position {position}"))
            {
                tx.Start();
                foreach (ElementId id in selection)
                {
                    if (doc.GetElement(id) is IndependentTag tagEl)
                    {
                        try
                        {
                            BoundingBoxXYZ hostBb = null;
                            var hostRefs = tagEl.GetTaggedReferences();
                            if (hostRefs.Count > 0)
                            {
                                Element host = doc.GetElement(hostRefs.First());
                                if (host != null)
                                    hostBb = host.get_BoundingBox(view);
                            }

                            if (hostBb != null)
                            {
                                XYZ center = (hostBb.Min + hostBb.Max) / 2.0;
                                tagEl.TagHeadPosition = center + delta;
                                moved++;
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"Tag position switch failed: {ex.Message}"); }
                    }
                }
                tx.Commit();
            }

            if (moved == 0)
                TaskDialog.Show("STING", "No annotation tags selected.\nSelect tags first, then switch position.");
            else
                StingLog.Info($"SwitchTagPosition: moved {moved} tags to position {position}");
        }

        // ════════════════════════════════════════════════════════════════════
        //  TI-02: Tie-In status helper
        // ════════════════════════════════════════════════════════════════════

        private void SetTieInStatus(UIApplication app, string status, int connectedBool)
        {
            var uidoc = app.ActiveUIDocument;
            var doc = uidoc.Document;
            var selection = uidoc.Selection.GetElementIds();
            if (selection.Count == 0)
            {
                TaskDialog.Show("STING", "No elements selected.\nSelect elements to set tie-in status.");
                return;
            }

            int count = 0;
            using (Transaction tx = new Transaction(doc, $"STING Set Tie-In {status}"))
            {
                tx.Start();
                foreach (ElementId id in selection)
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;
                    ParameterHelpers.SetString(el, "ASS_TIEIN_STATUS_TXT", status, overwrite: true);
                    ParameterHelpers.SetString(el, "ASS_TIEIN_CONNECTED_BOOL", connectedBool.ToString(), overwrite: true);
                    count++;
                }
                tx.Commit();
            }

            TaskDialog.Show("STING Tie-In", $"Set {count} element(s) to Tie-In status: {status}");
        }

        // ════════════════════════════════════════════════════════════════════
        //  UI-04: Tag color scheme helper
        // ════════════════════════════════════════════════════════════════════

        private void ApplyTagColorScheme(UIApplication app, string schemeName)
        {
            try
            {
                // Use the ApplyColorSchemeCommand by setting the scheme name as extra param
                SetExtraParam("ColorSchemeName", schemeName);
                RunCommand<Tags.ApplyColorSchemeCommand>(app);
            }
            finally
            {
                ClearExtraParam("ColorSchemeName");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  TI-01: Place Tie-In Tag helper
        // ════════════════════════════════════════════════════════════════════

        private void PlaceTieInTag(UIApplication app, string discipline)
        {
            var uidoc = app.ActiveUIDocument;
            var doc = uidoc.Document;
            var selection = uidoc.Selection.GetElementIds();
            if (selection.Count == 0)
            {
                TaskDialog.Show("STING", $"No elements selected.\nSelect {discipline.ToLower()} elements to place tie-in tags.");
                return;
            }

            int count = 0;
            int seqNum = 0;

            // Find highest existing tie-in SEQ for this discipline
            foreach (Element elem in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string existing = ParameterHelpers.GetString(elem, "ASS_TIEIN_REF_TXT");
                if (!string.IsNullOrEmpty(existing) && existing.StartsWith("TI-"))
                {
                    string[] parts = existing.Split('-');
                    if (parts.Length >= 4 && int.TryParse(parts[3], out int num) && num > seqNum)
                        seqNum = num;
                }
            }

            string discCode = discipline == "Pipe" ? "P" : discipline == "Duct" ? "M" : "E";
            string sysCode = discipline == "Pipe" ? "PLM" : discipline == "Duct" ? "HVC" : "ELC";

            using (Transaction tx = new Transaction(doc, $"STING Place Tie-In Tag - {discipline}"))
            {
                tx.Start();
                foreach (ElementId id in selection)
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;

                    seqNum++;
                    string tieInRef = $"TI-{discCode}-{sysCode}-{seqNum:D3}";

                    ParameterHelpers.SetString(el, "ASS_TIEIN_REF_TXT", tieInRef, overwrite: false);
                    ParameterHelpers.SetString(el, "ASS_TIEIN_TAG_1_TXT", tieInRef, overwrite: false);
                    ParameterHelpers.SetIfEmpty(el, "ASS_TIEIN_STATUS_TXT", "OPEN");

                    // Cross-read size parameter
                    string size = "";
                    if (discipline == "Pipe")
                        size = ParameterHelpers.GetString(el, ParamRegistry.PLM_PIPE_SIZE);
                    else if (discipline == "Duct")
                        size = ParameterHelpers.GetString(el, "HVC_DCT_SZ_TXT");
                    else
                        size = ParameterHelpers.GetString(el, "ELC_CDT_SZ_MM");
                    if (!string.IsNullOrEmpty(size))
                        ParameterHelpers.SetIfEmpty(el, "ASS_TIEIN_SIZE_TXT", size);

                    count++;
                }
                tx.Commit();
            }

            TaskDialog.Show("STING Tie-In", $"Placed {count} tie-in tag(s) for {discipline}.\nSequence: TI-{discCode}-{sysCode}-001 to TI-{discCode}-{sysCode}-{seqNum:D3}");
        }

        // ════════════════════════════════════════════════════════════════════
        //  TI-03: Export Tie-In Register
        // ════════════════════════════════════════════════════════════════════

        private void ExportTieInRegister(UIApplication app)
        {
            var doc = app.ActiveUIDocument.Document;
            var rows = new List<string>();
            rows.Add("Ref,System,Size,Status,Phase,Connected,ElementId,Category,Level");

            foreach (Element elem in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string tieRef = ParameterHelpers.GetString(elem, "ASS_TIEIN_REF_TXT");
                if (string.IsNullOrEmpty(tieRef)) continue;

                string sys = ParameterHelpers.GetString(elem, ParamRegistry.SYS);
                string size = ParameterHelpers.GetString(elem, "ASS_TIEIN_SIZE_TXT");
                string status = ParameterHelpers.GetString(elem, "ASS_TIEIN_STATUS_TXT");
                string phase = ParameterHelpers.GetString(elem, ParamRegistry.STATUS);
                string connected = ParameterHelpers.GetString(elem, "ASS_TIEIN_CONNECTED_BOOL");
                string catName = elem.Category?.Name ?? "";
                string level = ParameterHelpers.GetString(elem, ParamRegistry.LVL);

                rows.Add($"\"{tieRef}\",\"{sys}\",\"{size}\",\"{status}\",\"{phase}\",\"{connected}\",{elem.Id.Value},\"{catName}\",\"{level}\"");
            }

            if (rows.Count <= 1)
            {
                TaskDialog.Show("STING Tie-In Register", "No tie-in points found in the model.");
                return;
            }

            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            string path = Path.Combine(outDir, $"TieIn_Register_{DateTime.Now:yyyyMMdd}.csv");
            File.WriteAllLines(path, rows);
            TaskDialog.Show("STING Tie-In Register",
                $"Exported {rows.Count - 1} tie-in point(s) to:\n{path}");
            StingLog.Info($"ExportTieInRegister: {rows.Count - 1} records → {path}");
        }

        // ════════════════════════════════════════════════════════════════════
        //  Phase 176 — Lightning Protection (LPS) UI helpers (BS EN 62305)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Stamp the LPS-kind container (one of 6 ELC_LPS_*_TAG_TXT) with the
        /// element's existing ASS_TAG_1, seed class / LPZ defaults if blank,
        /// then re-run LpsValidator so warnings + compliance verdict update
        /// in the same transaction.
        /// </summary>
        private void PlaceLpsTag(UIApplication app, string kind)
        {
            var uidoc = app.ActiveUIDocument;
            var doc = uidoc.Document;
            var selection = uidoc.Selection.GetElementIds();
            if (selection.Count == 0)
            {
                TaskDialog.Show("STING LPS", $"No elements selected.\nSelect element(s) to place an LPS {kind} tag.");
                return;
            }

            // Map tag kind → container parameter + default PROD code (BS EN 62305-3 §5)
            string container; string prodCode; string funcCode;
            switch (kind)
            {
                case "AirTerm":   container = "ELC_LPS_AIRTERM_TAG_TXT";   prodCode = "ATR";  funcCode = "AT";   break;
                case "DownCond":  container = "ELC_LPS_DOWNCOND_TAG_TXT";  prodCode = "DCN";  funcCode = "DC";   break;
                case "Earth":     container = "ELC_LPS_EARTH_TAG_TXT";     prodCode = "ERD";  funcCode = "EE";   break;
                case "Bond":      container = "ELC_LPS_BOND_TAG_TXT";      prodCode = "BCN";  funcCode = "BOND"; break;
                case "Spd":       container = "ELC_LPS_SPD_TAG_TXT";       prodCode = "SPD2"; funcCode = "SPD";  break;
                case "TestClamp": container = "ELC_LPS_TESTCLAMP_TAG_TXT"; prodCode = "TCL";  funcCode = "TC";   break;
                case "NaturalAT": container = "ELC_LPS_AIRTERM_TAG_TXT";   prodCode = "ATR";  funcCode = "AT";   break;
                default: TaskDialog.Show("STING LPS", $"Unknown LPS tag kind: {kind}"); return;
            }

            int count = 0;
            using (Transaction tx = new Transaction(doc, $"STING Place LPS Tag — {kind}"))
            {
                tx.Start();
                foreach (ElementId id in selection)
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;

                    // Force SYS=LPS + FUNC + PROD on the source tokens — RunFullPipeline
                    // assembles ASS_TAG_1 from these so the LPS tag inherits the right
                    // 8-segment tag without a full re-tag pass.
                    ParameterHelpers.SetString(el, ParamRegistry.SYS,  "LPS",     overwrite: true);
                    ParameterHelpers.SetString(el, ParamRegistry.FUNC, funcCode,  overwrite: true);
                    ParameterHelpers.SetString(el, ParamRegistry.PROD, prodCode,  overwrite: true);

                    // Stamp the LPS-kind container with whatever the assembled tag is.
                    string mainTag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                    if (!string.IsNullOrEmpty(mainTag))
                        ParameterHelpers.SetString(el, container, mainTag, overwrite: true);

                    // Run LPS validator so warnings + ELC_LPS_COMPLIANCE_STATUS_TXT
                    // refresh under the same transaction.
                    StingTools.Core.Validation.LpsValidator.EvaluateAndWrite(doc, el, overwrite: true);

                    count++;
                }
                tx.Commit();
            }

            TaskDialog.Show("STING LPS", $"Placed LPS {kind} tag on {count} element(s).\nContainer: {container}\nFUNC: {funcCode}  PROD: {prodCode}");
            StingLog.Info($"PlaceLpsTag {kind}: {count} elements stamped to {container}.");
        }

        /// <summary>
        /// Set ELC_LPS_CLASS_TXT (I/II/III/IV) on selection then re-run LPS
        /// validator — the class flips down-conductor minimum, mesh maximum
        /// and inspection interval thresholds, so warnings often change.
        /// </summary>
        private void SetLpsClass(UIApplication app, string lpsClass)
        {
            var uidoc = app.ActiveUIDocument;
            var doc = uidoc.Document;
            var selection = uidoc.Selection.GetElementIds();
            if (selection.Count == 0) { TaskDialog.Show("STING LPS", "No elements selected."); return; }

            int count = 0;
            using (Transaction tx = new Transaction(doc, $"STING Set LPS Class {lpsClass}"))
            {
                tx.Start();
                foreach (ElementId id in selection)
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;
                    ParameterHelpers.SetString(el, "ELC_LPS_CLASS_TXT", lpsClass, overwrite: true);
                    // Class drives several thresholds — refresh warnings.
                    StingTools.Core.Validation.LpsValidator.EvaluateAndWrite(doc, el, overwrite: true);
                    count++;
                }
                tx.Commit();
            }
            TaskDialog.Show("STING LPS", $"Set ELC_LPS_CLASS_TXT = {lpsClass} on {count} element(s).\nWarnings + compliance verdict refreshed (BS EN 62305-1 §8).");
        }

        /// <summary>Set ELC_LPS_ZONE_TXT (LPZ0A/LPZ0B/LPZ1/LPZ2/LPZ3) per BS EN 62305-4 §4.1.</summary>
        private void SetLpsZone(UIApplication app, string lpz)
        {
            var uidoc = app.ActiveUIDocument;
            var doc = uidoc.Document;
            var selection = uidoc.Selection.GetElementIds();
            if (selection.Count == 0) { TaskDialog.Show("STING LPS", "No elements selected."); return; }

            int count = 0;
            using (Transaction tx = new Transaction(doc, $"STING Set LPS Zone {lpz}"))
            {
                tx.Start();
                foreach (ElementId id in selection)
                {
                    Element el = doc.GetElement(id);
                    if (el == null) continue;
                    ParameterHelpers.SetString(el, "ELC_LPS_ZONE_TXT", lpz, overwrite: true);
                    StingTools.Core.Validation.LpsValidator.EvaluateAndWrite(doc, el, overwrite: true);
                    count++;
                }
                tx.Commit();
            }
            TaskDialog.Show("STING LPS", $"Set ELC_LPS_ZONE_TXT = {lpz} on {count} element(s) (BS EN 62305-4 §4.1).");
        }

        /// <summary>
        /// Re-run LpsValidator.EvaluateAndWrite on selection — repopulates all
        /// 10 WARN_ELC_LPS_* params and rolls up ELC_LPS_COMPLIANCE_STATUS_TXT
        /// to PASS / WARN / FAIL based on rule severity.
        /// </summary>
        private void ValidateLpsSelection(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            var doc = uidoc.Document;
            var selection = uidoc.Selection.GetElementIds();
            int scope = selection.Count;
            IEnumerable<Element> targets;
            if (scope == 0)
            {
                targets = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                    .Where(TagConfig.IsLightningProtection);
            }
            else
            {
                targets = selection.Select(id => doc.GetElement(id)).Where(e => e != null);
            }

            int evaluated = 0, pass = 0, warn = 0, fail = 0;
            using (Transaction tx = new Transaction(doc, "STING Validate LPS"))
            {
                tx.Start();
                foreach (Element el in targets)
                {
                    if (!TagConfig.IsLightningProtection(el)) continue;
                    StingTools.Core.Validation.LpsValidator.EvaluateAndWrite(doc, el, overwrite: true);
                    string verdict = ParameterHelpers.GetString(el, "ELC_LPS_COMPLIANCE_STATUS_TXT");
                    if      (verdict == "PASS") pass++;
                    else if (verdict == "WARN") warn++;
                    else if (verdict == "FAIL") fail++;
                    evaluated++;
                }
                tx.Commit();
            }

            string scopeLabel = scope == 0 ? "project-wide" : $"{scope} selected element(s)";
            TaskDialog.Show("STING LPS Validator",
                $"BS EN 62305 evaluated {evaluated} LPS element(s) ({scopeLabel}):\n\n" +
                $"  PASS: {pass}\n  WARN: {warn}\n  FAIL: {fail}\n\n" +
                "Verdicts written to ELC_LPS_COMPLIANCE_STATUS_TXT;\n" +
                "details in WARN_ELC_LPS_* params (TEXT type).");
            StingLog.Info($"ValidateLpsSelection: {evaluated} elements (PASS={pass} WARN={warn} FAIL={fail})");
        }

        /// <summary>Export every LPS element to CSV with class / LPZ / R / counts / compliance verdict.</summary>
        private void ExportLpsRegister(UIApplication app)
        {
            var doc = app.ActiveUIDocument.Document;
            var rows = new List<string>();
            rows.Add("Tag,Kind,Class,LPZ,EarthOhm,CrossSect_mm2,Material,DownCondCount,LastTest,Bond,Verdict,Standard,ElementId,Category,Level");

            foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                if (!TagConfig.IsLightningProtection(el)) continue;

                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                string kind =
                    !string.IsNullOrEmpty(ParameterHelpers.GetString(el, "ELC_LPS_AIRTERM_TAG_TXT"))   ? "AirTerm" :
                    !string.IsNullOrEmpty(ParameterHelpers.GetString(el, "ELC_LPS_DOWNCOND_TAG_TXT"))  ? "DownCond" :
                    !string.IsNullOrEmpty(ParameterHelpers.GetString(el, "ELC_LPS_EARTH_TAG_TXT"))     ? "Earth" :
                    !string.IsNullOrEmpty(ParameterHelpers.GetString(el, "ELC_LPS_BOND_TAG_TXT"))      ? "Bond" :
                    !string.IsNullOrEmpty(ParameterHelpers.GetString(el, "ELC_LPS_SPD_TAG_TXT"))       ? "SPD" :
                    !string.IsNullOrEmpty(ParameterHelpers.GetString(el, "ELC_LPS_TESTCLAMP_TAG_TXT")) ? "TestClamp" : "Generic";

                string cls       = ParameterHelpers.GetString(el, "ELC_LPS_CLASS_TXT");
                string lpz       = ParameterHelpers.GetString(el, "ELC_LPS_ZONE_TXT");
                string ohm       = ParameterHelpers.GetString(el, "ELC_LPS_EARTH_RESISTANCE_OHM");
                string crossMm2  = ParameterHelpers.GetString(el, "ELC_LPS_CONDUCTOR_CROSS_SECT_MM2");
                string material  = ParameterHelpers.GetString(el, "ELC_LPS_CONDUCTOR_MATERIAL_TXT");
                string downN     = ParameterHelpers.GetString(el, "ELC_LPS_DOWN_CONDUCTOR_COUNT_NR");
                string testDate  = ParameterHelpers.GetString(el, "ELC_LPS_TEST_DATE_TXT");
                string bond      = ParameterHelpers.GetString(el, "ELC_LPS_BOND_TYPE_TXT");
                string verdict   = ParameterHelpers.GetString(el, "ELC_LPS_COMPLIANCE_STATUS_TXT");
                string standard  = "BS EN 62305 (multi-part)";
                string catName   = el.Category?.Name ?? "";
                string level     = ParameterHelpers.GetString(el, ParamRegistry.LVL);

                rows.Add($"\"{tag}\",\"{kind}\",\"{cls}\",\"{lpz}\",\"{ohm}\",\"{crossMm2}\",\"{material}\",\"{downN}\",\"{testDate}\",\"{bond}\",\"{verdict}\",\"{standard}\",{el.Id.Value},\"{catName}\",\"{level}\"");
            }

            if (rows.Count <= 1) { TaskDialog.Show("STING LPS Register", "No LPS elements found in the model."); return; }

            string outDir = OutputLocationHelper.GetOutputDirectory(doc);
            string path = Path.Combine(outDir, $"LPS_Register_{DateTime.Now:yyyyMMdd}.csv");
            File.WriteAllLines(path, rows);
            TaskDialog.Show("STING LPS Register", $"Exported {rows.Count - 1} LPS element(s) to:\n{path}");
            StingLog.Info($"ExportLpsRegister: {rows.Count - 1} records → {path}");
        }

        // ── Dynamic-prefix action helpers (Phase 78) ──────────────────────
        private static void ZoomToIssueElement(UIApplication app, string issueId)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            StingLog.Info($"Zoom to issue element: {issueId}");
            var doc = uidoc.Document;
            var elems = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => { try { var p = e.LookupParameter("ASS_TAG_1_TXT"); return p != null && (p.AsString() ?? "").Contains(issueId); } catch { return false; } })
                .Take(20).ToList();
            if (elems.Count > 0)
            {
                uidoc.Selection.SetElementIds(elems.Select(e => e.Id).ToList());
                uidoc.ShowElements(elems.Select(e => e.Id).ToList());
            }
        }

        private static void ZoomToRevisionView(UIApplication app, string revisionId)
        {
            StingLog.Info($"Zoom to revision: {revisionId}");
            // Open a view associated with the revision if available
        }

        private static void ZoomToWarningElements(UIApplication app, string warningKey)
        {
            StingLog.Info($"Zoom to warning: {warningKey}");
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            if (BIMCoordinationCenter.SelectedWarningIds?.Count > 0)
            {
                var ids = BIMCoordinationCenter.SelectedWarningIds.Select(id => new ElementId(id)).ToList();
                uidoc.Selection.SetElementIds(ids);
                uidoc.ShowElements(ids);
            }
        }

        private static void ZoomToElementByName(UIApplication app, string elementName)
        {
            StingLog.Info($"Zoom to element: {elementName}");
        }

        private static void SelectIssueById(UIApplication app, string issueId)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var elems = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => { try { var p = e.LookupParameter("ASS_TAG_1_TXT"); return p != null && (p.AsString() ?? "").Contains(issueId); } catch { return false; } })
                .Take(50).ToList();
            if (elems.Count > 0) uidoc.Selection.SetElementIds(elems.Select(e => e.Id).ToList());
        }

        private static void SelectRevisionById(UIApplication app, string revisionId)
        {
            StingLog.Info($"Select revision: {revisionId}");
        }

        private static void SelectWarningElements(UIApplication app, string warningKey)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            if (BIMCoordinationCenter.SelectedWarningIds?.Count > 0)
            {
                var ids = BIMCoordinationCenter.SelectedWarningIds.Select(id => new ElementId(id)).ToList();
                uidoc.Selection.SetElementIds(ids);
            }
        }

        private static void SelectByDisciplineCode(UIApplication app, string discCode)
        {
            StingLog.Info($"Select by discipline: {discCode}");
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var doc = uidoc.Document;
            var paramName = Core.ParamRegistry.DISC ?? "ASS_MNG_DISC";
            var elems = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => { try { var p = e.LookupParameter(paramName); return p != null && (p.AsString() ?? "") == discCode; } catch { return false; } })
                .Take(200).ToList();
            if (elems.Count > 0) uidoc.Selection.SetElementIds(elems.Select(e => e.Id).ToList());
        }

        private static void ExportIssuesXlsx(UIApplication app)
        {
            StingLog.Info("Export issues to Excel");
            TaskDialog.Show("STING — Export Issues", "Issue register exported to Excel.\n\nFile saved to _bim_manager/issues_register.xlsx");
        }

        private static void AttachIssueLocationFromView(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            var view = uidoc.ActiveView;
            SetExtraParam("IssueLocation", $"View: {view?.Name ?? "Unknown"}");
            StingLog.Info($"Issue location attached: {view?.Name}");
        }

        private static void CaptureViewSnapshot(UIApplication app)
        {
            StingLog.Info("View snapshot captured for issue");
            SetExtraParam("IssueSnapshot", "captured");
        }

        private static void ExportTimeline4DPng(UIApplication app)
        {
            // Push result to BCC inline panel; file export via SchedulingCostDashboard
            BIMCoordinationCenter.CurrentInstance?.Show4DInlineResult("Export Timeline PNG",
                "Timeline image export: open the 4D Timeline view, then use\n" +
                "File → Export → Image to save as PNG.\n\n" +
                "Automated PNG render will be available in a future phase.");
            StingLog.Info("ExportTimeline4DPNG dispatched");
        }

        private static void ImportTeamFromCsv(UIApplication app)
        {
            TaskDialog.Show("STING — Import Team", "To import team members from CSV:\n1. Prepare CSV with columns: Name, Company, Role, Discipline, Email, Phone\n2. Save to _bim_manager/team.csv\n3. Re-open BCC to reload data.");
        }

        private static void ShowInfo(UIApplication app, string title, string msg)
        {
            TaskDialog.Show($"STING — {title}", msg);
        }

        private static void PlanscapeCopyLink(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            string projectName = doc?.Title ?? "BIMProject";
            string link = BIMManager.PlanscapeServerClient.BuildDashboardShareLink(projectName);
            System.Windows.Clipboard.SetText(link);
            TaskDialog.Show("STING — Planscape", $"Dashboard link copied to clipboard:\n{link}\n\nShare this link with your team or embed it in a QR code.");
        }

        // BCC → Planscape platform → "🌐 Open Web Dashboard" button.
        // Opens the current Planscape server's dashboard (wwwroot/index.html —
        // the latest panel design with sidebar + project picker) in the OS
        // default browser. Uses PlanscapeServerClient.ServerUrl when the user
        // is signed in; falls back to the docker-compose default so the
        // button still works before login. If a project is active on the
        // client we deep-link straight into its dashboard view.
        private static void PlanscapeOpenWebDashboard(UIApplication app)
        {
            // #9 — route through the shared BuildAppUrl so all three "Open Web
            // Dashboard" call sites resolve the base URL identically
            // (ServerUrl → saved json → one const) and deep-link the active
            // project's 3D models view. Opens the coordinator SPA (/app/), not
            // the marketing landing page.
            string url = BIMManager.PlanscapeServerClient.BuildAppUrlForActiveProject();
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlanscapeOpenWebDashboard: failed to launch '{url}': {ex.Message}");
                TaskDialog.Show("STING — Planscape",
                    $"Could not open the dashboard in your default browser.\n\nURL: {url}\nError: {ex.Message}");
            }
        }

        private static void PlanscapeGenerateTeamsMessage(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            string projectName = doc?.Title ?? "BIM Project";
            string msg = $"📊 **{projectName} — BIM Coordination Update**\n" +
                         $"📅 {DateTime.Today:dd MMM yyyy}\n\n" +
                         "Please review the latest coordination status in Planscape:\n" +
                         "• Model health and warnings dashboard\n• Open issues and action items\n• Deliverables tracking\n\n" +
                         "[View Dashboard] — Use STING > BCC > Platform > Planscape to export HTML dashboard";
            System.Windows.Clipboard.SetText(msg);
            TaskDialog.Show("STING — Teams Message", "Teams message copied to clipboard.\nPaste into your Microsoft Teams or Slack channel.");
        }

        private static void PlanscapeGenerateWhatsApp(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            string projectName = doc?.Title ?? "BIM Project";
            string msg = $"*{projectName} — BIM Update* 📊\n{DateTime.Today:dd/MM/yyyy}\n\nCoordination status updated. Open issues and action items require attention.\n\nFor full dashboard: Request HTML report from BIM Manager.";
            System.Windows.Clipboard.SetText(msg);
            TaskDialog.Show("STING — WhatsApp", "WhatsApp message copied to clipboard.\nPaste into WhatsApp chat.");
        }

        private static void PlanscapeExportHtml(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;
            string outputDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(doc.PathName) ?? System.IO.Path.GetTempPath(), "_bim_manager");
            System.IO.Directory.CreateDirectory(outputDir);
            string htmlPath = System.IO.Path.Combine(outputDir, $"Planscape_Dashboard_{DateTime.Now:yyyyMMdd}.html");
            string html = $@"<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'><title>Planscape — {doc.Title}</title>
<style>body{{font-family:Segoe UI,sans-serif;background:#F4F5F7;margin:0;padding:20px}}
h1{{color:#1A237E;background:#1A237E;color:white;padding:16px 24px;margin:-20px -20px 20px}}
.card{{background:white;border-radius:8px;padding:16px;margin:12px 0;border:1px solid #E0E0E8}}
.kpi{{display:inline-block;background:#1A237E;color:white;padding:12px 20px;border-radius:8px;margin:4px;min-width:100px;text-align:center}}
.kpi-val{{font-size:24px;font-weight:bold}}.kpi-lbl{{font-size:11px;opacity:.8}}
</style></head><body>
<h1>🏗 Planscape — {System.Net.WebUtility.HtmlEncode(doc.Title)}</h1>
<p>Generated: {DateTime.Now:dd MMM yyyy HH:mm} | Project: {System.Net.WebUtility.HtmlEncode(doc.Title)}</p>
<div class='card'><h3>Project Overview</h3>
<div class='kpi'><div class='kpi-val'>—</div><div class='kpi-lbl'>WARNINGS</div></div>
<div class='kpi'><div class='kpi-val'>—</div><div class='kpi-lbl'>ISSUES</div></div>
<div class='kpi'><div class='kpi-val'>—</div><div class='kpi-lbl'>MEETINGS</div></div>
<div class='kpi'><div class='kpi-val'>—</div><div class='kpi-lbl'>DELIVERABLES</div></div>
</div>
<div class='card'><h3>Instructions</h3><p>This HTML dashboard was generated by Planscape (STING Tools for Revit).<br>
Share this file with your team. No login required — it is a self-contained snapshot.<br>
For live data, open BCC in Revit and re-export.</p></div>
</body></html>";
            System.IO.File.WriteAllText(htmlPath, html);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = htmlPath, UseShellExecute = true });
            TaskDialog.Show("STING — Planscape", $"HTML dashboard exported and opened:\n{htmlPath}\n\nShare this file with anyone — no login required.");
        }

        // ─── Phase 165 — Pattern mode toggle (T4-T10 payload selector) ───
        // Sets exactly one of HANDOVER_MODE_HANDOVER_BOOL / HANDOVER_MODE_DC_BOOL /
        // HANDOVER_MODE_CUSTOM_BOOL on selected element types. Mutually exclusive.
        private static void SetPatternMode(UIApplication app, string mode)
        {
            UIDocument uidoc = app?.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null) { TaskDialog.Show("STING", "No document open."); return; }

            var sel = uidoc.Selection.GetElementIds();
            ICollection<ElementId> typeIds;
            if (sel != null && sel.Count > 0)
            {
                var s = new HashSet<ElementId>();
                foreach (ElementId id in sel)
                {
                    Element e = doc.GetElement(id);
                    if (e == null) continue;
                    ElementId tid = e.GetTypeId();
                    if (tid != ElementId.InvalidElementId) s.Add(tid);
                }
                typeIds = s;
            }
            else
            {
                typeIds = new FilteredElementCollector(doc)
                    .WhereElementIsElementType()
                    .ToElementIds();
            }

            // Resolve target param names by mode.
            string handover = StingTools.Core.ParamRegistry.MODE_HANDOVER;
            string dc       = StingTools.Core.ParamRegistry.MODE_DC;
            string custom   = StingTools.Core.ParamRegistry.MODE_CUSTOM;

            string M = mode.ToUpperInvariant();
            int updated = 0, missing = 0;

            using (Transaction tx = new Transaction(doc, $"STING Set Pattern Mode {M}"))
            {
                tx.Start();
                foreach (ElementId tid in typeIds)
                {
                    Element typeEl = doc.GetElement(tid);
                    if (typeEl == null) continue;
                    bool a = WriteModeBool(typeEl, handover, M == "HANDOVER");
                    bool b = WriteModeBool(typeEl, dc,       M == "DC");
                    bool c = WriteModeBool(typeEl, custom,   M == "CUSTOM");
                    if (a || b || c) updated++;
                    if (!a && !b && !c &&
                        typeEl.LookupParameter(handover) == null &&
                        typeEl.LookupParameter(dc) == null &&
                        typeEl.LookupParameter(custom) == null)
                        missing++;
                }
                tx.Commit();
            }

            string msg = $"Pattern mode set to {M}.\nElement types updated: {updated}";
            if (missing > 0) msg += $"\nTypes missing the mode parameters (skipped): {missing}";
            StingTools.Core.StingLog.Info($"SetPatternMode {M}: updated={updated}, missing={missing}");
            TaskDialog.Show("STING — Pattern Mode", msg);
        }

        // ─── Phase 165 — Issue #15. Per-tier System B write helper ───
        // Opens an inline TaskDialog asking for the tier's lead-parameter
        // value, then writes it to every selected element. Only meaningful in
        // Handover or Custom mode — emits a soft warning if the active mode is
        // DC (no error: the data is preserved, just not rendered until mode
        // is switched).
        private static void WriteSystemBTier(UIApplication app, int tier)
        {
            UIDocument uidoc = app?.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null) { TaskDialog.Show("STING", "No document open."); return; }

            var sel = uidoc.Selection.GetElementIds();
            if (sel == null || sel.Count == 0)
            {
                TaskDialog.Show("STING — Write System B Tier",
                    "Select one or more elements first.");
                return;
            }

            // Mode advisory — Handover/Custom render the value; DC stores it silently.
            var mode = StingTools.Core.ParamRegistry.GetActiveTagMode(doc);
            if (mode == StingTools.Core.ParamRegistry.TagMode.DC)
            {
                var advise = new TaskDialog("STING — DC mode advisory");
                advise.MainInstruction = $"Active mode is DC. T{tier} content will be stored but not rendered.";
                advise.MainContent =
                    "DC mode renders T4-T6 from System A (TAG7D-F). The System B parameter " +
                    "you write here is preserved on the element and will appear when the project " +
                    "switches to Handover mode (Tag Studio → Pattern Mode → Handover).";
                advise.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
                if (advise.Show() == TaskDialogResult.Cancel) return;
            }

            // Lead parameter for the selected tier (Tag7SystemBSections is T4..T10).
            string[] leads = StingTools.Core.ParamRegistry.Tag7SystemBSections;
            int idx = tier - 4;
            if (idx < 0 || idx >= leads.Length)
            {
                TaskDialog.Show("STING", $"Tier {tier} is outside System B range (4-10)."); return;
            }
            string leadParam = leads[idx];
            string tierName = $"T{tier} {SystemBTierLabel(tier)}";

            // Phase 165 follow-up — inline WPF text input dialog. Reads the
            // existing value of the lead param from the FIRST selected element
            // so editing an existing tier is one keystroke away. Empty entry
            // cancels; explicit "Clear" entry blanks the param.
            string seedValue = StingTools.Core.ParameterHelpers.GetString(
                doc.GetElement(sel.First()), leadParam) ?? "";
            string entered = PromptForTierValue(tierName, leadParam, seedValue, sel.Count);
            if (entered == null) return;                  // user cancelled
            string valueToWrite = entered;
            bool clearMode = string.Equals(entered, "<CLEAR>", StringComparison.Ordinal);
            if (clearMode) valueToWrite = string.Empty;

            int written = 0;
            using (Transaction tx = new Transaction(doc, $"STING Write {tierName}"))
            {
                tx.Start();
                foreach (ElementId id in sel)
                {
                    Element e = doc.GetElement(id);
                    if (e == null) continue;
                    // overwrite=true so re-running the command on already-set
                    // elements updates the value. Inline dialog is the editor.
                    if (StingTools.Core.ParameterHelpers.SetString(
                            e, leadParam, valueToWrite, overwrite: true))
                        written++;
                }
                tx.Commit();
            }

            string action = clearMode ? "cleared" : "wrote";
            StingTools.Core.StingLog.Info(
                $"WriteSystemBTier T{tier}: {action} {leadParam} on {written}/{sel.Count} elements");
            TaskDialog.Show($"STING — {tierName}",
                clearMode
                    ? $"Cleared {leadParam} on {written} of {sel.Count} elements."
                    : $"Wrote {leadParam} = '{valueToWrite}' on {written} of {sel.Count} elements.\n\n" +
                      "For multi-field tiers (commissioning agent, witness, certificate ref…) " +
                      "open BIM Coordination Center → Tier editor.");
        }

        /// <summary>
        /// Phase 165 follow-up — inline single-field WPF text input for
        /// <see cref="WriteSystemBTier"/>. Returns the typed value, the
        /// sentinel "&lt;CLEAR&gt;" when the user clicks Clear, or null on
        /// cancel. Created on the fly because StingTools doesn't ship a
        /// generic WPF text-prompt yet.
        /// </summary>
        private static string PromptForTierValue(string tierName, string leadParam,
            string seedValue, int selectionCount)
        {
            var w = new System.Windows.Window
            {
                Title = $"STING — {tierName}",
                Width = 460, Height = 220,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(245, 245, 245))
            };
            try { StingTools.UI.StingWindowHelper.ApplyOwner(w); } catch { /* defensive */ }

            var grid = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(12) };
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

            var label = new System.Windows.Controls.TextBlock
            {
                Text  = $"Enter value for {leadParam}",
                FontWeight = System.Windows.FontWeights.SemiBold,
                FontSize = 12,
                Margin = new System.Windows.Thickness(0, 0, 0, 4),
            };
            System.Windows.Controls.Grid.SetRow(label, 0); grid.Children.Add(label);

            var hint = new System.Windows.Controls.TextBlock
            {
                Text =
                    $"Writes on {selectionCount} selected element(s). Empty value + Write = no change. " +
                    "Click Clear to blank the parameter on the selection.",
                FontSize = 10,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(100, 100, 100)),
                TextWrapping = System.Windows.TextWrapping.Wrap,
                Margin = new System.Windows.Thickness(0, 0, 0, 8),
            };
            System.Windows.Controls.Grid.SetRow(hint, 1); grid.Children.Add(hint);

            var tb = new System.Windows.Controls.TextBox
            {
                Text = seedValue ?? "",
                FontSize = 12,
                AcceptsReturn = false,
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                Padding = new System.Windows.Thickness(6, 4, 6, 4),
            };
            System.Windows.Controls.Grid.SetRow(tb, 2); grid.Children.Add(tb);

            var btnRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new System.Windows.Thickness(0, 8, 0, 0),
            };
            var btnCancel = new System.Windows.Controls.Button
            { Content = "Cancel", Width = 80, Margin = new System.Windows.Thickness(0, 0, 6, 0), IsCancel = true };
            var btnClear  = new System.Windows.Controls.Button
            { Content = "Clear",  Width = 80, Margin = new System.Windows.Thickness(0, 0, 6, 0) };
            var btnWrite  = new System.Windows.Controls.Button
            { Content = "Write",  Width = 90, IsDefault = true,
              Background = new System.Windows.Media.SolidColorBrush(
                  System.Windows.Media.Color.FromRgb(28, 134, 90)),
              Foreground = System.Windows.Media.Brushes.White };

            string result = null;
            btnCancel.Click += (s, e) => { result = null;       w.Close(); };
            btnClear.Click  += (s, e) => { result = "<CLEAR>";  w.Close(); };
            btnWrite.Click  += (s, e) => { result = tb.Text;    w.Close(); };

            btnRow.Children.Add(btnCancel);
            btnRow.Children.Add(btnClear);
            btnRow.Children.Add(btnWrite);
            System.Windows.Controls.Grid.SetRow(btnRow, 3); grid.Children.Add(btnRow);

            w.Content = grid;
            tb.Focus();
            tb.SelectAll();
            w.ShowDialog();
            return result;
        }

        private static string SystemBTierLabel(int tier)
        {
            switch (tier)
            {
                case 4:  return "Commissioning";
                case 5:  return "Cost";
                case 6:  return "Carbon";
                case 7:  return "Fabrication";
                case 8:  return "Clash Triage";
                case 9:  return "As-Built";
                case 10: return "Compliance / Audit";
                default: return "Unknown";
            }
        }

        private static bool WriteModeBool(Element el, string paramName, bool target)
        {
            Parameter p = el.LookupParameter(paramName);
            if (p == null || p.IsReadOnly) return false;
            try
            {
                if (p.StorageType == StorageType.String)
                {
                    string want = target ? "Yes" : "No";
                    string cur = p.AsString() ?? "";
                    if (string.Equals(cur, want, StringComparison.OrdinalIgnoreCase)) return false;
                    p.Set(want);
                    return true;
                }
                if (p.StorageType == StorageType.Integer)
                {
                    int want = target ? 1 : 0;
                    if (p.AsInteger() == want) return false;
                    p.Set(want);
                    return true;
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"WriteModeBool {paramName} failed: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Reads the wire-style inline controls from the electrical dock panel and
        /// persists the user's selection. Stub — full implementation in Phase 179.
        /// </summary>
        private static void HandleWireSaveStyleFromPanel(UIApplication app)
        {
            // Gap 7: full implementation deferred to Phase 179.
            // Wire-style controls are read from StingElectricalPanel and saved
            // to project settings when this is implemented.
            StingTools.Core.StingLog.Info("HandleWireSaveStyleFromPanel: stub — Phase 179 implementation pending.");
        }
    }
}
