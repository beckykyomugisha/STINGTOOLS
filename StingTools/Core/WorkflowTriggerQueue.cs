using System;
using System.Collections.Generic;

namespace StingTools.Core
{
    /// <summary>
    /// DSCH-40 — the pending queue of workflow presets fired by WorkflowScheduler
    /// triggers (document-open, compliance-fall, SLA-violation, warning-threshold) and
    /// AUTO_RUN_WORKFLOW_ON_OPEN.
    ///
    /// A preset already waiting is not queued twice: a trigger that fires again before
    /// its preset has run adds nothing. Thread-safe. Revit-free; tested in
    /// StingTools.Tags.Tests.
    /// </summary>
    public sealed class WorkflowTriggerQueue
    {
        private readonly object _lock = new object();
        private readonly Queue<string> _queue = new Queue<string>();
        private readonly HashSet<string> _pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Queue a preset. False when the name is blank or already waiting.</summary>
        public bool Enqueue(string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName)) return false;
            string name = presetName.Trim();
            lock (_lock)
            {
                if (!_pending.Add(name)) return false;
                _queue.Enqueue(name);
                return true;
            }
        }

        /// <summary>The next preset, or null when none is waiting.</summary>
        public string TryDequeue()
        {
            lock (_lock)
            {
                if (_queue.Count == 0) return null;
                string name = _queue.Dequeue();
                _pending.Remove(name);
                return name;
            }
        }

        public bool IsEmpty { get { lock (_lock) return _queue.Count == 0; } }

        public int Count { get { lock (_lock) return _queue.Count; } }

        public void Clear()
        {
            lock (_lock) { _queue.Clear(); _pending.Clear(); }
        }
    }

    /// <summary>
    /// DSCH-40 — when the Idling drain may hand the next queued preset to the command
    /// handler. Until this existed the queue drained only when a document opened, so a
    /// compliance-fall or warning-threshold trigger that fired mid-session waited for
    /// the next open (and that drain only set a parameter, it never ran the preset).
    ///
    /// Rules: nothing waiting = Idle (the job drops off the scheduler); a preset still
    /// running, or a drain already in progress (re-entry), = Wait; fewer than
    /// <see cref="MinInterval"/> since the last dispatch = Wait (rate limit, so a
    /// trigger that keeps firing cannot run presets back to back). Otherwise Dispatch.
    /// </summary>
    public static class WorkflowTriggerDrainPolicy
    {
        /// <summary>Minimum time between two dispatched presets.</summary>
        public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(30);

        public enum Decision { Idle, Wait, Dispatch }

        public static Decision Decide(bool hasPending, bool presetRunning, bool drainInProgress,
                                      DateTime? lastDispatchUtc, DateTime nowUtc, TimeSpan minInterval)
        {
            if (!hasPending) return Decision.Idle;
            if (presetRunning || drainInProgress) return Decision.Wait;
            if (lastDispatchUtc.HasValue && nowUtc - lastDispatchUtc.Value < minInterval) return Decision.Wait;
            return Decision.Dispatch;
        }
    }
}
