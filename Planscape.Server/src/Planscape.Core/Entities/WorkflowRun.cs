namespace Planscape.Core.Entities;

/// <summary>
/// Record of a workflow preset execution (DailyQA, MorningHealthCheck, etc.).
/// </summary>
public class WorkflowRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string PresetName { get; set; } = "";
    public string UserName { get; set; } = "";
    public int StepsPassed { get; set; }
    public int StepsFailed { get; set; }
    public int StepsSkipped { get; set; }
    public double DurationMs { get; set; }
    public double ComplianceBefore { get; set; }
    public double ComplianceAfter { get; set; }
    public string? StepResultsJson { get; set; } // per-step detail
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Project? Project { get; set; }
}
