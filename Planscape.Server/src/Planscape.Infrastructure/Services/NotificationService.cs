using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.SignalR;
using Planscape.Core.Entities;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Notification service that pushes real-time alerts via SignalR.
/// Uses tenant groups for broadcast and user groups for targeted notifications.
/// Push tokens are stored in-memory for now (ConcurrentDictionary).
/// </summary>
public class NotificationService : INotificationService
{
    private readonly IHubContext<NotificationHub> _hub;
    private readonly ILogger<NotificationService> _logger;
    private readonly IPushNotificationService? _pushService;

    /// <summary>
    /// In-memory push token store: userId -> set of push tokens (device tokens, etc.)
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, ConcurrentBag<string>> PushTokens = new();

    public NotificationService(IHubContext<NotificationHub> hub, ILogger<NotificationService> logger, IPushNotificationService? pushService = null)
    {
        _hub = hub;
        _logger = logger;
        _pushService = pushService;
    }

    /// <summary>
    /// Broadcast a notification to all connected clients in a tenant group on a specific channel.
    /// </summary>
    public async Task NotifyAsync(Guid tenantId, string channel, string title, string message, object? data = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Notification [{Channel}] to tenant {TenantId}: {Title} — {Message}",
            channel, tenantId, title, message);

        var payload = new
        {
            channel,
            title,
            message,
            data,
            timestamp = DateTime.UtcNow
        };

        await _hub.Clients.Group($"tenant_{tenantId}")
            .SendAsync("Notification", payload, ct);

        // Also dispatch push notification to all tenant devices
        if (_pushService != null)
        {
            _ = _pushService.SendToTenantAsync(tenantId, new PushPayload
            {
                Title = title,
                Body = message,
                Channel = channel,
                Data = new Dictionary<string, string>
                {
                    ["type"] = "tenant_notification",
                    ["channel"] = channel,
                    ["tenantId"] = tenantId.ToString()
                }
            }, ct);
        }
    }

    /// <summary>
    /// Send a notification to a specific user by their user ID.
    /// </summary>
    public async Task NotifyUserAsync(Guid userId, string title, string message, object? data = null, CancellationToken ct = default)
    {
        _logger.LogInformation("User notification to {UserId}: {Title} — {Message}",
            userId, title, message);

        var payload = new
        {
            title,
            message,
            data,
            timestamp = DateTime.UtcNow
        };

        await _hub.Clients.Group($"user_{userId}")
            .SendAsync("UserNotification", payload, ct);

        // Also dispatch push notification to user devices
        if (_pushService != null)
        {
            _ = _pushService.SendToUserAsync(userId, new PushPayload
            {
                Title = title,
                Body = message,
                Data = new Dictionary<string, string>
                {
                    ["type"] = "user_notification",
                    ["userId"] = userId.ToString()
                }
            }, ct);
        }
    }

    /// <summary>
    /// Register a push token for a user (in-memory store).
    /// </summary>
    public static void RegisterPushToken(Guid userId, string token)
    {
        var tokens = PushTokens.GetOrAdd(userId, _ => new ConcurrentBag<string>());
        if (!tokens.Contains(token))
            tokens.Add(token);
    }

    /// <summary>
    /// Get all push tokens for a user.
    /// </summary>
    public static IReadOnlyCollection<string> GetPushTokens(Guid userId)
    {
        return PushTokens.TryGetValue(userId, out var tokens)
            ? tokens.ToArray()
            : Array.Empty<string>();
    }
}
