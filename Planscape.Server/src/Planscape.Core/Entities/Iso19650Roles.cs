namespace Planscape.Core.Entities;

/// <summary>
/// The canonical <see cref="ProjectMember.Iso19650Role"/> vocabulary.
///
/// WHY THIS EXISTS
/// ---------------
/// This list already existed — as an anonymous array inlined in
/// <c>ProjectMembersController.GetRoles()</c>, served to the web members grid and
/// the mobile picker. Being a response literal rather than a type, nothing could
/// validate against it, so every write path accepted any string at all:
///
///   ProjectMembersController:217  join/update existing member
///   ProjectMembersController:248  add member
///   ProjectMembersController:348  invite by email (new user)
///   ProjectMembersController:430  invite by email (existing user)
///   ProjectMembersController:524  edit member
///
/// The result is measurable. A local database holds two rows outside this
/// vocabulary: <c>'S'</c>, which is a value from the DIFFERENT list that
/// <see cref="AppUser.Iso19650Role"/> declares, and <c>'EL'</c>, which is in no
/// declared vocabulary at all. Neither could have been written by a first-party
/// UI, and neither was rejected.
///
/// The same shape produced the bug <see cref="ProjectRoles"/> documents: eleven
/// gates testing an ISO code against the ProjectRole column. An unvalidated
/// column drifts, and a drifted column makes every gate reading it a guess.
///
/// The deferral was explicit. <c>ProjectMembersController</c> carried:
///
///     // Iso19650Role is deliberately NOT validated in this pass — its
///     // vocabulary lives in GetRoles() below and constraining it is a
///     // separate, wider change.
///
/// This is that separate change.
///
/// TOLERANT BY DESIGN
/// ------------------
/// Validation rejects an explicitly-supplied value outside the list. It does NOT
/// reject a request that omits the field, so an existing row holding a stray stays
/// editable — someone fixing a member's ProjectRole must not be blocked by a value
/// they did not write and may not be able to interpret.
///
/// That tolerance is deliberate but must not become amnesia: the boot report in
/// Program.cs names the strays out loud, and the cleanup is tracked as its own
/// issue so a human who knows what those members were meant to be decides. Guessing
/// at a mapping table would be inventing data.
///
/// NOT THE SAME LIST AS AppUser.Iso19650Role
/// -----------------------------------------
/// <see cref="AppUser"/> declares its own, different vocabulary for a column of the
/// same name (A/M/E/S/H/P/C/I/K/Q/F/W/L/Z). Two vocabularies for one concept is how
/// 'S' arrived here. Reconciling them is tracked separately; this type governs the
/// ProjectMember column only, and says so rather than quietly covering both.
/// </summary>
public static class Iso19650Roles
{
    /// <summary>One ISO 19650 role code and the label the UI shows for it.</summary>
    public readonly record struct Role(string Code, string Label);

    /// <summary>Every legal ProjectMember.Iso19650Role, in the order the members
    /// grid and the mobile picker present them. This is the single source served by
    /// <c>GET api/projects/{id}/members/roles</c> — the endpoint no longer carries
    /// its own copy, so the list clients are offered and the list writes are checked
    /// against cannot drift apart.</summary>
    public static readonly IReadOnlyList<Role> Catalogue = new[]
    {
        new Role("A",  "Appointing Party"),
        new Role("PM", "Project Manager"),
        new Role("BC", "BIM Coordinator"),
        new Role("BA", "BIM Author"),
        new Role("AR", "Architect"),
        new Role("SE", "Structural Engineer"),
        new Role("ME", "MEP Engineer"),
        new Role("CE", "Civil Engineer"),
        new Role("QS", "Quantity Surveyor"),
        new Role("CA", "Contract Administrator"),
        new Role("CT", "Main Contractor"),
        new Role("SC", "Subcontractor"),
        new Role("FM", "Facilities Manager"),
        new Role("OM", "Operations Manager"),
        new Role("CL", "Client Representative"),
        new Role("M",  "Model Author"),
        new Role("V",  "Viewer"),
        new Role("Z",  "Unassigned"),
    };

    /// <summary>Just the codes — the shape a validation error reports back, matching
    /// the <c>allowed</c> field of <c>invalid_project_role</c>.</summary>
    public static readonly IReadOnlyList<string> All =
        Catalogue.Select(r => r.Code).ToArray();

    /// <summary>Case-insensitive membership test for write validation. Mirrors
    /// <see cref="ProjectRoles.IsCanonical"/> so the two columns are validated the
    /// same way.</summary>
    public static bool IsCanonical(string? role)
        => role != null && All.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
}
