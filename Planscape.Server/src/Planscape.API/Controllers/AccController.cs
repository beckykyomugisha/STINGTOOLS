using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planscape.API.Authorization;
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
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/acc")]
[Authorize]
[ProjectAccess]
public class AccController : ControllerBase
{
    private readonly AccSyncService _acc;

    public AccController(AccSyncService acc) => _acc = acc;

    /// <summary>Push open Planscape issues to ACC and report the result.</summary>
    [HttpPost("sync")]
    public async Task<ActionResult<AccSyncService.AccSyncReport>> Sync(Guid projectId, CancellationToken ct)
    {
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
        var token = await _acc.GetFreshAccessTokenAsync(projectId, ct);
        if (token == null)
            return NotFound(new { message = "No active ACC connection, or token refresh failed. Connect ACC for this project first." });
        return Ok(new { accessToken = token });
    }
}
