namespace Planscape.Core.Entities;

/// <summary>
/// Phase 179 — Required-photos checklist authored by a BIM manager and
/// fulfilled by site coordinators. Every item is a named shot
/// ("North-east elevation pre-pour", "Penetration L02 col-grid C/4")
/// the team must capture before the inspection / pour / handover.
///
/// Items are <see cref="PhotoChecklistItem"/> rows; each item links to
/// zero-or-more <see cref="SitePhoto"/> via the FulfilledByPhotoId
/// pointer (one chosen capture marks the item complete; coordinators can
/// re-link when a better shot is taken).
///
/// Lifecycle:
///   Draft    — author is composing, not yet active
///   Active   — coordinators see it on mobile + BCC, can fulfill
///   Closed   — every item complete OR explicitly waived; no further edits
///   Archived — closed > N days, soft-hidden from default views
/// </summary>
public class PhotoChecklist : ITenantScoped
{
    public Guid Id        { get; set; } = Guid.NewGuid();
    public Guid TenantId  { get; set; }
    public Guid ProjectId { get; set; }

    public string  Name        { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Inspection | Pour | Handover | Toolbox | AsBuilt | Custom — drives default reasons + reviewers.</summary>
    public string Kind { get; set; } = "Custom";

    /// <summary>Draft | Active | Closed | Archived.</summary>
    public string Status { get; set; } = "Draft";

    public string? LevelCode      { get; set; }
    public string? ZoneCode       { get; set; }
    public Guid?   WorkPackageId  { get; set; }

    /// <summary>UTC date by which the checklist must be complete; surfaces RAG indicator on dashboard.</summary>
    public DateTime? DueAt { get; set; }

    public DateTime CreatedAt       { get; set; } = DateTime.UtcNow;
    public Guid?    CreatedByUserId { get; set; }
    public DateTime? ClosedAt       { get; set; }
    public Guid?     ClosedByUserId { get; set; }

    public Project? Project { get; set; }

    public static readonly string[] ValidStatuses = { "Draft", "Active", "Closed", "Archived" };
    public static readonly string[] ValidKinds    = { "Inspection", "Pour", "Handover", "Toolbox", "AsBuilt", "Custom" };
}
