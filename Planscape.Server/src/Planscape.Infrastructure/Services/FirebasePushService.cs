using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Push notification service that dispatches to FCM HTTP v1 API.
/// Falls back to no-op when Firebase is not configured.
/// Device tokens are persisted in the database (DevicePushToken table).
/// </summary>
public class FirebasePushService : IPushNotificationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FirebasePushService> _logger;
    private readonly string? _fcmProjectId;
    private readonly string? _fcmServiceAccountJson;
    private readonly HttpClient _httpClient;

    public FirebasePushService(
        IServiceScopeFactory scopeFactory,
        ILogger<FirebasePushService> logger,
        IConfiguration config,
        IHttpClientFactory httpClientFactory)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _fcmProjectId = config["Firebase:ProjectId"];
        _fcmServiceAccountJson = config["Firebase:ServiceAccountJson"];
        _httpClient = httpClientFactory.CreateClient("FCM");
    }

    public async Task SendToUserAsync(Guid userId, PushPayload payload, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();

        var tokens = await db.Set<DevicePushToken>()
            .Where(t => t.UserId == userId)
            .Select(t => new { t.Token, t.Platform })
            .ToListAsync(ct);

        if (tokens.Count == 0)
        {
            _logger.LogDebug("No push tokens registered for user {UserId}", userId);
            return;
        }

        var invalidTokens = new List<string>();
        foreach (var t in tokens)
        {
            var success = await SendFcmMessageAsync(t.Token, payload, ct);
            if (!success)
                invalidTokens.Add(t.Token);
        }

        // Clean up invalid/expired tokens
        if (invalidTokens.Count > 0)
        {
            await RemoveInvalidTokensAsync(db, invalidTokens, ct);
        }
    }

    public async Task SendToTenantAsync(Guid tenantId, PushPayload payload, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();

        var tokens = await db.Set<DevicePushToken>()
            .Where(t => t.TenantId == tenantId)
            .Select(t => new { t.Token, t.Platform })
            .ToListAsync(ct);

        if (tokens.Count == 0) return;

        var invalidTokens = new List<string>();
        foreach (var t in tokens)
        {
            var success = await SendFcmMessageAsync(t.Token, payload, ct);
            if (!success)
                invalidTokens.Add(t.Token);
        }

        if (invalidTokens.Count > 0)
        {
            await RemoveInvalidTokensAsync(db, invalidTokens, ct);
        }
    }

    public async Task RegisterTokenAsync(Guid userId, Guid tenantId, string token, string platform, string? deviceName, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();

        var existing = await db.Set<DevicePushToken>()
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Token == token, ct);

        if (existing != null)
        {
            existing.LastUsedAt = DateTime.UtcNow;
            existing.DeviceName = deviceName ?? existing.DeviceName;
        }
        else
        {
            var parsedPlatform = Enum.TryParse<PushPlatform>(platform, true, out var p) ? p : PushPlatform.FCM;
            db.Set<DevicePushToken>().Add(new DevicePushToken
            {
                UserId = userId,
                TenantId = tenantId,
                Token = token,
                Platform = parsedPlatform,
                DeviceName = deviceName
            });
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Push token registered for user {UserId} on {Platform}", userId, platform);

        // Also register in the in-memory SignalR token store for backwards compatibility
        NotificationService.RegisterPushToken(userId, token);
    }

    public async Task UnregisterTokenAsync(Guid userId, string token, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();

        var existing = await db.Set<DevicePushToken>()
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Token == token, ct);

        if (existing != null)
        {
            db.Set<DevicePushToken>().Remove(existing);
            await db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Push token unregistered for user {UserId}", userId);
    }

    /// <summary>
    /// Send a single FCM HTTP v1 message. Returns false if the token is invalid/expired.
    /// </summary>
    private async Task<bool> SendFcmMessageAsync(string deviceToken, PushPayload payload, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_fcmProjectId))
        {
            _logger.LogDebug("Firebase not configured — skipping push notification");
            return true; // Don't mark token as invalid when Firebase isn't configured
        }

        try
        {
            var url = $"https://fcm.googleapis.com/v1/projects/{_fcmProjectId}/messages:send";

            var message = new
            {
                message = new
                {
                    token = deviceToken,
                    notification = new
                    {
                        title = payload.Title,
                        body = payload.Body
                    },
                    data = payload.Data,
                    android = new
                    {
                        priority = "high",
                        notification = new
                        {
                            channel_id = payload.Channel ?? "planscape_default"
                        }
                    },
                    apns = new
                    {
                        payload = new
                        {
                            aps = new
                            {
                                alert = new { title = payload.Title, body = payload.Body },
                                sound = "default",
                                badge = 1
                            }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(message);
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            // In production, use Google OAuth2 access token from service account
            // For now, use the API key if provided
            var apiKey = _fcmServiceAccountJson;
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            var response = await _httpClient.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("FCM push sent successfully to {Token}", deviceToken[..Math.Min(10, deviceToken.Length)]);
                return true;
            }

            var body = await response.Content.ReadAsStringAsync(ct);

            // 404 or specific error codes indicate invalid/expired token
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                body.Contains("UNREGISTERED") || body.Contains("INVALID_ARGUMENT"))
            {
                _logger.LogWarning("FCM token invalid/expired: {Token}", deviceToken[..Math.Min(10, deviceToken.Length)]);
                return false;
            }

            _logger.LogWarning("FCM push failed ({Status}): {Body}", response.StatusCode, body);
            return true; // Don't remove token on transient errors
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FCM push dispatch error");
            return true; // Don't remove token on exception
        }
    }

    private static async Task RemoveInvalidTokensAsync(PlanscapeDbContext db, List<string> tokens, CancellationToken ct)
    {
        var toRemove = await db.Set<DevicePushToken>()
            .Where(t => tokens.Contains(t.Token))
            .ToListAsync(ct);

        if (toRemove.Count > 0)
        {
            db.Set<DevicePushToken>().RemoveRange(toRemove);
            await db.SaveChangesAsync(ct);
        }
    }
}

/// <summary>
/// No-op push service used when Firebase is not configured.
/// </summary>
public class NullPushNotificationService : IPushNotificationService
{
    private readonly ILogger<NullPushNotificationService> _logger;

    public NullPushNotificationService(ILogger<NullPushNotificationService> logger) => _logger = logger;

    public Task SendToUserAsync(Guid userId, PushPayload payload, CancellationToken ct = default)
    {
        _logger.LogDebug("Push notification (no-op) to user {UserId}: {Title}", userId, payload.Title);
        return Task.CompletedTask;
    }

    public Task SendToTenantAsync(Guid tenantId, PushPayload payload, CancellationToken ct = default)
    {
        _logger.LogDebug("Push notification (no-op) to tenant {TenantId}: {Title}", tenantId, payload.Title);
        return Task.CompletedTask;
    }

    public Task RegisterTokenAsync(Guid userId, Guid tenantId, string token, string platform, string? deviceName, CancellationToken ct = default)
    {
        // Still register in in-memory store for SignalR compatibility
        NotificationService.RegisterPushToken(userId, token);
        return Task.CompletedTask;
    }

    public Task UnregisterTokenAsync(Guid userId, string token, CancellationToken ct = default)
        => Task.CompletedTask;
}
