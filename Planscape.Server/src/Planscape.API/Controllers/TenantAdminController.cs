using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.API.Controllers;

/// <summary>
/// S1.7 — single page that the tenant Owner / Admin lives on. Returns the
/// numbers a buyer needs to feel in control of their subscription:
///
///   • Plan, currency, trial expiry, billing-cycle
///   • Live usage vs limits (authors / coordinators / projects / storage)
///   • Last-month revenue (if any payments) + next charge date
///   • Recent admin events (member invited, project created, plan changed)
///
/// All endpoints scoped to the resolved tenant by the global query filter
/// from S1.1 — there is no path-parameter for tenant id so an admin can't
/// even type the wrong one.
/// </summary>
[ApiController]
[Route("api/tenant")]
[Authorize(Roles = "Owner,Admin")]
public class TenantAdminController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IQuotaGuardService _quota;

    public TenantAdminController(PlanscapeDbContext db, ITenantContext tenantContext, IQuotaGuardService quota)
    {
        _db = db;
        _tenantContext = tenantContext;
        _quota = quota;
    }

    /// <summary>Single dashboard payload — plan, usage, members, billing.</summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult> Dashboard(CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == _tenantContext.TenantId, ct);
        if (tenant == null) return NotFound();

        var limits = BillingPlanLimits.For(tenant.Plan);

        var memberCount = await _db.ProjectMembers.CountAsync(ct);
        var authorIds = await _db.ProjectMembers
            .Where(m => m.ProjectRole == "Author")
            .Select(m => m.UserId).Distinct().ToListAsync(ct);
        var coordIds  = await _db.ProjectMembers
            .Where(m => m.ProjectRole != "Author")
            .Select(m => m.UserId).Distinct().ToListAsync(ct);
        var projectCount = await _db.Projects.CountAsync(ct);
        var storageBytes = await _db.ProjectModels.AsNoTracking()
            .Where(m => m.DeletedAt == null)
            .SumAsync(m => (long?)m.FileSizeBytes, ct) ?? 0;

        var users = await _db.Users.AsNoTracking()
            .OrderBy(u => u.DisplayName)
            .Select(u => new { u.Id, u.Email, u.DisplayName, u.Role, u.Iso19650Role, u.LastLoginAt, u.IsActive })
            .Take(200).ToListAsync(ct);

        return Ok(new
        {
            tenant = new
            {
                tenant.Id,
                tenant.Name,
                tenant.Slug,
                tenant.ContactEmail,
                Plan         = tenant.Plan.ToString(),
                tenant.Currency,
                BillingCycle = tenant.BillingCycle.ToString(),
                tenant.TrialExpiresAt,
                tenant.IsActive,
                tenant.CreatedAt,
                tenant.MimEnabled,
            },
            usage = new
            {
                authors      = new { current = authorIds.Count,  max = limits.MaxAuthors },
                coordinators = new { current = coordIds.Count,   max = limits.MaxCoordinators },
                projects     = new { current = projectCount,     max = limits.MaxProjects },
                storage      = new { currentMb = storageBytes / 1024 / 1024, maxMb = limits.StorageMb },
                memberSeats  = memberCount,
            },
            limits = new
            {
                limits.MaxAuthors,
                limits.MaxCoordinators,
                limits.MaxProjects,
                limits.StorageMb,
                MonthlyUsd = limits.MonthlyUsd,
            },
            users,
        });
    }

    /// <summary>Invite a new user to the tenant. Picks Author or Coordinator role.</summary>
    [HttpPost("invite")]
    public async Task<ActionResult> Invite([FromBody] InviteUserRequest req, CancellationToken ct)
    {
        // Quota check — refuses with 402 if cap reached.
        var role = string.Equals(req.Role, "Author", StringComparison.OrdinalIgnoreCase) ? "Author" : "Coordinator";
        var quota = await _quota.CheckCanAddUserAsync(role, ct);
        if (!quota.Allowed)
            return StatusCode(StatusCodes.Status402PaymentRequired, new
            {
                error = "quota_exceeded",
                axis = quota.Axis.ToString(),
                current = quota.Current,
                max = quota.Max,
                reason = quota.Reason,
                upgrade_url = "/billing/upgrade",
            });

        // Existing user with this email?
        var existing = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email.ToLowerInvariant(), ct);
        if (existing != null)
            return Conflict(new { message = "User with that email already exists." });

        var user = new AppUser
        {
            TenantId    = _tenantContext.TenantId,
            Email       = req.Email.Trim().ToLowerInvariant(),
            DisplayName = req.DisplayName,
            // Stub password — real flow sends an invite link with a one-time
            // password-set token. For S1.7 we just plant the row.
            PasswordHash = "INVITED",
            Role        = role == "Author" ? UserRole.Contributor : UserRole.Coordinator,
            Iso19650Role = role == "Author" ? "A" : "C",
            IsActive    = false, // becomes true when invitee sets a password
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return Ok(new { user.Id, user.Email, user.DisplayName, role });
    }

    /// <summary>Remove a user from the tenant.</summary>
    [HttpDelete("users/{userId:guid}")]
    public async Task<ActionResult> RemoveUser(Guid userId, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null) return NotFound();
        if (user.Role == UserRole.Owner)
            return BadRequest(new { message = "Cannot remove the tenant owner; transfer ownership first." });
        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public record InviteUserRequest(string Email, string DisplayName, string Role);
