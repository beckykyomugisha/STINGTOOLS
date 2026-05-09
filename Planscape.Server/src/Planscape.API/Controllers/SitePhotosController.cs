using System.Security.Cryptography;
using Hangfire;
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

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 178 — Site photo workflow.
///
///   POST /api/projects/{pid}/photos/capture           — multipart upload
///   GET  /api/projects/{pid}/photos                   — filtered list
///   GET  /api/projects/{pid}/photos/{phid}            — single photo
///   GET  /api/projects/{pid}/photos/{phid}/file       — bytes (original or redacted)
///   PUT  /api/projects/{pid}/photos/{phid}/audience   — flip Internal ↔ PendingReview
///   POST /api/projects/{pid}/photos/{phid}/approve    — PM/Admin/Owner approve (caption required)
///   POST /api/projects/{pid}/photos/{phid}/reject     — reviewer reject with reason
///   POST /api/projects/{pid}/photos/{phid}/withdraw   — retract published photo
///   POST /api/projects/{pid}/photos/bulk-approve      — batch approve (shared caption)
///   GET  /api/projects/{pid}/photos/digest-preview    — what the daily digest would send
///
/// Approval gates the blur+watermark worker (separate container — see
/// Planscape.Infrastructure.Jobs.RedactPublishedPhotoJob). Internal +
/// PendingReview photos never reach client-portal users; only the
/// redacted derivative does.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/photos")]
[Authorize]
[ProjectAccess]
public class SitePhotosController : ControllerBase
{
    private const long MaxPhotoSize = 25 * 1024 * 1024;        // 25 MB hard cap on a single capture
    private const int  MinCaptionChars = 3;                    // approval guard

    private readonly PlanscapeDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly IAuditService _audit;
    private readonly IPushNotificationService _push;
    private readonly INotificationService _notif;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<SitePhotosController> _logger;

