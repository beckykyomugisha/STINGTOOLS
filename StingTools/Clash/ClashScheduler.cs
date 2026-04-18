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
            _timer = new Timer(60 * 60 * 1000) { AutoReset = true };
            _timer.Elapsed += (s, e) =>
            {
                try { LiveClashHandler.Event?.Raise(); }
                catch (Exception ex) { StingLog.Warn("ClashScheduler tick: " + ex.Message); }
            };
            _timer.Start();
        }

        public void Stop() { _timer?.Stop(); _timer = null; }
    }
}
