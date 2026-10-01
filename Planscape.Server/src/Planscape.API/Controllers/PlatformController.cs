using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Infrastructure.Services;

namespace Planscape.API.Controllers;

/// <summary>
/// Manages external BIM platform connections (ACC, Procore, Aconex, Trimble Connect).
/// Provides CRUD, connection testing, manual sync trigger, and webhook receiver.
///
/// ACC connections are NOT handled by the generic connector path here: /test and
/// /sync delegate to <see cref="AccSyncService"/>, the single ACC code path. It
/// refreshes through AccTokenRefresher (advisory lock, rotated refresh token
/// persisted at once, a token rotated by another process adopted) and records the
/// honest OK / PARTIAL / FAILED / BUSY / RECONNECT_REQUIRED status. Calling
/// AccConnector directly used to bypass the lock and overwrite that status with
/// a count-only "OK".
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/platform")]
[Authorize]
[ProjectAccess]
public class PlatformController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IPlatformConnectorFactory _connectorFactory;
    private readonly AccSyncService _acc;

    public PlatformController(PlanscapeDbContext db, IPlatformConnectorFactory connectorFactory, AccSyncService acc)
    {
        _db = db;
        _connectorFactory = connectorFactory;
        _acc = acc;
    }

    // ── CRUD ──

    /// <summary>List all platform connections for a project.</summary>
    [HttpGet]
    public async Task<ActionResult<List<PlatformConnectionDto>>> List(Guid projectId)
    {
        var tenantId = GetTenantId();
        var connections = await _db.PlatformConnections
            .Where(c => c.TenantId == tenantId && c.ProjectId == projectId)
            .OrderBy(c => c.Platform)
            .Select(c => ToDto(c))
            .ToListAsync();

        return Ok(connections);
    }

    /// <summary>Get a single platform connection by ID.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PlatformConnectionDto>> Get(Guid projectId, Guid id)
    {
        var conn = await FindConnection(projectId, id);
        if (conn == null) return NotFound();
        return Ok(ToDto(conn));
    }

    /// <summary>Create a new platform connection.</summary>
    [HttpPost]
    public async Task<ActionResult<PlatformConnectionDto>> Create(Guid projectId, [FromBody] CreatePlatformConnectionRequest request)
    {
        if (!await this.CanAdministerProjectAsync(_db, projectId)) return AdministerForbidden();
        if (RejectCiphertextTokens(request.AccessToken, request.RefreshToken) is { } bad) return bad;
        var tenantId = GetTenantId();

        // Server-owned keys (the ACC issue map) are never accepted from a client.
        string? config = null;
        if (request.ConfigJson != null)
        {
            var (merged, cfgError) = AccSyncService.MergeClientConfig(null, request.ConfigJson);
            if (merged == null) return BadRequest(new { error = cfgError });
            config = merged;
        }

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");

        // Check for duplicate platform connection
        var exists = await _db.PlatformConnections
            .AnyAsync(c => c.TenantId == tenantId && c.ProjectId == projectId && c.Platform == request.Platform);
        if (exists)
            return Conflict(new { message = $"A {request.Platform} connection already exists for this project" });

        var conn = new PlatformConnection
        {
            TenantId = tenantId,
            ProjectId = projectId,
            Platform = request.Platform,
            Name = request.Name,
            ExternalProjectId = request.ExternalProjectId ?? "",
            AccessToken = request.AccessToken,
            RefreshToken = request.RefreshToken,
            TokenExpiresAt = request.TokenExpiresAt,
            WebhookSecret = request.WebhookSecret,
            ConfigJson = config,
            IsActive = true
        };

        _db.PlatformConnections.Add(conn);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { projectId, id = conn.Id }, ToDto(conn));
    }

    /// <summary>Update an existing platform connection.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PlatformConnectionDto>> Update(Guid projectId, Guid id, [FromBody] UpdatePlatformConnectionRequest request)
    {
        if (!await this.CanAdministerProjectAsync(_db, projectId)) return AdministerForbidden();
        if (RejectCiphertextTokens(request.AccessToken, request.RefreshToken) is { } bad) return bad;
        var conn = await FindConnection(projectId, id);
        if (conn == null) return NotFound();

        // ConfigJson is MERGED: the server-owned keys (accIssueMap, accIssueStatus…)
        // are kept from the stored copy whatever the client sends. A client that
        // round-tripped a stale copy — or dropped the map — would otherwise make
        // the next ACC sync re-push every issue as a duplicate.
        if (request.ConfigJson != null)
        {
            var (merged, cfgError) = AccSyncService.MergeClientConfig(conn.ConfigJson, request.ConfigJson);
            if (merged == null) return BadRequest(new { error = cfgError });
            conn.ConfigJson = merged;
        }

        if (request.Name != null) conn.Name = request.Name;
        if (request.ExternalProjectId != null)
        {
            // S3: a different ACC project gets a clean issue state (the old one is archived).
            if (conn.Platform == PlatformType.ACC && !string.IsNullOrWhiteSpace(conn.ConfigJson))
            {
                try
                {
                    if (Newtonsoft.Json.Linq.JToken.Parse(conn.ConfigJson) is Newtonsoft.Json.Linq.JObject cfg
                        && AccSyncService.ArchiveForProjectChange(cfg, conn.ExternalProjectId, request.ExternalProjectId) >= 0)
                        conn.ConfigJson = cfg.ToString(Newtonsoft.Json.Formatting.None);
                }
                catch (Newtonsoft.Json.JsonException)
                {
                    return BadRequest(new { error = "The stored ConfigJson is unreadable; it may hold the ACC issue map. Repair it before changing the ACC project." });
                }
            }
            conn.ExternalProjectId = request.ExternalProjectId;
        }
        if (request.AccessToken != null) conn.AccessToken = request.AccessToken;
        if (request.RefreshToken != null) conn.RefreshToken = request.RefreshToken;
        if (request.TokenExpiresAt.HasValue) conn.TokenExpiresAt = request.TokenExpiresAt;
        if (request.WebhookSecret != null) conn.WebhookSecret = request.WebhookSecret;
        if (request.IsActive.HasValue) conn.IsActive = request.IsActive.Value;

        await _db.SaveChangesAsync();
        return Ok(ToDto(conn));
    }

    /// <summary>Delete a platform connection.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, Guid id)
    {
        if (!await this.CanAdministerProjectAsync(_db, projectId)) return AdministerForbidden();
        var conn = await FindConnection(projectId, id);
        if (conn == null) return NotFound();

        _db.PlatformConnections.Remove(conn);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Actions ──

    /// <summary>Test connectivity for a platform connection.</summary>
    [HttpPost("{id:guid}/test")]
    public async Task<ActionResult<PlatformTestResult>> TestConnection(Guid projectId, Guid id, CancellationToken ct)
    {
        var conn = await FindConnection(projectId, id);
        if (conn == null) return NotFound();

        // ACC: one code path — the locked, persisting refresher in AccSyncService.
        if (conn.Platform == PlatformType.ACC)
            return Ok(await _acc.TestConnectionAsync(conn, ct));

        var connector = _connectorFactory.GetConnector(conn.Platform);
        var result = await connector.TestConnectionAsync(conn, ct);
        // TestConnection may have refreshed the OAuth token. Providers like ACC
        // rotate the refresh token on every refresh, so persist any rotation —
        // otherwise the stored token is left invalid and the next op fails.
        await _db.SaveChangesAsync(ct);
        return Ok(result);
    }

    /// <summary>
    /// Trigger a manual sync for a platform connection. For ACC this is exactly
    /// POST acc/sync (same role gate, same service, same status codes) and the
    /// body is the <see cref="AccSyncService.AccSyncReport"/>.
    /// </summary>
    [HttpPost("{id:guid}/sync")]
    public async Task<IActionResult> Sync(Guid projectId, Guid id, CancellationToken ct)
    {
        var conn = await FindConnection(projectId, id);
        if (conn == null) return NotFound();

        if (!conn.IsActive)
            return BadRequest(new { message = "Connection is not active" });

        if (conn.Platform == PlatformType.ACC)
        {
            if (!await this.CanWriteProjectAsync(_db, projectId, ct))
                return StatusCode(403, new { error = "A read-only project role cannot push issues to ACC." });
            // One active ACC connection per project (unique TenantId+ProjectId+Platform),
            // so the project-scoped sync acts on exactly this row.
            var report = await _acc.SyncProjectAsync(projectId, ct);
            return report.Status switch
            {
                AccSyncService.StatusOk or AccSyncService.StatusPartial => Ok(report),
                AccSyncService.StatusBusy or AccSyncService.StatusReconnect => Conflict(report),
                _ => StatusCode(502, report),
            };
        }

        var connector = _connectorFactory.GetConnector(conn.Platform);

        var elements = await _db.TaggedElements
            .Where(e => e.ProjectId == projectId)
            .ToListAsync(ct);

        var result = await connector.SyncAsync(conn, elements, ct);

        conn.LastSyncAt = DateTime.UtcNow;
        conn.LastSyncStatus = result.Success ? "OK" : "FAILED";
        conn.LastSyncError = result.Error;
        await _db.SaveChangesAsync(ct);

        return Ok(result);
    }

    /// <summary>
    /// Receive a webhook callback from an external platform.
    /// This endpoint is unauthenticated — verification uses the connection's WebhookSecret.
    /// </summary>
    [HttpPost("{id:guid}/webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(Guid projectId, Guid id, CancellationToken ct)
    {
        var conn = await _db.PlatformConnections
            .FirstOrDefaultAsync(c => c.Id == id && c.ProjectId == projectId, ct);
        if (conn == null) return NotFound();

        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(ct);
        var signature = Request.Headers["X-Webhook-Signature"].FirstOrDefault()
            ?? Request.Headers["X-Hub-Signature-256"].FirstOrDefault();

        var connector = _connectorFactory.GetConnector(conn.Platform);
        var result = await connector.HandleWebhookAsync(conn, payload, signature, ct);

        return result.Handled ? Ok(new { result.Action }) : BadRequest(new { result.Error });
    }

    // ── Helpers ──

    // Create / Update / Delete can replace the team-shared OAuth tokens, so they
    // take the same gate as connecting ACC (AccOAuthController.Start).
    /// <summary>
    /// A client may not store a value that looks like our own ciphertext: it would
    /// be written through unchanged and then read back as an undecryptable token.
    /// </summary>
    private ObjectResult? RejectCiphertextTokens(params string?[] tokens)
        => tokens.Any(Planscape.Infrastructure.Security.PlatformTokenProtection.IsEncrypted)
            ? BadRequest(new { error = $"Token values may not start with '{Planscape.Infrastructure.Security.PlatformTokenProtection.Prefix}'." })
            : null;

    private ObjectResult AdministerForbidden()
        => StatusCode(403, new { error = "Only a project manager or administrator can change platform connections." });

    private Guid GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private async Task<PlatformConnection?> FindConnection(Guid projectId, Guid id)
    {
        var tenantId = GetTenantId();
        return await _db.PlatformConnections
            .FirstOrDefaultAsync(c => c.Id == id && c.ProjectId == projectId && c.TenantId == tenantId);
    }

    private static PlatformConnectionDto ToDto(PlatformConnection c) => new()
    {
        Id = c.Id,
        Platform = c.Platform,
        Name = c.Name,
        ExternalProjectId = c.ExternalProjectId,
        IsActive = c.IsActive,
        CreatedAt = c.CreatedAt,
        LastSyncAt = c.LastSyncAt,
        LastSyncStatus = c.LastSyncStatus,
        LastSyncError = c.LastSyncError,
        HasAccessToken = !string.IsNullOrEmpty(c.AccessToken),
        HasRefreshToken = !string.IsNullOrEmpty(c.RefreshToken),
        TokensUnreadable = AccTokenRefresher.TokensUnreadable(c),
        TokenExpiresAt = c.TokenExpiresAt,
        ConfigJson = c.ConfigJson
    };
}

// ── DTOs ──

public class PlatformConnectionDto
{
    public Guid Id { get; set; }
    public PlatformType Platform { get; set; }
    public string Name { get; set; } = "";
    public string ExternalProjectId { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public string? LastSyncStatus { get; set; }
    public string? LastSyncError { get; set; }
    public bool HasAccessToken { get; set; }
    public bool HasRefreshToken { get; set; }
    /// <summary>The stored tokens exist but cannot be decrypted (key ring changed) — reconnect.</summary>
    public bool TokensUnreadable { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    public string? ConfigJson { get; set; }
}

public class CreatePlatformConnectionRequest
{
    public PlatformType Platform { get; set; }
    public string Name { get; set; } = "";
    public string? ExternalProjectId { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    public string? WebhookSecret { get; set; }
    public string? ConfigJson { get; set; }
}

public class UpdatePlatformConnectionRequest
{
    public string? Name { get; set; }
    public string? ExternalProjectId { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    public string? WebhookSecret { get; set; }
    public string? ConfigJson { get; set; }
    public bool? IsActive { get; set; }
}
