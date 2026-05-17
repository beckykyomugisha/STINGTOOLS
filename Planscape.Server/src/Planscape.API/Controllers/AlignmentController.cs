namespace Planscape.API.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

[ApiController]
[Route("api/projects/{projectId:guid}/alignment")]
[Authorize]
public class AlignmentController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IIfcAlignmentValidator _validator;

    public AlignmentController(PlanscapeDbContext db, ITenantContext tenant, IIfcAlignmentValidator validator)
    { _db = db; _tenant = tenant; _validator = validator; }

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
}
