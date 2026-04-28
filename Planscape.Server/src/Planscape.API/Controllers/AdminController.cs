using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// Organization and user administration — org management, user CRUD, audit log.
/// Requires Admin role.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin,Owner")]
public class AdminController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly Planscape.Infrastructure.Authorization.IPermissionRevocationStore _revocations;

    public AdminController(
        PlanscapeDbContext db,
        Planscape.Infrastructure.Authorization.IPermissionRevocationStore revocations)
    {
        _db = db;
        _revocations = revocations;
    }

    // ── Organization Management ──

    [HttpGet("org")]
    public async Task<ActionResult> GetOrganization()
    {
        var tenantId = GetTenantId();
        var tenant = await _db.Tenants
            .Include(t => t.Users)
            .Include(t => t.Projects)
            .FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant == null) return NotFound();

        return Ok(new
        {
            tenant.Id, tenant.Name, tenant.Slug, tenant.Tier,
            tenant.MaxUsers, tenant.MaxProjects, tenant.MimEnabled,
            UserCount = tenant.Users.Count,
            ProjectCount = tenant.Projects.Count,
            ActiveProjects = tenant.Projects.Count(p => p.Status == ProjectStatus.Active)
        });
    }

    // ── User Management ──

    [HttpGet("users")]
    public async Task<ActionResult> GetUsers()
    {
        var tenantId = GetTenantId();
        var users = await _db.Users
            .Where(u => u.TenantId == tenantId)
            .Select(u => new
            {
                u.Id, u.Email, u.DisplayName, u.Role, u.Iso19650Role,
                u.IsActive, u.LastLoginAt, u.CreatedAt
            })
            .OrderBy(u => u.DisplayName)
            .ToListAsync();

        return Ok(users);
    }

    [HttpPost("users")]
    public async Task<ActionResult> CreateUser([FromBody] CreateUserRequest req)
    {
        var tenantId = GetTenantId();
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant == null) return NotFound("Tenant not found");

        var userCount = await _db.Users.CountAsync(u => u.TenantId == tenantId && u.IsActive);
        if (userCount >= tenant.MaxUsers)
            return BadRequest($"User limit ({tenant.MaxUsers}) reached for {tenant.Tier} tier");

        if (await _db.Users.AnyAsync(u => u.Email == req.Email))
            return Conflict($"Email {req.Email} already exists");

        var user = new AppUser
        {
            TenantId = tenantId,
            Email = req.Email,
            DisplayName = req.DisplayName,
            PasswordHash = HashPassword(req.Password),
            Role = Enum.TryParse<UserRole>(req.Role, true, out var r) ? r : UserRole.Contributor,
            Iso19650Role = req.Iso19650Role ?? "M"
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetUsers), new { id = user.Id }, new { user.Id, user.Email, user.DisplayName });
    }

    [HttpPut("users/{userId}")]
    public async Task<ActionResult> UpdateUser(Guid userId, [FromBody] UpdateUserRequest req)
    {
        var tenantId = GetTenantId();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId);
        if (user == null) return NotFound();

        // Phase 156 — detect permission-changing fields before
        // applying. If any of them changed value, bump the user's
        // revocation floor after the save commits so old tokens
        // can't pivot through policy-gated endpoints. Display-name
        // changes don't trigger revocation — they're not security-
        // relevant.
        var permissionChanged = false;
        if (req.DisplayName != null) user.DisplayName = req.DisplayName;
        if (req.Role != null && Enum.TryParse<UserRole>(req.Role, true, out var r) && user.Role != r)
        {
            user.Role = r; permissionChanged = true;
        }
        if (req.Iso19650Role != null && user.Iso19650Role != req.Iso19650Role)
        {
            user.Iso19650Role = req.Iso19650Role; permissionChanged = true;
        }
        if (req.IsActive.HasValue && user.IsActive != req.IsActive.Value)
        {
            user.IsActive = req.IsActive.Value; permissionChanged = true;
        }

        await _db.SaveChangesAsync();
        if (permissionChanged)
        {
            // Fire-and-forget; a Redis blip mustn't block the admin
            // action. The store no-ops on connection failure so the
            // worst case is the pre-Phase-156 lag for that one user.
            _ = _revocations.RevokeAllPriorTokensAsync(user.Id);
        }
        return Ok(new { user.Id, user.Email, user.DisplayName, user.Role, user.IsActive });
    }

    // Phase 157 added a revoke-tokens endpoint here. Phase 158 moved
    // it to the new SecurityController under the SecurityOfficerOrAdmin
    // policy so the action no longer requires tenant-Admin/Owner role
    // (separation of duties for SOC2 / ISO 27001). The new route is
    // POST /api/security/users/{userId}/revoke-tokens and accepts a
    // caller-supplied reason + category.

    // ── Audit Log ──

    [HttpGet("audit")]
    public async Task<ActionResult> GetAuditLog(
        [FromQuery] Guid? projectId = null, [FromQuery] string? action = null,
        [FromQuery] string? source = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var tenantId = GetTenantId();
        var query = _db.AuditLogs.Where(a => a.TenantId == tenantId);

        if (projectId.HasValue) query = query.Where(a => a.ProjectId == projectId);
        if (!string.IsNullOrEmpty(action)) query = query.Where(a => a.Action == action);
        // M12 — let admins filter by which client originated the write so a
        // misbehaving mobile build or plugin version can be triaged at a glance.
        if (!string.IsNullOrEmpty(source)) query = query.Where(a => a.Source == source);

        var total = await query.CountAsync();
        var logs = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        return Ok(new { logs, total, page, pageSize });
    }

    // ── License Management ──

    [HttpGet("licenses")]
    public async Task<ActionResult> GetLicenses()
    {
        var tenantId = GetTenantId();
        var keys = await _db.LicenseKeys
            .Where(k => k.TenantId == tenantId)
            .Select(k => new
            {
                k.Id, k.Key, k.Tier, k.MaxActivations, k.CurrentActivations,
                k.MimEnabled, k.IsActive, k.ExpiresAt, k.CreatedAt,
                k.LastActivatedBy, k.LastActivatedAt
            })
            .ToListAsync();

        return Ok(keys);
    }

    // Phase 151 → 152: tenant-keywords endpoints moved to
    // TenantKeywordsController so the auth gate can drop the
    // class-level Admin/Owner restriction in favour of the finer-
    // grained BimManagerOrAdmin policy. Routes are unchanged
    // (/api/admin/tenant-keywords) so existing clients are unaffected.

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;

    private static string HashPassword(string password)
        => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
}

public record CreateUserRequest(string Email, string DisplayName, string Password, string? Role, string? Iso19650Role);
public record UpdateUserRequest(string? DisplayName, string? Role, string? Iso19650Role, bool? IsActive);
/// <summary>Phase 151 — body for PUT /admin/tenant-keywords. Null/empty Json clears.</summary>
public record TenantKeywordsRequest(string? Json);
