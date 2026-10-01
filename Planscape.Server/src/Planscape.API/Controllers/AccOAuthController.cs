using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Planscape.API.Services;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;
using Planscape.Infrastructure.Services.Aps;

namespace Planscape.API.Controllers;

/// <summary>
/// ACC (Autodesk Construction Cloud) 3-legged OAuth for the team-shared grant.
///
///   GET  /api/acc/oauth/start?projectId=…       → Autodesk authorize URL with a
///          sealed, single-use, time-limited state (<see cref="AccOAuthState"/>).
///   GET  /api/acc/oauth/callback?code=…&amp;state=… → exchanges the code and stores
///          the tokens on the project's PlatformConnection (encrypted at rest).
///   POST /api/acc/oauth/disconnect?projectId=… → clears the tokens locally.
///
/// WHO MAY CONNECT: one grant is shared by the whole project team, so binding it
/// (or removing it) is a project-administration act — Start and Disconnect both
/// require project visibility (404 otherwise, no existence leak) AND
/// <see cref="ProjectRoles.CanAdministerProject"/> (403).
///
/// The callback is anonymous (Autodesk redirects the browser to it). It trusts
/// nothing but the sealed state, and reads/writes the connection row with
/// IgnoreQueryFilters + an explicit TenantId/ProjectId/Platform predicate
/// because there is no tenant context on that request.
///
/// Config: Acc:ClientId, Acc:ClientSecret, Acc:CallbackUrl, Acc:Scopes;
/// APS host from Aps:BaseUrl (<see cref="ApsEndpoints"/>).
/// </summary>
[ApiController]
[Route("api/acc/oauth")]
[Authorize]
public class AccOAuthController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly AccOAuthState _state;
    private readonly ILogger<AccOAuthController> _log;

    public AccOAuthController(PlanscapeDbContext db, IHttpClientFactory http, IConfiguration config,
        AccOAuthState state, ILogger<AccOAuthController> log)
    {
        _db = db;
        _http = http;
        _config = config;
        _state = state;
        _log = log;
    }

    /// <summary>Shared gate for Start and Disconnect: null when allowed.</summary>
    internal async Task<ActionResult?> RequireAdministerAsync(Guid projectId, CancellationToken ct)
    {
        if (projectId == Guid.Empty) return BadRequest(new { error = "projectId is required" });
        if (!await ProjectVisibility.CanSeeProjectAsync(_db, projectId, User, ct)) return NotFound();
        if (!await this.CanAdministerProjectAsync(_db, projectId, ct))
            return StatusCode(403, new { error = "Only a project manager or administrator can connect or disconnect ACC for this project." });
        return null;
    }

    /// <summary>
    /// AUT-6: the default grant. <c>data:create</c> was missing, and APS requires it to CREATE a
    /// webhook ("data:read ... which all Webhooks requests require, and the data:create scope,
    /// which creating a webhook requires" - Creating a Webhook (Forma Reviews) / (Forma Issues),
    /// read 2026-10-01), so POST acc/webhooks/subscribe could not create its hooks on a default
    /// grant. A connection made before this change keeps its old scopes until it is reconnected:
    /// a refresh cannot widen a grant (the refresh scope must be the same or a subset).
    /// <c>Acc:Scopes</c> still overrides.
    /// </summary>
    public const string DefaultScopes = "data:read data:write data:create";

    [HttpGet("start")]
    public async Task<ActionResult> Start([FromQuery] Guid projectId, CancellationToken ct)
    {
        var clientId = _config["Acc:ClientId"];
        var callback = _config["Acc:CallbackUrl"];
        var scopes   = string.IsNullOrWhiteSpace(_config["Acc:Scopes"]) ? DefaultScopes : _config["Acc:Scopes"]!;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(callback))
            return StatusCode(503, new { error = "acc_not_configured", message = "Acc:ClientId and Acc:CallbackUrl must be set." });

        var gate = await RequireAdministerAsync(projectId, ct);
        if (gate != null) return gate;

        var tenantId = ProjectVisibility.GetTenantId(User);
        var userId   = ProjectVisibility.GetUserId(User);
        if (tenantId == Guid.Empty || userId == Guid.Empty)
            return StatusCode(403, new { error = "Token carries no tenant/user identity." });

        string state = _state.Issue(tenantId, projectId, userId);

        var url =
            $"{ApsEndpoints.AuthorizeUrl(_config)}?response_type=code" +
            $"&client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(callback)}" +
            $"&scope={Uri.EscapeDataString(scopes)}" +
            $"&state={Uri.EscapeDataString(state)}";
        return Ok(new { authorizeUrl = url });
    }

    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<ActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return BadRequest(new { error = "missing_code_or_state" });

        AccOAuthState.Payload? p;
        AccOAuthState.RedeemError err;
        try
        {
            (p, err) = await _state.RedeemAsync(state, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "ACC OAuth callback: state store unavailable");
            return StatusCode(503, new { error = "state_store_unavailable" });
        }
        if (err == AccOAuthState.RedeemError.Invalid || p == null)
        {
            _log.LogWarning("ACC OAuth callback: invalid or expired state rejected.");
            return BadRequest(new { error = "invalid_state" });
        }
        if (err == AccOAuthState.RedeemError.Replayed)
        {
            _log.LogWarning("ACC OAuth callback: state for project {Project} was already used (or never issued) — rejected.", p?.ProjectId);
            return BadRequest(new { error = "state_already_used" });
        }

        var clientId     = _config["Acc:ClientId"];
        var clientSecret = _config["Acc:ClientSecret"];
        var callback     = _config["Acc:CallbackUrl"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret) || string.IsNullOrWhiteSpace(callback))
            return StatusCode(503, new { error = "acc_not_configured" });

        // The project must still exist in the state's tenant (no tenant context here).
        bool projectOk = await _db.Projects.IgnoreQueryFilters()
            .AnyAsync(x => x.Id == p.ProjectId && x.TenantId == p.TenantId, ct);
        if (!projectOk) return NotFound(new { error = "project_not_found" });

        var http = _http.CreateClient();
        // The authorisation code is single-use: ApsRetry retries only 429 / 503+Retry-After.
        using var resp = await ApsRetry.SendAsync(http, () =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, ApsEndpoints.TokenUrl(_config))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"]   = "authorization_code",
                    ["code"]         = code,
                    ["redirect_uri"] = callback,
                })
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
            return req;
        }, idempotent: false, _log, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            // The APS body is logged, never echoed to the browser.
            _log.LogWarning("ACC OAuth token exchange HTTP {Status}: {Body}", (int)resp.StatusCode, body.Length > 1000 ? body[..1000] : body);
            return StatusCode(502, new { error = "token_exchange_failed", status = (int)resp.StatusCode });
        }

        string? accessToken, refreshToken;
        int expiresIn;
        try
        {
            var j = JObject.Parse(body);
            accessToken  = (string?)j["access_token"];
            refreshToken = (string?)j["refresh_token"];
            expiresIn    = (int?)j["expires_in"] ?? 3600;
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            _log.LogWarning(ex, "ACC OAuth token exchange returned unparseable JSON");
            return StatusCode(502, new { error = "token_exchange_unparseable" });
        }
        if (string.IsNullOrEmpty(accessToken))
        {
            _log.LogWarning("ACC OAuth token exchange response had no access_token");
            return StatusCode(502, new { error = "token_exchange_no_access_token" });
        }
        if (string.IsNullOrEmpty(refreshToken))
            _log.LogWarning("ACC OAuth token exchange returned no refresh_token — the connection will need reconnecting in ~60 min.");

        // One row per (tenant, project, platform) — a unique index. Reconnect
        // UPDATES it (keeping the chosen ACC project / subtype / issue map);
        // inserting a second row would violate the index and 500.
        var row = await _db.PlatformConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == p.TenantId
                                   && c.ProjectId == p.ProjectId
                                   && c.Platform == PlatformType.ACC, ct);
        if (row == null)
        {
            row = new PlatformConnection
            {
                ProjectId = p.ProjectId,
                TenantId  = p.TenantId,
                Platform  = PlatformType.ACC,
                Name      = "ACC",
            };
            _db.PlatformConnections.Add(row);
        }
        row.AccessToken    = accessToken;
        row.RefreshToken   = refreshToken;
        row.TokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn - 60);
        row.IsActive       = true;
        row.LastSyncStatus = "CONNECTED";
        row.LastSyncError  = null;
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("ACC connected for project {Project} (tenant {Tenant}) by user {User}.", p.ProjectId, p.TenantId, p.UserId);

        return Content("<html><body><p>ACC connected. You can close this tab and return to Planscape.</p></body></html>",
            "text/html");
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect([FromQuery] Guid projectId, CancellationToken ct)
    {
        var gate = await RequireAdministerAsync(projectId, ct);
        if (gate != null) return gate;

        var row = await _db.PlatformConnections
            .FirstOrDefaultAsync(p => p.ProjectId == projectId && p.Platform == PlatformType.ACC, ct);
        if (row == null) return NotFound();
        row.IsActive = false;
        row.AccessToken = string.Empty;
        row.RefreshToken = null;
        row.TokenExpiresAt = null;
        row.LastSyncStatus = "DISCONNECTED";
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("ACC disconnected for project {Project} by user {User}.", projectId, ProjectVisibility.GetUserId(User));
        return NoContent();
    }
}
