namespace Planscape.Core.Entities;

/// <summary>
/// Phase 165 (NEW-08) — outbound webhook subscription. Fires HMAC-signed
/// JSON POST to a tenant-supplied URL on selected events. Opens the platform
/// to no-code integrations (Zapier, Make, n8n, custom) without needing an
/// OAuth round-trip from each contractor's tooling.
/// </summary>
public class OutboundWebhook : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid? ProjectId { get; set; }

    public WebhookEventType EventType { get; set; }

    /// <summary>Target HTTPS URL. The dispatcher rejects http:// outside development.</summary>
    public string TargetUrl { get; set; } = "";

    /// <summary>HMAC-SHA256 secret used to sign payloads. Stored as a SHA-256
    /// hash; the cleartext is returned to the user once on creation.</summary>
    public string SecretHash { get; set; } = "";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastFiredAt { get; set; }
    public int? LastStatusCode { get; set; }
    public string? LastError { get; set; }
    public int FailureCount { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Project? Project { get; set; }
}

public enum WebhookEventType
{
    IssueCreated = 0,
    IssueUpdated = 1,
    DocumentTransitioned = 2,
    ComplianceDropped = 3,
    ClashRaised = 4,
    TransmittalSent = 5,
    MeetingCreated = 6,
    DeliverableIssued = 7,
}
