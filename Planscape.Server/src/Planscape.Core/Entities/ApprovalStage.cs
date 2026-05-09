namespace Planscape.Core.Entities;

/// <summary>
/// Phase 178c (T3-12) — One stage of an <see cref="ApprovalChain"/>.
///
/// <para>
/// <b>Mode = "PARALLEL"</b>: every listed approver must approve in any
/// order. The stage completes once every approver has decided.
/// </para>
/// <para>
/// <b>Mode = "SEQUENTIAL"</b>: the listed approvers act in the declared
/// order; an approver further down the list cannot decide until everyone
/// before them has approved.
/// </para>
/// <para>
/// A REJECT decision at any stage rejects the whole chain and the
/// document does not transition. Decisions are recorded as a JSON array
/// in <see cref="DecisionsJson"/> — each entry: { userId, decision,
/// reason, decidedAt }.
/// </para>
/// </summary>
public class ApprovalStage : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ChainId { get; set; }

    /// <summary>0-based execution order within the chain.</summary>
    public int Order { get; set; }

    /// <summary>"PARALLEL" or "SEQUENTIAL".</summary>
    public string Mode { get; set; } = "PARALLEL";

    /// <summary>JSON array of required approver user-ids (Guid).</summary>
    public string RequiredApproversJson { get; set; } = "[]";

    /// <summary>PENDING | APPROVED | REJECTED | SKIPPED.</summary>
    public string Status { get; set; } = "PENDING";

    /// <summary>JSON array of decision rows (see class summary).</summary>
    public string DecisionsJson { get; set; } = "[]";

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Optional stage label, e.g. "Discipline lead", "Project review".</summary>
    public string? Label { get; set; }

    // Navigation
    public ApprovalChain? Chain { get; set; }

    /// <summary>
    /// Convenience: parse the JSON array of required approver user ids.
    /// Returns an empty list on parse failure (defensive).
    /// </summary>
    public static IReadOnlyList<Guid> ParseRequiredApprovers(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<Guid>();
        try
        {
            var arr = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(raw);
            return arr ?? new List<Guid>();
        }
        catch { return Array.Empty<Guid>(); }
    }

    public static string SerializeApprovers(IEnumerable<Guid> ids)
        => System.Text.Json.JsonSerializer.Serialize(ids ?? Array.Empty<Guid>());
}
