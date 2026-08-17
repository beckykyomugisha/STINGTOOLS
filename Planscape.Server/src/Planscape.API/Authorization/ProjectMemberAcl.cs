using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Authorization;

/// <summary>
/// Phase 177 — per-folder/per-discipline/per-suitability ACL lookup for the
/// authenticated user against a project. Sits beside <see cref="ProjectAccessAttribute"/>:
/// that gate decides "can this user see the project at all"; this helper
/// then narrows what slice they see *inside* the project.
///
/// Nulls preserve old behaviour: a member without any populated allow-list
/// gets the whole project (matching the pre-Phase 177 semantics).
/// Tenant Admins / Owners / SecurityOfficers bypass member-row narrowing
/// entirely so they retain their cross-project audit reach.
/// </summary>
public static class ProjectMemberAcl
{
    public sealed record AclSlice(
        string[]? Cde,
        string[]? Disciplines,
        string[]? Suitabilities,
        bool BypassesAcl)
    {
        public bool AllowsCde(string? state) =>
            BypassesAcl || ProjectMember.IsAllowed(JoinOrNull(Cde), state);

        public bool AllowsDiscipline(string? disc) =>
            BypassesAcl || ProjectMember.IsAllowed(JoinOrNull(Disciplines), disc);

        public bool AllowsSuitability(string? suit) =>
            BypassesAcl || ProjectMember.IsAllowed(JoinOrNull(Suitabilities), suit);

        public bool AllowsDocument(DocumentRecord d) =>
            AllowsCde(d.CdeStatus) && AllowsDiscipline(d.Discipline) && AllowsSuitability(d.SuitabilityCode);

        private static string? JoinOrNull(string[]? arr) =>
            arr == null || arr.Length == 0 ? null : string.Join(',', arr);
    }

    /// <summary>
    /// Read the ACL slice for the calling user. Returns a "bypass" slice
    /// when the user is a tenant Admin/Owner/SecurityOfficer or when no
    /// ProjectMember row exists (e.g. project author falls through
    /// ProjectVisibility but isn't yet on the member table).
    /// </summary>
    public static async Task<AclSlice> ResolveAsync(
        PlanscapeDbContext db,
        Guid projectId,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var role = user.FindFirst("role")?.Value ?? "";
        if (role is "Admin" or "Owner" or "SecurityOfficer")
            return new AclSlice(null, null, null, BypassesAcl: true);

        var subClaim = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
        if (!Guid.TryParse(subClaim, out var userId))
            return new AclSlice(null, null, null, BypassesAcl: true); // can't narrow what we can't identify

        // #631 — read the three allow-list columns for real.
        //
        // These were hard-coded to null between cb503b024 (2026-05-16) and this
        // commit, which meant ApplyTo could never narrow a query and the Phase
        // 177 ACL restricted nothing for ~3 months. The stub was a deliberate
        // crash workaround: a full entity fetch expands to SELECT *, which
        // failed with "column p.AllowedCdeStates does not exist" on a database
        // where migration 20260508000000_AddProjectMemberAcls had not been
        // applied. Its comment said "until the migration is run".
        //
        // That reasoning is obsolete and must not be restored. This codebase
        // does not run migrations: schema comes from OnModelCreating via
        // EnsureCreated plus the idempotent patchers, and render.yaml sets
        // PLANSCAPE_USE_ENSURE_CREATED=true on both the api and worker services
        // (docs/adr/0001-schema-management.md). The three columns therefore
        // exist wherever the model does — verified present on a live database.
        //
        // The explicit projection is kept, minus the null literals: it selects
        // exactly the four columns this method needs, so the query cannot be
        // widened by an unrelated entity change and cannot fetch a column that
        // does not exist.
        var memberRow = await db.ProjectMembers
            .AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.UserId == userId && m.IsActive)
            .Select(m => new
            {
                m.Id,
                m.AllowedCdeStates,
                m.AllowedDisciplines,
                m.AllowedSuitabilities,
            })
            .FirstOrDefaultAsync(ct);

        // No member row = inherit (project author / tenant manager fallthrough).
        // Don't 403 here — ProjectAccessAttribute already cleared visibility.
        if (memberRow == null)
            return new AclSlice(null, null, null, BypassesAcl: true);

        return new AclSlice(
            Cde:           ProjectMember.ParseAllowList(memberRow.AllowedCdeStates),
            Disciplines:   ProjectMember.ParseAllowList(memberRow.AllowedDisciplines),
            Suitabilities: ProjectMember.ParseAllowList(memberRow.AllowedSuitabilities),
            BypassesAcl:   false);
    }

    /// <summary>
    /// Apply the ACL to a document IQueryable. No-op when the slice bypasses
    /// or every axis is null. Filtering happens in SQL via three IN clauses.
    /// </summary>
    public static IQueryable<DocumentRecord> ApplyTo(IQueryable<DocumentRecord> query, AclSlice acl)
    {
        if (acl.BypassesAcl) return query;
        if (acl.Cde is { Length: > 0 } cde)
            query = query.Where(d => cde.Contains(d.CdeStatus));
        if (acl.Disciplines is { Length: > 0 } disc)
            query = query.Where(d => d.Discipline != null && disc.Contains(d.Discipline));
        if (acl.Suitabilities is { Length: > 0 } suit)
            query = query.Where(d => suit.Contains(d.SuitabilityCode));
        return query;
    }
}
