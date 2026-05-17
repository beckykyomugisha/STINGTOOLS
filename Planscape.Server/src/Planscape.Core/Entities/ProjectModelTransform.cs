namespace Planscape.Core.Entities;

/// <summary>
/// Gap B — Persistent coordinate transform for a federated model.
/// Stores the manual (or auto-computed) correction that brings this
/// model into the project's canonical coordinate system. Kept separate
/// from ProjectModel so the main entity stays lean and the transform
/// history can be audited.
///
/// Convention: all values are in the project's LengthUnit (mm by
/// default). RotationDeg is the clockwise Z-axis rotation applied
/// after translation — positive = rotate east toward north.
/// </summary>
public class ProjectModelTransform : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId  { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ProjectModelId { get; set; }

    /// <summary>X translation in project length units (mm default).</summary>
    public double TranslationX { get; set; } = 0;

    /// <summary>Y translation in project length units (mm default).</summary>
    public double TranslationY { get; set; } = 0;

    /// <summary>Z translation (elevation correction, mm default).</summary>
    public double TranslationZ { get; set; } = 0;

    /// <summary>Z-axis rotation correction in degrees (clockwise positive).</summary>
    public double RotationDeg { get; set; } = 0;

    /// <summary>Uniform scale correction (1.0 = no change; use for mm vs m unit fixes).</summary>
    public double ScaleFactor { get; set; } = 1.0;

    /// <summary>True when the transform was computed from IfcMapConversion data (Gap F).</summary>
    public bool IsAutoComputed { get; set; } = false;

    /// <summary>True when a coordinator has explicitly confirmed or manually set the transform.</summary>
    public bool IsConfirmed { get; set; } = false;

    /// <summary>Display name of the user who last applied or confirmed this transform.</summary>
    public string? AppliedBy { get; set; }

    public DateTime? AppliedAt { get; set; }

    /// <summary>Coordinator notes explaining why this correction was needed.</summary>
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt  { get; set; }

    public ProjectModel? Model { get; set; }
}
