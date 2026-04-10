namespace Planscape.Core.Entities;

public enum PushPlatform
{
    FCM = 0,
    APNs = 1,
    Web = 2
}

/// <summary>
/// Stores device push notification tokens for FCM/APNs/Web push.
/// </summary>
public class DevicePushToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string Token { get; set; } = "";
    public PushPlatform Platform { get; set; }
    public string? DeviceName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    public AppUser? User { get; set; }
    public Tenant? Tenant { get; set; }
}
