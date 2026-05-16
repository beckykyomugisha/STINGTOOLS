namespace Planscape.Core.Entities;

/// <summary>
/// A classification system used for BOQ work-section coding —
/// NRM2 (RICS), NRM1, Uniclass 2015, MasterFormat 2020, OmniClass,
/// CSI WBS, or a project-specific scheme.
///
/// Each row is the parent of many <see cref="ClassificationCode"/>
/// rows (the actual section codes). Systems are tenant-scoped because
/// large firms typically maintain their own modified/extended cuts
/// of the public standards (e.g. "Planscape-NRM2 v3" with internal
/// preamble overrides). The public reference systems are seeded at
/// system level (TenantId = null).
/// </summary>
public class ClassificationSystem : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Null for public reference systems (NRM2, Uniclass 2015, MasterFormat).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Short identifier — "NRM2", "UNICLASS_2015", "MASTERFORMAT_2020", "WBS".</summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Issuing body — "RICS", "NBS", "CSI", "ISO".</summary>
    public string? Authority { get; set; }

    /// <summary>Published edition — "2nd edition 2021", "v1.18", etc.</summary>
    public string? Edition { get; set; }

    /// <summary>
    /// Default measurement protocol — "NRM2_RULES", "POMI", "ASMM", "SMM7", "CSI_SECTION".
    /// Drives which take-off rules are applied when this system is
    /// the primary BOQ classification.
    /// </summary>
    public string? MeasurementProtocol { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<ClassificationCode> Codes { get; set; } = new();
}
