namespace Planscape.API.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;
using Planscape.Infrastructure.SignalR;

/// <remarks>
/// <c>[ProjectAccess]</c> gates reads (404). The two POSTs both mutate or run
/// expensive work against the project's alignment state, so each also takes the
/// member gate (403) — the auto-align POST in particular writes a
/// ProjectModelTransform.
///
/// The auto-align action overrides the class route with an absolute
/// <c>~/api/projects/{projectId:guid}/...</c> template; that template still
/// carries <c>{projectId}</c>, so the attribute resolves it from route data as
/// normal.
/// </remarks>
[ApiController]
[Route("api/projects/{projectId:guid}/alignment")]
[Authorize]
[ProjectAccess]
public class AlignmentController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;

    public AlignmentController(PlanscapeDbContext db, ITenantContext tenant)
    { _db = db; _tenant = tenant; }

    // GET /api/projects/{id}/alignment — all alignment reports for the project
    [HttpGet]
    public async Task<ActionResult> List(Guid projectId, CancellationToken ct)
    {
        var rows = await _db.IfcAlignmentReports.AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.TenantId == _tenant.TenantId)
            .OrderByDescending(r => r.ValidatedAt)
            .ToListAsync(ct);

        return Ok(new {
            count = rows.Count,
            passed = rows.Count(r => r.Verdict == "PASS"),
            warned = rows.Count(r => r.Verdict == "WARN"),
            failed = rows.Count(r => r.Verdict == "FAIL"),
            reports = rows,
        });
    }

    // GET /api/projects/{id}/alignment/model/{modelId}
    [HttpGet("model/{modelId:guid}")]
    public async Task<ActionResult<IfcAlignmentReport>> GetForModel(Guid projectId, Guid modelId, CancellationToken ct)
    {
        var report = await _db.IfcAlignmentReports.AsNoTracking()
            .Where(r => r.ProjectId == projectId && r.ProjectModelId == modelId && r.TenantId == _tenant.TenantId)
            .OrderByDescending(r => r.ValidatedAt)
            .FirstOrDefaultAsync(ct);
        return report == null ? NotFound() : Ok(report);
    }

    // POST /api/projects/{id}/alignment/coherence — run full federated coherence scan
    [HttpPost("coherence")]
    public async Task<ActionResult> RunCoherence(
        Guid projectId,
        [FromServices] IFederatedCoherenceJob coherenceJob,
        CancellationToken ct)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;

        var report = await coherenceJob.RunAsync(projectId, _tenant.TenantId, ct);
        return Ok(report);
    }

    // POST /api/projects/{projectId}/models/{modelId}/alignment/auto-align
    // — suggest or apply an auto-computed coordinate transform
    [HttpPost("~/api/projects/{projectId:guid}/models/{modelId:guid}/alignment/auto-align")]
    public async Task<ActionResult> AutoAlign(
        Guid projectId,
        Guid modelId,
        [FromServices] IAutoAlignService autoAlign,
        [FromServices] IHubContext<FederatedModelHub> modelHub,
        [FromServices] IHubContext<NotificationHub> notificationHub,
        CancellationToken ct)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;

        // #12 — pass both hubs so a successful auto-align broadcasts ModelUpdated:
        // FederatedModelHub for any /hubs/model client + NotificationHub
        // (project-{id}) which is where the dashboard + Revit plugin actually
        // listen. Previously null was passed, so no client ever refreshed.
        var result = await autoAlign.ComputeAsync(
            projectId, _tenant.TenantId, modelId, modelHub, ct, notificationHub);
        return result.Success ? Ok(result) : BadRequest(new { result.Message });
    }
}
