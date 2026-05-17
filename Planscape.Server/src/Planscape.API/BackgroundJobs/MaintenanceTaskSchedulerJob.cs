using Microsoft.EntityFrameworkCore;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.MIM.Entities;

namespace Planscape.API.BackgroundJobs;

/// <summary>
/// Phase 178c (T3-22) — Maintenance task runtime scheduler.
///
/// <para>Runs daily. For each <see cref="MaintenanceTask"/>:</para>
/// <list type="bullet">
///   <item>If <c>NextDueDate</c> is in the next 7 days → push notification
///         to the asset's owner role (or project FM team).</item>
///   <item>If overdue → escalate (notify project PM + log audit).</item>
///   <item>If <c>Status == "COMPLETED"</c> and <c>FrequencyDays &gt; 0</c>
///         and the next occurrence row hasn't been minted yet, auto-create
///         the next occurrence row (CompletedDate + FrequencyDays).</item>
/// </list>
///
/// <para>The job is idempotent — re-running it the same day produces the
/// same notifications + the same set of next-occurrence rows. It skips
/// tenants without the MIM addon enabled.</para>
/// </summary>
public class MaintenanceTaskSchedulerJob
{
    private readonly PlanscapeDbContext _db;
    private readonly IPushNotificationService _push;
    private readonly IAuditService _audit;
    private readonly ILogger<MaintenanceTaskSchedulerJob> _log;

    public MaintenanceTaskSchedulerJob(
        PlanscapeDbContext db,
        IPushNotificationService push,
        IAuditService audit,
        ILogger<MaintenanceTaskSchedulerJob> log)
    {
        _db = db; _push = push; _audit = audit; _log = log;
    }

