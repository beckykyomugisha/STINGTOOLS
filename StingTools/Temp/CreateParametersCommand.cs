using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Temp
{
    /// <summary>
    /// Ported from STINGTemp 1_Setup.panel — Create Parameters.
    /// Binds shared parameters from MR_PARAMETERS.txt to the active project.
    /// Delegates to the same logic as Tags.LoadSharedParamsCommand.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateParametersCommand : IExternalCommand, IPanelCommand
    {
        public Result Execute(UIApplication app)
        {
            try
            {
                var cmd = new Tags.LoadSharedParamsCommand();
                string message = "";
                var elements = new ElementSet();
                return cmd.Execute(null, ref message, elements);
            }
            catch (Exception ex)
            {
                StingLog.Error("CreateParametersCommand crashed", ex);
                try { TaskDialog.Show("Create Parameters", $"Error: {ex.Message}"); } catch { }
                return Result.Failed;
            }
        }

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var cmd = new Tags.LoadSharedParamsCommand();
            return cmd.Execute(commandData, ref message, elements);
        }
    }
}
