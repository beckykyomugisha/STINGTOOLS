using Hangfire;
using Hangfire.Dashboard;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Hangfire dashboard authorization filter.
/// Allows all access in Development; requires Admin role in production.
/// </summary>
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // Allow unrestricted access in Development
        var env = httpContext.RequestServices.GetRequiredService<IHostEnvironment>();
        if (env.IsDevelopment())
            return true;

        // In production, require authenticated user with Admin role
        return httpContext.User.Identity?.IsAuthenticated == true
            && httpContext.User.IsInRole("Admin");
    }
}

/// <summary>
/// Hourly job that scans all active projects and creates compliance snapshots
/// for any project whose compliance falls below threshold.
/// </summary>
public class ComplianceCheckJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ComplianceCheckJob> _logger;

    private const double ComplianceThreshold = 80.0;

    public ComplianceCheckJob(IServiceScopeFactory scopeFactory, ILogger<ComplianceCheckJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [Hangfire.AutomaticRetry(Attempts = 3, OnAttemptsExceeded = Hangfire.AttemptsExceededAction.Delete)]
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 3600)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("ComplianceCheckJob started");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        // Phase 175 audit P0-1 — Hangfire runs without HttpContext so the
        // global tenant filter sees Guid.Empty and matches no rows. Bypass
        // the filter and rely on each row's TenantId for cross-tenant work.
        db.BypassTenantFilter = true;

        var projects = await db.Projects
            .Where(p => p.Status == Core.Entities.ProjectStatus.Active)
            .ToListAsync(ct);

        int snapshotsCreated = 0;

        foreach (var project in projects)
        {
            if (project.CompliancePercent >= ComplianceThreshold)
                continue;

            var totalElements = await db.TaggedElements
                .CountAsync(e => e.ProjectId == project.Id, ct);
            var taggedComplete = await db.TaggedElements
                .CountAsync(e => e.ProjectId == project.Id && !string.IsNullOrEmpty(e.Tag1), ct);
            var staleCount = await db.TaggedElements
                .CountAsync(e => e.ProjectId == project.Id && e.IsStale, ct);

            double tagPercent = totalElements > 0
                ? Math.Round(100.0 * taggedComplete / totalElements, 2)
                : 0;

            string ragStatus = tagPercent >= 90 ? "GREEN" : tagPercent >= 70 ? "AMBER" : "RED";

            var snapshot = new Core.Entities.ComplianceSnapshot
            {
                ProjectId = project.Id,
                CapturedBy = "system/compliance-check",
                CapturedAt = DateTime.UtcNow,
                TotalElements = totalElements,
                TaggedComplete = taggedComplete,
                Untagged = totalElements - taggedComplete,
                StaleCount = staleCount,
                TagPercent = tagPercent,
                RagStatus = ragStatus
            };

            db.ComplianceSnapshots.Add(snapshot);
            snapshotsCreated++;

            _logger.LogInformation(
                "Project {Code} compliance {Percent}% (below {Threshold}%) — snapshot created",
                project.Code, tagPercent, ComplianceThreshold);
        }

        if (snapshotsCreated > 0)
            await db.SaveChangesAsync(ct);

        _logger.LogInformation("ComplianceCheckJob completed — {Count} snapshots created", snapshotsCreated);
    }
}

