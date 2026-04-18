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
            _subscribed = true;
        }
    }
}
