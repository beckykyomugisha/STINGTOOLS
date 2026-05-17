namespace Planscape.Core.Entities;

/// <summary>
/// Per-project automation rule for clash detection results. Defines when
/// a clash should automatically create a BimIssue, fire a webhook, or
/// notify a specific user via push.
///
/// Multiple rules can match a single clash — they fire in priority order
/// (lower = higher priority). First matching auto-issue rule creates the
/// issue; all matching notification rules fire.
/// </summary>
public class ClashAutomationRule : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Rule name shown in admin UI.</summary>
    public string Name { get; set; } = "";

    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;

    // Matching criteria — all set criteria must match (AND)
    public ClashSeverity? MinSeverity { get; set; }       // e.g. only fire for MAJOR+
    public string? DisciplineA { get; set; }              // null = any
    public string? DisciplineB { get; set; }
    public ClashKind? Kind { get; set; }
    public double? MinOverlapVolumeMm3 { get; set; }
    public string? LevelCode { get; set; }

    // Actions
    public bool AutoCreateIssue { get; set; } = false;
    public string? AutoAssignTo { get; set; }             // email or role keyword: "BIM_COORDINATOR", "DISCIPLINE_LEAD"
    public string? IssuePriority { get; set; }            // override BimIssue priority
    public bool NotifyPush { get; set; } = false;
    public string? NotifyUsers { get; set; }              // comma-separated emails
    public bool FireWebhook { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }

    // Navigation
    public Project? Project { get; set; }
}
