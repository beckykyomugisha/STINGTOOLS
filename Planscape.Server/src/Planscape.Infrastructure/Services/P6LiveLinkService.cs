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
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

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

    public P6LiveLinkService(
        ILogger<P6LiveLinkService> logger,
        IServiceScopeFactory       scopeFactory)
    {
        _logger       = logger;
        _scopeFactory = scopeFactory;
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
            var activities = await FetchActivitiesAsync(settings, ct);
            log.ActivitiesPolled = activities.Count;

            if (activities.Count == 0)
            {
                _logger.LogInformation("P6Sync project {Id}: no activities returned from P6.", projectId);
                return log;
            }

            // Build lookup: P6ActivityId → % complete
            var pctByActivity = activities
                .Where(a => a.ActivityId != null)
                .ToDictionary(a => a.ActivityId!, a => a.PercentComplete);

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
            foreach (var el in elements)
            {
                if (el.P6ActivityId == null) continue;
                if (!pctByActivity.TryGetValue(el.P6ActivityId, out double pct)) continue;

                el.PercentComplete = pct;
                updated++;
            }

            if (updated > 0)
                await db.SaveChangesAsync(ct);

            log.ElementsUpdated = updated;
            _logger.LogInformation(
                "P6Sync project {Id}: {Polled} activities, {Updated} elements updated.",
                projectId, log.ActivitiesPolled, log.ElementsUpdated);
        }
        catch (Exception ex)
        {
            log.ErrorMessage = ex.Message;
            _logger.LogError(ex, "P6Sync project {Id} failed.", projectId);
        }

        return log;
    }

    // ── HTTP helper ──────────────────────────────────────────────────────────

    private async Task<List<P6Activity>> FetchActivitiesAsync(
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

        string credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}"));
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

        string fields = "ActivityId,Name,ActualStartDate,ActualFinishDate,PercentComplete,RemainingDuration";
        string url    = $"activity?ProjectId={Uri.EscapeDataString(settings.ProjectId)}&Fields={fields}";

        var response = await http.GetAsync(url, ct);
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
}

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

        // Load all projects that have P6 settings stored
        var projects = await db.Projects
            .AsNoTracking()
            .Where(p => p.ConfigJson != null && p.ConfigJson.Contains("\"p6\""))
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
