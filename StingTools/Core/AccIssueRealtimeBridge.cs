using System;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using StingTools.BIMManager;
using StingTools.V6;

namespace StingTools.Core
{
    /// <summary>
    /// Auto-import of ACC issues on the Planscape server's "acc.issue.changed" relay
    /// (AutodeskWebhooksController → NotificationHub project group → PlanscapeRealtimeClient).
    ///
    /// THREADING CONTRACT (same as WarningsRealtimeBridge). The handler runs on the SignalR
    /// callback thread, so it touches no Revit API and no Document. It only decides WHEN
    /// (AccAutoImportGate: at most one run per 60 s, with one trailing run for signals that
    /// arrive inside the window) and then queues ACC_ImportIssues through the dock panel's
    /// ExternalEvent — ExternalEvent.Raise is the one Revit call documented as safe from any
    /// thread. Whether the project opted in ("autoImportIssues": true) is checked by the
    /// command itself, on the Revit thread, against the ACTIVE document; a project that did
    /// not opt in logs one line and imports nothing.
    ///
    /// Wired from WarningsRealtimeBridge.Wire(), which runs on every Planscape login.
    ///
    /// OWN EXTERNAL EVENT. The import is queued on a dedicated ExternalEvent, never on the dock
    /// panel's shared one: that handler holds ONE pending command tag, so a signal arriving while
    /// Revit was busy overwrote a command the user had just clicked (which then never ran), and a
    /// static "next run is automatic" flag could end up attached to the wrong run — making a
    /// manual import silent, or an automatic one interactive on a project that never opted in.
    /// The dedicated handler runs the import with automatic=true explicitly.
    /// </summary>
    internal static class AccIssueRealtimeBridge
    {
        private static ExternalEvent _event;

        /// <summary>Create the dedicated ExternalEvent. Must run in a Revit API context (startup).</summary>
        internal static void Initialise()
        {
            if (_event != null) return;
            try { _event = ExternalEvent.Create(new AccAutoImportHandler()); }
            catch (Exception ex) { StingLog.Warn("AccIssueRealtimeBridge.Initialise: " + ex.Message); }
        }

        private sealed class AccAutoImportHandler : IExternalEventHandler
        {
            public void Execute(UIApplication app)
            {
                try
                {
                    UI.StingCommandHandler.SetCurrentApp(app);
                    var r = new Clash.AccImportIssuesCommand().Run(null, forceFull: false, automatic: true);
                    StingLog.Info("ACC_ImportIssues (auto): " + r);
                }
                catch (Exception ex) { StingLog.Error("ACC_ImportIssues (auto)", ex); }
            }

            public string GetName() => "STING ACC auto-import";
        }

        private static bool _wired;
        private static readonly object _wireLock = new object();
        private static readonly AccAutoImportGate _gate = new AccAutoImportGate();

        internal static void Wire()
        {
            if (_wired) return;
            lock (_wireLock)
            {
                if (_wired) return;
                try
                {
                    PlanscapeRealtimeClient.Instance.AccIssueChanged += OnAccIssueChanged;
                    _wired = true;
                    StingLog.Info("AccIssueRealtimeBridge: subscribed to acc.issue.changed.");
                }
                catch (Exception ex) { StingLog.Warn($"AccIssueRealtimeBridge.Wire: {ex.Message}"); }
            }
        }

        /// <summary>SignalR callback thread. Must never throw.</summary>
        private static void OnAccIssueChanged(object sender, RealtimeEvent e)
        {
            try
            {
                string id = (string)e?.Data?["accIssueId"] ?? "?";
                string ev = (string)e?.Data?["event"] ?? "?";
                var decision = _gate.OnSignal(DateTime.UtcNow, out TimeSpan delay);
                StingLog.Info($"acc.issue.changed received ({ev}, ACC issue {id}) — {decision}" +
                              (decision == AccAutoImportDecision.ScheduleTrailing ? $" in {delay.TotalSeconds:F0} s" : ""));
                switch (decision)
                {
                    case AccAutoImportDecision.RunNow:
                        Queue();
                        break;
                    case AccAutoImportDecision.ScheduleTrailing:
                        Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(delay).ConfigureAwait(false);
                                _gate.OnTrailingFired(DateTime.UtcNow);
                                Queue();
                            }
                            catch (Exception ex) { StingLog.Warn($"AccIssueRealtimeBridge trailing run: {ex.Message}"); }
                        });
                        break;
                }
            }
            catch (Exception ex) { StingLog.Warn($"AccIssueRealtimeBridge.OnAccIssueChanged: {ex.Message}"); }
        }

        private static void Queue()
        {
            ExternalEventRequest req = _event == null ? ExternalEventRequest.Denied : _event.Raise();
            // Pending = already queued and not yet run: the queued run covers this signal too.
            if (req != ExternalEventRequest.Accepted && req != ExternalEventRequest.Pending)
                StingLog.Warn($"AccIssueRealtimeBridge: could not queue the ACC auto-import ({req}; " +
                              (_event == null ? "the event was not created at startup" : "Revit refused the request") +
                              ") — run Import Issues manually.");
        }
    }
}
