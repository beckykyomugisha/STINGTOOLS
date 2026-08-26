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
                var doc = (data?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document;

                // Phase 195 — single instance. Re-launching used to construct a
                // second editor that could open behind Revit; with Revit itself
                // input-blocked until the ExternalEvent completes there was then no
                // way to reach either window. Now a second launch just re-surfaces
                // the one that is already open.
                var existing = DrawingTypeEditorDialog.CurrentInstance;
                if (existing != null && existing.IsLoaded)
                {
                    StingWindowHelper.BringToFront(existing);
                    return Result.Succeeded;
                }

                var dlg = new DrawingTypeEditorDialog(doc);
                DrawingTypeEditorDialog.CurrentInstance = dlg;

                // Phase 137 — Show() (modeless) instead of ShowDialog() (modal).
                // A modal WPF window blocks Revit's ExternalEvent queue, so action
                // buttons inside the editor (Edit CSV…, Populate, Validate,
                // section/elevation runners, etc.) never fired their dispatched
                // commands. Modeless lets ExternalEvent.Raise complete.
                //
                // Phase 195 — ShowOwned() replaces the raw Show() + a hand-rolled
                // Process.GetCurrentProcess().MainWindowHandle owner. That handle is
                // frequently IntPtr.Zero inside Revit, which left the editor unowned
                // and behind the main window with no way to click through to it.
                // ShowOwned resolves the real Revit HWND, shows the window in the
                // taskbar, and forces it to the foreground.
                StingWindowHelper.ShowOwned(dlg);
                StingLog.Info("DrawingTypeEditor: opened (modeless, owned, foreground).");
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
