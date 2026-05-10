using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 179 — Photo album CRUD + membership + per-album visibility.
///
///   GET    /api/projects/{pid}/photo-albums              — list albums caller can see
///   POST   /api/projects/{pid}/photo-albums              — create (author only)
///   GET    /api/projects/{pid}/photo-albums/{aid}        — single album + photo ids
///   PUT    /api/projects/{pid}/photo-albums/{aid}        — rename / re-target / re-cover
///   DELETE /api/projects/{pid}/photo-albums/{aid}        — soft = unlock+empty; hard = drop row
///   POST   /api/projects/{pid}/photo-albums/{aid}/photos — add a batch of photo ids
///   DELETE /api/projects/{pid}/photo-albums/{aid}/photos/{photoId}  — remove one
///   POST   /api/projects/{pid}/photo-albums/{aid}/lock   — freeze membership (author only)
///   POST   /api/projects/{pid}/photo-albums/{aid}/unlock — reopen for edits (author only)
///   POST   /api/projects/{pid}/photo-albums/{aid}/reorder — bulk SortOrder rewrite
///
/// Mutation gate: tenant Admin / Owner OR project PM. Read gate: project
/// member, AND-ed with the album's visibility (Internal / Members /
/// Client / Distribution).
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/photo-albums")]
[Authorize]
[ProjectAccess]
public class PhotoAlbumsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IAuditService _audit;
    private readonly IHubContext<NotificationHub> _hub;

    public PhotoAlbumsController(
        PlanscapeDbContext db,
        IAuditService audit,
        IHubContext<NotificationHub> hub)
    {
        _db = db;
        _audit = audit;
        _hub = hub;
    }

    // ── GET / list ────────────────────────────────────────────────────
    [HttpGet]
    public async Task<ActionResult> List(
        Guid projectId,
        [FromQuery] string? kind,
        [FromQuery] string? visibility,
        CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var q = _db.PhotoAlbums.AsNoTracking().Where(a => a.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(kind)) q = q.Where(a => a.Kind == kind);
        if (!string.IsNullOrWhiteSpace(visibility)) q = q.Where(a => a.Visibility == visibility);

        var rows = await (
            from a in q
            join dg in _db.DistributionGroups.AsNoTracking() on a.DistributionGroupId equals dg.Id into dgg
            from dg in dgg.DefaultIfEmpty()
            select new {
                a.Id, a.ProjectId, a.Name, a.Description, a.Visibility, a.Kind,
                a.DistributionGroupId,
                DistributionGroupName = dg != null ? dg.Name : null,
                a.CoverPhotoId, a.IsLocked, a.LockedAt, a.AutoArchiveAfterDays,
                a.CreatedAt, a.CreatedByUserId, a.UpdatedAt
            })
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        // Cheap photo-count probe — single GROUP BY query, not n+1.
        var counts = await _db.PhotoAlbumPhotos.AsNoTracking()
            .Where(ap => rows.Select(r => r.Id).Contains(ap.AlbumId))
            .GroupBy(ap => ap.AlbumId)
            .Select(g => new { AlbumId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var countMap = counts.ToDictionary(c => c.AlbumId, c => c.Count);

        return Ok(rows.Select(r => new {
            r.Id, r.ProjectId, r.Name, r.Description, r.Visibility, r.Kind,
            r.DistributionGroupId, r.DistributionGroupName,
            r.CoverPhotoId, r.IsLocked, r.LockedAt, r.AutoArchiveAfterDays,
            r.CreatedAt, r.CreatedByUserId, r.UpdatedAt,
            PhotoCount = countMap.GetValueOrDefault(r.Id, 0)
        }));
    }

    // ── POST / create ─────────────────────────────────────────────────
    [HttpPost]
    public async Task<ActionResult> Create(
        Guid projectId,
        [FromBody] CreateAlbumRequest req,
        CancellationToken ct = default)
    {
        if (!await IsCuratorAsync(projectId, ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "name_required" });
        if (!PhotoAlbum.ValidVisibilities.Contains(req.Visibility ?? "Members"))
            return BadRequest(new { error = "invalid_visibility", allowed = PhotoAlbum.ValidVisibilities });

        var tenantId = GetTenantId();
        var album = new PhotoAlbum
        {
            TenantId            = tenantId,
            ProjectId           = projectId,
            Name                = req.Name.Trim(),
            Description         = req.Description?.Trim(),
            Visibility          = req.Visibility ?? "Members",
            DistributionGroupId = req.DistributionGroupId,
            Kind                = string.IsNullOrWhiteSpace(req.Kind) ? null : req.Kind.Trim(),
            AutoArchiveAfterDays = req.AutoArchiveAfterDays,
            CreatedByUserId     = CurrentUserIdOrNull(),
        };
        _db.PhotoAlbums.Add(album);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("CREATE", "PhotoAlbum", album.Id.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { projectId, album.Name, album.Visibility, album.Kind }));
        return CreatedAtAction(nameof(GetOne), new { projectId, albumId = album.Id }, album);
    }

    // ── GET / single ──────────────────────────────────────────────────
    [HttpGet("{albumId:guid}")]
    public async Task<ActionResult> GetOne(Guid projectId, Guid albumId, CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var album = await _db.PhotoAlbums.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == albumId && a.ProjectId == projectId, ct);
        if (album == null) return NotFound();
        if (!await CanViewAlbumAsync(album, ct)) return Forbid();

        var raw = await _db.PhotoAlbumPhotos.AsNoTracking()
            .Where(ap => ap.AlbumId == albumId)
            .OrderBy(ap => ap.SortOrder).ThenBy(ap => ap.AddedAt)
            .Select(ap => new { ap.PhotoId, ap.SortOrder, ap.AddedAt })
            .ToListAsync(ct);

        // Phase 179.3 — pipe the photo ids through PhotoAclGate so
        // per-photo PhotoAccessRule rows are honoured here too.
        // Without this, putting a strictly-ACL'd photo in a Members-
        // visible album would defeat the rule.
        var probe   = await PhotoAclGate.ResolveProbeAsync(_db, projectId, User, ct);
        var allIds  = raw.Select(r => r.PhotoId).ToList();
        var visible = await PhotoAclGate.FilterVisibleAsync(_db, allIds, probe, ct);
        var ndaPending = await PhotoAclGate.NdaRequiredAsync(_db, allIds, probe, ct);
        var photos = raw
            .Where(r => visible.Contains(r.PhotoId) || ndaPending.Contains(r.PhotoId))
            .ToList();

        return Ok(new {
            album, photos,
            ndaRequiredIds = ndaPending.ToArray(),
        });
    }

    // ── PUT / update ──────────────────────────────────────────────────
    [HttpPut("{albumId:guid}")]
    public async Task<ActionResult> Update(
        Guid projectId, Guid albumId,
        [FromBody] UpdateAlbumRequest req,
        CancellationToken ct = default)
    {
        if (!await IsCuratorAsync(projectId, ct)) return Forbid();
        var album = await _db.PhotoAlbums
            .FirstOrDefaultAsync(a => a.Id == albumId && a.ProjectId == projectId, ct);
        if (album == null) return NotFound();
        if (album.IsLocked) return BadRequest(new { error = "album_locked" });

        if (req.Name != null) album.Name = req.Name.Trim();
        if (req.Description != null) album.Description = req.Description.Trim();
        if (req.Visibility != null)
        {
            if (!PhotoAlbum.ValidVisibilities.Contains(req.Visibility))
                return BadRequest(new { error = "invalid_visibility", allowed = PhotoAlbum.ValidVisibilities });
            album.Visibility = req.Visibility;
        }
        if (req.DistributionGroupId.HasValue) album.DistributionGroupId = req.DistributionGroupId.Value;
        if (req.ClearDistributionGroup == true) album.DistributionGroupId = null;
        if (req.Kind != null) album.Kind = string.IsNullOrWhiteSpace(req.Kind) ? null : req.Kind.Trim();
        if (req.CoverPhotoId.HasValue) album.CoverPhotoId = req.CoverPhotoId.Value;
        if (req.AutoArchiveAfterDays.HasValue) album.AutoArchiveAfterDays = req.AutoArchiveAfterDays.Value;
        album.UpdatedAt = DateTime.UtcNow;
        album.UpdatedByUserId = CurrentUserIdOrNull();

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("UPDATE", "PhotoAlbum", album.Id.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { req.Name, req.Visibility, req.DistributionGroupId }));
        return Ok(album);
    }

    // ── DELETE ────────────────────────────────────────────────────────
    [HttpDelete("{albumId:guid}")]
    public async Task<ActionResult> Delete(
        Guid projectId, Guid albumId,
        [FromQuery] bool hard = false,
        CancellationToken ct = default)
    {
        if (!await IsCuratorAsync(projectId, ct)) return Forbid();
        var album = await _db.PhotoAlbums
            .FirstOrDefaultAsync(a => a.Id == albumId && a.ProjectId == projectId, ct);
        if (album == null) return NotFound();

        if (hard)
        {
            _db.PhotoAlbums.Remove(album);
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync("DELETE", "PhotoAlbum", album.Id.ToString(), "{\"hard\":true}");
            return NoContent();
        }

        // Soft: unlock and clear photos, keep the row for audit history.
        album.IsLocked = false;
        var members = await _db.PhotoAlbumPhotos.Where(ap => ap.AlbumId == albumId).ToListAsync(ct);
        _db.PhotoAlbumPhotos.RemoveRange(members);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("EMPTY", "PhotoAlbum", album.Id.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { removed = members.Count }));
        return NoContent();
    }

    // ── POST /photos — add batch ─────────────────────────────────────
    [HttpPost("{albumId:guid}/photos")]
    public async Task<ActionResult> AddPhotos(
        Guid projectId, Guid albumId,
        [FromBody] AddPhotosRequest req,
        CancellationToken ct = default)
    {
        if (!await IsCuratorAsync(projectId, ct)) return Forbid();
        if (req.PhotoIds == null || req.PhotoIds.Length == 0) return BadRequest(new { error = "ids_required" });
        if (req.PhotoIds.Length > 500) return BadRequest(new { error = "batch_too_large", max = 500 });

        var album = await _db.PhotoAlbums
            .FirstOrDefaultAsync(a => a.Id == albumId && a.ProjectId == projectId, ct);
        if (album == null) return NotFound();
        if (album.IsLocked) return BadRequest(new { error = "album_locked" });

        // Defense: every photo must belong to the same project.
        var idSet = new HashSet<Guid>(req.PhotoIds);
        var validIds = await _db.SitePhotos.AsNoTracking()
            .Where(p => p.ProjectId == projectId && idSet.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(ct);

        var existing = await _db.PhotoAlbumPhotos
            .Where(ap => ap.AlbumId == albumId && validIds.Contains(ap.PhotoId))
            .Select(ap => ap.PhotoId)
            .ToListAsync(ct);
        var existingSet = new HashSet<Guid>(existing);
        var nextSort = await _db.PhotoAlbumPhotos
            .Where(ap => ap.AlbumId == albumId)
            .Select(ap => (int?)ap.SortOrder)
            .MaxAsync(ct) ?? 0;

        int added = 0;
        var actorId = CurrentUserIdOrNull();
        foreach (var id in validIds)
        {
            if (existingSet.Contains(id)) continue;
            nextSort += 100;
            _db.PhotoAlbumPhotos.Add(new PhotoAlbumPhoto
            {
                AlbumId       = albumId,
                PhotoId       = id,
                SortOrder     = nextSort,
                AddedByUserId = actorId,
            });
            added++;
        }

        if (album.CoverPhotoId == null && validIds.Count > 0)
            album.CoverPhotoId = validIds[0];
        album.UpdatedAt = DateTime.UtcNow;
        album.UpdatedByUserId = actorId;

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("ADD_PHOTOS", "PhotoAlbum", albumId.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new {
                requested = req.PhotoIds.Length, valid = validIds.Count, added,
                duplicates = validIds.Count - added,
                missing = req.PhotoIds.Length - validIds.Count
            }));

        _ = _hub.Clients.Group($"project-{projectId}").SendAsync("PhotoAlbumChanged", new { projectId, albumId, added }, ct);
        return Ok(new { added, total = await _db.PhotoAlbumPhotos.CountAsync(ap => ap.AlbumId == albumId, ct) });
    }

    // ── DELETE /photos/{id} — remove one ─────────────────────────────
    [HttpDelete("{albumId:guid}/photos/{photoId:guid}")]
    public async Task<ActionResult> RemovePhoto(
        Guid projectId, Guid albumId, Guid photoId,
        CancellationToken ct = default)
    {
        if (!await IsCuratorAsync(projectId, ct)) return Forbid();
        var album = await _db.PhotoAlbums
            .FirstOrDefaultAsync(a => a.Id == albumId && a.ProjectId == projectId, ct);
        if (album == null) return NotFound();
        if (album.IsLocked) return BadRequest(new { error = "album_locked" });

        var ap = await _db.PhotoAlbumPhotos.FirstOrDefaultAsync(x => x.AlbumId == albumId && x.PhotoId == photoId, ct);
        if (ap == null) return NotFound();

        _db.PhotoAlbumPhotos.Remove(ap);
        if (album.CoverPhotoId == photoId) album.CoverPhotoId = null;
        album.UpdatedAt = DateTime.UtcNow;
        album.UpdatedByUserId = CurrentUserIdOrNull();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("REMOVE_PHOTO", "PhotoAlbum", albumId.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { photoId }));
        return NoContent();
    }

    // ── POST /lock + /unlock ─────────────────────────────────────────
    [HttpPost("{albumId:guid}/lock")]
    public async Task<ActionResult> Lock(Guid projectId, Guid albumId, CancellationToken ct = default)
        => await SetLockAsync(projectId, albumId, locked: true, ct);

    [HttpPost("{albumId:guid}/unlock")]
    public async Task<ActionResult> Unlock(Guid projectId, Guid albumId, CancellationToken ct = default)
        => await SetLockAsync(projectId, albumId, locked: false, ct);

    private async Task<ActionResult> SetLockAsync(Guid projectId, Guid albumId, bool locked, CancellationToken ct)
    {
        if (!await IsCuratorAsync(projectId, ct)) return Forbid();
        var album = await _db.PhotoAlbums
            .FirstOrDefaultAsync(a => a.Id == albumId && a.ProjectId == projectId, ct);
        if (album == null) return NotFound();

        var actorId = CurrentUserIdOrNull();
        album.IsLocked       = locked;
        album.LockedAt       = locked ? DateTime.UtcNow : null;
        album.LockedByUserId = locked ? actorId : null;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(locked ? "LOCK" : "UNLOCK", "PhotoAlbum", albumId.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { actor = actorId }));
        return Ok(album);
    }

    // ── POST /reorder — bulk SortOrder rewrite ───────────────────────
    [HttpPost("{albumId:guid}/reorder")]
    public async Task<ActionResult> Reorder(
        Guid projectId, Guid albumId,
        [FromBody] ReorderRequest req,
        CancellationToken ct = default)
    {
        if (!await IsCuratorAsync(projectId, ct)) return Forbid();
        var album = await _db.PhotoAlbums
            .FirstOrDefaultAsync(a => a.Id == albumId && a.ProjectId == projectId, ct);
        if (album == null) return NotFound();
        if (album.IsLocked) return BadRequest(new { error = "album_locked" });
        if (req.Order == null || req.Order.Length == 0) return BadRequest(new { error = "order_required" });

        var rows = await _db.PhotoAlbumPhotos.Where(ap => ap.AlbumId == albumId).ToListAsync(ct);
        var byId = rows.ToDictionary(r => r.PhotoId);
        int n = 0;
        foreach (var photoId in req.Order)
        {
            n += 100;
            if (byId.TryGetValue(photoId, out var ap)) ap.SortOrder = n;
        }
        album.UpdatedAt = DateTime.UtcNow;
        album.UpdatedByUserId = CurrentUserIdOrNull();
        await _db.SaveChangesAsync(ct);
        return Ok(new { reordered = req.Order.Length });
    }

    // ── helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Mutation gate (matches SitePhotosController.IsApproverAsync):
    /// tenant Admin / Owner OR project PM.
    /// </summary>
    private async Task<bool> IsCuratorAsync(Guid projectId, CancellationToken ct)
    {
        var role = User.FindFirst("role")?.Value ?? "";
        if (role is "Admin" or "Owner") return true;
        var userId = CurrentUserIdOrNull();
        if (userId == null) return false;
        return await _db.ProjectMembers.AsNoTracking().AnyAsync(m =>
            m.ProjectId == projectId && m.UserId == userId.Value &&
            m.IsActive && m.ProjectRole == "PM", ct);
    }

    /// <summary>
    /// Read gate: project member always sees Internal/Members; Client
    /// audience also exposes to ClientGuest; Distribution audience needs
    /// membership of the linked group (matched by user-id OR by the
    /// caller's verified email claim for external recipients).
    /// </summary>
    private async Task<bool> CanViewAlbumAsync(PhotoAlbum album, CancellationToken ct)
    {
        var role = User.FindFirst("role")?.Value ?? "";
        if (role is "Admin" or "Owner") return true;
        // Internal / Members: any project member already passed the
        // RequireProjectMemberAsync gate above so we only need to gate
        // out ClientGuest principals here.
        if (album.Visibility == "Internal") return role != "ClientGuest";
        if (album.Visibility == "Members")  return role != "ClientGuest";
        // Client visibility = project members AND ClientGuest readers.
        if (album.Visibility == "Client") return true;
        if (album.Visibility == "Distribution")
        {
            if (!album.DistributionGroupId.HasValue) return false;
            var userId = CurrentUserIdOrNull();
            var email  = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                       ?? User.FindFirst("email")?.Value;
            return await _db.DistributionGroupMembers.AsNoTracking()
                .AnyAsync(m => m.DistributionGroupId == album.DistributionGroupId.Value &&
                    ((userId.HasValue && m.UserId == userId.Value) ||
                     (email != null && m.ExternalEmail != null &&
                      m.ExternalEmail.ToLower() == email.ToLower())), ct);
        }
        return false;
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

// ── DTOs ──────────────────────────────────────────────────────────────

public record CreateAlbumRequest(
    string Name,
    string? Description,
    string? Visibility,            // Internal | Members | Client | Distribution
    Guid?   DistributionGroupId,
    string? Kind,
    int?    AutoArchiveAfterDays);

public record UpdateAlbumRequest(
    string? Name,
    string? Description,
    string? Visibility,
    Guid?   DistributionGroupId,
    bool?   ClearDistributionGroup,
    string? Kind,
    Guid?   CoverPhotoId,
    int?    AutoArchiveAfterDays);

public record AddPhotosRequest(Guid[] PhotoIds);
public record ReorderRequest(Guid[] Order);
