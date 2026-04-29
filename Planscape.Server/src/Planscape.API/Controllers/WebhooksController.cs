using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 165 (NEW-08) — outbound webhook subscription management.
/// One row per (tenant, [project], event) tuple; each fires an HMAC-SHA256
/// signed JSON POST to a user-supplied URL on the matching event.
/// </summary>
[ApiController]
[Route("api/webhooks")]
[Authorize]
public class WebhooksController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly OutboundWebhookDispatcher _dispatcher;

    public WebhooksController(PlanscapeDbContext db, OutboundWebhookDispatcher dispatcher)
    {
        _db = db;
        _dispatcher = dispatcher;
    }

    /// <summary>List all webhook subscriptions for the current tenant.</summary>
    [HttpGet]
    public async Task<ActionResult<List<WebhookDto>>> List([FromQuery] Guid? projectId)
    {
        var tenantId = GetTenantId();
        var query = _db.OutboundWebhooks.Where(w => w.TenantId == tenantId);
        if (projectId.HasValue) query = query.Where(w => w.ProjectId == projectId.Value || w.ProjectId == null);
        var rows = await query.OrderByDescending(w => w.CreatedAt).ToListAsync();
        return Ok(rows.Select(ToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WebhookDto>> Get(Guid id)
    {
        var hook = await Find(id);
        if (hook == null) return NotFound();
        return Ok(ToDto(hook));
    }

    /// <summary>Create a new webhook subscription. Returns the cleartext secret
    /// once — store it client-side, the server only retains the hash.</summary>
    [HttpPost]
    public async Task<ActionResult<WebhookCreatedDto>> Create([FromBody] CreateWebhookRequest req)
    {
        var tenantId = GetTenantId();
        if (string.IsNullOrWhiteSpace(req.TargetUrl)) return BadRequest("TargetUrl is required.");
        if (!Uri.TryCreate(req.TargetUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http"))
            return BadRequest("TargetUrl must be a valid http(s) URL.");

        var secret = req.Secret ?? Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var hook = new OutboundWebhook
        {
            TenantId = tenantId,
            ProjectId = req.ProjectId,
            EventType = req.EventType,
            TargetUrl = req.TargetUrl,
            SecretHash = OutboundWebhookDispatcher.HashSecret(secret),
            IsActive = true,
        };
        _db.OutboundWebhooks.Add(hook);
        await _db.SaveChangesAsync();

        return Ok(new WebhookCreatedDto
        {
            Webhook = ToDto(hook),
            Secret = secret,
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WebhookDto>> Update(Guid id, [FromBody] UpdateWebhookRequest req)
    {
        var hook = await Find(id);
        if (hook == null) return NotFound();
        if (!string.IsNullOrWhiteSpace(req.TargetUrl)) hook.TargetUrl = req.TargetUrl;
        if (req.IsActive.HasValue) hook.IsActive = req.IsActive.Value;
        if (req.EventType.HasValue) hook.EventType = req.EventType.Value;
        await _db.SaveChangesAsync();
        return Ok(ToDto(hook));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var hook = await Find(id);
        if (hook == null) return NotFound();
        _db.OutboundWebhooks.Remove(hook);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Fire a synthetic test event so the user can verify the receiver.</summary>
    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> TestFire(Guid id)
    {
        var hook = await Find(id);
        if (hook == null) return NotFound();
        await _dispatcher.FireAsync(hook.TenantId, hook.ProjectId, hook.EventType, new
        {
            test = true,
            webhookId = hook.Id,
            sentAt = DateTime.UtcNow,
            note = "STING webhook test event"
        });
        await _db.Entry(hook).ReloadAsync();
        return Ok(ToDto(hook));
    }

    // ── Helpers ──

    private Guid GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private async Task<OutboundWebhook?> Find(Guid id)
    {
        var tenantId = GetTenantId();
        return await _db.OutboundWebhooks.FirstOrDefaultAsync(w => w.Id == id && w.TenantId == tenantId);
    }

    private static WebhookDto ToDto(OutboundWebhook w) => new()
    {
        Id = w.Id,
        ProjectId = w.ProjectId,
        EventType = w.EventType,
        TargetUrl = w.TargetUrl,
        IsActive = w.IsActive,
        CreatedAt = w.CreatedAt,
        LastFiredAt = w.LastFiredAt,
        LastStatusCode = w.LastStatusCode,
        LastError = w.LastError,
        FailureCount = w.FailureCount,
    };
}

public class WebhookDto
{
    public Guid Id { get; set; }
    public Guid? ProjectId { get; set; }
    public WebhookEventType EventType { get; set; }
    public string TargetUrl { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastFiredAt { get; set; }
    public int? LastStatusCode { get; set; }
    public string? LastError { get; set; }
    public int FailureCount { get; set; }
}

public class WebhookCreatedDto
{
    public WebhookDto Webhook { get; set; } = new();
    /// <summary>Cleartext signing secret — returned ONCE on creation. Server retains
    /// only the SHA-256 hash. Save this on the receiver to verify X-STING-Signature.</summary>
    public string Secret { get; set; } = "";
}

public class CreateWebhookRequest
{
    public Guid? ProjectId { get; set; }
    public WebhookEventType EventType { get; set; }
    public string TargetUrl { get; set; } = "";
    /// <summary>Optional caller-supplied secret. When null, a random GUID-pair is generated.</summary>
    public string? Secret { get; set; }
}

public class UpdateWebhookRequest
{
    public string? TargetUrl { get; set; }
    public bool? IsActive { get; set; }
    public WebhookEventType? EventType { get; set; }
}
