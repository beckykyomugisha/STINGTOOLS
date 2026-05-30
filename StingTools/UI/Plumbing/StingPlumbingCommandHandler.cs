// StingPlumbingCommandHandler — IExternalEventHandler that dispatches
// plumbing dock-panel button clicks to IExternalCommand classes on the
// Revit API thread. Phase 178c → Phase 179 (8 tabs, 27 commands).

using System;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.UI.Plumbing
{
    public class StingPlumbingCommandHandler : IExternalEventHandler
    {
        public static StingPlumbingCommandHandler Instance { get; private set; }
        public static ExternalEvent Event { get; private set; }

        private readonly object _lock = new object();
        private string _pendingTag;

        public static void Initialise(UIControlledApplication app)
        {
            if (Instance != null) return;
            Instance = new StingPlumbingCommandHandler();
            Event    = ExternalEvent.Create(Instance);
        }

        public void SetCommand(string tag)
        {
            lock (_lock) _pendingTag = tag;
        }

        public string GetName() => "STING Plumbing Command Handler";

        public void Execute(UIApplication app)
        {
            try { StingTools.UI.StingCommandHandler.SetCurrentApp(app); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            string tag;
            lock (_lock) { tag = _pendingTag; _pendingTag = null; }
            if (string.IsNullOrEmpty(tag)) return;

            try
            {
                switch (tag)
                {
                    // ── Phase 178c (legacy — kept for backwards compat) ──
                    case "Plumbing_AutoSizeDrainage":
                        Run<StingTools.Commands.Plumbing.AutoSizeDrainageCommand>(app); break;
                    case "Plumbing_BackflowAudit":
                        Run<StingTools.Commands.Plumbing.BackflowAuditCommand>(app); break;
                    case "Plumbing_RainwaterCalc":
                        Run<StingTools.Commands.Plumbing.RainwaterCalcCommand>(app); break;
                    case "Plumbing_TrapVentAudit":
                        Run<StingTools.Commands.Plumbing.TrapAndVentAuditCommand>(app); break;
                    case "Plumbing_PRVSchedule":
                        Run<StingTools.Commands.Plumbing.PRVScheduleCommand>(app); break;
                    case "Plumbing_DeadLegScan":
                        Run<StingTools.Commands.Plumbing.DeadLegScanCommand>(app); break;
                    case "Plumbing_CrossConnection":
                        Run<StingTools.Commands.Plumbing.CrossConnectionScanCommand>(app); break;
                    case "Plumbing_RecircBalance":
                        Run<StingTools.Commands.Plumbing.RecircBalanceCommand>(app); break;
                    case "Plumbing_StackCapacity":
                        Run<StingTools.Commands.Plumbing.StackCapacityCommand>(app); break;
                    case "Plumbing_MaterialAudit":
                        Run<StingTools.Commands.Plumbing.MaterialAuditCommand>(app); break;

                    // ── Phase 179a — SYSTEM ──
                    case "Plumb_SaveSystemConfig":
                        Run<StingTools.Commands.Plumbing.PlumbSaveSystemConfigCommand>(app); break;
                    case "Plumb_LoadSystemConfig":
                        Run<StingTools.Commands.Plumbing.PlumbLoadSystemConfigCommand>(app); break;

                    // ── Phase 179b — SUPPLY / DRAINAGE sizing ──
                    case "Plumb_ScanFixtures":
                        Run<StingTools.Commands.Plumbing.PlumbScanFixturesCommand>(app); break;
                    case "Plumb_SizeSupply":
                        Run<StingTools.Commands.Plumbing.PlumbSizeSupplyCommand>(app); break;
                    case "Plumb_SizeDrainage":
                        Run<StingTools.Commands.Plumbing.PlumbSizeDrainageCommand>(app); break;
                    case "Plumb_PressureCheck":
                        Run<StingTools.Commands.Plumbing.PlumbPressureCheckCommand>(app); break;
                    case "Plumb_ExpVessel":
                        Run<StingTools.Commands.Plumbing.PlumbExpVesselCommand>(app); break;
                    case "Plumb_TMVRegister":
                        Run<StingTools.Commands.Plumbing.PlumbTMVRegisterCommand>(app); break;

                    // ── Phase 179c — ROUTE ──
                    case "Plumb_AutoRoute":
                        Run<StingTools.Commands.Plumbing.PlumbAutoRouteCommand>(app); break;
                    case "Plumb_FixSlopes":
                        Run<StingTools.Commands.Plumbing.PlumbFixSlopesCommand>(app); break;
                    case "Plumb_InsertPTraps":
                        Run<StingTools.Commands.Plumbing.PlumbInsertPTrapsCommand>(app); break;
                    case "Plumb_PlaceSleeves":
                        Run<StingTools.Commands.Plumbing.PlumbPlaceSleevesCommand>(app); break;
                    case "Plumb_PlaceHangers":
                        Run<StingTools.Commands.Plumbing.PlumbPlaceHangersCommand>(app); break;

                    // ── Phase 179d — DRAINAGE detail ──
                    case "Plumb_VentDesign":
                        Run<StingTools.Commands.Plumbing.PlumbVentDesignCommand>(app); break;
                    case "Plumb_InvertLevels":
                        Run<StingTools.Commands.Plumbing.PlumbInvertLevelsCommand>(app); break;

                    // Closes the design → model loop for vents: takes the VentDesigner
                    // requirement list and actually creates pipe + AAV instances.
                    case "Plumb_CreateVents":
                        Run<StingTools.Commands.Plumbing.PlumbCreateVentsCommand>(app); break;

                    // ── Phase 179e — STORM / AUDIT ──
                    case "Plumb_RWH":
                        Run<StingTools.Commands.Plumbing.PlumbRwhCommand>(app); break;
                    case "Plumb_SuDS":
                        Run<StingTools.Commands.Plumbing.PlumbSuDSCommand>(app); break;
                    case "Plumb_Soakaway":
                        Run<StingTools.Commands.Plumbing.PlumbSoakawayCommand>(app); break;
                    case "Plumb_SepticTank":
                        Run<StingTools.Commands.Plumbing.PlumbSepticTankCommand>(app); break;
                    case "Plumb_RoofDrainage":
                        Run<StingTools.Commands.Plumbing.PlumbRoofDrainageCommand>(app); break;
                    case "Plumb_FullAudit":
                        Run<StingTools.Commands.Plumbing.PlumbFullAuditCommand>(app); break;

                    // ── Phase 179f — DOCS ──
                    case "Plumb_PipeSchedule":
                        Run<StingTools.Commands.Plumbing.PlumbPipeScheduleCommand>(app); break;
                    case "Plumb_BOQ":
                        Run<StingTools.Commands.Plumbing.PlumbBOQCommand>(app); break;
                    case "Plumb_ManholeSchedule":
                        Run<StingTools.Commands.Plumbing.PlumbManholeScheduleCommand>(app); break;
                    case "Plumb_Isometric":
                        Run<StingTools.Commands.Plumbing.PlumbIsometricCommand>(app); break;
                    case "Plumb_CommPack":
                        Run<StingTools.Commands.Plumbing.PlumbCommPackCommand>(app); break;
                    case "Plumb_SupplySchematic":
                        Run<StingTools.Commands.Plumbing.PlumbSupplySchematicCommand>(app); break;

                    // ── Plan-level symbol placement (STING_PLUMBING_SYMBOLS.json) ──
                    case "PlumbSym_WC":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceWCCommand>(app); break;
                    case "PlumbSym_Urinal":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceUrinalCommand>(app); break;
                    case "PlumbSym_Bidet":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceBidetCommand>(app); break;
                    case "PlumbSym_WHB":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceWHBCommand>(app); break;
                    case "PlumbSym_VanityBasin":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceVanityBasinCommand>(app); break;
                    case "PlumbSym_Bath":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceBathCommand>(app); break;
                    case "PlumbSym_Shower":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceShowerCommand>(app); break;
                    case "PlumbSym_SingleSink":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceSingleSinkCommand>(app); break;
                    case "PlumbSym_DoubleSink":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceDoubleSinkCommand>(app); break;
                    case "PlumbSym_CleanersSink":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceCleanersSinkCommand>(app); break;
                    case "PlumbSym_FloorDrainRound":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceFloorDrainRoundCommand>(app); break;
                    case "PlumbSym_FloorDrainSquare":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceFloorDrainSquareCommand>(app); break;
                    case "PlumbSym_Gulley":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceGulleyCommand>(app); break;
                    case "PlumbSym_GateValve":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceGateValveCommand>(app); break;
                    case "PlumbSym_GlobeValve":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceGlobeValveCommand>(app); break;
                    case "PlumbSym_BallValve":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceBallValveCommand>(app); break;
                    case "PlumbSym_ButterflyValve":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceButterflyValveCommand>(app); break;
                    case "PlumbSym_CheckValve":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceCheckValveCommand>(app); break;
                    case "PlumbSym_PRV":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlacePRVCommand>(app); break;
                    case "PlumbSym_Strainer":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceStrainerCommand>(app); break;
                    case "PlumbSym_FlexConn":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceFlexConnCommand>(app); break;
                    case "PlumbSym_HWCDirect":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceHWCDirectCommand>(app); break;
                    case "PlumbSym_HWCIndirect":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.PlaceHWCIndirectCommand>(app); break;
                    case "PlumbSym_BrowseAll":
                        Run<StingTools.Commands.Symbols.PlumbingSymbolCommands.BrowsePlumbingSymbolsCommand>(app); break;

                    // ── Group 5: surfaced plumbing engines (were orphaned — no button) ──
                    case "Plumb_PumpSelect":        Run<StingTools.Commands.Plumbing.PlumbPumpSelectCommand>(app); break;
                    case "Plumb_BoosterSet":        Run<StingTools.Commands.Plumbing.PlumbBoosterSetCommand>(app); break;
                    case "Plumb_BuildNetwork":      Run<StingTools.Commands.Plumbing.PlumbBuildNetworkCommand>(app); break;
                    case "Plumb_NetworkPressure":   Run<StingTools.Commands.Plumbing.PlumbNetworkPressureCommand>(app); break;
                    case "Plumb_NetworkStats":      Run<StingTools.Commands.Plumbing.PlumbNetworkStatsCommand>(app); break;
                    case "Plumb_PressureZone":      Run<StingTools.Commands.Plumbing.PlumbPressureZoneCommand>(app); break;
                    case "Plumb_SlopeAutomation":   Run<StingTools.Commands.Plumbing.PlumbSlopeAutomationCommand>(app); break;
                    case "Plumb_GenerateSpools":    Run<StingTools.Commands.Plumbing.PlumbGenerateSpoolsCommand>(app); break;
                    case "Plumb_SpoolSchedule":     Run<StingTools.Commands.Plumbing.PlumbSpoolScheduleCommand>(app); break;
                    case "Plumb_DrainageSchematic": Run<StingTools.Commands.Plumbing.PlumbDrainageSchematicCommand>(app); break;
                    case "Plumb_TMVEngine":         Run<StingTools.Commands.Plumbing.PlumbTMVEngineCommand>(app); break;
                    case "Plumb_LegionellaReport":  Run<StingTools.Commands.Plumbing.PlumbLegionellaReportCommand>(app); break;
                    case "Plumb_WaterSafetyPlan":   Run<StingTools.Commands.Plumbing.PlumbWaterSafetyPlanCommand>(app); break;

                    default:
                        StingLog.Warn($"StingPlumbingCommandHandler: unknown tag '{tag}'");
                        break;
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("StingPlumbingCommandHandler", ex);
                TaskDialog.Show("STING Plumbing", $"Command '{tag}' failed:\n{ex.Message}");
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
                TaskDialog.Show("STING Plumbing", $"Command failed: {ex.Message}");
            }
        }
    }
}
