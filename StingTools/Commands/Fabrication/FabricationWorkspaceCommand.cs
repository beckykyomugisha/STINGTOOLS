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
                var doc = data?.Application?.ActiveUIDocument?.Document;
                var dlg = new FabricationWorkspaceDialog(doc);
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
