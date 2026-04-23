// StingTools v4 MVP — Phase L tray-fill widget command.
//
// Pick a cable tray or conduit; compute fill via TrayFillCalculator;
// show the cross-section in the TrayFillWindow. Works best on
// projects whose CableManifest is populated (Phase J AddCable),
// but degrades gracefully to "empty tray" rendering.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StingTools.Core;
using StingTools.Core.Electrical;
using StingTools.UI;

namespace StingTools.Commands.Electrical
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ShowTrayFillCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc   = ctx.Doc;
            var uidoc = ctx.UIDoc;

            Element tray;
            try
            {
                var reference = uidoc.Selection.PickObject(ObjectType.Element,
                    new TrayFilter(), "Pick a cable tray or conduit");
                tray = doc.GetElement(reference);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }

            var manifest = CableManifest.Load(doc);
            var report   = TrayFillCalculator.Compute(doc, tray, manifest);

            var win = new TrayFillWindow();
            win.Display(report);
            win.Show();
            return Result.Succeeded;
        }
    }

    internal class TrayFilter : ISelectionFilter
    {
        public bool AllowElement(Element el) => el is CableTray || el is Conduit;
        public bool AllowReference(Reference r, XYZ p) => true;
    }
}
