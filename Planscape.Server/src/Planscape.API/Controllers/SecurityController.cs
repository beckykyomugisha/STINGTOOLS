using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Services;
using Planscape.Infrastructure.Authorization;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 158 — separation-of-duties surface for security-sensitive
/// admin actions. Gated by the <c>SecurityOfficerOrAdmin</c> policy
/// so a tenant can have a dedicated SecurityOfficer persona who can
/// terminate sessions + read audit logs without holding tenant Admin
/// privileges (which would also grant user / project / vocabulary
/// edit).
///
/// Routes:
///   POST /api/security/users/{userId}/revoke-tokens
///     — replaces the Phase 157 /api/admin/users/{id}/revoke-tokens
///     route which sat under the class-level Admin/Owner gate.
///     Old route kept on AdminController for backward compatibility
///     during the transition.
/// </summary>
[ApiController]
[Route("api/security")]
[Authorize(Policy = "SecurityOfficerOrAdmin")]
public class SecurityController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IPermissionRevocationStore _revocations;
    private readonly IAuditService _audit;

    public SecurityController(
        PlanscapeDbContext db,
        IPermissionRevocationStore revocations,
        IAuditService audit)
    {
        _db = db;
        _revocations = revocations;
        _audit = audit;
    }

    /// <summary>
    /// Phase 158 — explicit "revoke this user's sessions" with a
    /// caller-supplied reason captured in the audit log. The reason
    /// is free-text but the API surfaces commonly-used categories
    /// (suspected_credential_leak / employee_offboarding /
    /// scheduled_rotation / suspicious_activity) so SOC2 reviews
    /// can be filtered by classification later.
    /// </summary>
    [HttpPost("users/{userId}/revoke-tokens")]
    public async Task<ActionResult> RevokeTokens(Guid userId, [FromBody] RevokeTokensRequest? req = null)
    {
        var tenantId = GetTenantId();
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId);
        if (user == null) return NotFound();

        var reason = (req?.Reason ?? "").Trim();
        var category = (req?.Category ?? "").Trim().ToLowerInvariant();

        // Cap the reason length so a pasted novel doesn't bloat the
        // audit table; 500 chars is plenty for "credential leak
        // reported by user via support ticket #12345" style entries.
        if (reason.Length > 500) reason = reason[..500];

        await _revocations.RevokeAllPriorTokensAsync(userId);
        await _audit.LogAsync("USER_REVOKE", "AppUser", userId.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                target = user.Email,
                reason = string.IsNullOrEmpty(reason) ? "(no reason supplied)" : reason,
                category = string.IsNullOrEmpty(category) ? "unspecified" : category,
                via = "security_controller",
            }));

        return Ok(new
        {
            userId,
            email = user.Email,
            revokedAt = DateTime.UtcNow,
            reason = reason.Length > 0 ? reason : (string?)null,
            category = category.Length > 0 ? category : (string?)null,
            note = "All previously-issued tokens for this user will be denied on next policy-gated request.",
        });
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;
}

/// <summary>
/// Phase 158 — body for POST /security/users/{id}/revoke-tokens.
/// Both fields are optional. <c>Category</c> is intended for
/// SOC2-friendly classification (suspected_credential_leak /
/// employee_offboarding / scheduled_rotation / suspicious_activity);
/// <c>Reason</c> is free-text context, capped at 500 chars
/// server-side.
/// </summary>
public record RevokeTokensRequest(string? Reason, string? Category);
