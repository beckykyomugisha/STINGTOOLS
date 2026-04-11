using Planscape.Core.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Planscape.Infrastructure.Storage;

/// <summary>
/// Stores files on the local filesystem under {StoragePath}/{tenantSlug}/{projectCode}/{fileName}.
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private readonly string _rootPath;

    public LocalFileStorageService(IConfiguration config)
    {
        _rootPath = config["Storage:Path"] ?? Path.Combine(Directory.GetCurrentDirectory(), "storage");
    }

    public async Task<string> SaveAsync(string tenantSlug, string projectCode, string fileName, Stream content, CancellationToken ct = default)
    {
        var dir = Path.Combine(_rootPath, tenantSlug, projectCode);
        Directory.CreateDirectory(dir);

        // Deduplicate filename if it already exists
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

        // Return the relative path that callers persist in the database
        return Path.Combine(tenantSlug, projectCode, finalName).Replace('\\', '/');
    }

    public Task<Stream?> GetAsync(string path, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_rootPath, path);
        if (!File.Exists(fullPath))
            return Task.FromResult<Stream?>(null);

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<Stream?>(stream);
    }

    public Task<bool> DeleteAsync(string path, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_rootPath, path);
        if (!File.Exists(fullPath))
            return Task.FromResult(false);

        File.Delete(fullPath);
        return Task.FromResult(true);
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_rootPath, path);
        return Task.FromResult(File.Exists(fullPath));
    }
}
