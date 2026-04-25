using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// T3 — Outbound webhooks to Slack + Microsoft Teams. Both platforms accept
/// incoming JSON POSTs to a shared "Incoming Webhook" URL configured per
/// channel. We post once per notification, shaping the body per-provider:
///
///   - Slack  : { "text": "...", "blocks": [...] }
///   - Teams  : Adaptive Card payload (schema 1.4)
///
/// Config:
///   Webhooks:Slack:DefaultUrl   = https://hooks.slack.com/services/...
///   Webhooks:Teams:DefaultUrl   = https://outlook.office.com/webhook/...
///   Webhooks:Slack:Tenants:<tenantId> — per-tenant override
///   Webhooks:Teams:Tenants:<tenantId> — per-tenant override
///
/// When no URL is configured for the tenant + provider, the dispatcher
/// silently no-ops. Failures log at warn level but never throw — we don't
/// want a downstream Slack outage to break our own code path.
/// </summary>
public class ChatWebhookDispatcher
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<ChatWebhookDispatcher> _logger;

    public ChatWebhookDispatcher(IHttpClientFactory http, IConfiguration config,
        ILogger<ChatWebhookDispatcher> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Fire-and-forget dispatch. Safe to call on hot paths — the HTTP POST
    /// runs on a worker thread so the caller returns immediately.
    /// </summary>
    public void FireAndForget(Guid tenantId, string title, string body, string? severity = null, string? url = null)
    {
        _ = Task.Run(async () =>
        {
            try { await DispatchAsync(tenantId, title, body, severity, url); }
            catch (Exception ex) { _logger.LogWarning(ex, "Chat webhook dispatch crashed"); }
        });
    }

    public async Task DispatchAsync(Guid tenantId, string title, string body, string? severity = null, string? url = null)
    {
        var slackUrl = Resolve("Slack", tenantId);
        var teamsUrl = Resolve("Teams", tenantId);
        if (string.IsNullOrEmpty(slackUrl) && string.IsNullOrEmpty(teamsUrl))
            return;

        using var client = _http.CreateClient("webhook");
        client.Timeout = TimeSpan.FromSeconds(8);

        if (!string.IsNullOrEmpty(slackUrl))
            await PostAsync(client, slackUrl, BuildSlackPayload(title, body, severity, url));

        if (!string.IsNullOrEmpty(teamsUrl))
            await PostAsync(client, teamsUrl, BuildTeamsPayload(title, body, severity, url));
    }

    private string? Resolve(string provider, Guid tenantId)
    {
        // Per-tenant override wins over the default.
        return _config[$"Webhooks:{provider}:Tenants:{tenantId}"]
            ?? _config[$"Webhooks:{provider}:DefaultUrl"];
    }

    private async Task PostAsync(HttpClient client, string url, string json)
    {
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await client.PostAsync(url, content);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync();
                _logger.LogWarning("Webhook POST {Url} failed with {Status}: {Body}",
                    url, (int)resp.StatusCode, err);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Webhook POST {Url} crashed", url); }
    }

    private static string BuildSlackPayload(string title, string body, string? severity, string? url)
    {
        // Slack Block Kit — header + section + optional button.
        var blocks = new List<object>
        {
            new { type = "header", text = new { type = "plain_text", text = title, emoji = true } },
            new { type = "section", text = new { type = "mrkdwn", text = body } },
        };
        if (!string.IsNullOrEmpty(url))
        {
            blocks.Add(new
            {
                type = "actions",
                elements = new object[]
                {
                    new
                    {
                        type = "button",
                        text = new { type = "plain_text", text = "Open in Planscape" },
                        url,
                        style = severity == "CRITICAL" || severity == "HIGH" ? "danger" : "primary",
                    }
                },
            });
        }
        return JsonSerializer.Serialize(new { text = title, blocks });
    }

    private static string BuildTeamsPayload(string title, string body, string? severity, string? url)
    {
        // Teams Incoming Webhook accepts a legacy MessageCard; we send the
        // shorter Adaptive Card wrapper which most Teams channels now accept.
        var themeColor = severity switch
        {
            "CRITICAL" => "D32F2F",
            "HIGH"     => "F57C00",
            "MEDIUM"   => "FBC02D",
            _          => "1A237E",
        };
        var card = new
        {
            type = "MessageCard",
            context = "https://schema.org/extensions",
            themeColor,
            summary = title,
            title,
            text = body,
            potentialAction = string.IsNullOrEmpty(url) ? null : new object[]
            {
                new
                {
                    type = "OpenUri",
                    name = "Open in Planscape",
                    targets = new object[] { new { os = "default", uri = url } },
                }
            },
        };
        return JsonSerializer.Serialize(card);
    }
}
