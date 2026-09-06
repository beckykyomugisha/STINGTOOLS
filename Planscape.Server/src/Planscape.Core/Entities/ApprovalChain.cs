namespace Planscape.Core.Entities;

/// <summary>
/// Phase 178c (T3-12) — Multi-step approval chain for a CDE state
/// transition on a <see cref="DocumentRecord"/>. Each chain owns one or
/// more <see cref="ApprovalStage"/>s. A document transitions only when
/// every stage is complete; within a stage, the <c>Mode</c> field decides
/// whether all listed approvers must approve in any order (PARALLEL) or
/// in the declared order (SEQUENTIAL).
///
/// The legacy single-approver <see cref="DocumentApproval"/> path stays
/// in place for back-compat; if a document has no chain attached the
/// existing endpoints behave as before.
/// </summary>
public class ApprovalChain : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid DocumentId { get; set; }

    /// <summary>The CDE transition this chain covers, e.g. "SHARED-&gt;PUBLISHED".</summary>
    public string Transition { get; set; } = "";

    /// <summary>OPEN | COMPLETED | REJECTED | CANCELLED.</summary>
    public string Status { get; set; } = "OPEN";

    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// The document's revision at the moment this chain was OPENED — i.e. what the
    /// approvers were actually looking at when the round began. Mirrors
    /// <see cref="DocumentApproval.RevisionSnapshot"/> so both approval paths can be
    /// scoped by the same predicate.
    ///
    /// Stamped at creation, deliberately not at completion: the point of the field is
    /// to record the content that was sanctioned, and that is fixed when the round
    /// opens, not when the last approver clicks.
    ///
    /// <para><b>NULL means "pre-dates this field, matches any revision".</b> Issue #552
    /// left the backfill policy open with three options; this is the compatible one, and
    /// it is chosen on purpose. Treating NULL as stale would close every historical hole
    /// at once but would also stop every already-COMPLETED chain from satisfying its gate
    /// the day it shipped — blocking real publishes on live projects with no warning.
    /// That is a migration event, not a bug fix, and it is the owner's call. The residual
    /// exposure is therefore known rather than assumed to be zero: chains completed
    /// before this field existed keep their permanent pass.</para>
    /// </summary>
    public string? RevisionSnapshot { get; set; }

    /// <summary>Optional human-readable description of the chain rules.</summary>
    public string? Description { get; set; }

    // Navigation
    public DocumentRecord? Document { get; set; }
    public Project? Project { get; set; }
    public List<ApprovalStage> Stages { get; set; } = new();
}
