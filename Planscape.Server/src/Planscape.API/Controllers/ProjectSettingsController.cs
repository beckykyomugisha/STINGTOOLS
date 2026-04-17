using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// NEW-FLEX-02 / 05 / 08 — Per-project configuration surfaced to mobile so
/// issue types, priorities, disciplines, attachment limits and SLA hours are
/// no longer hard-coded in the UI.
///
/// Reads from three sources (in order of precedence):
///   1. Project.SettingsJson overrides
///   2. Tenant-level appsettings section (Tenants:{id}:Settings)
///   3. System defaults (this controller's Defaults block)
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/settings")]
[Authorize]
public class ProjectSettingsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IConfiguration _config;

    public ProjectSettingsController(PlanscapeDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;

    [HttpGet]
    public async Task<ActionResult> GetSettings(Guid projectId)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound();

        // Deserialize any project-level overrides
        Dictionary<string, object?> overrides = new();
        if (!string.IsNullOrWhiteSpace(project.ConfigJson))
        {
            try
            {
                overrides = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(project.ConfigJson)
                    ?? new();
            }
            catch { /* malformed — ignore, fall back to defaults */ }
        }

        // Defaults — match what the mobile UI hard-codes today.
        var settings = new
        {
            issueTypes = overrides.ContainsKey("issueTypes") ? overrides["issueTypes"]
                : new[] { "RFI", "NCR", "SI", "TQ", "CLASH", "DEFECT" },
            priorities = overrides.ContainsKey("priorities") ? overrides["priorities"]
                : new[] { "CRITICAL", "HIGH", "MEDIUM", "LOW" },
            disciplines = overrides.ContainsKey("disciplines") ? overrides["disciplines"]
                : new[] { "M", "E", "P", "A", "S", "FP", "LV", "G" },
            cdeStates = overrides.ContainsKey("cdeStates") ? overrides["cdeStates"]
                : new[] { "WIP", "SHARED", "PUBLISHED", "ARCHIVE" },
            suitabilityCodes = overrides.ContainsKey("suitabilityCodes") ? overrides["suitabilityCodes"]
                : new[] { "S0", "S1", "S2", "S3", "S4", "S5", "S6", "S7" },
            limits = new
            {
                maxAttachmentMB = GetInt(overrides, "maxAttachmentMB", 50),
                maxDocumentMB = GetInt(overrides, "maxDocumentMB", 100),
                maxPhotosPerIssue = GetInt(overrides, "maxPhotosPerIssue", 10),
            },
            slaHours = new
            {
                critical = GetInt(overrides, "slaCriticalHours", 4),
                high = GetInt(overrides, "slaHighHours", 24),
                medium = GetInt(overrides, "slaMediumHours", 168),
                low = GetInt(overrides, "slaLowHours", 336),
            },
            geofence = new
            {
                hasBoundary = !string.IsNullOrWhiteSpace(project.BoundaryPolygon),
                requireBoundary = bool.TryParse(_config["Geofence:RequireBoundary"], out var req) && req,
            },
        };

        return Ok(settings);
    }

    private static int GetInt(Dictionary<string, object?> dict, string key, int fallback)
    {
        if (!dict.TryGetValue(key, out var v) || v == null) return fallback;
        if (v is System.Text.Json.JsonElement je && je.TryGetInt32(out var i)) return i;
        if (int.TryParse(v.ToString(), out var p)) return p;
        return fallback;
    }

    /// <summary>
    /// Project admin can overwrite the overrides JSON. Caller must have a
    /// project role of K (BIM Manager) or C (Coordinator).
    /// </summary>
    [HttpPut]
    public async Task<ActionResult> UpdateSettings(Guid projectId, [FromBody] Dictionary<string, object?> overrides)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound();

        var userIdClaim = User.FindFirst("user_id")?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId)) return Forbid();

        var member = await _db.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId && m.IsActive);
        if (member == null || (member.Iso19650Role != "K" && member.Iso19650Role != "C"))
            return Forbid();

        project.ConfigJson = System.Text.Json.JsonSerializer.Serialize(overrides);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
