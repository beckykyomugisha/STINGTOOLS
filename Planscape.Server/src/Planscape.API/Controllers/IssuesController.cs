using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.API.Controllers;

[ApiController]
[Route("api/projects/{projectId}/[controller]")]
[EnableRateLimiting("mobile")]
[Authorize]
public class IssuesController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly Planscape.Core.Interfaces.INotificationService _notifications;
    private readonly Planscape.Core.Interfaces.IPushNotificationService _push;
    private readonly IFileStorageService _storage;
    private readonly IGeofenceValidationService _geofence;
    private readonly IThumbnailService _thumbnails;
    private readonly ILogger<IssuesController> _logger;
    private readonly IAuditService _audit;
    private readonly IHubContext<NotificationHub> _notifHub;
    private readonly Planscape.Infrastructure.Services.OutboundWebhookDispatcher? _webhooks;

    private static readonly Dictionary<string, int> SLAHours = new()
    {
        ["CRITICAL"] = 4, ["HIGH"] = 24, ["MEDIUM"] = 168, ["LOW"] = 336
    };

    private const long MaxAttachmentSize = 50 * 1024 * 1024; // 50 MB

    private static readonly string[] ImageContentTypes = { "image/jpeg", "image/png", "image/webp" };
    private static readonly int[] ValidThumbSizes = { 150, 300, 600 };

    public IssuesController(PlanscapeDbContext db,
        Planscape.Core.Interfaces.INotificationService notifications,
        Planscape.Core.Interfaces.IPushNotificationService push,
        IFileStorageService storage,
        IGeofenceValidationService geofence,
        IThumbnailService thumbnails,
        ILogger<IssuesController> logger,
        IAuditService audit,
        IHubContext<NotificationHub> notifHub,
        Planscape.Infrastructure.Services.OutboundWebhookDispatcher? webhooks = null)
    {
        _db = db;
        _notifications = notifications;
        _push = push;
        _storage = storage;
        _geofence = geofence;
        _thumbnails = thumbnails;
        _logger = logger;
        _audit = audit;
        _notifHub = notifHub;
        _webhooks = webhooks;
    }

    [HttpGet]
    public async Task<ActionResult> GetIssues(Guid projectId,
        [FromQuery] string? status = null, [FromQuery] string? type = null,
        [FromQuery] Guid? modelId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var tenantId = GetTenantId();
        var query = _db.Issues.Where(i => i.ProjectId == projectId && i.Project!.TenantId == tenantId);

        if (!string.IsNullOrEmpty(status)) query = query.Where(i => i.Status == status);
        if (!string.IsNullOrEmpty(type)) query = query.Where(i => i.Type == type);
        // Phase 164 — modelId filter so the mobile sibling-pin loader can
        // request only issues anchored to the active model. Backed by the
        // existing single-column index on BimIssue.ModelId (PlanscapeDbContext.cs:136).
        if (modelId.HasValue) query = query.Where(i => i.ModelId == modelId);

        var total = await query.CountAsync();
        var issues = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new
            {
                i.Id, i.IssueCode, i.Type, i.Title, i.Priority, i.Status,
                i.Assignee, i.Discipline, i.Revision, i.CreatedBy, i.CreatedAt, i.DueDate, i.ResolvedAt,
                // Phase 164 — model anchor fields. Without these the Phase 163
                // mobile sibling-pin filter operated on fields the projection
                // never returned, so pins never rendered. Adding them here is
                // additive at the wire level (existing clients ignore unknown
                // properties).
                i.ModelId, i.ModelElementGuid, i.ModelX, i.ModelY, i.ModelZ,
                IsOverdue = i.DueDate.HasValue && i.DueDate < DateTime.UtcNow && i.Status != "CLOSED" && i.Status != "RESOLVED",
                DaysOpen = (int)(DateTime.UtcNow - i.CreatedAt).TotalDays
            })
            .ToListAsync();

        return Ok(new { items = issues, total, page, pageSize });
    }

    [HttpPost]
    public async Task<ActionResult> CreateIssue(Guid projectId, [FromBody] CreateIssueRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");

        // NEW-LOGIC-02 — Sanitise Type. Only 2-6 uppercase letters allowed to prevent
        // injection of "-" that would break IssueCode parsing elsewhere.
        if (string.IsNullOrWhiteSpace(req.Type) ||
            !System.Text.RegularExpressions.Regex.IsMatch(req.Type, @"^[A-Z]{2,6}$"))
        {
            return BadRequest(new { error = "Type must be 2-6 uppercase letters (e.g. RFI, NCR, SI, TQ, CLASH, DEFECT)" });
        }

        // MODEL-VIEWER — validate ModelId belongs to this project. Stops a
        // malicious client linking an issue to a model in a different project
        // (which would still upload but later 404 on the viewer file fetch).
        // Soft-deleted models are rejected too — same `DeletedAt == null`
        // gate as ModelsController.
        if (req.ModelId.HasValue)
        {
            bool modelOwned = await _db.ProjectModels.AnyAsync(m =>
                m.Id == req.ModelId.Value && m.ProjectId == projectId && m.DeletedAt == null);
            if (!modelOwned)
                return BadRequest(new { error = "ModelId does not belong to this project" });
        }

        // NEW-LOGIC-08 — Validate lat/lng ranges before geofence check.
        // MobileContextMiddleware parses headers but never range-checks them.
        if (HttpContext.Items.TryGetValue("Latitude", out var latObj) &&
            HttpContext.Items.TryGetValue("Longitude", out var lngObj) &&
            latObj is double lat && lngObj is double lng)
        {
            if (double.IsNaN(lat) || double.IsNaN(lng) || Math.Abs(lat) > 90 || Math.Abs(lng) > 180)
                return BadRequest(new { error = "Invalid latitude/longitude range" });
            if (!_geofence.IsInsideBoundary(project.BoundaryPolygon, lat, lng))
                return StatusCode(403, new { error = "Device location is outside the project geofence boundary" });
        }

        // NEW-LOGIC-01/02 — Issue code is generated inside a retry loop so that
        // concurrent CreateIssue requests for the same Type cannot both write
        // the same code. A unique DB index on (ProjectId, IssueCode) is the
        // ultimate guard (see migration 20250407 line 189) — this loop handles
        // the expected contention case gracefully.
        int nextNum = 1;
        var lastIssue = await _db.Issues
            .Where(i => i.ProjectId == projectId && i.Type == req.Type)
            .OrderByDescending(i => i.IssueCode)
            .FirstOrDefaultAsync();
        if (lastIssue != null)
        {
            // Use LastIndexOf so a Type accidentally containing "-" (impossible after
            // the regex gate above, but belt-and-braces) splits sensibly.
            var idx = lastIssue.IssueCode.LastIndexOf('-');
            if (idx > 0 && int.TryParse(lastIssue.IssueCode.AsSpan(idx + 1), out int n)) nextNum = n + 1;
        }

        // NEW-SRV-23 + NEW-MOB-17: resolve and validate assignee against project membership.
        // Accept any of: AssigneeUserId (preferred), AssigneeEmail, or legacy Assignee display name.
        AppUser? assigneeUser = null;
        if (req.AssigneeUserId.HasValue)
        {
            assigneeUser = await _db.Users.FirstOrDefaultAsync(u =>
                u.Id == req.AssigneeUserId.Value && u.TenantId == tenantId);
        }
        if (assigneeUser == null && !string.IsNullOrWhiteSpace(req.AssigneeEmail))
        {
            assigneeUser = await _db.Users.FirstOrDefaultAsync(u =>
                u.Email == req.AssigneeEmail && u.TenantId == tenantId);
        }
        if (assigneeUser == null && !string.IsNullOrWhiteSpace(req.Assignee))
        {
            // Legacy display-name match — kept so older mobile builds still work
            assigneeUser = await _db.Users.FirstOrDefaultAsync(u =>
                u.DisplayName == req.Assignee && u.TenantId == tenantId);
        }
        if (assigneeUser != null)
        {
            var isMember = await _db.ProjectMembers.AnyAsync(m =>
                m.ProjectId == projectId && m.UserId == assigneeUser.Id && m.IsActive);
            if (!isMember)
            {
                return BadRequest(new { error = "Assignee is not an active member of this project" });
            }
        }

        var creatorClaim = User.FindFirst("user_id")?.Value;
        Guid? creatorId = Guid.TryParse(creatorClaim, out var cid) ? cid : null;

        // Source detection: explicit > X-Device-Id presence > default "web"
        var source = req.Source
            ?? (Request.Headers.ContainsKey("X-Device-Id") ? "mobile" : "web");

        var issue = new BimIssue
        {
            ProjectId = projectId,
            IssueCode = $"{req.Type}-{nextNum:D4}",
            Type = req.Type,
            Title = req.Title,
            Description = req.Description,
            Priority = req.Priority ?? "MEDIUM",
            Assignee = assigneeUser?.DisplayName ?? req.Assignee,
            AssigneeEmail = assigneeUser?.Email ?? req.AssigneeEmail,
            AssigneeUserId = assigneeUser?.Id,
            Discipline = req.Discipline,
            CreatedBy = User.FindFirst("display_name")?.Value ?? "Unknown",
            CreatedByUserId = creatorId,
            LinkedElementIds = req.LinkedElementIds,
            DueDate = ComputeSLADeadline(req.Priority ?? "MEDIUM"),
            Latitude = req.Latitude,
            Longitude = req.Longitude,
            LocationAccuracy = req.LocationAccuracy,
            DeviceId = req.DeviceId ?? Request.Headers["X-Device-Id"].ToString(),
            Source = source,
            // MODEL-VIEWER — pass through the 3D anchor when supplied.
            ModelId = req.ModelId,
            ModelElementGuid = req.ModelElementGuid,
            ModelX = req.ModelX,
            ModelY = req.ModelY,
            ModelZ = req.ModelZ,
        };

        // NEW-LOGIC-01/02 — Save with retry on UNIQUE(ProjectId, IssueCode) collision.
        // A concurrent CreateIssue for the same Type could also have computed nextNum;
        // Postgres raises 23505 (DbUpdateException) — bump and retry up to 5 times.
        _db.Issues.Add(issue);
        int attempts = 0;
        while (true)
        {
            try
            {
                await _db.SaveChangesAsync();
                break;
            }
            catch (DbUpdateException) when (attempts++ < 5)
            {
                _db.Entry(issue).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                nextNum++;
                issue.IssueCode = $"{req.Type}-{nextNum:D4}";
                _db.Issues.Add(issue);
            }
        }
        await _audit.LogAsync("CREATE", "Issue", issue.Id.ToString());

        // NEW-INT-05 — Broadcast IssueCreated to mobile + web clients subscribed to the project.
        _ = _notifHub.Clients.Group($"project-{projectId}").SendAsync("IssueCreated", new
        {
            issue.Id, issue.IssueCode, issue.Type, issue.Title, issue.Priority,
            issue.Status, issue.Assignee, issue.AssigneeUserId, issue.Discipline,
            issue.CreatedBy, issue.CreatedAt, issue.DueDate,
            projectId
        });

        // Phase 165 (NEW-08) — outbound webhook fanout for tenant integrations.
        _webhooks?.FireAndForget(tenantId, projectId, WebhookEventType.IssueCreated, new
        {
            issue.Id, issue.IssueCode, issue.Type, issue.Title, issue.Priority,
            issue.Status, issue.Assignee, issue.Discipline, issue.CreatedBy, issue.CreatedAt, issue.DueDate,
        });

        // SRV-07 — issue creation push must reach project members only, not the whole tenant.
        _ = _notifications.NotifyProjectAsync(projectId, "issues",
            $"New {issue.Type}: {issue.IssueCode}",
            issue.Title,
            new { issue.Id, issue.IssueCode, issue.Type, issue.Priority, projectId });

        // If assigned, send targeted push to assignee.
        // Prefer the resolved FK (NEW-SRV-23); fall back to legacy DisplayName lookup.
        if (issue.AssigneeUserId.HasValue || !string.IsNullOrEmpty(issue.Assignee))
        {
            AppUser? assignee = null;
            if (issue.AssigneeUserId.HasValue)
            {
                assignee = await _db.Users.FirstOrDefaultAsync(u =>
                    u.Id == issue.AssigneeUserId.Value && u.TenantId == tenantId);
            }
            if (assignee == null && !string.IsNullOrEmpty(issue.Assignee))
            {
                assignee = await _db.Users.FirstOrDefaultAsync(u =>
                    u.DisplayName == issue.Assignee && u.TenantId == tenantId);
            }
            if (assignee != null)
            {
                _ = _push.SendToUserAsync(assignee.Id, new Planscape.Core.Interfaces.PushPayload
                {
                    Title = $"Assigned: {issue.IssueCode} [{issue.Priority}]",
                    Body = issue.Title,
                    Channel = "issues",
                    Data = new Dictionary<string, string>
                    {
                        ["type"] = "issue_assigned",
                        ["issueId"] = issue.Id.ToString(),
                        ["issueCode"] = issue.IssueCode,
                        ["priority"] = issue.Priority,
                        ["projectId"] = projectId.ToString()
                    }
                });
            }
        }

        return CreatedAtAction(nameof(GetIssues), new { projectId }, issue);
    }

    [HttpPut("{issueId}")]
    public async Task<ActionResult> UpdateIssue(Guid projectId, Guid issueId, [FromBody] UpdateIssueRequest req)
    {
        var tenantId = GetTenantId();
        var issue = await _db.Issues
            .FirstOrDefaultAsync(i => i.Id == issueId && i.ProjectId == projectId && i.Project!.TenantId == tenantId);
        if (issue == null) return NotFound();

        // NEW-INFO-07 — Capture before/after for the audit log so the activity
        // timeline can show who changed what.
        var diff = new Dictionary<string, object?>();
        if (req.Status != null && req.Status != issue.Status)
        {
            diff["Status"] = new { from = issue.Status, to = req.Status };
            issue.Status = req.Status;
            if (req.Status is "RESOLVED" or "CLOSED")
                issue.ResolvedAt = DateTime.UtcNow;
        }
        if (req.Priority != null && req.Priority != issue.Priority)
        {
            diff["Priority"] = new { from = issue.Priority, to = req.Priority };
            issue.Priority = req.Priority;
        }
        if (req.Assignee != null && req.Assignee != issue.Assignee)
        {
            diff["Assignee"] = new { from = issue.Assignee, to = req.Assignee };
            issue.Assignee = req.Assignee;
        }
        if (req.Description != null && req.Description != issue.Description)
        {
            diff["Description"] = new { changed = true };
            issue.Description = req.Description;
        }

        await _db.SaveChangesAsync();

        var detailsJson = diff.Count > 0
            ? System.Text.Json.JsonSerializer.Serialize(diff)
            : null;
        await _audit.LogAsync("UPDATE", "Issue", issue.Id.ToString(), detailsJson);

        // NEW-INT-05 — Broadcast IssueUpdated with the diff so all subscribed
        // mobile clients refresh.
        _ = _notifHub.Clients.Group($"project-{projectId}").SendAsync("IssueUpdated", new
        {
            issue.Id, issue.IssueCode, issue.Status, issue.Priority,
            issue.Assignee, issue.AssigneeUserId, issue.ResolvedAt,
            updatedAt = DateTime.UtcNow,
            diff, projectId
        });

        return Ok(issue);
    }

    // ── Activity timeline (NEW-INFO-06/07) ─────────────────────────────────
    /// <summary>
    /// Return the audit-log entries for this issue in chronological order.
    /// Includes CREATE + every UPDATE and its diff payload.
    /// </summary>
    [HttpGet("{issueId}/activity")]
    public async Task<ActionResult> GetActivity(Guid projectId, Guid issueId)
    {
        var tenantId = GetTenantId();
        var exists = await _db.Issues.AnyAsync(i =>
            i.Id == issueId && i.ProjectId == projectId && i.Project!.TenantId == tenantId);
        if (!exists) return NotFound();

        var idString = issueId.ToString();
        var entries = await _db.AuditLogs
            .Where(a => a.TenantId == tenantId
                && a.EntityType == "Issue"
                && a.EntityId == idString)
            .OrderBy(a => a.Timestamp)
            .Select(a => new
            {
                a.Id,
                a.Action,
                a.EntityType,
                a.EntityId,
                a.UserId,
                a.Timestamp,
                a.Source,
                a.DetailsJson
            })
            .ToListAsync();

        return Ok(entries);
    }

    // ── Issue Attachments ────────────────────────────────────────────────

    /// <summary>
    /// Upload a file attachment to an issue.
    /// </summary>
    [HttpPost("{issueId}/attachments")]
    [RequestSizeLimit(MaxAttachmentSize)]
    public async Task<ActionResult> UploadAttachment(Guid projectId, Guid issueId, IFormFile file)
    {
        var tenantId = GetTenantId();
        var issue = await _db.Issues
            .FirstOrDefaultAsync(i => i.Id == issueId && i.ProjectId == projectId && i.Project!.TenantId == tenantId);
        if (issue == null) return NotFound("Issue not found");

        if (file.Length == 0) return BadRequest("File is empty");
        if (file.Length > MaxAttachmentSize) return BadRequest($"File exceeds {MaxAttachmentSize / (1024 * 1024)} MB limit");

        var project = await _db.Projects.Include(p => p.Tenant).FirstOrDefaultAsync(p => p.Id == projectId);
        if (project == null) return NotFound("Project not found");

        var tenantSlug = project.Tenant?.Slug ?? tenantId.ToString();

        // Buffer upload, compute SHA-256, then persist via storage abstraction
        using var memStream = new MemoryStream();
        await file.CopyToAsync(memStream);
        var contentHash = Convert.ToHexString(SHA256.HashData(memStream.ToArray())).ToLowerInvariant();

        // NEW-LOGIC-06 — If the client declared an image Content-Type, verify with
        // magic-byte sniffing so a renamed executable can't impersonate a photo.
        memStream.Position = 0;
        if (!string.IsNullOrEmpty(file.ContentType)
            && file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            if (!Planscape.Infrastructure.Security.FileContentValidator.IsImage(memStream, out _))
            {
                return BadRequest(new { error = "File content does not match declared image MIME type" });
            }
        }

        // NEW-LOGIC-07 — Sanitise filename before handing to the storage layer.
        var safeName = Planscape.Infrastructure.Security.FileContentValidator.SanitiseFileName(
            file.FileName, fallback: $"attachment-{DateTime.UtcNow:yyyyMMddHHmmss}");

        memStream.Position = 0;
        var subPath = $"{project.Code}/issues/{issue.IssueCode}";
        var relativePath = await _storage.SaveAsync(tenantSlug, subPath, safeName, memStream);

        // Create a DocumentRecord for the file
        var doc = new DocumentRecord
        {
            ProjectId = projectId,
            FileName = Path.GetFileName(relativePath),
            FilePath = relativePath,
            DocumentType = "ATTACHMENT",
            CdeStatus = "WIP",
            SuitabilityCode = "S0",
            Discipline = issue.Discipline,
            FileSizeBytes = file.Length,
            ContentHash = contentHash,
            UploadedBy = User.FindFirst("display_name")?.Value ?? "Unknown"
        };
        _db.Documents.Add(doc);

        // Link to issue
        var attachment = new IssueAttachment
        {
            IssueId = issueId,
            DocumentId = doc.Id,
            AttachedBy = User.FindFirst("display_name")?.Value ?? "Unknown"
        };
        _db.IssueAttachments.Add(attachment);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("CREATE", "IssueAttachment", attachment.Id.ToString());

        // S04 — generate JPEG thumbnails (150/300/600 px) and extract EXIF GPS for image uploads.
        // Thumbnails are persisted via the same storage abstraction, using a sibling "thumbnails"
        // subfolder so the GetThumbnail endpoint can derive paths deterministically.
        if (!string.IsNullOrEmpty(file.ContentType)
            && ImageContentTypes.Contains(file.ContentType.ToLowerInvariant()))
        {
            try
            {
                memStream.Position = 0;
                var thumbnails = await _thumbnails.GenerateThumbnailsAsync(memStream);
                var baseName = Path.GetFileNameWithoutExtension(relativePath);
                var thumbSubPath = $"{subPath}/thumbnails";
                foreach (var (size, bytes) in thumbnails)
                {
                    using var ms = new MemoryStream(bytes);
                    await _storage.SaveAsync(tenantSlug, thumbSubPath, $"{baseName}_{size}.jpg", ms);
                }

                memStream.Position = 0;
                var (lat, lng) = _thumbnails.ExtractGpsFromExif(memStream);
                // H2 — Promote EXIF coordinates to the parent BimIssue row so the
                // map view + on-site search work even when live GPS was denied at
                // the moment of issue creation. We only fill blanks: a live-GPS
                // value already on the issue always wins. LocationAccuracy is set
                // to 0 to mark "EXIF-sourced" (vs. a positive metres value from
                // expo-location).
                if (lat.HasValue && lng.HasValue)
                {
                    _logger.LogInformation("EXIF GPS extracted for issue {Code}: {Lat},{Lng}",
                        issue.IssueCode, lat.Value, lng.Value);
                    if (!issue.Latitude.HasValue && !issue.Longitude.HasValue)
                    {
                        issue.Latitude = lat.Value;
                        issue.Longitude = lng.Value;
                        issue.LocationAccuracy = 0;
                        await _db.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                // Thumbnail / EXIF failure must never break the upload.
                _logger.LogWarning(ex, "Thumbnail/EXIF generation failed for {File}", file.FileName);
            }
        }

        return Ok(new
        {
            attachment.Id, attachment.IssueId, attachment.DocumentId,
            doc.FileName, doc.FileSizeBytes, doc.ContentHash, attachment.AttachedAt
        });
    }

    /// <summary>
    /// List attachments for an issue.
    /// </summary>
    [HttpGet("{issueId}/attachments")]
    public async Task<ActionResult> GetAttachments(Guid projectId, Guid issueId)
    {
        var tenantId = GetTenantId();
        var issue = await _db.Issues
            .FirstOrDefaultAsync(i => i.Id == issueId && i.ProjectId == projectId && i.Project!.TenantId == tenantId);
        if (issue == null) return NotFound();

        var attachments = await _db.IssueAttachments
            .Where(a => a.IssueId == issueId)
            .Include(a => a.Document)
            .Select(a => new
            {
                a.Id, a.IssueId, a.DocumentId, a.AttachedAt, a.AttachedBy,
                a.Document!.FileName, a.Document.FileSizeBytes, a.Document.ContentHash
            })
            .ToListAsync();

        return Ok(attachments);
    }

    /// <summary>
    /// Remove an attachment from an issue.
    /// </summary>
    [HttpDelete("{issueId}/attachments/{attachmentId}")]
    public async Task<ActionResult> RemoveAttachment(Guid projectId, Guid issueId, Guid attachmentId)
    {
        var tenantId = GetTenantId();
        var attachment = await _db.IssueAttachments
            .Include(a => a.Issue)
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.IssueId == issueId
                && a.Issue!.ProjectId == projectId && a.Issue.Project!.TenantId == tenantId);
        if (attachment == null) return NotFound();

        _db.IssueAttachments.Remove(attachment);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("DELETE", "IssueAttachment", attachmentId.ToString());
        return NoContent();
    }

    /// <summary>
    /// Stream a pre-generated thumbnail for an image attachment.
    /// Thumbnails live in a sibling "thumbnails" folder with names
    /// {originalBaseName}_{size}.jpg.
    /// </summary>
    [HttpGet("{issueId}/attachments/{attachmentId}/thumbnail")]
    public async Task<IActionResult> GetThumbnail(Guid projectId, Guid issueId, Guid attachmentId,
        [FromQuery] int size = 300, CancellationToken ct = default)
    {
        if (!ValidThumbSizes.Contains(size)) size = 300;

        var tenantId = GetTenantId();
        var attachment = await _db.IssueAttachments
            .Include(a => a.Document)
            .Include(a => a.Issue)
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.IssueId == issueId
                && a.Issue!.ProjectId == projectId && a.Issue.Project!.TenantId == tenantId, ct);
        if (attachment?.Document?.FilePath == null) return NotFound();

        // Derive thumbnail path from the original FilePath: replace the final
        // segment with thumbnails/{baseName}_{size}.jpg.
        var originalPath = attachment.Document.FilePath!;
        var dir = Path.GetDirectoryName(originalPath)?.Replace('\\', '/') ?? "";
        var baseName = Path.GetFileNameWithoutExtension(originalPath);
        var thumbPath = string.IsNullOrEmpty(dir)
            ? $"thumbnails/{baseName}_{size}.jpg"
            : $"{dir}/thumbnails/{baseName}_{size}.jpg";

        var stream = await _storage.GetAsync(thumbPath, ct);
        if (stream == null) return NotFound();
        return File(stream, "image/jpeg");
    }

    /// <summary>
    /// Link an existing document to an issue.
    /// </summary>
    [HttpPost("{issueId}/attachments/link")]
    public async Task<ActionResult> LinkDocument(Guid projectId, Guid issueId, [FromBody] LinkAttachmentRequest req)
    {
        var tenantId = GetTenantId();
        var issue = await _db.Issues
            .FirstOrDefaultAsync(i => i.Id == issueId && i.ProjectId == projectId && i.Project!.TenantId == tenantId);
        if (issue == null) return NotFound("Issue not found");

        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == req.DocumentId && d.ProjectId == projectId);
        if (doc == null) return NotFound("Document not found");

        // Check not already linked
        if (await _db.IssueAttachments.AnyAsync(a => a.IssueId == issueId && a.DocumentId == req.DocumentId))
            return Conflict("Document already attached to this issue");

        var attachment = new IssueAttachment
        {
            IssueId = issueId,
            DocumentId = req.DocumentId,
            AttachedBy = User.FindFirst("display_name")?.Value ?? "Unknown"
        };
        _db.IssueAttachments.Add(attachment);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("LINK", "IssueAttachment", attachment.Id.ToString());

        return Ok(new { attachment.Id, attachment.IssueId, attachment.DocumentId, attachment.AttachedAt });
    }

    // ── BCF 2.1 round-trip (Phase 95) ────────────────────────────────────────
    // These endpoints sit on IssuesController (not BcfController) because the
    // round-trip operates against the BimIssue collection under this project.
    // Both call into Planscape.Shared.BCF.BcfEngine — the exact same pure-C#
    // serialiser the Revit plugin uses, so Navisworks/Solibri round-trips
    // produce byte-for-byte identical wire format regardless of whether the
    // ZIP was written by the plugin or the server.

    /// <summary>
    /// Stream a BCF 2.1 .bcfzip built from this project's issues. Optional
    /// status filter narrows to OPEN/IN_PROGRESS for clash-review workflows.
    /// </summary>
    [HttpGet("bcf-export")]
    public async Task<IActionResult> BcfExport(Guid projectId, [FromQuery] string? status, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
        if (project == null) return NotFound("Project not found");

        var query = _db.Issues.AsNoTracking().Where(i => i.ProjectId == projectId);
        if (!string.IsNullOrEmpty(status)) query = query.Where(i => i.Status == status);
        var issues = await query.ToListAsync(ct);

        try
        {
            var coord = issues.Select(ToCoordIssue).ToList();
            var bytes = Planscape.Shared.BCF.BcfEngine.ExportToBytes(coord);
            await _audit.LogAsync("BCF_EXPORT", "Project", projectId.ToString(),
                System.Text.Json.JsonSerializer.Serialize(new { count = coord.Count, status }));
            var fileName = $"planscape-{project.Code}-{DateTime.UtcNow:yyyyMMdd_HHmmss}.bcfzip";
            return File(bytes, "application/octet-stream", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BCF export failed for project {ProjectId}", projectId);
            return Problem(title: "BCF export failed", detail: ex.Message, statusCode: 500);
        }
    }

    /// <summary>
    /// Accept a multipart BCF 2.1 .bcfzip upload, parse it via the shared
    /// BcfEngine, and upsert matching issues by BCF GUID. New topics become
    /// new BimIssues with IssueCode "BCF-xxxxxxxx" (first 8 chars of topic GUID).
    /// </summary>
    [HttpPost("bcf-import")]
    [RequestSizeLimit(100 * 1024 * 1024)]
    [Authorize(Roles = "Admin,Owner,Coordinator,Manager")]
    public async Task<ActionResult> BcfImport(Guid projectId, IFormFile? file, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
        if (project == null) return NotFound("Project not found");
        if (file == null || file.Length == 0) return BadRequest(new { error = "file_required" });

        List<Planscape.Shared.BCF.CoordIssue> parsed;
        try
        {
            using var stream = file.OpenReadStream();
            parsed = Planscape.Shared.BCF.BcfEngine.ImportFromStream(stream);
        }
        catch (Exception ex)
        {
            // BcfEngine.ImportFromStream swallows most failures and returns an
            // empty list; we still guard here in case the underlying IFormFile
            // stream throws before BcfEngine gets it (closed client, read quota).
            _logger.LogError(ex, "BCF import parse failed for project {ProjectId}", projectId);
            return Problem(title: "BCF import failed", detail: ex.Message, statusCode: 500);
        }

        if (parsed.Count == 0)
            return BadRequest(new { error = "no_topics_found", detail = "The uploaded file contained no valid BCF 2.1 topics." });

        int added = 0, updated = 0, skipped = 0;
        foreach (var ci in parsed)
        {
            if (string.IsNullOrEmpty(ci.Guid)) { skipped++; continue; }

            var existing = await _db.Issues.FirstOrDefaultAsync(
                i => i.BcfGuid == ci.Guid && i.ProjectId == projectId, ct);

            if (existing != null)
            {
                existing.Title       = Trim(ci.Title, 240);
                existing.Description = ci.Description ?? existing.Description;
                existing.Type        = UpperOr(ci.Type, existing.Type);
                existing.Priority    = UpperOr(ci.Priority, existing.Priority);
                existing.Status      = UpperOr(ci.Status, existing.Status);
                existing.Assignee    = ci.Assignee ?? existing.Assignee;
                updated++;
            }
            else
            {
                _db.Issues.Add(new BimIssue
                {
                    ProjectId   = projectId,
                    IssueCode   = $"BCF-{ci.Guid.Substring(0, Math.Min(8, ci.Guid.Length))}",
                    Title       = Trim(ci.Title, 240),
                    Description = ci.Description,
                    Type        = UpperOr(ci.Type, "RFI"),
                    Priority    = UpperOr(ci.Priority, "MEDIUM"),
                    Status      = UpperOr(ci.Status, "OPEN"),
                    Assignee    = ci.Assignee,
                    BcfGuid     = ci.Guid,
                    CreatedBy   = User.FindFirst("display_name")?.Value ?? "bcf-import",
                    Source      = "bcf",
                    CreatedAt   = ci.CreationDate == default ? DateTime.UtcNow : ci.CreationDate.ToUniversalTime(),
                });
                added++;
            }
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("BCF_IMPORT", "Project", projectId.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { added, updated, skipped, total = parsed.Count }));

        return Ok(new { added, updated, skipped, total = parsed.Count });
    }

    // Entity-side mapping (server) — kept inline here rather than in Planscape.Shared
    // because BimIssue is an EF Core entity that Planscape.Shared must not take a
    // dependency on. The shared engine only speaks CoordIssue.
    private static Planscape.Shared.BCF.CoordIssue ToCoordIssue(BimIssue i) => new()
    {
        Guid          = string.IsNullOrEmpty(i.BcfGuid) ? i.Id.ToString() : i.BcfGuid!,
        Title         = i.Title ?? "",
        Description   = i.Description,
        Priority      = (i.Priority ?? "MEDIUM").ToUpperInvariant(),
        Type          = (i.Type ?? "RFI").ToUpperInvariant(),
        Status        = (i.Status ?? "OPEN").ToUpperInvariant(),
        Assignee      = i.Assignee,
        Author        = i.CreatedBy,
        CreationDate  = i.CreatedAt,
        ReferenceLink = i.IssueCode,
    };

    private static string Trim(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "(untitled)" : (s.Length > max ? s[..max] : s);

    private static string UpperOr(string? s, string fallback) =>
        string.IsNullOrWhiteSpace(s) ? fallback : s.Trim().ToUpperInvariant();

    [HttpGet("sla")]
    public async Task<ActionResult> GetSLAReport(Guid projectId)
    {
        var tenantId = GetTenantId();
        var issues = await _db.Issues
            .Where(i => i.ProjectId == projectId && i.Project!.TenantId == tenantId
                && i.Status != "CLOSED" && i.Status != "RESOLVED")
            .ToListAsync();

        var violations = issues.Where(i => i.DueDate.HasValue && i.DueDate < DateTime.UtcNow).ToList();
        var byPriority = issues.GroupBy(i => i.Priority)
            .ToDictionary(g => g.Key, g => new { total = g.Count(), overdue = g.Count(i => i.DueDate < DateTime.UtcNow) });

        return Ok(new
        {
            totalOpen = issues.Count,
            violations = violations.Count,
            byPriority,
            oldestOverdue = violations.OrderBy(v => v.DueDate).FirstOrDefault()?.IssueCode,
            avgAgeHours = violations.Any() ? violations.Average(v => (DateTime.UtcNow - v.CreatedAt).TotalHours) : 0
        });
    }

    private static DateTime ComputeSLADeadline(string priority)
    {
        int hours = SLAHours.GetValueOrDefault(priority, 168);
        return DateTime.UtcNow.AddHours(hours);
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record CreateIssueRequest(
    string Type,
    string Title,
    string? Description,
    string? Priority,
    string? Assignee,
    string? AssigneeEmail,
    Guid? AssigneeUserId,
    string? Discipline,
    string? LinkedElementIds,
    double? Latitude,
    double? Longitude,
    double? LocationAccuracy,
    string? DeviceId,
    string? Source,
    // MODEL-VIEWER — 3D anchor captured at creation time.
    // ModelId comes from the mobile creation form's model picker.
    // ModelElementGuid + ModelX/Y/Z come from "create issue here" gestures
    // raised inside the viewer; both halves are nullable so plain RFI flows
    // (no model linkage at all) keep working unchanged.
    Guid? ModelId,
    string? ModelElementGuid,
    double? ModelX,
    double? ModelY,
    double? ModelZ);
public record UpdateIssueRequest(string? Status, string? Priority, string? Assignee, string? Description);
public record LinkAttachmentRequest(Guid DocumentId);
