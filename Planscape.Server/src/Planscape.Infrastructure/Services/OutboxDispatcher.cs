using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// S3.2 — drains the OutboxMessages table every minute and dispatches each
/// pending message to the right channel (SignalR / push / email / webhook).
///
/// At-least-once: a message stays Pending until the dispatch handler returns
/// success; failures schedule a NextAttemptAt with exponential backoff (1m,
/// 5m, 30m, 2h, 12h). After 6 attempts it transitions to DeadLettered for
/// human review.
///
/// Channels are kept thin — the dispatcher delegates to existing services
/// (INotificationService, IPushNotificationService, IEmailService, etc.)
/// rather than re-implementing transport. Adding a new channel is one
/// branch + one service injection.
/// </summary>
public class OutboxDispatcher
{
    private static readonly TimeSpan[] Backoffs =
    {
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(12),
    };
    private const int MaxAttempts = 6;
    private const int BatchSize   = 100;

    private readonly PlanscapeDbContext _db;
    private readonly INotificationService _signalr;
    private readonly IPushNotificationService _push;
    private readonly IEmailService _email;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        PlanscapeDbContext db,
        INotificationService signalr,
        IPushNotificationService push,
        IEmailService email,
        ILogger<OutboxDispatcher> logger)
    {
        _db = db; _signalr = signalr; _push = push; _email = email; _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        _db.BypassTenantFilter = true;
        var now = DateTime.UtcNow;

        var batch = await _db.Set<OutboxMessage>()
            .Where(m => m.Status == OutboxStatus.Pending
                     && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var msg in batch)
        {
            try
            {
                await DispatchAsync(msg, ct);
                msg.Status = OutboxStatus.Dispatched;
                msg.DispatchedAt = DateTime.UtcNow;
                msg.LastError = null;
            }
            catch (Exception ex)
            {
                msg.Attempts += 1;
                msg.LastAttemptAt = DateTime.UtcNow;
                msg.LastError = ex.Message;
                if (msg.Attempts >= MaxAttempts)
                {
                    msg.Status = OutboxStatus.DeadLettered;
                    _logger.LogError(ex, "Outbox message {Id} dead-lettered after {Attempts} attempts", msg.Id, msg.Attempts);
                }
                else
                {
                    var delay = Backoffs[Math.Min(msg.Attempts - 1, Backoffs.Length - 1)];
                    msg.NextAttemptAt = DateTime.UtcNow.Add(delay);
                }
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task DispatchAsync(OutboxMessage msg, CancellationToken ct)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(msg.PayloadJson);
        var root = doc.RootElement;
        switch (msg.Channel)
        {
            case "signalr-tenant":
                {
                    var title = root.GetProperty("title").GetString() ?? msg.Topic;
                    var message = root.GetProperty("message").GetString() ?? "";
                    var channel = root.TryGetProperty("subChannel", out var sc) ? sc.GetString() ?? "default" : "default";
                    await _signalr.NotifyAsync(msg.TenantId, channel, title, message, null, ct);
                    break;
                }
            case "signalr-user":
                {
                    var userId = Guid.Parse(root.GetProperty("userId").GetString() ?? Guid.Empty.ToString());
                    var title = root.GetProperty("title").GetString() ?? msg.Topic;
                    var message = root.GetProperty("message").GetString() ?? "";
                    await _signalr.NotifyUserAsync(userId, title, message, null, ct);
                    break;
                }
            case "signalr-project":
                {
                    var projectId = Guid.Parse(root.GetProperty("projectId").GetString() ?? Guid.Empty.ToString());
                    var title = root.GetProperty("title").GetString() ?? msg.Topic;
                    var message = root.GetProperty("message").GetString() ?? "";
                    var subChannel = root.TryGetProperty("subChannel", out var sc) ? sc.GetString() ?? "default" : "default";
                    await _signalr.NotifyProjectAsync(projectId, subChannel, title, message, null, ct);
                    break;
                }
            case "push":
                {
                    var userId = Guid.Parse(root.GetProperty("userId").GetString() ?? Guid.Empty.ToString());
                    var payload = new PushPayload
                    {
                        Title    = root.GetProperty("title").GetString() ?? msg.Topic,
                        Body     = root.GetProperty("body").GetString()  ?? "",
                        Channel  = root.TryGetProperty("channel", out var ch) ? ch.GetString() : null,
                        Priority = root.TryGetProperty("priority", out var pr) ? pr.GetString() : null,
                    };
                    await _push.SendToUserAsync(userId, payload, ct);
                    break;
                }
            case "email":
                {
                    var to      = root.GetProperty("to").GetString()      ?? "";
                    var subject = root.GetProperty("subject").GetString() ?? "";
                    var html    = root.GetProperty("html").GetString()    ?? "";
                    await _email.SendNotificationAsync(to, subject, html, ct);
                    break;
                }
            default:
                throw new InvalidOperationException($"Unknown outbox channel '{msg.Channel}'");
        }
    }
}
