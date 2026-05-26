namespace Planscape.Core.Entities;

/// <summary>
/// A monthly interim payment certificate against a contract. Matches
/// the plugin-side `StingTools.Core.PaymentCert.PaymentCertificate`
/// shape (Phase 184g / P5.1).
///
/// Lifecycle: Draft → Issued → (Disputed | Agreed) → Paid → Superseded.
///
/// Built from the contractor's Schedule of Values (SovLine rows).
/// Mobile users sign through the
/// `PUT /api/projects/{id}/boq/payment-certs/{id}/sign` endpoint.
/// </summary>
public class PaymentCertificate : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>Sequential number within a contract.</summary>
    public int CertNumber { get; set; }

    /// <summary>Contract identifier — project number or explicit ref.</summary>
    public string ContractRef { get; set; } = "";

    /// <summary>"JCT2024" | "NEC4" | "FIDIC2017Red".</summary>
    public string Form { get; set; } = "NEC4";

    /// <summary>"Draft" | "Issued" | "Disputed" | "Agreed" | "Paid" | "Superseded".</summary>
    public string Status { get; set; } = "Draft";

    public DateTime ValuationDate { get; set; } = DateTime.UtcNow;
    public DateTime? IssuedDate { get; set; }

    public string Currency { get; set; } = "GBP";

    public string ContractorName { get; set; } = "";
    public string EmployerName { get; set; } = "";
    public string ProjectName { get; set; } = "";

    /// <summary>Retention % at this cert (auto-halves once threshold hit).</summary>
    public decimal RetentionPercent { get; set; } = 3.0m;
    public decimal HalfRetentionAtPercent { get; set; } = 100.0m;
    public decimal EffectiveRetentionPercent { get; set; } = 3.0m;

    public decimal GrossValuation { get; set; }
    public decimal RetentionAmount { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal NetThisCert { get; set; }
    public decimal VatPercent { get; set; } = 20.0m;
    public decimal VatAmount { get; set; }
    public decimal TotalPayable { get; set; }

    public int? SupersededByCertNumber { get; set; }

    public string? SignedByContractor { get; set; }
    public DateTime? ContractorSignedDate { get; set; }
    public string? SignedByEmployer { get; set; }
    public DateTime? EmployerSignedDate { get; set; }

    /// <summary>Rationale captured at signing (especially on dispute).</summary>
    public string? Note { get; set; }

    /// <summary>Snapshot of the SOV lines as JSON. Source of truth for the cert.</summary>
    public string? SovJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }

    public Project? Project { get; set; }
}
