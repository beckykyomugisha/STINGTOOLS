namespace Planscape.Core.Entities;

/// <summary>
/// Per-project team membership.
/// Allows fine-grained role assignment: a user can be a Viewer on Project A
/// and a Coordinator on Project B within the same tenant.
/// </summary>
public class ProjectMember : ITenantScoped
{
    public Guid Id          { get; set; } = Guid.NewGuid();
    public Guid TenantId    { get; set; }
    public Guid ProjectId   { get; set; }
    public Guid UserId      { get; set; }

    /// <summary>Application role within this project (Viewer/Contributor/Coordinator/Manager).</summary>
    public string ProjectRole  { get; set; } = "Contributor";

    /// <summary>ISO 19650-2 role code for this project (A/PM/BC/AR/SE/ME/QS etc.).</summary>
    public string Iso19650Role { get; set; } = "M";

    // ── Per-folder ACLs (Phase 177 — document manager review) ───────────────
    //
    // Three orthogonal axes that narrow what this member can see and act on
    // *within* the project. All three default to null which means "inherit
    // role-tier defaults" so existing rows behave exactly as before. A
    // populated CSV string restricts the member to that subset.
    //
    // - AllowedCdeStates    "WIP,SHARED,PUBLISHED,ARCHIVE"  (or null = all)
    // - AllowedDisciplines  "M,E,P"                          (or null = all)
    // - AllowedSuitabilities "S2,S3,S4"                      (or null = all)
    //
    // Stored as comma-separated text rather than a join table so the JWT
    // /me payload stays compact and DocumentsController.GetAll can apply the
    // filter inline without a second round-trip.

    /// <summary>Comma-separated CDE states this member may see; null = all.</summary>
    public string? AllowedCdeStates { get; set; }

    /// <summary>Comma-separated discipline codes this member may see; null = all.</summary>
    public string? AllowedDisciplines { get; set; }

    /// <summary>Comma-separated suitability codes this member may see; null = all.</summary>
    public string? AllowedSuitabilities { get; set; }

    public bool IsActive   { get; set; } = true;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public string? InvitedBy { get; set; }

    // Navigation
    public Project?  Project { get; set; }
    public AppUser?  User    { get; set; }

    // ── Helpers (parse + check ACL membership) ──────────────────────────────

    public static string[]? ParseAllowList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : parts;
    }

    /// <summary>
    /// Returns true if the supplied value is contained in the allow-list, OR
    /// the allow-list is null/empty (which means "all"). Case-insensitive.
    /// </summary>
    public static bool IsAllowed(string? csv, string? value)
    {
        var parts = ParseAllowList(csv);
        if (parts == null) return true;                   // null = "all"
        if (string.IsNullOrEmpty(value)) return false;
        foreach (var p in parts)
            if (string.Equals(p, value, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
