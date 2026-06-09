using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    private readonly IConfiguration _config;
    private readonly ILogger<ProjectMembersController> _logger;

    public ProjectMembersController(PlanscapeDbContext db,
                                    IEmailService emailService,
                                    IProjectMembershipNotifier membershipNotifier,
                                    IConfiguration config,
                                    ILogger<ProjectMembersController> logger)
    {
        _db = db;
        _emailService = emailService;
        _membershipNotifier = membershipNotifier;
        _config = config;
        _logger = logger;
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
            // Phase 177-D — preset baseline; explicit request fields still win.
            var profileExisting = await ResolveProfileAsync(req.AccessProfileId);
            existing.IsActive     = true;
            existing.ProjectRole  = req.ProjectRole  ?? profileExisting?.DefaultProjectRole  ?? "Contributor";
            existing.Iso19650Role = req.Iso19650Role ?? profileExisting?.DefaultIso19650Role ?? "M";
            existing.JoinedAt     = DateTime.UtcNow;
            existing.InvitedBy    = GetCurrentUserName();
            existing.AllowedCdeStates     = req.AllowedCdeStates     != null ? ToCsv(req.AllowedCdeStates)     : profileExisting?.AllowedCdeStates;
            existing.AllowedDisciplines   = req.AllowedDisciplines   != null ? ToCsv(req.AllowedDisciplines)   : profileExisting?.AllowedDisciplines;
            existing.AllowedSuitabilities = req.AllowedSuitabilities != null ? ToCsv(req.AllowedSuitabilities) : profileExisting?.AllowedSuitabilities;

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

        // Phase 177-D — resolve preset; explicit request fields still win.
        var profile = await ResolveProfileAsync(req.AccessProfileId);
        var member = new ProjectMember
        {
            ProjectId    = projectId,
            UserId       = req.UserId,
            ProjectRole  = req.ProjectRole  ?? profile?.DefaultProjectRole  ?? "Contributor",
            Iso19650Role = req.Iso19650Role ?? profile?.DefaultIso19650Role ?? "M",
            InvitedBy    = GetCurrentUserName(),
            AllowedCdeStates     = req.AllowedCdeStates     != null ? ToCsv(req.AllowedCdeStates)     : profile?.AllowedCdeStates,
            AllowedDisciplines   = req.AllowedDisciplines   != null ? ToCsv(req.AllowedDisciplines)   : profile?.AllowedDisciplines,
            AllowedSuitabilities = req.AllowedSuitabilities != null ? ToCsv(req.AllowedSuitabilities) : profile?.AllowedSuitabilities,
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
        // Authorize by PROJECT role (see AuthorizeManageAsync). On denial, log
        // the reason and return JSON { message } the plugin surfaces — never a
        // bare 403 (which the user can't act on).
        var auth = await AuthorizeManageAsync(projectId);
        if (!auth.ok)
        {
            _logger.LogWarning("[invite] denied: caller {UserId} role {Role} on project {ProjectId} — {Reason}",
                ProjectVisibility.GetUserId(User), auth.callerRole, projectId, auth.reason);
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = $"You don't have permission to invite members to this project ({auth.reason})."
            });
        }

        var tenantId = GetTenantId();
        var project  = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.TenantId == tenantId);
        if (project == null) return NotFound("Project not found");

        // Resolve the public base URL once (Planscape:PublicBaseUrl when set —
        // e.g. behind a tunnel/cloud; else the request origin). Used for BOTH
        // the email link and the link returned to the plugin so they're identical.
        var baseUrl = Planscape.API.PublicUrl.Resolve(_config, Request);
        var linkWarning = InviteLink.UnstableBaseWarning(baseUrl);

        string? rawInviteToken = null;   // set when we mint/reissue a pending invite
        bool emailDispatched = false;    // true only when an invite email was actually attempted

        // Single active invite token per pending user. Stored as the SHA-256
        // hash in RefreshToken (RESET: prefix → reset-password.html sets the
        // password AND activates). Re-minting overwrites the prior hash, so the
        // previous link stops working — exactly one live token at a time.
        // Expiry defaults to 7 days; override via Auth:InviteTokenExpiryDays.
        int inviteExpiryDays = int.TryParse(_config["Auth:InviteTokenExpiryDays"], out var ied) && ied > 0 ? ied : 7;
        string MintInviteToken(AppUser u)
        {
            var raw = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
            u.RefreshToken = $"RESET:{hash}";
            u.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(inviteExpiryDays);
            return raw;
        }

        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.Email == req.Email.ToLowerInvariant() && u.TenantId == tenantId);

        if (user == null)
        {
            // User doesn't exist in this org — create a pending account
            var tenant = await _db.Tenants.FindAsync(tenantId);
            var userCount = await _db.Users.CountAsync(u => u.TenantId == tenantId && u.IsActive);
            if (tenant != null && userCount >= tenant.MaxUsers)
                return BadRequest($"User limit ({tenant.MaxUsers}) reached. Upgrade your plan to add more users.");

            user = new AppUser
            {
                TenantId      = tenantId,
                Email         = req.Email.Trim().ToLowerInvariant(),
                DisplayName   = req.DisplayName ?? req.Email.Split('@')[0],
                PasswordHash  = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString(), 12), // temp password
                Role          = UserRole.Contributor,
                Iso19650Role  = req.Iso19650Role ?? "M",
                IsActive      = false,  // awaiting first login / password set
            };
            // Mint the single active invite token. RESET: prefix (not INV:) so
            // the link validates against /api/auth/reset-password, which sets the
            // password AND activates the user — one complete onboarding step.
            // The DB only ever holds the SHA-256 hash; the raw value goes in the
            // email and is single-use (reset-password nulls it on consume).
            rawInviteToken = MintInviteToken(user);
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

            // Send invite email with the one-click deep link (token + email +
            // project). baseUrl uses Planscape:PublicBaseUrl when set, so the
            // link a remote guest receives is reachable — never internal localhost.
            await _emailService.SendInviteEmailAsync(
                user.Email, user.DisplayName, GetCurrentUserName(),
                project.Name, baseUrl, rawInviteToken, projectId);
            emailDispatched = _emailService.IsConfigured;
        }
        else if (!user.IsActive)
        {
            // Re-invite of an existing PENDING (never-activated) user. Reissue a
            // fresh token — invalidating the prior one — and resend, so the
            // re-invite isn't a silent no-op. Idempotent: each call replaces the
            // last token, so repeated re-invites keep working.
            if (string.IsNullOrWhiteSpace(user.DisplayName) && !string.IsNullOrWhiteSpace(req.DisplayName))
                user.DisplayName = req.DisplayName!;
            rawInviteToken = MintInviteToken(user);
            await _db.SaveChangesAsync();

            await _emailService.SendInviteEmailAsync(
                user.Email, user.DisplayName, GetCurrentUserName(),
                project.Name, baseUrl, rawInviteToken, projectId);
            emailDispatched = _emailService.IsConfigured;
            _logger.LogInformation("[invite] reissued token for {Email} on project {ProjectId}", user.Email, projectId);
        }

        // Add to project. Only block as a duplicate when the user is a fully
        // onboarded (active) member — a pending invitee whose member row already
        // exists must still be re-invitable (token reissued above).
        var existing = await _db.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == user.Id);
        if (existing != null && existing.IsActive && user.IsActive)
            return Conflict("User is already a member of this project");

        // Phase 177-D — apply named preset baseline; explicit fields override.
        var inviteProfile = await ResolveProfileAsync(req.AccessProfileId);
        if (existing != null)
        {
            existing.IsActive = true; existing.JoinedAt = DateTime.UtcNow;
            existing.AllowedCdeStates     = req.AllowedCdeStates     != null ? ToCsv(req.AllowedCdeStates)     : inviteProfile?.AllowedCdeStates;
            existing.AllowedDisciplines   = req.AllowedDisciplines   != null ? ToCsv(req.AllowedDisciplines)   : inviteProfile?.AllowedDisciplines;
            existing.AllowedSuitabilities = req.AllowedSuitabilities != null ? ToCsv(req.AllowedSuitabilities) : inviteProfile?.AllowedSuitabilities;
        }
        else
        {
            _db.ProjectMembers.Add(new ProjectMember
            {
                ProjectId    = projectId, UserId = user.Id,
                ProjectRole  = req.ProjectRole  ?? inviteProfile?.DefaultProjectRole  ?? "Contributor",
                Iso19650Role = req.Iso19650Role ?? inviteProfile?.DefaultIso19650Role ?? "M",
                InvitedBy    = GetCurrentUserName(),
                AllowedCdeStates     = req.AllowedCdeStates     != null ? ToCsv(req.AllowedCdeStates)     : inviteProfile?.AllowedCdeStates,
                AllowedDisciplines   = req.AllowedDisciplines   != null ? ToCsv(req.AllowedDisciplines)   : inviteProfile?.AllowedDisciplines,
                AllowedSuitabilities = req.AllowedSuitabilities != null ? ToCsv(req.AllowedSuitabilities) : inviteProfile?.AllowedSuitabilities,
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

        // Item 8 — report whether mail ACTUALLY went out (not merely whether SMTP
        // is configured) so the plugin shows "emailed" vs "copy the link" honestly.
        // Deep link — return the one-click accept URL so the plugin can show + log
        // it and copy it as the fallback when mail wasn't sent.
        bool emailSent = emailDispatched;
        string? inviteUrl = rawInviteToken != null
            ? InviteLink.BuildAcceptUrl(baseUrl, req.Email.Trim(), rawInviteToken, projectId)
            : null;

        _logger.LogInformation(
            "[invite] sent project={ProjectId} to={Email} emailSent={EmailSent} link={Link}{Warn}",
            projectId, req.Email, emailSent, inviteUrl ?? "(none)",
            linkWarning != null ? " WARNING(base-url): " + linkWarning : "");

        return Ok(new
        {
            message    = $"Invitation recorded for {req.Email}",
            userId     = user.Id,
            isPending  = !user.IsActive,
            emailSent,
            inviteLink = inviteUrl,
            linkWarning,
            note       = !user.IsActive
                ? (emailSent ? "An invitation email has been sent with instructions to set a password."
                             : "Email is not configured on the server — copy the invitation link to the invitee instead.")
                : null
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

        // Phase 177-D — apply preset first (if supplied), then let explicit
        // request fields override individual axes. AccessProfileId is treated
        // as a one-shot stamp; we don't store the profile id on the member
        // because later edits to the profile shouldn't retroactively change
        // existing member rows (audit hygiene).
        var profile = await ResolveProfileAsync(req.AccessProfileId);
        if (profile != null)
        {
            member.ProjectRole          = profile.DefaultProjectRole;
            member.Iso19650Role         = profile.DefaultIso19650Role;
            member.AllowedCdeStates     = profile.AllowedCdeStates;
            member.AllowedDisciplines   = profile.AllowedDisciplines;
            member.AllowedSuitabilities = profile.AllowedSuitabilities;
        }

        if (req.ProjectRole  != null) member.ProjectRole  = req.ProjectRole;
        if (req.Iso19650Role != null) member.Iso19650Role = req.Iso19650Role;
        // Phase 177 — pass null array to leave a column unchanged; pass an
        // empty array to clear it; pass a non-empty array to overwrite.
        var aclTouched =
            profile != null ||
            req.AllowedCdeStates     != null ||
            req.AllowedDisciplines   != null ||
            req.AllowedSuitabilities != null;
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

        // Phase 177 — re-shard the member's SignalR subscriptions to match the
        // new allow-list. Fire-and-forget: the response shouldn't block on
        // hub fan-out, and the broadcast is idempotent if it races with reconnect.
        if (aclTouched)
            _ = _membershipNotifier.NotifyAclChangedAsync(member.UserId, projectId);

        return Ok(new { member.Id, member.ProjectRole, member.Iso19650Role });
    }

    // ── Gap 2 — Edit per-member ACLs post-invite ──────────────────────────────
    //
    // The existing PUT /{memberId} updates role + ACLs together. This focused
    // endpoint lets a BIM manager edit ONLY the three allow-list axes without
    // touching the role, so the UI can present a separate "Access settings"
    // card without risking an accidental role change.

    [HttpPut("{memberId}/acl")]
    public async Task<ActionResult> UpdateAcl(Guid projectId, Guid memberId, [FromBody] UpdateAclRequest req)
    {
        if (!await IsManagerOrAboveAsync(projectId)) return Forbid();

        var member = await _db.ProjectMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.ProjectId == projectId && m.IsActive);
        if (member == null) return NotFound();

        // null = "leave this axis unchanged"; [] = "clear the axis (no restriction)"
        if (req.AllowedCdeStates     != null) member.AllowedCdeStates     = ToCsv(req.AllowedCdeStates);
        if (req.AllowedDisciplines   != null) member.AllowedDisciplines   = ToCsv(req.AllowedDisciplines);
        if (req.AllowedSuitabilities != null) member.AllowedSuitabilities = ToCsv(req.AllowedSuitabilities);

        var userId = Guid.TryParse(User.FindFirst("sub")?.Value, out var uid) ? uid : (Guid?)null;
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId  = GetTenantId(),
            ProjectId = projectId,
            UserId    = userId,
            Action    = "project_member_acl_updated",
            EntityType = "ProjectMember",
            EntityId  = memberId.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                allowedCdeStates     = member.AllowedCdeStates,
                allowedDisciplines   = member.AllowedDisciplines,
                allowedSuitabilities = member.AllowedSuitabilities,
            }),
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // Re-shard SignalR subscriptions to reflect the new allow-list.
        _ = _membershipNotifier.NotifyAclChangedAsync(member.UserId, projectId);

        return Ok(new
        {
            member.Id,
            member.AllowedCdeStates,
            member.AllowedDisciplines,
            member.AllowedSuitabilities
        });
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
        => (await AuthorizeManageAsync(projectId)).ok;

    /// <summary>
    /// Authorize a membership-management action (add / invite / update / remove).
    /// Authorization is by the caller's PROJECT role, NOT the global JWT
    /// <c>role</c> claim — that earlier check 403'd a project Manager/author
    /// whose tenant role happened to be Contributor, so they couldn't invite
    /// to their own project. A caller is allowed when they are:
    ///   • a tenant Admin / Owner / SecurityOfficer, OR
    ///   • the project author (<c>Project.CreatedById</c>), OR
    ///   • an active <c>ProjectMember</c> whose <c>ProjectRole</c> is
    ///     Owner / Admin / Manager.
    /// Returns the (denial) reason and the caller's effective role for logging.
    /// </summary>
    private async Task<(bool ok, string reason, string callerRole)> AuthorizeManageAsync(Guid projectId)
    {
        if (!await CanAccessProjectAsync(projectId))
            return (false, "caller cannot see this project", "(none)");

        if (ProjectVisibility.IsTenantAdmin(User))
            return (true, "tenant admin", User.FindFirst("role")?.Value ?? "Admin");

        var userId = ProjectVisibility.GetUserId(User);

        var project = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId);
        if (project?.CreatedById is Guid author && author == userId)
            return (true, "project author", "Author");

        var member = await _db.ProjectMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId && m.IsActive);
        if (member == null)
            return (false, "caller is not an active member of this project", "(none)");

        var pr = member.ProjectRole ?? "";
        if (pr is "Owner" or "Admin" or "Manager")
            return (true, $"project {pr}", pr);

        return (false, $"project role '{pr}' lacks membership-management permission (need Manager or above)", pr);
    }

    // Phase 177 — normalise inbound array → CSV; null/empty array means
    // "no narrowing for this axis" so it persists as null.
    private static string? ToCsv(string[]? arr)
    {
        if (arr == null) return null;
        var cleaned = arr.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
        return cleaned.Length == 0 ? null : string.Join(',', cleaned);
    }

    /// <summary>
    /// Phase 177-D — resolve an AccessProfile into a snapshot of the four
    /// fields (CSV allow-lists + default roles) so the caller can fold them
    /// into a ProjectMember row. Returns null if the profile id is null,
    /// missing, or belongs to a different tenant.
    /// </summary>
    private async Task<AccessProfile?> ResolveProfileAsync(Guid? profileId)
    {
        if (profileId is null || profileId == Guid.Empty) return null;
        var tenantId = GetTenantId();
        return await _db.AccessProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == profileId.Value && p.TenantId == tenantId && p.IsActive);
    }
}

