// Pack 13 — Autodesk Platform Services webhook receiver.
//
//   * dm.version.added         → stamp UpdatedAt on the matching DocumentRecord
//   * docs.approval.completed  → DocumentRecord.CdeStatus WIP/SHARED → PUBLISHED
//   * model.review.completed   → SignalR notice to the project's group
//
// APS CONTRACT (verified 2026-09-30)
//   * Signature: header x-adsk-signature = "sha1hash=" + hex(HMAC-SHA1(raw body,
//     hook secret)). Computed over the RAW BYTES — never a re-encoded string.
//   * Payload: { version, resourceUrn, hook:{ hookId, tenant, event, system,
//     scope }, payload:{ … } }. The event name is hook.event.
//   * Delivery is at-least-once; x-adsk-delivery-id identifies a delivery and is
//     used to drop duplicates. Respond 2xx within 7 s.
//
// TENANCY: this endpoint is anonymous, so there is no tenant context and the
// global filter would match nothing (Guid.Empty). Every read uses
// IgnoreQueryFilters() + an explicit TenantId/ProjectId predicate resolved from
// the ACC PlatformConnection the event belongs to — never a cross-tenant search.
// SignalR goes to the project's group ("project-{id}", as NotificationHub joins
// it), never Clients.All.
//
// URN matching still uses the StatusHistoryJson blob the plugin writes on
// ACCPublish (first-pass; an indexed AccUrn column is a Pack-13 follow-up).

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services.Aps;
using Planscape.Infrastructure.SignalR;

namespace Planscape.API.Controllers;

[ApiController]
[Route("api/webhooks/autodesk")]
[AllowAnonymous] // APS authenticates via HMAC signature, not bearer token.
public class AutodeskWebhooksController : ControllerBase
{
    public const string SignaturePrefix = "sha1hash=";
    private const string DeliveryKeyPrefix = "aps-webhook-delivery:";
    private static readonly TimeSpan DeliveryMemory = TimeSpan.FromHours(48);

    private readonly PlanscapeDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly IConfiguration _config;
    private readonly IDistributedCache _cache;
    private readonly ILogger<AutodeskWebhooksController> _log;

    public AutodeskWebhooksController(
        PlanscapeDbContext db,
        IHubContext<NotificationHub> hub,
        IConfiguration config,
        IDistributedCache cache,
        ILogger<AutodeskWebhooksController> log)
    {
        _db = db;
        _hub = hub;
        _config = config;
        _cache = cache;
        _log = log;
    }

