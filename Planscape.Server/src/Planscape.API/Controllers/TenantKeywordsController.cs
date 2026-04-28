using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 152 — admin surface for editing the tenant-scoped deliverable
/// state-machine keyword extensions. Split out of
/// <see cref="AdminController"/> so the auth gate can drop the
/// class-level <c>Admin / Owner</c> restriction in favour of the
/// finer-grained <c>BimManagerOrAdmin</c> policy: a BIM Manager (ISO
/// 19650 role <c>K</c> on any active project) can curate vocabulary
/// without being promoted to tenant Owner.
///
/// Tenant Admin / Owner short-circuits the policy so existing
/// operators see no behaviour change. The previous endpoints under
/// <c>/api/admin/tenant-keywords</c> have moved here and are
/// preserved at the same route.
/// </summary>
[ApiController]
[Route("api/admin/tenant-keywords")]
[Authorize(Policy = "BimManagerOrAdmin")]
public class TenantKeywordsController : ControllerBase
{
    private readonly PlanscapeDbContext _db;

    public TenantKeywordsController(PlanscapeDbContext db) => _db = db;

    /// <summary>Read the tenant's deliverable-state-machine keyword
    /// extensions. Returns the raw JSON + a flag for "any extensions
    /// configured" so the dashboard can render an empty-state.</summary>
    [HttpGet]
    public async Task<ActionResult> Get()
    {
        var tenantId = GetTenantId();
        var json = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.KeywordExtensionsJson)
            .FirstOrDefaultAsync();
        return Ok(new
        {
            tenantId,
            hasExtensions = !string.IsNullOrWhiteSpace(json),
            json,
        });
    }

    /// <summary>Replace the tenant's keyword extensions JSON. Empty
    /// body / null clears. Validated through the same parser the
    /// runtime uses, so a malformed payload is rejected here rather
    /// than being silently ignored at request time.</summary>
    [HttpPut]
    public async Task<ActionResult> Set([FromBody] TenantKeywordsRequest req)
    {
        var tenantId = GetTenantId();
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant == null) return NotFound();

        if (string.IsNullOrWhiteSpace(req.Json))
        {
            tenant.KeywordExtensionsJson = null;
            await _db.SaveChangesAsync();
            return Ok(new { tenantId, cleared = true });
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(req.Json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                return BadRequest(new { error = "Body must be a JSON object" });
        }
        catch (System.Text.Json.JsonException)
        {
            return BadRequest(new { error = "Body is not valid JSON" });
        }

        var parsed = Planscape.Infrastructure.Workflow.DbTenantKeywordResolver.ParseForValidation(req.Json);
        if (parsed.Count == 0)
            return BadRequest(new
            {
                error = "JSON has no recognised keyword buckets",
                hint = "Body shape: { \"working\": [\"PARKED\"], \"terminal\": [\"FROZEN\"] }",
            });

        tenant.KeywordExtensionsJson = req.Json;
        await _db.SaveChangesAsync();
        return Ok(new
        {
            tenantId,
            buckets = parsed.Count,
            entries = parsed.Sum(kv => kv.Value.Count),
        });
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}
