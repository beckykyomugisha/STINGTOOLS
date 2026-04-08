using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StingBIM.Core;
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

    /// <summary>
    /// Full sync endpoint (plugin v2.2+): elements + compliance snapshot + warning summary
    /// + SEQ counters in a single atomic request.
    /// If ProjectId == Guid.Empty the project is auto-created from ProjectName/ProjectCode.
    /// </summary>
    [HttpPost("fullsync")]
    public async Task<ActionResult<FullSyncResponse>> FullSync([FromBody] FullSyncRequest request)
    {
        var tenantId = GetTenantId();

        // ── Resolve or auto-create project ──────────────────────────────────
        Project? project = null;
        bool projectCreated = false;

        if (request.ProjectId != Guid.Empty)
        {
            project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == request.ProjectId && p.TenantId == tenantId);
            if (project == null) return NotFound(new { message = $"Project {request.ProjectId} not found" });
        }
        else if (!string.IsNullOrWhiteSpace(request.ProjectName))
        {
            // Find by name first (case-insensitive)
            project = await _db.Projects.FirstOrDefaultAsync(
                p => p.TenantId == tenantId && p.Name.ToLower() == request.ProjectName.ToLower());

            if (project == null)
            {
                // Auto-create — verify tier allows it
                var tenant = await _db.Tenants.FindAsync(tenantId);
                if (tenant != null)
                {
                    int pCount = await _db.Projects.CountAsync(p => p.TenantId == tenantId);
                    int pLimit = TierLimits.MaxProjects(tenant.Tier);
                    if (!TierLimits.BelowLimit(pCount, pLimit, tenant.MaxProjects))
                        return BadRequest(new { message = $"Project limit reached ({TierLimits.LimitLabel(pLimit, tenant.MaxProjects)}). Upgrade for unlimited projects." });
                }

                // Ensure unique code
                string code = string.IsNullOrWhiteSpace(request.ProjectCode)
                    ? request.ProjectName.Replace(" ", "").ToUpper()[..Math.Min(8, request.ProjectName.Length)]
                    : request.ProjectCode.ToUpper();
                if (await _db.Projects.AnyAsync(p => p.TenantId == tenantId && p.Code == code))
                    code = code + "_" + DateTime.UtcNow.ToString("MMdd");

                project = new Project
                {
                    TenantId    = tenantId,
                    Name        = request.ProjectName,
                    Code        = code,
                    Description = $"Auto-created by StingTools plugin sync ({request.UserName})",
                    Phase       = "Design"
                };
                _db.Projects.Add(project);
                await _db.SaveChangesAsync();
                projectCreated = true;
            }
        }
        else
        {
            return BadRequest(new { message = "Provide ProjectId or ProjectName" });
        }

        // ── Sync elements (same logic as /sync) ─────────────────────────────
        int created = 0, updated = 0;
        if (request.Elements.Count > 0)
        {
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
            await _db.SaveChangesAsync();
        }

        // ── Save compliance snapshot (if provided) ───────────────────────────
        if (request.Compliance != null)
        {
            var snap = new ComplianceSnapshot
            {
                ProjectId           = project.Id,
                CapturedAt          = DateTime.UtcNow,
                CapturedBy          = request.UserName,
                TotalElements       = request.Compliance.TotalElements,
                TaggedComplete      = request.Compliance.TaggedComplete,
                TaggedIncomplete    = request.Compliance.TaggedIncomplete,
                Untagged            = request.Compliance.Untagged,
                FullyResolved       = request.Compliance.FullyResolved,
                StaleCount          = request.Compliance.StaleCount,
                PlaceholderCount    = request.Compliance.PlaceholderCount,
                TagPercent          = request.Compliance.TagPercent,
                StrictPercent       = request.Compliance.StrictPercent,
                ContainerPercent    = request.Compliance.ContainerPercent,
                RagStatus           = request.Compliance.RagStatus,
                WarningCount        = request.Warnings?.Total ?? 0,
                WarningHealthScore  = request.Warnings?.HealthScore ?? 0,
                ByDisciplineJson    = System.Text.Json.JsonSerializer.Serialize(request.Compliance.ByDiscipline),
                EmptyTokenCountsJson= System.Text.Json.JsonSerializer.Serialize(request.Compliance.EmptyTokenCounts)
            };
            _db.ComplianceSnapshots.Add(snap);
        }

        // ── Save SEQ counters (max-per-key merge) ────────────────────────────
        int seqSaved = 0;
        if (request.SeqCounters.Count > 0)
        {
            var keys = request.SeqCounters.Keys.ToList();
            var existing = await _db.SeqCounters
                .Where(s => s.ProjectId == project.Id && keys.Contains(s.CounterKey))
                .ToDictionaryAsync(s => s.CounterKey);

            foreach (var (key, incomingVal) in request.SeqCounters)
            {
                if (existing.TryGetValue(key, out var counter))
                {
                    if (incomingVal > counter.CurrentValue)
                    {
                        counter.CurrentValue = incomingVal;
                        counter.UpdatedBy = request.UserName;
                        counter.UpdatedAt = DateTime.UtcNow;
                        seqSaved++;
                    }
                }
                else
                {
                    _db.SeqCounters.Add(new SeqCounter
                    {
                        ProjectId    = project.Id,
                        CounterKey   = key,
                        CurrentValue = incomingVal,
                        UpdatedBy    = request.UserName,
                        UpdatedAt    = DateTime.UtcNow
                    });
                    seqSaved++;
                }
            }
        }

        // ── Update project live metrics ──────────────────────────────────────
        var metrics = request.Compliance != null
            ? new ComplianceSummaryDto
              {
                  TotalElements     = request.Compliance.TotalElements,
                  Tagged            = request.Compliance.TaggedComplete,
                  CompliancePercent = request.Compliance.TagPercent,
                  RagStatus         = request.Compliance.RagStatus
              }
            : await ComputeComplianceAsync(project.Id);

        project.TotalElements    = metrics.TotalElements;
        project.TaggedElements   = metrics.Tagged;
        project.CompliancePercent= metrics.CompliancePercent;
        project.RagStatus        = metrics.RagStatus;
        project.LastSyncAt       = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        // ── Broadcast via SignalR ────────────────────────────────────────────
        await _tagHub.Clients.Group(project.Id.ToString())
            .SendAsync("TagsUpdated", new { created, updated, total = request.Elements.Count });
        await _complianceHub.Clients.Group(project.Id.ToString())
            .SendAsync("ComplianceUpdated", metrics);

        return Ok(new FullSyncResponse
        {
            ProjectId         = project.Id,
            ProjectCreated    = projectCreated,
            Received          = request.Elements.Count,
            Created           = created,
            Updated           = updated,
            SeqCountersSaved  = seqSaved,
            CompliancePercent = metrics.CompliancePercent,
            RagStatus         = metrics.RagStatus
        });
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
