namespace StingBIM.Core.Entities;

/// <summary>
/// Per-project team membership.
/// Allows fine-grained role assignment: a user can be a Viewer on Project A
/// and a Coordinator on Project B within the same tenant.
/// </summary>
public class ProjectMember
{
    public Guid Id          { get; set; } = Guid.NewGuid();
    public Guid ProjectId   { get; set; }
    public Guid UserId      { get; set; }

    /// <summary>Application role within this project (Viewer/Contributor/Coordinator/Manager).</summary>
    public string ProjectRole  { get; set; } = "Contributor";

    /// <summary>ISO 19650-2 role code for this project (A/PM/BC/AR/SE/ME/QS etc.).</summary>
    public string Iso19650Role { get; set; } = "M";

    public bool IsActive   { get; set; } = true;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public string? InvitedBy { get; set; }

    // Navigation
    public Project?  Project { get; set; }
    public AppUser?  User    { get; set; }
}
