// Pack 13 — Autodesk Platform Services webhook receiver.
//
//   * dm.version.added / dm.version.modified → stamp UpdatedAt on the matching DocumentRecord
//   * docs.approval.completed  → DocumentRecord.CdeStatus WIP/SHARED → PUBLISHED
//   * model.review.completed   → SignalR notice to the project's group
//   * issue.created-1.0 / issue.updated-1.0 → "acc.issue.changed" to the project's group
//
// CONNECTION RESOLUTION: hooks registered by POST acc/webhooks/subscribe
// (AccWebhookService) carry ?connectionId=… in their callback URL, and their hook
// ids are recorded on the connection; a pinned delivery from a hook id the
// connection did not record is ignored. The payload.projectId lookup is kept
// only as a fallback for hooks created before that endpoint existed — that field
// is not verified.
//
// APS CONTRACT (verified 2026-09-30)
//   * Signature: header x-adsk-signature = "sha1hash=" + hex(HMAC-SHA1(raw body,
//     hook secret)). Computed over the RAW BYTES — never a re-encoded string.
//   * Payload: { version, resourceUrn, hook:{ hookId, tenant, event, system,
//     scope }, payload:{ … } }. The event name is hook.event.
//   * Delivery is at-least-once; x-adsk-delivery-id identifies a delivery and is
//     used to drop duplicates — claimed atomically in the ApsWebhookDeliveries
//     table (ApsWebhookDeliveryGuard). Respond 2xx within 7 s.
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;
using Planscape.Infrastructure.Services.Aps;
using Planscape.Infrastructure.SignalR;

namespace Planscape.API.Controllers;

[ApiController]
[Route("api/webhooks/autodesk")]
[AllowAnonymous] // APS authenticates via HMAC signature, not bearer token.
public class AutodeskWebhooksController : ControllerBase
{
    public const string SignaturePrefix = "sha1hash=";

    private readonly PlanscapeDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly IConfiguration _config;
    private readonly ApsWebhookDeliveryGuard _deliveries;
    private readonly ILogger<AutodeskWebhooksController> _log;

    public AutodeskWebhooksController(
        PlanscapeDbContext db,
        IHubContext<NotificationHub> hub,
        IConfiguration config,
        ApsWebhookDeliveryGuard deliveries,
        ILogger<AutodeskWebhooksController> log)
    {
        _db = db;
        _hub = hub;
        _config = config;
        _deliveries = deliveries;
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

        // At-least-once delivery: drop a delivery already processed. The claim is
        // an atomic insert (ApsWebhookDeliveryGuard, ACC-SRV-7), so two concurrent
        // deliveries of one id cannot both pass. A claim that cannot be taken
        // (database error) processes anyway — the handlers are idempotent in effect.
        string deliveryId = Request.Headers["x-adsk-delivery-id"].FirstOrDefault() ?? "";
        bool claimed = false;
        if (deliveryId.Length > 0)
        {
            var claim = await _deliveries.TryClaimAsync(deliveryId, ct);
            if (claim == ApsWebhookDeliveryGuard.Claim.Duplicate)
            {
                _log.LogInformation("Autodesk webhook: duplicate delivery {Delivery} ignored.", deliveryId);
                return Ok(new { ok = true, duplicate = true });
            }
            claimed = claim == ApsWebhookDeliveryGuard.Claim.Claimed;
        }

        try
        {
            return await ProcessAsync(root, connectionId, deliveryId, ct);
        }
        catch
        {
            // Processing failed: give the claim back so APS's redelivery is applied,
            // not dropped as a duplicate of an event that never took effect.
            if (claimed) await _deliveries.ReleaseAsync(deliveryId, CancellationToken.None);
            throw;
        }
    }

    private async Task<IActionResult> ProcessAsync(JsonElement root, Guid? connectionId, string deliveryId, CancellationToken ct)
    {
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
            case "dm.version.modified":
                await HandleVersionAdded(conn.Value.TenantId, conn.Value.ProjectId, urn, ct);
                break;
            case "issue.created-1.0":
            case "issue.updated-1.0":
                // Reported, not applied: Planscape issue status is never changed from
                // ACC (see AccSyncService READ-BACK). Clients refresh on this signal.
                await Broadcast(conn.Value.ProjectId, "acc.issue.changed",
                    new { @event = ev, accIssueId = Str(root, "payload", "id"), at = DateTime.UtcNow });
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
                .Select(c => new { c.TenantId, c.ProjectId, c.ConfigJson }).FirstOrDefaultAsync(ct);
            if (pinned == null) return null;

            // When the connection recorded its hooks, the delivering hook must be one
            // of them — a stray or foreign hook pointed at this URL is not trusted.
            string hookId = Str(root, "hook", "hookId");
            var probe = new PlatformConnection { ConfigJson = pinned.ConfigJson };
            if (hookId.Length > 0
                && Planscape.Infrastructure.Services.AccSyncService.TryParseConfig(probe, out var cfg, out _)
                && Planscape.Infrastructure.Services.AccWebhookService.ReadHooks(cfg) is { Count: > 0 } recorded
                && !recorded.Any(h => h.HookId == hookId))
            {
                _log.LogWarning("Autodesk webhook: hook {Hook} is not registered on connection {Connection} — ignored.", hookId, id);
                return null;
            }
            return (pinned.TenantId, pinned.ProjectId);
        }

        string accProject = Str(root, "payload", "projectId");
        if (accProject.Length == 0) accProject = Str(root, "payload", "project");
        if (accProject.Length == 0) return null;
        accProject = ApsEndpoints.StripHubPrefix(accProject);

        // A stored id may carry the Data Management "b." prefix (connections made
        // before SaveSelection normalised it) — match both forms.
        string prefixed = "b." + accProject;
        var matches = await active.Where(c => c.ExternalProjectId == accProject || c.ExternalProjectId == prefixed)
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
