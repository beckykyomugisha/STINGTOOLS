namespace StingBIM.Core.Interfaces;

public record PushPayload
{
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public string? Channel { get; init; }
    public Dictionary<string, string> Data { get; init; } = new();
}

public interface IPushNotificationService
{
    /// <summary>
    /// Send push notification to a specific user's registered devices.
    /// </summary>
    Task SendToUserAsync(Guid userId, PushPayload payload, CancellationToken ct = default);

    /// <summary>
    /// Send push notification to all users in a tenant.
    /// </summary>
    Task SendToTenantAsync(Guid tenantId, PushPayload payload, CancellationToken ct = default);

    /// <summary>
    /// Register a device push token for a user.
    /// </summary>
    Task RegisterTokenAsync(Guid userId, Guid tenantId, string token, string platform, string? deviceName, CancellationToken ct = default);

    /// <summary>
    /// Remove a device push token.
    /// </summary>
    Task UnregisterTokenAsync(Guid userId, string token, CancellationToken ct = default);
}
