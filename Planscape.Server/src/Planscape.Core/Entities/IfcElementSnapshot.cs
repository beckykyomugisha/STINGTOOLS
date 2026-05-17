namespace Planscape.Core.Entities;

/// <summary>
/// Gap-5 — Per-element IFC change tracking snapshot.
///
/// One row is written per IfcElement per upload pass. Comparing the
/// <see cref="PropertiesHash"/> of the latest row against the previous
/// upload's row gives instant Added / Modified / Deleted / Unchanged
/// classification without loading the full property bag into memory.
///
/// Rows are append-only — older snapshots are never deleted so the full
/// change history is preserved for audit / trend queries.
/// </summary>
public class IfcElementSnapshot : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ── Tenant / project scoping ──────────────────────────────────────
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>
    /// The specific model upload this snapshot belongs to.
    /// Corresponds to the <c>ProjectModel</c> (or equivalent) record
    /// created when the IFC file was uploaded.
    /// </summary>
    public Guid ProjectModelId { get; set; }

    // ── Element identity ──────────────────────────────────────────────
    /// <summary>IFC 22-character compressed GUID (stable across exports).</summary>
    public string IfcGuid { get; set; } = "";

    /// <summary>IFC schema type, e.g. "IfcWall", "IfcDoor", "IfcBeam".</summary>
    public string IfcType { get; set; } = "";

    /// <summary>Human-readable element name from the IFC file, if present.</summary>
    public string? Name { get; set; }

    /// <summary>Building storey name the element is assigned to, if resolved.</summary>
    public string? Storey { get; set; }

    /// <summary>
    /// ISO 19650 discipline code inferred from the element type or property
    /// sets (e.g. "M" for mechanical, "E" for electrical).  Null when
    /// discipline cannot be determined.
    /// </summary>
    public string? Discipline { get; set; }

    // ── Axis-aligned bounding box (mm) ────────────────────────────────
    /// <summary>Minimum X coordinate of the element bounding box in mm.</summary>
    public double? MinX { get; set; }

    /// <summary>Minimum Y coordinate of the element bounding box in mm.</summary>
    public double? MinY { get; set; }

    /// <summary>Minimum Z coordinate of the element bounding box in mm.</summary>
    public double? MinZ { get; set; }

    /// <summary>Maximum X coordinate of the element bounding box in mm.</summary>
    public double? MaxX { get; set; }

    /// <summary>Maximum Y coordinate of the element bounding box in mm.</summary>
    public double? MaxY { get; set; }

    /// <summary>Maximum Z coordinate of the element bounding box in mm.</summary>
    public double? MaxZ { get; set; }

    // ── Change tracking ───────────────────────────────────────────────
    /// <summary>
    /// SHA-256 hex digest of the serialized property bag. Used for fast
    /// change detection — a hash mismatch triggers "Modified" without
    /// deserializing both property bags.
    /// </summary>
    public string PropertiesHash { get; set; } = "";

    /// <summary>
    /// Classification relative to the previous upload for this project model.
    /// One of: "Added" | "Modified" | "Deleted" | "Unchanged".
    /// </summary>
    public string ChangeKind { get; set; } = "Added";

    /// <summary>UTC timestamp at which this snapshot was written.</summary>
    public DateTime SnapshotAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Monotonically increasing counter per project, supplied by the caller.
    /// Allows ordering snapshots chronologically without relying on wall-clock
    /// timestamps (which can collide under rapid re-uploads).
    /// </summary>
    public int UploadSequence { get; set; }
}
