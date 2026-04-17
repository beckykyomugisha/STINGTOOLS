using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// NEW-FLEX-12 — Per-user notification preferences.
/// GET /api/me/notifications/preferences — fetch current toggles + quiet hours
/// PUT /api/me/notifications/preferences — upsert the calling user's record
/// </summary>
[ApiController]
[Route("api/me/notifications/preferences")]
[Authorize]
public class NotificationPreferencesController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public NotificationPreferencesController(PlanscapeDbContext db) { _db = db; }

    private Guid GetUserId() =>
        Guid.TryParse(User.FindFirst("user_id")?.Value, out var id) ? id : Guid.Empty;

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;

    [HttpGet]
    public async Task<ActionResult<UserNotificationPreferences>> Get()
    {
        var userId = GetUserId();
        var tenantId = GetTenantId();
        if (userId == Guid.Empty || tenantId == Guid.Empty) return Forbid();

        var prefs = await _db.Set<UserNotificationPreferences>()
            .FirstOrDefaultAsync(p => p.UserId == userId);
        if (prefs == null)
        {
            // Return default shape (not persisted) so clients don't have to probe for null.
            prefs = new UserNotificationPreferences { UserId = userId, TenantId = tenantId };
        }
        return Ok(prefs);
    }

    public record UpdatePreferencesRequest(
        bool? IssuesEnabled,
        bool? ComplianceEnabled,
        bool? RevisionsEnabled,
        bool? MeetingsEnabled,
        bool? SlaBreachesEnabled,
        string? Channel,
        string? QuietHoursStart,
        string? QuietHoursEnd,
        string? TimeZone);

    [HttpPut]
    public async Task<ActionResult<UserNotificationPreferences>> Update([FromBody] UpdatePreferencesRequest req)
    {
        var userId = GetUserId();
        var tenantId = GetTenantId();
        if (userId == Guid.Empty || tenantId == Guid.Empty) return Forbid();

        var prefs = await _db.Set<UserNotificationPreferences>()
            .FirstOrDefaultAsync(p => p.UserId == userId);
        var isNew = prefs == null;
        prefs ??= new UserNotificationPreferences { UserId = userId, TenantId = tenantId };

        if (req.IssuesEnabled.HasValue) prefs.IssuesEnabled = req.IssuesEnabled.Value;
        if (req.ComplianceEnabled.HasValue) prefs.ComplianceEnabled = req.ComplianceEnabled.Value;
        if (req.RevisionsEnabled.HasValue) prefs.RevisionsEnabled = req.RevisionsEnabled.Value;
        if (req.MeetingsEnabled.HasValue) prefs.MeetingsEnabled = req.MeetingsEnabled.Value;
        if (req.SlaBreachesEnabled.HasValue) prefs.SlaBreachesEnabled = req.SlaBreachesEnabled.Value;

        if (!string.IsNullOrWhiteSpace(req.Channel))
        {
            var ch = req.Channel.ToLowerInvariant();
            if (ch is "push" or "email" or "signalr" or "all") prefs.Channel = ch;
            else return BadRequest(new { error = "Channel must be push | email | signalr | all" });
        }
        // Quiet hours — accept HH:MM or null (to disable)
        prefs.QuietHoursStart = ValidateHHMM(req.QuietHoursStart);
        prefs.QuietHoursEnd = ValidateHHMM(req.QuietHoursEnd);
        if (req.TimeZone != null) prefs.TimeZone = req.TimeZone;
        prefs.UpdatedAt = DateTime.UtcNow;

        if (isNew) _db.Add(prefs);
        await _db.SaveChangesAsync();
        return Ok(prefs);
    }

    private static string? ValidateHHMM(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (System.Text.RegularExpressions.Regex.IsMatch(raw, @"^([01]\d|2[0-3]):[0-5]\d$"))
            return raw;
        return null;
    }
}
