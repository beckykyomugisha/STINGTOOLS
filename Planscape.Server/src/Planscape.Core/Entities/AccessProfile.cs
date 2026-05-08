namespace Planscape.Core.Entities;

/// <summary>
/// Phase 177-D — tenant-scoped named ACL preset for the per-folder
/// permission model on <see cref="ProjectMember"/>. Lets PMs invite
/// people with a single dropdown ("Trade subcontractor — M only",
/// "Reviewer", "Client read-only") rather than ticking three multi-
/// selects per person.
///
/// Profiles live at the tenant level so the same naming applies across
/// every project in the organisation. Applying a profile copies its
/// allow-list strings onto the new <see cref="ProjectMember"/> row;
/// later edits to the profile do NOT retroactively rewrite existing
/// members (that would be surprising audit-wise — the inviter chose a
/// snapshot at invite time).
/// </summary>
public class AccessProfile : ITenantScoped
{
    public Guid Id       { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Display name shown in the picker. Unique per tenant.</summary>
    public string Name { get; set; } = "";

    /// <summary>Optional one-line description shown under the name.</summary>
    public string? Description { get; set; }

    // CSV allow-lists, identical semantics to ProjectMember:
    //   null     = "all" (no narrowing on this axis)
    //   non-null = restrict to the listed codes
    public string? AllowedCdeStates     { get; set; }
    public string? AllowedDisciplines   { get; set; }
    public string? AllowedSuitabilities { get; set; }

    /// <summary>Default ProjectRole stamped on the member when applied.</summary>
    public string DefaultProjectRole { get; set; } = "Contributor";

    /// <summary>Default ISO 19650 role code stamped on the member when applied.</summary>
    public string DefaultIso19650Role { get; set; } = "M";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
}
