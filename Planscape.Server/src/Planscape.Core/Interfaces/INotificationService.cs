namespace Planscape.Core.Interfaces;

public interface INotificationService
{
    Task NotifyAsync(Guid tenantId, string channel, string title, string message, object? data = null, CancellationToken ct = default);
    Task NotifyUserAsync(Guid userId, string title, string message, object? data = null, CancellationToken ct = default);

    /// <summary>
    /// SRV-07 — Broadcast a notification only to the project's members. Both the
    /// SignalR fan-out and the FCM/APNs push fan-out are filtered to userIds
    /// that have a <see cref="Planscape.Core.Entities.ProjectMember"/> row for
    /// the given project. Per-user delivery preferences are still honoured.
    /// </summary>
    Task NotifyProjectAsync(Guid projectId, string channel, string title, string message, object? data = null, CancellationToken ct = default);

    /// <summary>
    /// #7 — Fire a named SignalR event (e.g. "BoqSnapshotUpdated") to the
    /// project group `project-{projectId}`. This is a lightweight UI-refresh
    /// signal: it does NOT push, does NOT consult per-user preferences, and
    /// does NOT persist a notification row. Use it for typed client-refresh
    /// events that subscribers bind to by exact name (mobile cost dashboard,
    /// etc.) rather than the generic "Notification" channel.
    /// </summary>
    Task NotifyProjectEventAsync(Guid projectId, string eventName, object? payload = null, CancellationToken ct = default);
}
