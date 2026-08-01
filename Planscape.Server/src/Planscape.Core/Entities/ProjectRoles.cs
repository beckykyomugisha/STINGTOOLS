using System.Linq.Expressions;

namespace Planscape.Core.Entities;

/// <summary>
/// The canonical <see cref="ProjectMember.ProjectRole"/> vocabulary, and the
/// capability predicates derived from it.
///
/// WHY THIS EXISTS
/// ---------------
/// Eleven call sites gated on <c>ProjectRole == "PM"</c>. But "PM" is not a
/// ProjectRole at all — the two fields carry different vocabularies, and each
/// says so on itself:
///
///   ProjectMember.cs:15  ProjectRole   "Viewer/Contributor/Coordinator/Manager"
///   ProjectMember.cs:18  Iso19650Role  "A/PM/BC/AR/SE/ME/QS etc."
///
/// "PM" only ever appears in the ISO 19650 list — which is also what
/// <c>GET api/projects/{id}/members/roles</c> returns, and what the web grid
/// saves into <c>iso19650Role</c>. Those eleven sites were reading the right
/// code off the wrong column, so the gates matched (essentially) nobody.
///
/// This is a wrong-field bug, not a data migration.
///
/// TWO SHAPES, DELIBERATELY
/// ------------------------
/// Controllers need a claims-aware async check (the tenant `role` claim can
/// grant access without any ProjectMember row). Background jobs need something
/// EF can translate INSIDE a <c>.Where(...)</c>. A method the jobs called
/// per-row would silently client-evaluate the whole table, so the job-facing
/// shape is an <see cref="Expression"/> and stays one.
///
/// Keep the two in step: <see cref="CanCurateProject"/> is the expression form
/// of <see cref="CanCurate"/>, and likewise for the approval pair. The tests
/// assert they agree.
/// </summary>
public static class ProjectRoles
{
    // ── Canonical ProjectRole vocabulary ────────────────────────────────────
    // Viewer..Admin mirror the web members dropdown
    // (planscape-web/app/projects/[id]/members/page.tsx:23). ClientGuest is
    // included because DailyPhotoDigestJob.cs:126 already reads it, so it is
    // a real value in the system whether or not the UI offers it.

    public const string Viewer      = "Viewer";
    public const string Contributor = "Contributor";
    public const string Coordinator = "Coordinator";
    public const string Manager     = "Manager";
    public const string Owner       = "Owner";
    public const string Admin       = "Admin";
    public const string ClientGuest = "ClientGuest";

    /// <summary>Every legal ProjectRole. Order matches the web dropdown, with
    /// ClientGuest appended.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Viewer, Contributor, Coordinator, Manager, Owner, Admin, ClientGuest,
    };

    /// <summary>Case-insensitive membership test for write validation.</summary>
    public static bool IsCanonical(string? role)
        => role != null && All.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));

    // ── ISO 19650 role codes that carry project authority ───────────────────
    // A  = Appointing Party, PM = Project Manager, BC = BIM Coordinator.
    // These come from the Iso19650Role column — the one that actually holds
    // "PM" — which is the whole point of this class.

    private const string IsoAppointingParty = "A";
    private const string IsoProjectManager  = "PM";
    private const string IsoBimCoordinator  = "BC";

    // ── LEGACY TOLERANCE — read this before "tidying" it away ───────────────
    //
    // The gates being replaced tested `ProjectRole == "PM"`. "PM" is not a
    // canonical ProjectRole and no first-party UI writes it there — but
    // ProjectMembersController:418 accepted ANY string with no validation
    // until this change, so a row with ProjectRole = "PM" is possible and any
    // such row passes the CURRENT gate.
    //
    // Dropping it would NARROW access for those rows, which is exactly what
    // this change promises not to do. So "PM" is accepted as a ProjectRole
    // here for back-compat, while `All` deliberately excludes it so new writes
    // are rejected. Remove only after confirming no rows carry it.
    private const string LegacyProjectRolePm = "PM";

    // ── Capability: CurateProject ───────────────────────────────────────────
    // Albums, checklists, distribution groups, deleting another member's saved
    // view. Broader than photo approval: curation is organising, not signing off.

    /// <summary>In-memory form. <paramref name="isTenantAdmin"/> covers the
    /// tenant `role` claim being Admin or Owner, which grants regardless of
    /// any ProjectMember row.</summary>
    public static bool CanCurate(string? projectRole, string? iso19650Role, bool isTenantAdmin = false)
        => isTenantAdmin
        || Eq(projectRole, Manager) || Eq(projectRole, Owner) || Eq(projectRole, Admin)
        || Eq(projectRole, Coordinator) || Eq(projectRole, LegacyProjectRolePm)
        || Eq(iso19650Role, IsoProjectManager) || Eq(iso19650Role, IsoAppointingParty)
        || Eq(iso19650Role, IsoBimCoordinator);

    /// <summary>EF-translatable form for use inside <c>.Where(...)</c>.
    /// Deliberately written with plain <c>==</c> and <c>||</c> so it becomes a
    /// SQL <c>OR</c> chain rather than client evaluation.</summary>
    public static Expression<Func<ProjectMember, bool>> CanCurateProject =>
        m => m.ProjectRole == Manager
          || m.ProjectRole == Owner
          || m.ProjectRole == Admin
          || m.ProjectRole == Coordinator
          || m.ProjectRole == LegacyProjectRolePm
          || m.Iso19650Role == IsoProjectManager
          || m.Iso19650Role == IsoAppointingParty
          || m.Iso19650Role == IsoBimCoordinator;

    // ── Capability: ApproveSitePhotos ───────────────────────────────────────
    // Photo approve/reject, include-originals, share-link issuance, PUT
    // photo-policy. Narrower: these decisions release imagery outside the
    // project, so a Coordinator curating albums does not get them.

    public static bool CanApproveSitePhotos(string? projectRole, string? iso19650Role, bool isTenantAdmin = false)
        => isTenantAdmin
        || Eq(projectRole, Manager) || Eq(projectRole, Owner) || Eq(projectRole, Admin)
        || Eq(projectRole, LegacyProjectRolePm)
        || Eq(iso19650Role, IsoProjectManager) || Eq(iso19650Role, IsoAppointingParty);

    public static Expression<Func<ProjectMember, bool>> CanApproveSitePhotosPredicate =>
        m => m.ProjectRole == Manager
          || m.ProjectRole == Owner
          || m.ProjectRole == Admin
          || m.ProjectRole == LegacyProjectRolePm
          || m.Iso19650Role == IsoProjectManager
          || m.Iso19650Role == IsoAppointingParty;

    private static bool Eq(string? a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
