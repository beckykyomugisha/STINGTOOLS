using Microsoft.EntityFrameworkCore;
using StingBIM.Infrastructure.Data;
using StingBIM.Core.Entities;

namespace StingBIM.API.BackgroundJobs;

/// <summary>
/// Automation gap fix: Runs every 2 hours.
/// For each project with warning health score &lt; 60, auto-creates a BIM issue
/// if no issue of type "NCR" for warnings already exists in the last 7 days.
/// This ensures critical warning spikes are never silently ignored.
/// </summary>
public class WarningEscalationJob
{
    private readonly StingBimDbContext _db;
    private readonly ILogger<WarningEscalationJob> _log;

    public WarningEscalationJob(StingBimDbContext db, ILogger<WarningEscalationJob> log)
    {
        _db = db; _log = log;
    }

    public async Task ExecuteAsync()
    {
        var cutoff7d = DateTime.UtcNow.AddDays(-7);

        // Find recent compliance snapshots with low warning health
        var snapshots = await _db.ComplianceSnapshots
            .Include(s => s.Project)
            .Where(s => s.WarningHealthScore > 0
                     && s.WarningHealthScore < 60
                     && s.CapturedAt >= DateTime.UtcNow.AddHours(-3)) // recent snapshot
            .GroupBy(s => s.ProjectId)
            .Select(g => g.OrderByDescending(s => s.CapturedAt).First())
            .ToListAsync();

        int created = 0;
        foreach (var snap in snapshots)
        {
            // Skip if a system warning issue was already raised in last 7 days
            bool hasRecent = await _db.Issues.AnyAsync(i =>
                i.ProjectId == snap.ProjectId
                && i.Type == "NCR"
                && i.CreatedBy == "system"
                && i.CreatedAt >= cutoff7d
                && i.Title.Contains("Warning health"));

            if (hasRecent) continue;

            // Auto-create issue
            var lastNcr = await _db.Issues
                .Where(i => i.ProjectId == snap.ProjectId && i.Type == "NCR")
                .OrderByDescending(i => i.IssueCode)
                .FirstOrDefaultAsync();

            int nextNum = 1;
            if (lastNcr != null)
            {
                var parts = lastNcr.IssueCode.Split('-');
                if (parts.Length == 2 && int.TryParse(parts[1], out int n)) nextNum = n + 1;
            }

            string priority = snap.WarningHealthScore < 30 ? "CRITICAL" : "HIGH";
            var issue = new BimIssue
            {
                ProjectId   = snap.ProjectId,
                IssueCode   = $"NCR-{nextNum:D4}",
                Type        = "NCR",
                Title       = $"Warning health degraded: {snap.WarningHealthScore}/100 ({snap.WarningCount} warnings)",
                Description = $"Automated alert: Model warning health score dropped to {snap.WarningHealthScore}/100 " +
                              $"with {snap.WarningCount} active warnings. Compliance: {snap.TagPercent:F1}% ({snap.RagStatus}). " +
                              "Review and resolve model warnings in Revit.",
                Priority    = priority,
                Status      = "OPEN",
                CreatedBy   = "system",
                DueDate     = DateTime.UtcNow.AddHours(priority == "CRITICAL" ? 4 : 24)
            };

            _db.Issues.Add(issue);
            created++;
            _log.LogInformation("WarningEscalationJob: auto-created {Code} for project {Id} (health={Score})",
                issue.IssueCode, snap.ProjectId, snap.WarningHealthScore);
        }

        await _db.SaveChangesAsync();
        _log.LogInformation("WarningEscalationJob: {Count} issues auto-created", created);
    }
}
