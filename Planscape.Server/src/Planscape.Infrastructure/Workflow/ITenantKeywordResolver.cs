namespace Planscape.Infrastructure.Workflow;

/// <summary>
/// Phase 151 — resolves the tenant-scoped keyword extensions for a
/// given <see cref="Planscape.Core.Entities.Tenant"/>. Sits between the
/// platform-wide <see cref="IPlatformKeywordRegistry"/> and the
/// per-project JSON in priority order.
///
/// Returns an empty dictionary when the tenant has no
/// <c>KeywordExtensionsJson</c> set; the loader treats empty layers as
/// no-ops so the merge is well-defined even on a fresh tenant.
///
/// Implementations are expected to cache parsed JSON (parsing every
/// state-machine load would be wasteful since the JSON only changes
/// when an admin updates it). The provided
/// <see cref="DbTenantKeywordResolver"/> uses a small per-tenant
/// LRU keyed on <c>(TenantId, JSON-content-hash)</c> so a stale cache
/// entry self-invalidates when an admin edits the JSON.
/// </summary>
public interface ITenantKeywordResolver
{
    /// <summary>
    /// Returns the parsed keyword map for the tenant. Empty dictionary
    /// when the tenant has no extensions configured. Never returns null.
    /// Cancellation token is honoured for the underlying DB read.
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> ResolveAsync(
        Guid tenantId, CancellationToken ct = default);
}
