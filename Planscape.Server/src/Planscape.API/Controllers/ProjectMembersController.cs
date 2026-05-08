using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.API.Authorization;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;
using Planscape.Infrastructure.SignalR;

namespace Planscape.API.Controllers;

/// <summary>
/// Per-project team membership management.
/// Controls which users can access a project and at what role level.
/// Requires Manager or above to modify; any member can read the list.
/// </summary>
[ApiController]
[Route("api/projects/{projectId}/members")]
[Authorize]
[ProjectAccess]
public class ProjectMembersController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly IEmailService _emailService;
    private readonly IProjectMembershipNotifier _membershipNotifier;

    public ProjectMembersController(PlanscapeDbContext db,
                                    IEmailService emailService,
                                    IProjectMembershipNotifier membershipNotifier)
    {
        _db = db;
        _emailService = emailService;
        _membershipNotifier = membershipNotifier;
    }

    // ── GET all members for a project ─────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult> GetMembers(Guid projectId)
    {
        if (!await CanAccessProjectAsync(projectId)) return NotFound();

        var members = await _db.ProjectMembers
            .Include(m => m.User)
            .Where(m => m.ProjectId == projectId && m.IsActive)
            .Select(m => new
            {
                m.Id,
                m.UserId,
                m.User!.Email,
                m.User.DisplayName,
                m.ProjectRole,
                m.Iso19650Role,
                m.JoinedAt,
                m.InvitedBy,
                // Phase 177 — surface the per-folder ACLs so the admin UI
                // (BCC Project Members tab + mobile project-settings) can
                // edit them without an extra round-trip per row.
                m.AllowedCdeStates,
                m.AllowedDisciplines,
                m.AllowedSuitabilities
            })
            .OrderBy(m => m.DisplayName)
            .ToListAsync();

        return Ok(members);
    }

    // ── Phase 177 — return *my* ACL slice for this project ─────────────────
    //
    // Plugin (BCC Deliverables tab) and mobile (documents.tsx) call this on
    // project load to learn which CDE-state tabs / discipline filters /
    // suitability dropdowns the user is permitted to see, so the UI can hide
    // controls the server would 404 on anyway.

    [HttpGet("me")]
    public async Task<ActionResult> GetMyAccess(Guid projectId)
    {
        if (!await CanAccessProjectAsync(projectId)) return NotFound();

        var subClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(subClaim, out var userId)) return Unauthorized();

        var role = User.FindFirst("role")?.Value ?? "";
        var bypass = role is "Admin" or "Owner" or "SecurityOfficer";

        var member = await _db.ProjectMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId && m.IsActive);

        return Ok(new
        {
            projectId,
            userId,
            bypassesAcl          = bypass || member == null,
            projectRole          = member?.ProjectRole,
            iso19650Role         = member?.Iso19650Role,
            allowedCdeStates     = ProjectMember.ParseAllowList(member?.AllowedCdeStates)     ?? Array.Empty<string>(),
            allowedDisciplines   = ProjectMember.ParseAllowList(member?.AllowedDisciplines)   ?? Array.Empty<string>(),
            allowedSuitabilities = ProjectMember.ParseAllowList(member?.AllowedSuitabilities) ?? Array.Empty<string>(),
        });
    }

    // ── Add a user to the project ──────────────────────────────────────────────

    [HttpPost]
    public async Task<ActionResult> AddMember(Guid projectId, [FromBody] AddMemberRequest req)
    {
        if (!await IsManagerOrAboveAsync(projectId)) return Forbid();

        var tenantId = GetTenantId();
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");

        // User must belong to same tenant
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == req.UserId && u.TenantId == tenantId && u.IsActive);
        if (user == null) return NotFound($"User {req.UserId} not found in your organisation");

        // Check for existing membership
        var existing = await _db.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == req.UserId);

        if (existing != null)
        {
            if (existing.IsActive) return Conflict("User is already a member of this project");
            // Re-activate
            existing.IsActive     = true;
            existing.ProjectRole  = req.ProjectRole ?? "Contributor";
            existing.Iso19650Role = req.Iso19650Role ?? "M";
            existing.JoinedAt     = DateTime.UtcNow;
            existing.InvitedBy    = GetCurrentUserName();
            existing.AllowedCdeStates     = ToCsv(req.AllowedCdeStates);
            existing.AllowedDisciplines   = ToCsv(req.AllowedDisciplines);
            existing.AllowedSuitabilities = ToCsv(req.AllowedSuitabilities);

            var userId1 = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid1) ? uid1 : (Guid?)null;
            _db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenantId,
                ProjectId = projectId,
                UserId = userId1,
                Action = "project_member_reactivated",
                EntityType = "ProjectMember",
                EntityId = existing.Id.ToString(),
                DetailsJson = JsonSerializer.Serialize(new { req.UserId, existing.ProjectRole, existing.Iso19650Role }),
                Timestamp = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            return Ok(new { message = "Membership re-activated", memberId = existing.Id });
        }

        var member = new ProjectMember
        {
            ProjectId    = projectId,
            UserId       = req.UserId,
            ProjectRole  = req.ProjectRole  ?? "Contributor",
            Iso19650Role = req.Iso19650Role ?? "M",
            InvitedBy    = GetCurrentUserName(),
            AllowedCdeStates     = ToCsv(req.AllowedCdeStates),
            AllowedDisciplines   = ToCsv(req.AllowedDisciplines),
            AllowedSuitabilities = ToCsv(req.AllowedSuitabilities)
        };
        _db.ProjectMembers.Add(member);

        var userId2 = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid2) ? uid2 : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            ProjectId = projectId,
            UserId = userId2,
            Action = "project_member_added",
            EntityType = "ProjectMember",
            EntityId = member.Id.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { req.UserId, member.ProjectRole, member.Iso19650Role, user.DisplayName }),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetMembers), new { projectId },
            new { message = $"{user.DisplayName} added to project", memberId = member.Id });
    }

    // ── Invite by email (user may not exist yet) ───────────────────────────────

    [HttpPost("invite")]
    public async Task<ActionResult> InviteByEmail(Guid projectId, [FromBody] InviteByEmailRequest req)
    {
        if (!await IsManagerOrAboveAsync(projectId)) return Forbid();

        var tenantId = GetTenantId();
        var project  = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");

        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.Email == req.Email.ToLowerInvariant() && u.TenantId == tenantId);

        if (user == null)
        {
            // User doesn't exist in this org — create a pending account
            var tenant = await _db.Tenants.FindAsync(tenantId);
            var userCount = await _db.Users.CountAsync(u => u.TenantId == tenantId && u.IsActive);
            if (tenant != null && userCount >= tenant.MaxUsers)
                return BadRequest($"User limit ({tenant.MaxUsers}) reached. Upgrade your plan to add more users.");

            // P10 — generate an invitation token the user can exchange for an
            // access token via POST /api/auth/accept-invitation. Stored in
            // RefreshToken with "INV:" prefix so AuthController.AcceptInvitation
            // can distinguish it from refresh / reset tokens.
            //
            // Phase 175 — store the SHA-256 of the token, not the raw value.
            // The raw value goes in the email body; the DB only ever holds
            // the hash. AcceptInvitation hashes the inbound token before
            // comparing.
            var inviteToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            var inviteTokenHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(inviteToken)));

            user = new AppUser
            {
                TenantId      = tenantId,
                Email         = req.Email.Trim().ToLowerInvariant(),
                DisplayName   = req.DisplayName ?? req.Email.Split('@')[0],
                PasswordHash  = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString(), 12), // temp password
                Role          = UserRole.Contributor,
                Iso19650Role  = req.Iso19650Role ?? "M",
                IsActive      = false,  // awaiting first login / password set
                RefreshToken  = $"INV:{inviteTokenHash}",
                RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(14),
            };
            _db.Users.Add(user);

            var userId5 = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid5) ? uid5 : (Guid?)null;
            _db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenantId,
                ProjectId = projectId,
                UserId = userId5,
                Action = "user_invited",
                EntityType = "AppUser",
                EntityId = user.Id.ToString(),
                DetailsJson = JsonSerializer.Serialize(new { user.Email, user.DisplayName, req.ProjectRole, req.Iso19650Role }),
                Timestamp = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            // Send invite email with password-reset link
            await _emailService.SendInviteEmailAsync(
                user.Email, user.DisplayName, GetCurrentUserName(),
                project.Name, $"{Request.Scheme}://{Request.Host}");
        }

        // Add to project
        var existing = await _db.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == user.Id);
        if (existing != null && existing.IsActive)
            return Conflict("User is already a member of this project");

        if (existing != null)
        {
            existing.IsActive = true; existing.JoinedAt = DateTime.UtcNow;
            existing.AllowedCdeStates     = ToCsv(req.AllowedCdeStates);
            existing.AllowedDisciplines   = ToCsv(req.AllowedDisciplines);
            existing.AllowedSuitabilities = ToCsv(req.AllowedSuitabilities);
        }
        else
        {
            _db.ProjectMembers.Add(new ProjectMember
            {
                ProjectId    = projectId, UserId = user.Id,
                ProjectRole  = req.ProjectRole  ?? "Contributor",
                Iso19650Role = req.Iso19650Role ?? "M",
                InvitedBy    = GetCurrentUserName(),
                AllowedCdeStates     = ToCsv(req.AllowedCdeStates),
                AllowedDisciplines   = ToCsv(req.AllowedDisciplines),
                AllowedSuitabilities = ToCsv(req.AllowedSuitabilities)
            });
        }

        var userId6 = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid6) ? uid6 : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            ProjectId = projectId,
            UserId = userId6,
            Action = "project_member_invited",
            EntityType = "ProjectMember",
            DetailsJson = JsonSerializer.Serialize(new { req.Email, req.ProjectRole, req.Iso19650Role, UserId = user.Id }),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        return Ok(new
        {
            message    = $"Invitation recorded for {req.Email}",
            userId     = user.Id,
            isPending  = !user.IsActive,
            note       = user.IsActive ? null : "An invitation email has been sent with instructions to set a password."
        });
    }

    // ── Update member role ─────────────────────────────────────────────────────

    [HttpPut("{memberId}")]
    public async Task<ActionResult> UpdateMember(Guid projectId, Guid memberId, [FromBody] UpdateMemberRequest req)
    {
        if (!await IsManagerOrAboveAsync(projectId)) return Forbid();

        var member = await _db.ProjectMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.ProjectId == projectId);
        if (member == null) return NotFound();

        if (req.ProjectRole  != null) member.ProjectRole  = req.ProjectRole;
        if (req.Iso19650Role != null) member.Iso19650Role = req.Iso19650Role;
        // Phase 177 — pass null array to leave a column unchanged; pass an
        // empty array to clear it; pass a non-empty array to overwrite.
        if (req.AllowedCdeStates     != null) member.AllowedCdeStates     = ToCsv(req.AllowedCdeStates);
        if (req.AllowedDisciplines   != null) member.AllowedDisciplines   = ToCsv(req.AllowedDisciplines);
        if (req.AllowedSuitabilities != null) member.AllowedSuitabilities = ToCsv(req.AllowedSuitabilities);

        var userId3 = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid3) ? uid3 : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = GetTenantId(),
            ProjectId = projectId,
            UserId = userId3,
            Action = "project_member_updated",
            EntityType = "ProjectMember",
            EntityId = memberId.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { member.ProjectRole, member.Iso19650Role }),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        return Ok(new { member.Id, member.ProjectRole, member.Iso19650Role });
    }

    // ── Remove a member ────────────────────────────────────────────────────────

    [HttpDelete("{memberId}")]
    public async Task<ActionResult> RemoveMember(Guid projectId, Guid memberId)
    {
        if (!await IsManagerOrAboveAsync(projectId)) return Forbid();

        var member = await _db.ProjectMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.ProjectId == projectId);
        if (member == null) return NotFound();

        member.IsActive = false;

        var userId4 = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid4) ? uid4 : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = GetTenantId(),
            ProjectId = projectId,
            UserId = userId4,
            Action = "project_member_removed",
            EntityType = "ProjectMember",
            EntityId = memberId.ToString(),
            DetailsJson = JsonSerializer.Serialize(new { member.UserId }),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // S4 — evict the user's running SignalR connections from the
        // project group so they stop receiving project events immediately.
        // Fire-and-forget is fine: the controller's response shouldn't
        // block on hub fan-out, and the broadcast is idempotent if it
        // races with reconnect.
        _ = _membershipNotifier.RevokeProjectAccessAsync(member.UserId, projectId);

        return NoContent();
    }

    // ── ISO 19650 roles lookup ─────────────────────────────────────────────────

    [HttpGet("roles")]
    public ActionResult GetRoles() => Ok(new[]
    {
        new { Code = "A",  Label = "Appointing Party" },
        new { Code = "PM", Label = "Project Manager" },
        new { Code = "BC", Label = "BIM Coordinator" },
        new { Code = "BA", Label = "BIM Author" },
        new { Code = "AR", Label = "Architect" },
        new { Code = "SE", Label = "Structural Engineer" },
        new { Code = "ME", Label = "MEP Engineer" },
        new { Code = "CE", Label = "Civil Engineer" },
        new { Code = "QS", Label = "Quantity Surveyor" },
        new { Code = "CA", Label = "Contract Administrator" },
        new { Code = "CT", Label = "Main Contractor" },
        new { Code = "SC", Label = "Subcontractor" },
        new { Code = "FM", Label = "Facilities Manager" },
        new { Code = "OM", Label = "Operations Manager" },
        new { Code = "CL", Label = "Client Representative" },
        new { Code = "M",  Label = "Model Author" },
        new { Code = "V",  Label = "Viewer" },
        new { Code = "Z",  Label = "Unassigned" }
    });

    // ── Helpers ────────────────────────────────────────────────────────────────

    private Guid GetTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var id) ? id : Guid.Empty;

    private string GetCurrentUserName() =>
        User.FindFirst("display_name")?.Value ?? User.FindFirst("sub")?.Value ?? "System";

    private async Task<bool> CanAccessProjectAsync(Guid projectId)
    {
        // Phase 175 — visibility is author OR active member OR tenant admin.
        // The previous tenant-only check leaked project membership across
        // un-invited users in the same tenant.
        return await ProjectVisibility.CanSeeProjectAsync(_db, projectId, User);
    }

    private async Task<bool> IsManagerOrAboveAsync(Guid projectId)
    {
        if (!await CanAccessProjectAsync(projectId)) return false;
        var role = User.FindFirst("role")?.Value ?? "";
        return role is "Manager" or "Admin" or "Owner";
    }

    // Phase 177 — normalise inbound array → CSV; null/empty array means
    // "no narrowing for this axis" so it persists as null.
    private static string? ToCsv(string[]? arr)
    {
        if (arr == null) return null;
        var cleaned = arr.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
        return cleaned.Length == 0 ? null : string.Join(',', cleaned);
    }
}

// ── Request DTOs ───────────────────────────────────────────────────────────────
//
// Phase 177 — three optional ACL allow-lists are accepted as either CSV
// ("WIP,SHARED") or string[] from the mobile JSON serialiser. The
// controller normalises both to CSV before persisting.

public record AddMemberRequest(
    Guid    UserId,
    string? ProjectRole,
    string? Iso19650Role,
    string[]? AllowedCdeStates     = null,
    string[]? AllowedDisciplines   = null,
    string[]? AllowedSuitabilities = null);

public record InviteByEmailRequest(
    string  Email,
    string? DisplayName,
    string? ProjectRole,
    string? Iso19650Role,
    string[]? AllowedCdeStates     = null,
    string[]? AllowedDisciplines   = null,
    string[]? AllowedSuitabilities = null);

public record UpdateMemberRequest(
    string? ProjectRole,
    string? Iso19650Role,
    string[]? AllowedCdeStates     = null,
    string[]? AllowedDisciplines   = null,
    string[]? AllowedSuitabilities = null);
