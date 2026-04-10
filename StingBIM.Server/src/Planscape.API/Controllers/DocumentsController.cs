using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// ISO 19650 CDE document management with validated state transitions.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/[controller]")]
[Authorize]
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

    private readonly IConfiguration _config;

    // Max file size: 100 MB
    private const long MaxFileSize = 100 * 1024 * 1024;

    public DocumentsController(PlanscapeDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
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
        return Ok(new { docs, total, page, pageSize });
    }

    [HttpPost]
    public async Task<ActionResult> CreateDocument(Guid projectId, [FromBody] CreateDocumentRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");

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
        return CreatedAtAction(nameof(GetDocuments), new { projectId }, doc);
    }

    /// <summary>
    /// Upload a file and create a document record in one step.
    /// Stores files under {StoragePath}/{tenantSlug}/{projectCode}/.
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

        if (file.Length == 0) return BadRequest("File is empty");
        if (file.Length > MaxFileSize) return BadRequest($"File exceeds {MaxFileSize / (1024 * 1024)} MB limit");

        // Build storage path: {root}/{tenant_slug}/{project_code}/{filename}
        var storageRoot = _config["Storage:Path"] ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        var tenantSlug = project.Tenant?.Slug ?? tenantId.ToString();
        var folder = Path.Combine(storageRoot, tenantSlug, project.Code);
        Directory.CreateDirectory(folder);

        // Deduplicate filename if it already exists
        var fileName = file.FileName;
        var filePath = Path.Combine(folder, fileName);
        if (System.IO.File.Exists(filePath))
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            fileName = $"{name}_{DateTime.UtcNow:yyyyMMddHHmmss}{ext}";
            filePath = Path.Combine(folder, fileName);
        }

        // Write file and compute SHA-256 hash
        string contentHash;
        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }
        await using (var hashStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            var hash = await SHA256.HashDataAsync(hashStream);
            contentHash = Convert.ToHexString(hash).ToLowerInvariant();
        }

        var doc = new DocumentRecord
        {
            ProjectId = projectId,
            FileName = fileName,
            FilePath = filePath,
            Description = description,
            DocumentType = documentType ?? "",
            CdeStatus = "WIP",
            SuitabilityCode = "S0",
            Discipline = discipline,
            Originator = originator,
            Revision = revision,
            FileSizeBytes = file.Length,
            ContentHash = contentHash,
            UploadedBy = User.FindFirst("display_name")?.Value ?? "Unknown",
            StatusHistoryJson = JsonConvert.SerializeObject(new[]
            {
                new { timestamp = DateTime.UtcNow, oldState = "", newState = "WIP", suitability = "S0",
                    user = User.FindFirst("display_name")?.Value ?? "Unknown" }
            })
        };

        _db.Documents.Add(doc);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetDocuments), new { projectId }, new
        {
            doc.Id, doc.FileName, doc.FilePath, doc.DocumentType,
            doc.CdeStatus, doc.SuitabilityCode, doc.Discipline, doc.Revision,
            doc.FileSizeBytes, doc.ContentHash, doc.UploadedBy, doc.UploadedAt
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

        if (string.IsNullOrEmpty(doc.FilePath) || !System.IO.File.Exists(doc.FilePath))
            return NotFound("File not found on disk");

        var stream = new FileStream(doc.FilePath, FileMode.Open, FileAccess.Read);
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

        // Validate transition
        if (!ValidTransitions.TryGetValue(doc.CdeStatus, out var validTargets))
            return BadRequest($"Unknown current state: {doc.CdeStatus}");

        if (!validTargets.Contains(req.NewState))
            return BadRequest($"Invalid CDE transition: {doc.CdeStatus} → {req.NewState}. Valid: {string.Join(", ", validTargets)}");

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

        if (!ValidTransitions.TryGetValue(doc.CdeStatus, out var validTargets))
            return BadRequest($"Unknown current state: {doc.CdeStatus}");

        if (!validTargets.Contains(req.NewStatus))
            return BadRequest($"Invalid CDE transition: {doc.CdeStatus} → {req.NewStatus}. Valid: {string.Join(", ", validTargets)}");

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

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record CreateDocumentRequest(string FileName, string? DocumentType, string? Discipline, string? Revision, string? Description = null, string? Originator = null);
public record CdeTransitionRequest(string NewState, string? SuitabilityCode, string? Revision);
public record MobileTransitionRequest(string NewStatus);
