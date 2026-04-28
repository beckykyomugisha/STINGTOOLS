using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Infrastructure.Authorization;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 155 — admin surface for editing the tenant-scoped BIM-Manager
/// role override (Phase 154 added the column; Phase 155 adds the API
/// + UI). Same auth gate as the tenant-keywords endpoint:
/// <c>BimManagerOrAdmin</c> policy. Existing tenant Admin / Owner
/// users can edit the list; a BIM Manager (any active K-role on a
/// project) can edit too because the policy short-circuits on Admin
/// / Owner role and otherwise grants on the configured ISO role.
///
/// Routes:
///   GET  /api/admin/tenant-bim-manager-roles — current JSON + flag
///   PUT  /api/admin/tenant-bim-manager-roles — replace / clear
///
/// Body shape on PUT mirrors the column: a JSON array of single-letter
/// ISO 19650 codes plus an outer wrapper because we want to be able to
/// clear via <c>{ "json": null }</c> and HTTP doesn't distinguish
/// "empty string" from "absent" without one.
/// </summary>
[ApiController]
[Route("api/admin/tenant-bim-manager-roles")]
[Authorize(Policy = "BimManagerOrAdmin")]
public class TenantBimManagerRolesController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public TenantBimManagerRolesController(PlanscapeDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult> Get()
    {
        var tenantId = GetTenantId();
        var json = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.BimManagerIso19650RolesJson)
            .FirstOrDefaultAsync();

        // Try to parse so the client can render a friendly preview;
        // falling back to the raw JSON when the override is malformed
        // gives the operator a chance to fix it without round-tripping.
        var parsed = string.IsNullOrWhiteSpace(json)
            ? null
            : DbTenantBimManagerRoleResolver.ParseForValidation(json);

        return Ok(new
        {
            tenantId,
            hasOverride = !string.IsNullOrWhiteSpace(json),
            json,
            parsed,
        });
    }

    [HttpPut]
    public async Task<ActionResult> Set([FromBody] TenantBimManagerRolesRequest req)
    {
        var tenantId = GetTenantId();
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant == null) return NotFound();

        if (string.IsNullOrWhiteSpace(req.Json))
        {
            tenant.BimManagerIso19650RolesJson = null;
            await _db.SaveChangesAsync();
            return Ok(new { tenantId, cleared = true });
        }

        // Validate shape before persisting. We accept an array of
        // strings; anything else returns 400 here so the bad JSON
        // doesn't get silently ignored at request time.
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(req.Json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                return BadRequest(new { error = "Body must be a JSON array of strings" });
        }
        catch (System.Text.Json.JsonException)
        {
            return BadRequest(new { error = "Body is not valid JSON" });
        }

        var parsed = DbTenantBimManagerRoleResolver.ParseForValidation(req.Json);
        if (parsed == null || parsed.Count == 0)
            return BadRequest(new
            {
                error = "JSON parsed to no usable role codes",
                hint = "Body shape: [\"K\", \"C\"] — single-letter ISO 19650 codes, non-empty strings",
            });

        tenant.BimManagerIso19650RolesJson = req.Json;
        await _db.SaveChangesAsync();
        return Ok(new { tenantId, roles = parsed });
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

/// <summary>Phase 155 — body for PUT /admin/tenant-bim-manager-roles.
/// Null/empty <c>Json</c> clears the override.</summary>
public record TenantBimManagerRolesRequest(string? Json);
