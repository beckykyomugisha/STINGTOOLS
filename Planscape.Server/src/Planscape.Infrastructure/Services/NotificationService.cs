using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Notification service that pushes real-time alerts via SignalR + Push.
///
/// C6 fix: honours <see cref="UserNotificationPreferences"/> — per-category
/// opt-out + quiet hours + channel preference. For user-targeted notifications
/// we look up the user's row before dispatching and drop the delivery when:
///   - the category toggle is off (IssuesEnabled / ComplianceEnabled / …)
///   - the current local time falls inside the user's QuietHours window
///   - the user's `Channel` preference excludes the active channel
///
/// Tenant-wide broadcasts go out unconditionally (only SignalR; no push)
/// since we can't resolve individual preferences for every subscriber.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly IHubContext<NotificationHub> _hub;
    private readonly ILogger<NotificationService> _logger;
    private readonly IPushNotificationService? _pushService;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// In-memory push token store: userId -> set of push tokens (device tokens, etc.)
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, ConcurrentBag<string>> PushTokens = new();

    public NotificationService(
        IHubContext<NotificationHub> hub,
        ILogger<NotificationService> logger,
        IServiceScopeFactory scopeFactory,
        IPushNotificationService? pushService = null)
    {
        _hub = hub;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _pushService = pushService;
    }

    /// <summary>
    /// Broadcast a notification to all connected clients in a tenant group on a specific channel.
    /// Tenant-wide — individual preferences are not consulted (no user context here).
    /// </summary>
    public async Task NotifyAsync(Guid tenantId, string channel, string title, string message, object? data = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Notification [{Channel}] to tenant {TenantId}: {Title} — {Message}",
            channel, tenantId, title, message);

        var payload = new { channel, title, message, data, timestamp = DateTime.UtcNow };

        await _hub.Clients.Group($"tenant_{tenantId}")
            .SendAsync("Notification", payload, ct);

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
    /// Send a notification to a specific user by their user ID. Honours the
    /// user's <see cref="UserNotificationPreferences"/>.
    /// </summary>
    public async Task NotifyUserAsync(Guid userId, string title, string message, object? data = null, CancellationToken ct = default)
    {
        // C6 — per-user preference lookup. Channel is inferred from `data.channel`
        // when present; falls back to "generic" when unspecified.
        var channel = (data as IDictionary<string, object?>)?.TryGetValue("channel", out var c) == true
            ? c?.ToString() ?? "generic"
            : "generic";

        var (shouldSignalR, shouldPush) = await ResolveDelivery(userId, channel, ct);

        if (!shouldSignalR && !shouldPush)
        {
            _logger.LogDebug("User notification suppressed by preferences — user={UserId} channel={Channel}", userId, channel);
            return;
        }

        _logger.LogInformation("User notification to {UserId}: {Title} — {Message} (signalr={Sig}, push={Push})",
            userId, title, message, shouldSignalR, shouldPush);

        var payload = new { title, message, data, timestamp = DateTime.UtcNow };

        if (shouldSignalR)
        {
            await _hub.Clients.Group($"user_{userId}")
                .SendAsync("UserNotification", payload, ct);
        }

        if (shouldPush && _pushService != null)
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
    /// Determine whether SignalR and/or Push should be delivered for this user + channel,
    /// based on <see cref="UserNotificationPreferences"/>. Unknown users or missing
    /// preferences fall back to both delivery paths (opt-in by default).
    /// </summary>
    private async Task<(bool signalR, bool push)> ResolveDelivery(Guid userId, string channel, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
            var prefs = await db.UserNotificationPreferences.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId, ct);
            if (prefs == null) return (true, true); // opt-in default

            // Category toggle — string-insensitive match by channel.
            bool categoryAllowed = channel switch
            {
                "issue"      or "issues"      or "issue_assigned" or "issue_created"
                    => prefs.IssuesEnabled,
                "compliance" or "compliance_changed"
                    => prefs.ComplianceEnabled,
                "revision"   or "revisions"
                    => prefs.RevisionsEnabled,
                "meeting"    or "meetings"
                    => prefs.MeetingsEnabled,
                "sla"        or "sla_breach"
                    => prefs.SlaBreachesEnabled,
                _ => true,
            };
            if (!categoryAllowed) return (false, false);

            // Quiet hours — compare against the user's local time.
            if (IsInQuietHours(prefs))
            {
                // Critical channels bypass quiet hours so life-safety alerts aren't missed.
                if (channel != "sla_breach" && channel != "critical")
                    return (false, false);
            }

            // Channel split — "push" | "signalr" | "email" | "all".
            return prefs.Channel?.ToLowerInvariant() switch
            {
                "signalr" => (true, false),
                "push"    => (false, true),
                "email"   => (false, false),  // email is dispatched elsewhere
                _          => (true, true),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve notification preferences for {UserId} — defaulting to opt-in", userId);
            return (true, true);
        }
    }

    private static bool IsInQuietHours(UserNotificationPreferences prefs)
    {
        if (string.IsNullOrWhiteSpace(prefs.QuietHoursStart) || string.IsNullOrWhiteSpace(prefs.QuietHoursEnd))
            return false;
        if (!TryParseHhMm(prefs.QuietHoursStart, out var start)) return false;
        if (!TryParseHhMm(prefs.QuietHoursEnd, out var end)) return false;

        // Resolve the user's local time. Fall back to UTC if the timezone is missing/invalid.
        DateTime now;
        try
        {
            var tz = string.IsNullOrWhiteSpace(prefs.TimeZone)
                ? TimeZoneInfo.Utc
                : TimeZoneInfo.FindSystemTimeZoneById(prefs.TimeZone);
            now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
        catch { now = DateTime.UtcNow; }

        var nowMinutes = now.Hour * 60 + now.Minute;
        var startMinutes = start.Hour * 60 + start.Minute;
        var endMinutes = end.Hour * 60 + end.Minute;

        // Window may cross midnight (e.g. 22:00 → 07:00).
        return startMinutes <= endMinutes
            ? nowMinutes >= startMinutes && nowMinutes < endMinutes
            : nowMinutes >= startMinutes || nowMinutes < endMinutes;
    }

    private static bool TryParseHhMm(string s, out TimeOnly result)
    {
        result = default;
        if (s.Length < 4 || s.Length > 5) return false;
        var parts = s.Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return false;
        if (h < 0 || h > 23 || m < 0 || m > 59) return false;
        result = new TimeOnly(h, m);
        return true;
    }

    /// <summary>In-memory registration for legacy tests. Prefer <see cref="DevicePushToken"/> table.</summary>
    public static void RegisterPushToken(Guid userId, string token)
    {
        var tokens = PushTokens.GetOrAdd(userId, _ => new ConcurrentBag<string>());
        if (!tokens.Contains(token)) tokens.Add(token);
    }

    public static IReadOnlyCollection<string> GetPushTokens(Guid userId)
    {
        return PushTokens.TryGetValue(userId, out var tokens)
            ? tokens.ToArray()
            : Array.Empty<string>();
    }
}
