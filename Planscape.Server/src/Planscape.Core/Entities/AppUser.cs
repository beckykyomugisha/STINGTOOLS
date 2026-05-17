namespace Planscape.Core.Entities;

/// <summary>
/// Application user with multi-tenant membership and ISO 19650 role.
/// </summary>
public class AppUser : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Viewer;
    public string Iso19650Role { get; set; } = "Z"; // ISO 19650 role code (A/M/E/S/H/P/C/I/K/Q/F/W/L/Z)
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    // F2 — soft-delete support. Use IsDeleted/DeletedAt instead of hard-deleting
    // users so audit logs, issue assignments and project memberships remain intact.
    // A global query filter in PlanscapeDbContext excludes deleted users from all
    // normal queries. DeletedByUserId is the admin who performed the deletion.
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
}

public enum UserRole
{
    Viewer = 0,
    Contributor = 1,
    Coordinator = 2,
    Manager = 3,
    Admin = 4,
    Owner = 5,

    // Phase 158 — separation-of-duties role for SOC2 / ISO 27001
    // audits. A SecurityOfficer can revoke user sessions
    // (token-floor bumps + audit trail) but is NOT an Admin/Owner;
    // they can't edit projects, members, or BIM-Manager roles.
    // Authorisation policy <c>SecurityOfficerOrAdmin</c> grants
    // SecurityOfficer + Admin + Owner; Admin / Owner short-circuits
    // so existing operators are unaffected.
    SecurityOfficer = 6,
}
