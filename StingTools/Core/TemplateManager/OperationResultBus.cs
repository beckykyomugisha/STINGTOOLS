using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// In-process pub/sub for OperationResult instances. Commands publish their
    /// outcomes; the Template Manager dashboard (or any other listener) subscribes
    /// and renders inline. When no subscribers are present, callers fall back to
    /// TaskDialog so the ribbon entry points keep working.
    ///
    /// Threading: subscribe/unsubscribe are thread-safe; Publish dispatches on
    /// the caller's thread. UI subscribers must Dispatcher.BeginInvoke as needed.
    /// </summary>
    public static class OperationResultBus
    {
        private static readonly object _gate = new();
        private static readonly List<Action<OperationResult>> _subscribers = new();

        // Rolling history (last 50) so the dashboard can repopulate when re-opened.
        private const int HistoryCap = 50;
        private static readonly ConcurrentQueue<OperationResult> _history = new();

        /// <summary>True when at least one live subscriber is registered.</summary>
        public static bool HasSubscribers
        {
            get { lock (_gate) return _subscribers.Count > 0; }
        }

        /// <summary>Subscribe to receive every published OperationResult.</summary>
        public static IDisposable Subscribe(Action<OperationResult> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (_gate) _subscribers.Add(handler);
            return new Unsubscriber(handler);
        }

        /// <summary>
        /// Publish a result. If there are subscribers, returns true (caller should
        /// skip its TaskDialog fallback). If false, caller should fall back to
        /// the legacy TaskDialog so non-dashboard entry points stay functional.
        /// </summary>
        public static bool Publish(OperationResult result)
        {
            if (result == null) return false;
            _history.Enqueue(result);
            while (_history.Count > HistoryCap && _history.TryDequeue(out _)) { }

            Action<OperationResult>[] handlers;
            lock (_gate) handlers = _subscribers.ToArray();

            bool delivered = false;
            foreach (var h in handlers)
            {
                try { h(result); delivered = true; }
                catch (Exception ex) { StingTools.Core.StingLog.Warn($"OperationResultBus.Publish handler failed: {ex.Message}"); }
            }
            return delivered;
        }

        /// <summary>Snapshot of recent results, newest first. Bounded to HistoryCap.</summary>
        public static IReadOnlyList<OperationResult> RecentHistory()
        {
            return _history.Reverse().ToList();
        }

        /// <summary>Clear the rolling history. Subscribers stay intact.</summary>
        public static void ClearHistory()
        {
            while (_history.TryDequeue(out _)) { }
        }

        private sealed class Unsubscriber : IDisposable
        {
            private readonly Action<OperationResult> _handler;
            private bool _disposed;
            public Unsubscriber(Action<OperationResult> h) { _handler = h; }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                lock (_gate) _subscribers.Remove(_handler);
            }
        }
    }
}
