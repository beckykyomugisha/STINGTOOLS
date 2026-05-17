using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services.PhotoPipeline;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Phase 178 — On-demand Hangfire job that runs the blur+watermark
/// pipeline for a single <see cref="SitePhoto"/> after a reviewer
/// approves it. Runs on the dedicated <c>photo-redaction</c> queue,
/// which is bound to the separate <c>planscape-worker</c> container so
/// face-detect CPU never starves the API process (decision: split
/// worker now).
///
/// Pipeline (delegates to <see cref="PhotoRedactionPipeline"/>):
///   1. Fetch SitePhoto + DocumentRecord (path, project, tenant).
///   2. Skip when Audience != Approved (idempotent — re-run is no-op).
///   3. Pull original bytes via IFileStorageService.
///   4. Detect faces + number-plates → Gaussian-blur each box.
///   5. Apply watermark band ("PLANSCAPE · {code} · {client} · {date}").
///   6. Save derivative as ${baseName}_redacted.jpg in same subfolder.
///   7. Set RedactedFilePath, BlurStatus = Done, WatermarkApplied = true,
///      Audience = ClientPortal.
///   8. On any failure: BlurStatus = Failed; Audience stays Approved
///      (fail-closed — admin must retry; client sees nothing).
///
/// Caller: <see cref="Planscape.API.Controllers.SitePhotosController.Approve"/>
/// + <see cref="Planscape.API.Controllers.SitePhotosController.BulkApprove"/>
/// enqueue via <c>IBackgroundJobClient.Enqueue&lt;RedactPublishedPhotoJob&gt;</c>.
/// </summary>
public class RedactPublishedPhotoJob
{
    private readonly PlanscapeDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly IPhotoRedactionPipeline _pipeline;
    private readonly ILogger<RedactPublishedPhotoJob> _logger;

    public RedactPublishedPhotoJob(
        PlanscapeDbContext db,
        IFileStorageService storage,
        IPhotoRedactionPipeline pipeline,
        ILogger<RedactPublishedPhotoJob> logger)
    {
        _db = db;
        _storage = storage;
        _pipeline = pipeline;
        _logger = logger;
    }

    [Hangfire.Queue("photo-redaction")]
    public async Task RunAsync(Guid photoId, CancellationToken ct)
    {
        var photo = await _db.SitePhotos
            .Include(p => p.Project)
                .ThenInclude(pr => pr!.Tenant)
            .Include(p => p.Document)
            .FirstOrDefaultAsync(p => p.Id == photoId, ct);
        if (photo == null)
        {
            _logger.LogWarning("RedactPublishedPhoto: photo {Id} not found — skipping", photoId);
            return;
        }
        if (photo.Audience != "Approved")
        {
            _logger.LogDebug("RedactPublishedPhoto: photo {Id} audience={Audience} — skipping (not approved)",
                photoId, photo.Audience);
            return;
        }
        if (photo.Document?.FilePath == null)
        {
            _logger.LogError("RedactPublishedPhoto: photo {Id} has no document path — failing", photoId);
            await SetFailedAsync(photo, "missing_document_path", ct);
            return;
        }

        try
        {
            using var src = await _storage.GetAsync(photo.Document.FilePath, ct);
            if (src == null)
            {
                await SetFailedAsync(photo, "missing_blob", ct);
                return;
            }

            // Resolve watermark text from project + client metadata. Per
            // the locked decision: "PLANSCAPE · {code} · {client_short} · {date}"
            // where client_short = PRJ_ORG_CLIENT_INITIALS_TXT first, then
            // first 8 chars of PRJ_ORG_CLIENT_NAME_TXT.
            var watermark = BuildWatermark(photo);

            await using var ms = new MemoryStream();
            await src.CopyToAsync(ms, ct);
            ms.Position = 0;

            var result = await _pipeline.RedactAsync(ms, watermark, ct);
            if (result == null)
            {
                await SetFailedAsync(photo, "pipeline_returned_null", ct);
                return;
            }

            // Quarantine — too many faces means a crowd shot; needs human
            // review before publish. The pipeline tags this case explicitly.
            if (result.Quarantined)
            {
                _logger.LogWarning("RedactPublishedPhoto: photo {Id} quarantined ({Reason})",
                    photoId, result.QuarantineReason);
                await SetFailedAsync(photo, $"quarantined:{result.QuarantineReason}", ct);
                return;
            }

            var dir       = System.IO.Path.GetDirectoryName(photo.Document.FilePath)?.Replace('\\', '/') ?? "";
            var baseName  = System.IO.Path.GetFileNameWithoutExtension(photo.Document.FilePath);
            var redactedFileName = $"{baseName}_redacted.jpg";
            var tenantSlug = photo.Project?.Tenant?.Slug ?? photo.TenantId.ToString();

            using var derivative = new MemoryStream(result.Bytes);
            var redactedPath = await _storage.SaveAsync(tenantSlug, dir + "/redacted", redactedFileName, derivative, ct);

            photo.RedactedFilePath = redactedPath;
            photo.BlurStatus = "Done";
            photo.WatermarkApplied = true;
            photo.Audience = "ClientPortal";
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "RedactPublishedPhoto: photo {Id} published — facesBlurred={Faces} platesBlurred={Plates}",
                photoId, result.FacesBlurred, result.PlatesBlurred);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RedactPublishedPhoto: photo {Id} failed", photoId);
            await SetFailedAsync(photo, "exception:" + ex.GetType().Name, ct);
            throw;     // let Hangfire retry-policy kick in
        }
    }

    private async Task SetFailedAsync(SitePhoto photo, string reason, CancellationToken ct)
    {
        photo.BlurStatus = "Failed";
        // Audience stays at "Approved" so admins see it in the queue and
        // can retry without re-approving. The reason is captured in audit.
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("RedactPublishedPhoto: photo {Id} marked Failed — {Reason}", photo.Id, reason);
    }

    private static string BuildWatermark(SitePhoto photo)
    {
        // Resolve client_short — read shared parameter values via the
        // project relation. PRJ_ORG_CLIENT_INITIALS_TXT lives on the
        // Project/Tenant in the schema; fall back to first 8 chars of
        // the client name. Empty values get gracefully omitted.
        var code   = photo.Project?.Code ?? "—";
        var client = ResolveClientShort(photo);
        var date   = photo.CapturedAt.ToString("yyyy-MM-dd");
        var parts  = new List<string> { "PLANSCAPE", code };
        if (!string.IsNullOrWhiteSpace(client)) parts.Add(client!);
        parts.Add(date);
        return string.Join(" · ", parts);
    }

    private static string? ResolveClientShort(SitePhoto photo)
    {
        // Best-effort lookup — projects expose their client metadata via
        // either dedicated columns (preferred) or the legacy ProjectInfo
        // shared-parameter blob. We probe both surfaces.
        var p = photo.Project;
        if (p == null) return null;
        // Prefer initials column if present on Project; fall through.
        var initialsProp = p.GetType().GetProperty("ClientInitials");
        if (initialsProp?.GetValue(p) is string initials && !string.IsNullOrWhiteSpace(initials)) return initials;
        var nameProp = p.GetType().GetProperty("ClientName");
        if (nameProp?.GetValue(p) is string name && !string.IsNullOrWhiteSpace(name))
        {
            var trimmed = name.Trim();
            return trimmed.Length <= 8 ? trimmed : trimmed.Substring(0, 8);
        }
        return null;
    }
}
