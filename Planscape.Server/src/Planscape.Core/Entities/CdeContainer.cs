namespace Planscape.Core.Entities;

/// <summary>
/// ISO 19650 information container — a folder node in the CDE hierarchy.
/// Supports arbitrary depth via self-referencing ParentContainerId.
/// Replaces the implied "folder = CDE state + discipline" pattern with an
/// explicit tree that mirrors ACC / Aconex folder structures.
/// </summary>
public class CdeContainer : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";

    /// <summary>
    /// Self-referencing FK. Null = root container for this project.
    /// </summary>
    public Guid? ParentContainerId { get; set; }

    /// <summary>
    /// Broad categorisation: DISCIPLINE / PHASE / DOCUMENT_TYPE / CUSTOM.
    /// </summary>
    public string? ContainerType { get; set; }

    /// <summary>
    /// ISO 19650 discipline code (M, E, P, A, S …) — optional tag for
    /// quick ACL filtering.
    /// </summary>
    public string? Discipline { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; } = 0;
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public Project? Project { get; set; }
    public CdeContainer? Parent { get; set; }
    public List<CdeContainer> Children { get; set; } = new();
    public List<DocumentRecord> Documents { get; set; } = new();
}
