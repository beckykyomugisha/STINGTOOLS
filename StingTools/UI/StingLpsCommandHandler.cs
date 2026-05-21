// StingLpsCommandHandler — IExternalEventHandler that dispatches Lightning
// Protection dock-panel button clicks to IExternalCommand classes on the
// Revit API thread. Mirrors StingHvacCommandHandler / StingElectricalCommandHandler
// in shape so future merging is trivial.

using System;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Commands.Lightning;

namespace StingTools.UI
{
    public class StingLpsCommandHandler : IExternalEventHandler
    {
        public static StingLpsCommandHandler Instance { get; private set; }
        public static ExternalEvent Event { get; private set; }

        // ── Per-tab snapshot inputs (header state) ────────────────────
        public static string CurrentStandard     = "BS_EN_62305";   // BS_EN_62305 | IEC_62305 | NFPA_780 | NFC_17_102
        public static string CurrentLpsClass     = "II";            // I | II | III | IV | AUTO
        public static string CurrentAirTermMethod = "ROLLING_SPHERE"; // ROLLING_SPHERE | MESH | PROTECTION_ANGLE
        public static string CurrentMaterial     = "COPPER";        // COPPER | ALUMINIUM | STEEL | STAINLESS
        public static string CurrentRegion       = "UK";            // UK | EU | US | TROPIC | AFRICA
        public static string CurrentScope        = "ActiveView";    // ActiveView | Selection | Project
        public static double CurrentEquipmentWithstandKv = 1.5;
        public static double CurrentSoilResistivityOhmM  = 100.0;

        private static readonly object _stateLock = new object();
        private string _pendingTag;

        public static void SetInputs(
            string standard, string lpsClass, string airTermMethod,
            string material, string region, string scope,
            double equipmentWithstandKv, double soilResistivityOhmM)
        {
            lock (_stateLock)
            {
                if (standard      != null) CurrentStandard      = standard;
                if (lpsClass      != null) CurrentLpsClass      = lpsClass;
                if (airTermMethod != null) CurrentAirTermMethod = airTermMethod;
                if (material      != null) CurrentMaterial      = material;
                if (region        != null) CurrentRegion        = region;
                if (scope         != null) CurrentScope         = scope;
                if (!double.IsNaN(equipmentWithstandKv) && equipmentWithstandKv > 0)
                    CurrentEquipmentWithstandKv = equipmentWithstandKv;
                if (!double.IsNaN(soilResistivityOhmM) && soilResistivityOhmM > 0)
                    CurrentSoilResistivityOhmM = soilResistivityOhmM;
            }
        }

        public static (string Standard, string LpsClass, string AirTermMethod,
                       string Material, string Region, string Scope,
                       double EquipmentWithstandKv, double SoilResistivityOhmM) Snapshot()
        {
            lock (_stateLock)
            {
                return (CurrentStandard, CurrentLpsClass, CurrentAirTermMethod,
                        CurrentMaterial, CurrentRegion, CurrentScope,
                        CurrentEquipmentWithstandKv, CurrentSoilResistivityOhmM);
            }
        }

        public static void Initialise(UIControlledApplication app)
        {
            if (Instance != null) return;
            Instance = new StingLpsCommandHandler();
            try { Event = ExternalEvent.Create(Instance); }
            catch (Exception ex) { StingLog.Error("LpsCommandHandler ExternalEvent.Create", ex); }
        }

        public void SetCommand(string tag)
        {
            System.Threading.Interlocked.Exchange(ref _pendingTag, tag ?? "");
            try { Event?.Raise(); }
            catch (Exception ex) { StingLog.Warn($"LpsCommandHandler.SetCommand: {ex.Message}"); }
        }

        public string GetName() => "STING LPS Command Dispatcher";

