// Pack 13 — Autodesk Platform Services webhook receiver.
//
// Closes the cloud-first loop for documents / reviews / issues:
//
//   * dm.version.added         → stamp LastAccSyncAt on matching DocumentRecord
//   * docs.approval.completed  → transition DocumentRecord.CdeStatus → PUBLISHED
//   * model.review.completed   → SignalR broadcast to connected clients
//
// APS requires an endpoint that verifies a shared secret, parses the JSON
// payload, and returns 2xx within 30 s. We fan events out to the existing
// Planscape services via DI rather than do the work inline — that way the
// same code paths handle both plugin-initiated and webhook-initiated events.
//
// URN matching uses the JSON StatusHistoryJson blob the plugin writes when
// uploading via ACCPublish — first-pass; a dedicated indexed column is a
// future cleanup once AccUrn becomes a first-class schema column.

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.SignalR;

namespace Planscape.API.Controllers;

[ApiController]
[Route("api/webhooks/autodesk")]
[AllowAnonymous] // APS authenticates via HMAC signature, not bearer token.
public class AutodeskWebhooksController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly IConfiguration _config;
    private readonly ILogger<AutodeskWebhooksController> _log;

    public AutodeskWebhooksController(
        PlanscapeDbContext db,
        IHubContext<NotificationHub> hub,
        IConfiguration config,
        ILogger<AutodeskWebhooksController> log)
    {
        _db = db;
        _hub = hub;
        _config = config;
        _log = log;
    }

    /// <summary>
    /// Single endpoint for all APS webhook events. APS delivers events with
    /// a "x-adsk-signature" HMAC-SHA256 header keyed by the subscription
    /// secret; reject on mismatch to prevent spoofed events from unlocking
    /// documents.
    /// </summary>
    [HttpPost("event")]
    public async Task<IActionResult> Event()
    {
        string secret = _config["Autodesk:WebhookSecret"] ?? "";
        if (string.IsNullOrEmpty(secret))
        {
            _log.LogWarning("Autodesk webhook called but Autodesk:WebhookSecret is unset. Rejecting.");
            return StatusCode(503, new { error = "webhook secret not configured" });
        }

        Request.EnableBuffering();
        string body;
        using (var reader = new System.IO.StreamReader(Request.Body, leaveOpen: true))
            body = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        string expected = ComputeHmac(body, secret);
        string received = Request.Headers["x-adsk-signature"].FirstOrDefault() ?? "";
        if (!string.Equals(expected, received, StringComparison.Ordinal))
        {
            _log.LogWarning("Autodesk webhook signature mismatch — rejecting event.");
            return Unauthorized(new { error = "signature mismatch" });
        }

        JsonElement root;
        try { root = JsonDocument.Parse(body).RootElement; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Autodesk webhook payload parse failed.");
            return BadRequest(new { error = "invalid json" });
        }

        string ev = root.TryGetProperty("event", out var evEl) ? evEl.GetString() ?? "" : "";
        switch (ev)
        {
            case "dm.version.added":
                await HandleVersionAdded(root);
                break;
            case "docs.approval.completed":
                await HandleApprovalCompleted(root);
                break;
            case "model.review.completed":
                await HandleReviewCompleted(root);
                break;
            default:
                _log.LogInformation("Autodesk webhook: ignoring event '{Event}'", ev);
                break;
        }

        return Ok(new { ok = true });
    }

    private async Task HandleVersionAdded(JsonElement payload)
    {
        string urn = payload.TryGetProperty("resourceUrn", out var urnEl) ? urnEl.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(urn)) return;
        var doc = await FindByUrn(urn);
        if (doc == null)
        {
            _log.LogInformation("dm.version.added: no DocumentRecord matching URN={Urn}; skipping", urn);
            return;
        }
        doc.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _hub.Clients.All.SendAsync("document.version.added",
            new { documentId = doc.Id, urn, at = doc.UpdatedAt });
    }

    private async Task HandleApprovalCompleted(JsonElement payload)
    {
        string urn = payload.TryGetProperty("resourceUrn", out var urnEl) ? urnEl.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(urn)) return;
        var doc = await FindByUrn(urn);
        if (doc == null) return;
        // State transition: WIP/SHARED → PUBLISHED. Idempotent — never steps back.
        if (doc.CdeStatus == "WIP" || doc.CdeStatus == "SHARED")
        {
            doc.CdeStatus = "PUBLISHED";
            doc.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _hub.Clients.All.SendAsync("document.cde.published",
                new { documentId = doc.Id, urn, at = doc.UpdatedAt });
        }
    }

    private async Task HandleReviewCompleted(JsonElement payload)
    {
        string urn = payload.TryGetProperty("resourceUrn", out var urnEl) ? urnEl.GetString() ?? "" : "";
        await _hub.Clients.All.SendAsync("review.completed", new { urn, at = DateTime.UtcNow });
    }

    /// <summary>
    /// First-pass URN lookup — checks StatusHistoryJson for a stored
    /// "accUrn" field. A dedicated indexed AccUrn column is on the Pack-13
    /// followup list but stays out of this ship to keep the migration
    /// surface small.
    /// </summary>
    private async Task<DocumentRecord?> FindByUrn(string urn)
    {
        if (string.IsNullOrEmpty(urn)) return null;
        // Postgres JSONB → substring match is cheap even without an index.
        return await _db.DocumentRecords
            .FirstOrDefaultAsync(d => d.StatusHistoryJson != null && d.StatusHistoryJson.Contains(urn));
    }

    private static string ComputeHmac(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
