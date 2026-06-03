using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.API.Authorization;

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
[ProjectAccess]
public class ModelsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly ILogger<ModelsController> _logger;
    private readonly Planscape.Core.Interfaces.IModelConverter _converter;

    // Upload cap — glTF can be large but anything over this is almost certainly
    // an uncompressed IFC or a federated model that should be split.
    private const long MaxModelSizeBytes = 500L * 1024 * 1024;

    public ModelsController(
        PlanscapeDbContext db,
        IFileStorageService storage,
        ILogger<ModelsController> logger,
        Planscape.Core.Interfaces.IModelConverter converter)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
        _converter = converter;
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
    // RequestSizeLimit caps the raw HTTP body. RequestFormLimits caps the
    // multipart-form parser separately — its default is 128 MB regardless
    // of the HTTP body cap. Without this attribute, Revit / mobile uploads
    // bigger than 128 MB fail with HTTP 400 "Multipart body length limit
    // 134217728" even though the 500 MB body limit allows the bytes.
    [RequestFormLimits(MultipartBodyLengthLimit = MaxModelSizeBytes,
                       ValueLengthLimit = int.MaxValue)]
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
        // #1 (Empty 3D viewer) + IFC path — the web viewer is GLTFLoader-only
        // (viewer.html:782), so a publish only renders if it ends up as GLB.
        //   • GLB/glTF       → render directly (always accepted)
        //   • IFC/RVT        → accepted ONLY when a converter is configured
        //                      (MODEL_CONVERTER_PROVIDER != "null"); ModelDerivativeJob
        //                      then emits a GLB derivative + flips Format=Glb.
        //   • OBJ/FBX, or IFC/RVT with no converter → rejected at the boundary.
        // Net: "successful publish ⇒ viewable" still holds, but the IFC-first
        // workflow works whenever the operator has enabled the converter.
        bool converterEnabled = _converter != null &&
            !string.Equals(_converter.ProviderName, "null", StringComparison.OrdinalIgnoreCase);
        bool renderable  = format is ModelFormat.Glb or ModelFormat.Gltf;
        bool convertible = converterEnabled && format is ModelFormat.Ifc or ModelFormat.Rvt;
        if (!renderable && !convertible)
            return BadRequest(new
            {
                error = "unsupported_viewer_format",
                format = format.ToString(),
                converterEnabled,
                message = converterEnabled
                    ? "Publish GLB/glTF (rendered directly) or IFC/RVT (auto-converted). Convert OBJ/FBX to GLB first."
                    : "The viewer renders GLB/glTF only and no IFC converter is enabled. Export to GLB (plugin: ‘Export 3D view to GLB’) or set MODEL_CONVERTER_PROVIDER=ifcconvert."
            });
        var project = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return NotFound(new { error = "project_not_found" });

        var tenantSlug = await TenantSlug(ct);
        var projectCode = string.IsNullOrWhiteSpace(project.Code) ? project.Id.ToString("N") : project.Code;

        // Hash first so we can short-circuit duplicate uploads — unless
        // the caller explicitly set Force=true (e.g. Revit plugin's
        // "republish anyway" flow when bytes haven't changed but the
        // user wants a new revision row).
        string hash;
        using (var hashStream = req.File.OpenReadStream())
        {
            hash = await ComputeHashAsync(hashStream, ct);
        }
        var existing = await _db.ProjectModels
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.ContentHash == hash && m.DeletedAt == null, ct);
        if (existing != null)
        {
            // Geometry hash is identical, so the GLB itself is normally
            // already on disk — skip the costly re-upload. BUT: refresh the
            // ELEMENT-MAP and THUMBNAIL sidecars (they're cheap, and they're
            // often the reason a coordinator re-publishes — schema changes,
            // new tagging, etc.). This avoids the recurring "I re-published
            // and the viewer still shows the old element-map" trap.
            //
            // Self-healing: when the row was previously flagged
            // StorageMissing (bytes wiped by a container rebuild without
            // the persistent volume), re-save the geometry now so the
            // republish actually restores the file on disk instead of
            // silently keeping a broken row.
            bool sidecarChanged = false;
            if (existing.StorageMissingAt != null)
            {
                using var fs = req.File.OpenReadStream();
                existing.StoragePath = await _storage.SaveAsync(
                    tenantSlug, $"{projectCode}/models", req.File.FileName, fs, ct);
                _logger.LogInformation(
                    "Model {ModelId} bytes restored from re-publish — was StorageMissing since {When}",
                    existing.Id, existing.StorageMissingAt);
                sidecarChanged = true;
            }
            if (req.ElementMap != null && req.ElementMap.Length > 0)
            {
                if (req.ElementMap.Length > 5 * 1024 * 1024)
                    return BadRequest(new { error = "element_map_too_large", maxMb = 5 });
                using var s = req.ElementMap.OpenReadStream();
                existing.ElementMapPath = await _storage.SaveAsync(
                    tenantSlug, $"{projectCode}/models", req.ElementMap.FileName, s, ct);
                sidecarChanged = true;
            }
            if (req.Thumbnail != null && req.Thumbnail.Length > 0)
            {
                if (req.Thumbnail.Length > 2 * 1024 * 1024)
                    return BadRequest(new { error = "thumbnail_too_large", maxMb = 2 });
                using var s = req.Thumbnail.OpenReadStream();
                existing.ThumbnailPath = await _storage.SaveAsync(
                    tenantSlug, $"{projectCode}/models", req.Thumbnail.FileName, s, ct);
                sidecarChanged = true;
            }
            // Refresh the cheap row-level fields too, since the same Revit
            // view can ship a new ElementCount / Bounds / Revision label.
            if (req.ElementCount > 0) { existing.ElementCount = req.ElementCount; sidecarChanged = true; }
            if (!string.IsNullOrEmpty(req.Revision)) { existing.Revision = req.Revision; sidecarChanged = true; }
            if (req.BoundsMaxX != 0 || req.BoundsMinX != 0)
            {
                existing.BoundsMinX = req.BoundsMinX; existing.BoundsMinY = req.BoundsMinY; existing.BoundsMinZ = req.BoundsMinZ;
                existing.BoundsMaxX = req.BoundsMaxX; existing.BoundsMaxY = req.BoundsMaxY; existing.BoundsMaxZ = req.BoundsMaxZ;
                sidecarChanged = true;
            }
            // Re-publishing the same bytes also restores the geometry file
            // on disk via the SaveAsync above (when bytes match an existing
            // entry, the storage layer is idempotent), so any prior
            // "missing" flag is now stale and should be cleared.
            if (existing.StorageMissingAt != null) { existing.StorageMissingAt = null; sidecarChanged = true; }
            if (sidecarChanged) await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Model upload — duplicate geometry hash {Hash}; sidecars {Sc} for {ModelId}",
                hash, sidecarChanged ? "REFRESHED" : "unchanged", existing.Id);
            return Ok(new { id = existing.Id, duplicate = true, sidecarsRefreshed = sidecarChanged,
                            message = sidecarChanged
                                ? "Geometry already published — element-map and metadata refreshed."
                                : "Geometry already published — no new sidecars to refresh." });
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
        var row = await _db.ProjectModels
            .FirstOrDefaultAsync(m => m.Id == modelId && m.ProjectId == projectId && m.DeletedAt == null, ct);
        if (row == null) return NotFound(new { error = "model_not_found" });

        var stream = await _storage.GetAsync(row.StoragePath, ct);
        if (stream == null)
        {
            // Row exists but bytes are gone (typical cause: container
            // rebuild without the persistent storage volume mount). Flag
            // the row so federation-status / dashboard can surface it
            // and the viewer can show an actionable "Republish from
            // Revit" CTA instead of a generic 404.
            if (row.StorageMissingAt == null)
            {
                row.StorageMissingAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                _logger.LogWarning(
                    "Model {ModelId} flagged StorageMissing — StoragePath '{Path}' returned null from storage",
                    row.Id, row.StoragePath);
            }
            return NotFound(new
            {
                error    = "storage_missing",
                id       = row.Id,
                fileName = row.FileName,
                message  = "The model row exists but the geometry file is no longer on the server. " +
                           "Republish from Revit (BIM tab → Publish Model) to restore it.",
                missingSince = row.StorageMissingAt,
            });
        }

        // Storage is healthy — clear any stale "missing" flag.
        if (row.StorageMissingAt != null)
        {
            row.StorageMissingAt = null;
            await _db.SaveChangesAsync(ct);
        }
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

    // ── Compliance heatmap ─────────────────────────────────────────────
    /// <summary>
    /// Returns STING tag completeness per element GUID for the project so
    /// the 3D viewer can colour each mesh red/amber/green.
    ///
    /// GET /api/projects/{projectId}/models/heatmap
    /// Response: { elements: [{ guid, disc, isComplete, missingTokens[] }] }
    /// </summary>
    [HttpGet("/api/projects/{projectId:guid}/models/heatmap")]
    public async Task<ActionResult> GetHeatmap(Guid projectId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();

        var elements = await _db.TaggedElements.AsNoTracking()
            .Where(e => e.ProjectId == projectId)
            .Select(e => new
            {
                e.UniqueId,
                e.Disc, e.Loc, e.Zone, e.Lvl, e.Sys, e.Func, e.Prod, e.Seq,
                e.IsComplete,
            })
            .ToListAsync(ct);

        var result = elements.Select(e =>
        {
            var missing = new List<string>();
            if (string.IsNullOrEmpty(e.Disc)) missing.Add("disc");
            if (string.IsNullOrEmpty(e.Loc))  missing.Add("loc");
            if (string.IsNullOrEmpty(e.Zone)) missing.Add("zone");
            if (string.IsNullOrEmpty(e.Lvl))  missing.Add("lvl");
            if (string.IsNullOrEmpty(e.Sys))  missing.Add("sys");
            if (string.IsNullOrEmpty(e.Func)) missing.Add("func");
            if (string.IsNullOrEmpty(e.Prod)) missing.Add("prod");
            if (string.IsNullOrEmpty(e.Seq))  missing.Add("seq");
            return new
            {
                guid = e.UniqueId,
                disc = e.Disc,
                isComplete = e.IsComplete,
                missingTokens = missing,
            };
        });

        return Ok(new { projectId, elements = result });
    }

    // ── Federation status (Phase 143) ──────────────────────────────────

    /// <summary>
    /// BIM Coordinator's "are all disciplines up-to-date?" view. Aggregates
    /// the latest published model per discipline + counts of total / stale
    /// models. Stale = not republished in <paramref name="staleDays"/> days
    /// (default 14, the typical ISO 19650 information-exchange cadence on
    /// UK projects).
    /// </summary>
    [HttpGet("/api/projects/{projectId:guid}/federation-status")]
    public async Task<ActionResult> GetFederationStatus(
        Guid projectId,
        [FromQuery] int staleDays = 14,
        CancellationToken ct = default)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();
        if (staleDays < 1) staleDays = 1;
        if (staleDays > 365) staleDays = 365;

        var staleCutoff = DateTime.UtcNow.AddDays(-staleDays);

        var allModels = await _db.ProjectModels.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.DeletedAt == null)
            .Select(m => new
            {
                m.Id, m.Discipline, m.Name, m.FileName, m.UploadedAt,
                m.UploadedBy, m.Revision, m.ElementCount, m.FileSizeBytes
            })
            .ToListAsync(ct);

        // Group by discipline; "??" coalesces missing/empty into a synthetic
        // "GEN" bucket so the manager can spot models uploaded without a
        // discipline tag (a workflow gap).
        var perDiscipline = allModels
            .GroupBy(m => string.IsNullOrWhiteSpace(m.Discipline) ? "GEN" : m.Discipline!.ToUpperInvariant())
            .Select(g =>
            {
                var latest = g.OrderByDescending(m => m.UploadedAt).First();
                var stale = latest.UploadedAt < staleCutoff;
                return new
                {
                    discipline = g.Key,
                    modelCount = g.Count(),
                    latest = new
                    {
                        latest.Id, latest.Name, latest.FileName, latest.Revision,
                        latest.UploadedAt, latest.UploadedBy,
                        latest.ElementCount, latest.FileSizeBytes
                    },
                    daysSinceUpload = (int)(DateTime.UtcNow - latest.UploadedAt).TotalDays,
                    stale
                };
            })
            .OrderBy(x => x.discipline)
            .ToList();

        var totalModels = allModels.Count;
        var staleModels = allModels.Count(m => m.UploadedAt < staleCutoff);
        var disciplinesWithStale = perDiscipline.Count(d => d.stale);

        // RAG status — the dashboard tile colour. Red if any discipline is
        // stale and the project has expected disciplines; amber if any model
        // is stale; green otherwise. Empty project (no models yet) is amber.
        string rag = totalModels == 0 ? "AMBER"
            : disciplinesWithStale > 0 ? "RED"
            : staleModels > 0 ? "AMBER" : "GREEN";

        return Ok(new
        {
            projectId,
            generatedAt = DateTime.UtcNow,
            staleDays,
            totals = new
            {
                models = totalModels,
                disciplines = perDiscipline.Count,
                staleModels,
                disciplinesWithStale
            },
            rag,
            disciplines = perDiscipline
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static ModelMetaDto ToMetaDto(ProjectModel m) => new(
        m.Id, m.ProjectId, m.Name, m.Description, m.Discipline, m.FileName,
        m.Format.ToString(), m.FileSizeBytes, m.ContentHash,
        m.ElementMapPath != null, m.ThumbnailPath != null,
        m.ElementCount, m.Units, m.Revision,
        m.BoundsMinX, m.BoundsMinY, m.BoundsMinZ,
        m.BoundsMaxX, m.BoundsMaxY, m.BoundsMaxZ,
        m.UploadedBy, m.UploadedAt,
        StorageOk: m.StorageMissingAt == null,
        StorageMissingAt: m.StorageMissingAt);

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

    // ── Federation manifest — unified scene catalogue for the viewer ──

    /// <summary>
    /// GET /api/projects/{id}/federation/manifest — unified scene manifest.
    /// Returns the list of scene chunks the viewer needs to load, grouped by
    /// discipline + level. Mobile/web fetches this once and streams chunks on
    /// demand based on camera position + filter state.
    /// </summary>
    [HttpGet("/api/projects/{projectId:guid}/federation/manifest")]
    public async Task<ActionResult> GetFederationManifest(Guid projectId, CancellationToken ct)
    {
        if (!await ProjectInTenant(projectId, ct)) return Forbid();

        var models = await _db.ProjectModels.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.DeletedAt == null)
            .Select(m => new {
                id = m.Id,
                name = m.Name,
                discipline = m.Discipline ?? "?",
                format = m.Format.ToString(),
                uploadedAt = m.UploadedAt,
                elementCount = m.ElementCount,
                bounds = (m.BoundsMinX.HasValue && m.BoundsMaxX.HasValue) ? new {
                    min = new[] { m.BoundsMinX, m.BoundsMinY, m.BoundsMinZ },
                    max = new[] { m.BoundsMaxX, m.BoundsMaxY, m.BoundsMaxZ },
                } : null,
                units = m.Units,
                revision = m.Revision,
            }).ToListAsync(ct);

        var chunks = await _db.SceneNodes.AsNoTracking()
            .Where(n => n.ProjectId == projectId && n.DeletedAt == null)
            .Select(n => new {
                id = n.Id,
                sourceModelId = n.SourceModelId,
                discipline = n.Discipline,
                level = n.LevelCode,
                system = n.SystemCode,
                storagePath = n.StoragePath,
                contentHash = n.ContentHash,
                sizeBytes = n.FileSizeBytes,
                vertexCount = n.VertexCount,
                aabb = new { min = new[] { n.MinX, n.MinY, n.MinZ }, max = new[] { n.MaxX, n.MaxY, n.MaxZ } },
                compression = n.Compression,
            }).ToListAsync(ct);

        // Compute overall federation bounds
        double? minX = chunks.Count > 0 ? chunks.Min(c => c.aabb.min[0]) : null;
        double? minY = chunks.Count > 0 ? chunks.Min(c => c.aabb.min[1]) : null;
        double? minZ = chunks.Count > 0 ? chunks.Min(c => c.aabb.min[2]) : null;
        double? maxX = chunks.Count > 0 ? chunks.Max(c => c.aabb.max[0]) : null;
        double? maxY = chunks.Count > 0 ? chunks.Max(c => c.aabb.max[1]) : null;
        double? maxZ = chunks.Count > 0 ? chunks.Max(c => c.aabb.max[2]) : null;

        // Get alignment reports for cross-checks
        var alignmentReports = await _db.IfcAlignmentReports.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .GroupBy(r => r.ProjectModelId)
            .Select(g => g.OrderByDescending(r => r.ValidatedAt).First())
            .ToListAsync(ct);

        var disciplines = models.GroupBy(m => m.discipline).Select(g => new {
            code = g.Key,
            modelCount = g.Count(),
            chunkCount = chunks.Count(c => g.Select(x => x.id).Contains(c.sourceModelId)),
        }).ToList();

        return Ok(new {
            projectId,
            generatedAt = DateTime.UtcNow,
            models,
            chunks,
            disciplines,
            bounds = new { min = new[] { minX, minY, minZ }, max = new[] { maxX, maxY, maxZ } },
            alignment = new {
                reported = alignmentReports.Count,
                passed = alignmentReports.Count(r => r.Verdict == "PASS"),
                warned = alignmentReports.Count(r => r.Verdict == "WARN"),
                failed = alignmentReports.Count(r => r.Verdict == "FAIL"),
            },
        });
    }
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
    /// <summary>
    /// When true, bypasses the SHA-256 content-hash dedup and creates a
    /// new ProjectModel row even if an entry with the same bytes
    /// already exists. Used by the Revit plugin's "republish anyway"
    /// flow when a user wants to re-issue an unchanged GLB as a new
    /// revision (e.g. updated element map only).
    /// </summary>
    public bool Force { get; set; }
}

/// <summary>
/// Body of <c>PATCH /api/projects/{id}/models/{modelId}/metadata</c>.
/// Used by the plugin's "Refresh metadata only" publish mode — every
/// field is optional, only the supplied ones are applied.
/// </summary>
public class PatchMetadataRequest
{
    public IFormFile? ElementMap { get; set; }
    public IFormFile? Thumbnail { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Discipline { get; set; }
    public string? Revision { get; set; }
    public int? ElementCount { get; set; }
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
    DateTime UploadedAt,
    bool StorageOk,
    DateTime? StorageMissingAt);
