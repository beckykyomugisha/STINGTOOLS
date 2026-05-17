namespace Planscape.Core.Entities;

/// <summary>
/// Record of a workflow preset execution (DailyQA, MorningHealthCheck, etc.).
/// </summary>
public class WorkflowRun : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
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

    /// <summary>
    /// JSON blob linking this run to affected entities so the audit trail
    /// can correlate workflow runs with the documents/issues/transmittals they touched.
    /// Shape: { "documentIds": [...], "issueIds": [...], "transmittalIds": [...] }
    /// </summary>
    public string? LinkedEntityJson { get; set; }

    // Navigation
    public Project? Project { get; set; }
}
