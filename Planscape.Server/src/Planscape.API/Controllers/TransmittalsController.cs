using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// ISO 19650 document transmittal management.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/transmittals")]
[Authorize]
public class TransmittalsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public TransmittalsController(PlanscapeDbContext db) => _db = db;

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

        // Auto-generate collision-safe TX code — scan max numeric suffix
        var existingCodes = await _db.Transmittals
            .Where(t => t.ProjectId == projectId)
            .Select(t => t.TransmittalCode)
            .ToListAsync();

        int nextNum = 1;
        foreach (var code in existingCodes)
        {
            var parts = code.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[1], out int n) && n >= nextNum)
                nextNum = n + 1;
        }

        var transmittal = new Transmittal
        {
            ProjectId = projectId,
            TransmittalCode = $"TX-{nextNum:D4}",
            Recipient = req.Recipient,
            Notes = req.Notes,
            DocumentIdsJson = req.DocumentIdsJson,
            CreatedBy = User.FindFirst("display_name")?.Value ?? "Unknown"
        };

        _db.Transmittals.Add(transmittal);

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

        var existingCodes = await _db.Transmittals
            .Where(t => t.ProjectId == projectId)
            .Select(t => t.TransmittalCode)
            .ToListAsync();

        int nextNum = 1;
        foreach (var code in existingCodes)
        {
            var parts = code.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[1], out int n) && n >= nextNum)
                nextNum = n + 1;
        }

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

        tx.Status = "SENT";
        tx.SentAt = DateTime.UtcNow;

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
        return Ok(tx);
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record CreateTransmittalRequest(string Recipient, string? Notes, string? DocumentIdsJson);
