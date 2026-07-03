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
// Flow (sync — Run):
//   MCP thread            :  Run(job) → enqueue → event.Raise()
//   Revit API thread      :  Execute() drains queue → job.Func(uiApp) → Done.Set()
//   MCP thread            :  Done.Wait(timeout) → return job.Result (or typed error)
//
// Flow (async — Submit, for project-scope / long writes where a 15s timeout would
// be ambiguous — a timed-out write may have committed):
//   MCP thread            :  Submit(job) → enqueue → Raise() → return jobId immediately
//   Revit API thread      :  Execute() runs job to completion → store _completed[jobId]
//   MCP thread (later)    :  get_job_status(jobId) → TryGetResult → running | read-back
//
// Registration: the ExternalEvent is created once at startup by Initialise(),
// called from StingToolsApp.OnStartup alongside StingDockPanel.Initialise. Lazy
// creation from the MCP thread is NOT possible — ExternalEvent.Create must run in
// a valid Revit API context, which the ThreadPool thread is not.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
            // Async (Submit) jobs carry a public jobId; the handler stores their result
            // in _completed[JobId] rather than relying on a waiter.
            public bool Async;
            public string JobId;
        }

        private static readonly ConcurrentQueue<Job> _queue = new ConcurrentQueue<Job>();
        private static ExternalEvent _event;
        private static JobHandler _handler;
        private static readonly object _initLock = new object();

        // ── Async job store (bounded) ────────────────────────────────────────────
        private static readonly object _asyncLock = new object();
        private static readonly HashSet<string> _submitted = new HashSet<string>();
        private static readonly Dictionary<string, McpJobResult> _completed = new Dictionary<string, McpJobResult>();
        private static readonly Queue<string> _completedOrder = new Queue<string>();
        private const int MaxCompleted = 200;

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

        // ── Async submit / poll ──────────────────────────────────────────────────

        /// <summary>
        /// Enqueue <paramref name="job"/> and return a jobId IMMEDIATELY without blocking.
        /// The API-thread handler runs it to completion and stores the result; poll with
        /// <see cref="TryGetResult"/> (surfaced by the get_job_status tool). Use for
        /// project-scope / long writes where a synchronous 15s timeout would be ambiguous.
        /// </summary>
        public static string Submit(Func<UIApplication, McpJobResult> job)
        {
            string jobId = Guid.NewGuid().ToString("N");

            if (job == null)
            {
                StoreCompleted(jobId, McpJobResult.Error("bad_request", "No job supplied."));
                return jobId;
            }
            if (_event == null)
            {
                StoreCompleted(jobId, McpJobResult.Error("bridge_not_ready",
                    "MCP job bridge is not initialised. Restart Revit with STING Tools loaded."));
                return jobId;
            }

            var unit = new Job { Id = Guid.NewGuid(), JobId = jobId, Async = true, Func = job };
            lock (_asyncLock) { _submitted.Add(jobId); }
            _queue.Enqueue(unit);

            ExternalEventRequest raised;
            try
            {
                raised = _event.Raise();
            }
            catch (Exception ex)
            {
                unit.Abandoned = true;
                StingLog.Warn($"McpJobBridge.Submit Raise threw: {ex.Message}");
                StoreCompleted(jobId, McpJobResult.Error("revit_busy",
                    "Revit did not accept the request (raise failed). Retry shortly."));
                return jobId;
            }

            if (raised != ExternalEventRequest.Accepted &&
                raised != ExternalEventRequest.Pending)
            {
                unit.Abandoned = true;
                StoreCompleted(jobId, McpJobResult.Error("revit_busy",
                    "Revit is in a modal dialog / transaction / sync — retry shortly."));
            }

            return jobId;
        }

        /// <summary>
        /// completed → true + result; submitted-but-still-running → true + null;
        /// unknown jobId → false.
        /// </summary>
        public static bool TryGetResult(string jobId, out McpJobResult result)
        {
            lock (_asyncLock)
            {
                if (jobId != null && _completed.TryGetValue(jobId, out result)) return true;
                if (jobId != null && _submitted.Contains(jobId)) { result = null; return true; }
                result = null;
                return false;
            }
        }

        private static void StoreCompleted(string jobId, McpJobResult r)
        {
            lock (_asyncLock)
            {
                _submitted.Remove(jobId);
                if (!_completed.ContainsKey(jobId)) _completedOrder.Enqueue(jobId);
                _completed[jobId] = r;
                while (_completedOrder.Count > MaxCompleted)
                {
                    string old = _completedOrder.Dequeue();
                    _completed.Remove(old);
                }
            }
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
                        // Async jobs: publish the result to the completed store for polling.
                        if (unit.Async) StoreCompleted(unit.JobId, unit.Result);
                        // Sync jobs: signal the waiter (unless it already gave up).
                        try { if (!unit.Abandoned) unit.Done.Set(); }
                        catch (Exception ex) { StingLog.Warn($"McpJobBridge signal failed: {ex.Message}"); }
                    }
                }
            }
        }
    }
}
