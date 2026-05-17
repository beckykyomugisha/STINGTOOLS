namespace Planscape.Core.Entities;

/// <summary>
/// A snapshot of every <see cref="QuantityLine"/> at a fixed point in
/// the project lifecycle — Tender BOQ, Contract BOQ, Interim Valuation
/// #N, Final Account. Once a baseline is created, the lines it
/// references become immutable (BaselineId set on each line).
/// Subsequent changes are tracked as <see cref="BoqVariation"/>
/// records against the baseline.
///
/// A project will typically have:
///   Tender baseline      → frozen at S3 PUBLISHED
///   Contract baseline    → frozen on contract execution
///   Interim baseline #N  → frozen at each monthly valuation date
///   Final Account        → frozen at final certificate
/// </summary>
public class BoqBaseline : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Baseline kind: Tender / Contract / Interim / Final / AdHoc.</summary>
    public string Kind { get; set; } = "Tender";

    /// <summary>Human label — "Tender BOQ rev 0", "Interim Valuation #5".</summary>
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>Issue/valuation date.</summary>
    public DateTime BaselinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Number of <see cref="QuantityLine"/> rows captured.</summary>
    public int LineCount { get; set; }

    /// <summary>Sum of LineTotal across all lines in baseline currency.</summary>
    public decimal TotalValue { get; set; }

    public string Currency { get; set; } = "GBP";

    /// <summary>Optional reference back to a published <see cref="DocumentRecord"/>.</summary>
    public Guid? DocumentRecordId { get; set; }

    /// <summary>
    /// SHA-256 of (sorted line ids + total + count) — tamper-evidence
    /// for legal audit (NRM2 BOQ is a contract document).
    /// </summary>
    public string? Checksum { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }

    public Project? Project { get; set; }
}
