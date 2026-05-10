using System.Security.Cryptography;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 179 — Extension surface for the existing
/// <see cref="SitePhotosController"/>. Lives in a sibling controller so
/// the Phase 178 file stays untouched (and so the diff against the prior
/// review pass is easy to read).
///
///  Per-photo:
///    GET    /api/projects/{pid}/photos/{phid}/annotations
///    POST   /api/projects/{pid}/photos/{phid}/annotations
///    DELETE /api/projects/{pid}/photos/{phid}/annotations/{aid}
///    GET    /api/projects/{pid}/photos/{phid}/voice-notes
///    POST   /api/projects/{pid}/photos/{phid}/voice-notes        (multipart audio)
///    DELETE /api/projects/{pid}/photos/{phid}/voice-notes/{vid}
///    GET    /api/projects/{pid}/photos/{phid}/access-rules
///    POST   /api/projects/{pid}/photos/{phid}/access-rules
///    DELETE /api/projects/{pid}/photos/{phid}/access-rules/{rid}
///    POST   /api/projects/{pid}/photos/{phid}/re-redact          (admin re-run worker)
///    POST   /api/projects/{pid}/photos/{phid}/restore            (admin un-withdraw)
///
///  Bulk:
///    POST   /api/projects/{pid}/photos/bulk-reclassify           (Reason rewrite)
///    POST   /api/projects/{pid}/photos/bulk-reanchor             (Level/Zone/WP rewrite)
///    POST   /api/projects/{pid}/photos/bulk-force-state          (admin override)
///    POST   /api/projects/{pid}/photos/bulk-tag-album            (add to album)
/// </summary>
[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/photos")]
[ProjectAccess]
public class SitePhotosExtController : ControllerBase
{
    private const long MaxAudioBytes = 25 * 1024 * 1024;

    private readonly PlanscapeDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly IAuditService _audit;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<SitePhotosExtController> _logger;

    public SitePhotosExtController(
        PlanscapeDbContext db,
        IFileStorageService storage,
        IAuditService audit,
        IBackgroundJobClient jobs,
        ILogger<SitePhotosExtController> logger)
    {
        _db = db; _storage = storage; _audit = audit; _jobs = jobs; _logger = logger;
    }

    // ── Annotations ──────────────────────────────────────────────────

    [HttpGet("{photoId:guid}/annotations")]
    public async Task<ActionResult> ListAnnotations(
        Guid projectId, Guid photoId, CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var photo = await _db.SitePhotos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == photoId && p.ProjectId == projectId, ct);
        if (photo == null) return NotFound();
        var rows = await _db.PhotoAnnotations.AsNoTracking()
            .Where(a => a.PhotoId == photoId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("{photoId:guid}/annotations")]
    public async Task<ActionResult> CreateAnnotation(
        Guid projectId, Guid photoId,
        [FromBody] CreateAnnotationRequest req,
        CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var photo = await _db.SitePhotos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == photoId && p.ProjectId == projectId, ct);
        if (photo == null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.ShapesJson)) return BadRequest(new { error = "shapes_required" });
        // Cheap shape sanity — attempt parse.
        try { _ = System.Text.Json.JsonDocument.Parse(req.ShapesJson); }
        catch { return BadRequest(new { error = "invalid_shapes_json" }); }

        var ann = new PhotoAnnotation
        {
            PhotoId         = photoId,
            ShapesJson      = req.ShapesJson,
            Summary         = req.Summary?.Trim(),
            CreatedByUserId = CurrentUserIdOrNull(),
            CreatedByName   = User.FindFirst("display_name")?.Value,
        };
        _db.PhotoAnnotations.Add(ann);
        await _db.SaveChangesAsync(ct);
        return Ok(ann);
    }

    [HttpDelete("{photoId:guid}/annotations/{annotationId:guid}")]
    public async Task<ActionResult> DeleteAnnotation(
        Guid projectId, Guid photoId, Guid annotationId,
        CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var ann = await _db.PhotoAnnotations
            .FirstOrDefaultAsync(a => a.Id == annotationId && a.PhotoId == photoId, ct);
        if (ann == null) return NotFound();
        // Authors can drop their own; PMs can drop anyone's.
        var actor = CurrentUserIdOrNull();
        var role = User.FindFirst("role")?.Value ?? "";
        bool canDelete = ann.CreatedByUserId == actor || role is "Admin" or "Owner"
            || await IsApproverAsync(projectId, ct);
        if (!canDelete) return Forbid();
        _db.PhotoAnnotations.Remove(ann);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Voice notes ──────────────────────────────────────────────────

    [HttpGet("{photoId:guid}/voice-notes")]
    public async Task<ActionResult> ListVoiceNotes(
        Guid projectId, Guid photoId, CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var notes = await _db.PhotoVoiceNotes.AsNoTracking()
            .Where(v => v.PhotoId == photoId)
            .OrderBy(v => v.CreatedAt).ToListAsync(ct);
        return Ok(notes);
    }

    [HttpPost("{photoId:guid}/voice-notes")]
    [RequestSizeLimit(MaxAudioBytes)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult> CreateVoiceNote(
        Guid projectId, Guid photoId,
        [FromForm] CreateVoiceNoteForm form,
        CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        if (form.File == null || form.File.Length == 0) return BadRequest(new { error = "file_required" });
        if (form.File.Length > MaxAudioBytes) return BadRequest(new { error = "file_too_large", limitBytes = MaxAudioBytes });

        var photo = await _db.SitePhotos.AsNoTracking()
            .Include(p => p.Project).ThenInclude(p => p!.Tenant)
            .FirstOrDefaultAsync(p => p.Id == photoId && p.ProjectId == projectId, ct);
        if (photo == null) return NotFound();

        using var memStream = new MemoryStream();
        await form.File.CopyToAsync(memStream, ct);
        memStream.Position = 0;
        var contentHash = Convert.ToHexString(SHA256.HashData(memStream.ToArray())).ToLowerInvariant();
        memStream.Position = 0;

        var safeName = $"voice-{DateTime.UtcNow:yyyyMMddHHmmss}.m4a";
        var subPath = $"{photo.Project!.Code}/site-photos/voice";
        var tenantSlug = photo.Project.Tenant?.Slug ?? GetTenantId().ToString();
        var path = await _storage.SaveAsync(tenantSlug, subPath, safeName, memStream, ct);

        var doc = new DocumentRecord
        {
            ProjectId       = projectId,
            FileName        = System.IO.Path.GetFileName(path),
            FilePath        = path,
            DocumentType    = "PHOTO_VOICE",
            CdeStatus       = "WIP",
            SuitabilityCode = "S0",
            FileSizeBytes   = form.File.Length,
            ContentHash     = contentHash,
            UploadedBy      = User.FindFirst("display_name")?.Value ?? "Unknown",
        };
        _db.Documents.Add(doc);

        var note = new PhotoVoiceNote
        {
            TenantId        = GetTenantId(),
            PhotoId         = photoId,
            UserId          = CurrentUserIdOrNull(),
            DocumentId      = doc.Id,
            TranscriptText  = form.Transcript,
            Language        = form.Language ?? "en",
            DurationSeconds = form.DurationSeconds ?? 0,
            FileSizeBytes   = form.File.Length,
            MimeType        = string.IsNullOrEmpty(form.File.ContentType) ? "audio/mp4" : form.File.ContentType,
            CreatedBy       = User.FindFirst("display_name")?.Value,
        };
        _db.PhotoVoiceNotes.Add(note);
        await _db.SaveChangesAsync(ct);
        return Ok(note);
    }

    [HttpDelete("{photoId:guid}/voice-notes/{noteId:guid}")]
    public async Task<ActionResult> DeleteVoiceNote(
        Guid projectId, Guid photoId, Guid noteId,
        CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var note = await _db.PhotoVoiceNotes
            .FirstOrDefaultAsync(v => v.Id == noteId && v.PhotoId == photoId, ct);
        if (note == null) return NotFound();
        var actor = CurrentUserIdOrNull();
        var role = User.FindFirst("role")?.Value ?? "";
        bool canDelete = note.UserId == actor || role is "Admin" or "Owner"
            || await IsApproverAsync(projectId, ct);
        if (!canDelete) return Forbid();
        _db.PhotoVoiceNotes.Remove(note);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── ACL rules ────────────────────────────────────────────────────

    [HttpGet("{photoId:guid}/access-rules")]
    public async Task<ActionResult> ListAccessRules(
        Guid projectId, Guid photoId, CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var rows = await _db.PhotoAccessRules.AsNoTracking()
            .Where(r => r.PhotoId == photoId)
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("{photoId:guid}/access-rules")]
    public async Task<ActionResult> CreateAccessRule(
        Guid projectId, Guid photoId,
        [FromBody] CreateAccessRuleRequest req,
        CancellationToken ct = default)
    {
        if (!await IsApproverAsync(projectId, ct)) return Forbid();
        var photo = await _db.SitePhotos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == photoId && p.ProjectId == projectId, ct);
        if (photo == null) return NotFound();
        var rule = new PhotoAccessRule
        {
            PhotoId               = photoId,
            DistributionGroupId   = req.DistributionGroupId,
            VisibleDisciplines    = req.VisibleDisciplines,
            MinRoleToView         = req.MinRoleToView,
            VisibleFrom           = req.VisibleFrom,
            VisibleUntil          = req.VisibleUntil,
            RequiresNdaAcceptance = req.RequiresNdaAcceptance ?? false,
            CreatedByUserId       = CurrentUserIdOrNull(),
        };
        _db.PhotoAccessRules.Add(rule);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("CREATE", "PhotoAccessRule", rule.Id.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { photoId, rule.DistributionGroupId, rule.MinRoleToView }));
        return Ok(rule);
    }

    [HttpDelete("{photoId:guid}/access-rules/{ruleId:guid}")]
    public async Task<ActionResult> DeleteAccessRule(
        Guid projectId, Guid photoId, Guid ruleId,
        CancellationToken ct = default)
    {
        if (!await IsApproverAsync(projectId, ct)) return Forbid();
        var rule = await _db.PhotoAccessRules
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.PhotoId == photoId, ct);
        if (rule == null) return NotFound();
        _db.PhotoAccessRules.Remove(rule);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Re-redact + restore (admin) ──────────────────────────────────

    [HttpPost("{photoId:guid}/re-redact")]
    public async Task<ActionResult> ReRedact(
        Guid projectId, Guid photoId,
        CancellationToken ct = default)
    {
        if (!await IsApproverAsync(projectId, ct)) return Forbid();
        var photo = await _db.SitePhotos
            .FirstOrDefaultAsync(p => p.Id == photoId && p.ProjectId == projectId, ct);
        if (photo == null) return NotFound();
        if (photo.Audience is "Internal" or "PendingReview" or "Withdrawn")
            return BadRequest(new { error = "not_redactable_in_state", current = photo.Audience });
        photo.BlurStatus       = "Pending";
        photo.WatermarkApplied = false;
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<RedactPublishedPhotoJob>(j => j.RunAsync(photoId, CancellationToken.None));
        await _audit.LogAsync("REREDACT", "SitePhoto", photoId.ToString(), "{}");
        return Ok(photo);
    }

    [HttpPost("{photoId:guid}/restore")]
    public async Task<ActionResult> Restore(
        Guid projectId, Guid photoId,
        CancellationToken ct = default)
    {
        if (!await IsApproverAsync(projectId, ct)) return Forbid();
        var photo = await _db.SitePhotos
            .FirstOrDefaultAsync(p => p.Id == photoId && p.ProjectId == projectId, ct);
        if (photo == null) return NotFound();
        if (photo.Audience != "Withdrawn") return BadRequest(new { error = "only_withdrawn_can_restore" });
        photo.Audience    = "Internal";
        photo.WithdrawnAt = null;
        photo.WithdrawnByUserId = null;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("RESTORE", "SitePhoto", photoId.ToString(), "{}");
        return Ok(photo);
    }

    // ── Bulk admin operations ───────────────────────────────────────

    [HttpPost("bulk-reclassify")]
    public async Task<ActionResult> BulkReclassify(
        Guid projectId,
        [FromBody] BulkReclassifyRequest req,
        CancellationToken ct = default)
    {
        if (!await IsApproverAsync(projectId, ct)) return Forbid();
        if (req.PhotoIds == null || req.PhotoIds.Length == 0) return BadRequest(new { error = "ids_required" });
        if (string.IsNullOrEmpty(req.ToReason) || !SitePhoto.ValidReasons.Contains(req.ToReason))
            return BadRequest(new { error = "invalid_reason", allowed = SitePhoto.ValidReasons });
        if (req.PhotoIds.Length > 500) return BadRequest(new { error = "batch_too_large", max = 500 });

        var idSet = new HashSet<Guid>(req.PhotoIds);
        var photos = await _db.SitePhotos
            .Where(p => p.ProjectId == projectId && idSet.Contains(p.Id))
            .ToListAsync(ct);
        int updated = 0;
        foreach (var p in photos) { p.Reason = req.ToReason!; updated++; }
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("BULK_RECLASSIFY", "SitePhoto", projectId.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { req.ToReason, updated }));
        return Ok(new { updated });
    }

    [HttpPost("bulk-reanchor")]
    public async Task<ActionResult> BulkReanchor(
        Guid projectId,
        [FromBody] BulkReanchorRequest req,
        CancellationToken ct = default)
    {
        if (!await IsApproverAsync(projectId, ct)) return Forbid();
        if (req.PhotoIds == null || req.PhotoIds.Length == 0) return BadRequest(new { error = "ids_required" });
        if (req.PhotoIds.Length > 500) return BadRequest(new { error = "batch_too_large", max = 500 });
        var idSet = new HashSet<Guid>(req.PhotoIds);
        var photos = await _db.SitePhotos
            .Where(p => p.ProjectId == projectId && idSet.Contains(p.Id))
            .ToListAsync(ct);
        int updated = 0;
        foreach (var p in photos)
        {
            if (req.LevelCode != null) p.LevelCode = req.LevelCode;
            if (req.ZoneCode  != null) p.ZoneCode  = req.ZoneCode;
            if (req.WorkPackageId.HasValue) p.WorkPackageId = req.WorkPackageId.Value;
            updated++;
        }
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("BULK_REANCHOR", "SitePhoto", projectId.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { req.LevelCode, req.ZoneCode, updated }));
        return Ok(new { updated });
    }

    [HttpPost("bulk-force-state")]
    public async Task<ActionResult> BulkForceState(
        Guid projectId,
        [FromBody] BulkForceStateRequest req,
        CancellationToken ct = default)
    {
        var role = User.FindFirst("role")?.Value ?? "";
        if (role is not ("Admin" or "Owner")) return Forbid(); // tighter than approver — admin only
        if (req.PhotoIds == null || req.PhotoIds.Length == 0) return BadRequest(new { error = "ids_required" });
        if (string.IsNullOrEmpty(req.ToAudience) || !SitePhoto.ValidAudiences.Contains(req.ToAudience))
            return BadRequest(new { error = "invalid_audience", allowed = SitePhoto.ValidAudiences });
        var idSet = new HashSet<Guid>(req.PhotoIds);
        var photos = await _db.SitePhotos
            .Where(p => p.ProjectId == projectId && idSet.Contains(p.Id))
            .ToListAsync(ct);
        foreach (var p in photos)
        {
            p.Audience = req.ToAudience!;
            if (req.ToAudience == "ClientPortal" && string.IsNullOrEmpty(p.RedactedFilePath))
                p.BlurStatus = "Pending";
        }
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("BULK_FORCE_STATE", "SitePhoto", projectId.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new {
                req.ToAudience, count = photos.Count, reason = req.Reason
            }));
        // If we forced anything to ClientPortal that lacked a derivative, kick the worker.
        foreach (var p in photos.Where(x => x.BlurStatus == "Pending" && x.Audience == "ClientPortal"))
            _jobs.Enqueue<RedactPublishedPhotoJob>(j => j.RunAsync(p.Id, CancellationToken.None));
        return Ok(new { count = photos.Count });
    }

    [HttpPost("bulk-tag-album")]
    public async Task<ActionResult> BulkTagAlbum(
        Guid projectId,
        [FromBody] BulkTagAlbumRequest req,
        CancellationToken ct = default)
    {
        if (!await IsApproverAsync(projectId, ct)) return Forbid();
        if (req.AlbumId == Guid.Empty) return BadRequest(new { error = "album_required" });
        if (req.PhotoIds == null || req.PhotoIds.Length == 0) return BadRequest(new { error = "ids_required" });

        var album = await _db.PhotoAlbums
            .FirstOrDefaultAsync(a => a.Id == req.AlbumId && a.ProjectId == projectId, ct);
        if (album == null) return NotFound();
        if (album.IsLocked) return BadRequest(new { error = "album_locked" });

        var idSet = new HashSet<Guid>(req.PhotoIds);
        var validIds = await _db.SitePhotos.AsNoTracking()
            .Where(p => p.ProjectId == projectId && idSet.Contains(p.Id))
            .Select(p => p.Id).ToListAsync(ct);
        var existing = await _db.PhotoAlbumPhotos.AsNoTracking()
            .Where(ap => ap.AlbumId == req.AlbumId && validIds.Contains(ap.PhotoId))
            .Select(ap => ap.PhotoId).ToListAsync(ct);
        var existingSet = new HashSet<Guid>(existing);
        var nextSort = (await _db.PhotoAlbumPhotos
            .Where(ap => ap.AlbumId == req.AlbumId)
            .Select(ap => (int?)ap.SortOrder).MaxAsync(ct) ?? 0);
        int added = 0;
        foreach (var id in validIds)
        {
            if (existingSet.Contains(id)) continue;
            nextSort += 100;
            _db.PhotoAlbumPhotos.Add(new PhotoAlbumPhoto
            {
                AlbumId = req.AlbumId, PhotoId = id, SortOrder = nextSort,
                AddedByUserId = CurrentUserIdOrNull()
            });
            added++;
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { added });
    }

    // ── helpers ──────────────────────────────────────────────────────

    private async Task<bool> IsApproverAsync(Guid projectId, CancellationToken ct)
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

public record CreateAnnotationRequest(string ShapesJson, string? Summary);

public class CreateVoiceNoteForm
{
    public IFormFile? File { get; set; }
    public string?    Transcript      { get; set; }
    public string?    Language        { get; set; }
    public int?       DurationSeconds { get; set; }
}

public record CreateAccessRuleRequest(
    Guid?     DistributionGroupId,
    string?   VisibleDisciplines,
    string?   MinRoleToView,
    DateTime? VisibleFrom,
    DateTime? VisibleUntil,
    bool?     RequiresNdaAcceptance);

public record BulkReclassifyRequest(Guid[] PhotoIds, string? ToReason);
public record BulkReanchorRequest(Guid[] PhotoIds, string? LevelCode, string? ZoneCode, Guid? WorkPackageId);
public record BulkForceStateRequest(Guid[] PhotoIds, string? ToAudience, string? Reason);
public record BulkTagAlbumRequest(Guid[] PhotoIds, Guid AlbumId);
