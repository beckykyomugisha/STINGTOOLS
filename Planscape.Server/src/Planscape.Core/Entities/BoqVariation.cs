namespace Planscape.Core.Entities;

/// <summary>
/// A formal change to a baselined BOQ — instruction-driven (architect's
/// instruction, employer's instruction, contractor's claim, RFI
/// outcome). Variations carry their own line items (additions or
/// omissions) and a reference back to the originating
/// <see cref="BoqBaseline"/>.
///
/// Variations move through an approval lifecycle:
///   Draft → Submitted → Reviewed → Approved/Rejected → Incorporated
///
/// Once Approved, the engine creates new <see cref="QuantityLine"/>
/// rows linked back to this variation (negative quantities for omits)
/// so the running BOQ value reflects the change.
/// </summary>
public class BoqVariation : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    public Guid BaselineId { get; set; }

    /// <summary>Variation reference — "VO-001", "AI-042".</summary>
    public string Reference { get; set; } = "";

    /// <summary>"VO" (variation order), "AI" (architect's instruction), "EI", "CE", "RFI".</summary>
    public string Kind { get; set; } = "VO";

    public string Title { get; set; } = "";

    /// <summary>Narrative — what is being changed and why.</summary>
    public string? Description { get; set; }

    /// <summary>Originating instruction reference (free-text or doc id).</summary>
    public string? InstructionRef { get; set; }

    /// <summary>Originating issue — links to RFI/NCR that triggered this variation.</summary>
    public Guid? BimIssueId { get; set; }

    /// <summary>Net value impact (positive = addition, negative = omission).</summary>
    public decimal NetValue { get; set; }

    public string Currency { get; set; } = "GBP";

    /// <summary>Status: Draft / Submitted / Reviewed / Approved / Rejected / Incorporated.</summary>
    public string Status { get; set; } = "Draft";

    /// <summary>
    /// Phase 184o — *why* the variation arose. Distinct from
    /// <see cref="Kind"/> (the contractual route). Values:
    /// DesignChange / ClientRequest / SiteCondition / StatutoryChange /
    /// ErrorOmission / ContractorProposal / ScopeAddition / ScopeOmission /
    /// Specification / Quality / ProgrammeChange / Other.
    /// </summary>
    public string Reason { get; set; } = "Other";

    /// <summary>
    /// Who pays. Values: Employer / Contractor / Designer / Shared /
    /// ForceMajeure. Mirrors the plugin VariationLiability enum.
    /// </summary>
    public string Liability { get; set; } = "Employer";

    /// <summary>Free-text rationale captured at submission.</summary>
    public string? ReasonDetail { get; set; }

    /// <summary>EOT entitlement in calendar days; 0 = no time impact.</summary>
    public int EotDays { get; set; } = 0;

    public DateTime? SubmittedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public string? RejectionReason { get; set; }

    /// <summary>
    /// JSON array of <c>{ classificationCode, description, unit, qty, rate }</c>
    /// captured at submission. Once Approved these are materialised as
    /// <see cref="QuantityLine"/> rows with a back-reference.
    /// </summary>
    public string? LineDeltaJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }

    public Project? Project { get; set; }
    public BoqBaseline? Baseline { get; set; }
    public BimIssue? BimIssue { get; set; }
}
