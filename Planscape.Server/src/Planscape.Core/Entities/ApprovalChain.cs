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

    /// <summary>Optional human-readable description of the chain rules.</summary>
    public string? Description { get; set; }

    // Navigation
    public DocumentRecord? Document { get; set; }
    public Project? Project { get; set; }
    public List<ApprovalStage> Stages { get; set; } = new();
}