/// <summary>
/// Runs every 15 minutes. Checks BimIssues for overdue SLA (DueDate in the past,
/// Status not CLOSED/RESOLVED) and escalates priority.
/// </summary>
public class SlaEscalationJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SlaEscalationJob> _logger;

    public SlaEscalationJob(IServiceScopeFactory scopeFactory, ILogger<SlaEscalationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // Phase 175 audit P0-6 — make the job retry-safe. Without an explicit
    // policy, Hangfire's default 10-retry exponential backoff can re-bump
    // priority on a transient failure, fast-tracking issues to CRITICAL.
    [Hangfire.AutomaticRetry(Attempts = 3, OnAttemptsExceeded = Hangfire.AttemptsExceededAction.Delete)]
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 3600)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("SlaEscalationJob started");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        // Phase 175 audit P0-1 — Hangfire has no HttpContext; the global
        // tenant filter would otherwise see Guid.Empty and skip every row.
        db.BypassTenantFilter = true;
        var notifications = scope.ServiceProvider.GetService<INotificationService>();
        var push = scope.ServiceProvider.GetService<IPushNotificationService>();

        var now = DateTime.UtcNow;

        var overdueIssues = await db.Issues
            .Include(i => i.Project)
            .Include(i => i.AssigneeUser)
            .Where(i => i.DueDate != null
                && i.DueDate < now
                && i.Status != "CLOSED"
                && i.Status != "RESOLVED"
                && i.Priority != "CRITICAL")
            .ToListAsync(ct);

        int escalated = 0;

        foreach (var issue in overdueIssues)
        {
            var previousPriority = issue.Priority;

            issue.Priority = issue.Priority switch
            {
                "LOW" => "MEDIUM",
                "MEDIUM" => "HIGH",
                "HIGH" => "CRITICAL",
                _ => issue.Priority
            };

            if (issue.Priority != previousPriority)
            {
                escalated++;
                _logger.LogInformation(
                    "Issue {Code} escalated {From} -> {To} (due {DueDate:u})",
                    issue.IssueCode, previousPriority, issue.Priority, issue.DueDate);

                // SRV-07 — SLA breach goes to the issue's project members only,
                // not the whole tenant. Critical channels still bypass quiet hours
                // inside ResolveDelivery so on-call recipients still get paged.
                var tenantId = issue.Project?.TenantId ?? Guid.Empty;
                if (notifications != null)
                {
                    _ = notifications.NotifyProjectAsync(issue.ProjectId, "sla_breach",
                        $"SLA Breach: {issue.IssueCode} escalated to {issue.Priority}",
                        $"{issue.Title} — was {previousPriority}, overdue since {issue.DueDate:u}",
                        new { issue.Id, issue.IssueCode, previousPriority, issue.Priority, issue.ProjectId },
                        ct);
                }

                // Push to assignee if set. Phase 175 audit P0-5 — prefer
                // the AssigneeUserId FK (already loaded via Include) over
                // matching by DisplayName, which is non-unique and silently
                // mis-routes when two users share a name.
                var assigneeUser = issue.AssigneeUser;
                if (assigneeUser == null && !string.IsNullOrEmpty(issue.Assignee) && tenantId != Guid.Empty)
                {
                    // Legacy issues lacking AssigneeUserId — best-effort
                    // fall back, scoped to tenant. Logged so we can spot
                    // unmigrated rows and backfill the FK.
                    assigneeUser = await db.Users.FirstOrDefaultAsync(
                        u => u.DisplayName == issue.Assignee && u.TenantId == tenantId, ct);
                    if (assigneeUser != null)
                        _logger.LogDebug("Issue {Code} resolved assignee by DisplayName fallback — backfill AssigneeUserId.", issue.IssueCode);
                }

                if (assigneeUser != null && push != null)
                {
                    _ = push.SendToUserAsync(assigneeUser.Id, new PushPayload
                    {
                        Title = $"SLA Breach: {issue.IssueCode}",
                        Body = $"Escalated {previousPriority} → {issue.Priority}. {issue.Title}",
                        Channel = "sla_breach",
                        Data = new Dictionary<string, string>
                        {
                            ["type"] = "sla_escalation",
                            ["issueId"] = issue.Id.ToString(),
                            ["issueCode"] = issue.IssueCode,
                            ["priority"] = issue.Priority,
                            ["projectId"] = issue.ProjectId.ToString()
                        }
                    }, ct);
                }
            }
        }

        if (escalated > 0)
            await db.SaveChangesAsync(ct);

        // Phase 178b — T2-11 escalation chains. After the priority bump
        // above, additionally fire a "manager escalation" wave for issues
        // that have been CRITICAL + overdue past a threshold. This gives
        // the chain three steps:
        //   step 1  (existing): priority bump + push to assignee
        //   step 2  (new):      notify project PMs once issue is
        //                       CRITICAL AND overdue ≥ 24 h
        //   step 3  (new):      notify project Owner / Admin once issue
        //                       is CRITICAL AND overdue ≥ 72 h
        // Each escalation is logged to AuditLog so the chain is
        // visible on the issue's activity timeline. The thresholds are
        // intentionally relative to *now*, not the SLA breach moment,
        // so a re-opened CRITICAL issue gets the same treatment.
        var stuckCritical = await db.Issues
            .Include(i => i.Project)
            .Where(i => i.Priority == "CRITICAL"
                     && i.Status != "CLOSED"
                     && i.Status != "RESOLVED"
                     && i.DueDate != null
                     && i.DueDate < now)
            .ToListAsync(ct);

        int chainStep2 = 0, chainStep3 = 0;
        foreach (var issue in stuckCritical)
        {
            var overdueHours = (now - issue.DueDate!.Value).TotalHours;
            var tenantId = issue.Project?.TenantId ?? Guid.Empty;
            if (tenantId == Guid.Empty) continue;

            // Step 3 — Owner / Admin notification (≥ 72 h)
            if (overdueHours >= 72 && !await EscalationFiredAsync(db, issue.Id, "ESCALATE_STEP_3", ct))
            {
                var ownerIds = await db.ProjectMembers
                    .AsNoTracking()
                    .Where(m => m.ProjectId == issue.ProjectId && m.IsActive
                             && (m.ProjectRole == "Owner" || m.ProjectRole == "Admin"))
                    .Select(m => m.UserId)
                    .Distinct()
                    .ToListAsync(ct);
                foreach (var uid in ownerIds)
                {
                    if (push != null)
                    {
                        _ = push.SendToUserAsync(uid, new PushPayload {
                            Title = $"⛔ {issue.IssueCode} — 72h overdue",
                            Body = $"CRITICAL issue past escalation threshold: {issue.Title}",
                            Channel = "sla_breach",
                            Data = new Dictionary<string, string> {
                                ["type"] = "sla_escalation_step3",
                                ["issueId"] = issue.Id.ToString(),
                                ["issueCode"] = issue.IssueCode,
                                ["projectId"] = issue.ProjectId.ToString(),
                            }
                        }, ct);
                    }
                }
                db.AuditLogs.Add(new AuditLog {
                    TenantId = tenantId, ProjectId = issue.ProjectId,
                    Action = "ESCALATE_STEP_3", EntityType = "Issue", EntityId = issue.Id.ToString(),
                    DetailsJson = System.Text.Json.JsonSerializer.Serialize(new {
                        overdueHours = (int)overdueHours, recipientCount = ownerIds.Count
                    }),
                    Timestamp = now,
                });
                chainStep3++;
            }
            // Step 2 — PM notification (≥ 24 h, not yet step-3'd)
            else if (overdueHours >= 24 && !await EscalationFiredAsync(db, issue.Id, "ESCALATE_STEP_2", ct))
            {
                var pmIds = await db.ProjectMembers
                    .AsNoTracking()
                    .Where(m => m.ProjectId == issue.ProjectId && m.IsActive && m.ProjectRole == "PM")
                    .Select(m => m.UserId)
                    .Distinct()
                    .ToListAsync(ct);
                foreach (var uid in pmIds)
                {
                    if (push != null)
                    {
                        _ = push.SendToUserAsync(uid, new PushPayload {
                            Title = $"⚠ {issue.IssueCode} — 24h overdue",
                            Body = $"CRITICAL issue: {issue.Title}",
                            Channel = "sla_breach",
                            Data = new Dictionary<string, string> {
                                ["type"] = "sla_escalation_step2",
                                ["issueId"] = issue.Id.ToString(),
                                ["issueCode"] = issue.IssueCode,
                                ["projectId"] = issue.ProjectId.ToString(),
                            }
                        }, ct);
                    }
                }
                db.AuditLogs.Add(new AuditLog {
                    TenantId = tenantId, ProjectId = issue.ProjectId,
                    Action = "ESCALATE_STEP_2", EntityType = "Issue", EntityId = issue.Id.ToString(),
                    DetailsJson = System.Text.Json.JsonSerializer.Serialize(new {
                        overdueHours = (int)overdueHours, recipientCount = pmIds.Count
                    }),
                    Timestamp = now,
                });
                chainStep2++;
            }
        }
        if (chainStep2 + chainStep3 > 0) await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SlaEscalationJob completed — {Bumped} priority-bumped, {Step2} PM-notified, {Step3} Owner-notified",
            escalated, chainStep2, chainStep3);
    }

    /// <summary>
    /// Idempotency guard: an escalation step fires at most once per issue.
    /// Probes AuditLog for a row with the same (Action, EntityId).
    /// </summary>
    private static async Task<bool> EscalationFiredAsync(PlanscapeDbContext db, Guid issueId, string action, CancellationToken ct)
    {
        var idStr = issueId.ToString();
        return await db.AuditLogs.AsNoTracking()
            .AnyAsync(a => a.EntityType == "Issue" && a.EntityId == idStr && a.Action == action, ct);
    }
}

