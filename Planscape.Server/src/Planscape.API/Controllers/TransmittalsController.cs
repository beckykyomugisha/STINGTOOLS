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

        // Auto-generate TX code
        var lastTx = await _db.Transmittals
            .Where(t => t.ProjectId == projectId)
            .OrderByDescending(t => t.TransmittalCode)
            .FirstOrDefaultAsync();

        int nextNum = 1;
        if (lastTx != null)
        {
            var parts = lastTx.TransmittalCode.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[1], out int n)) nextNum = n + 1;
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
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetTransmittals), new { projectId }, transmittal);
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
        await _db.SaveChangesAsync();
        return Ok(tx);
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

public record CreateTransmittalRequest(string Recipient, string? Notes, string? DocumentIdsJson);
