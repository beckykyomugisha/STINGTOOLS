using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Controllers;

/// <summary>
/// S4.3 — onboarding wizard surface. After signup, the mobile / web app
/// walks the new tenant through 4 steps (project, team, plugin, mobile).
/// Server tracks completion so the next-load resumes where they stopped
/// and the founder revenue dashboard (S2.7) can show 'X of Y new tenants
/// finished onboarding this month'.
///
/// Steps:
///   1. CreateProject     — first project (name, code, phase)
///   2. InviteTeam        — first 1-3 coordinators / authors
///   3. ConnectPlugin     — generates the Revit plugin license key + endpoint URL
///   4. InstallMobile     — opt-in QR code for the mobile app
///
/// Each step is idempotent — calling it twice with the same payload
/// is a no-op. The wizard reads /onboarding/state to know what's done.
/// </summary>
[ApiController]
[Route("api/onboarding")]
[Authorize(Roles = "Owner,Admin")]
public class OnboardingController : ControllerBase
{
    private readonly PlanscapeDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IQuotaGuardService _quota;

    public OnboardingController(PlanscapeDbContext db, ITenantContext tenant, IQuotaGuardService quota)
    {
        _db = db; _tenant = tenant; _quota = quota;
    }

    /// <summary>Returns the wizard's progress so the UI can resume mid-flow.</summary>
    [HttpGet("state")]
    public async Task<ActionResult> State(CancellationToken ct)
    {
        var hasProject = await _db.Projects.AnyAsync(ct);
        var memberCount = await _db.ProjectMembers.CountAsync(ct);
        var licenseCount = await _db.LicenseKeys.CountAsync(ct);
        return Ok(new
        {
            steps = new[]
            {
                new { id = "createProject", label = "Create your first project",        done = hasProject },
                new { id = "inviteTeam",    label = "Invite your team",                  done = memberCount >= 1 },
                new { id = "connectPlugin", label = "Connect Revit (1 author seat)",     done = licenseCount >= 1 },
                new { id = "installMobile", label = "Install the mobile app",            done = await _db.DevicePushTokens.AnyAsync(ct) },
            },
            percentComplete = ComputePercent(hasProject, memberCount, licenseCount,
                                              await _db.DevicePushTokens.AnyAsync(ct)),
        });
    }

    [HttpPost("project")]
    public async Task<ActionResult> CreateProject([FromBody] OnboardingProjectRequest req, CancellationToken ct)
    {
        var quota = await _quota.CheckCanAddProjectAsync(ct);
        if (!quota.Allowed) return StatusCode(StatusCodes.Status402PaymentRequired, new { error = "quota_exceeded", quota });

        // Idempotent — a project with the same Code is fine; just return it.
        var existing = await _db.Projects.FirstOrDefaultAsync(p => p.Code == req.Code, ct);
        if (existing != null) return Ok(new { existing.Id, existing.Code, existing.Name });

        var project = new Project
        {
            TenantId = _tenant.TenantId,
            Code = req.Code,
            Name = req.Name,
            Phase = req.Phase ?? "Stage 2 — Concept Design",
            CreatedAt = DateTime.UtcNow,
        };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);
        return Ok(new { project.Id, project.Code, project.Name });
    }

    [HttpPost("team")]
    public async Task<ActionResult> InviteTeam([FromBody] OnboardingTeamRequest req, CancellationToken ct)
    {
        var added = new List<object>();
        foreach (var m in req.Members)
        {
            // Quota check per member — fail mid-list cleanly with a 402.
            var role = string.Equals(m.Role, "Author", StringComparison.OrdinalIgnoreCase) ? "Author" : "Coordinator";
            var q = await _quota.CheckCanAddUserAsync(role, ct);
            if (!q.Allowed) return StatusCode(StatusCodes.Status402PaymentRequired, new { error = "quota_exceeded", added, denied = m, quota = q });

            var existing = await _db.Users.FirstOrDefaultAsync(u => u.Email == m.Email.ToLowerInvariant(), ct);
            if (existing != null) { added.Add(new { existing.Id, existing.Email, role, status = "exists" }); continue; }

            var user = new AppUser
            {
                TenantId = _tenant.TenantId,
                Email = m.Email.Trim().ToLowerInvariant(),
                DisplayName = m.DisplayName,
                PasswordHash = "INVITED",
                Role = role == "Author" ? UserRole.Contributor : UserRole.Coordinator,
                Iso19650Role = role == "Author" ? "A" : "C",
                IsActive = false,
            };
            _db.Users.Add(user);
            added.Add(new { user.Id, user.Email, role, status = "invited" });
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { invited = added });
    }

    [HttpPost("plugin")]
    public async Task<ActionResult> ConnectPlugin(CancellationToken ct)
    {
        var existing = await _db.LicenseKeys.FirstOrDefaultAsync(k => k.IsActive, ct);
        if (existing != null) return Ok(new { existing.Key, endpoint = "https://api.planscape.app" });

        var licence = new LicenseKey
        {
            TenantId = _tenant.TenantId,
            Key = $"PLNS-{Guid.NewGuid():N}".ToUpper()[..32],
            Tier = LicenseTier.Premium,
            MaxActivations = 3,
            ExpiresAt = DateTime.UtcNow.AddYears(1),
            IsActive = true,
        };
        _db.LicenseKeys.Add(licence);
        await _db.SaveChangesAsync(ct);
        return Ok(new { licence.Key, endpoint = "https://api.planscape.app" });
    }

    /// <summary>QR payload for the mobile app's deep-link signup.</summary>
    [HttpGet("mobile-qr")]
    public ActionResult MobileQr()
    {
        // Returns a URL the mobile app's QR scanner recognises and routes to /(tabs)
        // already authenticated as the current user. Encoded server-side so we
        // can rotate the format later without changing the client.
        var deepLink = $"planscape://onboard?tenant={_tenant.TenantSlug}";
        return Ok(new
        {
            deepLink,
            installLinks = new
            {
                ios     = "https://apps.apple.com/app/planscape",
                android = "https://play.google.com/store/apps/details?id=app.planscape",
            },
        });
    }

    private static int ComputePercent(bool a, int memberCount, int licenseCount, bool d)
    {
        var done = (a ? 1 : 0) + (memberCount >= 1 ? 1 : 0) + (licenseCount >= 1 ? 1 : 0) + (d ? 1 : 0);
        return done * 25;
    }
}

public record OnboardingProjectRequest(string Code, string Name, string? Phase);
public record OnboardingTeamRequest(List<OnboardingTeamMember> Members);
public record OnboardingTeamMember(string Email, string DisplayName, string Role);
