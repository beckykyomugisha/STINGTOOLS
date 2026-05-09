// StingPlumbingCommandHandler — IExternalEventHandler that dispatches
// plumbing dock-panel button clicks to IExternalCommand classes on the
// Revit API thread. Phase 178c.

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
