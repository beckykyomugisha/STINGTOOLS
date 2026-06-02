using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;
using Planscape.API.Authorization;
using System.Text.Json;

namespace Planscape.API.Controllers;

/// <summary>
/// Federated model geometry delta endpoint.
///
/// POST /api/projects/{projectId}/federated-model/delta
///   Accepts a multipart/form-data body with:
///     • "glb"        — binary/glb — GLB file containing changed/added element meshes
///     • "deletedIds" — application/json — JSON array of deleted Revit element IDs (int)
///
/// Each mesh in the GLB must carry node.extras.uniqueId and node.extras.elementId
/// (written by GlbSerializer in the Revit plugin). The endpoint upserts a
/// FederatedElement row per node and notifies all connected viewers via SignalR.
///
/// GET /api/projects/{projectId}/federated-model/elements
///   Returns the list of non-deleted FederatedElement rows for the project
///   (used by the viewer to build its scene index on first load).
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/federated-model")]
[Authorize]
[ProjectAccess]
public class FederatedModelController : ControllerBase
{
    private readonly PlanscapeDbContext                    _db;
    private readonly IHubContext<FederatedModelHub>        _hub;
    private readonly IHubContext<NotificationHub>          _notificationHub;
    private readonly Planscape.Core.Interfaces.IFileStorageService _storage;

    public FederatedModelController(
        PlanscapeDbContext db,
        IHubContext<FederatedModelHub> hub,
        IHubContext<NotificationHub> notificationHub,
        Planscape.Core.Interfaces.IFileStorageService storage)
    {
        _db      = db;
        _hub     = hub;
        _notificationHub = notificationHub;
        _storage = storage;
    }

    // ── POST delta ───────────────────────────────────────────────────────────

    [HttpPost("delta")]
    [RequestSizeLimit(256 * 1024 * 1024)] // 256 MB cap per delta
    public async Task<ActionResult> PostDelta(
        Guid projectId,
        IFormFile? glb,
        IFormFile? deletedIds)
    {
        var tenantId = GetTenantId();
        var project  = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");

        // ── Parse deleted IDs ────────────────────────────────────────────────
        var deletedList = new List<long>();
        if (deletedIds != null)
        {
            try
            {
                using var stream = deletedIds.OpenReadStream();
                var ids = await JsonSerializer.DeserializeAsync<List<int>>(stream);
                if (ids != null) deletedList.AddRange(ids.Select(i => (long)i));
            }
            catch { /* ignore malformed JSON — caller already logged */ }
        }

        // ── Mark deleted rows ────────────────────────────────────────────────
        if (deletedList.Count > 0)
        {
            await _db.FederatedElements
                .Where(e => e.ProjectId == projectId && deletedList.Contains(e.ElementId) && !e.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.IsDeleted, true)
                    .SetProperty(e => e.UpdatedAt, DateTime.UtcNow));
        }

        // ── Parse and upsert GLB meshes ──────────────────────────────────────
        var updatedUniqueIds = new List<string>();
        if (glb != null && glb.Length > 0)
        {
            // Read GLB once into memory for node-extras parsing
            byte[] glbBytes;
            using (var ms = new MemoryStream((int)glb.Length))
            {
                await glb.CopyToAsync(ms);
                glbBytes = ms.ToArray();
            }

            // Store the full delta GLB blob for the viewer to stream
            string fileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}-delta.glb";
            string storagePath;
            using (var ms = new MemoryStream(glbBytes))
                storagePath = await _storage.SaveScopedAsync(tenantId, projectId, fileName, ms);

            // Parse per-element metadata from the GLB node extras
            var nodes = ParseGlbNodeExtras(glbBytes);
            var docGuid = User.FindFirst("source_doc_guid")?.Value ?? "revit-plugin";

