using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.API.Authorization;

namespace Planscape.API.Controllers;

/// <summary>
/// S6.2 — 3D markup endpoints. Markups are scene-anchored polylines
/// drawn by coordinators on top of the federated model. Stored as JSON
/// so the viewer can re-render with zero parsing cost.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/markups")]
[Authorize]
[ProjectAccess]
public class ModelMarkupsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;

    public ModelMarkupsController(PlanscapeDbContext db, ITenantContext tenant)
    {
        _db = db; _tenant = tenant;
    }

    [HttpGet]
    public async Task<ActionResult> List(Guid projectId, [FromQuery] Guid? issueId, [FromQuery] Guid? modelId, CancellationToken ct)
    {
        var rows = await _db.ModelMarkups.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.DeletedAt == null
                     && (issueId == null || m.IssueId == issueId)
                     && (modelId == null || m.ModelId == modelId))
            .OrderByDescending(m => m.CreatedAt)
            .Take(500)
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult> Create(Guid projectId, [FromBody] ModelMarkupRequest req, CancellationToken ct)
    {
        // Idempotency: if the client re-sends after a network failure, return
        // the existing row rather than creating a duplicate.
        if (!string.IsNullOrEmpty(req.IdempotencyKey))
        {
            var existing = await _db.ModelMarkups
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.ProjectId == projectId
                                       && m.IdempotencyKey == req.IdempotencyKey, ct);
            if (existing != null) return Ok(new { existing.Id });
        }

        var markup = new ModelMarkup
        {
            TenantId       = _tenant.TenantId,
            ProjectId      = projectId,
            ModelId        = req.ModelId,
            IssueId        = req.IssueId,
            Label          = req.Label,
            Color          = req.Color ?? "#E8912D",
            Thickness      = req.Thickness ?? 2f,
            PolylinesJson  = req.PolylinesJson ?? "[]",
            IdempotencyKey = req.IdempotencyKey,
        };
        _db.ModelMarkups.Add(markup);
        await _db.SaveChangesAsync(ct);
        return Ok(new { markup.Id });
    }

    [HttpDelete("{markupId:guid}")]
    public async Task<ActionResult> Delete(Guid projectId, Guid markupId, CancellationToken ct)
    {
        var markup = await _db.ModelMarkups.FirstOrDefaultAsync(m => m.Id == markupId && m.ProjectId == projectId, ct);
        if (markup == null) return NotFound();
        markup.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public record ModelMarkupRequest(Guid? ModelId, Guid? IssueId, string? Label, string? Color, float? Thickness, string? PolylinesJson, string? IdempotencyKey);
