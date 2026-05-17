namespace Planscape.Core.Entities;

/// <summary>
/// Represents a connection between a Planscape project/tenant and an external BIM platform
/// (ACC, Procore, Aconex, Trimble Connect).
/// </summary>
public class PlatformConnection : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public PlatformType Platform { get; set; }

    /// <summary>Display name for this connection (e.g. "ACC - Main Project").</summary>
    public string Name { get; set; } = "";

    /// <summary>Platform-specific project/hub identifier.</summary>
    public string ExternalProjectId { get; set; } = "";

    /// <summary>OAuth2 access token (encrypted at rest in production).</summary>
    public string? AccessToken { get; set; }

    /// <summary>OAuth2 refresh token for token renewal.</summary>
    public string? RefreshToken { get; set; }

    /// <summary>When the current access token expires.</summary>
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>Platform-specific webhook secret for callback verification.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Semi-structured config for platform-specific settings (sync filters, field mappings, etc.).</summary>
    public string? ConfigJson { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncAt { get; set; }
    public string? LastSyncStatus { get; set; }
    public string? LastSyncError { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Project? Project { get; set; }
}

public enum PlatformType
{
    ACC = 0,        // Autodesk Construction Cloud (BIM 360)
    Procore = 1,
    Aconex = 2,     // Oracle Aconex
    Trimble = 3     // Trimble Connect
}
