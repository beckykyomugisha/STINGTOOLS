namespace Planscape.Core.Entities;

/// <summary>
/// Gap 7 — Level harmonisation. One row per floor / storey in the
/// normalised level dictionary for a project. Each tool (ArchiCAD,
/// Revit, Tekla) may name the same physical storey differently
/// ("Level 01", "01", "L01", "1st Floor"). The coordinator defines
/// the canonical NormalizedName here and records per-tool aliases
/// in ToolMappingsJson so the clash detector and viewer can group
/// results correctly regardless of source tool.
/// </summary>
public class ProjectLevel : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId  { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Canonical storey name used internally (e.g. "L01", "GF", "B1", "RF").
    /// Matches STING's LVL token convention so federation and tagging stay aligned.
    /// </summary>
    public string NormalizedName { get; set; } = "";

    /// <summary>Human-readable label shown in the UI (e.g. "Ground Floor").</summary>
    public string? DisplayName { get; set; }

    /// <summary>Floor-to-floor elevation above project datum, in metres.</summary>
    public double? ElevationM { get; set; }

    /// <summary>
    /// Display sort order (0 = lowest, ascending). Allows non-alphabetic level
    /// ordering (basement levels appear before ground floor, etc.).
    /// </summary>
    public int SortIndex { get; set; }

    /// <summary>
    /// JSON array of per-tool level name mappings:
    /// [{ "tool": "ArchiCAD", "toolLevelName": "01" },
    ///  { "tool": "Revit",    "toolLevelName": "Level 01" },
    ///  { "tool": "Tekla",    "toolLevelName": "1st Floor" }]
    /// Stored as JSON to avoid a separate join table for this narrow use-case.
    /// </summary>
    public string? ToolMappingsJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
