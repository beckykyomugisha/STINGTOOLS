// StingTools v4 MVP — Clash Manager entry point (Phase I.3).
//
// Opens the ClashManagerDialog — the missing UI for the 33-file
// clash engine whose 5 pipeline commands (ClashRun / SessionRefresh
// / SessionClear / MatrixEdit / BcfExport) had no visible buttons
// prior to this commit.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Mep
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClashManagerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            try
            {
                // Remember the UIApplication so the dialog's child
                // buttons can dispatch Clash commands through the
                // existing IExternalEventHandler. Use ctx.App so the
                // command works both from the Revit ribbon (commandData
                // populated) and the dockable panel (commandData null,
                // UIApplication resolved via StingCommandHandler.CurrentApp).
                ClashManagerDispatcher.Bind(ctx.App);
                var dlg = new ClashManagerDialog(ctx.UIDoc);

                // Parent the modeless window to Revit's main HWND so it
                // stays on top of the Revit document window instead of
                // appearing behind it (which makes the button feel dead).
                try
                {
                    var hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                    if (hwnd != IntPtr.Zero)
                        new System.Windows.Interop.WindowInteropHelper(dlg).Owner = hwnd;
                }
                catch (Exception ownerEx)
                {
                    StingLog.Warn($"ClashManagerCommand owner: {ownerEx.Message}");
                }

                dlg.Show();
                dlg.Activate();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ClashManagerCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Thin static dispatcher routing ClashManagerDialog's toolbar
    /// buttons through the existing StingDockPanel.DispatchCommand
    /// IExternalEventHandler. Keeps the dialog free of app/handler
    /// plumbing so it can be reused by future commands that need a
    /// modeless entry point.
    /// </summary>
    public static class ClashManagerDispatcher
    {
        public static void Bind(UIApplication app) { /* no-op — DispatchCommand is static */ }

        public static void Dispatch(string tag)
        {
            try
            {
                if (!StingTools.UI.StingDockPanel.DispatchCommand(tag))
                    StingLog.Warn($"ClashManagerDispatcher.Dispatch({tag}) — dock panel not yet opened");
            }
            catch (Exception ex)
            { StingLog.Warn($"ClashManagerDispatcher.Dispatch({tag}): {ex.Message}"); }
        }
    }
}
