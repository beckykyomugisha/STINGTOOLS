using Microsoft.EntityFrameworkCore;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.API.Services;

/// <summary>
/// Phase 179 — read-time gate that AND-s the existing audience state
/// machine with the new <see cref="PhotoAccessRule"/> rows. Returns the
/// subset of photo ids the calling user is actually allowed to see.
///
/// Composition:
///   1. tenant Admin / Owner / SecurityOfficer always pass.
///   2. ClientGuest passes only when ClientPortal AND every rule passes.
///   3. Project members pass when every rule passes.
///
/// A rule passes when ALL of its constraints pass:
///   * DistributionGroupId   — caller is a member of that group
///   * VisibleDisciplines    — caller's discipline is in the allow list
///                             (best-effort: takes the user's first
///                             ACL-resolvable discipline; null callers
///                             fail this constraint)
///   * MinRoleToView         — caller's role is &gt;= rank
///   * VisibleFrom / Until   — current UTC time inside the window
///
/// Rules are AND-ed (most-restrictive wins). Photos with zero rules
/// always pass — that's the pre-Phase-179 behaviour.
/// </summary>
public static class PhotoAclGate
{
    private static readonly Dictionary<string, int> _roleRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ClientGuest"] = 1,
        ["Coordinator"] = 2,
        ["PM"]          = 3,
        ["Admin"]       = 4,
        ["Owner"]       = 5,
        ["SecurityOfficer"] = 5,
    };

    public sealed record AclProbe(
        bool             BypassesAcl,
        string           Role,
        Guid?            UserId,
        string?          Email,
        HashSet<Guid>    MemberOfGroupIds,
        HashSet<string>  Disciplines,
        HashSet<Guid>    NdaAcceptedPhotoIds);

    /// <summary>Build the per-request probe: caller's role / user-id /
    /// group memberships / disciplines / NDA-accepted set.
    ///
    /// Phase 180 — when <paramref name="cacheCtx"/> is supplied, the
    /// resolved probe is cached on <c>HttpContext.Items</c> keyed by
    /// projectId so List → /file → annotations on the same request
    /// share one DB round-trip set.</summary>
    public static async Task<AclProbe> ResolveProbeAsync(
        PlanscapeDbContext db,
        Guid projectId,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct = default,
        Microsoft.AspNetCore.Http.HttpContext? cacheCtx = null)
    {
        var cacheKey = $"acl_probe:{projectId}";
        if (cacheCtx is not null && cacheCtx.Items.TryGetValue(cacheKey, out var cached) && cached is AclProbe hit)
            return hit;

        var role = user.FindFirst("role")?.Value ?? "";
        var bypass = role is "Admin" or "Owner" or "SecurityOfficer";

        var subClaim = user.FindFirst("user_id")?.Value
            ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
        Guid? userId = Guid.TryParse(subClaim, out var u) ? u : null;
        var email = user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                  ?? user.FindFirst("email")?.Value;

        // Phase 179.3 — group membership now resolves user-id OR
        // verified external-email so DistributionGroups built around
        // sub-contractor emails actually gate.
        HashSet<Guid> groups = new();
        if (userId.HasValue || !string.IsNullOrEmpty(email))
        {
            var emailLower = email?.ToLowerInvariant();
            groups = (await db.DistributionGroupMembers.AsNoTracking()
                .Where(m =>
                    (userId.HasValue && m.UserId == userId.Value) ||
                    (emailLower != null && m.ExternalEmail != null &&
                     m.ExternalEmail.ToLower() == emailLower))
                .Select(m => m.DistributionGroupId)
                .ToListAsync(ct))
                .ToHashSet();
        }

        // Phase 179.3 — full discipline set (was first-only).
        // Empty set means "caller has no discipline narrowing on this
        // project"; rules with VisibleDisciplines reject in that case
        // (strict default), set MinRoleToView=Admin if you want a
        // looser bypass.
        var disciplines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (userId.HasValue)
        {
            var member = await db.ProjectMembers.AsNoTracking()
                .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId.Value && m.IsActive, ct);
            if (!string.IsNullOrEmpty(member?.AllowedDisciplines))
            {
                var parsed = ProjectMember.ParseAllowList(member.AllowedDisciplines);
                if (parsed != null)
                    foreach (var d in parsed) disciplines.Add(d);
            }
        }

        // NDA acceptance set — only fetched for non-bypass callers since
        // bypass skips the rule check entirely.
        HashSet<Guid> ndaAccepted = new();
        if (!bypass && userId.HasValue)
        {
            ndaAccepted = (await db.PhotoNdaAcceptances.AsNoTracking()
                .Where(a => a.UserId == userId.Value)
                .Select(a => a.PhotoId)
                .ToListAsync(ct))
                .ToHashSet();
        }

        var probe = new AclProbe(bypass, role, userId, email, groups, disciplines, ndaAccepted);
        if (cacheCtx is not null) cacheCtx.Items[cacheKey] = probe;
        return probe;
    }

    /// <summary>
    /// Returns the subset of input photo ids the caller may see, AND-ing
    /// the audience state with the per-photo PhotoAccessRule set. When
    /// <paramref name="probe"/>.BypassesAcl, returns the full set.
    /// </summary>
    public static async Task<HashSet<Guid>> FilterVisibleAsync(
        PlanscapeDbContext db,
        IReadOnlyCollection<Guid> photoIds,
        AclProbe probe,
        CancellationToken ct = default)
    {
        if (photoIds.Count == 0) return new HashSet<Guid>();
        if (probe.BypassesAcl) return new HashSet<Guid>(photoIds);

        var rules = await db.PhotoAccessRules.AsNoTracking()
            .Where(r => photoIds.Contains(r.PhotoId))
            .ToListAsync(ct);
        if (rules.Count == 0) return new HashSet<Guid>(photoIds);

        var byPhoto = rules.GroupBy(r => r.PhotoId).ToDictionary(g => g.Key, g => g.ToList());
        var visible = new HashSet<Guid>();
        var now = DateTime.UtcNow;

        foreach (var pid in photoIds)
        {
            if (!byPhoto.TryGetValue(pid, out var ruleSet)) { visible.Add(pid); continue; }

            bool allowed = true;
            foreach (var rule in ruleSet)
            {
                if (!RulePasses(rule, pid, probe, now)) { allowed = false; break; }
            }
            if (allowed) visible.Add(pid);
        }
        return visible;
    }

    private static bool RulePasses(PhotoAccessRule rule, Guid photoId, AclProbe probe, DateTime nowUtc)
    {
        if (rule.VisibleFrom.HasValue && nowUtc < rule.VisibleFrom.Value) return false;
        if (rule.VisibleUntil.HasValue && nowUtc > rule.VisibleUntil.Value) return false;

        if (rule.DistributionGroupId.HasValue)
        {
            if (!probe.MemberOfGroupIds.Contains(rule.DistributionGroupId.Value)) return false;
        }

        if (!string.IsNullOrEmpty(rule.MinRoleToView))
        {
            var minRank = _roleRank.GetValueOrDefault(rule.MinRoleToView, 0);
            var actual  = _roleRank.GetValueOrDefault(probe.Role, 0);
            if (actual < minRank) return false;
        }

        if (!string.IsNullOrEmpty(rule.VisibleDisciplines))
        {
            if (probe.Disciplines.Count == 0) return false;
            var allowed = rule.VisibleDisciplines.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            // Phase 179.3 — pass when ANY of the user's disciplines is
            // in the rule's allow-list (was: first-only string match).
            if (!allowed.Any(d => probe.Disciplines.Contains(d))) return false;
        }

        // Phase 179.2 — NDA gate. When the rule requires acceptance,
        // the caller must have a row in PhotoNdaAcceptances for this
        // photo. The probe pre-fetched the user's accepted set so this
        // is a cheap HashSet lookup.
        if (rule.RequiresNdaAcceptance)
        {
            if (!probe.NdaAcceptedPhotoIds.Contains(photoId)) return false;
        }
        return true;
    }

    /// <summary>
    /// Returns the subset of input photo ids that REQUIRE NDA acceptance
    /// from the calling user (i.e. at least one rule has
    /// <c>RequiresNdaAcceptance = true</c> and the user has not yet
    /// accepted). Mobile / desktop UIs use this to surface the
    /// "Accept &amp; view" prompt before the fetch.
    /// </summary>
    public static async Task<HashSet<Guid>> NdaRequiredAsync(
        PlanscapeDbContext db,
        IReadOnlyCollection<Guid> photoIds,
        AclProbe probe,
        CancellationToken ct = default)
    {
        if (photoIds.Count == 0 || probe.BypassesAcl) return new HashSet<Guid>();
        var rules = await db.PhotoAccessRules.AsNoTracking()
            .Where(r => photoIds.Contains(r.PhotoId) && r.RequiresNdaAcceptance)
            .Select(r => r.PhotoId)
            .ToListAsync(ct);
        return rules.Where(id => !probe.NdaAcceptedPhotoIds.Contains(id)).ToHashSet();
    }
}
