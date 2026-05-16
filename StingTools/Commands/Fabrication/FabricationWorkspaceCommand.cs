// StingTools v4 MVP — Fabrication Workspace launcher.
//
// Modal IExternalCommand that opens the light-themed
// FabricationWorkspaceDialog. The dialog itself is read-only with
// respect to the document; every action button it surfaces routes
// back through the dock panel command handler so the underlying
// fabrication commands run on the Revit API thread.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Fabrication
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class FabricationWorkspaceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, Autodesk.Revit.DB.ElementSet els)
        {
            try
            {
                // RunCommand<T> on the dock panel passes null for `data` and
                // expects callers to fall back to StingCommandHandler.CurrentApp.
                var app = data?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp;
                if (app == null)
                {
                    msg = "No active Revit application.";
                    StingLog.Warn("FabricationWorkspaceCommand: no UIApplication available.");
                    return Result.Failed;
                }
                var doc = app.ActiveUIDocument?.Document;

                var dlg = new FabricationWorkspaceDialog(doc);
                try
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(dlg);
                    helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                dlg.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("FabricationWorkspaceCommand", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }
    }
}
