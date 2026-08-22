namespace Planscape.Core.Entities;

/// <summary>
/// The canonical <see cref="ProjectMember.Iso19650Role"/> vocabulary.
///
/// WHY THIS EXISTS
/// ---------------
/// The column was free text. Every write site did
/// <c>req.Iso19650Role ?? … ?? "M"</c> with no validation, and that is how three
/// dead authorization gates happened: a gate compares against a value that
/// nothing prevents and nothing supplies.
///
/// The worst of them, <c>ProjectSettingsController.UpdateSettings</c>, required
/// this column to be "K" or "C". Neither is in the list below — they belong to
/// a DIFFERENT vocabulary, the one on <see cref="AppUser.Iso19650Role"/>
/// (AppUser.cs:14: A/M/E/S/H/P/C/I/K/Q/F/W/L/Z). Measured 2026-08-18: zero rows
/// carry K or C on either column, so nobody could edit project settings.
///
/// THE LIST BELOW IS THE ONE THE SERVER SERVES
/// -------------------------------------------
/// <c>GET api/projects/{id}/members/roles</c> renders directly from
/// <see cref="All"/> — it does not keep its own copy. That is the whole point:
/// a validator and an endpoint with separate literal lists is two sources of
/// truth and drifts back apart within a phase. If you add a role, add it here.
///
/// A MERGED LIST WAS NOT INVENTED. The AppUser vocabulary is a genuinely
/// different set for a different concept, and reconciling the two is an
/// architecture decision filed separately, not something to settle by quietly
/// unioning them here.
///
/// EXISTING ROWS MAY HOLD VALUES OUTSIDE THIS SET, AND THAT IS TOLERATED.
/// The local measurement found two ('S', leaked from the AppUser list, and
/// 'EL', which is in no vocabulary at all). Validation runs only on a value a
/// caller explicitly supplies, so an unrelated edit to such a member still
/// succeeds. They are reported at boot instead — see the [ISO] block in
/// Program.cs — and identified for a human to decide, rather than remapped by a
/// guess encoded in a migration.
/// </summary>
public static class Iso19650Roles
{
    /// <summary>Code plus the label the members dropdown shows.</summary>
    public readonly record struct Role(string Code, string Label);

    /// <summary>
    /// Every legal Iso19650Role, in the order the members dropdown presents
    /// them. <c>GetRoles()</c> serves this verbatim.
    /// </summary>
    public static readonly IReadOnlyList<Role> All = new[]
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

    /// <summary>Just the codes — the <c>allowed</c> array on a 400 body.</summary>
    public static readonly IReadOnlyList<string> AllCodes =
        All.Select(r => r.Code).ToList();

    /// <summary>
    /// Case-insensitive membership test for write validation.
    ///
    /// <c>null</c> is NOT canonical, but callers must not treat that as a
    /// rejection: every write site here means "leave it alone" by null, and
    /// only validates a value the caller actually supplied. Rejecting null
    /// would make an existing stray row unsaveable on an unrelated edit, which
    /// this change explicitly promises not to do.
    /// </summary>
    public static bool IsCanonical(string? code)
        => code != null && AllCodes.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase));
}
