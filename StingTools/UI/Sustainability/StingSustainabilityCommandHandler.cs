// StingSustainabilityCommandHandler — IExternalEventHandler that dispatches
// Sustainability dock-panel button clicks to IExternalCommand classes on the
// Revit API thread (Phase 195). Mirrors StingPlumbingCommandHandler. Unknown
// tags fall through to StingCommandHandler.

using System;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.UI.Sustainability
{
    public class StingSustainabilityCommandHandler : IExternalEventHandler
    {
        public static StingSustainabilityCommandHandler Instance { get; private set; }
        public static ExternalEvent Event { get; private set; }

        private readonly object _lock = new object();
        private string _pendingTag;

        public static void Initialise(UIControlledApplication app)
        {
            if (Instance != null) return;
            Instance = new StingSustainabilityCommandHandler();
            Event    = ExternalEvent.Create(Instance);
        }

        public void SetCommand(string tag) { lock (_lock) _pendingTag = tag; }

        public string GetName() => "STING Sustainability Command Handler";

        public void Execute(UIApplication app)
        {
            try { StingTools.UI.StingCommandHandler.SetCurrentApp(app); }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            string tag;
            lock (_lock) { tag = _pendingTag; _pendingTag = null; }
            if (string.IsNullOrEmpty(tag)) return;

            try
            {
                switch (tag)
                {
                    case "Sustain_ProjectSetup":
                        Run<StingTools.Commands.Sustainability.SustainProjectSetupCommand>(app); break;
                    case "Sustain_Dashboard":
                        Run<StingTools.Commands.Sustainability.SustainDashboardCommand>(app); break;
                    case "Sustain_SetBaseline":
                        Run<StingTools.Commands.Sustainability.SustainSetBaselineCommand>(app); break;
                    case "Sustain_SupplyConfig":
                        Run<StingTools.Commands.Sustainability.SustainSupplyConfigCommand>(app); break;
                    case "Sustain_EdgeExport":
                        Run<StingTools.Commands.Sustainability.SustainEdgeExportCommand>(app); break;
                    case "Sustain_LccBenefit":
                        Run<StingTools.Commands.Sustainability.SustainLccBenefitCommand>(app); break;
                    case "Sustain_EpdAssign":
                        Run<StingTools.Commands.Sustainability.SustainEpdAssignCommand>(app); break;
                    case "Sustain_LeedScorecard":
                        Run<StingTools.Commands.Sustainability.SustainLeedScorecardCommand>(app); break;

                    default:
                        // Unknown tags fall through to the main handler via the
                        // static dispatch surface (same pattern as the HVAC panel).
                        try
                        {
                            bool ok = StingTools.UI.StingDockPanel.DispatchCommand(tag);
                            if (!ok)
                                StingLog.Warn($"StingSustainabilityCommandHandler: fallback dispatch '{tag}' refused by main handler.");
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"StingSustainabilityCommandHandler: unknown tag '{tag}' ({ex.Message})");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("StingSustainabilityCommandHandler", ex);
                TaskDialog.Show("STING Sustainability", $"Command '{tag}' failed:\n{ex.Message}");
            }
        }

        private static void Run<T>(UIApplication app) where T : Autodesk.Revit.UI.IExternalCommand, new()
        {
            try
            {
                var c = new T();
                string m = "";
                c.Execute(null, ref m, new Autodesk.Revit.DB.ElementSet());
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
            catch (Exception ex)
            {
                StingLog.Error($"Run<{typeof(T).Name}>", ex);
                TaskDialog.Show("STING Sustainability", $"Command failed: {ex.Message}");
            }
        }
    }
}
