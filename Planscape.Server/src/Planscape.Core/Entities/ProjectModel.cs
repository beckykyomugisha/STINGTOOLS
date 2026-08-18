namespace Planscape.Core.Entities;

/// <summary>
/// MODEL-VIEWER — 3D model attached to a project. One project can have many
/// models (multi-discipline federation, phased submissions, etc.). Storage is
/// abstracted via <see cref="Planscape.Core.Interfaces.IFileStorageService"/>
/// so the same entity works with local FS / MinIO / S3 / R2.
///
/// Supported formats:
///   glTF / GLB       — primary, renders natively in the WebView viewer
///   IFC              — stored for archival; client needs a converter
///   RVT              — stored for archival; not renderable in-browser
///
/// An optional <c>ElementMapPath</c> points to a JSON sidecar produced by the
/// Revit plugin mapping element GUID → ISO 19650 tag, category, location.
/// Mobile uses that to cross-reference selected 3D elements with STING tags.
/// </summary>
public class ProjectModel : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Human-readable name — defaults to file name minus extension.</summary>
    public string Name { get; set; } = "";

    /// <summary>Optional free-text description (purpose, discipline, phase).</summary>
    public string? Description { get; set; }

    /// <summary>Discipline code (A/M/E/P/S/FP/LV/G) used for the disciplines layer filter.</summary>
    public string? Discipline { get; set; }

    /// <summary>Original file name (with extension) — shown in the UI.</summary>
    public string FileName { get; set; } = "";

    /// <summary>Format: glTF, GLB, IFC, RVT. Determines how the viewer treats it.</summary>
    public ModelFormat Format { get; set; } = ModelFormat.Glb;

    /// <summary>Storage path / key returned by IFileStorageService — opaque.</summary>
    public string StoragePath { get; set; } = "";

    /// <summary>SHA-256 of the file — used for client-side dedup + cache-busting.</summary>
    public string? ContentHash { get; set; }

    public long FileSizeBytes { get; set; }

    /// <summary>Optional small thumbnail (PNG) stored alongside the main file.</summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>Optional element-map JSON sidecar (Revit plugin exports this).</summary>
    public string? ElementMapPath { get; set; }

    /// <summary>Number of renderable elements (populated by the plugin / converter).</summary>
    public int? ElementCount { get; set; }

    /// <summary>Bounding box — used by the viewer to centre + auto-frame on load.</summary>
    public double? BoundsMinX { get; set; }
    public double? BoundsMinY { get; set; }
    public double? BoundsMinZ { get; set; }
    public double? BoundsMaxX { get; set; }
    public double? BoundsMaxY { get; set; }
    public double? BoundsMaxZ { get; set; }

    /// <summary>Units the viewer should assume (mm / m / ft). Defaults to mm.</summary>
    public string Units { get; set; } = "mm";

    /// <summary>Source revision — links back to a DocumentRecord revision, or the RVT revision number.</summary>
    public string? Revision { get; set; }

    public string UploadedBy { get; set; } = "";
    public Guid? UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Soft-delete — kept for audit; bytes and row are purged by
    /// <c>ModelPurgeJob</c> after 30 days.
    ///
    /// <para>C4 — that job now exists. This comment promised it for a long time
    /// while nothing implemented it, so every soft-deleted model's GLB, element
    /// map and thumbnail stayed in object storage forever.</para>
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// C7 — where an IFC upload is in the conversion pipeline:
    /// <c>Pending</c> | <c>Converting</c> | <c>Done</c> | <c>Failed</c>, or null
    /// for a model that needed no conversion.
    ///
    /// <para><b>Why it exists.</b> <c>IfcToGlbConversionJob</c> is deliberately
    /// best-effort — a sidecar error is logged and never thrown, so a failed
    /// convert leaves the IFC stored and re-uploadable. But nothing recorded the
    /// failure anywhere the product could see it, and the upload endpoint had
    /// already answered <c>202 Accepted</c> with "a renderable GLB derivative is
    /// being generated and will appear shortly". For a conversion that failed,
    /// that sentence never stops being false: the coordinator waits, refreshes,
    /// and eventually concludes the platform is broken — which is a fair reading
    /// of a promise that is never withdrawn.</para>
    ///
    /// <para>Stored as text rather than an enum so a value written by a newer
    /// worker cannot fail to deserialise on an older API instance mid-deploy.</para>
    /// </summary>
    public string? ConversionStatus { get; set; }

    /// <summary>C7 — why the conversion failed, when it did. Null otherwise.</summary>
    public string? ConversionError { get; set; }

    /// <summary>
    /// C4 — the model that replaced this one, when it was retired by a forced
    /// re-publish rather than deleted outright.
    ///
    /// <para>The upload endpoint deduplicates on content hash behind a unique
    /// filtered index on (TenantId, ProjectId, ContentHash) WHERE DeletedAt IS
    /// NULL. "Publish as a new revision" therefore could not simply insert a
    /// second row with the same bytes — it would violate that index — which is
    /// why the <c>Force</c> flag sat unread and the plugin's
    /// ForceNewRevision mode silently did a metadata refresh instead.</para>
    ///
    /// <para>Retiring the old row (soft-delete) frees the index and makes the
    /// relationship explicit: the history stays queryable, and "why did this
    /// model disappear?" has an answer that points at its replacement.</para>
    /// </summary>
    public Guid? SupersededByModelId { get; set; }

    /// <summary>
    /// Set when a /file fetch finds the row but the bytes are gone from
    /// storage (typical cause: container rebuild without the persistent
    /// volume mount). The viewer surfaces this as an actionable
    /// "Republish from Revit" CTA instead of a generic 404, and the
    /// dashboard / federation-status endpoint can flag broken entries.
    /// Cleared on the next successful republish.
    /// </summary>
    public DateTime? StorageMissingAt { get; set; }

    public Project? Project { get; set; }
    public AppUser? UploadedByUser { get; set; }
}

public enum ModelFormat
{
    Glb = 0,
    Gltf = 1,
    Ifc = 2,
    Rvt = 3,
    Obj = 4,
    Fbx = 5,
}