        public void Execute(UIApplication app)
        {
            try { StingCommandHandler.SetCurrentApp(app); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            string tag = System.Threading.Interlocked.Exchange(ref _pendingTag, null);
            if (string.IsNullOrEmpty(tag)) return;

            var doc = app?.ActiveUIDocument?.Document;
            if (doc == null && tag != "Lps_OpenSettings" && tag != "Lps_ReloadCatalogue")
            {
                TaskDialog.Show("STING LPS", "No document is open.");
                return;
            }

            try
            {
                switch (tag)
                {
                    // ── RISK tab ───────────────────────────────────────
                    case "Lps_ClassSetup":
                        Run<LpsClassSetupCommand>(app); break;
                    case "Lps_RunRiskInline":
                        Run<LpsRiskAssessmentInlineCommand>(app); break;

                    // ── AIR-TERM tab ───────────────────────────────────
                    case "Lps_PlanVisualise":
                        Run<LpsPlanViewVisualizerCommand>(app); break;
                    case "Lps_3DCoverage":
                        Run<LpsRollingSphere3DCommand>(app); break;
                    case "Lps_MarkTypes":
                        Run<LpsMarkElementTypesCommand>(app); break;

                    // ── CONDUCTORS tab ─────────────────────────────────
                    case "Lps_DownConductor":
                        Run<LpsDownConductorCheckerCommand>(app); break;
                    case "Lps_SepDistance":
                        Run<LpsSeparationDistanceCheckerCommand>(app); break;
                    case "Lps_RecalcKc":
                        Run<LpsRecalcKcFactorCommand>(app); break;

                    // ── EARTH tab ──────────────────────────────────────
                    case "Lps_EarthCheck":
                        Run<LpsEarthResistanceValidatorCommand>(app); break;
                    case "Lps_Bonding":
                        Run<LpsBondingInventoryCommand>(app); break;

                    // ── SPD tab ────────────────────────────────────────
                    case "Lps_SpdCoordinate":
                        Run<SpdCoordinationCheckCommand>(app); break;
                    case "Lps_SpdRecommend":
                        Run<SpdRecommendCommand>(app); break;
                    case "Lps_SpdExportBom":
                        Run<SpdExportBomCommand>(app); break;
                    case "Lps_SpdSaveOverride":
                        Run<SpdSaveOverrideCommand>(app); break;

                    // ── Cross-tab: load every grid from the active doc ─
                    case "Lps_LoadModel":
                        Run<LpsLoadFromModelCommand>(app); break;

                    // ── ZONES tab ──────────────────────────────────────
                    case "Lps_ZoneTagRooms":
                        Run<LpsRoomZoneTagCommand>(app); break;
                    case "Lps_ColourZones":
                        Run<LpsColourZonesCommand>(app); break;
                    case "Lps_ClearColours":
                        Run<LpsClearZoneColoursCommand>(app); break;

                    // ── RPRT tab ───────────────────────────────────────
                    case "Lps_Compliance":
                        Run<LpsComplianceCheckCommand>(app); break;
                    case "Lps_Dashboard":
                        Run<LpsDashboardCommand>(app); break;
                    case "Lps_InspectionSchedule":
                        Run<LpsInspectionSchedulerCommand>(app); break;
                    case "Lps_CreateSchedules":
                        Run<LpsCreateRevitScheduleCommand>(app); break;
                    case "Lps_FullReport":
                        Run<LpsFullReportCommand>(app); break;
                    case "Lps_SyncToServer":
                        Run<LpsSyncToServerCommand>(app); break;
                    case "Lps_ReloadCatalogue":
                        StingTools.Core.Lightning.SpdCoordinator.Reload();
                        try { StingTools.UI.StingLpsPanel.Instance?.RefreshAll(); }
                        catch (Exception ex) { StingLog.Warn($"RefreshAll: {ex.Message}"); }
                        TaskDialog.Show("STING LPS", "SPD catalogue reloaded — panel grids refreshed.");
                        break;
                    case "Lps_OpenSettings":
                        OpenSettings();
                        break;

                    default:
                        // Forward unknown tags to the main STING dispatcher.
                        try
                        {
                            bool ok = StingDockPanel.DispatchCommand(tag);
                            if (!ok)
                                StingLog.Warn($"Lps fallback dispatch '{tag}' refused by main handler.");
                        }
                        catch (Exception ex) { StingLog.Warn($"Lps fallback '{tag}': {ex.Message}"); }
                        break;
                }

                // Drop a row in the RPRT WorkflowGrid so the panel reflects activity.
                try { StingTools.UI.StingLpsPanel.Instance?.PushRunRow(tag, "✓"); }
                catch (Exception ex) { StingLog.Warn($"PushRunRow: {ex.Message}"); }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { /* user cancel */ }
            catch (Exception ex)
            {
                StingLog.Error($"LpsCommandHandler dispatch '{tag}'", ex);
                TaskDialog.Show("STING LPS", $"Command '{tag}' failed:\n{ex.Message}");
                try { StingTools.UI.StingLpsPanel.Instance?.PushRunRow(tag, "✗"); }
                catch (Exception ex2) { StingLog.Warn($"PushRunRow fail: {ex2.Message}"); }
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
                TaskDialog.Show("STING LPS", $"Command failed:\n{ex.Message}");
            }
        }

        private static void OpenSettings()
        {
            var td = new TaskDialog("STING LPS — Settings")
            {
                MainInstruction = "Lightning Protection data files",
                MainContent =
                    "Active corporate baselines (Data/LPS/):\n" +
                    "  • STING_LPS_CLASSES.json\n" +
                    "  • STING_LPS_FLASH_DENSITY.json\n" +
                    "  • STING_LPS_RISK_FACTORS.json\n" +
                    "  • STING_LPS_SPD_CATALOGUE.json\n\n" +
                    "Project override (optional):\n" +
                    "  <project>/_BIM_COORD/lps_spd_catalogue.json\n\n" +
                    "Edit any file in a text editor and click 'Reload catalogue' on the RPRT tab."
            };
            td.Show();
        }
    }
}
