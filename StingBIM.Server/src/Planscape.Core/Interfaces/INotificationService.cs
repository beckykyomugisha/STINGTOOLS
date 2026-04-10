namespace Planscape.Core.Interfaces;

public interface INotificationService
{
    Task NotifyAsync(Guid tenantId, string channel, string title, string message, object? data = null, CancellationToken ct = default);
    Task NotifyUserAsync(Guid userId, string title, string message, object? data = null, CancellationToken ct = default);
}
