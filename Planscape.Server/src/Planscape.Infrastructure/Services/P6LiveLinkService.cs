using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

// ── Password obfuscation helper (shared between P6Controller and P6LiveLinkService) ──

/// <summary>
/// XOR-based password obfuscation keyed on a project-specific GUID.
/// Lives in Planscape.Infrastructure so both the API layer (P6Controller)
/// and the service layer (P6LiveLinkService) can call it without a
/// circular project reference.
/// </summary>
public static class P6PasswordHelper
{
    public static string ObfuscatePassword(string password, string keyHint)
    {
        byte[] key   = DeriveKey(keyHint);
        byte[] plain = System.Text.Encoding.UTF8.GetBytes(password);
        for (int i = 0; i < plain.Length; i++)
            plain[i] ^= key[i % key.Length];
        return Convert.ToBase64String(plain);
    }

    public static string DeobfuscatePassword(string obfuscated, string keyHint)
    {
        byte[] key  = DeriveKey(keyHint);
        byte[] data = Convert.FromBase64String(obfuscated);
        for (int i = 0; i < data.Length; i++)
            data[i] ^= key[i % key.Length];
        return System.Text.Encoding.UTF8.GetString(data);
    }

    // ── Guid overloads so P6Controller can still use projectId as the key ──

    public static string ObfuscatePassword(string password, Guid projectId)
        => ObfuscatePassword(password, projectId.ToString());

    public static string DeobfuscatePassword(string obfuscated, Guid projectId)
        => DeobfuscatePassword(obfuscated, projectId.ToString());

    private static byte[] DeriveKey(string hint)
    {
        // Return the raw GUID bytes when hint is a valid GUID (16 bytes),
        // otherwise UTF-8 encode to produce a byte array of arbitrary length.
        if (Guid.TryParse(hint, out var g)) return g.ToByteArray();
        return System.Text.Encoding.UTF8.GetBytes(hint);
    }
}

// ── Settings POCO (stored as JSON in project settings) ─────────────────────

/// <summary>
/// Connection settings for the Primavera P6 REST API live link.
/// Stored per-project as JSON in the project settings column (encrypted at rest).
/// </summary>
public class P6LiveLinkSettings
{
    /// <summary>Base URL of the P6 REST API, e.g. https://p6.company.com/p6ws/restapi</summary>
    public string BaseUrl              { get; set; } = "";
    public string Username             { get; set; } = "";
    public string Password             { get; set; } = "";
    /// <summary>P6 project ID to poll.</summary>
    public string ProjectId            { get; set; } = "";
    /// <summary>How often to poll P6 for activity updates (default 30 minutes).</summary>
    public int    PollIntervalMinutes  { get; set; } = 30;
}

// ── Activity DTO (deserialised from P6 REST response) ───────────────────────

internal sealed class P6Activity
{
    public string? ObjectId          { get; set; }
    public string? ActivityId        { get; set; }
    public string? Name              { get; set; }
    public string? ActualStartDate   { get; set; }
    public string? ActualFinishDate  { get; set; }
    public double  PercentComplete   { get; set; }
    public double  RemainingDuration { get; set; }
}

// ── Service ─────────────────────────────────────────────────────────────────

/// <summary>
/// Feature gap 6 — Primavera P6 Live Link.
/// Polls the P6 REST API for activity updates and writes % complete values
/// back to <see cref="TaggedElement"/> rows that carry a matching
/// <see cref="TaggedElement.P6ActivityId"/>.
/// </summary>
public class P6LiveLinkService
{
    private readonly ILogger<P6LiveLinkService> _logger;
    private readonly IServiceScopeFactory       _scopeFactory;
    private readonly INotificationService?      _notifications;
    private readonly IPushNotificationService?  _push;

    // Milestone thresholds for push notifications (GAP-B)
    private static readonly double[] MilestoneThresholds = { 25.0, 50.0, 75.0, 100.0 };

    public P6LiveLinkService(
        ILogger<P6LiveLinkService> logger,
        IServiceScopeFactory       scopeFactory,
        INotificationService?      notifications = null,
        IPushNotificationService?  push          = null)
    {
        _logger        = logger;
        _scopeFactory  = scopeFactory;
        _notifications = notifications;
        _push          = push;
    }

    // ── Public entry point (called from Hangfire recurring job + manual trigger) ──

