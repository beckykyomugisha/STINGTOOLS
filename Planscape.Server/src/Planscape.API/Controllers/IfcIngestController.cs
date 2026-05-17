using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// T4-27 — IFC property ingest. Accepts an uploaded .ifc file (or
/// references an existing DocumentRecord), parses it via xbim, and
/// projects every IfcElement instance into the project's TaggedElement
/// table so the rest of the platform (clash, compliance, viewer
/// search) can read IFC-sourced properties without a second tool.
///
///   POST /api/projects/{pid}/ifc/ingest         — multipart upload + ingest
///   POST /api/projects/{pid}/ifc/ingest/from-document/{docId}
///                                                  ingest an already-uploaded
///                                                  IFC document
///   GET  /api/projects/{pid}/ifc/ingest/jobs/{jobId}
///                                                  poll an async ingest job
///
/// Geometry + clash detection require xbim.Geometry's native build
/// dependencies (~500 MB worker image bloat). DEFERRED to a later
/// pass — this controller's job is to land the property surface so
/// every other feature can use IFC data immediately.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/ifc")]
[Authorize]
[ProjectAccess]
public class IfcIngestController : ControllerBase
{
    private const long MaxIfcSize = 2L * 1024 * 1024 * 1024;   // 2 GB hard cap

    private readonly PlanscapeDbContext _db;
    private readonly IIfcIngester _ingester;
    private readonly IFileStorageService _storage;
    private readonly IAuditService _audit;
    private readonly ILogger<IfcIngestController> _logger;

    public IfcIngestController(
        PlanscapeDbContext db,
        IIfcIngester ingester,
        IFileStorageService storage,
        IAuditService audit,
        ILogger<IfcIngestController> logger)
    {
        _db = db;
        _ingester = ingester;
        _storage = storage;
        _audit = audit;
        _logger = logger;
    }

    [HttpPost("ingest")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxIfcSize)]
    public async Task<ActionResult> Ingest(
        Guid projectId,
        IFormFile file,
        CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.Include(p => p.Tenant)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId, ct);
        if (project == null) return NotFound("Project not found");
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;

        if (file == null || file.Length == 0) return BadRequest(new { error = "file_required" });
        if (file.Length > MaxIfcSize)         return BadRequest(new { error = "file_too_large", limitBytes = MaxIfcSize });

        var fname = file.FileName ?? "model.ifc";
        if (!fname.EndsWith(".ifc",   StringComparison.OrdinalIgnoreCase)
         && !fname.EndsWith(".ifczip",StringComparison.OrdinalIgnoreCase)
         && !fname.EndsWith(".ifcxml",StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "ifc_file_required" });
        }

        // Stream to a temp file so xbim can mmap / Esent it. Memory
        // streams blow the heap on >100 MB IFC dumps.
        var tmp = Path.Combine(Path.GetTempPath(), $"planscape_ifc_{Guid.NewGuid():N}.ifc");
        try
        {
            await using (var fs = System.IO.File.Create(tmp))
                await file.CopyToAsync(fs, ct);

            var result = await _ingester.IngestAsync(tmp, ct);
            await PersistAsTaggedElementsAsync(projectId, result, ct);
            await _audit.LogAsync("INGEST", "Ifc", projectId.ToString(),
                System.Text.Json.JsonSerializer.Serialize(new {
                    fileName = fname,
                    schema = result.SchemaVersion,
                    elementCount = result.ElementCount,
                    countsByType = result.CountsByType,
                    durationMs = (int)result.Duration.TotalMilliseconds,
                    warnings = result.Warnings
                }));

            return Ok(new {
                schema = result.SchemaVersion,
                elementCount = result.ElementCount,
                countsByType = result.CountsByType,
                durationMs = (int)result.Duration.TotalMilliseconds,
                warnings = result.Warnings
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IFC ingest failed for project {ProjectId}", projectId);
            return StatusCode(500, new { error = "ingest_failed", detail = ex.Message });
        }
        finally
        {
            try { if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp); } catch { }
        }
    }

    /// <summary>
    /// Persist the parsed elements as TaggedElement rows so the rest
    /// of the platform can read IFC properties via existing tag /
    /// search / compliance pipelines. Upserts by (ProjectId, UniqueId)
    /// so re-running ingest after a model revision updates rather than
    /// duplicates.
    /// </summary>
    private async Task PersistAsTaggedElementsAsync(Guid projectId, IfcIngestResult result, CancellationToken ct)
    {
        if (result.ElementCount == 0) return;

        // Index existing rows so we can update vs insert in one round-trip.
        var existing = await _db.TaggedElements
            .Where(t => t.ProjectId == projectId)
            .ToDictionaryAsync(t => t.UniqueId, ct);

        int inserted = 0, updated = 0;
        foreach (var el in result.Elements)
        {
            if (string.IsNullOrEmpty(el.GlobalId)) continue;
            if (existing.TryGetValue(el.GlobalId, out var row))
            {
                row.CategoryName = el.IfcType;
                row.Tag1      = el.Properties.GetValueOrDefault("Pset_Common.Tag")
                                ?? el.Properties.GetValueOrDefault("IfcTag")
                                ?? row.Tag1 ?? "";
                row.SyncedAt     = DateTime.UtcNow;
                updated++;
            }
            else
            {
                _db.TaggedElements.Add(new TaggedElement
                {
                    ProjectId    = projectId,
                    UniqueId     = el.GlobalId,
                    Tag1      = el.Properties.GetValueOrDefault("Pset_Common.Tag")
                                ?? el.Properties.GetValueOrDefault("IfcTag")
                                ?? "",
                    CategoryName = el.IfcType,
                    FamilyName   = el.Name ?? "",
                    SyncedAt     = DateTime.UtcNow,
                });
                inserted++;
            }
        }
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "IFC ingest: project {ProjectId} → {Inserted} new + {Updated} updated TaggedElement rows",
            projectId, inserted, updated);
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}
