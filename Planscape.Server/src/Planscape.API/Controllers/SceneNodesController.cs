using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// S5.5 — federation query + chunk ingest. Two endpoints in one
/// controller because they share the SceneNode entity:
///
///   GET  /api/projects/{id}/scene?disciplines=M,E,P
///        → SceneManifest the mobile chunked loader (S5.3) consumes.
///
///   POST /api/scene-nodes/ingest
///        → called by the converter sidecar (S5.2) to register a new
///          chunk after it splits + uploads. Auth via shared bearer
///          token; converter is internal-only.
/// </summary>
[ApiController]
[Route("api")]
public class SceneNodesController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IFileStorageService _storage;

    public SceneNodesController(PlanscapeDbContext db, ITenantContext tenant, IFileStorageService storage)
    {
        _db = db; _tenant = tenant; _storage = storage;
    }

    /// <summary>
    /// Federation manifest for a project. Returns one signed URL per
    /// chunk + AABB + content hash. Tenant-scoped via the global query
    /// filter (S1.1) — there's no path tenant-id parameter to mistype.
    /// </summary>
    [HttpGet("projects/{projectId:guid}/scene")]
    [Authorize]
    public async Task<ActionResult> GetScene(Guid projectId, [FromQuery] string? disciplines, CancellationToken ct)
    {
        var disciplineFilter = (disciplines ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.ToUpperInvariant())
            .ToHashSet();

        var rows = await _db.SceneNodes.AsNoTracking()
            .Where(n => n.ProjectId == projectId && n.DeletedAt == null
                     && (disciplineFilter.Count == 0 || disciplineFilter.Contains(n.Discipline)))
            .ToListAsync(ct);

        if (rows.Count == 0) return NotFound(new { message = "No scene chunks found. Re-publish the project model." });

        double minX = rows.Min(r => r.MinX), minY = rows.Min(r => r.MinY), minZ = rows.Min(r => r.MinZ);
        double maxX = rows.Max(r => r.MaxX), maxY = rows.Max(r => r.MaxY), maxZ = rows.Max(r => r.MaxZ);

        return Ok(new
        {
            projectId,
            generatedAt = DateTime.UtcNow,
            chunks = rows.Select(r => new
            {
                id          = r.Id,
                discipline  = r.Discipline,
                levelCode   = r.LevelCode,
                systemCode  = r.SystemCode,
                url         = $"/api/v1/scene-nodes/{r.Id}/file",
                hash        = r.ContentHash,
                sizeBytes   = r.FileSizeBytes,
                vertexCount = r.VertexCount,
                compression = r.Compression,
                minX = r.MinX, minY = r.MinY, minZ = r.MinZ,
                maxX = r.MaxX, maxY = r.MaxY, maxZ = r.MaxZ,
            }),
            minX, minY, minZ, maxX, maxY, maxZ,
            disciplines = rows.Select(r => r.Discipline).Distinct().ToArray(),
        });
    }

    /// <summary>Stream the chunk binary. Uses tenant-scoped storage from S1.2.</summary>
    [HttpGet("scene-nodes/{nodeId:guid}/file")]
    [Authorize]
    public async Task<ActionResult> GetChunkFile(Guid nodeId, CancellationToken ct)
    {
        var node = await _db.SceneNodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == nodeId && n.DeletedAt == null, ct);
        if (node == null) return NotFound();
        var stream = await _storage.GetAsync(node.StoragePath, ct);
        if (stream == null) return NotFound();
        Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        return File(stream, "model/gltf-binary");
    }

    /// <summary>
    /// Internal ingest endpoint hit by the converter sidecar after it
    /// splits + Draco-compresses a chunk. Auth via shared bearer token
    /// in env; the bearer corresponds to a service account on the
    /// platform tenant.
    /// </summary>
    [HttpPost("scene-nodes/ingest")]
    [AllowAnonymous]
    public async Task<ActionResult> Ingest(
        [FromForm] IFormFile file,
        [FromForm] Guid tenantId,
        [FromForm] Guid projectId,
        [FromForm] Guid sourceModelId,
        [FromForm] string discipline,
        [FromForm] string hash,
        [FromForm] int vertexCount,
        [FromForm] string compression,
        [FromForm] string aabb,
        CancellationToken ct)
    {
        var bearer = Request.Headers["Authorization"].ToString();
        var expected = (Environment.GetEnvironmentVariable("Converter__ApiBearer") ?? "").Trim();
        if (string.IsNullOrEmpty(expected) || !bearer.EndsWith(expected, StringComparison.Ordinal))
            return Unauthorized();

        // Cross-tenant write — bypass the global filter explicitly.
        _db.BypassTenantFilter = true;

        // Idempotent: if a SceneNode with this content hash + project already exists, return it.
        var existing = await _db.SceneNodes.FirstOrDefaultAsync(n => n.ContentHash == hash && n.ProjectId == projectId, ct);
        if (existing != null) return Ok(new { id = existing.Id, deduped = true });

        await using var stream = file.OpenReadStream();
        var path = await _storage.SaveAsync(
            tenantSlug: "t_" + tenantId.ToString("N"),
            projectCode: $"scenes/{projectId:N}",
            fileName: $"{discipline}_{hash[..12]}.glb",
            content: stream, ct: ct);

        var box = System.Text.Json.JsonSerializer.Deserialize<AabbDto>(aabb)
                  ?? throw new ArgumentException("aabb invalid");

        var node = new SceneNode
        {
            TenantId = tenantId,
            ProjectId = projectId,
            SourceModelId = sourceModelId,
            Discipline = discipline.ToUpperInvariant(),
            StoragePath = path,
            ContentHash = hash,
            FileSizeBytes = file.Length,
            VertexCount = vertexCount,
            Compression = compression,
            MinX = box.minX, MinY = box.minY, MinZ = box.minZ,
            MaxX = box.maxX, MaxY = box.maxY, MaxZ = box.maxZ,
        };
        _db.SceneNodes.Add(node);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = node.Id, deduped = false });
    }

    private record AabbDto(double minX, double minY, double minZ, double maxX, double maxY, double maxZ);
}
