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
            try { StingTools.UI.StingCommandHandler.SetCurrentApp(app); } catch { }

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
