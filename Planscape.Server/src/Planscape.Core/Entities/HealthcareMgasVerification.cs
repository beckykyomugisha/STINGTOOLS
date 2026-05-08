namespace Planscape.Core.Entities;

/// <summary>
/// Healthcare Pack H-22 — NFPA 99 §5.1.12 verification record persisted
/// via /api/projects/{id}/healthcare/mgas-verification.
/// </summary>
public class HealthcareMgasVerification : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Zone { get; set; } = "";
    public string GasCode { get; set; } = "";
    public string VerifierName { get; set; } = "";
    public string VerifierAsse6030Id { get; set; } = "";
    public string CertReference { get; set; } = "";
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public bool OverallPass { get; set; }
    public int PassCount { get; set; }
    public int FailCount { get; set; }
    /// <summary>JSON map step name → bool. Plugin and mobile post the
    /// 12 NFPA 99 §5.1.12 step results.</summary>
    public string CheckResultsJson { get; set; } = "{}";
    public string Notes { get; set; } = "";
}
