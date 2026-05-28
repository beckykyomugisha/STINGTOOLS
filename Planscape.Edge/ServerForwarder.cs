using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Planscape.Core.Interfaces;

namespace Planscape.Edge;

/// <summary>
/// 5B — forwards batches to the server's authenticated HTTP ingest
/// (POST /api/projects/{id}/telemetry/ingest). Returning false leaves the
/// batch on the queue for retry; only a confirmed 2xx commits. The edge holds
/// a project API token, so background ingest needs no per-request user context
/// (this is why protocol adapters live on the edge, not as server services).
/// </summary>
public sealed class ServerForwarder
{
    private readonly HttpClient _http;
    private readonly EdgeOptions _opt;
    private readonly ILogger<ServerForwarder> _log;

    public ServerForwarder(HttpClient http, IOptions<EdgeOptions> opt, ILogger<ServerForwarder> log)
    {
        _http = http;
        _opt = opt.Value;
        _log = log;
    }

    public async Task<bool> ForwardAsync(IReadOnlyList<TelemetryReading> readings, CancellationToken ct)
    {
        if (readings.Count == 0) return true;

        var url = $"{_opt.ServerUrl.TrimEnd('/')}/api/projects/{_opt.ProjectId}/telemetry/ingest";
        var payload = new
        {
            readings = readings.Select(r => new
            {
                deviceId = r.DeviceId, metric = r.Metric, value = r.Value, unit = r.Unit, ts = r.Ts,
            }),
        };
        var body = JsonConvert.SerializeObject(payload);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrEmpty(_opt.ApiToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opt.ApiToken);

            using var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                _log.LogInformation("Forwarded {Count} readings → {Status}", readings.Count, (int)resp.StatusCode);
                return true;
            }
            _log.LogWarning("Ingest rejected ({Status}); keeping batch for retry", (int)resp.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Forward failed (offline?); keeping batch for retry");
            return false;
        }
    }
}
