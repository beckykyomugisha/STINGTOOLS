// Healthcare Pack H-8 — Single Room Data Sheet issue command.
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StingTools.Core;
using StingTools.Docs.Templates;
using System;

namespace StingTools.Commands.Healthcare
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class IssueRoomDataSheetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = commandData.Application.ActiveUIDocument;
                var doc = uidoc.Document;
                Reference r;
                try { r = uidoc.Selection.PickObject(ObjectType.Element, "Pick the Room to issue an RDS for"); }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
                var room = doc.GetElement(r) as Room;
                if (room == null) { TaskDialog.Show("STING — RDS", "Selected element is not a Room."); return Result.Failed; }

                var path = RdsRenderer.Render(doc, room);
                if (string.IsNullOrEmpty(path))
                    TaskDialog.Show("STING — RDS", "RDS render failed (template missing or write error). See StingTools.log.");
                else
                    TaskDialog.Show("STING — RDS issued", $"Room Data Sheet written:\n{path}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("IssueRoomDataSheetCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
