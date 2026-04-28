namespace Planscape.Infrastructure.Authorization;

/// <summary>
/// Phase 155 — caches the parsed
/// <see cref="Planscape.Core.Entities.Tenant.BimManagerIso19650RolesJson"/>
/// override so authorisation requests don't re-parse JSON per call.
/// Mirrors the
/// <see cref="Planscape.Infrastructure.Workflow.ITenantKeywordResolver"/>
/// shape:
///   • Read-through against a static striped LRU keyed on
///     <c>(TenantId, FNV-1a content hash)</c>; hash flips when an
///     admin updates the JSON, so stale rows self-invalidate.
///   • Forgiving parse — any failure returns null so the caller falls
///     back to the deployment-global appsettings list.
///
/// Returns null (not empty list) when the tenant has no override
/// configured. Empty list means "tenant deliberately authored an
/// empty override" which should still fall back to deployment.
/// </summary>
public interface ITenantBimManagerRoleResolver
{
    /// <summary>
    /// Resolve the parsed override list for the given tenant. Returns
    /// null when the tenant has no override OR the override is
    /// malformed / empty (caller falls back to deployment defaults).
    /// </summary>
    Task<IReadOnlyList<string>?> ResolveAsync(Guid tenantId, CancellationToken ct = default);
}
