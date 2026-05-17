namespace Planscape.Core.Entities;

/// <summary>
/// ISO 19650 suitability codes — the formal CDE state taxonomy used to
/// stamp every information container as it moves through Work In Progress,
/// Shared, Published, and Archive states.
///
/// The S-codes (S0–S7) cover information shared for review at increasing
/// stages of design maturity. The B-codes (B1–B7) are equivalents for
/// information shared between disciplines without external review. The
/// A-codes (A1–A7) are for "Authorised and Accepted" published
/// information. CR (Code of Practice) / AB (As Built) are terminal.
/// </summary>
public enum SuitabilityCode
{
    /// <summary>Initial status — Work In Progress, not yet shared.</summary>
    S0 = 0,

    /// <summary>Suitable for coordination — design intent shared with team.</summary>
    S1 = 1,

    /// <summary>Suitable for information — issued for review (non-binding).</summary>
    S2 = 2,

    /// <summary>Suitable for internal review and comment.</summary>
    S3 = 3,

    /// <summary>Suitable for stage approval — formal stage-gate review.</summary>
    S4 = 4,

    /// <summary>Suitable for tender — released to bidders.</summary>
    S5 = 5,

    /// <summary>Suitable for construction — released for execution.</summary>
    S6 = 6,

    /// <summary>Suitable for PIM authorisation — handover to AIM.</summary>
    S7 = 7,

    /// <summary>Inter-discipline coordination (PIM team only).</summary>
    B1 = 11,

    /// <summary>Inter-discipline information.</summary>
    B2 = 12,

    /// <summary>Inter-discipline review.</summary>
    B3 = 13,

    /// <summary>Inter-discipline approval.</summary>
    B4 = 14,

    /// <summary>Inter-discipline construction.</summary>
    B5 = 15,

    /// <summary>Inter-discipline manufacture / install information.</summary>
    B6 = 16,

    /// <summary>Inter-discipline as-built.</summary>
    B7 = 17,

    /// <summary>Authorised for design development.</summary>
    A1 = 21,

    /// <summary>Authorised for procurement.</summary>
    A2 = 22,

    /// <summary>Authorised for construction.</summary>
    A3 = 23,

    /// <summary>Authorised for site / shop.</summary>
    A4 = 24,

    /// <summary>Authorised for manufacture / install.</summary>
    A5 = 25,

    /// <summary>Authorised for handover.</summary>
    A6 = 26,

    /// <summary>Authorised for operation / FM.</summary>
    A7 = 27,

    /// <summary>Code of Practice — frozen reference (terminal).</summary>
    CR = 90,

    /// <summary>As Built — final terminal state.</summary>
    AB = 99,
}

/// <summary>
/// A configurable transition rule in the per-tenant ISO 19650
/// suitability state machine. Each row says: "from state X to state Y
/// is legal, provided the actor holds role R and the n approvers in
/// chain C have signed off". Rules ship with sensible defaults but
/// firms commonly tailor them (e.g. enforce a 2-person review gate on
/// S3→S4).
/// </summary>
public class SuitabilityTransitionRule : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Null for tenant-wide rule, set for project-specific override.</summary>
    public Guid? ProjectId { get; set; }

    public SuitabilityCode FromCode { get; set; }
    public SuitabilityCode ToCode { get; set; }

    /// <summary>Comma-separated role keywords allowed to initiate this transition.</summary>
    public string? AllowedRoles { get; set; }

    /// <summary>
    /// Optional <see cref="ApprovalChain"/> id that must be satisfied
    /// before the transition is permitted (n-of-m sign-off).
    /// </summary>
    public Guid? RequiredApprovalChainId { get; set; }

    /// <summary>
    /// Pre-condition flags as bitmask: 1=AntivirusCleared, 2=NamingValid,
    /// 4=ContentHashRecorded, 8=RevisionIncremented, 16=PriorRevisionClosed.
    /// </summary>
    public int PreconditionMask { get; set; } = 0;

    /// <summary>
    /// Maximum hours allowed in <see cref="FromCode"/> before this rule
    /// auto-triggers (auto-promote stale WIP, auto-expire S2 review windows).
    /// Null = no auto-trigger.
    /// </summary>
    public int? AutoTriggerAfterHours { get; set; }

    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}

/// <summary>
/// One row per actual suitability transition performed on a document.
/// Provides the immutable per-document audit trail that ISO 19650-2 §5
/// requires every information container to carry.
/// </summary>
public class SuitabilityTransition : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid DocumentRecordId { get; set; }

    public SuitabilityCode FromCode { get; set; }
    public SuitabilityCode ToCode { get; set; }

    /// <summary>The rule that authorised this transition.</summary>
    public Guid? RuleId { get; set; }

    public string? Revision { get; set; }
    public string? Notes { get; set; }

    /// <summary>"User" / "AutoExpiry" / "ApprovalChain" / "Migration".</summary>
    public string TriggerSource { get; set; } = "User";

    public string TriggeredBy { get; set; } = "";
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;
}