/// <summary>
/// Daily job that archives (deletes) compliance snapshots older than 90 days
/// where WarningCount > 0, keeping the data set manageable.
/// </summary>
public class StaleWarningCleanupJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StaleWarningCleanupJob> _logger;

    private const int RetentionDays = 90;

    public StaleWarningCleanupJob(IServiceScopeFactory scopeFactory, ILogger<StaleWarningCleanupJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [Hangfire.AutomaticRetry(Attempts = 3, OnAttemptsExceeded = Hangfire.AttemptsExceededAction.Delete)]
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 3600)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("StaleWarningCleanupJob started");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        // Phase 175 audit P0-1 — Hangfire context lacks tenant_id.
        db.BypassTenantFilter = true;

        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);

        var staleSnapshots = await db.ComplianceSnapshots
            .Where(s => s.CapturedAt < cutoff && s.WarningCount > 0)
            .ToListAsync(ct);

        if (staleSnapshots.Count > 0)
        {
            db.ComplianceSnapshots.RemoveRange(staleSnapshots);
            await db.SaveChangesAsync(ct);
        }

        // NEW-LOGIC-14 — Prune push tokens that haven't been touched in 90 days.
        // FirebasePushService.BumpLastUsedAsync refreshes LastUsedAt on every
        // successful send, so an old timestamp implies the device is gone.
        var tokenCutoff = DateTime.UtcNow.AddDays(-90);
        var staleTokens = await db.Set<DevicePushToken>()
            .Where(t => t.LastUsedAt < tokenCutoff)
            .ToListAsync(ct);
        if (staleTokens.Count > 0)
        {
            db.Set<DevicePushToken>().RemoveRange(staleTokens);
            await db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "StaleWarningCleanupJob completed — {Snapshots} snapshots + {Tokens} stale push tokens pruned",
            staleSnapshots.Count, staleTokens.Count);
    }
}

