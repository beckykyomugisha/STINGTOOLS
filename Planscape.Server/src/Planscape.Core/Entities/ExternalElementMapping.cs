namespace Planscape.Core.Entities;

/// <summary>
/// Cross-host element identity mapping. Maps an IFC GlobalId (the
/// only stable cross-host key) to one or more host-specific element
/// ids (Revit ElementId, Blender object name, ArchiCAD GUID, Tekla
/// Identifier.GUID).
///
/// Every IFC ingest (POST /api/projects/{id}/ifc/data) upserts rows
/// here so an issue raised in one host on IfcGlobalId X surfaces in
/// every other host on the matching host_element_id.
///
/// Primary key is composite: (IfcGlobalId, Host, HostDocumentGuid).
/// Rationale: the same IFC GlobalId may appear in multiple linked
/// documents (federated models), and each pairing has its own
/// host_element_id.
/// </summary>
public class ExternalElementMapping : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>The IFC GlobalId — 22-char base64, stable per element across hosts.</summary>
    public string IfcGlobalId { get; set; } = "";

    /// <summary>Host plugin identifier: revit | blender | archicad | tekla.</summary>
    public string Host { get; set; } = "";

    /// <summary>The host-side element identifier (Revit ElementId, Blender object name, etc).</summary>
    public string HostElementId { get; set; } = "";

    /// <summary>
    /// The host document the element lives in (Revit RVT GUID, Blender .blend path hash,
    /// ArchiCAD doc GUID). Distinguishes the same IfcGlobalId in different federated docs.
    /// </summary>
    public string? HostDocumentGuid { get; set; }

    /// <summary>Free-text label for the host element (e.g. "Wall.042"). Helps debugging.</summary>
    public string? HostDisplayLabel { get; set; }

    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Tracks how many ingestion runs have touched this mapping.</summary>
    public int IngestionCount { get; set; } = 1;

    // Navigation
    public Tenant? Tenant { get; set; }
    public Project? Project { get; set; }
}
