using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StingBIM.Core.DTOs;
using StingBIM.Core.Entities;
using StingBIM.Infrastructure.Data;
using StingBIM.Infrastructure.SignalR;

namespace StingBIM.API.Controllers;

/// <summary>
/// Tag synchronization endpoint — receives tagged element data from the Revit plugin
/// and broadcasts updates to connected web dashboard clients via SignalR.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TagSyncController : ControllerBase
{
    private readonly StingBimDbContext _db;
    private readonly IHubContext<TagSyncHub> _tagHub;
    private readonly IHubContext<ComplianceHub> _complianceHub;

    public TagSyncController(StingBimDbContext db, IHubContext<TagSyncHub> tagHub, IHubContext<ComplianceHub> complianceHub)
    {
        _db = db;
        _tagHub = tagHub;
        _complianceHub = complianceHub;
    }

    /// <summary>
    /// Bulk sync tagged elements from Revit plugin to server.
    /// </summary>
    [HttpPost("sync")]
    public async Task<ActionResult<TagSyncResponse>> SyncElements([FromBody] TagSyncRequest request)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == request.ProjectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");

        int created = 0, updated = 0;

        // Load all existing elements for this project in one query (avoids N+1)
        var incomingIds = request.Elements.Select(e => e.RevitElementId).ToHashSet();
        var existingElements = await _db.TaggedElements
            .Where(e => e.ProjectId == project.Id && incomingIds.Contains(e.RevitElementId))
            .ToDictionaryAsync(e => e.RevitElementId);

        foreach (var dto in request.Elements)
        {
            if (existingElements.TryGetValue(dto.RevitElementId, out var existing))
            {
                MapDtoToEntity(dto, existing, request.UserName);
                updated++;
            }
            else
            {
                var entity = new TaggedElement { ProjectId = project.Id };
                MapDtoToEntity(dto, entity, request.UserName);
                _db.TaggedElements.Add(entity);
                created++;
            }
        }

        // Audit trail for tag sync operation
        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid) ? uid : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            ProjectId = project.Id,
            UserId = userId,
            Action = "tag_sync",
            EntityType = "TaggedElement",
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                elementsReceived = request.Elements.Count,
                created, updated,
                userName = request.UserName
            }),
            Timestamp = DateTime.UtcNow
        });

        // Update project compliance metrics
        await _db.SaveChangesAsync();
        var metrics = await ComputeComplianceAsync(project.Id);
        project.TotalElements = metrics.TotalElements;
        project.TaggedElements = metrics.Tagged;
        project.CompliancePercent = metrics.CompliancePercent;
        project.RagStatus = metrics.RagStatus;
        project.LastSyncAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Broadcast to web dashboard via SignalR
        await _tagHub.Clients.Group(project.Id.ToString())
            .SendAsync("TagsUpdated", new { created, updated, total = request.Elements.Count });
        await _complianceHub.Clients.Group(project.Id.ToString())
            .SendAsync("ComplianceUpdated", metrics);

        return Ok(new TagSyncResponse
        {
            Received = request.Elements.Count,
            Created = created,
            Updated = updated,
            CompliancePercent = metrics.CompliancePercent,
            RagStatus = metrics.RagStatus
        });
    }

    /// <summary>
    /// Get compliance summary for a project.
    /// </summary>
    [HttpGet("compliance/{projectId}")]
    public async Task<ActionResult<ComplianceSummaryDto>> GetCompliance(Guid projectId)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound();

        return Ok(await ComputeComplianceAsync(projectId));
    }

    /// <summary>
    /// Get all tagged elements for a project (paginated).
    /// </summary>
    [HttpGet("elements/{projectId}")]
    public async Task<ActionResult> GetElements(Guid projectId, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        var tenantId = GetTenantId();
        var elements = await _db.TaggedElements
            .Where(e => e.ProjectId == projectId && e.Project!.TenantId == tenantId)
            .OrderBy(e => e.Tag1)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var total = await _db.TaggedElements.CountAsync(e => e.ProjectId == projectId);

        return Ok(new { elements, total, page, pageSize });
    }

    private async Task<ComplianceSummaryDto> ComputeComplianceAsync(Guid projectId)
    {
        // Server-side aggregation — no .ToListAsync() to avoid loading all elements
        var q = _db.TaggedElements.Where(e => e.ProjectId == projectId);
        int total = await q.CountAsync();
        int tagged = await q.CountAsync(e => e.Tag1 != null && e.Tag1 != "");
        int resolved = await q.CountAsync(e => e.IsFullyResolved);
        int stale = await q.CountAsync(e => e.IsStale);
        double pct = total > 0 ? (double)tagged / total * 100 : 0;
        string rag = pct >= 80 ? "GREEN" : pct >= 50 ? "AMBER" : "RED";

        var byDisc = await q.Where(e => e.Disc != null && e.Disc != "")
            .GroupBy(e => e.Disc)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count);

        return new ComplianceSummaryDto
        {
            TotalElements = total, Tagged = tagged, Untagged = total - tagged,
            FullyResolved = resolved, Stale = stale,
            CompliancePercent = Math.Round(pct, 1), RagStatus = rag,
            ByDiscipline = byDisc
        };
    }

    private static void MapDtoToEntity(TagElementDto dto, TaggedElement entity, string userName)
    {
        entity.RevitElementId = dto.RevitElementId;
        entity.UniqueId = dto.UniqueId;
        entity.Disc = dto.Disc; entity.Loc = dto.Loc; entity.Zone = dto.Zone;
        entity.Lvl = dto.Lvl; entity.Sys = dto.Sys; entity.Func = dto.Func;
        entity.Prod = dto.Prod; entity.Seq = dto.Seq;
        entity.Tag1 = dto.Tag1; entity.Tag7 = dto.Tag7;
        entity.CategoryName = dto.CategoryName; entity.FamilyName = dto.FamilyName;
        entity.Status = dto.Status; entity.Rev = dto.Rev;
        entity.IsComplete = dto.IsComplete; entity.IsFullyResolved = dto.IsFullyResolved;
        entity.SyncedAt = DateTime.UtcNow; entity.SyncedBy = userName;
    }

    private Guid GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return claim != null ? Guid.Parse(claim) : Guid.Empty;
    }
}
