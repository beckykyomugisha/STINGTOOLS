namespace Planscape.Core.Entities;

/// <summary>
/// NEW-FLEX-12 — Per-user toggles and quiet hours for push + email + SignalR
/// delivery. Stored server-side so the same preferences apply on phone, tablet,
/// and web simultaneously.
/// </summary>
public class UserNotificationPreferences : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }

    // Per-category opt-out — default true (opted in)
    public bool IssuesEnabled { get; set; } = true;
    public bool ComplianceEnabled { get; set; } = true;
    public bool RevisionsEnabled { get; set; } = true;
    public bool MeetingsEnabled { get; set; } = true;
    public bool SlaBreachesEnabled { get; set; } = true;

    // Phase 178b — T2-13. Daily site-photo digest opt-in. Default ON for
    // ClientGuest (read-only client portal) and project members; OFF
    // wins when the user explicitly unsubscribes via Settings → Email
    // preferences. The digest job (DailyPhotoDigestJob) skips users
    // whose flag is false.
    public bool EmailDigestEnabled { get; set; } = true;
    /// <summary>Hour-of-day (0–23 UTC) the daily digest is sent. Project
    /// override (Project.DigestHour) wins when set; this is the per-user
    /// fallback. Default 17:00 UTC ≈ end-of-day across most time zones.</summary>
    public int  EmailDigestHourUtc { get; set; } = 17;

    // Delivery channel preference — "push" | "email" | "signalr" | "all"
    public string Channel { get; set; } = "all";

    // Quiet hours in 24h local time. When both are null quiet hours are disabled.
    // Values are stored as HH:MM strings; null disables.
    public string? QuietHoursStart { get; set; }
    public string? QuietHoursEnd { get; set; }
    public string? TimeZone { get; set; } // IANA, e.g. "Europe/London"

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public AppUser? User { get; set; }
    public Tenant? Tenant { get; set; }
}
