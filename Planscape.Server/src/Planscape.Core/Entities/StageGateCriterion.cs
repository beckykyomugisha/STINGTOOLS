namespace Planscape.Core.Entities;

/// <summary>
/// Phase 146 — normalised stage-gate criterion row. Replaces the
/// <c>StageGate.CriteriaJson</c> blob for production use so per-criterion
/// sign-off writes touch one row instead of rewriting an entire array.
/// CriteriaJson is kept on the parent row for backwards-compat reads on
/// projects whose criteria haven't been migrated yet (the controller
/// reads from the table when it's populated, falls back to the JSONB blob
/// when it's empty).
///
/// Indexed on (StageGateId, Key) UNIQUE so the mobile per-criterion
/// signoff endpoint can locate the row in O(1) and the BIM Manager can
/// rely on key uniqueness within a gate.
/// </summary>
public class StageGateCriterion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StageGateId { get; set; }

    /// <summary>Stable identifier within the gate, e.g. "spatial_coord_clash_zero".</summary>
    public string Key { get; set; } = "";

    /// <summary>Human-readable label rendered in the checklist row.</summary>
    public string Label { get; set; } = "";

    /// <summary>Optional longer help text.</summary>
    public string? Description { get; set; }

    /// <summary>Sort order for stable display. Defaults to 0; the mobile UI
    /// sorts alphabetically when ties occur.</summary>
    public int SortOrder { get; set; }

    public bool Met { get; set; }

    /// <summary>Optional FK to the document that evidences sign-off.</summary>
    public Guid? EvidenceDocId { get; set; }

    public string? SignedBy { get; set; }
    public Guid? SignedByUserId { get; set; }
    public DateTime? SignedAt { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public StageGate? StageGate { get; set; }
}
