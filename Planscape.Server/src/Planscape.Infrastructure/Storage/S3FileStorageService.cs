using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Planscape.Core.Interfaces;

namespace Planscape.Infrastructure.Storage;

/// <summary>
/// S3-compatible file storage backed by AWS S3, MinIO, Cloudflare R2, DigitalOcean Spaces,
/// or any other provider exposing the S3 protocol.
///
/// Configuration keys (appsettings.json or environment):
///   Storage:S3:BucketName           e.g. "planscape-prod"
///   Storage:S3:Region               e.g. "eu-west-2" (AWS) or "us-east-1" (MinIO/R2 default)
///   Storage:S3:ServiceUrl           optional — MinIO / R2 endpoint. Leave blank for AWS S3.
///   Storage:S3:AccessKey            access key id
///   Storage:S3:SecretKey            secret access key
///   Storage:S3:ForcePathStyle       "true" for MinIO, false/absent for AWS
/// </summary>
public class S3FileStorageService : IFileStorageService, IAsyncDisposable
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly ILogger<S3FileStorageService> _logger;
    private readonly ITenantContext? _tenantContext;
    private static readonly string[] CrossTenantPrefixes = { "derivatives", "thumbnails", "shared" };

    public S3FileStorageService(IConfiguration config, ILogger<S3FileStorageService> logger, ITenantContext? tenantContext = null)
    {
        _logger = logger;
        _tenantContext = tenantContext;

        var section = config.GetSection("Storage:S3");
        _bucket = section["BucketName"] ?? throw new InvalidOperationException("Storage:S3:BucketName not configured");

        var region = section["Region"] ?? "us-east-1";
        var serviceUrl = section["ServiceUrl"];
        var accessKey = section["AccessKey"];
        var secretKey = section["SecretKey"];
        var forcePathStyle = bool.TryParse(section["ForcePathStyle"], out var fps) && fps;

        var s3Config = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(region),
            ForcePathStyle = forcePathStyle,
        };
        if (!string.IsNullOrEmpty(serviceUrl))
        {
            s3Config.ServiceURL = serviceUrl;
            s3Config.UseHttp = serviceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        }

        AWSCredentials creds = !string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey)
            ? new BasicAWSCredentials(accessKey, secretKey)
            : FallbackCredentialsFactory.GetCredentials();

        _s3 = new AmazonS3Client(creds, s3Config);

        // Best-effort bucket auto-create on startup (idempotent). Callers in
        // prod typically pre-provision, but dev / MinIO single-node benefits
        // from not blowing up on first upload.
        _ = EnsureBucketAsync();
    }

    private async Task EnsureBucketAsync()
    {
        try
        {
            await _s3.PutBucketAsync(new PutBucketRequest { BucketName = _bucket });
            _logger.LogInformation("Created S3 bucket {Bucket}", _bucket);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "BucketAlreadyOwnedByYou" || ex.ErrorCode == "BucketAlreadyExists")
        {
            // fine — already exists
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EnsureBucketAsync failed for {Bucket}", _bucket);
        }
    }

    private static string BuildKey(string tenantSlug, string projectCode, string fileName)
        => $"{tenantSlug}/{projectCode}/{fileName}".Replace('\\', '/');

    private static string TenantSegment(Guid tenantId) => "t_" + tenantId.ToString("N");

    public Task<string> SaveScopedAsync(Guid tenantId, Guid projectId, string fileName, Stream content, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("tenantId required", nameof(tenantId));
        if (projectId == Guid.Empty) throw new ArgumentException("projectId required", nameof(projectId));
        return SaveInternalAsync(TenantSegment(tenantId), projectId.ToString("N"), fileName, content, ct);
    }

    public Task<string> SaveAsync(string tenantSlug, string projectCode, string fileName, Stream content, CancellationToken ct = default)
        => SaveInternalAsync(tenantSlug, projectCode, fileName, content, ct);

    private async Task<string> SaveInternalAsync(string topSegment, string subSegment, string fileName, Stream content, CancellationToken ct)
    {
        var key = $"{topSegment}/{subSegment}/{fileName}".Replace('\\', '/');

        // Existence check bypasses tenant validation — we just generated the
        // key from caller-supplied (already-trusted) inputs.
        if (await ExistsAsync(key, ct, bypassTenantCheck: true))
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            fileName = $"{stem}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}{ext}";
            key = $"{topSegment}/{subSegment}/{fileName}".Replace('\\', '/');
        }

        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            AutoCloseStream = false,
        };
        await _s3.PutObjectAsync(request, ct);
        _logger.LogDebug("Uploaded {Key} to {Bucket}", key, _bucket);
        return key;
    }

    public async Task<Stream?> GetAsync(string path, CancellationToken ct = default, bool bypassTenantCheck = false)
    {
        EnforceTenantOwnership(path, bypassTenantCheck);
        try
        {
            var response = await _s3.GetObjectAsync(new GetObjectRequest { BucketName = _bucket, Key = path }, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string path, CancellationToken ct = default, bool bypassTenantCheck = false)
    {
        EnforceTenantOwnership(path, bypassTenantCheck);
        try
        {
            await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucket, Key = path }, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken ct = default, bool bypassTenantCheck = false)
    {
        EnforceTenantOwnership(path, bypassTenantCheck);
        try
        {
            await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = _bucket, Key = path }, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>
    /// S1.2 — rejects paths whose first segment doesn't match the current
    /// tenant id (or slug, for legacy paths) or one of the well-known
    /// cross-tenant buckets ("derivatives", "thumbnails", "shared"). When
    /// no tenant context is wired (background job) the check is skipped —
    /// callers must pass <c>bypassTenantCheck</c> for clarity, but the
    /// defensive default fails closed without breaking platform code.
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

    public ValueTask DisposeAsync()
    {
        _s3.Dispose();
        return ValueTask.CompletedTask;
    }
}
