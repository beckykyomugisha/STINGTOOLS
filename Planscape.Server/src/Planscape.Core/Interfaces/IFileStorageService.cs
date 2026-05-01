namespace Planscape.Core.Interfaces;

/// <summary>
/// Abstracts file storage operations so controllers never touch the filesystem directly.
/// Swap LocalFileStorageService for MinIO / Azure Blob in production.
///
/// S1.2 — paths are tenant-scoped at the storage layer. Save methods produce
/// paths prefixed with <c>t_{tenantId}/{projectId}/...</c>; read/delete
/// methods reject paths whose tenant prefix doesn't match the resolved
/// current tenant from <c>ITenantContext</c>. Background jobs that need
/// cross-tenant access pass <c>bypassTenantCheck: true</c> explicitly.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Persist a file under <c>t_{tenantId}/{projectId}/...</c> and return the
    /// relative storage path. Preferred over the slug-based overload because
    /// tenant ids never change while slugs can be edited.
    /// </summary>
    Task<string> SaveScopedAsync(Guid tenantId, Guid projectId, string fileName, Stream content, CancellationToken ct = default);

    /// <summary>
    /// Legacy slug-based overload. New code should call <see cref="SaveScopedAsync"/>.
    /// Kept for backwards compatibility with existing call sites in the
    /// derivative pipeline ("derivatives" / "thumbnails" buckets).
    /// </summary>
    Task<string> SaveAsync(string tenantSlug, string projectCode, string fileName, Stream content, CancellationToken ct = default);

    /// <summary>
    /// Retrieve a readable stream for the given storage path, or null if not
    /// found. Throws <see cref="UnauthorizedAccessException"/> when the path's
    /// tenant prefix doesn't match the current tenant context (set
    /// <paramref name="bypassTenantCheck"/> for legitimate cross-tenant
    /// background work).
    /// </summary>
    Task<Stream?> GetAsync(string path, CancellationToken ct = default, bool bypassTenantCheck = false);

    /// <summary>
    /// Delete the file at the given storage path. Returns true if deleted.
    /// Same tenant-prefix enforcement as <see cref="GetAsync"/>.
    /// </summary>
    Task<bool> DeleteAsync(string path, CancellationToken ct = default, bool bypassTenantCheck = false);

    /// <summary>
    /// Check whether a file exists at the given storage path. Same tenant-prefix
    /// enforcement as <see cref="GetAsync"/>.
    /// </summary>
    Task<bool> ExistsAsync(string path, CancellationToken ct = default, bool bypassTenantCheck = false);
}