    public SitePhotosController(
        PlanscapeDbContext db,
        IFileStorageService storage,
        IAuditService audit,
        IPushNotificationService push,
        INotificationService notif,
        IHubContext<NotificationHub> hub,
        IBackgroundJobClient jobs,
        ILogger<SitePhotosController> logger)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
        _push = push;
        _notif = notif;
        _hub = hub;
        _jobs = jobs;
        _logger = logger;
    }

    // ── POST /capture ─────────────────────────────────────────────────
    /// <summary>
    /// Capture a new site photo. Multipart form fields:
    ///   file               (required) image bytes
    ///   reason             (required) Progress | Issue | Defect | Safety | AsBuilt | Reference
    ///   caption            (optional) free-text — required later to publish
    ///   levelCode, zoneCode, workPackageId, anchorIssueId, anchorElementGuid
    ///   modelId, modelX, modelY, modelZ
    ///   latitude, longitude, accuracyM
    ///   pairKey, classifierConfidence, classifierSignals (json string)
    ///   queuedClient (boolean — true if this came from the offline queue)
    /// </summary>
    [HttpPost("capture")]
    [RequestSizeLimit(MaxPhotoSize)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<SitePhotoDto>> Capture(
        Guid projectId,
        [FromForm] CaptureForm form,
        CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects
            .Include(p => p.Tenant)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
        if (project == null) return NotFound("Project not found");
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;

        if (form.File == null || form.File.Length == 0) return BadRequest(new { error = "file_required" });
        if (form.File.Length > MaxPhotoSize)            return BadRequest(new { error = "file_too_large", limitBytes = MaxPhotoSize });
        if (string.IsNullOrWhiteSpace(form.Reason) || !SitePhoto.ValidReasons.Contains(form.Reason))
            return BadRequest(new { error = "invalid_reason", allowed = SitePhoto.ValidReasons });

        // Image-only — site photos are never PDFs / docx.
        if (string.IsNullOrEmpty(form.File.ContentType) || !form.File.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "image_required", contentType = form.File.ContentType });

        // Magic-byte sniff — defends against renamed binaries.
        using var memStream = new MemoryStream();
        await form.File.CopyToAsync(memStream, ct);
        memStream.Position = 0;
        if (!Planscape.Infrastructure.Security.FileContentValidator.IsImage(memStream, out _))
            return BadRequest(new { error = "image_content_mismatch" });

        memStream.Position = 0;
        var contentHash = Convert.ToHexString(SHA256.HashData(memStream.ToArray())).ToLowerInvariant();

        var safeName = Planscape.Infrastructure.Security.FileContentValidator.SanitiseFileName(
            form.File.FileName, fallback: $"photo-{DateTime.UtcNow:yyyyMMddHHmmss}");

        // Per-tenant bucket with project subfolder (decision: per-tenant
        // with project subfolder — same pattern as IssueAttachment).
        memStream.Position = 0;
        var subPath = $"{project.Code}/site-photos";
        var tenantSlug = project.Tenant?.Slug ?? tenantId.ToString();
        var relativePath = await _storage.SaveAsync(tenantSlug, subPath, safeName, memStream, ct);

        var doc = new DocumentRecord
        {
            ProjectId       = projectId,
            FileName        = System.IO.Path.GetFileName(relativePath),
            FilePath        = relativePath,
            DocumentType    = "SITE_PHOTO",
            CdeStatus       = "WIP",
            SuitabilityCode = "S0",
            FileSizeBytes   = form.File.Length,
            ContentHash     = contentHash,
            UploadedBy      = User.FindFirst("display_name")?.Value ?? "Unknown",
        };
        _db.Documents.Add(doc);

        // Audience default by reason — Progress / AsBuilt go to review,
        // everything else stays Internal until the user toggles.
        var audience = SitePhoto.DefaultToReview(form.Reason) ? "PendingReview" : "Internal";

        var capturerClaim = User.FindFirst("user_id")?.Value
                         ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                         ?? User.FindFirst("sub")?.Value;
        Guid? capturerId = Guid.TryParse(capturerClaim, out var cid) ? cid : null;

        var photo = new SitePhoto
        {
            TenantId             = tenantId,
            ProjectId            = projectId,
            DocumentId           = doc.Id,
            Reason               = form.Reason,
            Audience             = audience,
            BlurStatus           = "NotRequired",
            Caption              = string.IsNullOrWhiteSpace(form.Caption) ? null : form.Caption.Trim(),
            CapturedAt           = form.CapturedAt ?? DateTime.UtcNow,
            CapturedByUserId     = capturerId,
            DeviceId             = form.DeviceId ?? Request.Headers["X-Device-Id"].ToString(),
            Source               = form.Source ?? (Request.Headers.ContainsKey("X-Device-Id") ? "mobile" : "web"),
            Latitude             = form.Latitude,
            Longitude            = form.Longitude,
            AccuracyM            = form.AccuracyM,
            LevelCode            = form.LevelCode,
            ZoneCode             = form.ZoneCode,
            WorkPackageId        = form.WorkPackageId,
            AnchorIssueId        = form.AnchorIssueId,
            AnchorElementGuid    = form.AnchorElementGuid,
            ModelId              = form.ModelId,
            ModelX               = form.ModelX,
            ModelY               = form.ModelY,
            ModelZ               = form.ModelZ,
            ClassifierConfidence = form.ClassifierConfidence ?? 0,
            ClassifierSignals    = form.ClassifierSignals,
            PairKey              = form.PairKey,
        };
        _db.SitePhotos.Add(photo);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("CREATE", "SitePhoto", photo.Id.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new {
                projectId, photo.Reason, photo.Audience,
                photo.LevelCode, photo.ZoneCode,
                fileName = doc.FileName, fileSizeBytes = doc.FileSizeBytes,
                queuedOffline = form.QueuedClient ?? false
            }));

        // Real-time push — every project member subscribed to the project
        // sees the new photo land in their gallery without a refresh.
        _ = _hub.Clients.Group($"project-{projectId}").SendAsync("SitePhotoCaptured", new {
            projectId, photoId = photo.Id, reason = photo.Reason, audience = photo.Audience,
        }, ct);

        // Auto-create issue when Reason ∈ {Issue, Defect, Safety} and no
        // explicit anchorIssueId was supplied. Defects → NCR, Safety →
        // SAFETY type with high priority. The created issue gets the
        // photo as an attachment (no second upload needed).
        if (SitePhoto.CreatesIssue(form.Reason) && photo.AnchorIssueId == null)
        {
            try
            {
                var issueType = form.Reason switch {
                    "Defect" => "NCR",
                    "Safety" => "SAFETY",
                    _        => "RFI",
                };
                var issuePriority = form.Reason == "Safety" ? "HIGH" : "MEDIUM";
                var newIssue = new BimIssue
                {
                    ProjectId       = projectId,
                    Type            = issueType,
                    IssueCode       = await NextIssueCodeAsync(projectId, issueType, ct),
                    Title           = photo.Caption ?? $"{form.Reason} captured on site",
                    Description     = photo.Caption,
                    Priority        = issuePriority,
                    Status          = "OPEN",
                    Discipline      = null,
                    CreatedBy       = User.FindFirst("display_name")?.Value ?? "Unknown",
                    CreatedByUserId = capturerId,
                    Latitude        = photo.Latitude,
                    Longitude       = photo.Longitude,
                    LocationAccuracy = photo.AccuracyM,
                    Source          = photo.Source,
                    DeviceId        = photo.DeviceId,
                    DueDate         = DateTime.UtcNow.AddHours(form.Reason == "Safety" ? 4 : 48),
                    ModelId         = photo.ModelId,
                    ModelElementGuid = photo.AnchorElementGuid,
                    ModelX          = photo.ModelX,
                    ModelY          = photo.ModelY,
                    ModelZ          = photo.ModelZ,
                };
                _db.Issues.Add(newIssue);
                _db.IssueAttachments.Add(new IssueAttachment
                {
                    IssueId     = newIssue.Id,
                    DocumentId  = doc.Id,
                    AttachedBy  = User.FindFirst("display_name")?.Value ?? "Unknown",
                });
                photo.AnchorIssueId = newIssue.Id;
                await _db.SaveChangesAsync(ct);
                await _audit.LogAsync("CREATE", "Issue", newIssue.Id.ToString(),
                    System.Text.Json.JsonSerializer.Serialize(new { autoCreatedFromSitePhoto = photo.Id }));
            }
            catch (Exception ex)
            {
                // Auto-issue creation must never break the photo capture
                // itself — the user can manually create one later if it
                // fails (e.g. concurrent IssueCode collision).
                _logger.LogWarning(ex, "Auto-issue creation failed for site photo {PhotoId}", photo.Id);
            }
        }

        return CreatedAtAction(nameof(GetOne), new { projectId, photoId = photo.Id }, await ToDtoAsync(photo, ct));
    }

    // ── GET / list ────────────────────────────────────────────────────
    [HttpGet]
    public async Task<ActionResult> List(
        Guid projectId,
        [FromQuery] string? reason,
        [FromQuery] string? audience,
        [FromQuery] string? levelCode,
        [FromQuery] string? zoneCode,
        [FromQuery] string? anchorElementGuid,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var tenantId = GetTenantId();
        var inTenant = await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
        if (!inTenant) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var q = _db.SitePhotos.AsNoTracking().Where(p => p.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(reason)) q = q.Where(p => p.Reason == reason);
        if (!string.IsNullOrWhiteSpace(audience)) q = q.Where(p => p.Audience == audience);
        if (!string.IsNullOrWhiteSpace(levelCode)) q = q.Where(p => p.LevelCode == levelCode);
        if (!string.IsNullOrWhiteSpace(zoneCode)) q = q.Where(p => p.ZoneCode == zoneCode);
        if (!string.IsNullOrWhiteSpace(anchorElementGuid)) q = q.Where(p => p.AnchorElementGuid == anchorElementGuid);
        if (from.HasValue) q = q.Where(p => p.CapturedAt >= from.Value);
        if (to.HasValue)   q = q.Where(p => p.CapturedAt <= to.Value);

        var total = await q.CountAsync(ct);

        // Two-step pattern: window the page IDs first, THEN join, so EF
        // Core 8 reliably pushes ORDER BY / LIMIT into a subquery before
        // the LEFT JOINs. The single-step LINQ syntax with into +
        // DefaultIfEmpty over a windowed source can fall back to client
        // evaluation in EF 8 (the translator is conservative there).
        // Two round-trips, both indexed: total cost is unchanged.
        var pageIds = await q
            .OrderByDescending(p => p.CapturedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var rows = pageIds.Count == 0
            ? new List<SitePhotoDto>()
            : await (
                from p in _db.SitePhotos.AsNoTracking()
                where pageIds.Contains(p.Id)
                join u in _db.Users.AsNoTracking() on p.CapturedByUserId equals u.Id into ug
                from u in ug.DefaultIfEmpty()
                join i in _db.Issues.AsNoTracking() on p.AnchorIssueId equals i.Id into ig
                from i in ig.DefaultIfEmpty()
                select new SitePhotoDto(
                    p.Id, p.ProjectId, p.DocumentId,
                    p.Reason, p.Audience, p.BlurStatus, p.WatermarkApplied,
                    p.Caption, p.CapturedAt, p.CapturedByUserId,
                    p.LevelCode, p.ZoneCode, p.AnchorIssueId, p.AnchorElementGuid,
                    p.ModelId, p.ModelX, p.ModelY, p.ModelZ,
                    p.PairKey, p.ClassifierConfidence,
                    p.ApprovedAt, p.ApprovedByUserId,
                    p.RejectedAt, p.RejectedReason,
                    p.Latitude, p.Longitude,
                    u != null ? u.DisplayName : null,
                    i != null ? i.Discipline : null
                )).ToListAsync(ct);

        // Reconstruct the requested ordering — Contains() doesn't preserve
        // pageIds ordering across DBs, so re-sort by CapturedAt desc.
        rows = rows.OrderByDescending(r => r.CapturedAt).ToList();

        return Ok(new { items = rows, total, page, pageSize });
    }

    // ── GET / single ──────────────────────────────────────────────────
    [HttpGet("{photoId:guid}")]
    public async Task<ActionResult<SitePhotoDto>> GetOne(Guid projectId, Guid photoId, CancellationToken ct)
    {
        var photo = await LoadPhotoAsync(projectId, photoId, ct);
        if (photo == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        return Ok(await ToDtoAsync(photo, ct));
    }

    // ── GET /file — original (internal) or redacted (client) ──────────
    /// <summary>
    /// Stream the photo bytes. Returns the redacted derivative when the
    /// caller is a client-portal user *and* the photo is currently
    /// ClientPortal-published. Project members always get the original.
    /// </summary>
    [HttpGet("{photoId:guid}/file")]
    public async Task<IActionResult> GetFile(Guid projectId, Guid photoId, CancellationToken ct)
    {
        var photo = await _db.SitePhotos.AsNoTracking()
            .Include(p => p.Document)
            .FirstOrDefaultAsync(p => p.Id == photoId && p.ProjectId == projectId, ct);
        if (photo?.Document?.FilePath == null) return NotFound();

        var role = User.FindFirst("role")?.Value ?? "";
        var isClientGuest = role == "ClientGuest";

        string path;
        if (isClientGuest)
        {
            if (photo.Audience != "ClientPortal" || string.IsNullOrEmpty(photo.RedactedFilePath))
                return Forbid();
            path = photo.RedactedFilePath!;
        }
        else
        {
            // Project members get the original; require active membership.
            if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
            path = photo.Document.FilePath!;
        }

        var stream = await _storage.GetAsync(path, ct);
        if (stream == null) return NotFound();
        return File(stream, "image/jpeg", photo.Document.FileName);
    }

    // ── PUT /audience — flip Internal ↔ PendingReview ────────────────
    /// <summary>
    /// User-driven audience toggle. Allowed transitions from this endpoint:
    ///   Internal      → PendingReview
    ///   PendingReview → Internal
    /// Approval and withdraw use their dedicated endpoints (which carry
    /// extra side-effects like enqueuing the worker / writing audit).
    /// </summary>
    [HttpPut("{photoId:guid}/audience")]
    public async Task<ActionResult> SetAudience(
        Guid projectId, Guid photoId,
        [FromBody] SetAudienceRequest req,
        CancellationToken ct)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var photo = await LoadPhotoAsync(projectId, photoId, ct);
        if (photo == null) return NotFound();

        if (req.Audience is not ("Internal" or "PendingReview"))
            return BadRequest(new { error = "audience_not_allowed_via_set", allowed = new[] { "Internal", "PendingReview" } });
        if (photo.Audience is "Approved" or "ClientPortal" or "Withdrawn")
            return BadRequest(new { error = "use_dedicated_endpoint", current = photo.Audience });

        var prev = photo.Audience;
        photo.Audience = req.Audience;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("UPDATE", "SitePhoto", photoId.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { audience = new { from = prev, to = photo.Audience } }));

        return Ok(await ToDtoAsync(photo, ct));
    }

    // ── POST /approve ─────────────────────────────────────────────────
    [HttpPost("{photoId:guid}/approve")]
    public async Task<ActionResult> Approve(
        Guid projectId, Guid photoId,
        [FromBody] ApproveRequest req,
        CancellationToken ct)
    {
        if (!await IsApproverAsync(projectId, ct)) return Forbid();
        var photo = await LoadPhotoAsync(projectId, photoId, ct);
        if (photo == null) return NotFound();

        var caption = (req.Caption ?? photo.Caption ?? "").Trim();
        if (caption.Length < MinCaptionChars)
            return BadRequest(new { error = "caption_required", minChars = MinCaptionChars });

        if (photo.Audience is "Approved" or "ClientPortal")
            return BadRequest(new { error = "already_approved" });
        if (photo.Audience is "Withdrawn")
            return BadRequest(new { error = "withdrawn_must_reset_first" });

        var actorId = CurrentUserIdOrNull();
        photo.Caption          = caption;
        photo.Audience         = "Approved";
        photo.BlurStatus       = "Pending";        // worker picks it up
        photo.ApprovedAt       = DateTime.UtcNow;
        photo.ApprovedByUserId = actorId;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("APPROVE", "SitePhoto", photoId.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { caption, approver = actorId }));

        // Enqueue blur+watermark worker on the dedicated photo-redaction
        // queue so it runs on the separate worker container (not on the
        // API process — protects API CPU when a batch lands at digest
        // time). The worker flips Audience → ClientPortal on success.
        _jobs.Enqueue<RedactPublishedPhotoJob>(j => j.RunAsync(photoId, CancellationToken.None));

        return Ok(await ToDtoAsync(photo, ct));
    }

    // ── POST /reject ──────────────────────────────────────────────────
    [HttpPost("{photoId:guid}/reject")]
    public async Task<ActionResult> Reject(
        Guid projectId, Guid photoId,
        [FromBody] RejectRequest req,
        CancellationToken ct)
    {
        if (!await IsApproverAsync(projectId, ct)) return Forbid();
        var photo = await LoadPhotoAsync(projectId, photoId, ct);
        if (photo == null) return NotFound();

        var actorId = CurrentUserIdOrNull();
        photo.Audience         = "Internal";          // back to internal-only
        photo.RejectedAt       = DateTime.UtcNow;
        photo.RejectedByUserId = actorId;
        photo.RejectedReason   = string.IsNullOrWhiteSpace(req.Reason) ? "rejected" : req.Reason.Trim();
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("REJECT", "SitePhoto", photoId.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { reason = photo.RejectedReason, rejector = actorId }));

        return Ok(await ToDtoAsync(photo, ct));
    }

    // ── POST /withdraw ────────────────────────────────────────────────
    [HttpPost("{photoId:guid}/withdraw")]
    public async Task<ActionResult> Withdraw(Guid projectId, Guid photoId, CancellationToken ct)
    {
        if (!await IsApproverAsync(projectId, ct)) return Forbid();
        var photo = await LoadPhotoAsync(projectId, photoId, ct);
        if (photo == null) return NotFound();

        if (photo.Audience != "ClientPortal")
            return BadRequest(new { error = "only_clientportal_can_withdraw", current = photo.Audience });

        var actorId = CurrentUserIdOrNull();
        photo.Audience          = "Withdrawn";
        photo.WithdrawnAt       = DateTime.UtcNow;
        photo.WithdrawnByUserId = actorId;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("WITHDRAW", "SitePhoto", photoId.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { withdrawer = actorId }));

        return Ok(await ToDtoAsync(photo, ct));
    }

    // ── POST /bulk-approve ────────────────────────────────────────────
    /// <summary>
    /// Bulk-approve a set of PendingReview photos with one shared caption
    /// (the BCC + mobile reviewer pattern: walk-through generated 40
    /// photos; PM approves the lot in 5 seconds with one caption).
    /// </summary>
    [HttpPost("bulk-approve")]
    public async Task<ActionResult> BulkApprove(
        Guid projectId,
        [FromBody] BulkApproveRequest req,
        CancellationToken ct)
    {
        if (!await IsApproverAsync(projectId, ct)) return Forbid();
        if (req.PhotoIds == null || req.PhotoIds.Length == 0) return BadRequest(new { error = "ids_required" });
        if (req.PhotoIds.Length > 200)                        return BadRequest(new { error = "batch_too_large", max = 200 });
        var caption = (req.Caption ?? "").Trim();
        if (caption.Length < MinCaptionChars) return BadRequest(new { error = "caption_required", minChars = MinCaptionChars });

        var idSet = new HashSet<Guid>(req.PhotoIds);
        var photos = await _db.SitePhotos
            .Where(p => p.ProjectId == projectId && idSet.Contains(p.Id))
            .ToListAsync(ct);

        var actorId = CurrentUserIdOrNull();
        int approved = 0, skipped = 0;
        var skippedDetail = new List<object>();
        foreach (var photo in photos)
        {
            if (photo.Audience is "Approved" or "ClientPortal" or "Withdrawn")
            {
                skipped++; skippedDetail.Add(new { photo.Id, reason = "already_terminal", current = photo.Audience });
                continue;
            }
            // Per-photo caption wins; otherwise apply the bulk caption.
            if (string.IsNullOrWhiteSpace(photo.Caption)) photo.Caption = caption;
            photo.Audience         = "Approved";
            photo.BlurStatus       = "Pending";
            photo.ApprovedAt       = DateTime.UtcNow;
            photo.ApprovedByUserId = actorId;
            approved++;
        }
        await _db.SaveChangesAsync(ct);

        // Enqueue worker once per approved photo. Each runs independently
        // so a single failure quarantines only one row.
        foreach (var photo in photos.Where(p => p.Audience == "Approved" && p.BlurStatus == "Pending"))
        {
            _jobs.Enqueue<RedactPublishedPhotoJob>(j => j.RunAsync(photo.Id, CancellationToken.None));
        }

        await _audit.LogAsync("APPROVE", "SitePhoto", projectId.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new {
                bulk = true, approved, skipped, skippedDetail, caption, approver = actorId
            }));

        return Ok(new { approved, skipped, skippedDetail });
    }

    // ── GET /digest-preview ───────────────────────────────────────────
    /// <summary>
    /// Returns the photos that would ship in today's digest if it ran
    /// now: <c>Audience = ClientPortal</c> photos approved in the last
    /// 24 h. Both the BCC tab and the email job render off this shape.
    /// </summary>
    [HttpGet("digest-preview")]
    public async Task<ActionResult> DigestPreview(Guid projectId, CancellationToken ct)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        var since = DateTime.UtcNow.AddHours(-24);
        var rows = await _db.SitePhotos.AsNoTracking()
            .Where(p => p.ProjectId == projectId && p.Audience == "ClientPortal" && p.ApprovedAt >= since)
            .OrderBy(p => p.CapturedAt)
            .Select(p => new {
                p.Id, p.Reason, p.Caption, p.LevelCode, p.ZoneCode,
                p.CapturedAt, p.ApprovedAt
            })
            .ToListAsync(ct);
        return Ok(new {
            projectId,
            windowStart = since,
            count = rows.Count,
            items = rows
        });
    }

    // ── helpers ───────────────────────────────────────────────────────

    private async Task<SitePhoto?> LoadPhotoAsync(Guid projectId, Guid photoId, CancellationToken ct) =>
        await _db.SitePhotos.FirstOrDefaultAsync(p => p.Id == photoId && p.ProjectId == projectId, ct);

    /// <summary>
    /// Approval gate (decision #1): tenant Admin / Owner OR
    /// ProjectMember.ProjectRole == "PM" on this project.
    /// </summary>
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

    private async Task<string> NextIssueCodeAsync(Guid projectId, string type, CancellationToken ct)
    {
        var last = await _db.Issues
            .Where(i => i.ProjectId == projectId && i.Type == type)
            .OrderByDescending(i => i.IssueCode)
            .Select(i => i.IssueCode)
            .FirstOrDefaultAsync(ct);
        int next = 1;
        if (!string.IsNullOrEmpty(last))
        {
            var idx = last.LastIndexOf('-');
            if (idx > 0 && int.TryParse(last.AsSpan(idx + 1), out var n)) next = n + 1;
        }
        return $"{type}-{next:D4}";
    }

    /// <summary>
    /// Build a SitePhotoDto for a single photo. When CapturedByUserId
    /// or AnchorIssueId is set, an extra round-trip resolves the
    /// captured-by display name + discipline so the BCC / viewer rows
    /// don't have to do their own lookups. Skipped when both keys are
    /// null (no joins needed).
    /// </summary>
    private async Task<SitePhotoDto> ToDtoAsync(SitePhoto p, CancellationToken ct)
    {
        string? capturedByName = null;
        string? discipline = null;
        if (p.CapturedByUserId.HasValue)
        {
            capturedByName = await _db.Users.AsNoTracking()
                .Where(u => u.Id == p.CapturedByUserId.Value)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct);
        }
        if (p.AnchorIssueId.HasValue)
        {
            discipline = await _db.Issues.AsNoTracking()
                .Where(i => i.Id == p.AnchorIssueId.Value)
                .Select(i => i.Discipline)
                .FirstOrDefaultAsync(ct);
        }
        return new SitePhotoDto(
            p.Id, p.ProjectId, p.DocumentId,
            p.Reason, p.Audience, p.BlurStatus, p.WatermarkApplied,
            p.Caption, p.CapturedAt, p.CapturedByUserId,
            p.LevelCode, p.ZoneCode, p.AnchorIssueId, p.AnchorElementGuid,
            p.ModelId, p.ModelX, p.ModelY, p.ModelZ,
            p.PairKey, p.ClassifierConfidence,
            p.ApprovedAt, p.ApprovedByUserId,
            p.RejectedAt, p.RejectedReason,
            p.Latitude, p.Longitude,
            capturedByName, discipline);
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────