// ── Request DTOs ───────────────────────────────────────────────────────────────
//
// Phase 177 — three optional ACL allow-lists are accepted as either CSV
// ("WIP,SHARED") or string[] from the mobile JSON serialiser. The
// controller normalises both to CSV before persisting.
//
// Phase 177-D — AccessProfileId optionally applies a tenant-scoped preset.
// When supplied, the preset's allow-lists + default roles are copied onto
// the member; any explicitly-provided fields on the request still win
// (so a PM can use a profile as the baseline and override one axis).

public record AddMemberRequest(
    Guid    UserId,
    string? ProjectRole,
    string? Iso19650Role,
    string[]? AllowedCdeStates     = null,
    string[]? AllowedDisciplines   = null,
    string[]? AllowedSuitabilities = null,
    Guid?   AccessProfileId        = null);

public record InviteByEmailRequest(
    string  Email,
    string? DisplayName,
    string? ProjectRole,
    string? Iso19650Role,
    string[]? AllowedCdeStates     = null,
    string[]? AllowedDisciplines   = null,
    string[]? AllowedSuitabilities = null,
    Guid?   AccessProfileId        = null);

public record UpdateMemberRequest(
    string? ProjectRole,
    string? Iso19650Role,
    string[]? AllowedCdeStates     = null,
    string[]? AllowedDisciplines   = null,
    string[]? AllowedSuitabilities = null,
    Guid?   AccessProfileId        = null);

/// <summary>
/// Gap 2 — focused ACL-only update. Null means "leave unchanged";
/// empty array means "remove restriction on this axis".
/// </summary>
public record UpdateAclRequest(
    string[]? AllowedCdeStates     = null,
    string[]? AllowedDisciplines   = null,
    string[]? AllowedSuitabilities = null);