    /// <summary>
    /// Sync P6 activity data for a single project.
    /// Returns the resulting <see cref="P6SyncLog"/> row.
    /// </summary>
    public async Task<P6SyncLog> SyncProjectAsync(
        Guid               projectId,
        P6LiveLinkSettings settings,
        CancellationToken  ct = default)
    {
        var log = new P6SyncLog
        {
            ProjectId = projectId,
            TenantId  = Guid.Empty, // set by caller after SyncProjectAsync returns
            SyncedAt  = DateTime.UtcNow,
        };

        try
        {
            var activities = await FetchActivitiesAsync(projectId, settings, ct);
            log.ActivitiesPolled = activities.Count;

            if (activities.Count == 0)
            {
                _logger.LogInformation("P6Sync project {Id}: no activities returned from P6.", projectId);
                return log;
            }

            // Build lookup: P6ActivityId → (PercentComplete, ActualStart, ActualFinish)
            var activityData = activities
                .Where(a => a.ActivityId != null)
                .ToDictionary(
                    a => a.ActivityId!,
                    a => (Pct: a.PercentComplete, Start: a.ActualStartDate, Finish: a.ActualFinishDate));


            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();

            // Disable tenant filter so the background job can cross tenants safely.
            db.BypassTenantFilter = true;

            // Load elements for this project that have a P6 activity id.
            // Filter by projectId at DB level to avoid cross-tenant data exposure.
            var elements = await db.TaggedElements
                .Where(e => e.ProjectId == projectId)
                .Where(e => e.P6ActivityId != null && e.P6ActivityId != "")
                .ToListAsync(ct);

            int updated = 0;

            // GAP-B — track which elements just completed and which crossed milestones.
            var justCompleted = new List<TaggedElement>();
            var milestoneCrossed = new List<(TaggedElement Element, double Milestone)>();

            foreach (var el in elements)
            {
                if (el.P6ActivityId == null) continue;
                if (!activityData.TryGetValue(el.P6ActivityId, out var data)) continue;

                double previous    = el.PercentComplete ?? 0.0;
                el.PercentComplete = data.Pct;
                if (data.Start  != null) el.ActualStart  = data.Start;
                if (data.Finish != null) el.ActualFinish = data.Finish;
                updated++;

                // Detect completion (reached 100%)
                if (previous < 100.0 && data.Pct >= 100.0)
                    justCompleted.Add(el);

                // Detect milestone crossings (25%, 50%, 75%, 100%)
                foreach (var threshold in MilestoneThresholds)
                {
                    if (previous < threshold && data.Pct >= threshold)
                        milestoneCrossed.Add((el, threshold));
                }
            }

            if (updated > 0)
                await db.SaveChangesAsync(ct);

            log.ElementsUpdated = updated;
            _logger.LogInformation(
                "P6Sync project {Id}: {Polled} activities, {Updated} elements updated.",
                projectId, log.ActivitiesPolled, log.ElementsUpdated);

            // GAP-B — auto-resolve open issues linked to completed elements,
            // and send push notifications for milestone crossings.
            if (justCompleted.Count > 0 || milestoneCrossed.Count > 0)
            {
                await ProcessCompletionNotificationsAsync(
                    db, projectId, justCompleted, milestoneCrossed, ct);
            }
        }
        catch (Exception ex)
        {
            log.ErrorMessage = ex.Message;
            _logger.LogError(ex, "P6Sync project {Id} failed.", projectId);
        }

        return log;
    }

    // ── GAP-B helpers ────────────────────────────────────────────────────────

