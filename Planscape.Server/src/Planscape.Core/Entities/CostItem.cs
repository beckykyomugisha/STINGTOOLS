namespace Planscape.Core.Entities;

/// <summary>
/// P5 — Cost-item aggregation for 5D reporting. The plugin's
/// <c>cost_rates_5d.csv</c> already gives us per-discipline rates; this
/// entity is the server-side target so the web dashboard and mobile home
/// screen can roll up spend by stage / discipline / trade.
///
/// Rows are typically imported from BOQ exports (one per take-off line) or
/// generated from tagged element quantities. Currency is stored as ISO 4217
/// (GBP / USD / EUR / UGX).
/// </summary>
public class CostItem : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    public string Code { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>STING discipline code (M/E/P/A/S/FP/LV/G) — groups costs on the dashboard.</summary>
    public string? Discipline { get; set; }

    /// <summary>CSI / NRM / custom trade bucket. Free-text so clients can use their own classification.</summary>
    public string? TradeBucket { get; set; }

    public Guid? ScheduleTaskId { get; set; }

    /// <summary>E.g. m, m², m³, ea, t, set. Free text to accommodate BOQ imports.</summary>
    public string Unit { get; set; } = "ea";

    public double Quantity { get; set; }
    public decimal UnitRate { get; set; }

    /// <summary>Quantity × UnitRate, stored so reports don't re-compute.</summary>
    public decimal LineTotal { get; set; }

    public string Currency { get; set; } = "GBP";

    /// <summary>Budget / Committed / Actual — drives variance reports.</summary>
    public CostKind Kind { get; set; } = CostKind.Budget;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Project? Project { get; set; }
    public ScheduleTask? ScheduleTask { get; set; }
}

public enum CostKind
{
    Budget    = 0,
    Committed = 1,
    Actual    = 2,
    Forecast  = 3,
}
