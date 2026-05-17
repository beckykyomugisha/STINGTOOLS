namespace Planscape.Core.Entities;

/// <summary>
/// One detected clash between two model elements. Created by ClashDetectionJob
/// when two SceneNodes from different disciplines have overlapping AABBs.
/// Lifecycle: NEW → ACKNOWLEDGED → RESOLVED → CLOSED (or DISMISSED).
///
/// A clash can be promoted to a full BimIssue when assigned for resolution —
/// IssueId links back to the created issue, BimIssue.ClashRecordId links forward.
/// </summary>
public class ClashRecord : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Stable hash of (modelA, elementA, modelB, elementB) — used for dedup on re-runs.</summary>
    public string ClashHash { get; set; } = "";

    /// <summary>HARD (geometry intersects), SOFT (within clearance), CLEARANCE (zone violation).</summary>
    public ClashKind Kind { get; set; }

    /// <summary>CRITICAL (auto-issue), MAJOR, MINOR, INFO.</summary>
    public ClashSeverity Severity { get; set; } = ClashSeverity.Minor;

    /// <summary>NEW | ACKNOWLEDGED | RESOLVED | CLOSED | DISMISSED.</summary>
    public ClashStatus Status { get; set; } = ClashStatus.New;

    // Element A
    public Guid ModelAId { get; set; }
    public string ElementAGuid { get; set; } = "";
    public string? ElementAName { get; set; }
    public string? ElementAType { get; set; }      // IfcWall, IfcPipe, IfcDuctSegment
    public string? DisciplineA { get; set; }       // M / E / P / A / S

    // Element B
    public Guid ModelBId { get; set; }
    public string ElementBGuid { get; set; } = "";
    public string? ElementBName { get; set; }
    public string? ElementBType { get; set; }
    public string? DisciplineB { get; set; }

    /// <summary>Penetration depth (mm) for HARD clashes, or shortest distance for SOFT.</summary>
    public double DistanceMm { get; set; }

    /// <summary>Overlap centre in model coordinates — viewer zooms here.</summary>
    public double CentreX { get; set; }
    public double CentreY { get; set; }
    public double CentreZ { get; set; }

    /// <summary>Overlap volume (mm³) — used for severity scoring.</summary>
    public double OverlapVolumeMm3 { get; set; }

    /// <summary>Optional level code at clash location (L01 / GF / B1).</summary>
    public string? LevelCode { get; set; }

    /// <summary>Optional zone code at clash location.</summary>
    public string? ZoneCode { get; set; }

    /// <summary>Assigned discipline lead or BIM coordinator.</summary>
    public string? AssignedTo { get; set; }

    /// <summary>Free-text resolution note from the BIM coordinator.</summary>
    public string? ResolutionNote { get; set; }

    /// <summary>If promoted to a BimIssue, this references it.</summary>
    public Guid? IssueId { get; set; }

    /// <summary>BCF 2.1 topic GUID — for cross-platform export.</summary>
    public string? BcfTopicGuid { get; set; }

    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public string? DetectedByJobId { get; set; }    // Hangfire job id for traceability

    public Project? Project { get; set; }
    public BimIssue? Issue { get; set; }
}

public enum ClashKind { Hard = 0, Soft = 1, Clearance = 2 }
public enum ClashSeverity { Info = 0, Minor = 1, Major = 2, Critical = 3 }
public enum ClashStatus { New = 0, Acknowledged = 1, Resolved = 2, Closed = 3, Dismissed = 4 }
