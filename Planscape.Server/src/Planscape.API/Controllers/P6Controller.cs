using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;
using Hangfire;
using Newtonsoft.Json;
using Planscape.API.Authorization;

namespace Planscape.API.Controllers;

/// <summary>
/// Feature gap 6 — Primavera P6 Live Link.
/// POST /api/projects/{id}/p6/configure  — save P6 connection settings
/// GET  /api/projects/{id}/p6/status     — last sync time + stats
/// POST /api/projects/{id}/p6/sync       — trigger immediate sync
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/p6")]
[Authorize]
[ProjectAccess]
public class P6Controller : ControllerBase
{
    private readonly PlanscapeDbContext  _db;
    private readonly P6LiveLinkService  _p6Svc;

    public P6Controller(PlanscapeDbContext db, P6LiveLinkService p6Svc)
    {
        _db    = db;
        _p6Svc = p6Svc;
    }

    // ── POST /api/projects/{projectId}/p6/configure ────────────────────────

    /// <summary>
    /// Save (or update) P6 live-link settings for a project.
    /// Settings are merged into the existing project.ConfigJson JSON under the
    /// "p6" key so other settings are preserved.
    /// SECURITY: Password is stored as received (plain text in ConfigJson).
    /// Production deployments MUST use column-level encryption (e.g. pgcrypto)
    /// or a secrets manager (Vault, AWS Secrets Manager) rather than storing
    /// P6 credentials in the ConfigJson column. TLS between the API and Postgres
    /// is a minimum baseline but is not sufficient on its own.
    /// </summary>
    [HttpPost("configure")]
    public async Task<ActionResult> Configure(
        Guid               projectId,
        [FromBody] P6LiveLinkSettings settings,
        CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();

        var project = await _db.Projects.FindAsync([projectId], ct);
        if (project == null) return NotFound();

        // Merge into existing settings JSON
        var settingsDict = new Dictionary<string, object?>();

        if (!string.IsNullOrEmpty(project.ConfigJson))
        {
            try
            {
                var existing = JsonConvert.DeserializeObject<Dictionary<string, object?>>(project.ConfigJson);
                if (existing != null) settingsDict = existing;
            }
            catch { /* malformed — start fresh */ }
        }

        settingsDict["p6"] = settings;
        project.ConfigJson   = JsonConvert.SerializeObject(settingsDict);
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "P6 settings saved.", pollIntervalMinutes = settings.PollIntervalMinutes });
    }

    // ── GET /api/projects/{projectId}/p6/status ────────────────────────────

    /// <summary>Returns the last sync log entry for the project.</summary>
    [HttpGet("status")]
    public async Task<ActionResult> Status(Guid projectId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();

        var project = await _db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.ConfigJson)
            .FirstOrDefaultAsync(ct);

        // Compute isConfigured directly from the stored settings so a newly
        // configured project that has never synced still reports true.
        bool isConfigured = false;
        if (!string.IsNullOrEmpty(project))
        {
            try
            {
                var dict = JsonConvert.DeserializeObject<Dictionary<string, object?>>(project);
                if (dict != null && dict.TryGetValue("p6", out var p6Obj) && p6Obj != null)
                {
                    var p6Settings = JsonConvert.DeserializeObject<P6LiveLinkSettings>(p6Obj.ToString() ?? "{}");
                    isConfigured = p6Settings != null && !string.IsNullOrWhiteSpace(p6Settings.BaseUrl);
                }
            }
            catch { /* malformed JSON — treat as unconfigured */ }
        }

        var logs = await _db.P6SyncLogs
            .AsNoTracking()
            .Where(l => l.ProjectId == projectId)
            .OrderByDescending(l => l.SyncedAt)
            .Take(10)
            .ToListAsync(ct);

        var last = logs.FirstOrDefault();
        return Ok(new
        {
            isConfigured,
            lastSyncAt       = last?.SyncedAt,
            activitiesPolled = last?.ActivitiesPolled ?? 0,
            elementsUpdated  = last?.ElementsUpdated  ?? 0,
            error            = last?.ErrorMessage,
            history          = logs.Select(l => new
            {
                syncedAt         = l.SyncedAt,
                activitiesPolled = l.ActivitiesPolled,
                elementsUpdated  = l.ElementsUpdated,
                error            = l.ErrorMessage,
            }),
        });
    }

    // ── GET /api/projects/{projectId}/p6/logs ─────────────────────────────

    /// <summary>GAP-C — Returns the last 20 P6 sync log rows ordered by SyncedAt desc.</summary>
    [HttpGet("logs")]
    public async Task<ActionResult> Logs(Guid projectId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();

        var logs = await _db.P6SyncLogs
            .AsNoTracking()
            .Where(l => l.ProjectId == projectId)
            .OrderByDescending(l => l.SyncedAt)
            .Take(20)
            .Select(l => new
            {
                syncedAt         = l.SyncedAt,
                activitiesPolled = l.ActivitiesPolled,
                elementsUpdated  = l.ElementsUpdated,
                error            = l.ErrorMessage,
            })
            .ToListAsync(ct);

        return Ok(logs);
    }

    // ── POST /api/projects/{projectId}/p6/sync ─────────────────────────────

    /// <summary>Trigger an immediate P6 sync (Hangfire fire-and-forget).</summary>
    [HttpPost("sync")]
    public async Task<ActionResult> SyncNow(Guid projectId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();

        var project = await _db.Projects.FindAsync([projectId], ct);
        if (project == null) return NotFound();

        P6LiveLinkSettings? settings = null;
        if (!string.IsNullOrEmpty(project.ConfigJson))
        {
            try
            {
                var dict = JsonConvert.DeserializeObject<Dictionary<string, object?>>(project.ConfigJson);
                if (dict != null && dict.TryGetValue("p6", out var p6Obj))
                    settings = JsonConvert.DeserializeObject<P6LiveLinkSettings>(p6Obj?.ToString() ?? "{}");
            }
            catch { }
        }

        if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
            return BadRequest(new { error = "P6 live link is not configured for this project. Call POST /p6/configure first." });

        // Enqueue as a Hangfire fire-and-forget job so the HTTP call returns immediately.
        // Use SyncAndPersistAsync so the resulting P6SyncLog is saved and visible in GET /p6/status.
        var capturedProjectId = projectId;
        var capturedSettings  = settings;
        var capturedTenantId  = GetTenantId();
        BackgroundJob.Enqueue<P6LiveLinkService>(svc =>
            svc.SyncAndPersistAsync(capturedProjectId, capturedTenantId, capturedSettings, CancellationToken.None));

        return Ok(new { status = "Sync enqueued. Check GET /p6/status in ~30 seconds for results." });
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<bool> ProjectInTenant(Guid projectId, CancellationToken ct)
        => await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == GetTenantId(), ct);

    private Guid GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }
}
