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

    public AdminController(PlanscapeDbContext db) => _db = db;

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

        if (req.DisplayName != null) user.DisplayName = req.DisplayName;
        if (req.Role != null && Enum.TryParse<UserRole>(req.Role, true, out var r)) user.Role = r;
        if (req.Iso19650Role != null) user.Iso19650Role = req.Iso19650Role;
        if (req.IsActive.HasValue) user.IsActive = req.IsActive.Value;

        await _db.SaveChangesAsync();
        return Ok(new { user.Id, user.Email, user.DisplayName, user.Role, user.IsActive });
    }

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

    // ── Tenant keyword extensions (Phase 151) ────────────────────────

    /// <summary>
    /// Phase 151 — read the tenant's deliverable-state-machine keyword
    /// extensions. Returns the raw JSON string + a parsed preview so
    /// the office dashboard can show what's currently in effect. Empty
    /// when the tenant has no extensions configured.
    /// </summary>
    [HttpGet("tenant-keywords")]
    public async Task<ActionResult> GetTenantKeywords()
    {
        var tenantId = GetTenantId();
        var json = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.KeywordExtensionsJson)
            .FirstOrDefaultAsync();
        return Ok(new
        {
            tenantId,
            hasExtensions = !string.IsNullOrWhiteSpace(json),
            json,
        });
    }

    /// <summary>
    /// Phase 151 — replace the tenant's keyword extensions JSON. Empty
    /// body / null clears the extensions. The body is validated by
    /// parsing through the same canonical-bucket / typo-skip rules
    /// applied at request time, so a malformed payload is rejected
    /// here rather than silently ignored at runtime.
    /// </summary>
    [HttpPut("tenant-keywords")]
    public async Task<ActionResult> SetTenantKeywords([FromBody] TenantKeywordsRequest req)
    {
        var tenantId = GetTenantId();
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant == null) return NotFound();

        if (string.IsNullOrWhiteSpace(req.Json))
        {
            tenant.KeywordExtensionsJson = null;
            await _db.SaveChangesAsync();
            return Ok(new { tenantId, cleared = true });
        }

        // Validate by parsing — if the JSON is malformed or has no
        // recognised buckets, reject with 400 rather than silently
        // storing it.
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> parsed;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(req.Json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                return BadRequest(new { error = "Body must be a JSON object" });
        }
        catch (System.Text.Json.JsonException)
        {
            return BadRequest(new { error = "Body is not valid JSON" });
        }
        // The DbTenantKeywordResolver does the bucket-by-bucket parse.
        // We invoke a one-shot static helper to validate without going
        // through DbContext.
        parsed = Planscape.Infrastructure.Workflow.DbTenantKeywordResolver.ParseForValidation(req.Json);
        if (parsed.Count == 0)
            return BadRequest(new
            {
                error = "JSON has no recognised keyword buckets",
                hint = "Body shape: { \"working\": [\"PARKED\"], \"terminal\": [\"FROZEN\"] }",
            });

        tenant.KeywordExtensionsJson = req.Json;
        await _db.SaveChangesAsync();
        return Ok(new
        {
            tenantId,
            buckets = parsed.Count,
            entries = parsed.Sum(kv => kv.Value.Count),
        });
    }

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;

    private static string HashPassword(string password)
        => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
}

public record CreateUserRequest(string Email, string DisplayName, string Password, string? Role, string? Iso19650Role);
public record UpdateUserRequest(string? DisplayName, string? Role, string? Iso19650Role, bool? IsActive);
/// <summary>Phase 151 — body for PUT /admin/tenant-keywords. Null/empty Json clears.</summary>
public record TenantKeywordsRequest(string? Json);
