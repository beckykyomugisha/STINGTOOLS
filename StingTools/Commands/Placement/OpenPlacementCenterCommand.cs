// Phase 127-A — entry-point command for the Placement Centre.
//
// Single dispatcher. The centre is modeless and singleton: re-clicking
// the toolbar button while it's already open just brings it forward.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class OpenPlacementCenterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // commandData is null when launched via the dock panel's
                // IExternalEventHandler dispatch (StingCommandHandler.RunCommand
                // passes null and relies on CurrentApp as the fallback).
                var uiApp = commandData?.Application ?? StingCommandHandler.CurrentApp;
                if (uiApp == null)
                {
                    message = "No active UIApplication — Revit may still be initialising.";
                    StingLog.Warn("OpenPlacementCenterCommand: no UIApplication available.");
                    return Result.Failed;
                }
                StingTools.UI.PlacementCenter.StingPlacementCenter.ShowOrFocus(uiApp);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("OpenPlacementCenterCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
