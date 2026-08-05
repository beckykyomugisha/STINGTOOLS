using System.Security.Cryptography;
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
/// Phase 179 — Time-bounded share-link issuance + public consumption.
///
/// Issuance (auth'd):
///   POST   /api/projects/{pid}/photo-share-links              — mint
///   GET    /api/projects/{pid}/photo-share-links              — list outstanding
///   POST   /api/projects/{pid}/photo-share-links/{id}/revoke  — revoke
///
/// Consumption (anonymous):
///   GET    /api/share/{token}                                 — JSON metadata
///   GET    /api/share/{token}/file                            — image bytes
///   GET    /api/share/{token}/album                           — JSON list of photo metas
/// </summary>
[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/photo-share-links")]
[ProjectAccess]
public class PhotoShareLinkAdminController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IAuditService _audit;

    public PhotoShareLinkAdminController(PlanscapeDbContext db, IAuditService audit)
    {
        _db = db; _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult> List(Guid projectId, CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var rows = await _db.PhotoShareLinks.AsNoTracking()
            .Where(l => l.ProjectId == projectId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult> Create(
        Guid projectId,
        [FromBody] CreateShareLinkRequest req,
        CancellationToken ct = default)
    {
        if (!await IsCuratorAsync(projectId, ct)) return Forbid();
        if (req.PhotoId == null && req.AlbumId == null)
            return BadRequest(new { error = "photo_or_album_required" });
        if (req.PhotoId != null && req.AlbumId != null)
            return BadRequest(new { error = "exactly_one_target" });

        // Defense: target must belong to the same project.
        if (req.PhotoId is { } pid)
        {
            var ok = await _db.SitePhotos.AsNoTracking().AnyAsync(p => p.Id == pid && p.ProjectId == projectId, ct);
            if (!ok) return BadRequest(new { error = "photo_not_in_project" });
        }
        if (req.AlbumId is { } aid)
        {
            var ok = await _db.PhotoAlbums.AsNoTracking().AnyAsync(a => a.Id == aid && a.ProjectId == projectId, ct);
            if (!ok) return BadRequest(new { error = "album_not_in_project" });
        }

        var link = new PhotoShareLink
        {
            TenantId        = GetTenantId(),
            ProjectId       = projectId,
            PhotoId         = req.PhotoId,
            AlbumId         = req.AlbumId,
            Token           = NewToken(),
            Label           = req.Label?.Trim(),
            ExpiresAt       = req.ExpiresAt ?? DateTime.UtcNow.AddDays(14),
            ForceRedacted   = req.ForceRedacted ?? true,
            MaxFetches      = req.MaxFetches,
            CreatedByUserId = CurrentUserIdOrNull(),
        };
        _db.PhotoShareLinks.Add(link);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("CREATE", "PhotoShareLink", link.Id.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new {
                projectId, link.PhotoId, link.AlbumId, link.ExpiresAt
            }));
        return Ok(link);
    }

    [HttpPost("{linkId:guid}/revoke")]
    public async Task<ActionResult> Revoke(
        Guid projectId, Guid linkId,
        CancellationToken ct = default)
    {
        if (!await IsCuratorAsync(projectId, ct)) return Forbid();
        var link = await _db.PhotoShareLinks
            .FirstOrDefaultAsync(l => l.Id == linkId && l.ProjectId == projectId, ct);
        if (link == null) return NotFound();
        if (link.RevokedAt != null) return Ok(link);
        link.RevokedAt       = DateTime.UtcNow;
        link.RevokedByUserId = CurrentUserIdOrNull();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("REVOKE", "PhotoShareLink", link.Id.ToString(), "{}");
        return Ok(link);
    }

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
    private Guid? CurrentUserIdOrNull()
    {
        var s = User.FindFirst("user_id")?.Value
             ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
             ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(s, out var id) ? id : null;
    }
    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;

    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}

/// <summary>
/// Anonymous share-link consumption controller. No auth attribute — the
/// token is the credential. Throttle, expiry, fetch-cap, and revocation
/// are all enforced inside.
/// </summary>
[ApiController]
[Route("api/share/{token}")]
public class PhotoShareLinkPublicController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly INotificationService _notif;

    public PhotoShareLinkPublicController(
        PlanscapeDbContext db,
        IFileStorageService storage,
        INotificationService notif)
    {
        _db = db; _storage = storage; _notif = notif;
    }

    /// <summary>
    /// Phase 180 — push the share-link issuer when the link is opened
    /// for the first time and again on the last allowed fetch (helps
    /// the issuer notice unauthorised forwarding). Fire-and-forget; a
    /// notification failure never breaks the consumer fetch.
    /// </summary>
    private async Task NotifyIssuerOnFirstAndLastFetch(PhotoShareLink link, CancellationToken ct)
    {
        try
        {
            if (link.CreatedByUserId is null) return;
            var isFirst = link.FetchCount == 1;
            var isLast  = link.MaxFetches.HasValue && link.FetchCount == link.MaxFetches.Value;
            if (!isFirst && !isLast) return;
            var label = string.IsNullOrEmpty(link.Label) ? "(unlabeled)" : link.Label!;
            await _notif.NotifyUserAsync(link.CreatedByUserId.Value,
                title: isLast ? "Share link reached fetch cap" : "Share link opened",
                message: isLast
                    ? $"Share link \"{label}\" reached its {link.MaxFetches}-fetch cap."
                    : $"Share link \"{label}\" was opened for the first time.",
                data: new { linkId = link.Id, link.PhotoId, link.AlbumId, link.FetchCount },
                ct: ct);
        }
        catch { /* swallow — public consumer must not see auth-side failures */ }
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult> Meta(string token, CancellationToken ct = default)
    {
        var link = await PeekLinkAsync(token, ct);
        if (link == null) return NotFound();
        if (link.AlbumId.HasValue)
        {
            var album = await _db.PhotoAlbums.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == link.AlbumId.Value, ct);
            return Ok(new {
                kind = "album",
                album?.Name, album?.Description, album?.Kind,
                expiresAt = link.ExpiresAt
            });
        }
        var photo = await _db.SitePhotos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == link.PhotoId!.Value, ct);
        if (photo == null) return NotFound();
        return Ok(new {
            kind = "photo",
            photo.Reason, photo.Caption, photo.LevelCode, photo.ZoneCode,
            photo.CapturedAt,
            expiresAt = link.ExpiresAt,
        });
    }

    [HttpGet("file")]
    [AllowAnonymous]
    public async Task<IActionResult> File(string token, CancellationToken ct = default)
    {
        // Phase 179.3 — atomic increment-then-check via UPDATE … WHERE
        // FetchCount < MaxFetches RETURNING. Two concurrent requests
        // can no longer both see the cap as un-hit.
        var link = await ConsumeOneFetchAsync(token, ct);
        if (link == null) return NotFound();
        await NotifyIssuerOnFirstAndLastFetch(link, ct);
        if (link.AlbumId.HasValue) return BadRequest(new { error = "album_share_use_album_endpoint" });
        var photo = await _db.SitePhotos.AsNoTracking()
            .Include(p => p.Document)
            .FirstOrDefaultAsync(p => p.Id == link.PhotoId!.Value, ct);
        if (photo?.Document?.FilePath == null) return NotFound();

        string path;
        if (link.ForceRedacted)
        {
            if (string.IsNullOrEmpty(photo.RedactedFilePath))
                return StatusCode(503, new { error = "redaction_not_ready" });
            path = photo.RedactedFilePath!;
        }
        else
        {
            path = photo.Document.FilePath!;
        }

        var stream = await _storage.GetAsync(path, ct, bypassTenantCheck: true);
        if (stream == null) return NotFound();
        return File(stream, "image/jpeg", photo.Document.FileName);
    }

    [HttpGet("album")]
    [AllowAnonymous]
    public async Task<ActionResult> Album(string token, CancellationToken ct = default)
    {
        // Phase 179.3 — same atomic increment as /file. Album browses
        // also count toward MaxFetches now so a 10-fetch album can't be
        // browsed unlimited times via this endpoint.
        var link = await ConsumeOneFetchAsync(token, ct);
        if (link == null || !link.AlbumId.HasValue) return NotFound();
        await NotifyIssuerOnFirstAndLastFetch(link, ct);
        var photoIds = await _db.PhotoAlbumPhotos.AsNoTracking()
            .Where(ap => ap.AlbumId == link.AlbumId.Value)
            .OrderBy(ap => ap.SortOrder)
            .Select(ap => ap.PhotoId)
            .ToListAsync(ct);
        var photos = await _db.SitePhotos.AsNoTracking()
            .Where(p => photoIds.Contains(p.Id))
            .Select(p => new {
                p.Id, p.Reason, p.Caption, p.LevelCode, p.ZoneCode,
                p.CapturedAt, p.RedactedFilePath
            })
            .ToListAsync(ct);
        return Ok(photos);
    }

    /// <summary>
    /// Read-only peek for the metadata endpoint. Does NOT increment the
    /// fetch counter. Returns null when the link is revoked, expired,
    /// or already over-cap (so the meta page hides the same as the file).
    /// </summary>
    private async Task<PhotoShareLink?> PeekLinkAsync(string token, CancellationToken ct)
    {
        var link = await _db.PhotoShareLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Token == token, ct);
        if (link == null) return null;
        if (link.RevokedAt != null) return null;
        if (link.ExpiresAt.HasValue && link.ExpiresAt.Value < DateTime.UtcNow) return null;
        if (link.MaxFetches.HasValue && link.FetchCount >= link.MaxFetches.Value) return null;
        return link;
    }

    /// <summary>
    /// Atomically advance the fetch counter for the given token. Returns
    /// the post-update <see cref="PhotoShareLink"/> when the link was
    /// live and within its cap; null when revoked, expired, or
    /// over-cap. Implemented via raw SQL <c>UPDATE … WHERE</c> so two
    /// concurrent requests can never both pass the cap check.
    /// </summary>
    private async Task<PhotoShareLink?> ConsumeOneFetchAsync(string token, CancellationToken ct)
    {
        // Single round-trip: increments FetchCount and returns the row
        // when (RevokedAt IS NULL) AND (ExpiresAt IS NULL OR ExpiresAt > now)
        // AND (MaxFetches IS NULL OR FetchCount < MaxFetches).
        var rows = await _db.Database.SqlQuery<PhotoShareLink>($@"
            UPDATE ""PhotoShareLinks""
            SET ""FetchCount"" = ""FetchCount"" + 1
            WHERE ""Token"" = {token}
              AND ""RevokedAt"" IS NULL
              AND (""ExpiresAt"" IS NULL OR ""ExpiresAt"" > NOW())
              AND (""MaxFetches"" IS NULL OR ""FetchCount"" < ""MaxFetches"")
            RETURNING *;
        ").ToListAsync(ct);
        return rows.FirstOrDefault();
    }
}

public record CreateShareLinkRequest(
    Guid?     PhotoId,
    Guid?     AlbumId,
    string?   Label,
    DateTime? ExpiresAt,
    bool?     ForceRedacted,
    int?      MaxFetches);
