using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.API.Authorization;

namespace Planscape.API.Controllers;

/// <summary>
/// Manages external BIM platform connections (ACC, Procore, Aconex, Trimble Connect).
/// Provides CRUD, connection testing, manual sync trigger, and webhook receiver.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/platform")]
[Authorize]
[ProjectAccess]
public class PlatformController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IPlatformConnectorFactory _connectorFactory;

    public PlatformController(PlanscapeDbContext db, IPlatformConnectorFactory connectorFactory)
    {
        _db = db;
        _connectorFactory = connectorFactory;
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
        var tenantId = GetTenantId();

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
            ConfigJson = request.ConfigJson,
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
        var conn = await FindConnection(projectId, id);
        if (conn == null) return NotFound();

        if (request.Name != null) conn.Name = request.Name;
        if (request.ExternalProjectId != null) conn.ExternalProjectId = request.ExternalProjectId;
        if (request.AccessToken != null) conn.AccessToken = request.AccessToken;
        if (request.RefreshToken != null) conn.RefreshToken = request.RefreshToken;
        if (request.TokenExpiresAt.HasValue) conn.TokenExpiresAt = request.TokenExpiresAt;
        if (request.WebhookSecret != null) conn.WebhookSecret = request.WebhookSecret;
        if (request.ConfigJson != null) conn.ConfigJson = request.ConfigJson;
        if (request.IsActive.HasValue) conn.IsActive = request.IsActive.Value;

        await _db.SaveChangesAsync();
        return Ok(ToDto(conn));
    }

    /// <summary>Delete a platform connection.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, Guid id)
    {
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

        var connector = _connectorFactory.GetConnector(conn.Platform);
        var result = await connector.TestConnectionAsync(conn, ct);
        return Ok(result);
    }

    /// <summary>Trigger a manual sync for a platform connection.</summary>
    [HttpPost("{id:guid}/sync")]
    public async Task<ActionResult<PlatformSyncResult>> Sync(Guid projectId, Guid id, CancellationToken ct)
    {
        var conn = await FindConnection(projectId, id);
        if (conn == null) return NotFound();

        if (!conn.IsActive)
            return BadRequest(new { message = "Connection is not active" });

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
