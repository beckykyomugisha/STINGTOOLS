using Microsoft.EntityFrameworkCore;
using Planscape.API.Services;
using Planscape.Infrastructure.Data;

namespace Planscape.API.BackgroundJobs;

/// <summary>
/// Runs every hour. Finds issues that have breached their SLA deadline and:
/// 1. Sends an email alert to the assignee
/// 2. Escalates priority if MEDIUM → HIGH after >48h breach
/// </summary>
public class SlaViolationJob
{
    private readonly PlanscapeDbContext _db;
    private readonly IEmailService _email;
    private readonly ILogger<SlaViolationJob> _log;
    private readonly IConfiguration _config;

    public SlaViolationJob(PlanscapeDbContext db, IEmailService email,
        ILogger<SlaViolationJob> log, IConfiguration config)
    {
        _db = db; _email = email; _log = log; _config = config;
    }

    public async Task ExecuteAsync()
    {
        string serverUrl = _config["App:ServerUrl"] ?? "https://planscape-api.onrender.com";
        var now = DateTime.UtcNow;

        var overdueIssues = await _db.Issues
            .Include(i => i.Project)
            .Where(i => i.DueDate.HasValue
                     && i.DueDate < now
                     && i.Status != "CLOSED"
                     && i.Status != "RESOLVED"
                     && !string.IsNullOrEmpty(i.Assignee))
            .ToListAsync();

        int alerted = 0, escalated = 0;
        foreach (var issue in overdueIssues)
        {
            int hoursOverdue = (int)(now - issue.DueDate!.Value).TotalHours;

            // Send alert every 4h for CRITICAL, 24h for HIGH, 48h for MEDIUM
            int alertIntervalHours = issue.Priority switch
            {
                "CRITICAL" => 4,
                "HIGH"     => 24,
                _          => 48
            };

            // Auto-escalate MEDIUM → HIGH after 48h breach
            if (issue.Priority == "MEDIUM" && hoursOverdue >= 48)
            {
                issue.Priority = "HIGH";
                escalated++;
                _log.LogInformation("SlaViolationJob: escalated {Code} MEDIUM→HIGH ({Hours}h overdue)", issue.IssueCode, hoursOverdue);
            }

            // Find assignee user
            var assigneeUser = await _db.Users
                .Where(u => u.Email == issue.Assignee && u.IsActive)
                .FirstOrDefaultAsync();

            if (assigneeUser != null && hoursOverdue % alertIntervalHours == 0)
            {
                await _email.SendSlaAlertAsync(
                    assigneeUser.Email,
                    assigneeUser.DisplayName,
                    issue.IssueCode,
                    issue.Title,
                    issue.Priority,
                    hoursOverdue);
                alerted++;
            }
        }

        await _db.SaveChangesAsync();
        _log.LogInformation("SlaViolationJob: {Alerted} alerts sent, {Escalated} escalated", alerted, escalated);
    }
}
