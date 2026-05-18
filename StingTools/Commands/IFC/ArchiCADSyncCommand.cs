// StingTools — ArchiCAD / IFC sync command.
//
// Entry point: TAGS → IFC → "ArchiCAD Sync"
// Tag: IFC_ArchiCADSync
//
// Runs the ArchiCAD ↔ Revit workflow via StingBridge:
//   1. Tries the ArchiCAD Live Link (named-pipe, fast).
//   2. Falls back to scanning the project's _ifc_drop/ folder.
//
// The drop folder lives at <project>/_ifc_drop/ by default; users can
// override via IFC_DROP_FOLDER_TXT in ProjectInformation.

using System;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingBridge.ArchiCAD;
using StingTools.Core;

namespace StingTools.Commands.IFC
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ArchiCADSyncCommand : IExternalCommand
    {
        public const string Tag = "IFC_ArchiCADSync";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            string dropFolder = ResolveDropFolder(doc);

            var td = new TaskDialog("STING — ArchiCAD / IFC Sync")
            {
                MainInstruction = "Sync ArchiCAD model with Revit",
                MainContent     =
                    "This command synchronises the ArchiCAD model into Revit via IFC.\n\n" +
                    $"Drop folder: {dropFolder}\n\n" +
                    "STING will first attempt a live connection to ArchiCAD.\n" +
                    "If ArchiCAD is not open, it will process any .ifc files\n" +
                    "already waiting in the drop folder.",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel
            };

            if (td.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

            try
            {
                SyncResult result = ArchiCADWorkflowAdapter.Sync(doc, dropFolder, liveFirst: true);

                TaskDialog.Show("ArchiCAD / IFC Sync",
                    $"Path taken : {result.Path}\n\n{result.Summary}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ArchiCADSyncCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string ResolveDropFolder(Document doc)
        {
            // User-overridable via ProjectInformation parameter.
            string custom = doc.ProjectInformation?
                .LookupParameter("IFC_DROP_FOLDER_TXT")?.AsString();

            if (!string.IsNullOrWhiteSpace(custom) && Directory.Exists(custom))
                return custom;

            // Default: alongside the .rvt file.
            string rvtDir = Path.GetDirectoryName(doc.PathName);
            if (!string.IsNullOrWhiteSpace(rvtDir))
                return Path.Combine(rvtDir, "_ifc_drop");

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "STING", "_ifc_drop");
        }
    }
}
