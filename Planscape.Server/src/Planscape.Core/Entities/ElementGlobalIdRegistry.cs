namespace Planscape.Core.Entities;

/// <summary>
/// Gap 6 — Cross-tool GlobalId registry. One row per element that appears
/// in two or more source models (ArchiCAD, Revit, Tekla). The coordinator
/// maps each tool's element identity to a single canonical IfcGlobalId so
/// clash detection, issue linking, and BCF topic references all refer to
/// the same physical element regardless of which tool created it.
/// </summary>
public class ElementGlobalIdRegistry : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId  { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>IFC 22-character GUID — the canonical cross-tool identity.</summary>
    public string? IfcGlobalId { get; set; }

    /// <summary>ArchiCAD element GUID (from .ifc / API export).</summary>
    public string? ArchiCadGuid { get; set; }

    /// <summary>Revit UniqueId (stable, persists across file saves).</summary>
    public string? RevitUniqueId { get; set; }

    /// <summary>Tekla Structures GUID.</summary>
    public string? TeklaGuid { get; set; }

    /// <summary>ISO 19650 discipline code: A, S, M, E, P, FP, LV.</summary>
    public string? Discipline { get; set; }

    /// <summary>IFC entity type, e.g. IfcWall, IfcBeam, IfcDuctSegment.</summary>
    public string? IfcType { get; set; }

    /// <summary>Human-readable element name / mark captured at ingest time.</summary>
    public string? ElementName { get; set; }

    /// <summary>
    /// Harmonised level name (FK to ProjectLevel.NormalizedName). Allows the
    /// coordinator to verify that all tools agree on which storey this element
    /// belongs to.
    /// </summary>
    public string? NormalizedLevelName { get; set; }

    /// <summary>
    /// Lifecycle status of this mapping:
    /// AutoMatched | ManuallyMapped | Ambiguous | Conflicted | Archived
    /// </summary>
    public string MappingStatus { get; set; } = "AutoMatched";

    /// <summary>Display name of the user who last set or confirmed this mapping.</summary>
    public string? MappedBy { get; set; }

    /// <summary>Free-text coordinator notes (e.g. "Revit doors share GUID with ArchiCAD frame").</summary>
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
