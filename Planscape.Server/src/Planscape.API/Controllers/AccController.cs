using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.API.Controllers;

/// <summary>
/// Server-side Autodesk Construction Cloud issue sync.
///
///   POST /api/projects/{projectId}/acc/sync          — push open Planscape issues → ACC
///   GET  /api/projects/{projectId}/acc/token         — fresh ACC access token for the plugin
///   GET  /api/projects/{projectId}/acc/hubs          — discovery: hubs visible to the grant
///   GET  /api/projects/{projectId}/acc/hubs/{hubId}/projects — discovery: projects in a hub
///   GET  /api/projects/{projectId}/acc/issue-types   — resolve the clash/coordination subtype
///   PUT  /api/projects/{projectId}/acc/selection     — store hub / ACC project / region / subtype
///
/// [ProjectAccess] (visibility, 404) applies to all. Releasing the team token
/// needs an author-level role; changing the connection's selection needs the
/// administer capability, the same gate as connecting.
///
/// The scheduled equivalent of /sync is the Hangfire recurring AccSyncService job.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/acc")]
[Authorize]
[ProjectAccess]
public class AccController : ControllerBase
{
    private readonly AccSyncService _acc;
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<AccController> _log;

    public AccController(AccSyncService acc, PlanscapeDbContext db, ILogger<AccController> log)
    {
        _acc = acc;
        _db = db;
        _log = log;
    }

    /// <summary>
    /// Push open Planscape issues to ACC. 200 for OK and PARTIAL (the report says
    /// which), 409 when another sync holds the lock, 502 when the sync FAILED.
    /// </summary>
    [HttpPost("sync")]
    public async Task<ActionResult<AccSyncService.AccSyncReport>> Sync(Guid projectId, CancellationToken ct)
    {
        if (!await this.CanWriteProjectAsync(_db, projectId, ct))
            return StatusCode(403, new { error = "A read-only project role cannot push issues to ACC." });
        var report = await _acc.SyncProjectAsync(projectId, ct);
        return report.Status switch
        {
            AccSyncService.StatusOk or AccSyncService.StatusPartial => Ok(report),
            AccSyncService.StatusBusy => Conflict(report),
            _ => StatusCode(502, report),
        };
    }

    /// <summary>
    /// Token-unification seam: hand the plugin a currently-valid ACC access token
    /// from the team-shared connection. The token can WRITE to ACC, so it is
    /// released only to author-level project roles (not Viewer / ClientGuest),
    /// and every issuance is logged.
    /// </summary>
    [HttpGet("token")]
    public async Task<IActionResult> GetToken(Guid projectId, CancellationToken ct)
    {
        var userId = ProjectVisibility.GetUserId(User);
        if (!await this.CanWriteProjectAsync(_db, projectId, ct))
        {
            _log.LogWarning("ACC token refused for user {User} on project {Project}: read-only role.", userId, projectId);
            return StatusCode(403, new { error = "Your project role is read-only; the shared ACC token is issued to authors only." });
        }

        var (token, error) = await _acc.GetFreshAccessTokenAsync(projectId, ct);
        if (token == null)
            return NotFound(new { message = error ?? "No active ACC connection. Connect ACC for this project first." });

        _log.LogInformation("ACC access token issued to user {User} for project {Project}.", userId, projectId);
        return Ok(new { accessToken = token });
    }

    [HttpGet("hubs")]
    public async Task<IActionResult> Hubs(Guid projectId, CancellationToken ct)
    {
        if (!await this.CanAdministerProjectAsync(_db, projectId, ct)) return Forbidden();
        var (hubs, error) = await _acc.ListHubsAsync(projectId, ct);
        return hubs == null ? StatusCode(502, new { error }) : Ok(hubs);
    }

    [HttpGet("hubs/{hubId}/projects")]
    public async Task<IActionResult> HubProjects(Guid projectId, string hubId, CancellationToken ct)
    {
        if (!await this.CanAdministerProjectAsync(_db, projectId, ct)) return Forbidden();
        var (projects, error) = await _acc.ListHubProjectsAsync(projectId, hubId, ct);
        return projects == null ? StatusCode(502, new { error }) : Ok(projects);
    }

    /// <summary>
    /// Resolve the issue subtype clashes are pushed under. Auto-picks (and, with
    /// store=true, saves) only an unambiguous clash/coordination match; otherwise
    /// returns every active subtype for the user to choose via PUT selection.
    /// </summary>
    [HttpGet("issue-types")]
    public async Task<IActionResult> IssueTypes(Guid projectId, [FromQuery] bool store, CancellationToken ct)
    {
        if (!await this.CanAdministerProjectAsync(_db, projectId, ct)) return Forbidden();
        var (result, error) = await _acc.ResolveIssueTypeAsync(projectId, store, ct);
        return result == null ? StatusCode(502, new { error }) : Ok(result);
    }

    public sealed record AccSelectionRequest(string? HubId, string? AccProjectId, string? Region, string? IssueSubtypeId);

    [HttpPut("selection")]
    public async Task<IActionResult> Selection(Guid projectId, [FromBody] AccSelectionRequest req, CancellationToken ct)
    {
        if (!await this.CanAdministerProjectAsync(_db, projectId, ct)) return Forbidden();
        if (req.Region != null && !AccRegions.IsValid(req.Region))
            return BadRequest(new { error = "invalid_region", allowed = AccRegions.All });
        var error = await _acc.SaveSelectionAsync(projectId, req.HubId, req.AccProjectId, req.Region, req.IssueSubtypeId, ct);
        return error == null ? NoContent() : BadRequest(new { error });
    }

    private ObjectResult Forbidden()
        => StatusCode(403, new { error = "Only a project manager or administrator can configure the ACC connection." });
}

/// <summary>Values APS accepts in the <c>x-ads-region</c> header.</summary>
public static class AccRegions
{
    public static readonly IReadOnlyList<string> All = new[] { "US", "CAN", "EMEA", "GBR", "DEU", "IND", "JPN", "AUS" };
    public static bool IsValid(string r) => r.Length == 0 || All.Contains(r.Trim().ToUpperInvariant());
}
