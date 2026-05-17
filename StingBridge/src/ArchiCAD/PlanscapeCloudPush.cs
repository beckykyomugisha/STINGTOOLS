// StingBridge — Planscape cloud push client.
//
// Runs alongside ArchiCADLiveLink. When ArchiCAD reports element changes
// (via the named pipe or add-on webhook), this class serialises them and
// POSTs to the Planscape Server so the SignalR ArchiCADHub can fan them
// out to any connected browser / mobile / desktop client worldwide.
//
// Flow:
//   ArchiCAD change event
//       → ArchiCADChangeListener.OnChanged()
//           → PlanscapeCloudPush.EnqueueAsync(event)
//               → batched every 500 ms
//                   → POST /api/archicad/{projectId}/push
//                       → ArchiCADHub.Clients.Group broadcasts
//                           → Planscape Web receives via SignalR
//
// Config (stored in StingBridge settings / appsettings.json):
//   PlanscapeServer   : "https://api.planscape.app"
//   ProjectId         : "<guid>"
//   BridgeKey         : "<base64 key from /api/archicad/{id}/keygen>"

using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StingTools.Core;

namespace StingBridge.ArchiCAD
{
    public class ArchiCADChangeEvent
    {
        public string Kind        { get; set; } = "Changed"; // Added | Changed | Deleted
        public string ElementId   { get; set; } = "";
        public string ElementType { get; set; } = "";
        public System.Collections.Generic.Dictionary<string, object>? Properties { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class PlanscapeCloudPush : IDisposable
    {
        private readonly HttpClient  _http;
        private readonly string      _serverBase;
        private readonly string      _projectId;
        private readonly string      _bridgeKey;
        private readonly string      _authorName;
        private readonly string      _authorEmail;

        private readonly ConcurrentQueue<ArchiCADChangeEvent> _queue = new();
        private readonly Timer _flushTimer;
        // Prevents concurrent FlushAsync invocations if a flush takes longer than the period.
        private readonly SemaphoreSlim _flushGate = new(1, 1);
        private bool _disposed;

        public PlanscapeCloudPush(string serverBase, string projectId, string bridgeKey,
            string authorName = "", string authorEmail = "")
        {
            _serverBase  = serverBase.TrimEnd('/');
            _projectId   = projectId;
            _bridgeKey   = bridgeKey;
            _authorName  = authorName;
            _authorEmail = authorEmail;

            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("X-StingBridge-Key", bridgeKey);
            _http.DefaultRequestHeaders.Add("X-Client-Type",     "stingbridge");

            // Flush queued events every 500 ms — gives a smooth live feel
            // without hammering the server on rapid successive changes.
            // The SemaphoreSlim gate ensures only one flush runs at a time even
            // if a slow network response causes the period to be exceeded.
            _flushTimer = new Timer(_ => _ = FlushAsync(), null,
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromMilliseconds(500));
        }

        // Queue an element change. Thread-safe — call from any thread.
        public void Enqueue(ArchiCADChangeEvent ev) => _queue.Enqueue(ev);

        // Send a heartbeat so Planscape Web can show the "LIVE" indicator.
        // authorName/authorEmail default to the values set in the constructor.
        public async Task SendStatusAsync(string? authorName = null, string? authorEmail = null)
        {
            authorName  ??= _authorName;
            authorEmail ??= _authorEmail;
            try
            {
                var payload = new
                {
                    connectedAuthors = new[] { $"{authorName} <{authorEmail}>" },
                    activeLayers     = Array.Empty<string>()
                };
                await _http.PostAsJsonAsync(
                    $"{_serverBase}/api/archicad/{_projectId}/status", payload);
            }
            catch (Exception ex) { StingLog.Warn($"PlanscapeCloudPush.Status: {ex.Message}"); }
        }

        private async Task FlushAsync()
        {
            if (_queue.IsEmpty) return;

            // Skip this tick if a previous flush is still in flight.
            if (!await _flushGate.WaitAsync(0)) return;
            try
            {
                var batch = new System.Collections.Generic.List<ArchiCADChangeEvent>();
                while (_queue.TryDequeue(out var ev)) batch.Add(ev);
                if (batch.Count == 0) return;

                try
                {
                    // Include author info when available so the Planscape Web "LIVE"
                    // indicator can show which ArchiCAD user is actively editing.
                    object? authorInfo = string.IsNullOrWhiteSpace(_authorName) ? null
                        : new { name = _authorName, email = _authorEmail, version = "ArchiCAD" };
                    var payload = new { events = batch, authorInfo };
                    var response = await _http.PostAsJsonAsync(
                        $"{_serverBase}/api/archicad/{_projectId}/push", payload);

                    if (!response.IsSuccessStatusCode)
                        StingLog.Warn($"PlanscapeCloudPush: server returned {(int)response.StatusCode}");
                    else
                        StingLog.Info($"PlanscapeCloudPush: pushed {batch.Count} event(s) → {_projectId}");
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"PlanscapeCloudPush.Flush: {ex.Message}");
                    // Re-queue on failure so events are not lost.
                    foreach (var ev in batch) _queue.Enqueue(ev);
                }
            }
            finally
            {
                _flushGate.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _flushTimer.Dispose();
            _flushGate.Dispose();
            _http.Dispose();
        }
    }
}
