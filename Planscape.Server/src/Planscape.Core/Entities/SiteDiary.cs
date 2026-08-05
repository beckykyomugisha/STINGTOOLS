namespace Planscape.Core.Entities;

/// <summary>
/// Phase 142 — Daily site diary entry, the construction-management record of
/// what happened on site that day. Captures the standard CIOB/CMAA fields:
/// weather, manpower, equipment, plant, materials received, deliveries,
/// safety incidents, visitors, and free-text narrative. One row per
/// (project, diary date, author) so multiple supervisors on the same site
/// can post their own diaries without conflict.
///
/// Photos and other attachments hang off the diary via
/// <see cref="DocumentRecord"/> rows linked through
/// <see cref="SiteDiaryAttachment"/> (parallel to <see cref="IssueAttachment"/>).
///
/// Status field models the trade contractor → main contractor → client
/// approval chain that's standard on UK ISO 19650 projects.
/// </summary>
public class SiteDiary : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>The calendar date the diary covers (UTC date — no time component).</summary>
    public DateTime DiaryDate { get; set; }

    public Guid? AuthorUserId { get; set; }
    public string AuthorName { get; set; } = "";
    public string AuthorRole { get; set; } = ""; // ISO 19650 role e.g. "TC", "MC", "SI"

    // ── Conditions ──
    /// <summary>Free-text weather summary. Mobile clients may auto-fill from a weather provider later.</summary>
    public string? Weather { get; set; }
    public double? TemperatureCelsius { get; set; }
    public double? WindSpeedKph { get; set; }
    public double? RainfallMm { get; set; }

    // ── Resources on site ──
    public int ManpowerCount { get; set; }
    /// <summary>JSON array — `[{ "trade": "Electricians", "count": 6 }, …]` so the schema scales without migrations.</summary>
    public string? ManpowerByTradeJson { get; set; }
    /// <summary>JSON array of equipment/plant items on site that day.</summary>
    public string? EquipmentJson { get; set; }
    /// <summary>JSON array of material deliveries received that day.</summary>
    public string? DeliveriesJson { get; set; }

    // ── Activities + observations ──
    /// <summary>Free-text narrative — the heart of the diary entry.</summary>
    public string? Narrative { get; set; }
    /// <summary>JSON array of `{title, completed, notes}` from the day's checklist if any.</summary>
    public string? ChecklistJson { get; set; }
    public string? VisitorsLog { get; set; }
    public string? SafetyIncidents { get; set; }
    public string? DelaysAndDisruption { get; set; }

    // ── Phase 178b — Reason taxonomy (mirrors SitePhoto) ──
    /// <summary>
    /// One of <see cref="SitePhoto.ValidReasons"/> — Progress | Issue | Defect |
    /// Safety | AsBuilt | Reference. Drives auto-routing on submit:
    /// Defect → auto-create NCR linked to this diary; Safety → priority
    /// Safety issue + push to safety officer + 4h SLA. Default
    /// "Reference" preserves pre-Phase-178b behaviour (no auto-route).
    /// </summary>
    public string Reason { get; set; } = "Reference";

    /// <summary>
    /// When auto-routing creates an issue at submit time, the resulting
    /// <see cref="BimIssue"/> id is recorded here so the diary detail
    /// view + audit timeline link straight back to the auto-created
    /// issue without a fresh search.
    /// </summary>
    public Guid? AutoCreatedIssueId { get; set; }

    // ── Lifecycle ──
    /// <summary>DRAFT, SUBMITTED, ACKNOWLEDGED, ARCHIVED. Submission triggers
    /// project-scoped notification; archival is a manager-only operation.</summary>
    public string Status { get; set; } = "DRAFT";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }

    // Optional GPS — useful when the project has multiple physical sites.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Navigation
    public Project? Project { get; set; }
    public AppUser? AuthorUser { get; set; }
    public List<SiteDiaryAttachment> Attachments { get; set; } = new();
}

/// <summary>
/// Join row that links a <see cref="DocumentRecord"/> to a
/// <see cref="SiteDiary"/> entry — parallel to <see cref="IssueAttachment"/>.
/// </summary>
public class SiteDiaryAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SiteDiaryId { get; set; }
    public Guid DocumentId { get; set; }
    public string AttachedBy { get; set; } = "";
    public DateTime AttachedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Free-text caption for the photo / file in the diary context.</summary>
    public string? Caption { get; set; }

    public SiteDiary? SiteDiary { get; set; }
    public DocumentRecord? Document { get; set; }
}
