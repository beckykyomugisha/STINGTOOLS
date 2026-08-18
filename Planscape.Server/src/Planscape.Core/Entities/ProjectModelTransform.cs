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

    /// <summary>
    /// True when the platform applied this transform on its own, without a
    /// coordinator confirming it, because the georeferencing behind it graded
    /// <see cref="Planscape.Core.Coordinates.TransformConfidence.High"/>.
    ///
    /// <para><b>Why this is not just <see cref="IsAutoComputed"/>.</b>
    /// <c>IsAutoComputed</c> records HOW the numbers were obtained; this records
    /// whether they are LIVE. The two came apart badly: both automatic writers
    /// stored <c>IsAutoComputed=true, IsConfirmed=false</c>, and the viewer
    /// applied a transform only when <c>IsConfirmed</c> was not false — so a
    /// perfectly good survey-derived alignment was computed, stored, and then
    /// never rendered until somebody confirmed it by hand. That is the whole
    /// "models don't line up automatically" symptom.</para>
    ///
    /// <para><b>Precedence.</b> <see cref="IsConfirmed"/> outranks this, and the
    /// precedence is enforced when WRITING, not when rendering: an automatic
    /// writer refuses to touch a row with <c>IsConfirmed=true</c>, so a
    /// confirmed row always holds the coordinator's numbers and there is nothing
    /// to reconcile at read time.</para>
    /// </summary>
    public bool AppliedAutomatically { get; set; } = false;

    /// <summary>
    /// How far the georeferencing evidence was trusted — "HIGH" | "LOW" | "NONE"
    /// (see <see cref="Planscape.Core.Coordinates.TransformConfidencePolicy"/>).
    /// Stored as text so the value is legible in a database session and survives
    /// enum reordering. Null on rows written before this field existed and on
    /// purely manual transforms.
    /// </summary>
    public string? Confidence { get; set; }

    /// <summary>
    /// Which pipeline produced the numbers: "manual" | "ifc-map-conversion" |
    /// "auto-align" | "revit-georef". <see cref="AppliedBy"/> records WHO,
    /// this records WHAT — needed because the UI has to explain to a
    /// coordinator why a model moved on its own.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>Display name of the user who last applied or confirmed this transform.</summary>
    public string? AppliedBy { get; set; }

    public DateTime? AppliedAt { get; set; }

    /// <summary>Coordinator notes explaining why this correction was needed.</summary>
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt  { get; set; }

    public ProjectModel? Model { get; set; }
}
