// StingTools — Drawing Template Manager
//
// Launches the WPF editor dialog. Edits are saved to the project
// override <project>/_BIM_COORD/drawing_types.json; corporate
// baseline on disk is never mutated.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DrawingTypeEditorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            try
            {
                var doc = data?.Application?.ActiveUIDocument?.Document;
                var dlg = new DrawingTypeEditorDialog(doc);
                dlg.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingTypeEditor", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }
    }
}
