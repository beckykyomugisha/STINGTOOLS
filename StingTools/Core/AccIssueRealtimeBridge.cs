using System;
using System.Threading.Tasks;
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
    /// </summary>
    internal static class AccIssueRealtimeBridge
    {
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
            Clash.AccImportIssuesCommand.MarkNextRunAutomatic();
            bool ok = UI.StingDockPanel.DispatchCommand("ACC_ImportIssues");
            if (!ok) Clash.AccImportIssuesCommand.CancelAutomatic();   // or the next MANUAL run would go silent
            if (!ok)
                StingLog.Warn("AccIssueRealtimeBridge: could not queue ACC_ImportIssues (the STING command " +
                              "dispatcher is not initialised or Revit refused the request) — run Import Issues manually.");
        }
    }
}
