namespace Planscape.Core.Entities;

/// <summary>
/// BIM project container. Each project holds tagged elements, compliance data, and documents.
/// </summary>
public class Project
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

    // Geofence boundary (S12) — GeoJSON Polygon
    [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "jsonb")]
    public string? BoundaryPolygon { get; set; }

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
