namespace Planscape.Core.Entities;

/// <summary>
/// A single code within a <see cref="ClassificationSystem"/> — e.g.
/// NRM2 section "C20" (In-situ concrete) or Uniclass "Ss_25_30_50"
/// (Concrete walls). Codes form a tree via <see cref="ParentCodeId"/>;
/// a typical NRM2 hierarchy is:
///
///   C          → Concrete works (level 1)
///     C20      → In-situ concrete (level 2)
///       C20.1  → Plain concrete (level 3, derived/project-specific)
///
/// Code is unique within (SystemId, Code). The Path column stores the
/// full dotted ancestry ("C.C20.C20.1") for cheap tree queries without
/// recursive CTEs.
/// </summary>
public class ClassificationCode : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Inherits from parent <see cref="ClassificationSystem"/>; null for seeded public codes.</summary>
    public Guid TenantId { get; set; }

    public Guid SystemId { get; set; }
    public Guid? ParentCodeId { get; set; }

    /// <summary>Section identifier — "C20", "Ss_25_30_50", "03 30 00".</summary>
    public string Code { get; set; } = "";

    /// <summary>Human-readable section title — "In-situ concrete".</summary>
    public string Title { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>Hierarchy depth — 1 for top sections, 2/3/4 for sub-sections.</summary>
    public int Level { get; set; } = 1;

    /// <summary>Dotted ancestry ("C.C20") for cheap prefix queries.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Default unit of measurement for this section per the parent
    /// system's rules — "m", "m²", "m³", "nr", "t", "kg", "h", "sum",
    /// "item". Take-off rules can override per element category.
    /// </summary>
    public string? DefaultUnit { get; set; }

    /// <summary>
    /// Whether this code is a "header" (no measured items attached, just
    /// groups children) or a "leaf" (carries quantity lines).
    /// </summary>
    public bool IsLeaf { get; set; } = true;

    public int SortOrder { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ClassificationSystem? System { get; set; }
    public ClassificationCode? Parent { get; set; }
}
