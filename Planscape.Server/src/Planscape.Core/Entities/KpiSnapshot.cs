namespace Planscape.Core.Entities;

/// <summary>
/// Daily executive-dashboard snapshot — one row per
/// (project, snapshot date) holding the canonical KPIs for that day.
/// Written by a Hangfire recurring job at 02:00 UTC.
///
/// Pre-computed rather than computed on demand because the executive
/// dashboard does cross-tenant rollup queries (board view: "all
/// projects across all subsidiaries") and we don't want every page
/// load to fan out to N project tables. Also lets Power BI Direct Query
/// hit a single denormalised table.
/// </summary>
public class KpiSnapshot : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }

    /// <summary>UTC date this snapshot represents (no time component).</summary>
    public DateTime SnapshotDate { get; set; }

    // ── Issue metrics ──
    public int IssuesOpen { get; set; }
    public int IssuesOverdue { get; set; }
    public int IssuesCreatedThisWeek { get; set; }
    public int IssuesResolvedThisWeek { get; set; }
    public double IssueAgeAvgDays { get; set; }
    public double IssueSlaCompliancePct { get; set; }

    // ── Clash metrics ──
    public int ClashesOpen { get; set; }
    public int ClashesCritical { get; set; }
    public int ClashesResolvedThisWeek { get; set; }

    // ── Model-check metrics ──
    public int ModelCheckFindingsOpen { get; set; }
    public int ModelCheckFindingsCritical { get; set; }
    public DateTime? LastModelCheckAt { get; set; }

    // ── Document metrics ──
    public int DocumentsTotal { get; set; }
    public int DocumentsWip { get; set; }
    public int DocumentsShared { get; set; }
    public int DocumentsPublished { get; set; }
    public int DocumentsOverdueReview { get; set; }
    public double DocumentApprovalAvgHours { get; set; }

    // ── Compliance metrics ──
    public double TagCompliancePct { get; set; }
    public double WarningsPct { get; set; }

    // ── BOQ metrics ──
    public decimal? BoqTotalValue { get; set; }
    public decimal? BoqCommittedValue { get; set; }
    public decimal? BoqActualValue { get; set; }
    public decimal? BoqForecastValue { get; set; }
    public decimal? VariationsNetValue { get; set; }
    public int VariationsCount { get; set; }

    // ── Programme metrics ──
    public double ProgrammeProgressPct { get; set; }
    public int ProgrammeMilestonesDue { get; set; }
    public int ProgrammeMilestonesAtRisk { get; set; }

    // ── Carbon metrics ──
    public double? EmbodiedCarbonKgCo2e { get; set; }
    public double? EmbodiedCarbonPerM2 { get; set; }

    public string Currency { get; set; } = "GBP";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Per-coordinator workload snapshot — captured weekly so the team-lead
/// dashboard can show "who's underwater" and rebalance assignments.
///
/// A heatmap (user × week) is the typical visualisation: red where a
/// coordinator has more open critical issues than they can reasonably
/// close, green where they have spare capacity.
/// </summary>
public class CoordinatorWorkload : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>ISO week start (Monday) UTC for this snapshot.</summary>
    public DateTime WeekStarting { get; set; }

    public int OpenIssuesAssigned { get; set; }
    public int OpenIssuesCritical { get; set; }
    public int OpenIssuesMajor { get; set; }
    public int OpenIssuesOverdue { get; set; }

    public int IssuesResolvedThisWeek { get; set; }
    public int IssuesCreatedThisWeek { get; set; }

    public int OpenClashesAssigned { get; set; }
    public int OpenModelCheckFindings { get; set; }

    public int PendingApprovalsCount { get; set; }

    /// <summary>Total open work units (issues + clashes + findings + approvals).</summary>
    public int WorkloadIndex { get; set; }

    /// <summary>"Light" / "Balanced" / "Heavy" / "Overloaded" based on WorkloadIndex bands.</summary>
    public string LoadBand { get; set; } = "Balanced";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AppUser? User { get; set; }
}

/// <summary>
/// A user- or role-pinned dashboard widget configuration. The frontend
/// renders the dashboard by reading these rows in display order. Lets
/// each coordinator pin the metrics that matter to them (clash burn-down,
/// document approval funnel, issue ageing, BOQ variance).
/// </summary>
public class DashboardWidget : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Null for tenant-wide widgets, set for per-user pinning.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Null for cross-project, set for single-project widget.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>
    /// Widget kind — drives renderer:
    /// IssueAgeing / IssueBurnDown / IssueByDiscipline / ClashHeatmap /
    /// ClashByLevel / ModelCheckSummary / DocumentFunnel /
    /// ApprovalQueue / BoqVariance / CarbonByDiscipline /
    /// CoordinatorWorkload / ProgrammeProgress / KpiCard.
    /// </summary>
    public string Kind { get; set; } = "KpiCard";

    public string Title { get; set; } = "";

    /// <summary>JSON configuration blob — schema per Kind.</summary>
    public string ConfigJson { get; set; } = "{}";

    public int SortOrder { get; set; } = 0;

    /// <summary>Grid coordinates for the layout (col, row, w, h).</summary>
    public int? GridCol { get; set; }
    public int? GridRow { get; set; }
    public int? GridWidth { get; set; }
    public int? GridHeight { get; set; }

    public bool Pinned { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
