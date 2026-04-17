using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Services;

/// <summary>
/// S11 — ISO 19650 audit trail for all write operations.
/// Reads user / tenant / device / GPS / source context from the current
/// HttpContext (populated earlier by MobileContextMiddleware and the JWT
/// auth pipeline) and appends a single AuditLog row per write.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Append an audit log entry. EntityId is stringly-typed so this works
    /// for long identity keys, Guid keys, and composite keys alike.
    /// <paramref name="detailsJson"/> should be a JSON blob or null.
    /// </summary>
    Task LogAsync(string action, string entityType, string entityId, string? detailsJson = null);
}

public class AuditService : IAuditService
{
    private readonly PlanscapeDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditService(PlanscapeDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task LogAsync(string action, string entityType, string entityId, string? detailsJson = null)
    {
        var ctx = _httpContextAccessor.HttpContext;

        // Tenant / user are GUIDs in the Planscape schema — parse from claims.
        // If anything is missing we still write the row so the audit trail is
        // complete; we never want "can't audit" to block the underlying write.
        Guid tenantId = Guid.Empty;
        if (Guid.TryParse(ctx?.User?.FindFirst("tenant_id")?.Value, out var tid))
            tenantId = tid;

        Guid? userId = null;
        var userClaim = ctx?.User?.FindFirst("sub")?.Value
            ?? ctx?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userClaim, out var uid)) userId = uid;

        var log = new AuditLog
        {
            TenantId = tenantId,
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            DetailsJson = detailsJson,
            IpAddress = ctx?.Connection?.RemoteIpAddress?.ToString(),
            // Mobile context fields populated by MobileContextMiddleware (S12).
            DeviceId = ctx?.Items["DeviceId"] as string,
            Latitude = ctx?.Items["Latitude"] as double?,
            Longitude = ctx?.Items["Longitude"] as double?,
            Source = (ctx?.Items["Source"] as string) ?? "desktop"
            // Timestamp defaults to DateTime.UtcNow on the entity itself.
        };

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync();
    }
}
