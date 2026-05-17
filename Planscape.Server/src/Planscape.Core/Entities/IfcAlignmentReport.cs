namespace Planscape.Core.Entities;

/// <summary>
/// Per-model alignment validation report. Generated at upload time by
/// IfcAlignmentValidator. Captures whether the IFC has consistent
/// georeferencing across federated discipline models — the single most
/// common cause of cross-software coordination failure.
/// </summary>
public class IfcAlignmentReport : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ProjectModelId { get; set; }

    /// <summary>IFC schema version: IFC2X3, IFC4, IFC4X3.</summary>
    public string? SchemaVersion { get; set; }

    /// <summary>IFC site GUID — should match across all discipline models on the same project.</summary>
    public string? IfcSiteGuid { get; set; }

    /// <summary>Project units string ("MILLI" / "METRE" / "FOOT"). Mismatches cause 1000× scale errors.</summary>
    public string? LengthUnit { get; set; }

    /// <summary>True north angle (degrees from project Y axis). Mismatches rotate models against each other.</summary>
    public double? TrueNorthDegrees { get; set; }

    /// <summary>Survey point easting (project base point in world coordinates).</summary>
    public double? SurveyEasting { get; set; }
    public double? SurveyNorthing { get; set; }
    public double? SurveyElevation { get; set; }

    /// <summary>Has IfcMapConversion (IFC4+ georeferencing).</summary>
    public bool HasMapConversion { get; set; }

    /// <summary>Has IfcProjectedCRS (coordinate reference system identifier).</summary>
    public bool HasProjectedCrs { get; set; }

    /// <summary>CRS name (e.g. "EPSG:27700" — British National Grid). Critical for site coordination.</summary>
    public string? CrsName { get; set; }

    /// <summary>Overall alignment quality: PASS / WARN / FAIL.</summary>
    public string Verdict { get; set; } = "WARN";

    /// <summary>JSON-serialized list of validation findings (severity + code + message + fixHint).</summary>
    public string FindingsJson { get; set; } = "[]";

    public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;
}

public sealed record IfcAlignmentFinding(
    string Severity,           // INFO / WARN / FAIL
    string Code,               // e.g. "NO_TRUE_NORTH", "UNIT_MISMATCH"
    string Message,            // Human-readable
    string? FixHint            // How to fix in source software
);
