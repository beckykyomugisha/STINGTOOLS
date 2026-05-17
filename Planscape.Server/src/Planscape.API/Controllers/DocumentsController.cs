using System.Security.Cryptography;
using Hangfire;
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
using Planscape.API.Authorization;

namespace Planscape.API.Controllers;

/// <summary>
/// ISO 19650 CDE document management with validated state transitions,
/// role-based access control, and approval gating per ISO 19650-2 §5.6.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/[controller]")]
[Authorize]
[ProjectAccess]
[EnableRateLimiting("mobile")]
public class DocumentsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    // ISO 19650-2 CDE state machine — valid one-way transitions
    private static readonly Dictionary<string, string[]> ValidTransitions = new()
    {
        ["WIP"] = new[] { "SHARED" },
        ["SHARED"] = new[] { "PUBLISHED", "WIP", "WITHDRAWN" }, // WIP = rework; WITHDRAWN = formal withdrawal
        ["PUBLISHED"] = new[] { "ARCHIVE", "SUPERSEDED", "WITHDRAWN" },
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

    // Gap 3 — ISO 19650-2 suitability code whitelist.
    // S0–S7: work-in-progress through handover; CR: coordination review; AB: as-built.
    private static readonly HashSet<string> ValidSuitabilityCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "S0", "S1", "S2", "S3", "S4", "S5", "S6", "S7", "CR", "AB"
    };

    // Minimum role required for each CDE transition (ISO 19650-2 §5.6)
    private static readonly Dictionary<string, UserRole> TransitionRoleRequirements = new()
    {
        ["WIP->SHARED"] = UserRole.Coordinator,       // Coordinator issues for coordination
        ["SHARED->PUBLISHED"] = UserRole.Manager,      // Manager approves for use
        ["SHARED->WIP"] = UserRole.Coordinator,        // Coordinator returns for rework
        ["PUBLISHED->ARCHIVE"] = UserRole.Manager,     // Manager archives
        ["PUBLISHED->SUPERSEDED"] = UserRole.Manager,  // Manager supersedes
        ["SHARED->WITHDRAWN"] = UserRole.Manager,      // Manager formally withdraws from shared
        ["PUBLISHED->WITHDRAWN"] = UserRole.Manager,   // Manager formally withdraws from published
    };

    // Transitions that require an explicit approval record before completing (ISO 19650-2 §5.6)
    private static readonly HashSet<string> ApprovalRequiredTransitions = new()
    {
        "SHARED->PUBLISHED",   // Publishing requires prior approval
        "PUBLISHED->SUPERSEDED" // Superseding a published document also requires approval
    };

    private readonly IFileStorageService _storage;
    private readonly IGeofenceValidationService _geofence;
    private readonly IThumbnailService _thumbnails;
    private readonly ILogger<DocumentsController> _logger;
    private readonly IAuditService _audit;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly IPushNotificationService _push;
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
        IPushNotificationService push,
        Planscape.Infrastructure.Services.OutboundWebhookDispatcher? webhooks = null)
    {
        _db = db;
        _storage = storage!;
        _geofence = geofence;
        _thumbnails = thumbnails;
        _logger = logger;
        _audit = audit;
        _hub = hub;
        _push = push;
        _webhooks = webhooks;
    }

    /// <summary>
    /// Phase 175 audit P1-14 — issue a presigned PUT URL so the client
    /// uploads the file body directly to object storage without proxying
    /// bytes through the API. After the PUT completes the client calls
    /// POST /finalize to create the DocumentRecord. Files land in
    /// uploads/raw/ and are scanned by ClamAvScannerJob before becoming
    /// available; downloads of unscanned / infected files are refused.
    /// </summary>
    [HttpPost("presign")]
    public async Task<ActionResult> PresignUpload(Guid projectId, [FromBody] PresignUploadRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FileName))
            return BadRequest(new { message = "fileName is required" });
        if (string.IsNullOrWhiteSpace(req.ContentType))
            return BadRequest(new { message = "contentType is required" });
        if (req.SizeBytes <= 0 || req.SizeBytes > MaxFileSize)
            return BadRequest(new { message = $"sizeBytes must be 1..{MaxFileSize}" });
        if (!Planscape.Infrastructure.Security.FileContentValidator
                .IsAllowedDocumentUpload(req.ContentType, req.FileName))
            return BadRequest(new { message = "File type is not permitted." });

        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound();

        // Scoped key: uploads/raw/t_{tenantId}/{projectId}/{guid}/{filename}
        var safeName = Path.GetFileName(req.FileName);
        var objectKey = $"uploads/raw/t_{tenantId:N}/{projectId:N}/{Guid.NewGuid():N}/{safeName}";

        try
        {
            var presigned = await _storage.GetPresignedPutUrlAsync(
                objectKey, req.ContentType, TimeSpan.FromMinutes(10), MaxFileSize);
            return Ok(new
            {
                uploadUrl = presigned.Url,
                objectKey = presigned.ObjectKey,
                expiresAt = presigned.ExpiresAt,
                requiredHeaders = presigned.Headers,
                maxBytes = MaxFileSize,
            });
        }
        catch (NotSupportedException)
        {
            return StatusCode(501, new
            {
                message = "Storage backend does not support presigned uploads. Use POST /upload (multipart) instead.",
            });
        }
    }

    /// <summary>
    /// Phase 175 — finalise a presigned upload by creating the
    /// DocumentRecord. The file is parked at uploads/raw/ until the
    /// AV scanner promotes it. Caller is the document's UploadedBy.
    /// </summary>
    [HttpPost("finalize")]
    public async Task<ActionResult> FinalizeUpload(Guid projectId, [FromBody] FinalizeUploadRequest req)
    {
        // FIX 20 — Scope mismatch between presign and finalise. The objectKey
        // MUST begin with the path segment that was minted during PresignUpload:
        //   uploads/raw/t_{tenantId}/{projectId}/...
        // Both tenantId and projectId come from the AUTHENTICATED JWT claims
        // (via GetTenantId() and the route parameter) — NOT from the request
        // body — so a client cannot finalise a document scoped to a different
        // tenant or project by forging the objectKey. Return Forbid (not
        // BadRequest) so the caller knows this is an authorisation failure.
        var tenantIdForCheck = GetTenantId();
        var expectedPrefix = $"uploads/raw/t_{tenantIdForCheck:N}/{projectId:N}/";
        if (string.IsNullOrWhiteSpace(req.ObjectKey)
            || !req.ObjectKey.StartsWith(expectedPrefix, StringComparison.Ordinal))
            return Forbid(); // scope mismatch — do not reveal expected prefix in response

        var tenantId = tenantIdForCheck; // already resolved above for the scope check
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound();
        // Phase 177 — match Upload/CreateDocument: ACL on bootstrap state + discipline.
        if (await RequireAclCreateAsync(projectId, req.Discipline) is { } aclDenied) return aclDenied;
        // GAP-16 — validate originator code format.
        if (ValidateOriginator(req.Originator) is { } origErr) return origErr;

        // Verify the upload actually landed in storage.
        if (!await _storage.ExistsAsync(req.ObjectKey))
            return BadRequest(new { message = "Upload not found at the given objectKey. PUT to the presigned URL first." });

        // GAP-06 — idempotency: if the client retries the finalize call for the
        // same objectKey, return the existing record rather than creating a duplicate.
        var existingByKey = await _db.Documents
            .FirstOrDefaultAsync(d => d.FilePath == req.ObjectKey && d.ProjectId == projectId && d.Project!.TenantId == tenantId);
        if (existingByKey != null)
            return Ok(new
            {
                existingByKey.Id, existingByKey.FileName, existingByKey.FilePath, existingByKey.ScanStatus,
                note = "Document already finalized for this objectKey.",
            });

        var safeName = Path.GetFileName(req.FileName ?? "");
        if (string.IsNullOrEmpty(safeName)) safeName = Path.GetFileName(req.ObjectKey);

        var doc = new DocumentRecord
        {
            TenantId      = tenantId,
            ProjectId     = projectId,
            FileName      = safeName,
            FilePath      = req.ObjectKey,
            DocumentType  = req.DocumentType ?? "",
            Discipline    = req.Discipline,
            Revision      = req.Revision,
            Description   = req.Description,
            Originator    = req.Originator,
            FileSizeBytes = req.SizeBytes,
            ContentHash   = req.ContentHash,
            UploadedBy    = User.FindFirst("display_name")?.Value ?? User.FindFirst("sub")?.Value ?? "system",
            UploadedAt    = DateTime.UtcNow,
            ScanStatus    = "PENDING",
        };
        _db.Documents.Add(doc);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            doc.Id, doc.FileName, doc.FilePath, doc.ScanStatus,
            note = "Document is awaiting antivirus scan; download will return 423 until the scanner promotes it.",
        });
    }

    [HttpGet]
    public async Task<ActionResult> GetDocuments(Guid projectId,
        [FromQuery] string? cdeStatus = null, [FromQuery] string? discipline = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var tenantId = GetTenantId();
        var query = _db.Documents.Where(d => d.ProjectId == projectId && d.Project!.TenantId == tenantId);

        if (!string.IsNullOrEmpty(cdeStatus)) query = query.Where(d => d.CdeStatus == cdeStatus);
        if (!string.IsNullOrEmpty(discipline)) query = query.Where(d => d.Discipline == discipline);
        // FIX 20 (mobile Fix 15) — full-text search across FileName, DocumentType, and Description.
        // Uses Contains which maps to LIKE '%...%' on Postgres — index-assisted when a pg_trgm
        // GIN index exists on these columns. Safe for small corpora; add the index before
        // enabling this on large document registers.
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(d => d.FileName.Contains(search) ||
                                     d.DocumentType.Contains(search) ||
                                     (d.Description != null && d.Description.Contains(search)));

        // Phase 177 — narrow by per-folder/per-discipline/per-suitability ACL.
        var acl = await Planscape.API.Authorization.ProjectMemberAcl.ResolveAsync(_db, projectId, User);
        query = Planscape.API.Authorization.ProjectMemberAcl.ApplyTo(query, acl);

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
        // Phase 177 — caller must hold WIP + the requested discipline.
        if (await RequireAclCreateAsync(projectId, req.Discipline) is { } aclDenied) return aclDenied;
        // Gap 3 — suitability code whitelist (if caller specifies one explicitly).
        if (req.SuitabilityCode != null && !ValidSuitabilityCodes.Contains(req.SuitabilityCode))
            return BadRequest(new { message = $"Invalid suitability code '{req.SuitabilityCode}'. Valid: {string.Join(", ", ValidSuitabilityCodes.OrderBy(s => s))}" });
        // GAP-16 — validate originator code format.
        if (ValidateOriginator(req.Originator) is { } origErr) return origErr;

        var doc = new DocumentRecord
        {
            ProjectId       = projectId,
            FileName        = req.FileName,
            Description     = req.Description,
            DocumentType    = req.DocumentType ?? "",
            CdeStatus       = "WIP",
            SuitabilityCode = req.SuitabilityCode ?? "S0",
            Discipline      = req.Discipline,
            Originator      = req.Originator,
            Revision        = req.Revision,
            ContainerId     = req.ContainerId,
            UploadedBy      = User.FindFirst("display_name")?.Value ?? "Unknown",
            StatusHistoryJson = JsonConvert.SerializeObject(new[]
            {
                new { timestamp = DateTime.UtcNow, oldState = "", newState = "WIP",
                      suitability = req.SuitabilityCode ?? "S0",
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
        // Phase 177 — block uploads outside the caller's CDE/discipline slice.
        if (await RequireAclCreateAsync(projectId, discipline) is { } aclDenied) return aclDenied;

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
                var (gpsLat, gpsLng) = _thumbnails.ExtractGpsFromExif(memStream);
                // DocumentRecord has no GPS columns; surface via logs until a migration adds them.
                if (gpsLat.HasValue && gpsLng.HasValue)
                {
                    _logger.LogInformation("EXIF GPS extracted for {File}: {Lat},{Lng}",
                        file.FileName, gpsLat.Value, gpsLng.Value);
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

        // GAP-F — Auto BOQ on IFC upload.
        // Replace fire-and-forget Task.Run with a Hangfire job so failures are
        // retried automatically (up to 3 attempts) and appear in the Hangfire
        // dashboard rather than being silently lost.
        if (Path.GetExtension(file.FileName).Equals(".ifc", StringComparison.OrdinalIgnoreCase))
        {
            var currentUserId = User.FindFirst("sub")?.Value ?? "system-ifc-import";
            BackgroundJob.Enqueue<Planscape.API.BackgroundJobs.IfcBoqSeedJob>(
                j => j.ExecuteAsync(doc.ProjectId, relativePath, currentUserId));
        }

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
        // Phase 177 — per-folder ACL.
        if (await RequireAclAsync(projectId, doc) is { } aclDenied) return aclDenied;

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

        // Phase 175 audit P1-15 — refuse downloads of files that haven't
        // cleared antivirus. PENDING returns 423 Locked so clients can
        // retry; INFECTED returns 451 Unavailable for Legal Reasons
        // (the closest standard mapping for "we have it but won't serve
        // it") and the threat name is logged in the server response.
        if (doc.ScanStatus == "PENDING")
            return StatusCode(423, new { message = "File is awaiting antivirus scan. Retry shortly." });
        if (doc.ScanStatus == "INFECTED")
            return StatusCode(451, new { message = "File is quarantined.", threat = doc.ScanThreatName });

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
        if (await RequireAclAsync(projectId, doc) is { } aclDenied) return aclDenied;

        var versions = await _db.DocumentVersions
            .Where(v => v.DocumentId == docId)
            .OrderByDescending(v => v.VersionNumber)
            .Take(200) // Phase 175 audit P1-11 — bound revision history
            .Select(v => new
            {
                v.Id,
                v.VersionNumber,
                v.FileSizeBytes,
                v.ContentHash,
                v.UploadedBy,
                v.UploadedAt,
                v.ChangeDescription
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
        if (await RequireAclAsync(projectId, doc) is { } aclDenied) return aclDenied;

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
    /// GAP-13 — bulk-download: POST a list of document IDs and receive a ZIP
    /// archive containing the current HEAD file for each. Skips docs that the
    /// caller cannot see (per ACL) or whose file is missing / not yet scanned.
    /// Capped at 50 documents per call to keep memory usage bounded.
    /// </summary>
    [HttpPost("bulk-download")]
    public async Task<ActionResult> BulkDownload(Guid projectId, [FromBody] Guid[] documentIds)
    {
        if (documentIds == null || documentIds.Length == 0)
            return BadRequest(new { message = "documentIds must be a non-empty array" });
        if (documentIds.Length > 50)
            return BadRequest(new { message = "Maximum 50 documents per bulk-download" });

        var tenantId = GetTenantId();
        var acl = await Planscape.API.Authorization.ProjectMemberAcl.ResolveAsync(_db, projectId, User);

        // Include SKIPPED-scan status: multipart uploads (legacy path) bypass the
        // AV scanner and are set to SKIPPED rather than CLEAN. Refusing to serve
        // them from bulk-download would break downloads of any pre-scan-era document.
        // PENDING and INFECTED are still excluded.
        var docs = await _db.Documents
            .Where(d => documentIds.Contains(d.Id)
                     && d.ProjectId == projectId
                     && d.Project!.TenantId == tenantId
                     && (d.ScanStatus == "CLEAN" || d.ScanStatus == "SKIPPED")
                     && !string.IsNullOrEmpty(d.FilePath))
            .ToListAsync();

        // Filter by per-member ACL
        docs = docs.Where(d => acl.AllowsDocument(d)).ToList();

        if (docs.Count == 0)
            return NotFound(new { message = "No accessible documents found for the provided IDs." });

        // Guard against unbounded memory: reject if the total uncompressed size
        // exceeds 500 MB. FileSizeBytes is set at upload time; treat 0 as unknown
        // and allow it through (the 50-document cap limits exposure).
        const long MaxBulkBytes = 500L * 1024 * 1024;
        var totalBytes = docs.Sum(d => d.FileSizeBytes);
        if (totalBytes > MaxBulkBytes)
            return BadRequest(new { message = $"Total file size ({totalBytes / 1024 / 1024} MB) exceeds the 500 MB bulk-download limit. Select fewer documents." });

        var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var doc in docs)
            {
                try
                {
                    using var fileStream = await _storage.GetAsync(doc.FilePath!);
                    if (fileStream == null) continue;

                    var entry = archive.CreateEntry(doc.FileName, System.IO.Compression.CompressionLevel.Fastest);
                    await using var entryStream = entry.Open();
                    await fileStream.CopyToAsync(entryStream);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "BulkDownload: skipping document {DocId} due to storage error", doc.Id);
                }
            }
        }

        ms.Position = 0;
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var projPrefix = projectId.ToString("N")[..8];
        return File(ms, "application/zip", $"documents-{projPrefix}-{timestamp}.zip");
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

        // Phase 177 — per-folder ACL: source slice + target slice + target suitability.
        if (await RequireAclTargetAsync(projectId, doc, req.NewState, req.SuitabilityCode) is { } aclDenied)
            return aclDenied;

        if (!ValidTransitions.TryGetValue(doc.CdeStatus, out var validTargets))
            return BadRequest($"Unknown current state: {doc.CdeStatus}");
        if (!validTargets.Contains(req.NewState))
            return BadRequest($"Invalid CDE transition: {doc.CdeStatus} → {req.NewState}. Valid: {string.Join(", ", validTargets)}");

        var roleCheck = CheckTransitionRole(doc.CdeStatus, req.NewState);
        if (roleCheck != null) return roleCheck;

        var approvalCheck = await CheckApprovalGate(doc.CdeStatus, req.NewState, docId, doc.Revision);
        if (approvalCheck != null) return approvalCheck;

        var oldState = doc.CdeStatus;
        return await PerformCdeTransitionAsync(doc, oldState, req.NewState, req.SuitabilityCode, req.Revision, tenantId, projectId, "web");
    }

    /// <summary>
    /// CDE state transition via POST (mobile-compatible endpoint).
    /// Accepts { "newStatus": "SHARED", "suitabilityCode": "S3", "revision": "P02" } body format.
    /// Gap 1 — now shares all logic with TransitionState: signature, stamp job, SignalR, webhooks.
    /// </summary>
    [HttpPost("{docId}/transition")]
    public async Task<ActionResult> TransitionStateMobile(Guid projectId, Guid docId, [FromBody] MobileTransitionRequest req)
    {
        var tenantId = GetTenantId();
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == docId && d.ProjectId == projectId && d.Project!.TenantId == tenantId);
        if (doc == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        // Phase 177 — per-folder ACL.
        if (await RequireAclTargetAsync(projectId, doc, req.NewStatus, req.SuitabilityCode) is { } aclDenied)
            return aclDenied;

        if (!ValidTransitions.TryGetValue(doc.CdeStatus, out var validTargets))
            return BadRequest($"Unknown current state: {doc.CdeStatus}");
        if (!validTargets.Contains(req.NewStatus))
            return BadRequest($"Invalid CDE transition: {doc.CdeStatus} → {req.NewStatus}. Valid: {string.Join(", ", validTargets)}");

        var roleCheck = CheckTransitionRole(doc.CdeStatus, req.NewStatus);
        if (roleCheck != null) return roleCheck;

        var approvalCheck = await CheckApprovalGate(doc.CdeStatus, req.NewStatus, docId, doc.Revision);
        if (approvalCheck != null) return approvalCheck;

        var oldState = doc.CdeStatus;
        return await PerformCdeTransitionAsync(doc, oldState, req.NewStatus, req.SuitabilityCode, req.Revision, tenantId, projectId, "mobile");
    }

    /// <summary>
    /// Shared core for all CDE transition paths (web PUT, mobile POST, plugin sync).
    /// Performs suitability validation, updates the document, creates the e-signature on
    /// SHARED→PUBLISHED, appends history, mints a DocumentRevision snapshot, persists,
    /// enqueues the stamp job, emits SignalR + webhook events.
    /// </summary>
    private async Task<ActionResult> PerformCdeTransitionAsync(
        DocumentRecord doc,
        string oldState,
        string newState,
        string? suitabilityCode,
        string? revision,
        Guid tenantId,
        Guid projectId,
        string source)
    {
        // Gap 1 (Gap 3) — suitability code whitelist + state/suitability pairing (shared for all callers).
        if (!string.IsNullOrEmpty(suitabilityCode) && !ValidSuitabilityCodes.Contains(suitabilityCode))
            return BadRequest(new { message = $"Invalid suitability code '{suitabilityCode}'. Valid: {string.Join(", ", ValidSuitabilityCodes.OrderBy(s => s))}" });
        if (!string.IsNullOrEmpty(suitabilityCode))
        {
            var suitCheck = ValidateSuitabilityForState(newState, suitabilityCode);
            if (suitCheck != null) return suitCheck;
        }

        var effectiveSuitability = suitabilityCode ?? DefaultSuitability.GetValueOrDefault(newState, doc.SuitabilityCode);
        doc.CdeStatus = newState;
        doc.SuitabilityCode = effectiveSuitability;
        if (revision != null) doc.Revision = revision;
        doc.UpdatedAt = DateTime.UtcNow;

        // Gap 1 — e-signature on S4 publication (was only in TransitionState, now shared).
        DocumentSignature? signature = null;
        if (oldState == "SHARED" && newState == "PUBLISHED")
        {
            var publisherName = User.FindFirst("display_name")?.Value ?? "Unknown";
            var publisherId   = User.FindFirst("sub")?.Value ?? "";
            doc.PublishedByUserId = publisherId;
            doc.PublishedByName   = publisherName;
            doc.PublishedAt       = DateTime.UtcNow;
            signature = new DocumentSignature
            {
                TenantId        = tenantId,
                ProjectId       = projectId,
                DocumentId      = doc.Id,
                SignedByUserId  = publisherId,
                SignedByName    = publisherName,
                SignedAt        = DateTime.UtcNow,
                SignatureNote   = effectiveSuitability != null
                    ? $"Published as {effectiveSuitability} — {doc.Revision}"
                    : $"Published — {doc.Revision}",
                WatermarkStatus = string.IsNullOrEmpty(doc.FilePath) ? "SKIPPED" : "PENDING",
            };
            _db.DocumentSignatures.Add(signature);
        }

        // History (GAP-15/GAP-22 — validated, capped at 100 entries).
        var history = LoadAndCapHistory(doc.StatusHistoryJson);
        history.Add(new
        {
            timestamp = DateTime.UtcNow, oldState, newState,
            suitability = doc.SuitabilityCode,
            user = User.FindFirst("display_name")?.Value ?? "Unknown",
            source
        });
        doc.StatusHistoryJson = JsonConvert.SerializeObject(history);

        // Phase 178c (T3-24) — auto-mint a DocumentRevision snapshot at every CDE transition.
        _db.DocumentRevisions.Add(new DocumentRevision
        {
            TenantId              = tenantId,
            DocumentId            = doc.Id,
            Revision              = doc.Revision ?? "P01",
            CdeStateAtRevision    = doc.CdeStatus,
            SuitabilityAtRevision = doc.SuitabilityCode,
            FilePath              = doc.FilePath,
            FileSizeBytes         = doc.FileSizeBytes,
            ContentHash           = doc.ContentHash,
            CreatedBy             = User.FindFirst("display_name")?.Value ?? "Unknown",
            CommentSummary        = $"CDE transition {oldState} → {newState}" + (source != "web" ? $" ({source})" : ""),
            Source                = "auto_cde_transition",
        });

        await _db.SaveChangesAsync();

        // Gap 1 — stamp job now shared across all callers.
        if (signature != null && signature.WatermarkStatus == "PENDING")
            BackgroundJob.Enqueue<Planscape.API.BackgroundJobs.DocumentPublicationStampJob>(
                j => j.ExecuteAsync(signature.Id));

        await _audit.LogAsync("TRANSITION", "Document", doc.Id.ToString(),
            $"{{\"oldState\":\"{oldState}\",\"newState\":\"{newState}\",\"source\":\"{source}\"}}");

        // Gap 1 — SignalR broadcast now shared (was missing from mobile path).
        await _hub.Clients.Groups(
                $"project-{projectId}-cde-{oldState}",
                $"project-{projectId}-cde-{doc.CdeStatus}")
            .SendAsync("DocumentUpdated", new
            {
                projectId, documentId = doc.Id,
                fileName = doc.FileName, cdeStatus = doc.CdeStatus,
                oldState, suitability = doc.SuitabilityCode,
                revision = doc.Revision, updatedAt = doc.UpdatedAt,
                kind = "cde_transition", source
            });

        // Gap 1 — webhook dispatch now shared (was missing from mobile path).
        _webhooks?.FireAndForget(tenantId, projectId, WebhookEventType.DocumentTransitioned, new
        {
            documentId = doc.Id, doc.FileName, oldState, newState = doc.CdeStatus,
            suitability = doc.SuitabilityCode, revision = doc.Revision,
            transitionedAt = doc.UpdatedAt
        });

        return Ok(doc);
    }

    [HttpGet("{docId}/history")]
    public async Task<ActionResult> GetHistory(Guid projectId, Guid docId)
    {
        var tenantId = GetTenantId();
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == docId && d.ProjectId == projectId && d.Project!.TenantId == tenantId);
        if (doc == null) return NotFound();
        if (await RequireAclAsync(projectId, doc) is { } aclDenied) return aclDenied;

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
        if (await RequireAclTargetAsync(projectId, doc, req.TargetState, null) is { } aclDenied) return aclDenied;

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

        // Gap 2 — capture the requestor's user ID for decision notifications,
        // and snapshot the current revision so the approval is scoped to it.
        var requestorUserId = Guid.TryParse(User.FindFirst("sub")?.Value, out var reqUid) ? reqUid : (Guid?)null;

        var approval = new DocumentApproval
        {
            DocumentId = docId,
            ProjectId = projectId,
            Transition = transition,
            Status = "PENDING",
            RequestedBy = User.FindFirst("display_name")?.Value ?? "Unknown",
            RequestedByUserId = requestorUserId,
            RevisionSnapshot = doc.Revision,
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
        if (await RequireAclAsync(projectId, doc) is { } aclDenied) return aclDenied;

        if (approval.Status != "PENDING")
            return BadRequest($"Approval already decided: {approval.Status}");

        if (req.Decision != "APPROVED" && req.Decision != "REJECTED")
            return BadRequest("Decision must be APPROVED or REJECTED");

        // Gap 12 — comments are mandatory when rejecting so the requestor knows why.
        if (req.Decision == "REJECTED" && string.IsNullOrWhiteSpace(req.Comments))
            return BadRequest(new { message = "Comments are required when rejecting an approval." });

        approval.Status = req.Decision;
        approval.DecidedBy = User.FindFirst("display_name")?.Value ?? "Unknown";
        approval.DecidedAt = DateTime.UtcNow;
        approval.Comments = req.Comments ?? approval.Comments;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("UPDATE", "DocumentApproval", approval.Id.ToString(),
            $"{{\"decision\":\"{req.Decision}\"}}");

        // Gap 4 — push the decision back to whoever requested the approval.
        if (approval.RequestedByUserId.HasValue)
        {
            _ = _push.SendToUserAsync(approval.RequestedByUserId.Value, new PushPayload
            {
                Title = req.Decision == "APPROVED" ? "Approval Granted" : "Approval Rejected",
                Body = $"Your request for {approval.Transition} was {req.Decision.ToLowerInvariant()}"
                       + (string.IsNullOrWhiteSpace(approval.Comments) ? "." : $": {approval.Comments}"),
                Channel = "documents",
                Data = new Dictionary<string, string>
                {
                    ["type"] = "approval_decided",
                    ["documentId"] = docId.ToString(),
                    ["approvalId"] = approvalId.ToString(),
                    ["decision"] = req.Decision,
                    ["projectId"] = projectId.ToString()
                }
            });
        }

        // Gap 5 — broadcast ApprovalDecided so the web UI can prompt "Publish now?" on APPROVED.
        await _hub.Clients.Group($"project-{projectId}").SendAsync("ApprovalDecided", new
        {
            projectId, documentId = docId, approvalId,
            transition = approval.Transition, decision = req.Decision,
            decidedBy = approval.DecidedBy, decidedAt = approval.DecidedAt,
            comments = approval.Comments,
            kind = "approval_decided"
        });

        return Ok(approval);
    }

    /// <summary>
    /// Get current approval status for a document's pending transitions.
    /// GAP-19 — supports pagination via ?page and ?pageSize query params.
    /// </summary>
    [HttpGet("{docId}/approval-status")]
    public async Task<ActionResult> GetApprovalStatus(Guid projectId, Guid docId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var tenantId = GetTenantId();
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == docId && d.ProjectId == projectId && d.Project!.TenantId == tenantId);
        if (doc == null) return NotFound();
        if (await RequireAclAsync(projectId, doc) is { } aclDenied) return aclDenied;

        var query = _db.DocumentApprovals
            .Where(a => a.DocumentId == docId)
            .OrderByDescending(a => a.RequestedAt);

        var total = await query.CountAsync();
        var approvals = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            doc.Id, doc.FileName, doc.CdeStatus,
            approvals,
            total,
            page,
            pageSize,
            hasMore = (page * pageSize) < total
        });
    }

    /// <summary>
    /// Phase 177 — single-call sync from the Revit plugin's
    /// <c>DeliverableLifecycle</c>. Finds-or-creates a DocumentRecord
    /// keyed by deliverable number, applies the new lifecycle status, and
    /// returns the resulting record. Idempotent — safe to retry. Plugin
    /// drives lifecycle locally first (the .docx is rendered on disk),
    /// then mirrors the state here so coordinators on web/mobile see the
    /// same truth without the plugin having to track server-side
    /// DocumentIds itself.
    ///
    /// All ACL + role + approval gates of the regular transition path
    /// still apply.
    /// </summary>
    [HttpPost("sync-from-plugin")]
    public async Task<ActionResult> SyncFromPlugin(Guid projectId, [FromBody] PluginDeliverableSyncRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.DocNumber))
            return BadRequest(new { message = "docNumber is required" });

        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        var userName = User.FindFirst("display_name")?.Value ?? "Plugin";

        // Find by plugin doc number — the plugin's stable identity. Stored
        // in FileName so the existing register-list endpoints surface it.
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d => d.ProjectId == projectId
                && d.Project!.TenantId == tenantId
                && d.FileName == req.DocNumber);

        var isCreate = doc == null;
        if (isCreate)
        {
            // Phase 177 — first-time create is gated like a regular upload.
            if (await RequireAclCreateAsync(projectId, req.Discipline) is { } aclCreateDenied)
                return aclCreateDenied;
            doc = new DocumentRecord
            {
                ProjectId       = projectId,
                FileName        = req.DocNumber,
                Description     = req.Title,
                DocumentType    = req.TemplateId ?? "DELIVERABLE",
                CdeStatus       = "WIP",
                SuitabilityCode = "S0",
                Discipline      = req.Discipline,
                Originator      = req.Originator,
                Revision        = req.Revision ?? "P01",
                UploadedBy      = userName,
                StatusHistoryJson = JsonConvert.SerializeObject(new[]
                {
                    new { timestamp = DateTime.UtcNow, oldState = "", newState = "WIP",
                          suitability = "S0", user = userName, source = "plugin" }
                })
            };
            _db.Documents.Add(doc);
        }

        // Phase 177 — ACL on the *target* state and suitability.
        if (await RequireAclTargetAsync(projectId, doc!, req.NewCdeStatus ?? doc!.CdeStatus, req.SuitabilityCode)
                is { } aclDenied)
            return aclDenied;

        // GAP-05 — enforce ValidTransitions in SyncFromPlugin the same way the
        // regular transition endpoint does. The plugin owns lifecycle locally, but
        // the server should not accept arbitrary state jumps (e.g. WIP → PUBLISHED).
        if (!string.IsNullOrEmpty(req.NewCdeStatus) && req.NewCdeStatus != doc!.CdeStatus)
        {
            if (!ValidTransitions.TryGetValue(doc!.CdeStatus, out var validPluginTargets)
                || !validPluginTargets.Contains(req.NewCdeStatus))
            {
                return BadRequest(new
                {
                    message = $"Invalid CDE transition: {doc!.CdeStatus} → {req.NewCdeStatus}. " +
                              $"Valid: {string.Join(", ", ValidTransitions.GetValueOrDefault(doc!.CdeStatus, Array.Empty<string>()))}"
                });
            }

            var roleCheck = CheckTransitionRole(doc!.CdeStatus, req.NewCdeStatus);
            if (roleCheck != null) return roleCheck;

            var approvalCheck = await CheckApprovalGate(doc!.CdeStatus, req.NewCdeStatus, doc!.Id, doc!.Revision);
            if (approvalCheck != null) return approvalCheck;

            var oldState = doc!.CdeStatus;
            doc!.CdeStatus = req.NewCdeStatus;
            doc!.SuitabilityCode = req.SuitabilityCode
                ?? DefaultSuitability.GetValueOrDefault(req.NewCdeStatus, doc!.SuitabilityCode);
            if (!string.IsNullOrEmpty(req.Revision)) doc!.Revision = req.Revision;
            doc!.UpdatedAt = DateTime.UtcNow;

            var history = LoadAndCapHistory(doc!.StatusHistoryJson);
            history.Add(new
            {
                timestamp = DateTime.UtcNow, oldState, newState = req.NewCdeStatus,
                suitability = doc!.SuitabilityCode,
                user = userName, source = "plugin",
                pluginAction = req.Action,
                reason = req.Reason
            });
            doc!.StatusHistoryJson = JsonConvert.SerializeObject(history);
        }
        else if (!string.IsNullOrEmpty(req.Revision) && req.Revision != doc!.Revision)
        {
            // Pure revision bump (no state change) — common for ReIssue.
            doc!.Revision = req.Revision;
            doc!.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync(isCreate ? "PLUGIN_CREATE" : "PLUGIN_TRANSITION",
            "Document", doc!.Id.ToString(),
            JsonConvert.SerializeObject(new
            {
                docNumber = req.DocNumber,
                action    = req.Action,
                state     = doc!.CdeStatus,
                revision  = doc!.Revision
            }));

        // Phase 177 — broadcast only to members whose ACL covers the new state.
        await _hub.Clients.Group($"project-{projectId}-cde-{doc.CdeStatus}")
            .SendAsync("DocumentUpdated", new
        {
            projectId, documentId = doc.Id,
            fileName = doc.FileName, cdeStatus = doc.CdeStatus,
            revision = doc.Revision, suitability = doc.SuitabilityCode,
            kind = "plugin_sync"
        });

        return Ok(new { doc.Id, doc.FileName, doc.CdeStatus, doc.SuitabilityCode, doc.Revision, isCreate });
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

    // ISO 19650 originator code: 2–8 uppercase alphanumeric characters.
    private static readonly System.Text.RegularExpressions.Regex OriginatorCodeRegex =
        new(@"^[A-Z0-9]{2,8}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // GAP-16 — validate originator against the ISO 19650 code pattern.
    private static ActionResult? ValidateOriginator(string? originator)
    {
        if (string.IsNullOrEmpty(originator)) return null; // optional field
        if (!OriginatorCodeRegex.IsMatch(originator))
            return new BadRequestObjectResult(new
            {
                message = $"Invalid originator code '{originator}'. Must be 2–8 uppercase alphanumeric characters per ISO 19650-1 §8."
            });
        return null;
    }

    // GAP-11 — suitability↔state pairing.
    // PUBLISHED requires S4+; SHARED requires at least S1 (not S0/WIP codes).
    // WITHDRAWN rejects any explicit suitability override — the code from the
    // preceding state is preserved as-is by PerformCdeTransitionAsync fallback.
    private static readonly Dictionary<string, string[]> StateAllowedSuitabilities = new()
    {
        ["WIP"]       = new[] { "S0" },
        ["SHARED"]    = new[] { "S1", "S2", "S3", "CR" },
        ["PUBLISHED"] = new[] { "S4", "S5", "S6", "AB" },
        ["ARCHIVE"]   = new[] { "S7" },
        ["SUPERSEDED"]= new[] { "S4", "S5", "S6", "S7", "AB" },
        ["WITHDRAWN"] = Array.Empty<string>(), // no suitability change on withdrawal; callers must omit the field
    };

    private static ActionResult? ValidateSuitabilityForState(string state, string suitabilityCode)
    {
        if (!StateAllowedSuitabilities.TryGetValue(state, out var allowed)) return null;
        if (!allowed.Contains(suitabilityCode, StringComparer.OrdinalIgnoreCase))
            return new BadRequestObjectResult(new
            {
                message = $"Suitability code '{suitabilityCode}' is not valid for CDE state '{state}'. " +
                          $"Allowed: {string.Join(", ", allowed)}"
            });
        return null;
    }

    // GAP-15 / GAP-22 — deserialise, validate and cap the status-history JSON.
    private const int MaxHistoryEntries = 100;

    private static List<object> LoadAndCapHistory(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new List<object>();
        try
        {
            var list = JsonConvert.DeserializeObject<List<object>>(json) ?? new List<object>();
            // Cap to the most recent MaxHistoryEntries entries.
            if (list.Count > MaxHistoryEntries)
                list = list.Skip(list.Count - MaxHistoryEntries).ToList();
            return list;
        }
        catch
        {
            // GAP-15 — if the stored JSON is malformed, start a fresh history
            // rather than propagating the corruption.
            return new List<object>();
        }
    }

    // MapIfcTypeToDiscipline removed — use IfcDisciplineMapper.ToStingCode directly.

    /// <summary>
    /// Phase 177 — return null when the document is within the caller's
    /// per-folder ACL slice; otherwise return 404. We deliberately 404
    /// (not 403) so that listing the project never leaks the existence
    /// of documents the user is not allowed to see.
    /// </summary>
    private async Task<ActionResult?> RequireAclAsync(Guid projectId, DocumentRecord doc)
    {
        var acl = await Planscape.API.Authorization.ProjectMemberAcl.ResolveAsync(_db, projectId, User);
        if (!acl.AllowsDocument(doc)) return NotFound();
        return null;
    }

    /// <summary>
    /// Phase 177 — gate document *creation* on the caller's ACL slice for
    /// the bootstrap state (always WIP for upload paths) and the requested
    /// discipline. Users with a discipline allow-list that excludes the
    /// requested discipline can't smuggle a doc into WIP via upload.
    /// </summary>
    private async Task<ActionResult?> RequireAclCreateAsync(Guid projectId, string? discipline, string bootstrapState = "WIP")
    {
        var acl = await Planscape.API.Authorization.ProjectMemberAcl.ResolveAsync(_db, projectId, User);
        if (!acl.AllowsCde(bootstrapState))
            return StatusCode(403, new { message = $"Your access does not include the {bootstrapState} CDE state." });
        if (!acl.AllowsDiscipline(discipline))
            return StatusCode(403, new { message = $"Your access does not include discipline {discipline ?? "(unset)"}." });
        return null;
    }

    /// <summary>
    /// Phase 177 — also gate transition *targets*: a member with WIP-only
    /// access can't transition a doc INTO SHARED. The source state was
    /// already validated by the AllowsDocument check.
    /// </summary>
    private async Task<ActionResult?> RequireAclTargetAsync(
        Guid projectId, DocumentRecord doc, string newState, string? newSuitability)
    {
        var acl = await Planscape.API.Authorization.ProjectMemberAcl.ResolveAsync(_db, projectId, User);
        if (!acl.AllowsDocument(doc)) return NotFound();
        if (!acl.AllowsCde(newState))
            return StatusCode(403, new { message = $"Your access does not include the {newState} CDE state." });
        if (!string.IsNullOrEmpty(newSuitability) && !acl.AllowsSuitability(newSuitability))
            return StatusCode(403, new { message = $"Your access does not include suitability {newSuitability}." });
        return null;
    }

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
    ///
    /// Phase 178c (T3-12) — also satisfied by a COMPLETED <see cref="ApprovalChain"/>
    /// for the same transition. Documents may use the legacy single-approver path
    /// or the new multi-step chain interchangeably.
    ///
    /// Gap 2 — currentRevision scopes the gate: an approval whose RevisionSnapshot
    /// does not match the document's current revision is treated as stale and
    /// ignored, so a rework bump always forces a fresh approval round.
    /// </summary>
    private async Task<ActionResult?> CheckApprovalGate(string oldState, string newState, Guid docId, string? currentRevision = null)
    {
        var transitionKey = $"{oldState}->{newState}";
        if (ApprovalRequiredTransitions.Contains(transitionKey))
        {
            // Gap 2 — only count approvals whose RevisionSnapshot matches the current
            // revision (or that pre-date the snapshot feature and carry null).
            var hasLegacyApproval = await _db.DocumentApprovals
                .AnyAsync(a => a.DocumentId == docId && a.Transition == transitionKey && a.Status == "APPROVED"
                    && (a.RevisionSnapshot == null || a.RevisionSnapshot == currentRevision));
            var hasCompletedChain = await _db.ApprovalChains
                .AnyAsync(c => c.DocumentId == docId && c.Transition == transitionKey && c.Status == "COMPLETED");
            if (!hasLegacyApproval && !hasCompletedChain)
                return BadRequest(new
                {
                    message = $"Transition {transitionKey} requires an approved DocumentApproval " +
                              "record (or a COMPLETED ApprovalChain) per ISO 19650-2 §5.6. " +
                              "Use POST {docId}/approval-request for the legacy single-approver path " +
                              "or POST {docId}/approval-chain for a multi-step chain."
                });
        }
        return null;
    }
}

public record CreateDocumentRequest(
    string  FileName,
    string? DocumentType,
    string? Discipline,
    string? Revision,
    string? Description     = null,
    string? Originator      = null,
    // Gap 1 — assign to a CDE container folder at creation time
    Guid?   ContainerId     = null,
    // Gap 3 — optional explicit suitability code (validated against whitelist)
    string? SuitabilityCode = null);

// Phase 175 audit P1-14 — presigned-upload DTOs.
public record PresignUploadRequest(string FileName, string ContentType, long SizeBytes);
public record FinalizeUploadRequest(
    string ObjectKey, string? FileName, long SizeBytes, string? ContentHash,
    string? DocumentType, string? Discipline, string? Revision,
    string? Description, string? Originator);
public record CdeTransitionRequest(string NewState, string? SuitabilityCode, string? Revision);
// Gap 1 — mobile request parity with CdeTransitionRequest: suitability + revision pass-through.
public record MobileTransitionRequest(string NewStatus, string? SuitabilityCode = null, string? Revision = null);
public record ApprovalRequestBody(string TargetState, string? Comments = null);

// Phase 177 — DeliverableLifecycle → server mirror.
public record PluginDeliverableSyncRequest(
    string  DocNumber,            // Plugin's stable id ("PRJ-A-001")
    string? Title,
    string? Discipline,
    string? Originator,
    string? Revision,
    string? TemplateId,           // A01 / A02 / A03 / A04 / B06 …
    string? NewCdeStatus,         // WIP / SHARED / PUBLISHED / ARCHIVE
    string? SuitabilityCode,      // S0..S7
    string? Action,               // issued / reissued / published / cancelled / superseded / replaced
    string? Reason);
public record ApprovalDecisionBody(string Decision, string? Comments = null);
