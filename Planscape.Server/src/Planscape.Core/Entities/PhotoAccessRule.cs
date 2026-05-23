namespace Planscape.Core.Entities;

/// <summary>
/// Phase 179 — Per-photo ACL row, layered ON TOP OF the existing audience
/// state machine. The audience field still drives publish lifecycle
/// (Internal → PendingReview → Approved → ClientPortal → Withdrawn);
/// this row narrows WHO inside that audience can actually see the photo.
///
/// At read time the controller AND-s the two gates:
///   1. Is the caller in the photo's audience? (pre-existing logic)
///   2. Does the caller pass every PhotoAccessRule attached to the photo?
///
/// Multiple rules on the same photo are combined with AND (most-restrictive
/// wins). Presence of zero rules = no narrowing (old behaviour).
///
/// Examples:
///   - DistributionGroupId set, everything else null → only members of
///     that group can view, regardless of project membership.
///   - VisibleDisciplines = "M,E" → only project members whose ACL slice
///     allows Mechanical or Electrical.
///   - MinRoleToView = "PM" → tenant Owner / Admin / project PM only.
///   - VisibleFrom / VisibleUntil non-null → time-bounded reveal.
///   - RequiresNdaAcceptance true → first viewer is logged in audit.
/// </summary>
public class PhotoAccessRule
{
    public Guid Id      { get; set; } = Guid.NewGuid();
    public Guid PhotoId { get; set; }

    public Guid? DistributionGroupId { get; set; }

    /// <summary>Comma-joined list of discipline codes (M,E,P,A,S, …).</summary>
    public string? VisibleDisciplines { get; set; }

    /// <summary>One of: ClientGuest | Coordinator | PM | Admin | Owner — the minimum role needed.</summary>
    public string? MinRoleToView { get; set; }

    public DateTime? VisibleFrom  { get; set; }
    public DateTime? VisibleUntil { get; set; }

    public bool RequiresNdaAcceptance { get; set; } = false;

    public DateTime CreatedAt        { get; set; } = DateTime.UtcNow;
    public Guid?    CreatedByUserId  { get; set; }

    public SitePhoto? Photo { get; set; }
    public DistributionGroup? DistributionGroup { get; set; }
}
