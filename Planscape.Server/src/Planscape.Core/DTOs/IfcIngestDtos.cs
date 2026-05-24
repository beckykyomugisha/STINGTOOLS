namespace Planscape.Core.DTOs;

/// <summary>
/// Payload for POST /api/projects/{id}/ifc/data — used by every host
/// plugin (Revit, BlenderBIM, ArchiCAD, Tekla) to ingest IFC-derived
/// element data along with the host-element-id ↔ ifc-guid mapping.
///
/// Distinct from TagSyncRequest in that it carries IfcGlobalId as the
/// primary key (cross-host stable) instead of Revit-specific
/// RevitElementId, and it carries explicit host attribution so the
/// server can populate ExternalElementMapping.
/// </summary>
public record IfcIngestRequest
{
    /// <summary>Host identifier: revit | blender | archicad | tekla.</summary>
    public string Host { get; init; } = "";

    /// <summary>
    /// Host-side document GUID (Revit RVT GUID, Blender .blend path hash,
    /// ArchiCAD doc GUID). May be null for stateless / headless ingests.
    /// </summary>
    public string? HostDocumentGuid { get; init; }

    /// <summary>Optional metadata about the producing plugin (version, user).</summary>
    public string PluginVersion { get; init; } = "";
    public string UserName { get; init; } = "";

    public List<IfcElementDto> Elements { get; init; } = new();
}

public record IfcElementDto
{
    /// <summary>IFC GlobalId — 22-char base64, stable per element across hosts.</summary>
    public string IfcGlobalId { get; init; } = "";

    /// <summary>Host-side element identifier (Revit ElementId, Blender object name, etc).</summary>
    public string HostElementId { get; init; } = "";

    /// <summary>Human-readable label for the host element (debugging / audit).</summary>
    public string? HostDisplayLabel { get; init; }

    // ---- 8-segment STING tag ----
    public string Discipline { get; init; } = "";
    public string Location { get; init; } = "";
    public string Zone { get; init; } = "";
    public string Level { get; init; } = "";
    public string System { get; init; } = "";
    public string Function { get; init; } = "";
    public string Product { get; init; } = "";
    public string Sequence { get; init; } = "";
    public string FullTag { get; init; } = "";

    // ---- context ----
    public string IfcClass { get; init; } = "";     // IfcWall, IfcDoor, etc.
    public string CategoryName { get; init; } = "";
    public string FamilyName { get; init; } = "";
    public string TypeName { get; init; } = "";
    public string? Status { get; init; }            // NEW/EXISTING/DEMOLISHED/...
    public string? Rev { get; init; }
    public string? RoomName { get; init; }
    public string? LevelName { get; init; }

    // ---- compliance ----
    public bool IsComplete { get; init; }
    public bool IsFullyResolved { get; init; }
    public bool IsStale { get; init; }
    public string? ValidationErrors { get; init; }   // JSON

    /// <summary>Client-wall-clock timestamp of last modification.</summary>
    public DateTime? LastModifiedUtc { get; init; }
}

public record IfcIngestResponse
{
    public int NewMappings { get; init; }
    public int UpdatedMappings { get; init; }
    public int NewElements { get; init; }
    public int UpdatedElements { get; init; }
    public int Skipped { get; init; }
    public List<string> Warnings { get; init; } = new();
    public string Summary => $"{NewMappings + UpdatedMappings} mappings, " +
                             $"{NewElements + UpdatedElements} elements; {Skipped} skipped";
}
