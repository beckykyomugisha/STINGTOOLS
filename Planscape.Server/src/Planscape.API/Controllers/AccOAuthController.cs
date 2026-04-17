using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// T3 — ACC (Autodesk Construction Cloud) OAuth2 scaffold.
///
/// Three-legged OAuth flow for per-user delegated access to a tenant's ACC
/// account:
///
///   GET  /api/acc/oauth/start?projectId=…     → returns the Autodesk
///          auth URL the user should open in a browser. Includes a state
///          token so the callback can tie back to the correct project.
///   GET  /api/acc/oauth/callback?code=…&amp;state=…  → exchanges the code for
///          access + refresh tokens and stores them in PlatformConnection.
///   POST /api/acc/oauth/disconnect?projectId=… → revokes locally
///          (Autodesk handles server-side revocation on token expiry).
///
/// Real credentials (Client ID / Secret / Callback URL) come from config:
///   Acc:ClientId
///   Acc:ClientSecret
///   Acc:CallbackUrl   = https://planscape.yourco.com/api/acc/oauth/callback
///   Acc:Scopes        = data:read data:write bucket:read bucket:create
///
/// When creds are absent the endpoints return 503 with a helpful message so
/// the mobile / dashboard "Connect ACC" button can show a setup CTA.
/// </summary>
[ApiController]
[Route("api/acc/oauth")]
[Authorize]
public class AccOAuthController : ControllerBase
{
    private const string AuthBase  = "https://developer.api.autodesk.com/authentication/v2/authorize";
    private const string TokenBase = "https://developer.api.autodesk.com/authentication/v2/token";

    private readonly PlanscapeDbContext _db;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;

    public AccOAuthController(PlanscapeDbContext db, IHttpClientFactory http, IConfiguration config)
    {
        _db = db;
        _http = http;
        _config = config;
    }

    [HttpGet("start")]
    public ActionResult Start([FromQuery] Guid projectId)
    {
        var clientId   = _config["Acc:ClientId"];
        var callback   = _config["Acc:CallbackUrl"];
        var scopes     = _config["Acc:Scopes"] ?? "data:read data:write bucket:read bucket:create";
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(callback))
            return StatusCode(503, new { error = "acc_not_configured", message = "Acc:ClientId and Acc:CallbackUrl must be set." });

        var tenantId = Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var t) ? t : Guid.Empty;
        var userId   = Guid.TryParse(User.FindFirst("sub")?.Value,       out var u) ? u : Guid.Empty;
        var state = $"{tenantId:N}.{userId:N}.{projectId:N}.{Guid.NewGuid():N}";

        var url =
            $"{AuthBase}?response_type=code" +
            $"&client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(callback)}" +
            $"&scope={Uri.EscapeDataString(scopes)}" +
            $"&state={Uri.EscapeDataString(state)}";
        return Ok(new { authorizeUrl = url, state });
    }

    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<ActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return BadRequest(new { error = "missing_code_or_state" });

        // state = tenant.user.project.nonce
        var parts = state.Split('.');
        if (parts.Length < 4
            || !Guid.TryParseExact(parts[0], "N", out var tenantId)
            || !Guid.TryParseExact(parts[1], "N", out var userId)
            || !Guid.TryParseExact(parts[2], "N", out var projectId))
            return BadRequest(new { error = "invalid_state" });

        var clientId     = _config["Acc:ClientId"];
        var clientSecret = _config["Acc:ClientSecret"];
        var callback     = _config["Acc:CallbackUrl"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return StatusCode(503, new { error = "acc_not_configured" });

        // Exchange code for tokens.
        using var client = _http.CreateClient("webhook");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"]    = "authorization_code",
            ["code"]          = code,
            ["redirect_uri"]  = callback!,
        });
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        form.Headers.Remove("Authorization");
        using var req = new HttpRequestMessage(HttpMethod.Post, TokenBase) { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            return StatusCode((int)resp.StatusCode, new { error = "token_exchange_failed", body });

        var json = System.Text.Json.JsonDocument.Parse(body);
        var accessToken  = json.RootElement.GetProperty("access_token").GetString();
        var refreshToken = json.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var expiresIn    = json.RootElement.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 3600;

        // Store on a PlatformConnection row (one per project / platform).
        var row = await _db.PlatformConnections
            .FirstOrDefaultAsync(p => p.ProjectId == projectId && p.Platform == PlatformType.ACC, ct);
        if (row == null)
        {
            row = new PlatformConnection
            {
                ProjectId = projectId,
                TenantId  = tenantId,
                Platform  = PlatformType.ACC,
                Name      = "ACC",
            };
            _db.PlatformConnections.Add(row);
        }
        row.AccessToken       = accessToken ?? "";
        row.RefreshToken      = refreshToken;
        row.TokenExpiresAt    = DateTime.UtcNow.AddSeconds(expiresIn - 60);
        row.IsActive          = true;
        row.LastSyncAt        = DateTime.UtcNow;
        row.LastSyncStatus    = "connected";
        await _db.SaveChangesAsync(ct);

        // Send the user back to the dashboard with a simple HTML page.
        return Content("<html><body><p>ACC connected. You can close this tab and return to Planscape.</p></body></html>",
            "text/html");
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect([FromQuery] Guid projectId, CancellationToken ct)
    {
        var row = await _db.PlatformConnections
            .FirstOrDefaultAsync(p => p.ProjectId == projectId && p.Platform == PlatformType.ACC, ct);
        if (row == null) return NotFound();
        row.IsActive = false;
        row.AccessToken = string.Empty;
        row.RefreshToken = null;
        row.TokenExpiresAt = null;
        row.LastSyncStatus = "disconnected";
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
