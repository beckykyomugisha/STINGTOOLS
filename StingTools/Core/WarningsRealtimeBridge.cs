using System;
using Newtonsoft.Json.Linq;
using StingTools.BIMManager;

namespace StingTools.Core
{
    /// <summary>
    /// IM Phase 3 — subscribes to the server's "WarningsReported" broadcast so a
    /// warning report pushed by ANOTHER session (or the mobile app) invalidates this
    /// session's warning cache instead of leaving the BCC showing stale counts for
    /// as long as the document stays open.
    ///
    /// THREADING CONTRACT — the whole point of this class.
    /// PlanscapeRealtimeClient.Raise invokes subscribers SYNCHRONOUSLY on the SignalR
    /// callback thread. That thread may not:
    ///   • touch any Revit API — no Document, no Element, no transaction. Doing so
    ///     from a non-Revit thread is undefined behaviour and typically a hard crash;
    ///   • mutate a collection the Revit thread may be enumerating — which rules out
    ///     WarningsEngine.InvalidateReportCache(), since it clears _classificationCache;
    ///   • touch WPF elements other than through a Dispatcher.
    ///
    /// So it does exactly two safe things: sets a volatile bool
    /// (WarningsEngine.MarkRemoteStale) that the Revit thread consumes on its next
    /// scan, and records a display hint in volatile fields the UI reads on next paint.
    /// No Revit call, no lock, no Dispatcher needed — nothing here can deadlock or
    /// throw into the socket pump.
    /// </summary>
    internal static class WarningsRealtimeBridge
    {
        private static bool _wired;
        private static readonly object _wireLock = new object();

        // Volatile so the Revit/UI thread sees the socket thread's write. Reference
        // assignment is atomic; readers take a local copy before use.
        private static volatile string _lastRemoteHint;
        private static volatile string _lastRemoteAtIso;

        /// <summary>
        /// Last "another session reported N warnings" hint, or null. Read on the UI
        /// thread when a panel paints; never blocks.
        /// </summary>
        internal static string LastRemoteHint => _lastRemoteHint;

        /// <summary>UTC ISO-8601 timestamp of that hint, or null.</summary>
        internal static string LastRemoteAtIso => _lastRemoteAtIso;

        /// <summary>Clear the hint once the UI has shown it.</summary>
        internal static void ClearHint()
        {
            _lastRemoteHint = null;
            _lastRemoteAtIso = null;
        }

        /// <summary>
        /// Idempotent — safe to call on every login / reconnect. Only the first call
        /// attaches the handler, so repeated connects cannot multiply subscriptions.
        /// </summary>
        internal static void Wire()
        {
            if (_wired) return;
            lock (_wireLock)
            {
                if (_wired) return;
                try
                {
                    PlanscapeRealtimeClient.Instance.WarningsReported += OnWarningsReported;
                    _wired = true;
                    StingLog.Info("WarningsRealtimeBridge: subscribed to WarningsReported.");
                }
                catch (Exception ex) { StingLog.Warn($"WarningsRealtimeBridge.Wire: {ex.Message}"); }
            }
        }

        /// <summary>
        /// Runs on the SignalR callback thread. Must never throw — an exception here
        /// propagates into the hub's message pump.
        /// </summary>
        private static void OnWarningsReported(object sender, RealtimeEvent e)
        {
            try
            {
                JObject data = e?.Data;

                int total = ReadInt(data, "totalWarnings");
                int delta = ReadInt(data, "delta");
                int health = ReadInt(data, "healthScore");

                // (1) Thread-safe cache invalidation — a single volatile write.
                WarningsEngine.MarkRemoteStale();

                // (2) Display hint for the next paint. No WPF touched here.
                string arrow = delta > 0 ? $"↑{delta}" : delta < 0 ? $"↓{Math.Abs(delta)}" : "→0";
                _lastRemoteAtIso = DateTime.UtcNow.ToString("o");
                _lastRemoteHint = $"Another session reported {total} warnings ({arrow}, health {health}).";

                StingLog.Info($"WarningsReported received: total={total} delta={delta} health={health} " +
                              "— warning cache marked stale.");
            }
            catch (Exception ex)
            {
                // Swallow: this is a socket callback, not a user action.
                StingLog.Warn($"WarningsRealtimeBridge.OnWarningsReported: {ex.Message}");
            }
        }

        private static int ReadInt(JObject o, string key)
            => o?[key] != null && int.TryParse((string)o[key], out int v) ? v : 0;
    }
}
