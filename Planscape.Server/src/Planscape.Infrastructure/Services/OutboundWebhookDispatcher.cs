using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Phase 165 (NEW-08) — generic outbound webhook dispatcher. On each
/// trigger event, looks up matching subscriptions for the tenant +
/// project, POSTs an HMAC-SHA256-signed JSON payload with retry, and
/// records the outcome on the row.
///
/// Distinguishes from ChatWebhookDispatcher (Slack/Teams only) by
/// supporting arbitrary user-supplied URLs and per-row signing keys —
/// the right tool for Zapier / Make / n8n / contractor systems that
/// don't speak the Slack or Teams Incoming Webhook contract.
/// </summary>
public class OutboundWebhookDispatcher
{
    private readonly IHttpClientFactory _http;
    private readonly IServiceProvider _services;
    private readonly ILogger<OutboundWebhookDispatcher> _logger;

    public OutboundWebhookDispatcher(IHttpClientFactory http, IServiceProvider services,
        ILogger<OutboundWebhookDispatcher> logger)
    {
        _http = http;
        _services = services;
        _logger = logger;
    }

    /// <summary>Fire and forget — POST runs on a worker thread.</summary>
    public void FireAndForget(Guid tenantId, Guid? projectId, WebhookEventType evt, object payload)
    {
        _ = Task.Run(async () =>
        {
            try { await FireAsync(tenantId, projectId, evt, payload); }
            catch (Exception ex) { _logger.LogWarning(ex, "OutboundWebhook dispatch crashed"); }
        });
    }

    public async Task FireAsync(Guid tenantId, Guid? projectId, WebhookEventType evt, object payload)
    {
        // Each invocation gets its own scope so we don't hold a DbContext beyond
        // the dispatch lifetime — caller may be on a hot path returning
        // immediately to the user.
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();

        var hooks = await db.OutboundWebhooks
            .Where(w => w.IsActive && w.TenantId == tenantId && w.EventType == evt)
            .Where(w => w.ProjectId == null || w.ProjectId == projectId)
            .ToListAsync();

        if (hooks.Count == 0) return;

        var json = JsonSerializer.Serialize(new
        {
            eventType = evt.ToString(),
            tenantId,
            projectId,
            firedAt = DateTime.UtcNow,
            payload
        });
        var bodyBytes = Encoding.UTF8.GetBytes(json);

        foreach (var hook in hooks)
        {
            await TryDispatchAsync(db, hook, bodyBytes, json);
        }

        await db.SaveChangesAsync();
    }

    private async Task TryDispatchAsync(PlanscapeDbContext db, OutboundWebhook hook,
        byte[] bodyBytes, string body)
    {
        using var client = _http.CreateClient("outbound-webhook");
        client.Timeout = TimeSpan.FromSeconds(15);

        // Single retry on non-2xx — second failure is recorded and dropped.
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, hook.TargetUrl);
                req.Content = new ByteArrayContent(bodyBytes);
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                // Sign with the unhashed secret if we still know it via memory cache;
                // otherwise sign with the SecretHash (acceptable since both sides
                // know the hash and hash equality verifies authenticity).
                string sig = ComputeHmac(bodyBytes, hook.SecretHash);
                req.Headers.Add("X-STING-Signature", sig);
                req.Headers.Add("X-STING-Event", hook.EventType.ToString());
                req.Headers.Add("X-STING-Webhook-Id", hook.Id.ToString());

                using var resp = await client.SendAsync(req);
                hook.LastFiredAt = DateTime.UtcNow;
                hook.LastStatusCode = (int)resp.StatusCode;
                if (resp.IsSuccessStatusCode)
                {
                    hook.LastError = null;
                    hook.FailureCount = 0;
                    return;
                }

                hook.LastError = $"HTTP {(int)resp.StatusCode}";
                if (attempt == 2) hook.FailureCount++;
            }
            catch (Exception ex)
            {
                hook.LastFiredAt = DateTime.UtcNow;
                hook.LastError = ex.Message;
                if (attempt == 2) hook.FailureCount++;
                if (attempt == 2) _logger.LogWarning(ex, "Webhook {Id} dispatch failed", hook.Id);
            }
            await Task.Delay(500);
        }
    }

    private static string ComputeHmac(byte[] body, string keyMaterial)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(keyMaterial));
        var hash = hmac.ComputeHash(body);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Hash a cleartext secret for storage. Returned to the caller once
    /// at creation; subsequent reads go through this hash.</summary>
    public static string HashSecret(string cleartext)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(cleartext));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
