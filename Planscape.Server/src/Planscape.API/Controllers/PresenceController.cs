using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.API.Controllers;

/// <summary>
/// T3 — REST snapshot of who is currently joined to a project's SignalR
/// group. Complements the real-time <c>PresenceChanged</c> event for screens
/// that render presence on first mount.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/presence")]
[Authorize]
public class PresenceController : ControllerBase
{
    private readonly PresenceTracker _presence;
    private readonly PlanscapeDbContext _db;

    public PresenceController(PresenceTracker presence, PlanscapeDbContext db)
    {
        _presence = presence;
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult> List(Guid projectId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var users = _presence.ProjectUsers(projectId);
        return Ok(new { projectId, count = users.Count, users });
    }

    private async Task<bool> ProjectInTenant(Guid projectId, CancellationToken ct)
    {
        var tenantId = Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
        if (tenantId == Guid.Empty) return false;
        return await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
    }
}