    private async Task ProcessCompletionNotificationsAsync(
        PlanscapeDbContext               db,
        Guid                             projectId,
        List<TaggedElement>              justCompleted,
        List<(TaggedElement, double)>    milestoneCrossed,
        CancellationToken                ct)
    {
        // Auto-resolve open issues linked to completed elements.
        foreach (var el in justCompleted)
        {
            // Match by ElementId stored in issue Description (since there is no
            // dedicated ActivityId FK on BimIssue — search on activity id string).
            var activityId = el.P6ActivityId ?? "";
            var openIssues = await db.Issues
                .Where(i => i.ProjectId == projectId
                         && (i.Status == "OPEN" || i.Status == "IN_PROGRESS")
                         && (i.Description != null && i.Description.Contains(activityId)))
                .ToListAsync(ct);

            foreach (var issue in openIssues)
            {
                issue.Status      = "RESOLVED";
                issue.ResolvedAt  = DateTime.UtcNow;
                issue.ResolvedBy  = "p6-auto-resolve";

                _logger.LogInformation(
                    "P6Sync: auto-resolving issue {Code} (linked to completed activity {ActivityId})",
                    issue.IssueCode, activityId);

                // Notify the issue assignee.
                if (_notifications != null && issue.AssigneeUserId.HasValue)
                {
                    try
                    {
                        await _notifications.NotifyUserAsync(
                            issue.AssigneeUserId.Value,
                            $"Issue {issue.IssueCode} auto-resolved",
                            $"P6 activity {activityId} reached 100% — issue marked resolved.",
                            new { issueId = issue.Id, issueCode = issue.IssueCode, projectId },
                            ct);
                    }
                    catch (Exception notifyEx)
                    {
                        _logger.LogWarning(notifyEx, "P6Sync: notification failed for issue {Code}.", issue.IssueCode);
                    }
                }
            }
        }

        if (justCompleted.Count > 0)
            await db.SaveChangesAsync(ct);

        // Push milestone notifications to project PM role users.
        if (milestoneCrossed.Count > 0 && _push != null)
        {
            var pmUserIds = await db.ProjectMembers
                .AsNoTracking()
                .Where(m => m.ProjectId == projectId && m.IsActive && m.ProjectRole == "PM")
                .Select(m => m.UserId)
                .Distinct()
                .ToListAsync(ct);

            foreach (var (el, milestone) in milestoneCrossed)
            {
                var body = $"Activity {el.P6ActivityId} reached {milestone}% complete.";
                foreach (var uid in pmUserIds)
                {
                    try
                    {
                        await _push.SendToUserAsync(uid, new PushPayload
                        {
                            Title   = $"P6 Milestone: {milestone}%",
                            Body    = body,
                            Channel = "p6_milestone",
                            Data    = new Dictionary<string, string>
                            {
                                ["type"]       = "p6_milestone",
                                ["activityId"] = el.P6ActivityId ?? "",
                                ["milestone"]  = milestone.ToString("F0"),
                                ["projectId"]  = projectId.ToString(),
                            }
                        }, ct);
                    }
                    catch (Exception pushEx)
                    {
                        _logger.LogWarning(pushEx,
                            "P6Sync: push notification failed for user {UserId}, milestone {Milestone}%.",
                            uid, milestone);
                    }
                }
            }
        }
    }

    // ── HTTP helper ──────────────────────────────────────────────────────────

    // GAP-E — inline retry policy: 3 attempts, exponential back-off (2s, 4s, 8s).
    // Polly is not referenced in this project, so we use a simple loop rather
    // than adding a new NuGet dependency. Only HttpRequestException and 5xx
    // status codes are retried; 4xx are treated as non-transient and thrown
    // immediately.
    private async Task<List<P6Activity>> FetchActivitiesAsync(
        Guid               planscapeProjectId,
        P6LiveLinkSettings settings,
        CancellationToken  ct)
    {
        const int maxAttempts = 3;
        int attempt = 0;

        while (true)
        {
            attempt++;
            try
            {
                return await FetchActivitiesOnceAsync(planscapeProjectId, settings, ct);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                int delayMs = (int)Math.Pow(2, attempt) * 1000; // 2s, 4s
                _logger.LogWarning(ex,
                    "P6 HTTP request failed (attempt {Attempt}/{Max}). Retrying in {Delay}ms.",
                    attempt, maxAttempts, delayMs);
                await Task.Delay(delayMs, ct);
            }
        }
    }

