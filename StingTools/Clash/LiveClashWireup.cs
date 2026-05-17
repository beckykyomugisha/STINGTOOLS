using StingTools.Core;
// LiveClashWireup.cs — subscribes to DocumentChanged and raises LiveClashHandler.Event
// whenever LiveClashUpdater has queued any dirty elements. Called once at app start.
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;

namespace StingTools.Core.Clash
{
    public static class LiveClashWireup
    {
        private static bool _subscribed;

        public static void Subscribe(UIControlledApplication uiApp)
        {
            if (_subscribed) return;
            uiApp.ControlledApplication.DocumentChanged += (s, e) =>
            {
                if (!LiveClashUpdater.DirtyQueue.IsEmpty)
                {
                    // Ensure lazy init, then raise — both lines are intentionally inside the
                    // guard so we don't raise when there's nothing to do.
                    var _ = LiveClashHandler.Instance;
                    LiveClashHandler.Event?.Raise();
                }
            };
            // rec-20: Clear the per-document ClashSession singleton when Revit
            // tells us the document is closing. Without this, stale mesh + OBB
            // trees for the just-closed doc sit in memory forever (on a project
            // with 50k elements that's 100-200 MB leaked per close/reopen cycle)
            // AND — worse — if the document is later reopened with geometry
            // changes made outside Revit, the stale session returns false
            // clash verdicts from the old mesh cache.
            uiApp.ControlledApplication.DocumentClosing += (s, e) =>
            {
                try
                {
                    if (e?.Document != null) ClashSession.Clear(e.Document);
                }
                catch (System.Exception ex)
                {
                    StingTools.Core.StingLog.Warn($"ClashSession.Clear on DocumentClosing: {ex.Message}");
                }
            };
            _subscribed = true;
        }
    }
}
