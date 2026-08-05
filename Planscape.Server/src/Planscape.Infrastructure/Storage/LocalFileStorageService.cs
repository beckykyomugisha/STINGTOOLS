using Planscape.Core.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Planscape.Infrastructure.Storage;

/// <summary>
/// Stores files on the local filesystem under
///   <c>{StoragePath}/t_{tenantId}/{projectId}/{fileName}</c>
/// New code calls <see cref="SaveScopedAsync"/>; legacy callers can still
/// use the slug-based <see cref="SaveAsync"/>. All read/delete operations
/// validate the path's tenant prefix against the resolved
/// <see cref="ITenantContext"/> unless <c>bypassTenantCheck</c> is true.
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private readonly string _rootPath;
    private readonly ITenantContext? _tenantContext;
    private static readonly string[] CrossTenantPrefixes = { "derivatives", "thumbnails", "shared" };

    public LocalFileStorageService(IConfiguration config, ITenantContext? tenantContext = null)
    {
        _rootPath = config["Storage:Path"] ?? Path.Combine(Directory.GetCurrentDirectory(), "storage");
        _tenantContext = tenantContext;
    }

    public Task<string> SaveScopedAsync(Guid tenantId, Guid projectId, string fileName, Stream content, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("tenantId required", nameof(tenantId));
        if (projectId == Guid.Empty) throw new ArgumentException("projectId required", nameof(projectId));
        return SaveInternalAsync(TenantSegment(tenantId), projectId.ToString("N"), fileName, content, ct);
    }

    public Task<string> SaveAsync(string tenantSlug, string projectCode, string fileName, Stream content, CancellationToken ct = default)
    {
        return SaveInternalAsync(tenantSlug, projectCode, fileName, content, ct);
    }

    private async Task<string> SaveInternalAsync(string topSegment, string subSegment, string fileName, Stream content, CancellationToken ct)
    {
        var dir = Path.Combine(_rootPath, topSegment, subSegment);
        Directory.CreateDirectory(dir);

        var finalName = fileName;
        var fullPath = Path.Combine(dir, finalName);
        if (File.Exists(fullPath))
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            finalName = $"{stem}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}{ext}";
            fullPath = Path.Combine(dir, finalName);
        }

        await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
        await content.CopyToAsync(fs, ct);

        return Path.Combine(topSegment, subSegment, finalName).Replace('\\', '/');
    }

    public Task<Stream?> GetAsync(string path, CancellationToken ct = default, bool bypassTenantCheck = false)
    {
        EnforceTenantOwnership(path, bypassTenantCheck);
        var fullPath = Path.Combine(_rootPath, path);
        if (!File.Exists(fullPath))
            return Task.FromResult<Stream?>(null);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<Stream?>(stream);
    }

    public Task<bool> DeleteAsync(string path, CancellationToken ct = default, bool bypassTenantCheck = false)
    {
        EnforceTenantOwnership(path, bypassTenantCheck);
        var fullPath = Path.Combine(_rootPath, path);
        if (!File.Exists(fullPath))
            return Task.FromResult(false);
        File.Delete(fullPath);
        return Task.FromResult(true);
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default, bool bypassTenantCheck = false)
    {
        EnforceTenantOwnership(path, bypassTenantCheck);
        var fullPath = Path.Combine(_rootPath, path);
        return Task.FromResult(File.Exists(fullPath));
    }

    /// <summary>
    /// S7.4.2 — recursive delete of every file under the prefix.
    /// Returns the number of files removed.
    /// </summary>
    public Task<int> DeleteByPrefixAsync(string prefix, CancellationToken ct = default, bool bypassTenantCheck = false)
    {
        EnforceTenantOwnership(prefix, bypassTenantCheck);
        var fullPath = Path.Combine(_rootPath, prefix);
        if (Directory.Exists(fullPath))
        {
            var count = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories).Length;
            Directory.Delete(fullPath, recursive: true);
            return Task.FromResult(count);
        }
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            return Task.FromResult(1);
        }
        return Task.FromResult(0);
    }

    /// <summary>
    /// Phase 175 — local FS can't issue a presigned URL. Callers fall
    /// back to the multipart endpoint when this throws.
    /// </summary>
    public Task<Planscape.Core.Interfaces.PresignedUpload> GetPresignedPutUrlAsync(
        string objectKey, string contentType, TimeSpan validFor, long maxBytes, CancellationToken ct = default)
        => throw new NotSupportedException(
            "LocalFileStorageService cannot generate presigned URLs. Use S3 storage in production or POST the bytes through the API.");

    /// <summary>
    /// Phase 175 — server-side move via filesystem rename.
    /// </summary>
    public Task MoveAsync(string sourceKey, string destKey, CancellationToken ct = default, bool bypassTenantCheck = false)
    {
        EnforceTenantOwnership(sourceKey, bypassTenantCheck);
        EnforceTenantOwnership(destKey, bypassTenantCheck);
        var src = Path.Combine(_rootPath, sourceKey);
        var dst = Path.Combine(_rootPath, destKey);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        if (File.Exists(dst)) File.Delete(dst);
        File.Move(src, dst);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Throws <see cref="UnauthorizedAccessException"/> if the path's first
    /// segment doesn't match the current tenant or one of the well-known
    /// cross-tenant buckets ("derivatives", "thumbnails", "shared"). When
    /// the caller doesn't have a tenant context (e.g. background job), the
    /// check is skipped — the same job must call with bypassTenantCheck for
    /// clarity, but defensive defaults won't break legitimate platform code.
    /// </summary>
    private void EnforceTenantOwnership(string path, bool bypassTenantCheck)
    {
        if (bypassTenantCheck) return;
        if (_tenantContext == null || _tenantContext.TenantId == Guid.Empty) return;

        var firstSegment = path.Split('/', '\\')[0];
        if (CrossTenantPrefixes.Contains(firstSegment, StringComparer.OrdinalIgnoreCase))
            return;

        var expected = TenantSegment(_tenantContext.TenantId);
        if (!string.Equals(firstSegment, expected, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(firstSegment, _tenantContext.TenantSlug, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Storage path '{path}' does not belong to the current tenant.");
        }
    }

    private static string TenantSegment(Guid tenantId) => "t_" + tenantId.ToString("N");
}