/// <summary>
/// Runs every 30 minutes. Iterates active PlatformConnections, refreshes tokens
/// if needed, syncs tagged elements to the external platform, and records results.
/// </summary>
public class PlatformSyncJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlatformSyncJob> _logger;

    private static readonly TimeSpan TokenRefreshBuffer = TimeSpan.FromMinutes(10);

    public PlatformSyncJob(IServiceScopeFactory scopeFactory, ILogger<PlatformSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [Hangfire.AutomaticRetry(Attempts = 3, OnAttemptsExceeded = Hangfire.AttemptsExceededAction.Delete)]
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 3600)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("PlatformSyncJob started");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        // Phase 175 audit P0-1 — Hangfire context lacks tenant_id.
        db.BypassTenantFilter = true;
        var factory = scope.ServiceProvider.GetRequiredService<IPlatformConnectorFactory>();

        var connections = await db.PlatformConnections
            .Include(c => c.Project)
            .Where(c => c.IsActive)
            .ToListAsync(ct);

        int synced = 0, failed = 0;

        foreach (var conn in connections)
        {
            try
            {
                var connector = factory.GetConnector(conn.Platform);

                // Refresh token if expiring soon
                if (conn.TokenExpiresAt.HasValue
                    && conn.TokenExpiresAt.Value - DateTime.UtcNow < TokenRefreshBuffer)
                {
                    var tokenResult = await connector.RefreshTokenAsync(conn, ct);
                    if (tokenResult.Success)
                    {
                        conn.AccessToken = tokenResult.AccessToken;
                        conn.RefreshToken = tokenResult.RefreshToken ?? conn.RefreshToken;
                        conn.TokenExpiresAt = tokenResult.ExpiresAt;
                        _logger.LogInformation("Refreshed token for {Platform} connection {Id}",
                            conn.Platform, conn.Id);
                    }
                    else
                    {
                        _logger.LogWarning("Token refresh failed for {Platform} connection {Id}: {Error}",
                            conn.Platform, conn.Id, tokenResult.Error);
                        conn.LastSyncStatus = "TOKEN_REFRESH_FAILED";
                        conn.LastSyncError = tokenResult.Error;
                        failed++;
                        continue;
                    }
                }

                // Get tagged elements for this project
                var elements = await db.TaggedElements
                    .Where(e => e.ProjectId == conn.ProjectId)
                    .ToListAsync(ct);

                var syncResult = await connector.SyncAsync(conn, elements, ct);

                conn.LastSyncAt = DateTime.UtcNow;
                conn.LastSyncStatus = syncResult.Success ? "OK" : "FAILED";
                conn.LastSyncError = syncResult.Error;

                if (syncResult.Success)
                {
                    synced++;
                    _logger.LogInformation(
                        "Synced {Platform} connection {Id} — pushed {Pushed}, pulled {Pulled}",
                        conn.Platform, conn.Id, syncResult.PushedCount, syncResult.PulledCount);
                }
                else
                {
                    failed++;
                    _logger.LogWarning(
                        "Sync failed for {Platform} connection {Id}: {Error}",
                        conn.Platform, conn.Id, syncResult.Error);
                }
            }
            catch (Exception ex)
            {
                failed++;
                conn.LastSyncAt = DateTime.UtcNow;
                conn.LastSyncStatus = "ERROR";
                conn.LastSyncError = ex.Message;
                _logger.LogError(ex, "PlatformSyncJob error for connection {Id}", conn.Id);
            }
        }

        if (connections.Count > 0)
            await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "PlatformSyncJob completed — {Total} connections, {Synced} synced, {Failed} failed",
            connections.Count, synced, failed);
    }
}

