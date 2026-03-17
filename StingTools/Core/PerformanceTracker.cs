using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace StingTools.Core
{
    /// <summary>
    /// Lightweight performance profiling for STING batch operations.
    /// Tracks per-operation and per-element timing, identifies bottlenecks,
    /// and generates performance reports.
    ///
    /// Usage:
    ///   using (PerformanceTracker.Track("BatchTag"))
    ///   {
    ///       foreach (var el in elements)
    ///       {
    ///           using (PerformanceTracker.TrackElement("BatchTag", el.Id.IntegerValue))
    ///           {
    ///               // ... process element ...
    ///           }
    ///       }
    ///   }
    ///
    ///   string report = PerformanceTracker.GetReport();
    /// </summary>
    public static class PerformanceTracker
    {
        // ── Session-level aggregation ────────────────────────────────────

        private static readonly ConcurrentDictionary<string, OperationStats> _operations =
            new ConcurrentDictionary<string, OperationStats>(StringComparer.OrdinalIgnoreCase);

        private static DateTime _sessionStart = DateTime.Now;

        /// <summary>Whether profiling is enabled. Disable in production for zero overhead.</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// Start tracking a named operation. Dispose the returned handle when done.
        /// </summary>
        public static IDisposable Track(string operationName)
        {
            if (!Enabled) return NullDisposable.Instance;
            var stats = _operations.GetOrAdd(operationName, _ => new OperationStats(operationName));
            return new OperationHandle(stats);
        }

        /// <summary>
        /// Track a single element within an operation. Dispose when element processing is done.
        /// </summary>
        public static IDisposable TrackElement(string operationName, int elementId)
        {
            if (!Enabled) return NullDisposable.Instance;
            var stats = _operations.GetOrAdd(operationName, _ => new OperationStats(operationName));
            return new ElementHandle(stats, elementId);
        }

        /// <summary>
        /// Record a single timing measurement for a named operation (manual mode).
        /// </summary>
        public static void Record(string operationName, TimeSpan elapsed, int elementCount = 0)
        {
            if (!Enabled) return;
            var stats = _operations.GetOrAdd(operationName, _ => new OperationStats(operationName));
            stats.AddInvocation(elapsed, elementCount);
        }

        /// <summary>Get a formatted performance report for all tracked operations.</summary>
        public static string GetReport()
        {
            if (_operations.IsEmpty)
                return "No performance data collected.";

            var sb = new StringBuilder();
            sb.AppendLine("STING Performance Report");
            sb.AppendLine($"Session start: {_sessionStart:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Report time:   {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(new string('─', 70));
            sb.AppendLine($"{"Operation",-30} {"Calls",6} {"Total",10} {"Avg",10} {"Max",10} {"Elem",8}");
            sb.AppendLine(new string('─', 70));

            foreach (var stats in _operations.Values.OrderByDescending(s => s.TotalElapsed))
            {
                sb.AppendLine(
                    $"{Truncate(stats.Name, 30),-30} " +
                    $"{stats.InvocationCount,6} " +
                    $"{FormatMs(stats.TotalElapsed),10} " +
                    $"{FormatMs(stats.AverageElapsed),10} " +
                    $"{FormatMs(stats.MaxElapsed),10} " +
                    $"{stats.TotalElements,8}");

                // Show slowest elements if any
                var slowest = stats.GetSlowestElements(3);
                foreach (var (elId, elapsed) in slowest)
                    sb.AppendLine($"  └ Element {elId}: {FormatMs(elapsed)}");
            }

            sb.AppendLine(new string('─', 70));
            var total = TimeSpan.FromMilliseconds(
                _operations.Values.Sum(s => s.TotalElapsed.TotalMilliseconds));
            sb.AppendLine($"{"TOTAL",-30} {"",-6} {FormatMs(total),10}");

            return sb.ToString();
        }

        /// <summary>Export performance data to CSV on the desktop.</summary>
        public static string ExportCsv()
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"STING_Performance_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            try
            {
                using (var sw = new StreamWriter(path))
                {
                    sw.WriteLine("Operation,Invocations,TotalMs,AvgMs,MaxMs,Elements,ElementsPerSec");
                    foreach (var stats in _operations.Values.OrderByDescending(s => s.TotalElapsed))
                    {
                        double eps = stats.TotalElapsed.TotalSeconds > 0
                            ? stats.TotalElements / stats.TotalElapsed.TotalSeconds : 0;
                        sw.WriteLine($"\"{stats.Name}\",{stats.InvocationCount},{stats.TotalElapsed.TotalMilliseconds:F0}," +
                            $"{stats.AverageElapsed.TotalMilliseconds:F1},{stats.MaxElapsed.TotalMilliseconds:F0}," +
                            $"{stats.TotalElements},{eps:F1}");
                    }
                }
                StingLog.Info($"Performance CSV exported: {path}");
            }
            catch (Exception ex)
            {
                StingLog.Error("Performance CSV export failed", ex);
                return null;
            }
            return path;
        }

        /// <summary>Reset all profiling data.</summary>
        public static void Reset()
        {
            _operations.Clear();
            _sessionStart = DateTime.Now;
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static string FormatMs(TimeSpan ts)
        {
            if (ts.TotalMinutes >= 1) return $"{ts.TotalMinutes:F1}m";
            if (ts.TotalSeconds >= 1) return $"{ts.TotalSeconds:F1}s";
            return $"{ts.TotalMilliseconds:F0}ms";
        }

        private static string Truncate(string s, int max)
        {
            return s.Length <= max ? s : s.Substring(0, max - 2) + "..";
        }

        // ── Data types ───────────────────────────────────────────────────

        private class OperationStats
        {
            public string Name { get; }
            public int InvocationCount { get; private set; }
            public int TotalElements { get; private set; }
            public TimeSpan TotalElapsed { get; private set; }
            public TimeSpan MaxElapsed { get; private set; }
            public TimeSpan AverageElapsed => InvocationCount > 0
                ? TimeSpan.FromMilliseconds(TotalElapsed.TotalMilliseconds / InvocationCount)
                : TimeSpan.Zero;

            private readonly object _lock = new object();
            private readonly List<(int elementId, TimeSpan elapsed)> _elementTimings = new List<(int, TimeSpan)>();
            private const int MaxElementTimings = 100; // keep top N slowest

            public OperationStats(string name) { Name = name; }

            public void AddInvocation(TimeSpan elapsed, int elementCount)
            {
                lock (_lock)
                {
                    InvocationCount++;
                    TotalElements += elementCount;
                    TotalElapsed += elapsed;
                    if (elapsed > MaxElapsed) MaxElapsed = elapsed;
                }
            }

            public void AddElementTiming(int elementId, TimeSpan elapsed)
            {
                lock (_lock)
                {
                    TotalElements++;
                    if (_elementTimings.Count < MaxElementTimings || elapsed > _elementTimings.Last().elapsed)
                    {
                        _elementTimings.Add((elementId, elapsed));
                        if (_elementTimings.Count > MaxElementTimings)
                        {
                            _elementTimings.Sort((a, b) => b.elapsed.CompareTo(a.elapsed));
                            _elementTimings.RemoveRange(MaxElementTimings, _elementTimings.Count - MaxElementTimings);
                        }
                    }
                }
            }

            public List<(int elementId, TimeSpan elapsed)> GetSlowestElements(int count)
            {
                lock (_lock)
                {
                    return _elementTimings
                        .OrderByDescending(e => e.elapsed)
                        .Take(count)
                        .ToList();
                }
            }
        }

        private class OperationHandle : IDisposable
        {
            private readonly OperationStats _stats;
            private readonly Stopwatch _sw;

            public OperationHandle(OperationStats stats)
            {
                _stats = stats;
                _sw = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                _sw.Stop();
                _stats.AddInvocation(_sw.Elapsed, 0);
                if (_sw.Elapsed.TotalSeconds >= 5)
                    StingLog.Info($"[PERF] {_stats.Name}: {_sw.Elapsed.TotalSeconds:F1}s");
            }
        }

        private class ElementHandle : IDisposable
        {
            private readonly OperationStats _stats;
            private readonly int _elementId;
            private readonly Stopwatch _sw;

            public ElementHandle(OperationStats stats, int elementId)
            {
                _stats = stats;
                _elementId = elementId;
                _sw = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                _sw.Stop();
                _stats.AddElementTiming(_elementId, _sw.Elapsed);
            }
        }

        private class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new NullDisposable();
            public void Dispose() { }
        }
    }
}
