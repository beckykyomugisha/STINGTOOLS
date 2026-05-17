namespace Planscape.Core.Entities;

/// <summary>
/// A take-off rule maps a class of BIM elements (by IFC type +
/// optional material/category/discipline filters) onto a
/// <see cref="ClassificationCode"/> with a prescribed quantification
/// formula. Rules are evaluated by the ingestion pipeline when a
/// project model is parsed — every element that matches a rule
/// produces a <see cref="QuantityLine"/>.
///
/// Rules are tenant-scoped (each firm encodes its own house standards)
/// and project-overridable: a project can opt into the firm-wide set
/// or define project-only overrides via <see cref="ProjectId"/>.
///
/// Example for NRM2 C20 (in-situ concrete):
///   IfcType   = "IfcWall"
///   MaterialPattern = "concrete|C\d+/\d+"
///   ClassificationCode = "C20"
///   Unit      = "m³"
///   QuantityFormula = "Qto_WallBaseQuantities.NetVolume"
///   DescriptionTemplate = "Reinforced concrete; {material}; wall;
///       vertical; {thicknessBand}mm thick; including formwork"
/// </summary>
public class TakeoffRule : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Null for firm-wide rules; set for project-specific overrides.</summary>
    public Guid? ProjectId { get; set; }

    public Guid ClassificationCodeId { get; set; }

    /// <summary>Rule name — appears in audit logs and the take-off authoring UI.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Lower numbers win on tie-break — a project-specific rule at
    /// priority 10 overrides a firm rule at priority 100.
    /// </summary>
    public int Priority { get; set; } = 100;

    public bool Enabled { get; set; } = true;

    // ── Match criteria (AND across columns; null = wildcard) ──

    /// <summary>IFC type filter — "IfcWall", "IfcSlab", "IfcDuctSegment", or "*" for any.</summary>
    public string? IfcType { get; set; }

    /// <summary>STING discipline filter — M/E/P/A/S/FP/LV/G/null.</summary>
    public string? Discipline { get; set; }

    /// <summary>Revit/IFC category — "Walls", "Floors", "Pipes". Regex supported.</summary>
    public string? CategoryPattern { get; set; }

    /// <summary>Regex matched against the element's primary material name.</summary>
    public string? MaterialPattern { get; set; }

    /// <summary>
    /// Optional property-set filter — JSON array of
    /// <c>{ "pset": "Pset_WallCommon", "prop": "LoadBearing", "op": "=", "value": true }</c>.
    /// All entries must match (logical AND).
    /// </summary>
    public string? PropertyFiltersJson { get; set; }

    // ── Output ──

    /// <summary>Unit of measurement — "m", "m²", "m³", "nr", "t", "kg".</summary>
    public string Unit { get; set; } = "nr";

    /// <summary>
    /// Quantity extraction formula. Three forms supported:
    /// <list type="bullet">
    /// <item><c>Qto_*.PropertyName</c> — read a BaseQuantities value direct from IFC.</item>
    /// <item><c>geom.volume / geom.area / geom.length / geom.count</c> — derived from geometry.</item>
    /// <item><c>{a} * {b} + 0.05</c> — simple expression over Qto/Pset values with a waste factor.</item>
    /// </list>
    /// </summary>
    public string QuantityFormula { get; set; } = "geom.count";

    /// <summary>
    /// NRM2-format item description template. Slot tokens are filled
    /// from element properties at ingestion: {material}, {grade},
    /// {thickness}, {thicknessBand}, {diameter}, {finishType},
    /// {fireRating}, {level}, {discipline}.
    /// </summary>
    public string DescriptionTemplate { get; set; } = "";

    /// <summary>Specification grade — "Basic", "Standard", "Enhanced", "Premium".</summary>
    public string? SpecificationGrade { get; set; }

    /// <summary>
    /// "Deemed included" items per NRM2 §3.6 — bullet list of work that
    /// is part of this rate and not measured separately
    /// (cutting, waste, lapping, fixing accessories).
    /// </summary>
    public string? DeemedIncludedJson { get; set; }

    /// <summary>
    /// Default waste-allowance percentage applied at ingestion
    /// (e.g. 5% for concrete, 10% for blockwork). Adds to the take-off
    /// quantity, recorded separately on the <see cref="QuantityLine"/>.
    /// </summary>
    public double WastePercent { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }

    public ClassificationCode? ClassificationCode { get; set; }
    public Project? Project { get; set; }
}
