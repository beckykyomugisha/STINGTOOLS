using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace StingTools.Commands.Licensing
{
    [Transaction(TransactionMode.ReadOnly)]
    public class ActivateStingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            StingTools.UI.ActivationDialog.ShowModal();
            return Result.Succeeded;
        }
    }
}
