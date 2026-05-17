namespace Planscape.Core.Entities;

/// <summary>
/// T4-28 — Generic Asset Data Sheet engine. Generalises the Healthcare
/// Pack RDS (Room Data Sheet) pattern beyond clinical projects:
///
///   "structured data sheet linked to a BIM anchor (room / element /
///    asset / system), populated from a JSON-defined template, with
///    a typed value bag, optional sign-off + audit trail, and a
///    deterministic render output."
///
/// Use cases:
///   - Room Data Sheets (clinical RDS — already shipping)
///   - Asset commissioning sheets (MEP equipment hand-off)
///   - Structural test records
///   - Fire-strategy compartment data
///   - Kitchen equipment specs (food-and-beverage projects)
///   - Lab equipment data sheets (pharma)
///
/// Templates are tenant-scoped JSON definitions stored in
/// <see cref="AssetDataSheetTemplate"/>; instances are this entity.
/// The engine validates submitted ValuesJson against the template's
/// field schema (required + types + value lists) before persist.
/// </summary>
public class AssetDataSheet : ITenantScoped
{
    public Guid Id        { get; set; } = Guid.NewGuid();
    public Guid TenantId  { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Reference to the template definition. Versioned so a
    /// template-edit doesn't retroactively invalidate existing
    /// instances.</summary>
    public Guid   TemplateId      { get; set; }
    public int    TemplateVersion { get; set; } = 1;

    /// <summary>The kind of anchor this sheet describes — purely a
    /// dispatch hint for the UI, since AnchorKey can mean different
    /// things per anchor type. One of: "Room", "Element", "Asset",
    /// "System", "Project".</summary>
    public string AnchorKind { get; set; } = "Room";

    /// <summary>
    /// The anchor identifier. Interpretation depends on AnchorKind:
    ///   Room    → Revit Room.UniqueId or IFC IfcSpace.GlobalId
    ///   Element → BIM element GUID (Revit UniqueId / IFC GlobalId)
    ///   Asset   → planscape MIM Asset id
    ///   System  → MEP system id / system name
    ///   Project → null (project-level sheet)
    /// </summary>
    public string? AnchorKey { get; set; }

    /// <summary>Field values, JSONB. Shape matches the template's
    /// fields[] definitions exactly: { fieldKey: value, ... }.</summary>
    public string ValuesJson { get; set; } = "{}";

    /// <summary>Computed completeness percentage at last save —
    /// (filled required fields / total required fields × 100).
    /// Surfaces in the dashboard without a per-row recomputation.</summary>
    public int CompletenessPct { get; set; }

    // Lifecycle — Draft → Submitted → Approved (or Rejected)
    public string  Status     { get; set; } = "Draft";
    public Guid?   AuthorUserId { get; set; }
    public string  AuthorName   { get; set; } = "";
    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt   { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt  { get; set; }
    public Guid?     ApprovedByUserId { get; set; }
    public string?   RejectedReason   { get; set; }

    public Project? Project { get; set; }
    public AppUser? Author  { get; set; }
}

/// <summary>
/// JSON-defined schema for a family of asset data sheets. Templates
/// are tenant-scoped so each customer can define their own catalogue
/// (Healthcare RDS, MEP commissioning, lab spec, etc.) without
/// touching the platform schema.
/// </summary>
public class AssetDataSheetTemplate : ITenantScoped
{
    public Guid Id       { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Unique-per-tenant template id (e.g. "rds-clinical-v3",
    /// "mep-commissioning-v1"). Lower-case + hyphen; surfaced in URLs.</summary>
    public string Code { get; set; } = "";

    public string Name        { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>One of: "Room", "Element", "Asset", "System", "Project".
    /// Templates are anchored to a single kind; can't move between
    /// kinds without a new template.</summary>
    public string AnchorKind { get; set; } = "Room";

    /// <summary>
    /// JSON schema for the field set. Shape:
    ///   {
    ///     "fields": [
    ///       { "key": "area_m2", "label": "Floor area",
    ///         "type": "number", "required": true, "unit": "m²" },
    ///       { "key": "fire_rating", "label": "Fire rating",
    ///         "type": "enum", "required": true,
    ///         "values": ["FR0", "FR30", "FR60", "FR120"] },
    ///       …
    ///     ],
    ///     "groups": [
    ///       { "label": "Envelope", "fields": ["area_m2", "fire_rating"] }
    ///     ]
    ///   }
    /// </summary>
    public string SchemaJson { get; set; } = "{}";

    public int     Version   { get; set; } = 1;
    public bool    IsActive  { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Tenant? Tenant { get; set; }
}
