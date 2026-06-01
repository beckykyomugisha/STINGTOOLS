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
///
/// AUTHORITY: this table is the single source of truth for
/// <b>"IFC GlobalId ↔ host element id" resolution</b> (which Revit/Blender/
/// ArchiCAD/IoT element a given GlobalId is). The denormalised
/// <c>*IfcGlobalId</c> columns on capture entities
/// (<see cref="HealthcarePressureLog.RoomIfcGlobalId"/>,
/// <see cref="PenetrationSignoff.ElementIfcGlobalId"/>) answer a DIFFERENT
/// question — "which IFC element is this record about" — and are authoritative
/// for THAT statement only. The two surfaces are written by independent paths
/// and may legitimately diverge: a GlobalId can be mapped here with no capture
/// row referencing it, and a capture row can carry a GlobalId that was never
/// ingested cross-host. Neither is derived from the other; consumers needing
/// both (e.g. <c>healthcare/by-ifc</c>) must tolerate either side being absent.
/// Rows are written best-effort/fire-and-forget from TagSync + ArchiCAD, so can
/// also be transiently missing after a dropped background write — recovered by
/// <c>MappingReconciliationJob</c> and observable via AuditLog.
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
