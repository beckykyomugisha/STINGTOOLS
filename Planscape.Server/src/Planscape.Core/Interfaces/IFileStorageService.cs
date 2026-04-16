namespace Planscape.Core.Interfaces;

/// <summary>
/// Abstracts file storage operations so controllers never touch the filesystem directly.
/// Swap LocalFileStorageService for MinIO / Azure Blob in production.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Persist a file and return the relative storage path.
    /// </summary>
    Task<string> SaveAsync(string tenantSlug, string projectCode, string fileName, Stream content, CancellationToken ct = default);

    /// <summary>
    /// Retrieve a readable stream for the given storage path, or null if not found.
    /// </summary>
    Task<Stream?> GetAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Delete the file at the given storage path. Returns true if deleted.
    /// </summary>
    Task<bool> DeleteAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Check whether a file exists at the given storage path.
    /// </summary>
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);
}
