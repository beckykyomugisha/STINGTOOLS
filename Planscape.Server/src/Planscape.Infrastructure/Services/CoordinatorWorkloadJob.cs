using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Hangfire recurring job — runs weekly (Monday 03:00 UTC).
/// Computes one <see cref="CoordinatorWorkload"/> row per BIM
/// coordinator so the team-lead heatmap stays fresh.
/// </summary>
public class CoordinatorWorkloadJob
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<CoordinatorWorkloadJob> _log;

    public CoordinatorWorkloadJob(PlanscapeDbContext db, ILogger<CoordinatorWorkloadJob> log)
    {
        _db = db;
        _log = log;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now       = DateTime.UtcNow;
        var monday    = now.Date.AddDays(-(int)now.DayOfWeek + 1);
        var weekAgo   = monday.AddDays(-7);
        var tomorrow  = now.AddDays(1);

        var coordinators = await _db.ProjectMembers
            .Where(m => m.Role == "BimManager" || m.Role == "Coordinator")
            .Select(m => new { m.UserId, m.TenantId })
            .Distinct()
            .ToListAsync(ct);

        _log.LogInformation("CoordinatorWorkloadJob: {Count} coordinators for week {Monday}",
            coordinators.Count, monday);

        foreach (var c in coordinators)
        {
            if (await _db.CoordinatorWorkloads.AnyAsync(
                    w => w.UserId == c.UserId && w.WeekStarting == monday, ct))
                continue;

            var openIssues  = await _db.Issues.CountAsync(
                i => i.AssigneeUserId == c.UserId && i.Status != "Closed", ct);
            var critical    = await _db.Issues.CountAsync(
                i => i.AssigneeUserId == c.UserId && i.Status != "Closed"
                  && i.Priority == "CRITICAL", ct);
            var major       = await _db.Issues.CountAsync(
                i => i.AssigneeUserId == c.UserId && i.Status != "Closed"
                  && i.Priority == "HIGH", ct);
            var overdue     = await _db.Issues.CountAsync(
                i => i.AssigneeUserId == c.UserId && i.Status != "Closed"
                  && i.DueDate.HasValue && i.DueDate < now, ct);
            var resolved    = await _db.Issues.CountAsync(
                i => i.AssigneeUserId == c.UserId
                  && i.UpdatedAt >= weekAgo && i.Status == "CLOSED", ct);
            var created     = await _db.Issues.CountAsync(
                i => i.AssigneeUserId == c.UserId && i.CreatedAt >= weekAgo, ct);
            var findings    = await _db.ModelCheckResults.CountAsync(
                r => r.Status == "Open", ct); // simplified — no per-user assignment on results
            var approvals   = await _db.DocumentApprovals.CountAsync(
                a => a.RequestedBy == c.UserId.ToString() && a.Status == "PENDING", ct);

            var workloadIndex = openIssues + critical * 3 + overdue * 2 + findings + approvals;
            var loadBand = workloadIndex switch
            {
                <= 5  => "Light",
                <= 15 => "Balanced",
                <= 30 => "Heavy",
                _     => "Overloaded",
            };

            _db.CoordinatorWorkloads.Add(new CoordinatorWorkload
            {
                TenantId              = c.TenantId,
                UserId                = c.UserId,
                WeekStarting          = monday,
                OpenIssuesAssigned    = openIssues,
                OpenIssuesCritical    = critical,
                OpenIssuesMajor       = major,
                OpenIssuesOverdue     = overdue,
                IssuesResolvedThisWeek = resolved,
                IssuesCreatedThisWeek  = created,
                OpenModelCheckFindings = findings,
                PendingApprovalsCount  = approvals,
                WorkloadIndex         = workloadIndex,
                LoadBand              = loadBand,
            });
        }

        await _db.SaveChangesAsync(ct);
    }
}
