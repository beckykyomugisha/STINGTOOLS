namespace Planscape.API.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

[ApiController]
[Route("api/projects/{projectId:guid}/alignment")]
[Authorize]
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
        CancellationToken ct)
    {
        // ComputeAsync's 4th parameter is the optional IHubContext for broadcasting
        // progress events; AutoAlign from this endpoint doesn't broadcast, so pass null.
        var result = await autoAlign.ComputeAsync(projectId, _tenant.TenantId, modelId, null, ct);
        return result.Success ? Ok(result) : BadRequest(new { result.Message });
    }
}
