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
}
