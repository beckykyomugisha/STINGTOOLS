using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StingBIM.Core.Entities;
using StingBIM.Infrastructure.Data;

namespace StingBIM.API.Controllers;

/// <summary>
/// ISO 19650 document transmittal management.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/transmittals")]
[Authorize]
public class TransmittalsController : ControllerBase
{
    private readonly StingBimDbContext _db;

    public TransmittalsController(StingBimDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult> GetTransmittals(Guid projectId)
    {
        var tenantId = GetTenantId();
        var transmittals = await _db.Set<Transmittal>()
            .Where(t => t.ProjectId == projectId && t.Project!.TenantId == tenantId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        return Ok(transmittals);
    }

    [HttpPost]
    public async Task<ActionResult> CreateTransmittal(Guid projectId, [FromBody] CreateTransmittalRequest req)
    {
        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");

        // Auto-generate TX code
        var lastTx = await _db.Set<Transmittal>()
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

        _db.Set<Transmittal>().Add(transmittal);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetTransmittals), new { projectId }, transmittal);
    }

    [HttpPut("{txId}/send")]
    public async Task<ActionResult> MarkSent(Guid projectId, Guid txId)
    {
        var tenantId = GetTenantId();
        var tx = await _db.Set<Transmittal>()
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
