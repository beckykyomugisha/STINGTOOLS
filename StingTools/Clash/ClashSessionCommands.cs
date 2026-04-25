// ClashSessionCommands.cs — BCC-dispatchable wrappers for the inline dispatch
// handlers in StingCommandHandler. BCC's action router
// (BIMCoordinationCenterCommand.DispatchCoordAction) resolves action tags via
// WorkflowEngine.GetCommandInstance which expects a real IExternalCommand
// instance — it can't run the inline switch-case handlers that
// StingCommandHandler uses for the dockable panel.
//
// These commands replicate the inline handlers so both paths (dockable panel
// tag dispatch AND BCC ActionDispatcher) resolve to the same behaviour.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;   // ElementSet — IExternalCommand.Execute signature
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ClashSessionRefreshCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) return Result.Cancelled;
                ClashSession.Clear(doc);
                TaskDialog.Show("STING Clash",
                    "Live-clash session cleared. It will re-initialise from the active " +
                    "3D view on the next element edit.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ClashSessionRefresh: {ex.Message}");
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ClashSessionClearCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc != null) ClashSession.Clear(doc);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ClashSessionClear: {ex.Message}");
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ClashMatrixEditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                string path = Path.Combine(dllDir, "data", "clash", "default_clash_matrix.json");
                if (File.Exists(path))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                        return Result.Succeeded;
                    }
                    catch (Exception startEx)
                    {
                        StingLog.Warn($"ClashMatrixEdit start: {startEx.Message}");
                        TaskDialog.Show("STING Clash",
                            $"Could not open editor for:\n{path}\n\n{startEx.Message}");
                        return Result.Failed;
                    }
                }
                TaskDialog.Show("STING Clash",
                    $"default_clash_matrix.json not found.\n\nExpected: {path}");
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ClashMatrixEdit: {ex.Message}");
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
