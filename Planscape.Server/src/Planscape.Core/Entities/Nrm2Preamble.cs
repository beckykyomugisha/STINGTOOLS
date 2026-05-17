namespace Planscape.Core.Entities;

/// <summary>
/// A reusable NRM2 preamble clause — the text printed at the head of a
/// work section describing materials standards, workmanship,
/// tolerances, testing, and protection requirements. Per NRM2 §4 every
/// work section in a compliant BOQ must carry preambles.
///
/// Clauses live in a tenant-scoped library that draws on a public
/// reference library (TenantId = null, seeded from BS/NBS standards).
/// They are attached to a project's BOQ via
/// <see cref="BoqPreambleAssignment"/> rows.
/// </summary>
public class Nrm2Preamble : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Null = public reference clause; set = tenant override or addition.</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// NRM2 section the clause belongs to (e.g. "C20" for in-situ
    /// concrete). Clauses without a section apply to Section A
    /// (preliminaries).
    /// </summary>
    public string? NrmSectionCode { get; set; }

    /// <summary>
    /// Clause group — "Materials", "Workmanship", "Testing",
    /// "Tolerances", "Protection", "Specification", "General".
    /// </summary>
    public string Group { get; set; } = "General";

    /// <summary>Short title — "Cement and aggregates".</summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// The clause text. Token slots are filled at render: {projectName},
    /// {client}, {contractType}, {revision}. Supports markdown for
    /// nested lists in the printed BOQ.
    /// </summary>
    public string Body { get; set; } = "";

    /// <summary>
    /// Comma-separated reference standards cited by this clause —
    /// "BS 8500-1:2023, BS EN 206-1, BS EN 197-1".
    /// </summary>
    public string? References { get; set; }

    /// <summary>Sort order within (NrmSectionCode, Group).</summary>
    public int SortOrder { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}

/// <summary>
/// Join row attaching a preamble clause to a project's BOQ document
/// (so the same clause library can be re-used across projects with
/// per-project free-text overrides).
/// </summary>
public class BoqPreambleAssignment : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid BoqDocumentId { get; set; }
    public Guid PreambleId { get; set; }

    /// <summary>Per-project free-text override (replaces Body if not null).</summary>
    public string? OverrideBody { get; set; }

    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Nrm2Preamble? Preamble { get; set; }
}
