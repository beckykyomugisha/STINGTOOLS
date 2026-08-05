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

    /// <summary>
    /// S7.4.2 — recursively remove every object whose key starts with
    /// <paramref name="prefix"/>. Used by <c>DataErasureJob</c> to wipe
    /// <c>t_{tenantId}/</c> after the cooling-off period and by future
    /// janitor passes that clean orphan files. Returns the count of
    /// objects deleted (best-effort — providers that can't enumerate
    /// safely return 0 and the caller logs an orphan warning).
    /// </summary>
    Task<int> DeleteByPrefixAsync(string prefix, CancellationToken ct = default, bool bypassTenantCheck = false);

    /// <summary>
    /// Phase 175 audit P1-14 — generate a presigned PUT URL the client
    /// uploads to directly, bypassing the API process. The returned key
    /// goes under <c>uploads/raw/t_{tenantId}/...</c>; a Hangfire
    /// scanner moves the file to <c>safe/...</c> after virus scan.
    /// Throws <see cref="NotSupportedException"/> on backends that
    /// can't presign (e.g. local filesystem in dev) — caller should
    /// fall back to the multipart upload endpoint in that case.
    /// </summary>
    Task<PresignedUpload> GetPresignedPutUrlAsync(
        string objectKey, string contentType, TimeSpan validFor, long maxBytes, CancellationToken ct = default);

    /// <summary>
    /// Move (server-side copy + delete) an object from one key to
    /// another inside the same bucket. Used by the ClamAV scanner
    /// to promote <c>uploads/raw/...</c> → <c>safe/...</c> after the
    /// scan passes (or → <c>quarantine/...</c> on hit).
    /// </summary>
    Task MoveAsync(string sourceKey, string destKey, CancellationToken ct = default, bool bypassTenantCheck = false);
}

/// <summary>
/// Result of a presigned-upload request.
/// </summary>
public record PresignedUpload(string Url, string ObjectKey, DateTime ExpiresAt, IReadOnlyDictionary<string, string> Headers);
