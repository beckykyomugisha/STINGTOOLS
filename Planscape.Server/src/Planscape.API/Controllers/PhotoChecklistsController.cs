using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 179 — Photo checklist authoring + fulfilment.
///
///   GET    /api/projects/{pid}/photo-checklists                      — list
///   POST   /api/projects/{pid}/photo-checklists                      — create (author)
///   GET    /api/projects/{pid}/photo-checklists/{cid}                — single + items
///   PUT    /api/projects/{pid}/photo-checklists/{cid}                — rename / re-status
///   DELETE /api/projects/{pid}/photo-checklists/{cid}                — drop row
///   POST   /api/projects/{pid}/photo-checklists/{cid}/items          — add item
///   PUT    /api/projects/{pid}/photo-checklists/{cid}/items/{iid}    — edit item
///   DELETE /api/projects/{pid}/photo-checklists/{cid}/items/{iid}    — remove
///   POST   /api/projects/{pid}/photo-checklists/{cid}/items/{iid}/fulfil    — link a photo
///   POST   /api/projects/{pid}/photo-checklists/{cid}/items/{iid}/waive     — waive (author)
///   POST   /api/projects/{pid}/photo-checklists/{cid}/close          — close once all complete
///
/// Author gate: tenant Admin / Owner OR project PM.
/// Fulfil gate: any active project member.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/photo-checklists")]
[Authorize]
[ProjectAccess]
public class PhotoChecklistsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IAuditService _audit;
    private readonly INotificationService _notif;

    public PhotoChecklistsController(
        PlanscapeDbContext db,
        IAuditService audit,
        INotificationService notif)
    {
        _db = db; _audit = audit; _notif = notif;
    }

    [HttpGet]
    public async Task<ActionResult> List(
        Guid projectId,
        [FromQuery] string? status,
        [FromQuery] string? kind,
        CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var q = _db.PhotoChecklists.AsNoTracking().Where(c => c.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(c => c.Status == status);
        if (!string.IsNullOrWhiteSpace(kind)) q = q.Where(c => c.Kind == kind);
        var rows = await q.OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

        var ids = rows.Select(c => c.Id).ToList();
        var counts = await _db.PhotoChecklistItems.AsNoTracking()
            .Where(i => ids.Contains(i.ChecklistId))
            .GroupBy(i => i.ChecklistId)
            .Select(g => new {
                ChecklistId = g.Key,
                Total = g.Count(),
                Done  = g.Count(x => x.FulfilledByPhotoId != null || x.IsWaived),
                Required = g.Count(x => x.IsRequired && !x.IsWaived)
            }).ToListAsync(ct);
        var countMap = counts.ToDictionary(c => c.ChecklistId);
        return Ok(rows.Select(r => new {
            r.Id, r.ProjectId, r.Name, r.Description, r.Kind, r.Status,
            r.LevelCode, r.ZoneCode, r.WorkPackageId, r.DueAt,
            r.CreatedAt, r.CreatedByUserId, r.ClosedAt,
            Total = countMap.GetValueOrDefault(r.Id)?.Total ?? 0,
            Done  = countMap.GetValueOrDefault(r.Id)?.Done ?? 0,
        }));
    }

    [HttpPost]
    public async Task<ActionResult> Create(
        Guid projectId,
        [FromBody] CreateChecklistRequest req,
        CancellationToken ct = default)
    {
        if (!await IsAuthorAsync(projectId, ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "name_required" });
        if (req.Kind != null && !PhotoChecklist.ValidKinds.Contains(req.Kind))
            return BadRequest(new { error = "invalid_kind", allowed = PhotoChecklist.ValidKinds });

        var c = new PhotoChecklist
        {
            TenantId        = GetTenantId(),
            ProjectId       = projectId,
            Name            = req.Name.Trim(),
            Description     = req.Description?.Trim(),
            Kind            = req.Kind ?? "Custom",
            Status          = "Draft",
            LevelCode       = req.LevelCode,
            ZoneCode        = req.ZoneCode,
            WorkPackageId   = req.WorkPackageId,
            DueAt           = req.DueAt,
            CreatedByUserId = CurrentUserIdOrNull(),
        };
        _db.PhotoChecklists.Add(c);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("CREATE", "PhotoChecklist", c.Id.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { projectId, c.Name, c.Kind }));
        return CreatedAtAction(nameof(GetOne), new { projectId, checklistId = c.Id }, c);
    }

    [HttpGet("{checklistId:guid}")]
    public async Task<ActionResult> GetOne(Guid projectId, Guid checklistId, CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var c = await _db.PhotoChecklists.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == checklistId && x.ProjectId == projectId, ct);
        if (c == null) return NotFound();
        var items = await _db.PhotoChecklistItems.AsNoTracking()
            .Where(i => i.ChecklistId == checklistId)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Title)
            .ToListAsync(ct);
        return Ok(new { checklist = c, items });
    }

    [HttpPut("{checklistId:guid}")]
    public async Task<ActionResult> Update(
        Guid projectId, Guid checklistId,
        [FromBody] UpdateChecklistRequest req,
        CancellationToken ct = default)
    {
        if (!await IsAuthorAsync(projectId, ct)) return Forbid();
        var c = await _db.PhotoChecklists
            .FirstOrDefaultAsync(x => x.Id == checklistId && x.ProjectId == projectId, ct);
        if (c == null) return NotFound();

        if (req.Name != null) c.Name = req.Name.Trim();
        if (req.Description != null) c.Description = req.Description.Trim();
        if (req.Kind != null && PhotoChecklist.ValidKinds.Contains(req.Kind)) c.Kind = req.Kind;
        if (req.Status != null && PhotoChecklist.ValidStatuses.Contains(req.Status)) c.Status = req.Status;
        if (req.LevelCode != null) c.LevelCode = req.LevelCode;
        if (req.ZoneCode != null) c.ZoneCode = req.ZoneCode;
        if (req.DueAt.HasValue) c.DueAt = req.DueAt;
        await _db.SaveChangesAsync(ct);
        return Ok(c);
    }

    [HttpDelete("{checklistId:guid}")]
    public async Task<ActionResult> Delete(Guid projectId, Guid checklistId, CancellationToken ct = default)
    {
        if (!await IsAuthorAsync(projectId, ct)) return Forbid();
        var c = await _db.PhotoChecklists
            .FirstOrDefaultAsync(x => x.Id == checklistId && x.ProjectId == projectId, ct);
        if (c == null) return NotFound();
        _db.PhotoChecklists.Remove(c);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{checklistId:guid}/items")]
    public async Task<ActionResult> AddItem(
        Guid projectId, Guid checklistId,
        [FromBody] AddChecklistItemRequest req,
        CancellationToken ct = default)
    {
        if (!await IsAuthorAsync(projectId, ct)) return Forbid();
        var c = await _db.PhotoChecklists.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == checklistId && x.ProjectId == projectId, ct);
        if (c == null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest(new { error = "title_required" });

        var nextSort = (await _db.PhotoChecklistItems
            .Where(i => i.ChecklistId == checklistId)
            .Select(i => (int?)i.SortOrder).MaxAsync(ct) ?? 0) + 100;

        var item = new PhotoChecklistItem
        {
            ChecklistId = checklistId,
            Title       = req.Title.Trim(),
            Description = req.Description?.Trim(),
            SortOrder   = nextSort,
            DefaultReason = req.DefaultReason ?? "Reference",
            IsRequired  = req.IsRequired ?? true,
        };
        _db.PhotoChecklistItems.Add(item);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetOne), new { projectId, checklistId }, item);
    }

    [HttpPut("{checklistId:guid}/items/{itemId:guid}")]
    public async Task<ActionResult> UpdateItem(
        Guid projectId, Guid checklistId, Guid itemId,
        [FromBody] UpdateChecklistItemRequest req,
        CancellationToken ct = default)
    {
        if (!await IsAuthorAsync(projectId, ct)) return Forbid();
        var item = await _db.PhotoChecklistItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.ChecklistId == checklistId, ct);
        if (item == null) return NotFound();
        if (req.Title != null) item.Title = req.Title.Trim();
        if (req.Description != null) item.Description = req.Description.Trim();
        if (req.DefaultReason != null) item.DefaultReason = req.DefaultReason;
        if (req.IsRequired.HasValue) item.IsRequired = req.IsRequired.Value;
        if (req.SortOrder.HasValue) item.SortOrder = req.SortOrder.Value;
        await _db.SaveChangesAsync(ct);
        return Ok(item);
    }

    [HttpDelete("{checklistId:guid}/items/{itemId:guid}")]
    public async Task<ActionResult> DeleteItem(
        Guid projectId, Guid checklistId, Guid itemId,
        CancellationToken ct = default)
    {
        if (!await IsAuthorAsync(projectId, ct)) return Forbid();
        var item = await _db.PhotoChecklistItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.ChecklistId == checklistId, ct);
        if (item == null) return NotFound();
        _db.PhotoChecklistItems.Remove(item);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{checklistId:guid}/items/{itemId:guid}/fulfil")]
    public async Task<ActionResult> Fulfil(
        Guid projectId, Guid checklistId, Guid itemId,
        [FromBody] FulfilItemRequest req,
        CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var item = await _db.PhotoChecklistItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.ChecklistId == checklistId, ct);
        if (item == null) return NotFound();

        var photo = await _db.SitePhotos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == req.PhotoId && p.ProjectId == projectId, ct);
        if (photo == null) return BadRequest(new { error = "photo_not_in_project" });

        var prev = item.FulfilledByPhotoId;
        item.FulfilledByPhotoId = req.PhotoId;
        item.FulfilledAt        = DateTime.UtcNow;
        item.FulfilledByUserId  = CurrentUserIdOrNull();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("FULFIL", "PhotoChecklistItem", itemId.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { fromPhotoId = prev, toPhotoId = req.PhotoId }));

        // Phase 180 — notify the checklist author so they can see
        // progress without polling. Only on first fulfilment to avoid
        // notification spam when a coordinator re-links to a better photo.
        if (prev is null)
        {
            var checklist = await _db.PhotoChecklists.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == checklistId, ct);
            if (checklist?.CreatedByUserId is { } authorId &&
                authorId != CurrentUserIdOrNull())
            {
                try
                {
                    await _notif.NotifyUserAsync(authorId,
                        title: $"Checklist item fulfilled",
                        message: $"\"{item.Title}\" was fulfilled in {checklist.Name}.",
                        data: new { projectId, checklistId, itemId, photoId = req.PhotoId },
                        ct: ct);
                }
                catch { /* notification failure must not break fulfilment */ }
            }
        }
        return Ok(item);
    }

    [HttpPost("{checklistId:guid}/items/{itemId:guid}/waive")]
    public async Task<ActionResult> Waive(
        Guid projectId, Guid checklistId, Guid itemId,
        [FromBody] WaiveItemRequest req,
        CancellationToken ct = default)
    {
        if (!await IsAuthorAsync(projectId, ct)) return Forbid();
        var item = await _db.PhotoChecklistItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.ChecklistId == checklistId, ct);
        if (item == null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Reason)) return BadRequest(new { error = "reason_required" });
        item.IsWaived     = true;
        item.WaivedReason = req.Reason.Trim();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WAIVE", "PhotoChecklistItem", itemId.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { reason = item.WaivedReason }));
        return Ok(item);
    }

    [HttpPost("{checklistId:guid}/close")]
    public async Task<ActionResult> Close(
        Guid projectId, Guid checklistId,
        CancellationToken ct = default)
    {
        if (!await IsAuthorAsync(projectId, ct)) return Forbid();
        var c = await _db.PhotoChecklists
            .FirstOrDefaultAsync(x => x.Id == checklistId && x.ProjectId == projectId, ct);
        if (c == null) return NotFound();

        var pending = await _db.PhotoChecklistItems
            .CountAsync(i => i.ChecklistId == checklistId && i.IsRequired && !i.IsWaived && i.FulfilledByPhotoId == null, ct);
        if (pending > 0) return BadRequest(new { error = "items_pending", pending });

        c.Status         = "Closed";
        c.ClosedAt       = DateTime.UtcNow;
        c.ClosedByUserId = CurrentUserIdOrNull();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("CLOSE", "PhotoChecklist", c.Id.ToString(), "{}");
        return Ok(c);
    }

    private async Task<bool> IsAuthorAsync(Guid projectId, CancellationToken ct)
    {
        var role = User.FindFirst("role")?.Value ?? "";
        if (role is "Admin" or "Owner") return true;
        var userId = CurrentUserIdOrNull();
        if (userId == null) return false;
        return await _db.ProjectMembers.AsNoTracking().AnyAsync(m =>
            m.ProjectId == projectId && m.UserId == userId.Value &&
            m.IsActive && m.ProjectRole == "PM", ct);
    }
    private Guid? CurrentUserIdOrNull()
    {
        var s = User.FindFirst("user_id")?.Value
             ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
             ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(s, out var id) ? id : null;
    }
    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record CreateChecklistRequest(
    string  Name,
    string? Description,
    string? Kind,
    string? LevelCode,
    string? ZoneCode,
    Guid?   WorkPackageId,
    DateTime? DueAt);

public record UpdateChecklistRequest(
    string? Name,
    string? Description,
    string? Kind,
    string? Status,
    string? LevelCode,
    string? ZoneCode,
    DateTime? DueAt);

public record AddChecklistItemRequest(
    string  Title,
    string? Description,
    string? DefaultReason,
    bool?   IsRequired);

public record UpdateChecklistItemRequest(
    string? Title,
    string? Description,
    string? DefaultReason,
    bool?   IsRequired,
    int?    SortOrder);

public record FulfilItemRequest(Guid PhotoId);
public record WaiveItemRequest(string Reason);
