using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

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
        ILogger<IssuesController> logger)
    {
        _db = db;
        _notifications = notifications;
        _push = push;
        _storage = storage;
        _geofence = geofence;
        _thumbnails = thumbnails;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult> GetIssues(Guid projectId,
        [FromQuery] string? status = null, [FromQuery] string? type = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var tenantId = GetTenantId();
        var query = _db.Issues.Where(i => i.ProjectId == projectId && i.Project!.TenantId == tenantId);

        if (!string.IsNullOrEmpty(status)) query = query.Where(i => i.Status == status);
        if (!string.IsNullOrEmpty(type)) query = query.Where(i => i.Type == type);

        var total = await query.CountAsync();
        var issues = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new
            {
                i.Id, i.IssueCode, i.Type, i.Title, i.Priority, i.Status,
                i.Assignee, i.Discipline, i.Revision, i.CreatedBy, i.CreatedAt, i.DueDate, i.ResolvedAt,
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

        // S12 — geofence boundary check for mobile issue creation
        if (HttpContext.Items.TryGetValue("Latitude", out var latObj) &&
            HttpContext.Items.TryGetValue("Longitude", out var lngObj) &&
            latObj is double lat && lngObj is double lng)
        {
            if (!_geofence.IsInsideBoundary(project.BoundaryPolygon, lat, lng))
                return StatusCode(403, new { error = "Device location is outside the project geofence boundary" });
        }

        // Auto-generate issue code
        var lastIssue = await _db.Issues
            .Where(i => i.ProjectId == projectId && i.Type == req.Type)
            .OrderByDescending(i => i.IssueCode)
            .FirstOrDefaultAsync();

        int nextNum = 1;
        if (lastIssue != null)
        {
            var parts = lastIssue.IssueCode.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[1], out int n)) nextNum = n + 1;
        }

        var issue = new BimIssue
        {
            ProjectId = projectId,
            IssueCode = $"{req.Type}-{nextNum:D4}",
            Type = req.Type,
            Title = req.Title,
            Description = req.Description,
            Priority = req.Priority ?? "MEDIUM",
            Assignee = req.Assignee,
            Discipline = req.Discipline,
            CreatedBy = User.FindFirst("display_name")?.Value ?? "Unknown",
            LinkedElementIds = req.LinkedElementIds,
            DueDate = ComputeSLADeadline(req.Priority ?? "MEDIUM")
        };

        _db.Issues.Add(issue);
        await _db.SaveChangesAsync();

        // Push notification for new issue
        _ = _notifications.NotifyAsync(tenantId, "issues",
            $"New {issue.Type}: {issue.IssueCode}",
            issue.Title,
            new { issue.Id, issue.IssueCode, issue.Type, issue.Priority, projectId });

        // If assigned, send targeted push to assignee
        if (!string.IsNullOrEmpty(issue.Assignee))
        {
            var assignee = await _db.Users.FirstOrDefaultAsync(u =>
                u.DisplayName == issue.Assignee && u.TenantId == tenantId);
            if (assignee != null)
            {
                _ = _push.SendToUserAsync(assignee.Id, new Planscape.Core.Entities.PushPayload
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

        if (req.Status != null)
        {
            issue.Status = req.Status;
            if (req.Status is "RESOLVED" or "CLOSED")
                issue.ResolvedAt = DateTime.UtcNow;
        }
        if (req.Priority != null) issue.Priority = req.Priority;
        if (req.Assignee != null) issue.Assignee = req.Assignee;
        if (req.Description != null) issue.Description = req.Description;

        await _db.SaveChangesAsync();
        return Ok(issue);
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

        memStream.Position = 0;
        var subPath = $"{project.Code}/issues/{issue.IssueCode}";
        var relativePath = await _storage.SaveAsync(tenantSlug, subPath, file.FileName, memStream);

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
                // If parent issue has no GPS, populate from EXIF. BimIssue has no GPS columns
                // in the current schema, so we surface the coordinates via logs for now; a
                // follow-up migration can promote them to first-class fields.
                if (lat.HasValue && lng.HasValue)
                {
                    _logger.LogInformation("EXIF GPS extracted for issue {Code}: {Lat},{Lng}",
                        issue.IssueCode, lat.Value, lng.Value);
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

        return Ok(new { attachment.Id, attachment.IssueId, attachment.DocumentId, attachment.AttachedAt });
    }

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

public record CreateIssueRequest(string Type, string Title, string? Description, string? Priority, string? Assignee, string? Discipline, string? LinkedElementIds);
public record UpdateIssueRequest(string? Status, string? Priority, string? Assignee, string? Description);
public record LinkAttachmentRequest(Guid DocumentId);
