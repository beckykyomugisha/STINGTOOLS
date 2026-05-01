namespace Planscape.Core.Entities;

/// <summary>
/// Phase 144 — RIBA Plan of Work / ISO 19650 stage gate. One row per
/// (project, stage) holding the planned + actual gate dates plus a
/// pass/fail decision and the gate-criteria narrative. Sits next to
/// <see cref="InformationDeliverable"/>: deliverables roll up to the
/// stage they are due against, so the gate dashboard can show the
/// open-deliverable count alongside each gate.
///
/// We model RIBA stages 0–7 (UK) but allow the bespoke
/// <see cref="StageCode"/> string so projects on alternative
/// frameworks (PAS 1192-6, US AIA G202, ICE) can use the same table.
/// </summary>
public class StageGate : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Short code, e.g. "RIBA-3" / "RIBA-4" / "PAS-3" / project-bespoke.</summary>
    public string StageCode { get; set; } = "";

    /// <summary>Display name, e.g. "Spatial Coordination" or "Technical Design".</summary>
    public string StageName { get; set; } = "";

    /// <summary>Numeric ordering for the dashboard timeline. RIBA stages map directly (0-7).</summary>
    public int SortOrder { get; set; }

    public DateTime? PlannedDate { get; set; }
    public DateTime? ActualDate { get; set; }

    /// <summary>NOT_STARTED / IN_PROGRESS / PASSED / FAILED / WAIVED.</summary>
    public string Status { get; set; } = "NOT_STARTED";

    /// <summary>Free-text gate-criteria narrative; structured criteria live in CriteriaJson.</summary>
    public string? Description { get; set; }

    /// <summary>JSON array of `{key, label, met:bool, evidenceDocId?}` so the team can sign off criterion-by-criterion.</summary>
    public string? CriteriaJson { get; set; }

    public string? DecidedBy { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Project? Project { get; set; }
    public List<InformationDeliverable> Deliverables { get; set; } = new();
}

/// <summary>
/// Phase 144 — A single MIDP / TIDP information-exchange deliverable. Each row
/// represents one "thing the appointed party owes the appointing party at this
/// stage" — typically a model or document but the entity is generic enough to
/// cover BEPs, COBie, schedules, and inspection certificates.
///
/// `OwnerRole` uses ISO 19650 originator codes (A/M/E/P/S/...) so the BIM
/// Manager can filter the MIDP by discipline. `Status` follows the standard
/// ISO 19650 information-state machine: PENDING → IN_PROGRESS → SUBMITTED →
/// ACCEPTED (or REJECTED + back to IN_PROGRESS).
/// </summary>
public class InformationDeliverable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Optional FK to the StageGate this deliverable rolls up to.</summary>
    public Guid? StageGateId { get; set; }

    /// <summary>Reference code for the MIDP, e.g. "M-3-MOD-01" (mech, stage 3, model, sequence 01).</summary>
    public string Code { get; set; } = "";

    public string Title { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>BS EN ISO 19650 type code (e.g. M3 / DR / RP / SP). Maps to the same dictionary as <see cref="DocumentRecord.DocumentType"/>.</summary>
    public string Type { get; set; } = "DR";

    /// <summary>ISO 19650 originator role code (A / B / C / E / M / S / W / X / …).</summary>
    public string OwnerRole { get; set; } = "";

    /// <summary>Optional discipline for filtering (M / E / P / A / S / FP / LV / G).</summary>
    public string? Discipline { get; set; }

    /// <summary>Suitability target at the gate, e.g. "S2" or "S4". Standard ISO 19650 codes.</summary>
    public string? SuitabilityTarget { get; set; }

    public DateTime DueDate { get; set; }

    /// <summary>PENDING / IN_PROGRESS / SUBMITTED / ACCEPTED / REJECTED / WAIVED.</summary>
    public string Status { get; set; } = "PENDING";

    public DateTime? SubmittedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public Guid? SubmittedByUserId { get; set; }

    public DateTime? AcceptedAt { get; set; }
    public string? AcceptedBy { get; set; }

    /// <summary>Optional FK to the actual document that was published as the deliverable.</summary>
    public Guid? DocumentId { get; set; }

    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Project? Project { get; set; }
    public StageGate? StageGate { get; set; }
    public DocumentRecord? Document { get; set; }
}
