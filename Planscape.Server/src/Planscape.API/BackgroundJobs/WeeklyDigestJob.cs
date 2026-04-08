using Microsoft.EntityFrameworkCore;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.BackgroundJobs;

/// <summary>
/// Runs every Monday at 08:00 UTC. Sends a compliance digest email to all
/// BIM Managers and above (Coordinator+) for each active project.
/// </summary>
public class WeeklyDigestJob
{
    private readonly PlanscapeDbContext _db;
    private readonly IEmailService _email;
    private readonly ILogger<WeeklyDigestJob> _log;

    public WeeklyDigestJob(PlanscapeDbContext db, IEmailService email, ILogger<WeeklyDigestJob> log)
    {
        _db = db; _email = email; _log = log;
    }

    public async Task ExecuteAsync()
    {
        var projects = await _db.Projects
            .Include(p => p.Tenant)
            .Where(p => p.Status == ProjectStatus.Active)
            .ToListAsync();

        int sent = 0;
        foreach (var project in projects)
        {
            int openIssues = await _db.Issues
                .CountAsync(i => i.ProjectId == project.Id && i.Status != "CLOSED" && i.Status != "RESOLVED");

            int overdueActions = await _db.MeetingActionItems
                .CountAsync(a => a.Meeting!.ProjectId == project.Id
                              && a.Status != "COMPLETE"
                              && a.DueDate.HasValue
                              && a.DueDate < DateTime.UtcNow);

            // Send to all coordinators and above who are members of this project
            var managers = await _db.ProjectMembers
                .Include(m => m.User)
                .Where(m => m.ProjectId == project.Id
                         && m.IsActive
                         && m.User != null
                         && (m.ProjectRole == "Manager" || m.ProjectRole == "Admin"
                             || m.ProjectRole == "Coordinator" || m.ProjectRole == "Owner"))
                .Select(m => m.User!)
                .ToListAsync();

            foreach (var user in managers.Where(u => u.IsActive))
            {
                await _email.SendComplianceDigestAsync(
                    user.Email, user.DisplayName,
                    project.Name,
                    project.CompliancePercent,
                    project.RagStatus ?? "RED",
                    openIssues, overdueActions);
                sent++;
            }
        }

        _log.LogInformation("WeeklyDigestJob: {Count} digest emails sent", sent);
    }
}
