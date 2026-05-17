namespace Planscape.Core.Entities;

/// <summary>
/// A subcontractor / trade scope — the unit at which a contractor
/// procures and packages BOQ items for tender. Each
/// <see cref="QuantityLine"/> can be optionally assigned to a work
/// package so the BOQ can be sliced per-package for tendering.
///
/// Typical packages on a large project: "Substructure", "Steel frame",
/// "Cladding", "Mechanical services", "Electrical services", "Lifts",
/// "Internal finishes", "External works", "Preliminaries".
/// </summary>
public class WorkPackage : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Short identifier — "WP-MECH", "WP-CLAD-01".</summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Discipline filter — primary discipline this package covers.</summary>
    public string? Discipline { get; set; }

    /// <summary>JSON array of section-code prefixes this package owns ("C", "G", "H").</summary>
    public string? SectionPrefixesJson { get; set; }

    /// <summary>Nominated contractor / supplier name (if known at tender).</summary>
    public string? Contractor { get; set; }

    /// <summary>Estimated package value at tender.</summary>
    public decimal? EstimatedValue { get; set; }

    /// <summary>Awarded package value (post-tender).</summary>
    public decimal? AwardedValue { get; set; }

    public string Currency { get; set; } = "GBP";

    /// <summary>Status: Draft / Tender / Awarded / OnSite / Complete / Closed.</summary>
    public string Status { get; set; } = "Draft";

    public DateTime? TenderIssuedAt { get; set; }
    public DateTime? AwardDate { get; set; }
    public DateTime? StartOnSite { get; set; }
    public DateTime? PracticalCompletion { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }

    public Project? Project { get; set; }
}
