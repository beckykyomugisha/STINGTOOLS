namespace Planscape.Core.Entities;

/// <summary>
/// Phase 180 — Multi-step approval signoffs for photos governed by a
/// <see cref="PhotoPolicy.ApprovalChain"/> of <c>TwoStepSafety</c> or
/// <c>TwoStepAll</c>.
///
/// One row per (photoId, stage) — the photo flips to Audience=Approved
/// only when every required stage has a row. Stage names are
/// policy-specific; the default chain is just "Site" then "HSEQ".
/// Single-step chains never write to this table.
/// </summary>
public class PhotoApprovalSignoff
{
    public Guid Id      { get; set; } = Guid.NewGuid();
    public Guid PhotoId { get; set; }

    /// <summary>"Site" | "HSEQ" | custom — defined by the project policy.</summary>
    public string Stage { get; set; } = "Site";

    public DateTime SignedAt        { get; set; } = DateTime.UtcNow;
    public Guid?    SignedByUserId  { get; set; }
    public string?  Caption         { get; set; }
    public string?  Notes           { get; set; }

    public SitePhoto? Photo { get; set; }
    public AppUser?   SignedByUser { get; set; }
}
