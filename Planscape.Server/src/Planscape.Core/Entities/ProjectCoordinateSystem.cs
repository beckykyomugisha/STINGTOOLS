namespace Planscape.Core.Entities;

/// <summary>
/// Gap A — Single-source coordinate system anchor for a project.
/// All uploaded models are validated against this definition so the
/// coordinator has one place to declare "what coordinate system this
/// project uses". Without it, alignment checks are purely relative.
/// </summary>
public class ProjectCoordinateSystem : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId  { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>EPSG code of the target CRS, e.g. "EPSG:27700" (British National Grid), "EPSG:32636".</summary>
    public string? CrsEpsgCode { get; set; }

    /// <summary>Human-readable CRS name, e.g. "British National Grid".</summary>
    public string? CrsName { get; set; }

    /// <summary>Project benchmark origin — easting in the CRS (metres).</summary>
    public double? OriginEasting { get; set; }

    /// <summary>Project benchmark origin — northing in the CRS (metres).</summary>
    public double? OriginNorthing { get; set; }

    /// <summary>Project benchmark elevation above datum (metres).</summary>
    public double? OriginElevation { get; set; }

    /// <summary>
    /// True north angle from the CRS Y-axis, clockwise positive (degrees).
    /// 0 = project Y aligns with CRS north. All models must export with
    /// this rotation to align.
    /// </summary>
    public double TrueNorthDeg { get; set; } = 0.0;

    /// <summary>Canonical project length unit: "mm" | "m". All models should use this unit.</summary>
    public string LengthUnit { get; set; } = "mm";

    /// <summary>
    /// The ProjectModel that all other models should align to.
    /// When set, the auto-align service uses this model's IfcMapConversion
    /// as the reference frame and computes relative transforms for new uploads.
    /// </summary>
    public Guid? ReferenceModelId { get; set; }

    /// <summary>Display name of the user who defined this coordinate system.</summary>
    public string? DefinedBy { get; set; }

    public DateTime? DefinedAt { get; set; }

    /// <summary>Free-text notes, e.g. survey report reference, benchmark monument ID.</summary>
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt  { get; set; }

    public Project? Project { get; set; }
}