    private async Task<List<P6Activity>> FetchActivitiesOnceAsync(
        Guid               planscapeProjectId,
        P6LiveLinkSettings settings,
        CancellationToken  ct)
    {
        // P6 REST API: GET /activity?ProjectObjectId=<id>&Fields=ActivityId,Name,...
        // Uses HTTP Basic auth (standard for P6 REST).
        using var http = new HttpClient
        {
            BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/"),
            Timeout     = TimeSpan.FromSeconds(30),
        };

        // Deobfuscate password stored as base64-XOR by P6PasswordHelper.ObfuscatePassword.
        // The key is the Planscape project GUID (same key used at Configure time).
        string plainPassword = settings.Password;
        try { plainPassword = P6PasswordHelper.DeobfuscatePassword(settings.Password, planscapeProjectId); }
        catch { /* not obfuscated (legacy plain-text) — use as-is */ }

        string credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{settings.Username}:{plainPassword}"));
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

        string fields = "ActivityId,Name,ActualStartDate,ActualFinishDate,PercentComplete,RemainingDuration";
        string url    = $"activity?ProjectId={Uri.EscapeDataString(settings.ProjectId)}&Fields={fields}";

        var response = await http.GetAsync(url, ct);

        // Retry on 5xx; throw immediately on 4xx (non-transient).
        if ((int)response.StatusCode >= 500)
        {
            throw new HttpRequestException(
                $"P6 API returned {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        response.EnsureSuccessStatusCode();

        string body = await response.Content.ReadAsStringAsync(ct);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        // P6 REST returns either an array or a wrapper { "data": [...] }
        if (body.TrimStart().StartsWith('['))
        {
            return JsonSerializer.Deserialize<List<P6Activity>>(body, options) ?? new();
        }
        else
        {
            var wrapper = JsonSerializer.Deserialize<P6ResponseWrapper>(body, options);
            return wrapper?.Data ?? new();
        }
    }

    private sealed class P6ResponseWrapper
    {
        public List<P6Activity>? Data { get; set; }
    }

    // ── Convenience wrapper ──────────────────────────────────────────────────

    /// <summary>
    /// Convenience wrapper called by <see cref="P6Controller.SyncNow"/>
    /// via Hangfire fire-and-forget. Runs the sync and persists the
    /// resulting <see cref="P6SyncLog"/> row so it appears in GET /p6/status.
    /// </summary>
    public async Task SyncAndPersistAsync(
        Guid               projectId,
        Guid               tenantId,
        P6LiveLinkSettings settings,
        CancellationToken  ct = default)
    {
        var log = await SyncProjectAsync(projectId, settings, ct);
        log.TenantId = tenantId;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;
        db.P6SyncLogs.Add(log);
        await db.SaveChangesAsync(ct);
    }
}

// ── Hangfire job ─────────────────────────────────────────────────────────────

/// <summary>
/// Hangfire recurring job — polls P6 for all projects that have a live-link
/// configured and writes % complete back to element rows.
/// Registered in Program.cs with a 30-minute cron.
/// </summary>
public class P6LiveLinkJob
{
    private readonly IServiceScopeFactory       _scopeFactory;
    private readonly P6LiveLinkService          _svc;
    private readonly ILogger<P6LiveLinkJob>     _logger;

    public P6LiveLinkJob(
        IServiceScopeFactory   scopeFactory,
        P6LiveLinkService      svc,
        ILogger<P6LiveLinkJob> logger)
    {
        _scopeFactory = scopeFactory;
        _svc          = svc;
        _logger       = logger;
    }

    [Hangfire.AutomaticRetry(Attempts = 2, OnAttemptsExceeded = Hangfire.AttemptsExceededAction.Delete)]
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        db.BypassTenantFilter = true;

        // Load only active projects that have P6 settings stored.
        // Archived / handed-over projects are excluded to avoid unnecessary P6 API calls.
        var projects = await db.Projects
            .AsNoTracking()
            .Where(p => p.Status == ProjectStatus.Active
                     && p.ConfigJson != null && p.ConfigJson.Contains("\"p6\""))
            .ToListAsync(ct);

        _logger.LogInformation("P6LiveLinkJob: {Count} projects with P6 config.", projects.Count);

        foreach (var project in projects)
        {
            P6LiveLinkSettings? settings = null;
            try
            {
                if (!string.IsNullOrEmpty(project.ConfigJson))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(project.ConfigJson);
                    if (doc.RootElement.TryGetProperty("p6", out var p6El))
                    {
                        settings = System.Text.Json.JsonSerializer.Deserialize<P6LiveLinkSettings>(
                            p6El.GetRawText(),
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "P6LiveLinkJob: could not parse P6 settings for project {Id}.", project.Id);
                continue;
            }

            if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl)) continue;

            var log = await _svc.SyncProjectAsync(project.Id, settings, ct);
            log.TenantId = project.TenantId;
            db.P6SyncLogs.Add(log);
        }

        if (projects.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