public class CaptureForm
{
    public IFormFile? File { get; set; }
    public string Reason { get; set; } = "Reference";
    public string? Caption { get; set; }
    public string? LevelCode { get; set; }
    public string? ZoneCode { get; set; }
    public Guid?   WorkPackageId { get; set; }
    public Guid?   AnchorIssueId { get; set; }
    public string? AnchorElementGuid { get; set; }
    public Guid?   ModelId { get; set; }
    public double? ModelX { get; set; }
    public double? ModelY { get; set; }
    public double? ModelZ { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? AccuracyM { get; set; }
    public string? PairKey { get; set; }
    public double? ClassifierConfidence { get; set; }
    public string? ClassifierSignals { get; set; }
    public DateTime? CapturedAt { get; set; }
    public string? DeviceId { get; set; }
    public string? Source { get; set; }
    public bool?   QueuedClient { get; set; }
}

public record SetAudienceRequest(string Audience);
public record ApproveRequest(string? Caption);
public record RejectRequest(string? Reason);
public record BulkApproveRequest(Guid[] PhotoIds, string Caption);

public record SitePhotoDto(
    Guid     Id,
    Guid     ProjectId,
    Guid     DocumentId,
    string   Reason,
    string   Audience,
    string   BlurStatus,
    bool     WatermarkApplied,
    string?  Caption,
    DateTime CapturedAt,
    Guid?    CapturedByUserId,
    string?  LevelCode,
    string?  ZoneCode,
    Guid?    AnchorIssueId,
    string?  AnchorElementGuid,
    Guid?    ModelId,
    double?  ModelX,
    double?  ModelY,
    double?  ModelZ,
    string?  PairKey,
    double   ClassifierConfidence,
    DateTime? ApprovedAt,
    Guid?    ApprovedByUserId,
    DateTime? RejectedAt,
    string?  RejectedReason,
    double?  Latitude,
    double?  Longitude,
    // ── Resolved at projection time (NOT stored on SitePhoto) ─────────
    // CapturedByName joins AppUser.DisplayName via CapturedByUserId so
    // the BCC + viewer rows can render "Captured by …" without a second
    // round-trip per row. Discipline is derived from the linked
    // BimIssue.Discipline when AnchorIssueId is set, else null. Both
    // are nullable strings so older clients that don't read them keep
    // working.
    string?  CapturedByName,
    string?  Discipline);
