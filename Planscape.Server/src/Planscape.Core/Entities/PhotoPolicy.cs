namespace Planscape.Core.Entities;

/// <summary>
/// Phase 179 — Per-project photo-workflow policy authored by BIM Manager
/// / Admin. Singleton row per project (controller upserts on PUT).
/// Drives behaviour the v1 site-photo workflow had hard-coded:
///
///   - Reason taxonomy override (disable Reference / add custom reasons)
///   - Default audience per Reason
///   - Watermark + cover-sheet template (logo path, footer text)
///   - Retention window (days before Approved photos auto-archive)
///   - Geofence polygon (off-site photos flagged)
///   - Daily-digest hour and recipient distribution group
///   - Approval-chain shape (single-step today; 2-step for Safety)
///   - Required-photo-checklist enforcement on shift end
/// </summary>
public class PhotoPolicy : ITenantScoped
{
    public Guid Id        { get; set; } = Guid.NewGuid();
    public Guid TenantId  { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>JSON: ["Progress","Issue","Defect","Safety","AsBuilt","Reference","Snag"].</summary>
    public string? AllowedReasonsJson { get; set; }

    /// <summary>JSON dict {"Progress":"PendingReview", "Reference":"Internal", …} — overrides default routing.</summary>
    public string? DefaultAudienceByReasonJson { get; set; }

    /// <summary>Storage path of the project's watermark logo (square PNG with alpha; 256×256 recommended).</summary>
    public string? WatermarkLogoPath { get; set; }

    /// <summary>"Project: {project} · {date} · {capturedBy}" — token-substituted when stamping.</summary>
    public string? WatermarkFooterTemplate { get; set; }

    public bool   WatermarkRequired { get; set; } = true;
    public bool   FaceBlurRequired  { get; set; } = true;
    public bool   PlateBlurRequired { get; set; } = false;

    public int?   RetentionDays            { get; set; }
    public bool   AutoArchiveAfterHandover { get; set; } = false;

    /// <summary>WKT polygon of the project geofence (epsg:4326). Null = no geofence enforcement.</summary>
    public string? GeofenceWkt { get; set; }
    /// <summary>Photos outside the geofence flag with this audience instead of the default ("Quarantine"|"Internal").</summary>
    public string? OffsiteAudience { get; set; }

    public int    DigestHourLocal             { get; set; } = 17;
    public Guid?  DigestDistributionGroupId   { get; set; }

    public string ApprovalChain { get; set; } = "Single";

    /// <summary>Block end-of-shift if any required-photo-checklist item is unfulfilled.</summary>
    public bool EnforceChecklistOnShiftEnd { get; set; } = false;

    public DateTime UpdatedAt        { get; set; } = DateTime.UtcNow;
    public Guid?    UpdatedByUserId  { get; set; }

    public Project? Project { get; set; }
    public DistributionGroup? DigestDistributionGroup { get; set; }

    public static readonly string[] ValidApprovalChains = { "Single", "TwoStepSafety", "TwoStepAll" };
}
