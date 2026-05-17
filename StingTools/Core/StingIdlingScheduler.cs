// Pack 8 — Idling scheduler.
//
// Revit's Idling event fires 10-100× per second whenever the user isn't
// interacting with the app. Perfect for background maintenance work that
// must not block the UI:
//
//   * ComplianceScan refresh (budget 50 ms)
//   * SLA scanner (budget 20 ms)
//   * Drip-feed of long batch ops (FullAutoPopulate / BulkParamWrite)
//
// Architecture: priority queue of IIdlingJob workers. Each tick drains up
// to WorkBudgetMs milliseconds of jobs. Priority 1 (highest) runs first.
// Jobs that return false still have work to do and are re-enqueued; jobs
// that return true are dropped.
//
// Deliberately stateless between ticks — every job receives the UIApplication
// fresh and must tolerate document changes / tab switches.
//
// Gated only by having at least one document open (UIApp.ActiveUIDocument != null).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System.Linq;

namespace StingTools.Core
{
    /// <summary>
    /// Worker contract for jobs scheduled on Revit's Idling event.
    /// </summary>
    public interface IIdlingJob
    {
        /// <summary>Human-readable name for logs.</summary>
        string Name { get; }

        /// <summary>Priority 1 = highest, 5 = lowest.</summary>
        int Priority { get; }

        /// <summary>Approximate work budget in milliseconds. The scheduler
        /// will skip the job if the remaining tick budget is smaller than
        /// this value.</summary>
        int BudgetMs { get; }

        /// <summary>Do a slice of work. Return true when fully done (the
        /// scheduler drops the job) or false to be re-queued for the next
        /// tick.</summary>
        bool Execute(UIApplication uiApp);
    }

    public static class StingIdlingScheduler
    {
        private const int TickBudgetMs = 100;
        private static readonly List<IIdlingJob> _jobs = new List<IIdlingJob>();
        private static readonly object _lock = new object();
        private static bool _subscribed;

        public static void Register(UIControlledApplication application)
        {
            if (_subscribed || application == null) return;
            try
            {
                application.Idling += OnIdling;
                _subscribed = true;
                StingLog.Info("StingIdlingScheduler registered");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingIdlingScheduler.Register: {ex.Message}");
            }
        }

        public static void Unregister(UIControlledApplication application)
        {
            if (!_subscribed || application == null) return;
            try
            {
                application.Idling -= OnIdling;
                _subscribed = false;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingIdlingScheduler.Unregister: {ex.Message}");
            }
        }

        /// <summary>
        /// Enqueue a job. Safe to call from any thread. Duplicate jobs (by
        /// reference equality) are collapsed.
        /// </summary>
        public static void Enqueue(IIdlingJob job)
        {
            if (job == null) return;
            lock (_lock)
            {
                if (!_jobs.Contains(job))
                {
                    _jobs.Add(job);
                    _jobs.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                }
            }
        }

        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            var uiApp = sender as UIApplication;
            if (uiApp == null || uiApp.ActiveUIDocument == null) return;

            IIdlingJob[] snapshot;
            lock (_lock)
            {
                if (_jobs.Count == 0) return;
                snapshot = _jobs.ToArray();
            }

            var stopwatch = Stopwatch.StartNew();
            var completed = new List<IIdlingJob>();
            foreach (var job in snapshot)
            {
                if (stopwatch.ElapsedMilliseconds + job.BudgetMs > TickBudgetMs) break;
                try
                {
                    bool done = job.Execute(uiApp);
                    if (done) completed.Add(job);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"StingIdlingScheduler.{job.Name}: {ex.Message}");
                    completed.Add(job);
                }
            }

            if (completed.Count > 0)
            {
                lock (_lock)
                {
                    foreach (var j in completed) _jobs.Remove(j);
                }
            }
        }
    }

    public class FullGeometrySyncJob : IIdlingJob
    {
        public string Name => "FullGeometrySync";
        public int Priority => 5;
        public int BudgetMs => 200;
        public bool Execute(UIApplication uiApp) { return true; }
    }

    public class StaleWarningPromotionJob : IIdlingJob
    {
        public string Name => "StaleWarningPromotion";
        public int Priority => 4;
        public int BudgetMs => 100;
        public bool Execute(UIApplication uiApp) { return true; }
    }

    /// <summary>
    /// Pack 8 pilot consumer — compliance-scan refresh. Drops itself after
    /// a single tick so it only runs once per enqueue.
    /// </summary>
    public class ComplianceRefreshJob : IIdlingJob
    {
        public string Name => "ComplianceRefresh";
        public int Priority => 3;
        public int BudgetMs => 50;

        public bool Execute(UIApplication uiApp)
        {
            try
            {
                ComplianceScan.InvalidateCache();
                var result = ComplianceScan.Scan(uiApp.ActiveUIDocument.Document);
                UI.StingDockPanel.UpdateComplianceStatus(result.StatusBarText, result.RAGStatus);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ComplianceRefreshJob: {ex.Message}");
            }
            return true;
        }
    }
}
