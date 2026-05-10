using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 178b — T2-5. Saved 3D viewer states. Each row is the opaque
/// viewer state JSON (camera + visibility + section + active disciplines)
/// plus optional thumbnail and an optional back-link to a meeting +
/// action item. The viewer's coordination layer captures + restores via
/// `captureViewState()` / `restoreViewState()`.
///
///   GET    /api/projects/{pid}/saved-views               — list (newest first)
///   GET    /api/projects/{pid}/saved-views/{id}          — single
///   POST   /api/projects/{pid}/saved-views               — create
///   POST   /api/projects/{pid}/saved-views/from-action    — create + link to meeting action
///   DELETE /api/projects/{pid}/saved-views/{id}          — delete (creator or PM)
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/saved-views")]
[Authorize]
[ProjectAccess]
public class SavedViewsController : ControllerBase
{
    private const int MaxStateBytes      = 250 * 1024;   // 250 KB JSON cap — viewer state stays small
    private const int MaxThumbnailBytes  = 500 * 1024;   // 500 KB base64 cap — ≈ 360 KB JPEG

    private readonly PlanscapeDbContext _db;
    private readonly IAuditService _audit;

    public SavedViewsController(PlanscapeDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult> List(
        Guid projectId,
        [FromQuery] Guid? meetingId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var q = _db.SavedViews.AsNoTracking().Where(v => v.ProjectId == projectId);
        if (meetingId.HasValue) q = q.Where(v => v.LinkedMeetingId == meetingId.Value);

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(v => v.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new {
                v.Id, v.Name, v.Description, v.ModelId,
                v.CapturedByUserId, v.CapturedByName, v.CreatedAt,
                v.LinkedMeetingId, v.LinkedActionItemId,
                hasThumbnail = v.ThumbnailB64 != null
            })
            .ToListAsync(ct);

        return Ok(new { items = rows, total, page, pageSize });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult> GetOne(Guid projectId, Guid id, CancellationToken ct)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var view = await _db.SavedViews.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id && v.ProjectId == projectId, ct);
        if (view == null) return NotFound();
        return Ok(view);
    }

    [HttpPost]
    public async Task<ActionResult> Create(
        Guid projectId,
        [FromBody] CreateSavedViewRequest req,
        CancellationToken ct)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "name_required" });
        if (req.Name.Length > 120) return BadRequest(new { error = "name_too_long", max = 120 });
        if (string.IsNullOrWhiteSpace(req.StateJson)) return BadRequest(new { error = "state_required" });
        if (System.Text.Encoding.UTF8.GetByteCount(req.StateJson) > MaxStateBytes)
            return BadRequest(new { error = "state_too_large", maxBytes = MaxStateBytes });
        if (req.ThumbnailB64 != null
            && System.Text.Encoding.UTF8.GetByteCount(req.ThumbnailB64) > MaxThumbnailBytes)
            return BadRequest(new { error = "thumbnail_too_large", maxBytes = MaxThumbnailBytes });

        var view = new SavedView
        {
            ProjectId = projectId,
            ModelId = req.ModelId,
            Name = req.Name.Trim(),
            Description = req.Description,
            StateJson = req.StateJson,
            ThumbnailB64 = req.ThumbnailB64,
            CapturedByUserId = CurrentUserIdOrNull(),
            CapturedByName = User.FindFirst("display_name")?.Value ?? "Unknown",
            LinkedMeetingId = req.LinkedMeetingId,
            LinkedActionItemId = req.LinkedActionItemId,
        };
        _db.SavedViews.Add(view);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("CREATE", "SavedView", view.Id.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { view.Name, view.LinkedMeetingId, view.LinkedActionItemId }));
        return CreatedAtAction(nameof(GetOne), new { projectId, id = view.Id }, new { view.Id, view.Name, view.CreatedAt });
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid projectId, Guid id, CancellationToken ct)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var view = await _db.SavedViews.FirstOrDefaultAsync(v => v.Id == id && v.ProjectId == projectId, ct);
        if (view == null) return NotFound();

        // Delete gating: creator can delete their own; PM/Admin/Owner can
        // delete any. Keeps a saved view from disappearing under another
        // member without explicit authority.
        var role = User.FindFirst("role")?.Value ?? "";
        var actor = CurrentUserIdOrNull();
        bool isPrivileged = role is "Admin" or "Owner";
        if (!isPrivileged && view.CapturedByUserId != actor)
        {
            // PM-on-this-project also OK
            isPrivileged = await _db.ProjectMembers.AnyAsync(m =>
                m.ProjectId == projectId && m.UserId == actor && m.IsActive && m.ProjectRole == "PM", ct);
        }
        if (!isPrivileged && view.CapturedByUserId != actor) return Forbid();

        _db.SavedViews.Remove(view);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("DELETE", "SavedView", id.ToString());
        return NoContent();
    }

    private Guid? CurrentUserIdOrNull()
    {
        var s = User.FindFirst("user_id")?.Value
             ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
             ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(s, out var id) ? id : null;
    }
}

public record CreateSavedViewRequest(
    string Name,
    string? Description,
    Guid? ModelId,
    string StateJson,
    string? ThumbnailB64,
    Guid? LinkedMeetingId,
    Guid? LinkedActionItemId);
