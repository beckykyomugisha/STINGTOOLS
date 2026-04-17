using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// MODEL-VIEWER — 3D model CRUD for a project.
///
///   GET    /api/projects/{projectId}/models            — list
///   GET    /api/projects/{projectId}/models/{modelId}  — metadata
///   POST   /api/projects/{projectId}/models            — upload (multipart)
///   GET    /api/projects/{projectId}/models/{modelId}/file        — download the geometry
///   GET    /api/projects/{projectId}/models/{modelId}/element-map — download the JSON sidecar
///   GET    /api/projects/{projectId}/models/{modelId}/thumbnail   — PNG preview (optional)
///   DELETE /api/projects/{projectId}/models/{modelId}  — soft delete
///
/// Tenant isolation is enforced on every call — the project must belong to the
/// caller's tenant. Files are streamed via <see cref="IFileStorageService"/>
/// so S3 / MinIO / local FS all work without controller changes.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/models")]
[Authorize]
public class ModelsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly ILogger<ModelsController> _logger;

    // Upload cap — glTF can be large but anything over this is almost certainly
    // an uncompressed IFC or a federated model that should be split.
    private const long MaxModelSizeBytes = 200L * 1024 * 1024;

    public ModelsController(
        PlanscapeDbContext db,
        IFileStorageService storage,
        ILogger<ModelsController> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    // ── List / metadata ────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult> List(Guid projectId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var rows = await _db.ProjectModels.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.DeletedAt == null)
            .OrderByDescending(m => m.UploadedAt)
            .Select(m => ToMetaDto(m))
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("{modelId:guid}")]
    public async Task<ActionResult> Get(Guid projectId, Guid modelId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var row = await _db.ProjectModels.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == modelId && m.ProjectId == projectId && m.DeletedAt == null, ct);
        if (row == null) return NotFound();
        return Ok(ToMetaDto(row));
    }

    // ── Upload ─────────────────────────────────────────────────────────

    [HttpPost]
    [RequestSizeLimit(MaxModelSizeBytes)]
    [Authorize(Roles = "Admin,Owner,Coordinator")]
    public async Task<ActionResult> Upload(
        Guid projectId,
        [FromForm] UploadModelRequest req,
        CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        if (req.File == null || req.File.Length == 0)
            return BadRequest(new { error = "file_required" });
        if (req.File.Length > MaxModelSizeBytes)
            return BadRequest(new { error = "file_too_large", maxMb = MaxModelSizeBytes / 1024 / 1024 });

        var format = InferFormat(req.File.FileName);
        var project = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound(new { error = "project_not_found" });

        var tenantSlug = await TenantSlug(ct);
        var projectCode = string.IsNullOrWhiteSpace(project.Code) ? project.Id.ToString("N") : project.Code;

        // Hash first so we can short-circuit duplicate uploads.
        string hash;
        using (var hashStream = req.File.OpenReadStream())
        {
            hash = await ComputeHashAsync(hashStream, ct);
        }
        var existing = await _db.ProjectModels.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.ContentHash == hash && m.DeletedAt == null, ct);
        if (existing != null)
        {
            _logger.LogInformation("Model upload skipped — duplicate hash {Hash} for project {ProjectId}", hash, projectId);
            return Conflict(new { error = "duplicate_content", id = existing.Id });
        }

        // Store geometry.
        string geometryPath;
        using (var fs = req.File.OpenReadStream())
        {
            geometryPath = await _storage.SaveAsync(tenantSlug, $"{projectCode}/models", req.File.FileName, fs, ct);
        }

        // Optional element-map + thumbnail sidecars uploaded in the same request.
        string? mapPath = null;
        if (req.ElementMap != null && req.ElementMap.Length > 0)
        {
            if (req.ElementMap.Length > 5 * 1024 * 1024)
                return BadRequest(new { error = "element_map_too_large", maxMb = 5 });
            using var s = req.ElementMap.OpenReadStream();
            mapPath = await _storage.SaveAsync(tenantSlug, $"{projectCode}/models", req.ElementMap.FileName, s, ct);
        }
        string? thumbnailPath = null;
        if (req.Thumbnail != null && req.Thumbnail.Length > 0)
        {
            if (req.Thumbnail.Length > 2 * 1024 * 1024)
                return BadRequest(new { error = "thumbnail_too_large", maxMb = 2 });
            using var s = req.Thumbnail.OpenReadStream();
            thumbnailPath = await _storage.SaveAsync(tenantSlug, $"{projectCode}/models", req.Thumbnail.FileName, s, ct);
        }

        var row = new ProjectModel
        {
            ProjectId = projectId,
            Name = string.IsNullOrWhiteSpace(req.Name) ? Path.GetFileNameWithoutExtension(req.File.FileName) : req.Name!,
            Description = req.Description,
            Discipline = req.Discipline,
            FileName = req.File.FileName,
            Format = format,
            StoragePath = geometryPath,
            ContentHash = hash,
            FileSizeBytes = req.File.Length,
            ThumbnailPath = thumbnailPath,
            ElementMapPath = mapPath,
            ElementCount = req.ElementCount,
            Units = string.IsNullOrWhiteSpace(req.Units) ? "mm" : req.Units!,
            Revision = req.Revision,
            BoundsMinX = req.BoundsMinX,
            BoundsMinY = req.BoundsMinY,
            BoundsMinZ = req.BoundsMinZ,
            BoundsMaxX = req.BoundsMaxX,
            BoundsMaxY = req.BoundsMaxY,
            BoundsMaxZ = req.BoundsMaxZ,
            UploadedBy = User.FindFirst("display_name")?.Value ?? User.Identity?.Name ?? "",
            UploadedByUserId = CurrentUserId(),
            UploadedAt = DateTime.UtcNow,
        };
        _db.ProjectModels.Add(row);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Model uploaded — {ModelId} {Format} {Size} bytes for project {ProjectId}",
            row.Id, row.Format, row.FileSizeBytes, projectId);

        return CreatedAtAction(nameof(Get), new { projectId, modelId = row.Id }, ToMetaDto(row));
    }

    // ── Downloads ──────────────────────────────────────────────────────

    [HttpGet("{modelId:guid}/file")]
    public async Task<IActionResult> DownloadFile(Guid projectId, Guid modelId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var row = await _db.ProjectModels.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == modelId && m.ProjectId == projectId && m.DeletedAt == null, ct);
        if (row == null) return NotFound();
        var stream = await _storage.GetAsync(row.StoragePath, ct);
        if (stream == null) return NotFound();
        return File(stream, GetMimeType(row.Format), row.FileName, enableRangeProcessing: true);
    }

    [HttpGet("{modelId:guid}/element-map")]
    public async Task<IActionResult> DownloadElementMap(Guid projectId, Guid modelId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var row = await _db.ProjectModels.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == modelId && m.ProjectId == projectId && m.DeletedAt == null, ct);
        if (row == null || row.ElementMapPath == null) return NotFound();
        var stream = await _storage.GetAsync(row.ElementMapPath, ct);
        if (stream == null) return NotFound();
        return File(stream, "application/json", $"{row.Name}-elements.json", enableRangeProcessing: true);
    }

    [HttpGet("{modelId:guid}/thumbnail")]
    public async Task<IActionResult> DownloadThumbnail(Guid projectId, Guid modelId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var row = await _db.ProjectModels.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == modelId && m.ProjectId == projectId && m.DeletedAt == null, ct);
        if (row == null || row.ThumbnailPath == null) return NotFound();
        var stream = await _storage.GetAsync(row.ThumbnailPath, ct);
        if (stream == null) return NotFound();
        return File(stream, "image/png", enableRangeProcessing: true);
    }

    // ── Delete ─────────────────────────────────────────────────────────

    [HttpDelete("{modelId:guid}")]
    [Authorize(Roles = "Admin,Owner,Coordinator")]
    public async Task<IActionResult> Delete(Guid projectId, Guid modelId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        var row = await _db.ProjectModels
            .FirstOrDefaultAsync(m => m.Id == modelId && m.ProjectId == projectId && m.DeletedAt == null, ct);
        if (row == null) return NotFound();
        row.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static ModelMetaDto ToMetaDto(ProjectModel m) => new(
        m.Id, m.ProjectId, m.Name, m.Description, m.Discipline, m.FileName,
        m.Format.ToString(), m.FileSizeBytes, m.ContentHash,
        m.ElementMapPath != null, m.ThumbnailPath != null,
        m.ElementCount, m.Units, m.Revision,
        m.BoundsMinX, m.BoundsMinY, m.BoundsMinZ,
        m.BoundsMaxX, m.BoundsMaxY, m.BoundsMaxZ,
        m.UploadedBy, m.UploadedAt);

    private static ModelFormat InferFormat(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".glb"  => ModelFormat.Glb,
            ".gltf" => ModelFormat.Gltf,
            ".ifc"  => ModelFormat.Ifc,
            ".rvt"  => ModelFormat.Rvt,
            ".obj"  => ModelFormat.Obj,
            ".fbx"  => ModelFormat.Fbx,
            _ => ModelFormat.Glb,
        };
    }

    private static string GetMimeType(ModelFormat f) => f switch
    {
        ModelFormat.Glb  => "model/gltf-binary",
        ModelFormat.Gltf => "model/gltf+json",
        ModelFormat.Ifc  => "application/x-step",
        ModelFormat.Obj  => "model/obj",
        ModelFormat.Fbx  => "application/octet-stream",
        ModelFormat.Rvt  => "application/octet-stream",
        _ => "application/octet-stream",
    };

    private static async Task<string> ComputeHashAsync(Stream stream, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        var hashBytes = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private async Task<bool> ProjectInTenant(Guid projectId, CancellationToken ct)
    {
        var tenantId = Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
        if (tenantId == Guid.Empty) return false;
        return await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
    }

    private async Task<string> TenantSlug(CancellationToken ct)
    {
        var tenantId = Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
        if (tenantId == Guid.Empty) return "unknown";
        return (await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Slug)
            .FirstOrDefaultAsync(ct)) ?? "unknown";
    }

    private Guid? CurrentUserId() =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : null;
}

public class UploadModelRequest
{
    public IFormFile? File { get; set; }
    public IFormFile? ElementMap { get; set; }
    public IFormFile? Thumbnail { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Discipline { get; set; }
    public int? ElementCount { get; set; }
    public string? Units { get; set; }
    public string? Revision { get; set; }
    public double? BoundsMinX { get; set; }
    public double? BoundsMinY { get; set; }
    public double? BoundsMinZ { get; set; }
    public double? BoundsMaxX { get; set; }
    public double? BoundsMaxY { get; set; }
    public double? BoundsMaxZ { get; set; }
}

public record ModelMetaDto(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    string? Discipline,
    string FileName,
    string Format,
    long FileSizeBytes,
    string? ContentHash,
    bool HasElementMap,
    bool HasThumbnail,
    int? ElementCount,
    string Units,
    string? Revision,
    double? BoundsMinX, double? BoundsMinY, double? BoundsMinZ,
    double? BoundsMaxX, double? BoundsMaxY, double? BoundsMaxZ,
    string UploadedBy,
    DateTime UploadedAt);
