using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Planscape.Infrastructure.Data;
using Planscape.API.Authorization;

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
[ProjectAccess]
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
            // Phase 144 — first-class admin settings stored on the Project row
            // (not in ConfigJson) because the server reads them on the hot
            // upload path. Surfaced here so the mobile/web settings screens
            // can render them as toggles instead of having to know the JSON
            // override key. Add new admin booleans to AdminSettingFlags.
            // Phase 145 — adds the custom deliverable state machine override.
            admin = new
            {
                enforceIso19650Naming = project.EnforceIso19650Naming,
                hasCustomDeliverableStateMachine = !string.IsNullOrWhiteSpace(project.CustomDeliverableStateMachineJson),
                customDeliverableStateMachineJson = project.CustomDeliverableStateMachineJson,
            },
        };

        return Ok(settings);
    }

    /// <summary>Set of recognised admin-toggle field names. Only the keys in
    /// this set are honoured by <see cref="UpdateSettings"/>; anything else in
    /// the body is treated as a soft preference and stored in <c>ConfigJson</c>
    /// untouched, preserving forward-compat for new mobile clients.</summary>
    private static readonly HashSet<string> AdminSettingFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "enforceIso19650Naming",
    };

    /// <summary>String-typed admin fields (vs the booleans above). Phase 145
    /// adds <c>customDeliverableStateMachineJson</c> so a BIM Manager can
    /// post a per-project override of the canonical 6-state ISO 19650 flow.
    /// Empty string clears the override.</summary>
    private static readonly HashSet<string> AdminStringFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "customDeliverableStateMachineJson",
    };

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

        // Phase 144 — split admin booleans (first-class columns) from soft
        // preferences (ConfigJson). The mobile settings screen sends both in
        // the same body for simplicity, so we route by key.
        // Phase 145 — additionally route admin *string* fields (e.g. the
        // custom state-machine JSONB) to first-class columns.
        var configOverrides = new Dictionary<string, object?>(overrides.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in overrides)
        {
            if (AdminSettingFlags.Contains(kv.Key))
            {
                var truthy = ParseBool(kv.Value);
                if (string.Equals(kv.Key, "enforceIso19650Naming", StringComparison.OrdinalIgnoreCase))
                    project.EnforceIso19650Naming = truthy;
                continue;
            }
            if (AdminStringFields.Contains(kv.Key))
            {
                var s = AsString(kv.Value);
                if (string.Equals(kv.Key, "customDeliverableStateMachineJson", StringComparison.OrdinalIgnoreCase))
                {
                    // Empty string clears the override; a non-empty value is
                    // validated by parsing it through DeliverableStateMachine
                    // so a malformed payload is rejected at the API instead of
                    // silently falling back at request-time.
                    if (string.IsNullOrWhiteSpace(s))
                    {
                        project.CustomDeliverableStateMachineJson = null;
                    }
                    else
                    {
                        var parsed = Planscape.Infrastructure.Workflow.DeliverableStateMachine.LoadOrDefault(s);
                        if (!parsed.IsCustom)
                            return BadRequest(new
                            {
                                error = "customDeliverableStateMachineJson is malformed or has no transitions",
                                hint = "Body must be JSON with at least one entry in transitions[]"
                            });
                        project.CustomDeliverableStateMachineJson = s;
                    }
                }
                continue;
            }
            configOverrides[kv.Key] = kv.Value;
        }

        project.ConfigJson = System.Text.Json.JsonSerializer.Serialize(configOverrides);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static string? AsString(object? raw)
    {
        if (raw is null) return null;
        if (raw is string s) return s;
        if (raw is System.Text.Json.JsonElement je)
        {
            return je.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => je.GetString(),
                System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Array => je.GetRawText(),
                System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined => null,
                _ => je.ToString(),
            };
        }
        return raw.ToString();
    }

    private static bool ParseBool(object? raw)
    {
        if (raw is null) return false;
        if (raw is bool b) return b;
        if (raw is System.Text.Json.JsonElement je)
        {
            return je.ValueKind switch
            {
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.String => bool.TryParse(je.GetString(), out var p) && p,
                System.Text.Json.JsonValueKind.Number => je.TryGetInt32(out var n) && n != 0,
                _ => false,
            };
        }
        return bool.TryParse(raw.ToString(), out var parsed) && parsed;
    }
}