    /// <summary>Constant-time check of <c>x-adsk-signature</c> against the raw body.</summary>
    public static bool VerifySignature(byte[] rawBody, string secret, string? header)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(header)) return false;
        header = header.Trim();
        if (!header.StartsWith(SignaturePrefix, StringComparison.OrdinalIgnoreCase)) return false;
        byte[] received;
        try { received = Convert.FromHexString(header.Substring(SignaturePrefix.Length)); }
        catch (FormatException) { return false; }
        byte[] expected = HMACSHA1.HashData(Encoding.UTF8.GetBytes(secret), rawBody);
        return CryptographicOperations.FixedTimeEquals(expected, received);
    }

    /// <summary>
    /// Single endpoint for all APS webhook events. Optional <c>connectionId</c>
    /// query parameter pins the event to one ACC PlatformConnection (register
    /// the hook callback URL with it); otherwise the connection is found by the
    /// ACC project id in the payload.
    /// </summary>
    [HttpPost("event")]
    public async Task<IActionResult> Event([FromQuery] Guid? connectionId, CancellationToken ct)
    {
        string secret = _config["Autodesk:WebhookSecret"] ?? "";
        if (string.IsNullOrEmpty(secret))
        {
            _log.LogWarning("Autodesk webhook called but Autodesk:WebhookSecret is unset. Rejecting.");
            return StatusCode(503, new { error = "webhook secret not configured" });
        }

        byte[] raw;
        using (var ms = new System.IO.MemoryStream())
        {
            await Request.Body.CopyToAsync(ms, ct);
            raw = ms.ToArray();
        }

        if (!VerifySignature(raw, secret, Request.Headers["x-adsk-signature"].FirstOrDefault()))
        {
            _log.LogWarning("Autodesk webhook signature mismatch — rejecting event.");
            return Unauthorized(new { error = "signature mismatch" });
        }

        JsonElement root;
        try { root = JsonDocument.Parse(raw).RootElement; }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Autodesk webhook payload parse failed.");
            return BadRequest(new { error = "invalid json" });
        }

        // At-least-once delivery: drop a delivery already processed. The cache is
        // an optimisation — if it is down, process anyway (handlers are idempotent).
        string deliveryId = Request.Headers["x-adsk-delivery-id"].FirstOrDefault() ?? "";
        if (deliveryId.Length > 0)
        {
            try
            {
                if (await _cache.GetStringAsync(DeliveryKeyPrefix + deliveryId, ct) != null)
                {
                    _log.LogInformation("Autodesk webhook: duplicate delivery {Delivery} ignored.", deliveryId);
                    return Ok(new { ok = true, duplicate = true });
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Autodesk webhook: dedupe cache unavailable; processing delivery {Delivery} without dedupe.", deliveryId);
            }
        }

        string ev = Str(root, "hook", "event");
        string urn = Str(root, "resourceUrn");

        var conn = await ResolveConnectionAsync(connectionId, root, ct);
        if (conn == null)
        {
            // Acknowledge: APS would otherwise redeliver an event we can never place.
            _log.LogWarning("Autodesk webhook {Event} (delivery {Delivery}): no ACC connection matches — ignored.", ev, deliveryId);
            return Ok(new { ok = true, matched = false });
        }

        switch (ev)
        {
            case "dm.version.added":
                await HandleVersionAdded(conn.Value.TenantId, conn.Value.ProjectId, urn, ct);
                break;
            case "docs.approval.completed":
                await HandleApprovalCompleted(conn.Value.TenantId, conn.Value.ProjectId, urn, ct);
                break;
            case "model.review.completed":
                await Broadcast(conn.Value.ProjectId, "review.completed", new { urn, at = DateTime.UtcNow });
                break;
            default:
                _log.LogInformation("Autodesk webhook: ignoring event '{Event}'", ev);
                break;
        }

        if (deliveryId.Length > 0)
        {
            try
            {
                await _cache.SetStringAsync(DeliveryKeyPrefix + deliveryId, "1",
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = DeliveryMemory }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Autodesk webhook: could not record delivery {Delivery} for dedupe.", deliveryId);
            }
        }
        return Ok(new { ok = true });
    }

    /// <summary>
    /// The (tenant, project) the event belongs to: the pinned connection when the
    /// callback URL carries one, else the single active ACC connection whose
    /// ExternalProjectId equals the payload's project id. Ambiguity → null.
    /// </summary>
    private async Task<(Guid TenantId, Guid ProjectId)?> ResolveConnectionAsync(Guid? connectionId, JsonElement root, CancellationToken ct)
    {
        var active = _db.PlatformConnections.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Platform == PlatformType.ACC && c.IsActive);

        if (connectionId is Guid id && id != Guid.Empty)
        {
            var pinned = await active.Where(c => c.Id == id)
                .Select(c => new { c.TenantId, c.ProjectId }).FirstOrDefaultAsync(ct);
            return pinned == null ? null : (pinned.TenantId, pinned.ProjectId);
        }

        string accProject = Str(root, "payload", "projectId");
        if (accProject.Length == 0) accProject = Str(root, "payload", "project");
        if (accProject.Length == 0) return null;
        accProject = ApsEndpoints.StripHubPrefix(accProject);

        var matches = await active.Where(c => c.ExternalProjectId == accProject)
            .Select(c => new { c.TenantId, c.ProjectId }).Take(2).ToListAsync(ct);
        return matches.Count == 1 ? (matches[0].TenantId, matches[0].ProjectId) : null;
    }

    private async Task HandleVersionAdded(Guid tenantId, Guid projectId, string urn, CancellationToken ct)
    {
        var doc = await FindByUrn(tenantId, projectId, urn, ct);
        if (doc == null)
        {
            _log.LogInformation("dm.version.added: no DocumentRecord matching URN={Urn} in project {Project}; skipping", urn, projectId);
            return;
        }
        doc.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await Broadcast(projectId, "document.version.added", new { documentId = doc.Id, urn, at = doc.UpdatedAt });
    }

    private async Task HandleApprovalCompleted(Guid tenantId, Guid projectId, string urn, CancellationToken ct)
    {
        var doc = await FindByUrn(tenantId, projectId, urn, ct);
        if (doc == null) return;
        // State transition: WIP/SHARED → PUBLISHED. Idempotent — never steps back.
        if (doc.CdeStatus == "WIP" || doc.CdeStatus == "SHARED")
        {
            doc.CdeStatus = "PUBLISHED";
            doc.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await Broadcast(projectId, "document.cde.published", new { documentId = doc.Id, urn, at = doc.UpdatedAt });
        }
    }

    private Task Broadcast(Guid projectId, string method, object payload)
        => HubBroadcastExtensions.SafeAsync(
            () => _hub.Clients.Group($"project-{projectId}").SendAsync(method, payload), _log, method);

    /// <summary>URN lookup scoped to one tenant + project (no tenant context on this request).</summary>
    private async Task<DocumentRecord?> FindByUrn(Guid tenantId, Guid projectId, string urn, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(urn)) return null;
        return await _db.Documents
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && d.ProjectId == projectId)
            .FirstOrDefaultAsync(d => d.StatusHistoryJson != null && d.StatusHistoryJson.Contains(urn), ct);
    }

    private static string Str(JsonElement el, params string[] path)
    {
        foreach (var p in path)
        {
            if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(p, out el)) return "";
        }
        return el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";
    }
}
