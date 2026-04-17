namespace Planscape.Core.Entities;

/// <summary>
/// P4 — Lightweight schedule task for 4D integration.
///
/// Lets us tie compliance milestones to RIBA stages + Gantt bars without
/// importing a full MS Project dependency graph. Predecessors are stored as
/// a semicolon-delimited list of other ScheduleTask Ids; the plugin's 4D
/// engine imports MS Project XML into this model.
/// </summary>
public class ScheduleTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }

    /// <summary>Human-readable WBS code (e.g. "2.4.1" or "M-HVAC-01").</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>RIBA Plan of Work 2020 stage (0-7). 0 = pre-stage 0, 8+ = post-handover.</summary>
    public int? RibaStage { get; set; }

    /// <summary>Discipline filter — matches STING DISC codes (M/E/P/A/S/FP/LV/G).</summary>
    public string? Discipline { get; set; }

    public DateTime? PlannedStart { get; set; }
    public DateTime? PlannedFinish { get; set; }
    public DateTime? ActualStart { get; set; }
    public DateTime? ActualFinish { get; set; }
    public DateTime? BaselineStart { get; set; }
    public DateTime? BaselineFinish { get; set; }

    /// <summary>Percent-complete 0-100. Live-updated from compliance when linked.</summary>
    public double PercentComplete { get; set; }

    /// <summary>Semicolon-delimited predecessor task IDs.</summary>
    public string? PredecessorIds { get; set; }

    /// <summary>Milestone tasks render as a diamond on the Gantt; zero-duration.</summary>
    public bool IsMilestone { get; set; }

    /// <summary>Optional link to the compliance metric that drives PercentComplete
    /// (e.g. "discipline.M.tagCompletionPct"). When present, a Hangfire job can
    /// update PercentComplete from the latest ComplianceSnapshot.</summary>
    public string? LinkedMetric { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Project? Project { get; set; }
}
