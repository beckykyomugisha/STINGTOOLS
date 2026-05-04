namespace Planscape.Core.Entities;

/// <summary>
/// BIM project container. Each project holds tagged elements, compliance data, and documents.
/// </summary>
public class Project : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = ""; // ISO 19650 project code
    public string? Description { get; set; }
    public string Phase { get; set; } = "Design"; // RIBA stage
    public ProjectStatus Status { get; set; } = ProjectStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncAt { get; set; }

    // Tag format configuration
    public string TagSeparator { get; set; } = "-";
    public int SeqNumPad { get; set; } = 4;
    public string? TagPrefix { get; set; }
    public string? TagSuffix { get; set; }
    public string? ConfigJson { get; set; } // project_config.json equivalent

    // Phase 143 — when true, document uploads must satisfy the ISO 19650
    // naming pattern. Defaults to false (advisory only) so existing
    // projects don't suddenly start rejecting uploads. BIM Manager turns
    // it on once the team has migrated naming.
    public bool EnforceIso19650Naming { get; set; }

    /// <summary>
    /// Phase 145 — optional JSONB override for the
    /// <see cref="InformationDeliverable"/> state machine. Shape:
    /// <c>{ "states": ["A","B",…], "transitions": [{"from":"A","to":"B"}, …],
    /// "terminal": ["X"] }</c>. Null means use the canonical 6-state ISO
    /// 19650 flow. Validated by <c>DeliverableStateMachine.LoadOrDefault</c>
    /// at request time so a malformed config falls back rather than locking
    /// the project out of transitions.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "jsonb")]
    public string? CustomDeliverableStateMachineJson { get; set; }

    // Geofence boundary (S12) — GeoJSON Polygon
    [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "jsonb")]
    public string? BoundaryPolygon { get; set; }

    // Project location + dashboard metadata (Phase 169 — ACC-style cards + map view)
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? CoverImageUrl { get; set; }
    public bool IsPinned { get; set; } = false;

    // Compliance metrics (cached)
    public double CompliancePercent { get; set; }
    public double ContainerCompliancePercent { get; set; }
    public int TotalElements { get; set; }
    public int TaggedElements { get; set; }
    public int WarningCount { get; set; }
    public string RagStatus { get; set; } = "RED";

    // Navigation
    public Tenant? Tenant { get; set; }
    public ICollection<TaggedElement> Elements { get; set; } = new List<TaggedElement>();
    public ICollection<BimIssue> Issues { get; set; } = new List<BimIssue>();
    public ICollection<DocumentRecord> Documents { get; set; } = new List<DocumentRecord>();
    public ICollection<WorkflowRun> WorkflowRuns { get; set; } = new List<WorkflowRun>();
}

public enum ProjectStatus { Active, Archived, Handed_Over }
