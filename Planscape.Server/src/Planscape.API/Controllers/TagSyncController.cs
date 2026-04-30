using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.DTOs;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.API.Controllers;

/// <summary>
/// Tag synchronization endpoint — receives tagged element data from the Revit plugin
/// and broadcasts updates to connected web dashboard clients via SignalR.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("mobile")]
public class TagSyncController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IHubContext<TagSyncHub> _tagHub;
    private readonly IHubContext<ComplianceHub> _complianceHub;

    private const int SyncBatchSize = 500;

    public TagSyncController(PlanscapeDbContext db, IHubContext<TagSyncHub> tagHub, IHubContext<ComplianceHub> complianceHub)
    {
        _db = db;
        _tagHub = tagHub;
        _complianceHub = complianceHub;
    }

    /// <summary>
    /// Bulk sync tagged elements from Revit plugin to server.
    /// Processes elements in batches to avoid large transaction overhead.
    /// </summary>
    [HttpPost("sync")]
    public async Task<ActionResult<TagSyncResponse>> SyncElements([FromBody] TagSyncRequest request)
    {
        if (request.Elements.Count == 0)
            return Ok(new TagSyncResponse { Received = 0 });

        if (request.Elements.Count > 50_000)
            return BadRequest(new { message = "Maximum 50,000 elements per sync request" });

        if (RequireTenantClaim(out var tenantId) is { } badClaim) return badClaim;
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == request.ProjectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");

        int created = 0, updated = 0;
        var conflicts = new List<SyncConflictDto>();

        // Load all existing elements for this project in one query (avoids N+1)
        var incomingIds = request.Elements.Select(e => e.RevitElementId).ToHashSet();
        var existingElements = await _db.TaggedElements
            .Where(e => e.ProjectId == project.Id && incomingIds.Contains(e.RevitElementId))
            .ToDictionaryAsync(e => e.RevitElementId);

        // Process in batches to limit EF change tracker pressure
        foreach (var batch in request.Elements.Chunk(SyncBatchSize))
        {
            foreach (var dto in batch)
            {
                if (existingElements.TryGetValue(dto.RevitElementId, out var existing))
                {
                    // Conflict detection: last-write-wins via LastModifiedUtc.
                    // - If the client did not supply a timestamp, accept the update (legacy client).
                    // - If the server has no stored timestamp, accept and adopt the client's.
                    // - If client timestamp > server timestamp, accept and bump Version.
                    // - Otherwise treat as a stale update — keep the server copy and record a conflict.
                    var clientTs = dto.LastModifiedUtc;
                    var serverTs = existing.LastModifiedUtc;

                    if (clientTs.HasValue && serverTs.HasValue && clientTs.Value <= serverTs.Value)
                    {
                        var conflict = new SyncConflict
                        {
                            ProjectId = project.Id,
                            TaggedElementId = existing.Id,
                            ElementId = dto.RevitElementId.ToString(),
                            ConflictType = "STALE_UPDATE",
                            Resolution = "SERVER_WINS",
                            ServerTimestamp = serverTs,
                            ClientTimestamp = clientTs,
                            ClientUserName = request.UserName
                        };
                        _db.SyncConflicts.Add(conflict);
                        conflicts.Add(new SyncConflictDto
                        {
                            ElementId = dto.RevitElementId.ToString(),
                            ServerTimestamp = serverTs,
                            ClientTimestamp = clientTs,
                            Resolution = "SERVER_WINS"
                        });
                        // Do NOT overwrite — server wins.
                    }
                    else
                    {
                        MapDtoToEntity(dto, existing, request.UserName);
                        existing.Version += 1;
                        existing.LastModifiedUtc = clientTs ?? DateTime.UtcNow;
                        updated++;
                    }
                }
                else
                {
                    var entity = new TaggedElement { ProjectId = project.Id };
                    MapDtoToEntity(dto, entity, request.UserName);
                    entity.Version = 1;
                    entity.LastModifiedUtc = dto.LastModifiedUtc ?? DateTime.UtcNow;
                    _db.TaggedElements.Add(entity);
                    existingElements[dto.RevitElementId] = entity; // prevent duplicate adds
                    created++;
                }
            }

            await _db.SaveChangesAsync();
        }

        // Compute compliance and update project metrics in a single save
        var metrics = await ComputeComplianceAsync(project.Id);
        project.TotalElements = metrics.TotalElements;
        project.TaggedElements = metrics.Tagged;
        project.CompliancePercent = metrics.CompliancePercent;
        project.ContainerCompliancePercent = metrics.ContainerPercent;
        project.RagStatus = metrics.RagStatus;
        project.LastSyncAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Broadcast to web dashboard via SignalR (fire-and-forget, don't fail sync on hub errors)
        _ = Task.Run(async () =>
        {
            try
            {
                var group = project.Id.ToString();
                await _tagHub.Clients.Group(group)
                    .SendAsync("TagsUpdated", new { created, updated, total = request.Elements.Count });
                await _complianceHub.Clients.Group(group)
                    .SendAsync("ComplianceUpdated", metrics);
            }
            catch { /* SignalR broadcast is best-effort */ }
        });

        return Ok(new TagSyncResponse
        {
            Received = request.Elements.Count,
            Created = created,
            Updated = updated,
            CompliancePercent = metrics.CompliancePercent,
            RagStatus = metrics.RagStatus,
            Conflicts = conflicts
        });
    }

    /// <summary>
    /// Get compliance summary for a project.
    /// </summary>
    [HttpGet("compliance/{projectId}")]
    public async Task<ActionResult<ComplianceSummaryDto>> GetCompliance(Guid projectId)
    {
        var tenantId = GetTenantId();
        var exists = await _db.Projects.AnyAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (!exists) return NotFound();

        return Ok(await ComputeComplianceAsync(projectId));
    }

    /// <summary>
    /// Get tagged elements for a project (paginated). When
    /// <paramref name="lastSyncUtc"/> is supplied, acts as a delta-sync:
    /// only elements changed after that watermark are returned, and a
    /// per-device <see cref="SyncWatermark"/> row is upserted so the
    /// client can pass the updated cursor on its next pull.
    ///
    /// Device identity is read from the optional <c>X-Device-Id</c>
    /// header and defaults to "desktop" when absent.
    /// </summary>
    [HttpGet("elements/{projectId}")]
    public async Task<ActionResult> GetElements(Guid projectId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100,
        [FromQuery] DateTime? lastSyncUtc = null)
    {
        var tenantId = GetTenantId();
        pageSize = Math.Clamp(pageSize, 1, 500);

        // Tenant-scope check up front so unauthorised projectIds 404
        // even when no elements would match the filter.
        var projectExists = await _db.Projects
            .AnyAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (!projectExists) return NotFound();

        var baseQuery = _db.TaggedElements
            .Where(e => e.ProjectId == projectId && e.Project!.TenantId == tenantId);

        // S06 — filter by LastModifiedUtc (the client-supplied modification
        // wall-clock) with a SyncedAt fallback for legacy rows that predate
        // the LastModifiedUtc column. Null-coalesce into a SQL COALESCE so
        // EF can translate it to a single server-side comparison.
        if (lastSyncUtc.HasValue)
        {
            var cutoff = lastSyncUtc.Value;
            baseQuery = baseQuery.Where(e =>
                (e.LastModifiedUtc ?? e.SyncedAt) > cutoff);
        }

        var total = await baseQuery.CountAsync();
        var elements = await baseQuery
            .OrderBy(e => e.Tag1)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // S06 — upsert per-device watermark after a successful delta pull.
        // We only bump the watermark when the caller actually supplied one
        // (otherwise this is just a paginated list, not a sync).
        if (lastSyncUtc.HasValue)
        {
            var deviceId = Request.Headers.TryGetValue("X-Device-Id", out var hdr)
                && !string.IsNullOrWhiteSpace(hdr.ToString())
                    ? hdr.ToString().Trim()
                    : "desktop";

            // New watermark = max(client cutoff, most recent element returned).
            // If the page is empty the cutoff itself is the correct new value.
            var newCutoff = elements.Count > 0
                ? elements.Max(e => e.LastModifiedUtc ?? e.SyncedAt)
                : lastSyncUtc.Value;

            var existing = await _db.SyncWatermarks
                .FirstOrDefaultAsync(w => w.ProjectId == projectId && w.DeviceId == deviceId);
            if (existing == null)
            {
                _db.SyncWatermarks.Add(new SyncWatermark
                {
                    ProjectId = projectId,
                    DeviceId = deviceId,
                    LastSyncUtc = newCutoff
                });
            }
            else
            {
                // Monotonic: never rewind a device's cursor.
                if (newCutoff > existing.LastSyncUtc) existing.LastSyncUtc = newCutoff;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();
        }

        return Ok(new { elements, total, page, pageSize });
    }

    /// <summary>
    /// Search tagged elements by text query across all tag fields.
    /// Uses PostgreSQL ILIKE for case-insensitive search.
    /// </summary>
    [HttpGet("elements/search")]
    public async Task<ActionResult> SearchElements(
        [FromQuery] Guid projectId,
        [FromQuery] string q,
        [FromQuery] int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return BadRequest("Query parameter 'q' must be at least 2 characters");

        var tenantId = GetTenantId();
        var pattern = $"%{q}%";
        limit = Math.Clamp(limit, 1, 200);

        var elements = await _db.TaggedElements
            .Where(e => e.ProjectId == projectId && e.Project!.TenantId == tenantId)
            .Where(e =>
                EF.Functions.ILike(e.Tag1, pattern) ||
                (e.Tag7 != null && EF.Functions.ILike(e.Tag7, pattern)) ||
                EF.Functions.ILike(e.UniqueId, pattern) ||
                EF.Functions.ILike(e.CategoryName, pattern) ||
                EF.Functions.ILike(e.FamilyName, pattern) ||
                EF.Functions.ILike(e.Disc, pattern) ||
                EF.Functions.ILike(e.Loc, pattern) ||
                EF.Functions.ILike(e.Zone, pattern) ||
                EF.Functions.ILike(e.Lvl, pattern) ||
                EF.Functions.ILike(e.Sys, pattern) ||
                EF.Functions.ILike(e.Func, pattern) ||
                EF.Functions.ILike(e.Prod, pattern) ||
                EF.Functions.ILike(e.Seq, pattern) ||
                (e.Status != null && EF.Functions.ILike(e.Status, pattern)) ||
                (e.Rev != null && EF.Functions.ILike(e.Rev, pattern)))
            .OrderBy(e => e.Tag1)
            .Take(limit)
            .ToListAsync();

        return Ok(elements);
    }

    /// <summary>
    /// Single-query compliance aggregation — replaces 4 separate COUNT queries + GroupBy
    /// with one SELECT that computes all metrics server-side.
    /// </summary>
    private async Task<ComplianceSummaryDto> ComputeComplianceAsync(Guid projectId)
    {
        var q = _db.TaggedElements.Where(e => e.ProjectId == projectId);

        // Single aggregation query: total, tagged, resolved, stale, and per-token empty counts
        var stats = await q.GroupBy(e => 1).Select(g => new
        {
            Total = g.Count(),
            Tagged = g.Count(e => e.Tag1 != null && e.Tag1 != ""),
            Resolved = g.Count(e => e.IsFullyResolved),
            Stale = g.Count(e => e.IsStale),
            Complete = g.Count(e => e.IsComplete),
            // Per-token empty counts for dashboard granularity
            EmptyDisc = g.Count(e => e.Disc == null || e.Disc == ""),
            EmptyLoc = g.Count(e => e.Loc == null || e.Loc == ""),
            EmptyZone = g.Count(e => e.Zone == null || e.Zone == ""),
            EmptyLvl = g.Count(e => e.Lvl == null || e.Lvl == ""),
            EmptySys = g.Count(e => e.Sys == null || e.Sys == ""),
            EmptyFunc = g.Count(e => e.Func == null || e.Func == ""),
            EmptyProd = g.Count(e => e.Prod == null || e.Prod == ""),
            EmptySeq = g.Count(e => e.Seq == null || e.Seq == ""),
            EmptyStatus = g.Count(e => e.Status == null || e.Status == ""),
            EmptyRev = g.Count(e => e.Rev == null || e.Rev == ""),
        }).FirstOrDefaultAsync();

        int total = stats?.Total ?? 0;
        int tagged = stats?.Tagged ?? 0;
        int resolved = stats?.Resolved ?? 0;
        int stale = stats?.Stale ?? 0;
        int complete = stats?.Complete ?? 0;
        double pct = total > 0 ? (double)tagged / total * 100 : 0;
        double containerPct = tagged > 0 ? (double)complete / tagged * 100 : 0;
        string rag = pct >= 80 ? "GREEN" : pct >= 50 ? "AMBER" : "RED";

        // Discipline breakdown — separate query since GroupBy on a string column
        // cannot be folded into the aggregation above
        var byDisc = await q.Where(e => e.Disc != null && e.Disc != "")
            .GroupBy(e => e.Disc)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key!, g => g.Count);

        var emptyTokens = new Dictionary<string, int>();
        if (stats != null)
        {
            emptyTokens["DISC"] = stats.EmptyDisc;
            emptyTokens["LOC"] = stats.EmptyLoc;
            emptyTokens["ZONE"] = stats.EmptyZone;
            emptyTokens["LVL"] = stats.EmptyLvl;
            emptyTokens["SYS"] = stats.EmptySys;
            emptyTokens["FUNC"] = stats.EmptyFunc;
            emptyTokens["PROD"] = stats.EmptyProd;
            emptyTokens["SEQ"] = stats.EmptySeq;
            emptyTokens["STATUS"] = stats.EmptyStatus;
            emptyTokens["REV"] = stats.EmptyRev;
        }

        return new ComplianceSummaryDto
        {
            TotalElements = total, Tagged = tagged, Untagged = total - tagged,
            FullyResolved = resolved, Stale = stale,
            CompliancePercent = Math.Round(pct, 1),
            ContainerPercent = Math.Round(containerPct, 1),
            RagStatus = rag,
            ByDiscipline = byDisc,
            EmptyTokenCounts = emptyTokens
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
        return claim != null && Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// P6 — null/empty tenant_id claim should yield a 400, not a silent
    /// "no projects matched" 404. The bare GetTenantId returning
    /// <see cref="Guid.Empty"/> caused TagSync to look exactly like
    /// "wrong project id" — un-debuggable for plugin authors.
    /// </summary>
    private ActionResult? RequireTenantClaim(out Guid tenantId)
    {
        tenantId = GetTenantId();
        if (tenantId == Guid.Empty)
        {
            return BadRequest(new
            {
                error = "missing_tenant_claim",
                message = "JWT is missing or has an unparseable tenant_id claim. Re-authenticate."
            });
        }
        return null;
    }
}
