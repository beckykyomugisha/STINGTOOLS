namespace Planscape.Core.Entities;

/// <summary>
/// Server-managed SEQ counter for multi-user sequence number coordination.
/// Uses max-per-key merge strategy to prevent duplicate sequence numbers.
/// </summary>
public class SeqCounter : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string CounterKey { get; set; } = ""; // e.g., "M_HVAC_SUP_AHU"
    public int CurrentValue { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Project? Project { get; set; }
}
