using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.API.Controllers;

/// <summary>
/// Server-side Autodesk Construction Cloud issue sync (#3 scaffold).
///
/// Sits alongside <see cref="PlatformController"/> (generic platform CRUD/test) and
/// provides the ACC-specific issue-push surface plus the token-unification seam:
///
///   POST /api/projects/{projectId}/acc/sync   — push open Planscape issues → ACC,
///                                                report pushed/skipped/pulled counts.
///   GET  /api/projects/{projectId}/acc/token   — return a fresh ACC access token for
///                                                the plugin to consume (team-shared grant).
///
/// The scheduled equivalent of /sync is the Hangfire AccScheduledSyncJob (every 30 min).
///
/// Both actions require a project ADMINISTRATOR (KUT deep review INT-7). [ProjectAccess]
/// alone admitted every member, viewers included: /token handed any of them the team's
/// delegated ACC access token (data:write / data:create), and /sync let any of them create
/// ACC issues. Nothing in the plugin, the web app or the mobile app calls either endpoint, so
/// narrowing them breaks no caller.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/acc")]
[Authorize]
[ProjectAccess]
public class AccController : ControllerBase
{
    private readonly AccSyncService _acc;
    private readonly PlanscapeDbContext _db;

    public AccController(AccSyncService acc, PlanscapeDbContext db)
    {
        _acc = acc;
        _db = db;
    }

    private static ObjectResult NotAdministrator(ControllerBase c) =>
        c.StatusCode(403, new { error = "Only a project administrator may use the shared ACC connection." });

    /// <summary>Push open Planscape issues to ACC and report the result.</summary>
    [HttpPost("sync")]
    public async Task<ActionResult<AccSyncService.AccSyncReport>> Sync(Guid projectId, CancellationToken ct)
    {
        if (!await this.CanAdministerProjectAsync(_db, projectId, ct)) return NotAdministrator(this);
        var report = await _acc.SyncProjectAsync(projectId, ct);
        return report.Success ? Ok(report) : BadRequest(report);
    }

    /// <summary>
    /// Token-unification seam: hand the plugin a currently-valid ACC access token from
    /// the team-shared connection so each engineer doesn't run their own 3-legged flow.
    /// Returns 404 when no active ACC connection exists or the token refresh fails.
    /// </summary>
    [HttpGet("token")]
    public async Task<IActionResult> GetToken(Guid projectId, CancellationToken ct)
    {
        if (!await this.CanAdministerProjectAsync(_db, projectId, ct)) return NotAdministrator(this);
        var token = await _acc.GetFreshAccessTokenAsync(projectId, ct);
        if (token == null)
            return NotFound(new { message = "No active ACC connection, or token refresh failed. Connect ACC for this project first." });
        return Ok(new { accessToken = token });
    }
}
