namespace StingBIM.Core.Entities;

/// <summary>
/// Point-in-time compliance snapshot for trend tracking and RIBA stage gate audits.
/// </summary>
public class ComplianceSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string CapturedBy { get; set; } = "";
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    // Core metrics
    public int TotalElements { get; set; }
    public int TaggedComplete { get; set; }
    public int TaggedIncomplete { get; set; }
    public int Untagged { get; set; }
    public int FullyResolved { get; set; }
    public int StaleCount { get; set; }
    public int PlaceholderCount { get; set; }
    public int WarningCount { get; set; }
    public int WarningHealthScore { get; set; }

    // Percentages
    public double TagPercent { get; set; }
    public double StrictPercent { get; set; }
    public double ContainerPercent { get; set; }
    public string RagStatus { get; set; } = "RED";

    // Breakdown (stored as JSON)
    public string? ByDisciplineJson { get; set; }
    public string? ByPhaseJson { get; set; }
    public string? EmptyTokenCountsJson { get; set; }

    // Navigation
    public Project? Project { get; set; }
}
