using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.API.Authorization;

namespace Planscape.API.Controllers;

/// <summary>P3 — Per-document markup overlays.</summary>
[ApiController]
[Route("api/projects/{projectId:guid}/documents/{documentId:guid}/markups")]
[Authorize]
[ProjectAccess]
public class MarkupsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    public MarkupsController(PlanscapeDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult> List(Guid projectId, Guid documentId, CancellationToken ct)
    {
        if (!await DocInTenant(projectId, documentId, ct)) return NotFound();
        var rows = await _db.DocumentMarkups.AsNoTracking()
            .Where(m => m.DocumentId == documentId && m.DeletedAt == null)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult> Create(Guid projectId, Guid documentId,
        [FromBody] UpsertMarkupRequest req, CancellationToken ct)
    {
        if (!await DocInTenant(projectId, documentId, ct)) return NotFound();
        var row = new DocumentMarkup
        {
            DocumentId       = documentId,
            ShapesJson       = req.ShapesJson ?? "[]",
            PageNumber       = Math.Max(1, req.PageNumber ?? 1),
            Summary          = req.Summary,
            PreviousMarkupId = req.PreviousMarkupId,
            CreatedByUserId  = CurrentUserId(),
            CreatedByName    = User.FindFirst("display_name")?.Value ?? User.Identity?.Name ?? "Unknown",
        };
        _db.DocumentMarkups.Add(row);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(List), new { projectId, documentId }, row);
    }

    [HttpPut("{markupId:guid}")]
    public async Task<ActionResult> Update(Guid projectId, Guid documentId, Guid markupId,
        [FromBody] UpsertMarkupRequest req, CancellationToken ct)
    {
        if (!await DocInTenant(projectId, documentId, ct)) return NotFound();
        var row = await _db.DocumentMarkups.FirstOrDefaultAsync(m =>
            m.Id == markupId && m.DocumentId == documentId && m.DeletedAt == null, ct);
        if (row == null) return NotFound();
        if (row.CreatedByUserId != CurrentUserId() && !User.IsInRole("Admin") && !User.IsInRole("Owner"))
            return Forbid();

        row.ShapesJson = req.ShapesJson ?? row.ShapesJson;
        row.Summary = req.Summary ?? row.Summary;
        row.PageNumber = req.PageNumber ?? row.PageNumber;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(row);
    }

    [HttpDelete("{markupId:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, Guid documentId, Guid markupId, CancellationToken ct)
    {
        if (!await DocInTenant(projectId, documentId, ct)) return NotFound();
        var row = await _db.DocumentMarkups.FirstOrDefaultAsync(m =>
            m.Id == markupId && m.DocumentId == documentId && m.DeletedAt == null, ct);
        if (row == null) return NotFound();
        row.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<bool> DocInTenant(Guid projectId, Guid documentId, CancellationToken ct)
    {
        var tenantId = Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
        if (tenantId == Guid.Empty) return false;
        return await _db.Documents.AsNoTracking().AnyAsync(d =>
            d.Id == documentId && d.ProjectId == projectId && d.Project!.TenantId == tenantId, ct);
    }

    private Guid? CurrentUserId() =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : null;
}

public record UpsertMarkupRequest(
    string? ShapesJson,
    int? PageNumber,
    string? Summary,
    Guid? PreviousMarkupId);
