namespace Planscape.Infrastructure.Workflow;

/// <summary>
/// Phase 154 — single source of truth for the six canonical role
/// buckets used by deliverable state-machine keyword inference. Was
/// duplicated across <see cref="DeliverableStateMachine.KnownRoles"/>,
/// <see cref="DbTenantKeywordResolver"/>'s <c>ValidRoles</c>, and the
/// dashboard's JS validator. Phase 154 has both server callers and the
/// new <c>GET /api/state-machine/role-buckets</c> endpoint read from
/// here, and the dashboard JS fetches that endpoint on load instead
/// of hardcoding the list — so adding a seventh role bucket later
/// would be a single-file change.
///
/// "none" is intentionally excluded: it's a sentinel that flags a
/// state with no inferred role, not a bucket a tenant can author.
/// </summary>
public static class RoleBuckets
{
    /// <summary>The six canonical buckets, lowercase, in priority
    /// order (matches <c>RolePriority</c> on
    /// <see cref="DeliverableStateMachine"/>).</summary>
    public static readonly IReadOnlyList<string> Canonical = new[]
    {
        "rejecting", "accepting", "submitting", "terminal", "working", "initial",
    };

    /// <summary>Same set as <see cref="Canonical"/> but as a
    /// <c>HashSet</c> for O(1) lookup. Case-insensitive.</summary>
    public static readonly IReadOnlySet<string> Set = new HashSet<string>(
        Canonical, StringComparer.OrdinalIgnoreCase);

    /// <summary>Convenience: <see cref="Canonical"/> plus the "none"
    /// sentinel, used by the loader's role-block validator on
    /// <see cref="DeliverableStateMachine"/>.</summary>
    public static readonly IReadOnlySet<string> WithNone = new HashSet<string>(
        new[] { "rejecting", "accepting", "submitting", "terminal", "working", "initial", "none" },
        StringComparer.OrdinalIgnoreCase);
}
