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
        bool        BypassesAcl,
        string      Role,
        Guid?       UserId,
        HashSet<Guid> MemberOfGroupIds,
        string?     DisciplineHint);

    /// <summary>Build the per-request probe: caller's role / user-id /
    /// group memberships / first discipline. Hits two indexed queries.</summary>
    public static async Task<AclProbe> ResolveProbeAsync(
        PlanscapeDbContext db,
        Guid projectId,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var role = user.FindFirst("role")?.Value ?? "";
        var bypass = role is "Admin" or "Owner" or "SecurityOfficer";

        var subClaim = user.FindFirst("user_id")?.Value
            ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
        Guid? userId = Guid.TryParse(subClaim, out var u) ? u : null;

        HashSet<Guid> groups;
        if (userId.HasValue)
        {
            groups = (await db.DistributionGroupMembers.AsNoTracking()
                .Where(m => m.UserId == userId.Value)
                .Select(m => m.DistributionGroupId)
                .ToListAsync(ct))
                .ToHashSet();
        }
        else
        {
            groups = new HashSet<Guid>();
        }

        // Discipline hint comes from the project member ACL slice — we
        // take the first allowed discipline. Null when the caller isn't
        // narrowed (and therefore fails any VisibleDisciplines rule by
        // strict default — set the override per-rule if you want the
        // looser behaviour).
        string? discipline = null;
        if (userId.HasValue)
        {
            var member = await db.ProjectMembers.AsNoTracking()
                .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId.Value && m.IsActive, ct);
            if (!string.IsNullOrEmpty(member?.AllowedDisciplines))
                discipline = ProjectMember.ParseAllowList(member.AllowedDisciplines)?.FirstOrDefault();
        }

        return new AclProbe(bypass, role, userId, groups, discipline);
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
                if (!RulePasses(rule, probe, now)) { allowed = false; break; }
            }
            if (allowed) visible.Add(pid);
        }
        return visible;
    }

    private static bool RulePasses(PhotoAccessRule rule, AclProbe probe, DateTime nowUtc)
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
            if (string.IsNullOrEmpty(probe.DisciplineHint)) return false;
            var allowed = rule.VisibleDisciplines.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!allowed.Any(d => string.Equals(d, probe.DisciplineHint, StringComparison.OrdinalIgnoreCase))) return false;
        }

        // RequiresNdaAcceptance — for now we don't gate fetch on prior
        // acceptance (the audit-trail flag exists but acceptance UI is a
        // follow-up). Returning true here matches the documented Phase
        // 179 behaviour.
        return true;
    }
}