    public async Task ExecuteAsync()
    {
        var now = DateTime.UtcNow;
        var soon = now.AddDays(7);

        // Find all MIM-enabled tenants once.
        var mimTenantIds = await _db.Tenants
            .Where(t => t.MimEnabled)
            .Select(t => t.Id)
            .ToListAsync();
        if (mimTenantIds.Count == 0)
        {
            _log.LogInformation("MaintenanceTaskSchedulerJob: no MIM-enabled tenants, skipping");
            return;
        }

        var due = await _db.MaintenanceTasks
            .Include(m => m.Asset)
            .Where(m => m.Asset != null
                     && m.Status != "COMPLETED"
                     && m.NextDueDate.HasValue
                     && m.NextDueDate <= soon)
            .ToListAsync();

        int upcomingNotified = 0;
        int overdueEscalated = 0;

        // Build a lookup of project → tenant + project members so we can
        // resolve recipients in one go.
        var projectIds = due.Select(t => t.Asset!.ProjectId).Distinct().ToList();
        var projects = await _db.Projects
            .Where(p => projectIds.Contains(p.Id) && mimTenantIds.Contains(p.TenantId))
            .ToDictionaryAsync(p => p.Id);

        // Members per project. We pick FM-tagged members first; fall back to
        // every member with a Manager / Owner project role for PM escalation.
        var memberRows = await _db.ProjectMembers
            .Where(m => projectIds.Contains(m.ProjectId))
            .Select(m => new { m.ProjectId, m.UserId, Role = m.ProjectRole, m.Iso19650Role })
            .ToListAsync();
        var membersByProject = memberRows
            .GroupBy(m => m.ProjectId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var task in due)
        {
            if (task.Asset == null) continue;
            if (!projects.TryGetValue(task.Asset.ProjectId, out var project)) continue;
            if (!membersByProject.TryGetValue(project.Id, out var members)) continue;

            var isOverdue = task.NextDueDate < now;
            var fmTeam = members
                .Where(m => string.Equals(m.Iso19650Role, "FM", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(m.Role, "Coordinator", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(m.Role, "Manager", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(m.Role, "Owner", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.UserId)
                .Distinct()
                .ToList();

            // Overdue → escalate to PMs (Owner/Manager) + audit.
            if (isOverdue)
            {
                var pms = members
                    .Where(m => string.Equals(m.Role, "Manager", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(m.Role, "Owner", StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.UserId)
                    .Distinct()
                    .ToList();
                var audience = pms.Count > 0 ? pms : fmTeam;
                if (audience.Count > 0)
                {
                    await FanOutAsync(audience, new PushPayload
                    {
                        Title = $"⚠ Overdue maintenance: {task.Asset.AssetTag}",
                        Body  = task.Title.Length > 0 ? task.Title : (task.Description ?? "Maintenance task overdue"),
                        Channel = "maintenance",
                        Data = new Dictionary<string, string>
                        {
                            ["type"] = "maintenance_overdue",
                            ["taskId"] = task.Id.ToString(),
                            ["assetId"] = task.AssetId.ToString(),
                            ["projectId"] = project.Id.ToString(),
                            ["isStatutory"] = task.IsStatutory.ToString().ToLowerInvariant()
                        }
                    });
                    overdueEscalated++;
                    try
                    {
                        await _audit.LogAsync("ESCALATE", "MaintenanceTask", task.Id.ToString(),
                            System.Text.Json.JsonSerializer.Serialize(new
                            {
                                taskCode = task.TaskCode,
                                projectId = project.Id,
                                isStatutory = task.IsStatutory,
                                dueDate = task.NextDueDate
                            }));
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Audit log failed for overdue maintenance task {TaskId}", task.Id);
                    }
                }
                if (task.Status != "OVERDUE")
                {
                    task.Status = "OVERDUE";
                }
            }
            else
            {
                // Upcoming (≤7 days) → notify the FM team.
                if (fmTeam.Count > 0)
                {
                    await FanOutAsync(fmTeam, new PushPayload
                    {
                        Title = $"📅 Upcoming maintenance: {task.Asset.AssetTag}",
                        Body  = $"Due {task.NextDueDate!.Value:yyyy-MM-dd} — {(task.Title.Length > 0 ? task.Title : task.Description)}",
                        Channel = "maintenance",
                        Data = new Dictionary<string, string>
                        {
                            ["type"] = "maintenance_upcoming",
                            ["taskId"] = task.Id.ToString(),
                            ["assetId"] = task.AssetId.ToString(),
                            ["projectId"] = project.Id.ToString(),
                            ["isStatutory"] = task.IsStatutory.ToString().ToLowerInvariant()
                        }
                    });
                    upcomingNotified++;
                }
            }
        }

        // ── Auto-create the next occurrence for tasks marked COMPLETED. ──
        // We look for COMPLETED tasks with a CompletedDate but no successor
        // row yet (a successor is detected by AssetId + TaskCode +
        // NextDueDate > CompletedDate). For each, mint the next row.
        int nextOccurrencesCreated = 0;
        var completed = await _db.MaintenanceTasks
            .Where(m => m.Status == "COMPLETED"
                     && m.CompletedDate.HasValue
                     && m.FrequencyDays > 0)
            .ToListAsync();
        foreach (var done in completed)
        {
            var nextDue = done.CompletedDate!.Value.AddDays(done.FrequencyDays);
            bool hasSuccessor = await _db.MaintenanceTasks.AnyAsync(m =>
                m.AssetId == done.AssetId
                && m.TaskCode == done.TaskCode
                && m.Id != done.Id
                && m.NextDueDate.HasValue
                && m.NextDueDate > done.CompletedDate);
            if (hasSuccessor) continue;

            _db.MaintenanceTasks.Add(new MaintenanceTask
            {
                AssetId           = done.AssetId,
                TaskCode          = done.TaskCode,
                Title             = done.Title,
                Description       = done.Description,
                Type              = done.Type,
                Priority          = done.Priority,
                Status            = "SCHEDULED",
                AssignedTo        = done.AssignedTo,
                FrequencyDays     = done.FrequencyDays,
                ScheduledDate     = nextDue,
                NextDueDate       = nextDue,
                StandardReference = done.StandardReference,
                IsStatutory       = done.IsStatutory,
                RegulatoryBody    = done.RegulatoryBody,
                EstimatedCost     = done.EstimatedCost,
                EstimatedHours    = done.EstimatedHours,
            });
            nextOccurrencesCreated++;
        }

        await _db.SaveChangesAsync();
        _log.LogInformation(
            "MaintenanceTaskSchedulerJob: upcoming={Up} overdue={Over} nextOccurrences={Next}",
            upcomingNotified, overdueEscalated, nextOccurrencesCreated);
    }

    private async Task FanOutAsync(IEnumerable<Guid> userIds, PushPayload payload)
    {
        foreach (var uid in userIds)
        {
            try { await _push.SendToUserAsync(uid, payload); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Maintenance push failed for user {UserId}", uid);
            }
        }
    }
}
