namespace Planscape.Core.Entities;

/// <summary>
/// FEDERATED-MODEL: A single element's tessellated geometry in the unified
/// project geometry store. One row per (ProjectId, SourceDocGuid, ElementId).
///
/// Ingest sources:
///   • Revit plugin — direct GLB delta via POST /federated-model/delta
///   • IFC hot-folder — parsed via IfcOpenShell ingest adapter (server-side)
///   • Speckle stream  — displayValue mesh extracted by SpeckleAdapter
///
/// The GLB bytes for the element are stored as a storage-path reference so
/// individual element geometry can be served to the viewer on demand. A
/// project-level combined GLB is rebuilt by FederatedModelBuilder whenever
/// the element set changes.
/// </summary>
public class FederatedElement : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId  { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Source document GUID (Revit ProjectInformation.UniqueId or IFC GUID).</summary>
    public string SourceDocGuid { get; set; } = "";

    /// <summary>Revit element integer Id within the source document.</summary>
    public long ElementId { get; set; }

    /// <summary>Revit UniqueId (stable across saves; used by the viewer for tagging).</summary>
    public string UniqueId { get; set; } = "";

    /// <summary>IFC global id (hex export id from ExportUtils.GetExportId).</summary>
    public string? IfcGuid { get; set; }

    /// <summary>Revit category name (Walls, Duct Curves, …).</summary>
    public string? Category { get; set; }

    /// <summary>
    /// Storage path / key for the per-element GLB file in IFileStorageService.
    /// Null for elements that arrived via a combined-file ingest (IFC hot-folder).
    /// </summary>
    public string? GlbStoragePath { get; set; }

    /// <summary>Bounding-box for frustum culling in the viewer (metres).</summary>
    public float MinX { get; set; }
    public float MinY { get; set; }
    public float MinZ { get; set; }
    public float MaxX { get; set; }
    public float MaxY { get; set; }
    public float MaxZ { get; set; }

    /// <summary>Ingest source identifier: "revit-plugin", "ifc-hotfolder", "speckle".</summary>
    public string Source { get; set; } = "revit-plugin";

    /// <summary>True when the element has been deleted in Revit; kept for 30 days then purged.</summary>
    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Project? Project { get; set; }
}
