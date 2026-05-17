namespace Planscape.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

public interface IClashAutomationService
{
    /// <summary>
    /// Process newly-detected clashes against the project's automation rules.
    /// Called by <see cref="ClashDetectionJob"/> after committing new
    /// <see cref="ClashRecord"/> rows. For each clash, evaluates every
    /// enabled rule (priority-ordered) — the first matching auto-issue rule
    /// promotes the clash to a <see cref="BimIssue"/>; every matching
    /// rule's notification + webhook actions fire.
    /// </summary>
    Task ProcessNewClashesAsync(Guid projectId, Guid tenantId, IReadOnlyList<Guid> newClashIds, CancellationToken ct);
}

/// <summary>
/// Automation glue layer between <see cref="ClashDetectionJob"/> and
/// downstream notification + webhook + auto-issue surfaces. Decouples
/// the detector (geometry math) from the side-effects (issue, push,
/// webhook) so the rules engine can evolve independently and is unit-
/// testable in isolation.
///
/// Default behaviour with zero project rules: auto-issue + push +
/// webhook for CRITICAL clashes only. This keeps fresh projects safe
/// without forcing every tenant to configure rules up-front.
/// </summary>
public sealed class ClashAutomationService : IClashAutomationService
{
    private readonly PlanscapeDbContext _db;
    private readonly INotificationService _notifications;
    private readonly OutboundWebhookDispatcher _webhooks;
    private readonly ILogger<ClashAutomationService> _logger;

    public ClashAutomationService(
        PlanscapeDbContext db,
        INotificationService notifications,
        OutboundWebhookDispatcher webhooks,
        ILogger<ClashAutomationService> logger)
    {
        _db = db;
        _notifications = notifications;
        _webhooks = webhooks;
        _logger = logger;
    }

