// ClashScheduler.cs — schedule headless full clash runs (e.g., after every save, or nightly).
using System;
using System.Timers;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    public sealed class ClashScheduler
    {
        private static ClashScheduler _inst;
        public static ClashScheduler Instance => _inst ?? (_inst = new ClashScheduler());
        private Timer _timer;

        public void StartHourly(UIApplication app)
        {
            _timer?.Stop();

            // rec-21: Ensure LiveClashHandler.Instance is eagerly constructed before
            // the timer starts. The singleton getter also builds the ExternalEvent
            // via ExternalEvent.Create; if the timer tick happens before any user
            // action has triggered that path, Event would be null and the tick
            // would silently no-op. Touching Instance once here is cheap and
            // guarantees Event is non-null for all subsequent ticks.
            try
            {
                var _ = LiveClashHandler.Instance;
                if (LiveClashHandler.Event == null)
                    StingLog.Warn("ClashScheduler.StartHourly: LiveClashHandler.Event is null after Instance access " +
                                  "— ExternalEvent may have failed to create. Scheduler ticks will no-op.");
            }
            catch (Exception ex) { StingLog.Warn("ClashScheduler lazy-init: " + ex.Message); }

            _timer = new Timer(60 * 60 * 1000) { AutoReset = true };
            _timer.Elapsed += (s, e) =>
            {
                try
                {
                    if (LiveClashHandler.Event == null)
                    {
                        StingLog.Warn("ClashScheduler tick skipped: LiveClashHandler.Event is null");
                        return;
                    }
                    LiveClashHandler.Event.Raise();
                }
                catch (Exception ex) { StingLog.Warn("ClashScheduler tick: " + ex.Message); }
            };
            _timer.Start();
            StingLog.Info("ClashScheduler started (hourly tick)");
        }

        public void Stop() { _timer?.Stop(); _timer = null; }
    }
}
