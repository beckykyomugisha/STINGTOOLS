using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;
using Planscape.Infrastructure.SignalR;
using Planscape.API.Authorization;

namespace Planscape.API.Controllers;

/// <summary>
/// ISO 19650 document transmittal management.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/transmittals")]
[Authorize]
[ProjectAccess]
public class TransmittalsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    // GAP-FIX-SIGNALR — broadcast TransmittalUpdated to project subscribers on
    // create / bulk-create / send. Both the Revit plugin (PlanscapeRealtimeClient)
    // and the mobile app (realtimeClient.ts) already listen for this event;
    // server-side broadcasts had been missed in the original wiring.
    private readonly IHubContext<NotificationHub> _notifHub;
    private readonly ISequenceCounterService _seq;

    // Phase 175 audit P1-15-tx — single counter key per project. Bump
    // the suffix when a different code series is introduced (e.g.
    // "tx:rev2") so the SeqCounter row stays unique-per-format.
    private const string TransmittalCounterKey = "transmittal:tx";

    public TransmittalsController(PlanscapeDbContext db, IHubContext<NotificationHub> notifHub, ISequenceCounterService seq)
    {
        _db = db;
        _notifHub = notifHub;
        _seq = seq;
    }

    /// <summary>
    /// Phase 175 — one-shot scan of existing TX-#### codes so the
    /// counter starts ahead of any historic value. After the SeqCounter
    /// row is materialised, subsequent allocations skip this scan.
    /// </summary>
    private async Task<int> ResolveSeedFloorAsync(Guid projectId, CancellationToken ct)
    {
        // Already-materialised counters short-circuit immediately.
        var existing = await _db.SeqCounters.AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.CounterKey == TransmittalCounterKey)
            .Select(s => (int?)s.CurrentValue)
            .FirstOrDefaultAsync(ct);
        if (existing.HasValue) return 0;

        // Cold start — scan once. The seed only matters until the
        // first allocation writes the row; from then on the counter
        // is authoritative.
        var maxFromHistory = await _db.Transmittals
            .Where(t => t.ProjectId == projectId)
            .Select(t => t.TransmittalCode)
            .ToListAsync(ct);
        int floor = 0;
        foreach (var code in maxFromHistory)
        {
            var parts = code.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[1], out int n) && n > floor)
                floor = n;
        }
        return floor;
    }

    [HttpGet]
    public async Task<ActionResult> GetTransmittals(Guid projectId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var tenantId = GetTenantId();
        var query = _db.Transmittals
            .Where(t => t.ProjectId == projectId && t.Project!.TenantId == tenantId);

        var total = await query.CountAsync();
        var transmittals = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        return Ok(new { transmittals, total, page, pageSize });
    }

    [HttpPost]
    public async Task<ActionResult> CreateTransmittal(Guid projectId, [FromBody] CreateTransmittalRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        // Phase 175 audit P1-15-tx — atomic counter via SeqCounter +
        // Postgres UPSERT RETURNING. Replaces the prior O(N) scan +
        // race-prone in-memory MAX. seedFloor handles cold start
        // against legacy data so the counter never collides with
        // historic codes.
        var seedFloor = await ResolveSeedFloorAsync(projectId, HttpContext.RequestAborted);
        var nextNum = await _seq.AllocateAsync(
            tenantId, projectId, TransmittalCounterKey,
            seedFloor: seedFloor, count: 1,
            updatedBy: User.FindFirst("display_name")?.Value,
            ct: HttpContext.RequestAborted);

        var transmittal = new Transmittal
        {
            ProjectId       = projectId,
            TransmittalCode = $"TX-{nextNum:D4}",
            Recipient       = req.Recipient,
            Notes           = req.Notes,
            DocumentIdsJson = req.DocumentIdsJson, // kept for backward compat
            CreatedBy       = User.FindFirst("display_name")?.Value ?? "Unknown"
        };

        _db.Transmittals.Add(transmittal);

        // Gap 5 — snapshot document versions at transmittal creation so the
        // record remains accurate even after subsequent uploads / revisions.
        if (req.DocumentIds != null && req.DocumentIds.Length > 0)
        {
            await SnapshotTransmittalDocumentsAsync(
                transmittal, projectId, tenantId, req.DocumentIds,
                HttpContext.RequestAborted);
        }

        // Audit trail for transmittal creation
        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid) ? uid : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            ProjectId = projectId,
            UserId = userId,
            Action = "transmittal_created",
            EntityType = "Transmittal",
            EntityId = transmittal.Id.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { transmittal.TransmittalCode, transmittal.Recipient }),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // GAP-FIX-SIGNALR — broadcast TransmittalUpdated. Fire-and-forget so
        // a slow SignalR fanout doesn't block the API response.
        _ = _notifHub.Clients.Group($"project-{projectId}").SendAsync("TransmittalUpdated", new
        {
            transmittal.Id, transmittal.TransmittalCode, transmittal.Recipient,
            transmittal.Status, transmittal.CreatedAt, transmittal.SentAt,
            projectId, kind = "created"
        });

        return CreatedAtAction(nameof(GetTransmittals), new { projectId }, transmittal);
    }

    /// <summary>
    /// Phase 142 — bulk-create endpoint so the offline queue and the
    /// plugin's PlanscapeServerClient can flush a backlog in one round-trip
    /// instead of N. Caps at 200 per call to keep request bodies bounded;
    /// caller must chunk larger batches. The TX code sequence is computed
    /// once and incremented in-memory to avoid scanning N times.
    /// </summary>
    [HttpPost("bulk")]
    public async Task<ActionResult> BulkCreate(Guid projectId, [FromBody] List<CreateTransmittalRequest> reqs)
    {
        if (reqs == null) return BadRequest("Body must be a JSON array");
        if (reqs.Count == 0) return Ok(new { created = 0, items = Array.Empty<object>() });
        if (reqs.Count > 200) return BadRequest("Maximum 200 transmittals per bulk operation");

        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        // Phase 175 audit P1-15-tx — bulk allocation in one round-trip.
        // The counter is bumped by `count` atomically; we then iterate
        // (lastValue - count + 1) … lastValue for per-row codes.
        var seedFloor = await ResolveSeedFloorAsync(projectId, HttpContext.RequestAborted);
        var lastNum = await _seq.AllocateAsync(
            tenantId, projectId, TransmittalCounterKey,
            seedFloor: seedFloor, count: reqs.Count,
            updatedBy: User.FindFirst("display_name")?.Value,
            ct: HttpContext.RequestAborted);
        int nextNum = lastNum - reqs.Count + 1;

        var createdBy = User.FindFirst("display_name")?.Value ?? "Unknown";
        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid) ? uid : (Guid?)null;

        var rows = new List<Transmittal>(reqs.Count);
        foreach (var req in reqs)
        {
            var t = new Transmittal
            {
                ProjectId = projectId,
                TransmittalCode = $"TX-{nextNum:D4}",
                Recipient = req.Recipient,
                Notes = req.Notes,
                DocumentIdsJson = req.DocumentIdsJson,
                CreatedBy = createdBy
            };
            nextNum++;
            rows.Add(t);
            _db.Transmittals.Add(t);

            // Gap 5 — version snapshot per transmittal in the bulk batch.
            if (req.DocumentIds != null && req.DocumentIds.Length > 0)
            {
                await SnapshotTransmittalDocumentsAsync(
                    t, projectId, tenantId, req.DocumentIds,
                    HttpContext.RequestAborted);
            }

            _db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenantId,
                ProjectId = projectId,
                UserId = userId,
                Action = "transmittal_created",
                EntityType = "Transmittal",
                EntityId = t.Id.ToString(),
                DetailsJson = JsonSerializer.Serialize(new { t.TransmittalCode, t.Recipient, bulk = true }),
                Timestamp = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();

        // GAP-FIX-SIGNALR — single bulk-event with the count + first/last code,
        // not one event per row. A 200-row bulk would otherwise spam every
        // subscribed client with 200 individual messages.
        _ = _notifHub.Clients.Group($"project-{projectId}").SendAsync("TransmittalUpdated", new
        {
            projectId,
            kind = "bulk_created",
            count = rows.Count,
            firstCode = rows.FirstOrDefault()?.TransmittalCode,
            lastCode  = rows.LastOrDefault()?.TransmittalCode,
        });

        return Ok(new
        {
            created = rows.Count,
            items = rows.Select(r => new { r.Id, r.TransmittalCode, r.Recipient, r.Status, r.CreatedAt })
        });
    }

    [HttpPut("{txId}/send")]
    public async Task<ActionResult> MarkSent(Guid projectId, Guid txId)
    {
        var tenantId = GetTenantId();
        var tx = await _db.Transmittals
            .FirstOrDefaultAsync(t => t.Id == txId && t.ProjectId == projectId && t.Project!.TenantId == tenantId);
        if (tx == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        tx.Status = "SENT";
        tx.SentAt = DateTime.UtcNow;

        // GAP-14 — re-snapshot FilePathAtTransmittal for any rows that still hold
        // the path from creation time, so the final send captures the current HEAD
        // file of each linked document (could differ if a version was uploaded after
        // the document was first added to the transmittal).
        var tdRows = await _db.TransmittalDocuments
            .Where(td => td.TransmittalId == txId)
            .ToListAsync();
        if (tdRows.Count > 0)
        {
            var docIds = tdRows.Select(td => td.DocumentId).Distinct().ToArray();
            var docPaths = await _db.Documents
                .Where(d => docIds.Contains(d.Id))
                .Select(d => new { d.Id, d.FilePath })
                .ToDictionaryAsync(x => x.Id, x => x.FilePath);

            foreach (var td in tdRows)
            {
                if (docPaths.TryGetValue(td.DocumentId, out var latestPath)
                    && !string.IsNullOrEmpty(latestPath))
                {
                    td.FilePathAtTransmittal = latestPath;
                }
            }
        }

        // Audit trail for transmittal sent
        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid) ? uid : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            ProjectId = projectId,
            UserId = userId,
            Action = "transmittal_sent",
            EntityType = "Transmittal",
            EntityId = txId.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { tx.TransmittalCode, tx.Recipient }),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // GAP-FIX-SIGNALR — broadcast TransmittalUpdated. The "sent" transition
        // is the most interesting moment for clients to react to (mobile inbox
        // moves the row to "Sent", plugin closes any open compose dialog).
        _ = _notifHub.Clients.Group($"project-{projectId}").SendAsync("TransmittalUpdated", new
        {
            tx.Id, tx.TransmittalCode, tx.Recipient, tx.Status, tx.SentAt,
            projectId, kind = "sent"
        });

        return Ok(tx);
    }

    /// <summary>
    /// POST /{txId}/documents — add documents (with version snapshot) to an existing
    /// DRAFT transmittal. Once the transmittal is SENT the document list is frozen.
    /// </summary>
    [HttpPost("{txId}/documents")]
    public async Task<ActionResult> AddDocuments(Guid projectId, Guid txId, [FromBody] Guid[] documentIds)
    {
        if (documentIds == null || documentIds.Length == 0)
            return BadRequest("documentIds must be a non-empty array");
        if (documentIds.Length > 200)
            return BadRequest("Maximum 200 documents per request");

        var tenantId = GetTenantId();
        var tx = await _db.Transmittals
            .FirstOrDefaultAsync(t => t.Id == txId && t.ProjectId == projectId && t.Project!.TenantId == tenantId);
        if (tx == null) return NotFound();
        if (await this.RequireProjectMemberAsync(_db, projectId) is { } denied) return denied;

        if (tx.Status == "SENT")
            return Conflict("Documents cannot be added to a SENT transmittal");

        var added = await SnapshotTransmittalDocumentsAsync(
            tx, projectId, tenantId, documentIds, HttpContext.RequestAborted);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            when (ex.InnerException?.Message.Contains("IX_TransmittalDocuments_TransmittalId_DocumentId",
                      StringComparison.OrdinalIgnoreCase) == true
                  || ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true)
        {
            // GAP-04 — unique constraint on (TransmittalId, DocumentId): return 409 rather than 500.
            return Conflict(new { message = "One or more documents are already linked to this transmittal." });
        }

        return Ok(new { added });
    }

    /// <summary>GET /{txId}/documents — list the version-snapshot rows for a transmittal (GAP-12).</summary>
    [HttpGet("{txId}/documents")]
    public async Task<ActionResult> GetDocuments(Guid projectId, Guid txId, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        var tx = await _db.Transmittals
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == txId && t.ProjectId == projectId && t.Project!.TenantId == tenantId, ct);
        if (tx == null) return NotFound();

        var docs = await _db.TransmittalDocuments
            .AsNoTracking()
            .Where(td => td.TransmittalId == txId)
            .Select(td => new
            {
                td.Id,
                td.DocumentId,
                td.DocumentVersionId,
                td.CdeStateAtTransmittal,
                td.SuitabilityAtTransmittal,
                td.FilePathAtTransmittal,
                td.AddedAt,
                FileName = _db.Documents
                    .Where(d => d.Id == td.DocumentId)
                    .Select(d => d.FileName)
                    .FirstOrDefault()
            })
            .OrderBy(td => td.AddedAt)
            .ToListAsync(ct);

        return Ok(new { transmittalId = txId, count = docs.Count, items = docs });
    }

    /// <summary>
    /// Snapshots each document's current version into TransmittalDocument rows so
    /// the transmittal record remains accurate even after subsequent uploads.
    /// Returns the number of rows created (skips IDs that are already linked or
    /// that don't belong to the project).
    /// </summary>
    private async Task<int> SnapshotTransmittalDocumentsAsync(
        Transmittal transmittal,
        Guid projectId,
        Guid tenantId,
        Guid[] documentIds,
        CancellationToken ct)
    {
        // GAP-10 — only PUBLISHED (S4) documents should be attached to a transmittal.
        // Non-PUBLISHED docs are silently skipped so the caller can still create the
        // transmittal and attach whichever docs are ready, without a hard failure.
        var docs = await _db.Documents
            .Where(d => documentIds.Contains(d.Id) && d.ProjectId == projectId && d.TenantId == tenantId
                     && d.CdeStatus == "PUBLISHED")
            .Include(d => d.Versions.OrderByDescending(v => v.VersionNumber).Take(1))
            .AsNoTracking()
            .ToListAsync(ct);

        // Existing snapshot IDs to avoid duplicates on the AddDocuments re-call path.
        var alreadyLinked = await _db.TransmittalDocuments
            .Where(td => td.TransmittalId == transmittal.Id)
            .Select(td => td.DocumentId)
            .ToHashSetAsync(ct);

        int created = 0;
        foreach (var doc in docs)
        {
            if (alreadyLinked.Contains(doc.Id)) continue;

            var latestVersion = doc.Versions.FirstOrDefault();
            _db.TransmittalDocuments.Add(new TransmittalDocument
            {
                TransmittalId            = transmittal.Id,
                DocumentId               = doc.Id,
                DocumentVersionId        = latestVersion?.Id,
                CdeStateAtTransmittal    = doc.CdeStatus,
                SuitabilityAtTransmittal = doc.SuitabilityCode,
                FilePathAtTransmittal    = latestVersion?.FilePath ?? doc.FilePath,
            });
            created++;
        }
        return created;
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record CreateTransmittalRequest(
    string Recipient,
    string? Notes,
    string? DocumentIdsJson,
    Guid[]? DocumentIds = null);
