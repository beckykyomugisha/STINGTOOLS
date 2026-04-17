using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// PUSH-01 — Delivers push notifications through the Expo Push API.
/// Expo issues tokens shaped <c>ExponentPushToken[xxxx]</c> to apps running in
/// Expo Go and to EAS standalone builds that don't set up raw FCM/APNs directly.
/// Expo relays those tokens to FCM/APNs on our behalf.
/// </summary>
public class ExpoPushService
{
    private readonly ILogger<ExpoPushService> _logger;
    private readonly HttpClient _http;
    private readonly string? _accessToken;

    public ExpoPushService(ILogger<ExpoPushService> logger, IHttpClientFactory httpClientFactory, Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _logger = logger;
        _http = httpClientFactory.CreateClient("Expo");
        _accessToken = config["Expo:AccessToken"]; // optional — lifts the anonymous rate-limit
    }

    /// <summary>
    /// True when the token matches the Expo push token shape.
    /// </summary>
    public static bool IsExpoToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        return token.StartsWith("ExponentPushToken[", StringComparison.Ordinal)
            || token.StartsWith("ExpoPushToken[", StringComparison.Ordinal);
    }

    /// <summary>
    /// Send a single push through the Expo Push API.
    /// Returns <c>false</c> for DeviceNotRegistered / InvalidCredentials (caller should prune the token).
    /// </summary>
    public async Task<bool> SendAsync(string expoToken, PushPayload payload, CancellationToken ct = default)
    {
        try
        {
            var body = new
            {
                to = expoToken,
                title = payload.Title,
                body = payload.Body,
                data = payload.Data ?? new Dictionary<string, string>(),
                sound = "default",
                priority = payload.Priority ?? "high",
                channelId = payload.Channel ?? "default",
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://exp.host/--/api/v2/push/send")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrEmpty(_accessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Expo push failed ({Status}): {Body}", (int)response.StatusCode, text);
                return true; // non-2xx isn't necessarily a bad-token — don't prune on transient failures
            }

            // Expo returns { data: { status: "ok" | "error", message?, details: { error: "DeviceNotRegistered" | ... } } }
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var data)) return true;
            if (!data.TryGetProperty("status", out var statusEl)) return true;
            var status = statusEl.GetString();
            if (status == "ok") return true;

            // Error path: prune on DeviceNotRegistered / InvalidCredentials / MismatchSenderId
            string? errorCode = null;
            if (data.TryGetProperty("details", out var details)
                && details.TryGetProperty("error", out var errEl))
                errorCode = errEl.GetString();

            if (errorCode is "DeviceNotRegistered" or "InvalidCredentials" or "MismatchSenderId")
            {
                _logger.LogInformation("Expo push token invalid ({Error}) — scheduling cleanup", errorCode);
                return false;
            }

            _logger.LogWarning("Expo push returned status={Status} error={Err}", status, errorCode ?? "(none)");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Expo push send failed");
            return true; // transient — keep the token
        }
    }
}
