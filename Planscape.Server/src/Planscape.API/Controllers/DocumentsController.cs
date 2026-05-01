using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.API.Controllers;

/// <summary>
/// ISO 19650 CDE document management with validated state transitions,
/// role-based access control, and approval gating per ISO 19650-2 §5.6.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/[controller]")]
[Authorize]
[EnableRateLimiting("mobile")]
public class DocumentsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    // ISO 19650-2 CDE state machine — valid one-way transitions
    private static readonly Dictionary<string, string[]> ValidTransitions = new()
    {
        ["WIP"] = new[] { "SHARED" },
        ["SHARED"] = new[] { "PUBLISHED", "WIP" }, // WIP = rework
        ["PUBLISHED"] = new[] { "ARCHIVE", "SUPERSEDED" },
        ["ARCHIVE"] = Array.Empty<string>(),
        ["SUPERSEDED"] = Array.Empty<string>(),
        ["WITHDRAWN"] = Array.Empty<string>(),
        ["OBSOLETE"] = Array.Empty<string>()
    };

    // Suitability code mapping per CDE state
    private static readonly Dictionary<string, string> DefaultSuitability = new()
    {
        ["WIP"] = "S0", ["SHARED"] = "S3", ["PUBLISHED"] = "S4", ["ARCHIVE"] = "S7"
    };

    // Minimum role required for each CDE transition (ISO 19650-2 §5.6)
    private static readonly Dictionary<string, UserRole> TransitionRoleRequirements = new()
    {
        ["WIP->SHARED"] = UserRole.Coordinator,       // Coordinator issues for coordination
        ["SHARED->PUBLISHED"] = UserRole.Manager,      // Manager approves for use
        ["SHARED->WIP"] = UserRole.Coordinator,        // Coordinator returns for rework
        ["PUBLISHED->ARCHIVE"] = UserRole.Manager,     // Manager archives
        ["PUBLISHED->SUPERSEDED"] = UserRole.Manager,  // Manager supersedes
    };

    // Transitions that require an explicit approval record before completing
    private static readonly HashSet<string> ApprovalRequiredTransitions = new()
    {
        "SHARED->PUBLISHED"  // Publishing requires prior approval per ISO 19650-2 §5.6
    };

    private readonly IFileStorageService _storage;
    private readonly IGeofenceValidationService _geofence;
    private readonly IThumbnailService _thumbnails;
    private readonly ILogger<DocumentsController> _logger;
    private readonly IAuditService _audit;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly Planscape.Infrastructure.Services.OutboundWebhookDispatcher? _webhooks;

    // Max file size: 100 MB
    private const long MaxFileSize = 100 * 1024 * 1024;

    private static readonly string[] ImageContentTypes = { "image/jpeg", "image/png", "image/webp" };

    public DocumentsController(PlanscapeDbContext db,
        IFileStorageService storage,
        IGeofenceValidationService geofence,
        IThumbnailService thumbnails,
        ILogger<DocumentsController> logger,
        IAuditService audit,
        IHubContext<NotificationHub> hub,
        Planscape.Infrastructure.Services.OutboundWebhookDispatcher? webhooks = null)
    {
        _db = db;
        _storage = storage;
        _geofence = geofence;
        _thumbnails = thumbnails;
        _logger = logger;
        _audit = audit;
        _hub = hub;
        _webhooks = webhooks;
    }

    [HttpGet]
    public async Task<ActionResult> GetDocuments(Guid projectId,
        [FromQuery] string? cdeStatus = null, [FromQuery] string? discipline = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var tenantId = GetTenantId();
        var query = _db.Documents.Where(d => d.ProjectId == projectId && d.Project!.TenantId == tenantId);

        if (!string.IsNullOrEmpty(cdeStatus)) query = query.Where(d => d.CdeStatus == cdeStatus);
        if (!string.IsNullOrEmpty(discipline)) query = query.Where(d => d.Discipline == discipline);

        var total = await query.CountAsync();
        var docs = await query.OrderByDescending(d => d.UploadedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new { items = docs, total, page, pageSize });
    }

    [HttpPost]
    public async Task<ActionResult> CreateDocument(Guid projectId, [FromBody] CreateDocumentRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        var doc = new DocumentRecord
        {
            ProjectId = projectId,
            FileName = req.FileName,
            Description = req.Description,
            DocumentType = req.DocumentType ?? "",
            CdeStatus = "WIP",
            SuitabilityCode = "S0",
            Discipline = req.Discipline,
            Originator = req.Originator,
            Revision = req.Revision,
            UploadedBy = User.FindFirst("display_name")?.Value ?? "Unknown",
            StatusHistoryJson = JsonConvert.SerializeObject(new[]
            {
                new { timestamp = DateTime.UtcNow, oldState = "", newState = "WIP", suitability = "S0",
                    user = User.FindFirst("display_name")?.Value ?? "Unknown" }
            })
        };

        _db.Documents.Add(doc);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("CREATE", "Document", doc.Id.ToString());
        return CreatedAtAction(nameof(GetDocuments), new { projectId }, doc);
    }

    /// <summary>
    /// Upload a file and create a document record in one step.
    /// If a DocumentRecord with the same project + filename already exists,
    /// creates a new DocumentVersion row, increments the version number,
    /// and updates the DocumentRecord to point at the latest file.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<ActionResult> UploadDocument(Guid projectId,
        IFormFile file,
        [FromForm] string? documentType,
        [FromForm] string? discipline,
        [FromForm] string? revision,
        [FromForm] string? description,
        [FromForm] string? originator)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects
            .Include(p => p.Tenant)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        // S7 — geofence parity with IssuesController.CreateIssue. A user
        // off-site shouldn't be uploading documents that purport to be
        // from the site. MobileContextMiddleware parses the
        // X-Latitude / X-Longitude headers; we range-check + boundary-check
        // them before allowing the write.
        if (HttpContext.Items.TryGetValue("Latitude", out var latObj) &&
            HttpContext.Items.TryGetValue("Longitude", out var lngObj) &&
            latObj is double lat && lngObj is double lng)
        {
            if (double.IsNaN(lat) || double.IsNaN(lng) || Math.Abs(lat) > 90 || Math.Abs(lng) > 180)
                return BadRequest(new { error = "Invalid latitude/longitude range" });
            if (!_geofence.IsInsideBoundary(project.BoundaryPolygon, lat, lng))
                return StatusCode(403, new { error = "Device location is outside the project geofence boundary" });
        }

        if (file.Length == 0) return BadRequest("File is empty");
        if (file.Length > MaxFileSize) return BadRequest($"File exceeds {MaxFileSize / (1024 * 1024)} MB limit");

        // S8 — MIME / extension whitelist. The previous code only validated
        // images; an attacker could upload an .exe with a forged Content-Type
        // of application/pdf and it would persist unchecked.
        if (!Planscape.Infrastructure.Security.FileContentValidator
                .IsAllowedDocumentUpload(file.ContentType, file.FileName))
        {
            return BadRequest(new { error = "File type is not permitted for document upload",
                                    contentType = file.ContentType,
                                    fileName = file.FileName });
        }

        // Phase 143 — ISO 19650 naming enforcement (per-project toggle).
        // Skipped for non-deliverable types (ATTACHMENT, PHOTO) since site
        // photos and issue attachments aren't expected to follow the
        // controlled file-naming convention.
        var docTypeUpper = (documentType ?? "").ToUpperInvariant();
        var isDeliverable = docTypeUpper switch
        {
            "ATTACHMENT" => false,
            "PHOTO" => false,
            "" => true, // unspecified counts as deliverable
            _ => true,
        };
        if (project.EnforceIso19650Naming && isDeliverable)
        {
            var validation = Planscape.Infrastructure.Validation.Iso19650NamingValidator
                .Validate(file.FileName);
            if (!validation.IsValid)
            {
                return BadRequest(new
                {
                    error = "ISO 19650 naming convention violated",
                    pattern = validation.Pattern,
                    fileName = file.FileName,
                    issues = validation.Errors,
                });
            }
        }

        var tenantSlug = project.Tenant?.Slug ?? tenantId.ToString();
        var userName = User.FindFirst("display_name")?.Value ?? "Unknown";

        // Buffer upload, compute SHA-256, then persist via storage abstraction
        using var memStream = new MemoryStream();
        await file.CopyToAsync(memStream);
        var contentHash = Convert.ToHexString(SHA256.HashData(memStream.ToArray())).ToLowerInvariant();

        // NEW-LOGIC-06 — If an image MIME type was declared, confirm the bytes match.
        memStream.Position = 0;
        if (!string.IsNullOrEmpty(file.ContentType)
            && file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            if (!Planscape.Infrastructure.Security.FileContentValidator.IsImage(memStream, out _))
                return BadRequest(new { error = "File content does not match declared image MIME type" });
        }

        // NEW-LOGIC-07 — Strip directory/suspect characters from the uploaded filename
        // before it reaches the storage layer.
        var safeName = Planscape.Infrastructure.Security.FileContentValidator.SanitiseFileName(
            file.FileName, fallback: $"document-{DateTime.UtcNow:yyyyMMddHHmmss}");

        memStream.Position = 0;
        var relativePath = await _storage.SaveAsync(tenantSlug, project.Code, safeName, memStream);

        // S04 — generate JPEG thumbnails (150/300/600 px) and extract EXIF GPS for image uploads.
        // Applies to both first uploads and new-version uploads since each gets its own relativePath.
        if (!string.IsNullOrEmpty(file.ContentType)
            && ImageContentTypes.Contains(file.ContentType.ToLowerInvariant()))
        {
            try
            {
                memStream.Position = 0;
                var thumbnails = await _thumbnails.GenerateThumbnailsAsync(memStream);
                var baseName = Path.GetFileNameWithoutExtension(relativePath);
                var thumbSubPath = $"{project.Code}/thumbnails";
                foreach (var (size, bytes) in thumbnails)
                {
                    using var ms = new MemoryStream(bytes);
                    await _storage.SaveAsync(tenantSlug, thumbSubPath, $"{baseName}_{size}.jpg", ms);
                }

                memStream.Position = 0;
                var (lat, lng) = _thumbnails.ExtractGpsFromExif(memStream);
                // DocumentRecord has no GPS columns; surface via logs until a migration adds them.
                if (lat.HasValue && lng.HasValue)
                {
                    _logger.LogInformation("EXIF GPS extracted for {File}: {Lat},{Lng}",
                        file.FileName, lat.Value, lng.Value);
                }
            }
            catch (Exception ex)
            {
                // Thumbnail / EXIF failure must never break the upload.
                _logger.LogWarning(ex, "Thumbnail/EXIF generation failed for {File}", file.FileName);
            }
        }

        // Check for existing document with same project + filename
        var existingDoc = await _db.Documents
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.ProjectId == projectId && d.FileName == file.FileName && d.Project!.TenantId == tenantId);

        if (existingDoc != null)
        {
            // Determine next version number
            var maxVersion = existingDoc.Versions.Count > 0
                ? existingDoc.Versions.Max(v => v.VersionNumber)
                : 1; // existing record without versions is implicitly v1
            var nextVersion = maxVersion + 1;

            // If there are no version rows yet, create one for the original upload
            if (existingDoc.Versions.Count == 0 && !string.IsNullOrEmpty(existingDoc.FilePath))
            {
                _db.DocumentVersions.Add(new DocumentVersion
                {
                    DocumentId = existingDoc.Id,
                    VersionNumber = 1,
                    FilePath = existingDoc.FilePath,
                    FileSizeBytes = existingDoc.FileSizeBytes,
                    ContentHash = existingDoc.ContentHash,
                    UploadedBy = existingDoc.UploadedBy,
                    UploadedAt = existingDoc.UploadedAt
                });
            }

            // Create version row for the new upload
            _db.DocumentVersions.Add(new DocumentVersion
            {
                DocumentId = existingDoc.Id,
                VersionNumber = nextVersion,
                FilePath = relativePath,
                FileSizeBytes = file.Length,
                ContentHash = contentHash,
                UploadedBy = userName,
                UploadedAt = DateTime.UtcNow
            });

            // Update the head record to point at the latest file
            existingDoc.FilePath = relativePath;
            existingDoc.FileSizeBytes = file.Length;
            existingDoc.ContentHash = contentHash;
            existingDoc.Revision = revision ?? $"P{nextVersion:D2}";
            existingDoc.UpdatedAt = DateTime.UtcNow;
            if (description != null) existingDoc.Description = description;

            await _db.SaveChangesAsync();
            await _audit.LogAsync("UPDATE", "Document", existingDoc.Id.ToString(),
                $"{{\"versionNumber\":{nextVersion}}}");

            return Ok(new
            {
                existingDoc.Id, existingDoc.FileName, existingDoc.FilePath, existingDoc.DocumentType,
                existingDoc.CdeStatus, existingDoc.SuitabilityCode, existingDoc.Discipline,
                existingDoc.Revision, existingDoc.FileSizeBytes, existingDoc.ContentHash,
                existingDoc.UploadedBy, existingDoc.UploadedAt,
                VersionNumber = nextVersion,
                Message = $"New version {nextVersion} created"
            });
        }

        // First upload — create new DocumentRecord + initial version row
        var doc = new DocumentRecord
        {
            ProjectId = projectId,
            FileName = Path.GetFileName(relativePath),
            FilePath = relativePath,
            Description = description,
            DocumentType = documentType ?? "",
            CdeStatus = "WIP",
            SuitabilityCode = "S0",
            Discipline = discipline,
            Originator = originator,
            Revision = revision ?? "P01",
            FileSizeBytes = file.Length,
            ContentHash = contentHash,
            UploadedBy = userName,
            StatusHistoryJson = JsonConvert.SerializeObject(new[]
            {
                new { timestamp = DateTime.UtcNow, oldState = "", newState = "WIP", suitability = "S0",
                    user = userName }
            })
        };

        _db.Documents.Add(doc);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("CREATE", "Document", doc.Id.ToString(), "{\"versionNumber\":1}");

        // Create version 1 row
        _db.DocumentVersions.Add(new DocumentVersion
        {
            DocumentId = doc.Id,
            VersionNumber = 1,
            FilePath = relativePath,
            FileSizeBytes = file.Length,
            ContentHash = contentHash,
            UploadedBy = userName,
            UploadedAt = doc.UploadedAt
        });
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetDocuments), new { projectId }, new
        {
            doc.Id, doc.FileName, doc.FilePath, doc.DocumentType,
            doc.CdeStatus, doc.SuitabilityCode, doc.Discipline, doc.Revision,
            doc.FileSizeBytes, doc.ContentHash, doc.UploadedBy, doc.UploadedAt,
            VersionNumber = 1
        });
    }

    /// <summary>
    /// Download a document file by ID.
    /// </summary>
    [HttpGet("{docId}/download")]
    public async Task<ActionResult> DownloadDocument(Guid projectId, Guid docId)
    {
        var tenantId = GetTenantId();
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == docId && d.ProjectId == projectId && d.Project!.TenantId == tenantId);
        if (doc == null) return NotFound();

        // S12 — geofence boundary check for mobile downloads
        if (HttpContext.Items.TryGetValue("Latitude", out var latObj) &&
            HttpContext.Items.TryGetValue("Longitude", out var lngObj) &&
            latObj is double lat && lngObj is double lng)
        {
            var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
            if (project != null && !_geofence.IsInsideBoundary(project.BoundaryPolygon, lat, lng))
                return StatusCode(403, new { error = "Device location is outside the project geofence boundary" });
        }

        if (string.IsNullOrEmpty(doc.FilePath))
            return NotFound("File not found on disk");

        var stream = await _storage.GetAsync(doc.FilePath);
        if (stream == null)
            return NotFound("File not found on disk");

        return File(stream, "application/octet-stream", doc.FileName);
    }

    /// <summary>
    /// Returns the version history for a document.
    /// </summary>
    [HttpGet("{docId}/versions")]
    public async Task<ActionResult> GetVersions(Guid projectId, Guid docId)
    {
        var tenantId = GetTenantId();
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == docId && d.ProjectId == projectId && d.Project!.TenantId == tenantId);
        if (doc == null) return NotFound();

        var versions = await _db.DocumentVersions
            .Where(v => v.DocumentId == docId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new
            {
                v.Id,
                v.VersionNumber,
                v.FileSizeBytes,
                v.ContentHash,
                v.UploadedBy,
                v.UploadedAt
            })
            .ToListAsync();

        return Ok(versions);
    }

    /// <summary>
    /// Download a specific historical version of a document.
    /// </summary>
    [HttpGet("{docId}/versions/{versionNumber:int}/download")]
    public async Task<ActionResult> DownloadVersion(Guid projectId, Guid docId, int versionNumber)
    {
        var tenantId = GetTenantId();
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == docId && d.ProjectId == projectId && d.Project!.TenantId == tenantId);
        if (doc == null) return NotFound();

        var version = await _db.DocumentVersions
            .FirstOrDefaultAsync(v => v.DocumentId == docId && v.VersionNumber == versionNumber);
        if (version == null) return NotFound("Version not found");

        if (string.IsNullOrEmpty(version.FilePath))
            return NotFound("File not found on disk");

        var stream = await _storage.GetAsync(version.FilePath);
        if (stream == null)
            return NotFound("File not found on disk");

        return File(stream, "application/octet-stream", doc.FileName);
    }

    /// <summary>
    /// CDE state transition with ISO 19650 validation.
    /// </summary>
    [HttpPut("{docId}/state")]
    public async Task<ActionResult> TransitionState(Guid projectId, Guid docId, [FromBody] CdeTransitionRequest req)
    {
        var tenantId = GetTenantId();
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == docId && d.ProjectId == projectId && d.Project!.TenantId == tenantId);
        if (doc == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        // Validate transition
        if (!ValidTransitions.TryGetValue(doc.CdeStatus, out var validTargets))
            return BadRequest($"Unknown current state: {doc.CdeStatus}");

        if (!validTargets.Contains(req.NewState))
            return BadRequest($"Invalid CDE transition: {doc.CdeStatus} → {req.NewState}. Valid: {string.Join(", ", validTargets)}");

        // RBAC: check minimum role for this transition
        var roleCheck = CheckTransitionRole(doc.CdeStatus, req.NewState);
        if (roleCheck != null) return roleCheck;

        // Approval gate: check if transition requires prior approval
        var approvalCheck = await CheckApprovalGate(doc.CdeStatus, req.NewState, docId);
        if (approvalCheck != null) return approvalCheck;

        var oldState = doc.CdeStatus;
        doc.CdeStatus = req.NewState;
        doc.SuitabilityCode = req.SuitabilityCode ?? DefaultSuitability.GetValueOrDefault(req.NewState, doc.SuitabilityCode);
        if (req.Revision != null) doc.Revision = req.Revision;
        doc.UpdatedAt = DateTime.UtcNow;

        // Append to status history
        var history = !string.IsNullOrEmpty(doc.StatusHistoryJson)
            ? JsonConvert.DeserializeObject<List<object>>(doc.StatusHistoryJson) ?? new()
            : new List<object>();
        history.Add(new
        {
            timestamp = DateTime.UtcNow, oldState, newState = req.NewState,
            suitability = doc.SuitabilityCode,
            user = User.FindFirst("display_name")?.Value ?? "Unknown"
        });
        doc.StatusHistoryJson = JsonConvert.SerializeObject(history);

        await _db.SaveChangesAsync();
        await _audit.LogAsync("TRANSITION", "Document", doc.Id.ToString(),
            $"{{\"oldState\":\"{oldState}\",\"newState\":\"{req.NewState}\"}}");

        // C2 — real-time push so coordinators in Revit + mobile viewers refresh.
        _ = _hub.Clients.Group($"project-{projectId}").SendAsync("DocumentUpdated", new
        {
            projectId, documentId = docId,
            fileName = doc.FileName, cdeStatus = doc.CdeStatus,
            oldState, suitability = doc.SuitabilityCode,
            revision = doc.Revision,
            updatedAt = doc.UpdatedAt,
            kind = "cde_transition"
        });

        // Phase 165 (NEW-08) — outbound webhook fanout.
        _webhooks?.FireAndForget(tenantId, projectId, WebhookEventType.DocumentTransitioned, new
        {
            documentId = doc.Id, doc.FileName, oldState, newState = doc.CdeStatus,
            suitability = doc.SuitabilityCode, revision = doc.Revision,
            transitionedAt = doc.UpdatedAt
        });

        return Ok(doc);
    }

    /// <summary>
    /// CDE state transition via POST (mobile-compatible endpoint).
    /// Accepts { "newStatus": "SHARED" } body format.
    /// </summary>
    [HttpPost("{docId}/transition")]
    public async Task<ActionResult> TransitionStateMobile(Guid projectId, Guid docId, [FromBody] MobileTransitionRequest req)
    {
        var tenantId = GetTenantId();
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == docId && d.ProjectId == projectId && d.Project!.TenantId == tenantId);
        if (doc == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        if (!ValidTransitions.TryGetValue(doc.CdeStatus, out var validTargets))
            return BadRequest($"Unknown current state: {doc.CdeStatus}");

        if (!validTargets.Contains(req.NewStatus))
            return BadRequest($"Invalid CDE transition: {doc.CdeStatus} → {req.NewStatus}. Valid: {string.Join(", ", validTargets)}");

        // RBAC: check minimum role for this transition
        var roleCheck = CheckTransitionRole(doc.CdeStatus, req.NewStatus);
        if (roleCheck != null) return roleCheck;

        // Approval gate: check if transition requires prior approval
        var approvalCheck = await CheckApprovalGate(doc.CdeStatus, req.NewStatus, docId);
        if (approvalCheck != null) return approvalCheck;

        var oldState = doc.CdeStatus;
        doc.CdeStatus = req.NewStatus;
        doc.SuitabilityCode = DefaultSuitability.GetValueOrDefault(req.NewStatus, doc.SuitabilityCode);
        doc.UpdatedAt = DateTime.UtcNow;

        var history = !string.IsNullOrEmpty(doc.StatusHistoryJson)
            ? JsonConvert.DeserializeObject<List<object>>(doc.StatusHistoryJson) ?? new()
            : new List<object>();
        history.Add(new
        {
            timestamp = DateTime.UtcNow, oldState, newState = req.NewStatus,
            suitability = doc.SuitabilityCode,
            user = User.FindFirst("display_name")?.Value ?? "Unknown"
        });
        doc.StatusHistoryJson = JsonConvert.SerializeObject(history);

        await _db.SaveChangesAsync();
        await _audit.LogAsync("TRANSITION", "Document", doc.Id.ToString(),
            $"{{\"oldState\":\"{oldState}\",\"newState\":\"{req.NewStatus}\"}}");
        return Ok(doc);
    }

    [HttpGet("{docId}/history")]
    public async Task<ActionResult> GetHistory(Guid projectId, Guid docId)
    {
        var tenantId = GetTenantId();
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == docId && d.ProjectId == projectId && d.Project!.TenantId == tenantId);
        if (doc == null) return NotFound();

        var history = !string.IsNullOrEmpty(doc.StatusHistoryJson)
            ? JsonConvert.DeserializeObject<List<object>>(doc.StatusHistoryJson)
            : new List<object>();

        return Ok(new { doc.Id, doc.FileName, doc.CdeStatus, doc.SuitabilityCode, history });
    }

    // ── Approval Endpoints ──

    /// <summary>
    /// Request approval for a CDE state transition (e.g. SHARED→PUBLISHED).
    /// Creates a PENDING DocumentApproval record per ISO 19650-2 §5.6.
    /// </summary>
    [HttpPost("{docId}/approval-request")]
    public async Task<ActionResult> RequestApproval(Guid projectId, Guid docId, [FromBody] ApprovalRequestBody req)
    {
        var tenantId = GetTenantId();
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == docId && d.ProjectId == projectId && d.Project!.TenantId == tenantId);
        if (doc == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        var transition = $"{doc.CdeStatus}->{req.TargetState}";

        // Only allow approval requests for transitions that actually require approval
        if (!ApprovalRequiredTransitions.Contains(transition))
            return BadRequest($"Transition {transition} does not require approval");

        // Validate the transition is valid from current state
        if (!ValidTransitions.TryGetValue(doc.CdeStatus, out var validTargets) || !validTargets.Contains(req.TargetState))
            return BadRequest($"Invalid CDE transition: {doc.CdeStatus} → {req.TargetState}");

        // Check for existing pending approval
        var existing = await _db.DocumentApprovals
            .FirstOrDefaultAsync(a => a.DocumentId == docId && a.Transition == transition && a.Status == "PENDING");
        if (existing != null)
            return Conflict(new { message = "A pending approval already exists for this transition", approvalId = existing.Id });

        var approval = new DocumentApproval
        {
            DocumentId = docId,
            ProjectId = projectId,
            Transition = transition,
            Status = "PENDING",
            RequestedBy = User.FindFirst("display_name")?.Value ?? "Unknown",
            Comments = req.Comments
        };

        _db.DocumentApprovals.Add(approval);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("CREATE", "DocumentApproval", approval.Id.ToString());
        return CreatedAtAction(nameof(GetApprovalStatus), new { projectId, docId }, approval);
    }

    /// <summary>
    /// Decide on a pending approval (APPROVED or REJECTED). Requires Manager+ role.
    /// </summary>
    [HttpPut("{docId}/approval/{approvalId}")]
    public async Task<ActionResult> DecideApproval(Guid projectId, Guid docId, Guid approvalId, [FromBody] ApprovalDecisionBody req)
    {
        var userRole = GetUserRole();
        if (userRole < UserRole.Manager)
            return StatusCode(403, new { message = "Only Manager or above can approve/reject transitions" });

        var tenantId = GetTenantId();
        var approval = await _db.DocumentApprovals
            .FirstOrDefaultAsync(a => a.Id == approvalId && a.DocumentId == docId
                && a.ProjectId == projectId);
        if (approval == null) return NotFound();

        // Verify the document belongs to this tenant
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == docId && d.ProjectId == projectId && d.Project!.TenantId == tenantId);
        if (doc == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        if (approval.Status != "PENDING")
            return BadRequest($"Approval already decided: {approval.Status}");

        if (req.Decision != "APPROVED" && req.Decision != "REJECTED")
            return BadRequest("Decision must be APPROVED or REJECTED");

        approval.Status = req.Decision;
        approval.DecidedBy = User.FindFirst("display_name")?.Value ?? "Unknown";
        approval.DecidedAt = DateTime.UtcNow;
        approval.Comments = req.Comments ?? approval.Comments;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("UPDATE", "DocumentApproval", approval.Id.ToString(),
            $"{{\"decision\":\"{req.Decision}\"}}");
        return Ok(approval);
    }

    /// <summary>
    /// Get current approval status for a document's pending transitions.
    /// </summary>
    [HttpGet("{docId}/approval-status")]
    public async Task<ActionResult> GetApprovalStatus(Guid projectId, Guid docId)
    {
        var tenantId = GetTenantId();
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == docId && d.ProjectId == projectId && d.Project!.TenantId == tenantId);
        if (doc == null) return NotFound();

        var approvals = await _db.DocumentApprovals
            .Where(a => a.DocumentId == docId)
            .OrderByDescending(a => a.RequestedAt)
            .ToListAsync();

        return Ok(new { doc.Id, doc.FileName, doc.CdeStatus, approvals });
    }

    /// <summary>
    /// Phase 143 — dry-run validate a candidate file name against the ISO
    /// 19650 / UK 2021 NA naming pattern. Lets the office dashboard + the
    /// mobile uploader give the user inline feedback before they upload.
    /// Always returns 200 with a structured payload (no body validation
    /// errors result in <c>isValid: true</c>).
    /// </summary>
    [HttpGet("validate-name")]
    public ActionResult ValidateName([FromQuery] string fileName)
    {
        var result = Planscape.Infrastructure.Validation.Iso19650NamingValidator
            .Validate(fileName ?? "");
        return Ok(new
        {
            fileName = fileName ?? "",
            isValid = result.IsValid,
            pattern = result.Pattern,
            issues = result.Errors,
        });
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;

    private UserRole GetUserRole()
    {
        var roleClaim = User.FindFirst("role")?.Value;
        return Enum.TryParse<UserRole>(roleClaim, ignoreCase: true, out var role) ? role : UserRole.Viewer;
    }

    /// <summary>
    /// Check role-based access for a CDE transition. Returns null if allowed, or an error ActionResult if denied.
    /// </summary>
    private ActionResult? CheckTransitionRole(string oldState, string newState)
    {
        var transitionKey = $"{oldState}->{newState}";
        if (TransitionRoleRequirements.TryGetValue(transitionKey, out var requiredRole))
        {
            var userRole = GetUserRole();
            if (userRole < requiredRole)
                return StatusCode(403, new
                {
                    message = $"Insufficient role for {transitionKey} transition. Required: {requiredRole}, Current: {userRole}"
                });
        }
        return null;
    }

    /// <summary>
    /// Check approval gate for transitions that require prior approval (e.g. SHARED→PUBLISHED).
    /// Returns null if no approval required or approval exists, or an error ActionResult if blocked.
    /// </summary>
    private async Task<ActionResult?> CheckApprovalGate(string oldState, string newState, Guid docId)
    {
        var transitionKey = $"{oldState}->{newState}";
        if (ApprovalRequiredTransitions.Contains(transitionKey))
        {
            var hasApproval = await _db.DocumentApprovals
                .AnyAsync(a => a.DocumentId == docId && a.Transition == transitionKey && a.Status == "APPROVED");
            if (!hasApproval)
                return BadRequest(new
                {
                    message = $"Transition {transitionKey} requires an approved DocumentApproval record per ISO 19650-2 §5.6. " +
                              "Use POST {docId}/approval-request to initiate the approval workflow."
                });
        }
        return null;
    }
}

public record CreateDocumentRequest(string FileName, string? DocumentType, string? Discipline, string? Revision, string? Description = null, string? Originator = null);
public record CdeTransitionRequest(string NewState, string? SuitabilityCode, string? Revision);
public record MobileTransitionRequest(string NewStatus);
public record ApprovalRequestBody(string TargetState, string? Comments = null);
public record ApprovalDecisionBody(string Decision, string? Comments = null);