    public async Task ProcessNewClashesAsync(Guid projectId, Guid tenantId, IReadOnlyList<Guid> newClashIds, CancellationToken ct)
    {
        if (newClashIds.Count == 0) return;

        var rules = await _db.ClashAutomationRules.AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.Enabled)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);

        // If no rules defined, apply a sensible default — auto-issue for CRITICAL clashes only.
        if (rules.Count == 0)
        {
            rules = new List<ClashAutomationRule>
            {
                new()
                {
                    Id = Guid.Empty, // synthetic
                    Name = "[default] Auto-issue critical clashes",
                    MinSeverity = ClashSeverity.Critical,
                    AutoCreateIssue = true,
                    NotifyPush = true,
                    FireWebhook = true,
                    IssuePriority = "HIGH",
                }
            };
        }

        // Load only the clashes flagged as new — we are already inside the same
        // DbContext the caller used, so these come back as tracked entities and
        // we can mutate Status / IssueId in place.
        var clashes = await _db.ClashRecords
            .Where(c => newClashIds.Contains(c.Id) && c.ProjectId == projectId)
            .ToListAsync(ct);

        int issuesCreated = 0, notificationsSent = 0, webhooksFired = 0;

        foreach (var clash in clashes)
        {
            // Auto-create issue — first matching rule wins to avoid duplicate issues.
            foreach (var rule in rules)
            {
                if (!Matches(clash, rule)) continue;

                if (rule.AutoCreateIssue && !clash.IssueId.HasValue)
                {
                    var issue = new BimIssue
                    {
                        TenantId = tenantId,
                        ProjectId = projectId,
                        Type = "CLASH",
                        Status = "OPEN",
                        Title = $"[Auto] {clash.Severity} clash: {clash.DisciplineA ?? "?"} vs {clash.DisciplineB ?? "?"}",
                        Description = $"Auto-created by rule '{rule.Name}'. Penetration: {clash.DistanceMm:N1} mm, overlap: {clash.OverlapVolumeMm3:N0} mm^3."
                                      + (clash.LevelCode != null ? $" Level: {clash.LevelCode}." : ""),
                        Priority = rule.IssuePriority ?? (clash.Severity == ClashSeverity.Critical ? "HIGH" : "MEDIUM"),
                        CreatedBy = "system:clash-automation",
                        CreatedAt = DateTime.UtcNow,
                    };
                    if (!string.IsNullOrWhiteSpace(rule.AutoAssignTo))
                    {
                        // Role keywords (BIM_COORDINATOR, DISCIPLINE_LEAD) would be resolved
                        // by a project role service. For now treat as literal email/display name —
                        // BimIssue carries both a display Assignee and an AssigneeEmail.
                        issue.Assignee = rule.AutoAssignTo;
                        if (rule.AutoAssignTo.Contains('@')) issue.AssigneeEmail = rule.AutoAssignTo;
                    }
                    _db.Issues.Add(issue);
                    clash.IssueId = issue.Id;
                    clash.Status = ClashStatus.Acknowledged;
                    clash.AcknowledgedAt = DateTime.UtcNow;
                    issuesCreated++;
                    break; // first matching auto-issue rule wins
                }
            }

            // Notifications + webhooks fire for ALL matching rules.
            foreach (var rule in rules)
            {
                if (!Matches(clash, rule)) continue;

                if (rule.NotifyPush)
                {
                    var emails = ResolveRecipients(rule.NotifyUsers, clash.AssignedTo);
                    if (emails.Count > 0)
                    {
                        // Resolve emails to userIds — INotificationService deals in userIds.
                        // Unknown emails are skipped (with a warning) rather than crashing the run.
                        var userIds = await _db.Users.AsNoTracking()
                            .Where(u => emails.Contains(u.Email))
                            .Select(u => new { u.Email, u.Id })
                            .ToListAsync(ct);

                        var unresolved = emails.Except(userIds.Select(u => u.Email)).ToList();
                        foreach (var miss in unresolved)
                            _logger.LogWarning("Clash notification: email {Email} not found on tenant {TenantId}", miss, tenantId);

                        foreach (var u in userIds)
                        {
                            try
                            {
                                await _notifications.NotifyUserAsync(
                                    u.Id,
                                    $"{clash.Severity} clash detected",
                                    $"{clash.DisciplineA ?? "?"} vs {clash.DisciplineB ?? "?"} at {clash.LevelCode ?? "unknown level"}",
                                    new Dictionary<string, object?>
                                    {
                                        ["channel"] = "clashes",
                                        ["clashId"] = clash.Id.ToString(),
                                        ["projectId"] = projectId.ToString(),
                                        ["severity"] = clash.Severity.ToString(),
                                    },
                                    ct);
                                notificationsSent++;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to notify user {UserId} of clash {ClashId}", u.Id, clash.Id);
                            }
                        }
                    }

                    // Also fan out a project-wide SignalR ping so the BCC clashes tab /
                    // mobile clashes list refresh in real time. Per-user pref gating
                    // happens inside NotifyProjectAsync.
                    try
                    {
                        await _notifications.NotifyProjectAsync(
                            projectId,
                            "clashes",
                            $"New {clash.Severity} clash",
                            $"{clash.DisciplineA ?? "?"} vs {clash.DisciplineB ?? "?"}",
                            new { clashId = clash.Id, severity = clash.Severity.ToString(), kind = clash.Kind.ToString() },
                            ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Project broadcast failed for clash {ClashId}", clash.Id);
                    }
                }

                if (rule.FireWebhook)
                {
                    // OutboundWebhookDispatcher does its own scope/HTTP fan-out and
                    // matches subscriptions by (tenantId, projectId, eventType).
                    // FireAndForget is non-blocking — the detection job continues
                    // immediately while the dispatcher retries on its own thread.
                    _webhooks.FireAndForget(
                        tenantId,
                        projectId,
                        WebhookEventType.ClashRaised,
                        new
                        {
                            clashId = clash.Id,
                            projectId,
                            severity = clash.Severity.ToString(),
                            kind = clash.Kind.ToString(),
                            disciplineA = clash.DisciplineA,
                            disciplineB = clash.DisciplineB,
                            elementAGuid = clash.ElementAGuid,
                            elementBGuid = clash.ElementBGuid,
                            overlapVolumeMm3 = clash.OverlapVolumeMm3,
                            distanceMm = clash.DistanceMm,
                            levelCode = clash.LevelCode,
                            zoneCode = clash.ZoneCode,
                            issueId = clash.IssueId,
                            ruleName = rule.Name,
                            detectedAt = clash.DetectedAt,
                        });
                    webhooksFired++;
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Clash automation for project {ProjectId}: {Issues} issues, {Notifications} notifications, {Webhooks} webhooks across {Clashes} new clashes",
            projectId, issuesCreated, notificationsSent, webhooksFired, clashes.Count);
    }

    private static bool Matches(ClashRecord clash, ClashAutomationRule rule)
    {
        if (rule.MinSeverity.HasValue && (int)clash.Severity < (int)rule.MinSeverity.Value) return false;
        if (rule.DisciplineA != null && clash.DisciplineA != rule.DisciplineA && clash.DisciplineB != rule.DisciplineA) return false;
        if (rule.DisciplineB != null && clash.DisciplineA != rule.DisciplineB && clash.DisciplineB != rule.DisciplineB) return false;
        if (rule.Kind.HasValue && clash.Kind != rule.Kind.Value) return false;
        if (rule.MinOverlapVolumeMm3.HasValue && clash.OverlapVolumeMm3 < rule.MinOverlapVolumeMm3.Value) return false;
        if (rule.LevelCode != null && clash.LevelCode != rule.LevelCode) return false;
        return true;
    }

    private static HashSet<string> ResolveRecipients(string? notifyUsers, string? assignedTo)
    {
        var raw = !string.IsNullOrWhiteSpace(notifyUsers) ? notifyUsers : assignedTo;
        if (string.IsNullOrWhiteSpace(raw)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Where(s => s.Contains('@')) // only obvious emails — role keywords go through a future resolver
                  .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
