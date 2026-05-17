// StingTools — Manual IFC drop-folder import command.
//
// Tag: IFC_DropImport
//
// Lets the user pick an .ifc file directly (bypassing the watcher) and
// imports it immediately.  Useful when the drop-folder watcher is not
// running or when the user wants to import a specific file.

using System;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingBridge.IFC;
using StingTools.Core;

namespace StingTools.Commands.IFC
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class IfcDropImportCommand : IExternalCommand
    {
        public const string Tag = "IFC_DropImport";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // Mode picker.
            var modeDlg = new TaskDialog("STING — IFC Import")
            {
                MainInstruction = "IFC Import mode",
                MainContent     = "How should the IFC file be brought into Revit?",
                CommonButtons   = TaskDialogCommonButtons.Cancel
            };
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Link IFC (recommended)",
                "Keeps the IFC as a live linked document. Non-destructive.");
            modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Import IFC",
                "Converts IFC geometry into native Revit elements.");

            var modeResult = modeDlg.Show();
            if (modeResult == TaskDialogResult.Cancel) return Result.Cancelled;

            IfcImportMode mode = modeResult == TaskDialogResult.CommandLink2
                ? IfcImportMode.Import
                : IfcImportMode.Link;

            // File picker.
            var dlg = new Autodesk.Revit.UI.FileOpenDialog("IFC files (*.ifc)|*.ifc");
            dlg.Title = "Select IFC file to import";
            if (dlg.Show() != ItemSelectionDialogResult.Confirmed) return Result.Cancelled;

            string ifcPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(
                dlg.GetSelectedModelPath());

            if (!File.Exists(ifcPath))
            {
                TaskDialog.Show("IFC Import", $"File not found:\n{ifcPath}");
                return Result.Failed;
            }

            try
            {
                IfcImportResult result = IfcRevitImporter.Import(doc, ifcPath, mode, applyTags: true);

                if (result.Success)
                    TaskDialog.Show("IFC Import",
                        $"Import complete.\n\n" +
                        $"File:            {Path.GetFileName(ifcPath)}\n" +
                        $"Mode:            {mode}\n" +
                        $"Elements stamped: {result.ElementsTagged}");
                else
                    TaskDialog.Show("IFC Import", $"Import failed:\n{result.ErrorMessage}");

                return result.Success ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex)
            {
                StingLog.Error("IfcDropImportCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
