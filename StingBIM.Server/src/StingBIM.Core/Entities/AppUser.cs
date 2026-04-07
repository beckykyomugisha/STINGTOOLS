namespace StingBIM.Core.Entities;

/// <summary>
/// Application user with multi-tenant membership and ISO 19650 role.
/// </summary>
public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Viewer;
    public string Iso19650Role { get; set; } = "Z"; // ISO 19650 role code (A/M/E/S/H/P/C/I/K/Q/F/W/L/Z)
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }

    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiresAt { get; set; }
    public string? PushToken { get; set; }        // FCM (Android) or APNs (iOS)
    public string? PushPlatform { get; set; }     // "android" | "ios" | "web"
    public DateTime? PushTokenUpdatedAt { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
}

public enum UserRole
{
    Viewer = 0,
    Contributor = 1,
    Coordinator = 2,
    Manager = 3,
    Admin = 4,
    Owner = 5
}
