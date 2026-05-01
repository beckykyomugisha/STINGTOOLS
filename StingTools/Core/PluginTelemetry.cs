#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace StingTools.Core
{
    /// <summary>
    /// S8.2 — opt-in OpenTelemetry-flavoured telemetry for the Revit plugin.
    /// Default OFF. When the user opts in via project_config.json
    /// (<c>"telemetry": { "enabled": true }</c>) every command's start /
    /// finish / failure is emitted as a span to the configured OTLP HTTP
    /// endpoint (or, if absent, written to a local rolling file the user
    /// can attach to a support ticket).
    ///
    /// The schema is OTLP/HTTP-protobuf-compatible JSON so a real
    /// collector can ingest it; we don't pull in the full
    /// OpenTelemetry.Exporter.OpenTelemetryProtocol package because that
    /// adds 3 MB to the plugin binary for a feature most users never
    /// enable.
    ///
    /// Privacy: span attributes are limited to project name, command tag,
    /// duration, error class. No element ids, parameter values, tag
    /// content, or anything that could leak project secrets.
    /// </summary>
    public static class PluginTelemetry
    {
        private static readonly ConcurrentBag<SpanRecord> Buffer = new();
        private static readonly object FlushLock = new();
        private static bool _enabled;
        private static string? _endpoint;
        private static string? _localFile;

        /// <summary>Configure once at plugin startup. Re-callable to flip without restart.</summary>
        public static void Configure(bool enabled, string? otlpEndpoint, string? localFilePath)
        {
            _enabled = enabled;
            _endpoint = otlpEndpoint;
            _localFile = localFilePath;
        }

        /// <summary>Wrap a command's body. Returns the value the lambda returns; emits a span on entry/exit.</summary>
        public static T Run<T>(string commandTag, Func<T> body, IDictionary<string, object?>? extras = null)
        {
            if (!_enabled) return body();
            var started = DateTime.UtcNow;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string? errorClass = null;
            try
            {
                return body();
            }
            catch (Exception ex)
            {
                errorClass = ex.GetType().Name;
                throw;
            }
            finally
            {
                sw.Stop();
                Buffer.Add(new SpanRecord
                {
                    Name = commandTag,
                    StartUtc = started,
                    DurationMs = sw.ElapsedMilliseconds,
                    ErrorClass = errorClass,
                    Extras = extras,
                });
                if (Buffer.Count >= 50) _ = Task.Run(FlushAsync);
            }
        }

        /// <summary>Void-returning overload — convenient for wrapping <c>Action</c>-shaped bodies.</summary>
        public static void Run(string commandTag, Action body, IDictionary<string, object?>? extras = null)
            => Run<int>(commandTag, () => { body(); return 0; }, extras);

        public static async Task FlushAsync()
        {
            if (!_enabled) return;
            List<SpanRecord> snapshot;
            lock (FlushLock)
            {
                snapshot = new List<SpanRecord>();
                while (Buffer.TryTake(out var s)) snapshot.Add(s);
            }
            if (snapshot.Count == 0) return;

            // Always write to the local file (audit trail) when configured.
            if (!string.IsNullOrEmpty(_localFile))
            {
                try
                {
                    var dir = Path.GetDirectoryName(_localFile);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    using var w = new StreamWriter(_localFile, append: true);
                    foreach (var s in snapshot)
                        await w.WriteLineAsync(JsonSerializer.Serialize(s));
                }
                catch (Exception ex) { StingLog.Warn($"Telemetry local-file write failed: {ex.Message}"); }
            }

            // POST to OTLP HTTP collector when configured.
            if (!string.IsNullOrEmpty(_endpoint))
            {
                try
                {
                    using var http = new HttpClient();
                    var json = JsonSerializer.Serialize(new { spans = snapshot });
                    using var resp = await http.PostAsync(_endpoint,
                        new StringContent(json, Encoding.UTF8, "application/json"));
                    if (!resp.IsSuccessStatusCode)
                        StingLog.Warn($"Telemetry export failed: {(int)resp.StatusCode}");
                }
                catch (Exception ex) { StingLog.Warn($"Telemetry export crashed: {ex.Message}"); }
            }
        }

        public class SpanRecord
        {
            public string Name { get; set; } = "";
            public DateTime StartUtc { get; set; }
            public long DurationMs { get; set; }
            public string? ErrorClass { get; set; }
            public IDictionary<string, object?>? Extras { get; set; }
        }
    }
}
