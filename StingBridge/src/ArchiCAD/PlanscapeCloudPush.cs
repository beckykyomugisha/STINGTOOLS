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

        private readonly ConcurrentQueue<ArchiCADChangeEvent> _queue = new();
        private readonly Timer _flushTimer;
        private bool _disposed;

        public PlanscapeCloudPush(string serverBase, string projectId, string bridgeKey)
        {
            _serverBase = serverBase.TrimEnd('/');
            _projectId  = projectId;
            _bridgeKey  = bridgeKey;

            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("X-StingBridge-Key", bridgeKey);
            _http.DefaultRequestHeaders.Add("X-Client-Type",     "stingbridge");

            // Flush queued events every 500 ms — gives a smooth live feel
            // without hammering the server on rapid successive changes.
            _flushTimer = new Timer(_ => _ = FlushAsync(), null,
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromMilliseconds(500));
        }

        // Queue an element change. Thread-safe — call from any thread.
        public void Enqueue(ArchiCADChangeEvent ev) => _queue.Enqueue(ev);

        // Send a heartbeat so Planscape Web can show the "LIVE" indicator.
        public async Task SendStatusAsync(string authorName, string authorEmail)
        {
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

            var batch = new System.Collections.Generic.List<ArchiCADChangeEvent>();
            while (_queue.TryDequeue(out var ev)) batch.Add(ev);
            if (batch.Count == 0) return;

            try
            {
                var payload = new { events = batch, authorInfo = (object?)null };
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _flushTimer.Dispose();
            _http.Dispose();
        }
    }
}
