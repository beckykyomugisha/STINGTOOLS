using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Hangfire recurring job — runs nightly at 02:00 UTC per tenant/project.
/// Computes one <see cref="KpiSnapshot"/> row so the executive dashboard
/// can avoid fan-out queries on every page load.
/// </summary>
public class KpiSnapshotJob
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<KpiSnapshotJob> _log;

    public KpiSnapshotJob(PlanscapeDbContext db, ILogger<KpiSnapshotJob> log)
    {
        _db = db;
        _log = log;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var today   = DateTime.UtcNow.Date;
        var weekAgo = DateTime.UtcNow.AddDays(-7);

        var projects = await _db.Projects
            .Select(p => new { p.Id, p.TenantId })
            .ToListAsync(ct);

        _log.LogInformation("KpiSnapshotJob: computing {Count} project snapshots for {Date}",
            projects.Count, today);

        // Save per-project so a failure on one doesn't discard others already computed.
        foreach (var p in projects)
        {
            try
            {
                await ComputeSnapshotAsync(p.TenantId, p.Id, today, weekAgo, ct);
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "KpiSnapshotJob failed for project {ProjectId}", p.Id);
                _db.ChangeTracker.Clear(); // discard any partial state for this project
            }
        }
    }

    private async Task ComputeSnapshotAsync(Guid tenantId, Guid projectId,
        DateTime today, DateTime weekAgo, CancellationToken ct)
    {
        // Skip if already computed today
        if (await _db.KpiSnapshots.AnyAsync(
                s => s.ProjectId == projectId && s.SnapshotDate == today, ct))
            return;

        // Filter by TenantId directly (avoid navigation-property join that may lazy-load).
        var issuesOpen    = await _db.Issues.CountAsync(
            i => i.ProjectId == projectId && i.TenantId == tenantId
              && i.Status != "CLOSED", ct);
        var issuesOverdue = await _db.Issues.CountAsync(
            i => i.ProjectId == projectId && i.TenantId == tenantId
              && i.Status != "CLOSED" && i.DueDate.HasValue && i.DueDate < DateTime.UtcNow, ct);
        var issuesCreated = await _db.Issues.CountAsync(
            i => i.ProjectId == projectId && i.TenantId == tenantId
              && i.CreatedAt >= weekAgo, ct);

        // ClashRecord.Status is the ClashStatus enum (New, Acknowledged,
        // Resolved, Closed, Dismissed). "Open" semantically means
        // New + Acknowledged — anything not yet triaged or already closed.
        var clashesOpen     = await _db.ClashRecords.CountAsync(
            c => c.ProjectId == projectId && c.TenantId == tenantId
              && (c.Status == ClashStatus.New || c.Status == ClashStatus.Acknowledged), ct);
        var clashesCritical = await _db.ClashRecords.CountAsync(
            c => c.ProjectId == projectId && c.TenantId == tenantId
              && (c.Status == ClashStatus.New || c.Status == ClashStatus.Acknowledged)
              && (int)c.Severity >= 2, ct);

        var docsTotal     = await _db.Documents.CountAsync(
            d => d.ProjectId == projectId && d.TenantId == tenantId, ct);
        var docsPublished = await _db.Documents.CountAsync(
            d => d.ProjectId == projectId && d.TenantId == tenantId
              && d.CdeStatus == "PUBLISHED", ct);

        var modelFindings = await _db.ModelCheckResults.CountAsync(
            r => r.ProjectId == projectId && r.TenantId == tenantId && r.Status == "Open", ct);
        var modelCritical = await _db.ModelCheckResults.CountAsync(
            r => r.ProjectId == projectId && r.TenantId == tenantId
              && r.Status == "Open" && r.Severity == "Critical", ct);

        var snap = new KpiSnapshot
        {
            TenantId                  = tenantId,
            ProjectId                 = projectId,
            SnapshotDate              = today,
            IssuesOpen                = issuesOpen,
            IssuesOverdue             = issuesOverdue,
            IssuesCreatedThisWeek     = issuesCreated,
            ClashesOpen               = clashesOpen,
            ClashesCritical           = clashesCritical,
            DocumentsTotal            = docsTotal,
            DocumentsPublished        = docsPublished,
            ModelCheckFindingsOpen    = modelFindings,
            ModelCheckFindingsCritical = modelCritical,
        };

        _db.KpiSnapshots.Add(snap);
    }
}