            foreach (var node in nodes)
            {
                if (string.IsNullOrEmpty(node.UniqueId)) continue;

                var existing = await _db.FederatedElements.FirstOrDefaultAsync(e =>
                    e.ProjectId == projectId &&
                    e.SourceDocGuid == docGuid &&
                    e.ElementId == node.ElementId);

                if (existing == null)
                {
                    existing = new FederatedElement
                    {
                        ProjectId   = projectId,
                        TenantId    = tenantId,
                        SourceDocGuid = docGuid,
                        Source      = "revit-plugin"
                    };
                    _db.FederatedElements.Add(existing);
                }

                existing.ElementId     = node.ElementId;
                existing.UniqueId      = node.UniqueId;
                existing.IfcGuid       = node.IfcGuid;
                existing.Category      = node.Category;
                existing.GlbStoragePath = storagePath;
                existing.IsDeleted     = false;
                existing.UpdatedAt     = DateTime.UtcNow;

                updatedUniqueIds.Add(node.UniqueId);
            }

            await _db.SaveChangesAsync();
        }

        // ── Notify connected viewers ─────────────────────────────────────────
        await FederatedModelHub.NotifyUpdate(
            _hub,
            projectId.ToString(),
            updatedUniqueIds,
            deletedList,
            notificationHub: _notificationHub);

        return Ok(new
        {
            updated = updatedUniqueIds.Count,
            deleted = deletedList.Count
        });
    }

    // ── GET elements (scene index) ───────────────────────────────────────────

    [HttpGet("elements")]
    public async Task<ActionResult> GetElements(Guid projectId)
    {
        var tenantId = GetTenantId();
        var project  = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");

        var elements = await _db.FederatedElements
            .AsNoTracking()
            .Where(e => e.ProjectId == projectId && !e.IsDeleted)
            .Select(e => new
            {
                e.UniqueId, e.IfcGuid, e.ElementId, e.Category,
                e.MinX, e.MinY, e.MinZ, e.MaxX, e.MaxY, e.MaxZ,
                e.GlbStoragePath, e.UpdatedAt
            })
            .ToListAsync();

        return Ok(elements);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Guid GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    // Minimal GLB node-extras parser — reads the JSON chunk of a GLB 2.0 file
    // and extracts node.extras objects that carry the per-element metadata
    // written by GlbSerializer (uniqueId, ifcGuid, elementId, category).
    private static List<GlbNodeExtras> ParseGlbNodeExtras(byte[] glbBytes)
    {
        var result = new List<GlbNodeExtras>();
        try
        {
            // GLB header: 12 bytes; first chunk immediately follows
            if (glbBytes.Length < 20) return result;
            // JSON chunk: 4-byte length at offset 12, 4-byte type at offset 16
            int jsonLength = BitConverter.ToInt32(glbBytes, 12);
            if (jsonLength <= 0 || 20 + jsonLength > glbBytes.Length) return result;
            string json = System.Text.Encoding.UTF8.GetString(glbBytes, 20, jsonLength).TrimEnd('\0', ' ');

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("nodes", out var nodes)) return result;

            foreach (var node in nodes.EnumerateArray())
            {
                if (!node.TryGetProperty("extras", out var extras)) continue;
                result.Add(new GlbNodeExtras
                {
                    UniqueId  = extras.TryGetProperty("uniqueId",  out var uid)  ? uid.GetString()  ?? "" : "",
                    IfcGuid   = extras.TryGetProperty("ifcGuid",   out var ifc)  ? ifc.GetString()       : null,
                    ElementId = extras.TryGetProperty("elementId", out var eid)  ? eid.GetInt64()        : 0,
                    Category  = extras.TryGetProperty("category",  out var cat)  ? cat.GetString()       : null,
                });
            }
        }
        catch { /* malformed GLB — skip extras parsing */ }
        return result;
    }

    private sealed class GlbNodeExtras
    {
        public string  UniqueId  { get; set; } = "";
        public string? IfcGuid   { get; set; }
        public long    ElementId { get; set; }
        public string? Category  { get; set; }
    }
}
