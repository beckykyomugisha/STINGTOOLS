// ClashScheduler.cs — schedule headless full clash runs (e.g., after every save, or nightly).
using System;
using System.IO;
using System.Reflection;
using System.Timers;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    /// <summary>
    /// B3: ExternalEvent handler that runs a full headless clash detection
    /// pass (mesh extraction → kernel → matrix → rule → group → persist) by
    /// invoking ClashRunCommand.Execute on the Revit API thread. Distinct
    /// from LiveClashHandler — the live handler only drains pending dirty
    /// edits via NarrowPhaseFor, so an idle model with no dirty queue would
    /// produce a no-op tick and never refresh clashes.json.
    /// </summary>
    internal sealed class ClashRunEventHandler : IExternalEventHandler
    {
        private static ClashRunEventHandler _inst;
        public static ExternalEvent Event { get; private set; }

        public static ClashRunEventHandler Instance
        {
            get
            {
                if (_inst == null) { _inst = new ClashRunEventHandler(); Event = ExternalEvent.Create(_inst); }
                return _inst;
            }
        }

        public string GetName() => "STING Clash Run Event";

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app?.ActiveUIDocument?.Document;
                if (doc == null) return;
                ClashRunCommand.RunHeadless(app);
            }
            catch (Exception ex) { StingLog.Error("ClashRunEventHandler.Execute", ex); }
        }
    }

    public sealed class ClashScheduler
    {
        private static ClashScheduler _inst;
        public static ClashScheduler Instance => _inst ?? (_inst = new ClashScheduler());
        private Timer _timer;
        // B3: Track last-run timestamp so the dirty-model gate suppresses
        // ticks when no element has been edited since the previous run.
        private DateTime _lastRunUtc = DateTime.MinValue;
        // F4: Event subscriptions — kept in fields so we can detach on Stop.
        private UIApplication _subscribedApp;
        private EventHandler<Autodesk.Revit.DB.Events.DocumentSavedEventArgs> _onSaved;
        private EventHandler<Autodesk.Revit.DB.Events.DocumentSynchronizedWithCentralEventArgs> _onSynced;

        public void StartHourly(UIApplication app) => Start(app, intervalMinutes: 0);

        /// <summary>
        /// B3: Start the scheduler with a configurable interval. When
        /// <paramref name="intervalMinutes"/> is 0, the per-project value
        /// from default_clash_matrix.json's <c>SchedulerIntervalMinutes</c>
        /// is used (default 60). The previous Hourly entry point is now a
        /// thin wrapper.
        /// </summary>
        public void Start(UIApplication app, int intervalMinutes)
        {
            _timer?.Stop();

            // rec-21: Eagerly build LiveClashHandler.Instance + the live ExternalEvent.
            //         Stays correct even when the scheduler is the only consumer.
            try
            {
                var _ = LiveClashHandler.Instance;
                if (LiveClashHandler.Event == null)
                    StingLog.Warn("ClashScheduler.Start: LiveClashHandler.Event is null after Instance access.");
            }
            catch (Exception ex) { StingLog.Warn("ClashScheduler lazy-init: " + ex.Message); }

            // B3: Eagerly build the dedicated ClashRunEvent so ticks dispatch
            //     a real ClashRunCommand pass (not just drain the live queue).
            try
            {
                var _ = ClashRunEventHandler.Instance;
                if (ClashRunEventHandler.Event == null)
                    StingLog.Warn("ClashScheduler.Start: ClashRunEvent is null after Instance access.");
            }
            catch (Exception ex) { StingLog.Warn("ClashRunEvent lazy-init: " + ex.Message); }

            int minutes = intervalMinutes > 0 ? intervalMinutes : ReadIntervalFromMatrix();
            if (minutes <= 0) minutes = 60;

            _timer = new Timer(minutes * 60 * 1000) { AutoReset = true };
            _timer.Elapsed += (s, e) => OnTick(app);
            _timer.Start();

            // F4: Event-driven supplements to the periodic tick. DocumentSaved
            //     and DocumentSynchronizedWithCentral fire after every user
            //     save / Sync With Central — instant feedback rather than
            //     waiting up to {minutes} for the next poll. Mesh cache also
            //     gets invalidated so the next run picks up post-save state.
            try
            {
                if (_subscribedApp != null) DetachEvents();
                _subscribedApp = app;
                _onSaved = (s, e) => OnDocumentChanged(app, "DocumentSaved");
                _onSynced = (s, e) => OnDocumentChanged(app, "SyncedWithCentral");
                app.Application.DocumentSaved += _onSaved;
                app.Application.DocumentSynchronizedWithCentral += _onSynced;
                StingLog.Info("ClashScheduler: subscribed to DocumentSaved + DocumentSynchronizedWithCentral");
            }
            catch (Exception ex) { StingLog.Warn($"ClashScheduler event subscribe: {ex.Message}"); }

            StingLog.Info($"ClashScheduler started ({minutes}-minute tick)");
        }

        /// <summary>
        /// F4: Save / Sync hook. Invalidates mesh cache (post-save state)
        /// and raises the run event. Throttled by the dirty gate so a save
        /// without edits is still a no-op.
        /// </summary>
        private void OnDocumentChanged(UIApplication app, string trigger)
        {
            try
            {
                var doc = app?.ActiveUIDocument?.Document;
                if (doc != null) MeshExtractor.InvalidateCacheFor(doc);
                StingLog.Info($"ClashScheduler {trigger} → run requested");
                OnTick(app);
            }
            catch (Exception ex) { StingLog.Warn($"ClashScheduler.OnDocumentChanged({trigger}): {ex.Message}"); }
        }

        private void DetachEvents()
        {
            try
            {
                if (_subscribedApp == null) return;
                if (_onSaved != null) _subscribedApp.Application.DocumentSaved -= _onSaved;
                if (_onSynced != null) _subscribedApp.Application.DocumentSynchronizedWithCentral -= _onSynced;
            }
            catch (Exception ex) { StingLog.Warn($"ClashScheduler.DetachEvents: {ex.Message}"); }
            _subscribedApp = null; _onSaved = null; _onSynced = null;
        }

        private void OnTick(UIApplication app)
        {
            try
            {
                // B3: Dirty-model gate. Skip the tick when no element has been
                // dirtied since the last run. Reads ClashSession.LastDirtyAtUtc
                // (volatile) for the active document.
                var doc = app?.ActiveUIDocument?.Document;
                if (doc != null)
                {
                    try
                    {
                        var session = ClashSession.ForDocument(doc);
                        if (_lastRunUtc != DateTime.MinValue && session.LastDirtyAtUtc <= _lastRunUtc)
                        {
                            // No dirt since last run; nothing meaningful to recompute.
                            return;
                        }
                    }
                    catch (Exception sessionEx) { StingLog.Warn($"ClashScheduler dirty gate: {sessionEx.Message}"); }
                }

                if (ClashRunEventHandler.Event == null)
                {
                    StingLog.Warn("ClashScheduler tick skipped: ClashRunEvent is null");
                    return;
                }
                _lastRunUtc = DateTime.UtcNow;
                ClashRunEventHandler.Event.Raise();
            }
            catch (Exception ex) { StingLog.Warn("ClashScheduler tick: " + ex.Message); }
        }

        /// <summary>
        /// B3: Read SchedulerIntervalMinutes from default_clash_matrix.json.
        /// Falls back to 60 minutes when missing or unparseable.
        /// </summary>
        private static int ReadIntervalFromMatrix()
        {
            try
            {
                string dll = Assembly.GetExecutingAssembly().Location;
                string dllDir = Path.GetDirectoryName(dll) ?? "";
                string[] candidates =
                {
                    Path.Combine(dllDir, "data", "clash", "default_clash_matrix.json"),
                    Path.Combine(dllDir, "data", "default_clash_matrix.json"),
                };
                foreach (var path in candidates)
                {
                    if (!File.Exists(path)) continue;
                    var json = JObject.Parse(File.ReadAllText(path));
                    var n = (int?)json["SchedulerIntervalMinutes"];
                    if (n.HasValue && n.Value > 0) return n.Value;
                    n = (int?)json["scheduler_interval_minutes"];
                    if (n.HasValue && n.Value > 0) return n.Value;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ClashScheduler.ReadIntervalFromMatrix: {ex.Message}"); }
            return 60;
        }

        public void Stop()
        {
            _timer?.Stop();
            _timer = null;
            DetachEvents();
        }
    }
}
