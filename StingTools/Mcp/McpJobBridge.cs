// ════════════════════════════════════════════════════════════════════════════
// McpJobBridge — marshals MCP work onto the Revit API thread, synchronously
//
// The MCP HTTP handler runs on an HttpListener ThreadPool thread with NO Revit
// API access. Any tool that touches the document must run its work on Revit's API
// thread and return the result to the waiting HTTP thread. This bridge is a
// DEDICATED ExternalEvent + IExternalEventHandler, deliberately SEPARATE from
// StingCommandHandler so MCP jobs never entangle the panel's _executeDepth
// re-entrancy state or its command-tag store.
//
// Flow (per call):
//   MCP thread            :  Run(job) → enqueue → event.Raise()
//   Revit API thread      :  Execute() drains queue → job.Func(uiApp) → Done.Set()
//   MCP thread            :  Done.Wait(timeout) → return job.Result (or typed error)
//
// Registration: the ExternalEvent is created once at startup by Initialise(),
// called from StingToolsApp.OnStartup alongside StingDockPanel.Initialise. Lazy
// creation from the MCP thread is NOT possible — ExternalEvent.Create must run in
// a valid Revit API context, which the ThreadPool thread is not.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Concurrent;
using System.Threading;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Mcp
{
    internal static class McpJobBridge
    {
        // One job unit of work handed from the MCP thread to the API thread.
        private sealed class Job
        {
            public Guid Id;
            public Func<UIApplication, McpJobResult> Func;
            public McpJobResult Result;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            // Set by the MCP thread when it stops waiting (revit_busy / timeout) so
            // the API thread skips the job instead of running an orphaned read/write.
            public volatile bool Abandoned;
        }

        private static readonly ConcurrentQueue<Job> _queue = new ConcurrentQueue<Job>();
        private static ExternalEvent _event;
        private static JobHandler _handler;
        private static readonly object _initLock = new object();

        /// <summary>
        /// Create the bridge's ExternalEvent. Must be called once from the Revit API
        /// context (StingToolsApp.OnStartup). Idempotent.
        /// </summary>
        public static void Initialise()
        {
            lock (_initLock)
            {
                if (_event != null) return;
                _handler = new JobHandler();
                _event = ExternalEvent.Create(_handler);
                StingLog.Info("McpJobBridge initialised — dedicated ExternalEvent ready.");
            }
        }

        /// <summary>
        /// Run <paramref name="job"/> on the Revit API thread and block up to
        /// <paramref name="timeoutMs"/> for its result. Callable from the MCP thread.
        /// Returns a typed error (revit_busy / timeout / bridge_not_ready) instead of
        /// throwing. The job itself should call McpSafety.RequireLicense() then
        /// RequireDocument(uiApp) first, and must never open modal UI.
        /// </summary>
        public static McpJobResult Run(Func<UIApplication, McpJobResult> job, int timeoutMs = 15000)
        {
            if (job == null)
                return McpJobResult.Error("bad_request", "No job supplied.");

            if (_event == null)
                return McpJobResult.Error("bridge_not_ready",
                    "MCP job bridge is not initialised. Restart Revit with STING Tools loaded.");

            var unit = new Job { Id = Guid.NewGuid(), Func = job };
            _queue.Enqueue(unit);

            ExternalEventRequest raised;
            try
            {
                raised = _event.Raise();
            }
            catch (Exception ex)
            {
                unit.Abandoned = true;
                StingLog.Warn($"McpJobBridge.Raise threw: {ex.Message}");
                return McpJobResult.Error("revit_busy",
                    "Revit did not accept the request (raise failed). Retry shortly.");
            }

            if (raised != ExternalEventRequest.Accepted &&
                raised != ExternalEventRequest.Pending)
            {
                // Accepted and Pending both mean the request IS registered and will be
                // honoured — the pending Execute drains the whole queue, so the job still
                // runs. Only a truly non-accepted result (Denied) becomes revit_busy;
                // abandoning Pending would spuriously fail concurrent/pipelined calls.
                unit.Abandoned = true;
                return McpJobResult.Error("revit_busy",
                    "Revit is in a modal dialog / transaction / sync — retry shortly.");
            }

            if (!unit.Done.Wait(timeoutMs))
            {
                // Timed out waiting for the API thread. Abandon so the job is skipped
                // if it is drained later. (We intentionally do not Dispose the MRE here
                // to avoid an ObjectDisposedException if the API thread races us.)
                unit.Abandoned = true;
                return McpJobResult.Error("timeout",
                    $"Revit did not complete the job within {timeoutMs} ms. It may be busy — retry shortly.");
            }

            return unit.Result ?? McpJobResult.Error("exception", "Job produced no result.");
        }

        // ── API-thread handler ───────────────────────────────────────────────────

        private sealed class JobHandler : IExternalEventHandler
        {
            public string GetName() => "STING MCP Job Bridge";

            public void Execute(UIApplication app)
            {
                // Drain the whole queue — ExternalEvent.Raise() calls can coalesce, so
                // one Execute may need to service several enqueued jobs.
                while (_queue.TryDequeue(out Job unit))
                {
                    if (unit.Abandoned)
                    {
                        // Caller stopped waiting (revit_busy / timeout). Skip entirely so
                        // no orphaned read/write runs and we never touch a released MRE.
                        continue;
                    }

                    try
                    {
                        unit.Result = unit.Func(app) ??
                            McpJobResult.Error("exception", "Job returned null.");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"McpJobBridge job {unit.Id} threw: {ex.Message}");
                        unit.Result = McpJobResult.Error("exception", ex.Message);
                    }
                    finally
                    {
                        try { if (!unit.Abandoned) unit.Done.Set(); }
                        catch (Exception ex) { StingLog.Warn($"McpJobBridge signal failed: {ex.Message}"); }
                    }
                }
            }
        }
    }
}
