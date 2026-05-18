// StingHvacCommandHandler — IExternalEventHandler that dispatches HVAC
// dock-panel button clicks to IExternalCommand classes on the Revit API
// thread. Mirrors StingElectricalCommandHandler / StingPlumbingCommandHandler
// in shape so future merging is trivial.
//
// HVAC tags fall into two buckets:
//   1. Tags handled directly here (HVAC-only commands like Mep_AutoSizeDuct,
//      MEPScheduleHVAC, DuctSeamAudit, Routing_AutoDrop, etc.) — run via Run<T>.
//   2. Generic tags that already exist on the main STING panel (SelectMechanical,
//      ModelCreateDuct, …) — forwarded to StingCommandHandler so we don't
//      duplicate the dispatch table.

using System;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.UI
{
    public class StingHvacCommandHandler : IExternalEventHandler
    {
        public static StingHvacCommandHandler Instance { get; private set; }
        public static ExternalEvent Event { get; private set; }

        // Per-tab snapshot inputs — populated by the panel before raising Event.
        // Each is a public static so commands can read them without a dependency on the UI dll.
        public static string CurrentRegion              = "UK_SI";
        public static string CurrentStandard            = "CIBSE";
        public static string CurrentPressureClassId     = "low";
        public static double CurrentAirDensityKgM3      = 1.20;
        public static string CurrentSizingStrategyId    = "velocity";
        public static string CurrentScope               = "ActiveView"; // ActiveView | Selection | Project
        public static string CurrentLoadEngine          = "RevitNative";
        public static string CurrentLoadCode            = "ASHRAE_90_1";

        private readonly object _lock = new object();
        private string _pendingTag;

        public static void Initialise(UIControlledApplication app)
        {
            if (Instance != null) return;
            Instance = new StingHvacCommandHandler();
            try { Event = ExternalEvent.Create(Instance); }
            catch (Exception ex) { StingLog.Error("HvacCommandHandler ExternalEvent.Create", ex); }
        }

        public void SetCommand(string tag)
        {
            lock (_lock) _pendingTag = tag ?? "";
            try { Event?.Raise(); }
            catch (Exception ex) { StingLog.Warn($"HvacCommandHandler.SetCommand: {ex.Message}"); }
        }

        public string GetName() => "STING HVAC Command Dispatcher";

        public void Execute(UIApplication app)
        {
            try { StingCommandHandler.SetCurrentApp(app); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            string tag;
            lock (_lock) { tag = _pendingTag; _pendingTag = null; }
            if (string.IsNullOrEmpty(tag)) return;

            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null && tag != "Hvac_OpenSettings")
            {
                TaskDialog.Show("STING HVAC", "No document is open.");
                return;
            }

            try
            {
                switch (tag)
                {
                    // ── EQPT tab ───────────────────────────────────────────
                    case "Hvac_SelectMechanical":
                        Run<StingTools.Select.SelectMechanicalCommand>(app); break;
                    case "Hvac_SelectDucts":
                        Run<StingTools.Select.SelectDuctsCommand>(app); break;
                    case "Hvac_PlaceEquipment":
                        Run<StingTools.Commands.Symbols.PlaceHvacEquipmentCommand>(app); break;
                    case "Hvac_EquipmentSchedule":
                    case "MEPScheduleHVAC":
                        Run<StingTools.Temp.MechanicalEquipmentScheduleCommand>(app); break;
                    case "Hvac_EquipmentAudit":
                        Run<StingTools.Temp.MEPSystemAuditCommand>(app); break;
                    case "Hvac_ConnectionAudit":
                        Run<StingTools.Temp.MEPConnectionAuditCommand>(app); break;
                    case "Hvac_SpaceAnalysis":
                        Run<StingTools.Temp.MEPSpaceAnalysisCommand>(app); break;
                    case "Hvac_SizingCheck":
                        Run<StingTools.Temp.MEPSizingCheckCommand>(app); break;

                    // ── SYS tab ────────────────────────────────────────────
                    case "Hvac_SystemAudit":
                        Run<StingTools.Temp.MEPSystemAuditCommand>(app); break;
                    case "Hvac_AutoFireDamper":
                    case "Routing_AutoFireDamper":
                        Run<StingTools.Commands.RoutingExt.AutoFireDamperCommand>(app); break;
                    case "Hvac_Ventilation":
                        Run<StingTools.Commands.StandardsExt.VentilationCommand>(app); break;

                    // ── CALCS tab ──────────────────────────────────────────
                    case "Hvac_AutoSizeDuct":
                    case "Mep_AutoSizeDuct":
                        // Phase 182 — strategy radio actually dispatches (gap D2).
                        // The CALCS tab's "Auto-size" button respects the header
                        // strategy radio rather than always calling MepAutoSizeDuct.
                        switch ((CurrentSizingStrategyId ?? "velocity").ToLowerInvariant())
                        {
                            case "equal_friction":
                                Run<StingTools.Commands.StandardsExt.DuctEqualFrictionCommand>(app); break;
                            case "static_regain":
                                Run<StingTools.Commands.MepDesign.DuctStaticRegainCommand>(app); break;
                            case "constant_pressure":
                                // No dedicated command yet; fall through to velocity sizing
                                // and stamp the chosen strategy in the audit trail.
                                Run<StingTools.Commands.Mep.MepAutoSizeDuctCommand>(app); break;
                            case "velocity":
                            default:
                                Run<StingTools.Commands.Mep.MepAutoSizeDuctCommand>(app); break;
                        }
                        break;
                    case "Hvac_DuctFriction":
                        Run<StingTools.Commands.Routing.CalcDuctFrictionCommand>(app); break;
                    case "Hvac_StaticRegain":
                        Run<StingTools.Commands.MepDesign.DuctStaticRegainCommand>(app); break;
                    case "Hvac_EqualFriction":
                        Run<StingTools.Commands.StandardsExt.DuctEqualFrictionCommand>(app); break;
                    case "Hvac_Balance":
                    case "Hvac_HardyCross":
                        Run<StingTools.Commands.Routing.HardyCrossCommand>(app); break;
                    case "Hvac_ValidateFills":
                    case "Routing_ValidateFills":
                        Run<StingTools.Commands.Routing.ValidateFillsCommand>(app); break;
                    case "Hvac_RunAllValidators":
                    case "Validation_RunAll":
                        Run<StingTools.Commands.Validation.RunAllValidatorsCommand>(app); break;
                    case "Hvac_SaveRules":
                        // Phase 182 — gap D1/A6
                        Run<StingTools.Commands.Hvac.HvacSaveRulesCommand>(app); break;
                    case "Hvac_ResetDefaults":
                        // Phase 188 — Fix G4: button on CALCS tab was an orphan.
                        // Reload the registry from corporate baseline + re-seed
                        // the CALCS DataGrid, effectively discarding any in-memory
                        // edits the user made to the rule rows. Project override
                        // on disk is left untouched (Save button is the only
                        // path that writes it).
                        try
                        {
                            StingTools.Core.Mep.MepSizingRegistry.Reload();
                            StingTools.UI.StingHvacPanel.Instance?.RefreshSizingRoles();
                            StingTools.UI.StingHvacPanel.Instance?.PushRunRow(
                                "Reset sizing rules → baseline", "⬤");
                            TaskDialog.Show("STING HVAC",
                                "Sizing-role grid reset to corporate baseline. " +
                                "Project override on disk (_BIM_COORD/mep_sizing_rules.json) " +
                                "is unchanged — click 'Save to project' to re-apply your edits.");
                        }
                        catch (Exception exR)
                        {
                            StingLog.Warn($"ResetDefaults: {exR.Message}");
                            TaskDialog.Show("STING HVAC", $"Reset failed: {exR.Message}");
                        }
                        break;
                    case "Hvac_DetectStaleSizes":
                        // Phase 183 — gap D8 (manual scan, no IUpdater overhead)
                        Run<StingTools.Commands.Hvac.HvacDetectStaleSizesCommand>(app); break;
                    case "Hvac_PressureClassAudit":
                        // Phase 183 — gap A3/D10 (pressure-class enforcement)
                        Run<StingTools.Commands.Hvac.HvacPressureClassAuditCommand>(app); break;
                    case "Hvac_CarbonReport":
                        // Phase 183 — gap C8 (HVAC plant + refrigerant carbon)
                        Run<StingTools.Commands.Hvac.HvacCarbonReportCommand>(app); break;

                    // ── DUCT tab ───────────────────────────────────────────
                    case "Hvac_CreateDuctTypes":
                    case "CreateDucts":
                        Run<StingTools.Temp.CreateDuctsCommand>(app); break;
                    case "Hvac_ModelCreateDuct":
                    case "ModelCreateDuct":
                        Run<StingTools.Model.ModelCreateDuctCommand>(app); break;
                    case "Hvac_DuctSeamAudit":
                        Run<StingTools.Commands.FabricationExt.DuctSeamAuditCommand>(app); break;
                    case "Hvac_AutoDrop":
                    case "Routing_AutoDrop":
                        Run<StingTools.Commands.Routing.AutoDropCommand>(app); break;
                    case "Hvac_GenerateLayout":
                    case "Routing_GenerateLayout":
                        Run<StingTools.Commands.Routing.GenerateLayoutCommand>(app); break;
                    case "Hvac_PlaceHangers":
                    case "Routing_PlaceHangers":
                        Run<StingTools.Commands.Routing.PlaceHangersCommand>(app); break;

                    // ── LOADS tab — heating + cooling + gbXML ──────────────
                    // Phase 181: real commands; Phase 180 TaskDialog stubs replaced.
                    case "Hvac_RunLoads":
                        Run<StingTools.Commands.Hvac.HvacRunLoadsCommand>(app); break;
                    case "Hvac_ExportGbxml":
                        Run<StingTools.Commands.Hvac.HvacExportGbxmlCommand>(app); break;
                    case "Hvac_AuditEnvelope":
                        Run<StingTools.Temp.MEPSpaceAnalysisCommand>(app); break;

                    // ── FAB tab ────────────────────────────────────────────
                    case "Hvac_ExportCutList":
                    case "Fabrication_ExportCutList":
                        Run<StingTools.Commands.Fabrication.ExportCutListCommand>(app); break;
                    case "Hvac_ExportIsometrics":
                    case "Fabrication_ExportIsometrics":
                        Run<StingTools.Commands.Fabrication.ExportIsometricsCommand>(app); break;
                    case "Hvac_ExportWeldMap":
                    case "Fabrication_ExportWeldMap":
                        Run<StingTools.Commands.Fabrication.ExportWeldMapCommand>(app); break;
                    case "Hvac_HangerTakedown":
                        Run<StingTools.Commands.FabricationExt.HangerTakedownCommand>(app); break;
                    case "Hvac_FlangeRating":
                        Run<StingTools.Commands.FabricationExt.FlangeRatingCommand>(app); break;
                    case "Hvac_SpoolWeight":
                        Run<StingTools.Commands.FabricationExt.SpoolWeightCommand>(app); break;
                    case "Hvac_ExportNC":
                        Run<StingTools.Commands.FabricationExt.ExportNCCommand>(app); break;

                    // ── RPRT tab ───────────────────────────────────────────
                    case "Hvac_ReloadRules":
                        // Phase 182 — Reload also re-seeds the CALCS grid (gap B9).
                        StingTools.Core.Mep.MepSizingRegistry.Reload();
                        try { StingTools.UI.StingHvacPanel.Instance?.RefreshSizingRoles(); }
                        catch (Exception ex) { StingLog.Warn($"RefreshSizingRoles: {ex.Message}"); }
                        try { StingTools.UI.StingHvacPanel.Instance?.PushRunRow("Reload sizing rules", "⬤"); }
                        catch (Exception ex) { StingLog.Warn($"PushRunRow: {ex.Message}"); }
                        TaskDialog.Show("STING HVAC", "MEP sizing rules reloaded — CALCS grid refreshed.");
                        break;
                    case "Hvac_OpenSettings":
                        OpenSettings();
                        break;

                    // ── Fallback: forward to main STING command handler ────
                    default:
                        try
                        {
                            // The main panel exposes a static dispatch surface
                            // (StingDockPanel.DispatchCommand) that owns the
                            // ExternalEvent for the unified handler. Routing
                            // unknown tags there avoids duplicating the 500+
                            // case switch this dispatcher would otherwise need.
                            bool ok = StingDockPanel.DispatchCommand(tag);
                            if (!ok)
                                StingLog.Warn($"Hvac fallback dispatch '{tag}' refused by main handler.");
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Hvac fallback dispatch '{tag}' failed: {ex.Message}");
                        }
                        break;
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { /* user cancel */ }
            catch (Exception ex)
            {
                StingLog.Error($"HvacCommandHandler dispatch '{tag}'", ex);
                TaskDialog.Show("STING HVAC", $"Command '{tag}' failed:\n{ex.Message}");
            }
        }

        private static void Run<T>(UIApplication app) where T : Autodesk.Revit.UI.IExternalCommand, new()
        {
            try
            {
                var cmd = new T();
                string msg = "";
                cmd.Execute(null, ref msg, new Autodesk.Revit.DB.ElementSet());
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
            catch (Exception ex)
            {
                StingLog.Error($"Run<{typeof(T).Name}>", ex);
                TaskDialog.Show("STING HVAC", $"Command failed:\n{ex.Message}");
            }
        }

        private static void OpenSettings()
        {
            var td = new TaskDialog("STING HVAC — Settings")
            {
                MainInstruction = "MEP sizing rules registry",
                MainContent =
                    "Active corporate baseline: Data/STING_MEP_SIZING_RULES.json\n\n" +
                    "Project override (optional):\n  <project>/_BIM_COORD/mep_sizing_rules.json\n\n" +
                    "Edit either file in any text editor and click 'Reload rules' on the RPRT tab."
            };
            td.Show();
        }
    }
}
