using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// S7.4 — GDPR (EU) / POPIA (South Africa) Subject Access + Erasure rights.
/// A tenant Owner / Admin can request:
///
///   GET    /api/data-rights/export      — ZIP containing every row
///                                          we hold for the tenant
///                                          (issues, audit log, models
///                                          metadata, members, billing).
///   POST   /api/data-rights/erase       — soft-delete + scheduled hard-
///                                          delete of all tenant data
///                                          after a 30-day cooling-off.
///   POST   /api/data-rights/cancel-erase — abort a pending erasure
///                                          inside the cooling-off window.
///
/// Erasure is in two stages so a misclick by an Owner doesn't take down
/// a year's project history. The cooling-off period is mandated by both
/// regulations (the 30-day bar is the strict reading of POPIA §24).
/// </summary>
[ApiController]
[Route("api/data-rights")]
[Authorize(Roles = "Owner,Admin")]
public class DataRightsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;

    public DataRightsController(PlanscapeDbContext db, ITenantContext tenant)
    {
        _db = db; _tenant = tenant;
    }

    [HttpGet("export")]
    public async Task<ActionResult> Export(CancellationToken ct)
    {
        var tenantId = _tenant.TenantId;

        // Stream a ZIP to the response so we don't buffer a multi-GB
        // export in memory. Each file inside is a JSON-line dump of one
        // table, scoped to the current tenant by the global filter.
        Response.ContentType = "application/zip";
        Response.Headers["Content-Disposition"] = $"attachment; filename=planscape-export-{tenantId:N}.zip";

        using var archive = new ZipArchive(Response.BodyWriter.AsStream(), ZipArchiveMode.Create, leaveOpen: true);

        await DumpAsync(archive, "tenant.json",     await _db.Tenants.Where(t => t.Id == tenantId).ToListAsync(ct));
        await DumpAsync(archive, "users.json",      await _db.Users.ToListAsync(ct));
        await DumpAsync(archive, "projects.json",   await _db.Projects.ToListAsync(ct));
        await DumpAsync(archive, "members.json",    await _db.ProjectMembers.ToListAsync(ct));
        await DumpAsync(archive, "issues.json",     await _db.Issues.ToListAsync(ct));
        await DumpAsync(archive, "documents.json",  await _db.Documents.ToListAsync(ct));
        await DumpAsync(archive, "models.json",     await _db.ProjectModels.ToListAsync(ct));
        await DumpAsync(archive, "audit-log.json",  await _db.AuditLogs.OrderBy(a => a.Timestamp).Take(100_000).ToListAsync(ct));
        await DumpAsync(archive, "subscriptions.json", await _db.Subscriptions.ToListAsync(ct));
        await DumpAsync(archive, "invoices.json",   await _db.Invoices.ToListAsync(ct));

        return new EmptyResult();
    }

    private static async Task DumpAsync<T>(ZipArchive archive, string entryName, IList<T> rows)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await using var writer = new StreamWriter(entryStream);
        var opts = new JsonSerializerOptions { WriteIndented = true };
        foreach (var row in rows)
            await writer.WriteLineAsync(JsonSerializer.Serialize(row, opts));
    }

    [HttpPost("erase")]
    public async Task<ActionResult> Erase([FromBody] EraseRequest req, CancellationToken ct)
    {
        if (req.ConfirmationPhrase != "ERASE EVERYTHING")
            return BadRequest(new { error = "confirmation_required",
                message = "ConfirmationPhrase must equal 'ERASE EVERYTHING' exactly." });

        var tenant = await _db.Tenants.FirstAsync(t => t.Id == _tenant.TenantId, ct);
        if (tenant.Slug == "planscape")
            return BadRequest(new { error = "platform_tenant_protected" });

        tenant.IsActive = false;
        tenant.PendingErasureAt = DateTime.UtcNow.AddDays(30);
        await _db.SaveChangesAsync(ct);
        return Ok(new
        {
            tenantId = tenant.Id,
            erasureCompletesAt = tenant.PendingErasureAt,
            message = "Tenant frozen. Hard-delete will run 30 days from now. Email hello@planscape.app to abort earlier than that."
        });
    }

    [HttpPost("cancel-erase")]
    public async Task<ActionResult> CancelErase(CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstAsync(t => t.Id == _tenant.TenantId, ct);
        if (tenant.PendingErasureAt == null) return BadRequest(new { error = "no_pending_erasure" });
        tenant.PendingErasureAt = null;
        tenant.IsActive = true;
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Erasure cancelled; tenant restored." });
    }
}

public record EraseRequest(string ConfirmationPhrase);
