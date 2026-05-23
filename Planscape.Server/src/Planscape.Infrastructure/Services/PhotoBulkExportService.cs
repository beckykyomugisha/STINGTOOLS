using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Phase 179 — Bulk export of site photos to a ZIP bundle. Used by the
/// BCC + mobile "handover bundle" / "weekly digest archive" / "snag-list
/// export" actions.
///
/// The ZIP contains:
///   - <c>photos/{seq}-{captureDate}-{shortHash}.jpg</c>   — original or
///     redacted derivative, depending on caller's audience choice
///   - <c>index.csv</c>                                    — register
///   - <c>annotations/{photoId}.json</c>                   — overlays
///   - (when requested) <c>album.html</c>                  — single-page
///     gallery with captions + meta, no JS, opens in any browser
///
/// PDF export is implemented separately in <see cref="PhotoPdfExportService"/>
/// (deferred — PDF rendering needs PdfSharp / QuestPDF and we're not
/// shipping that on the Linux sandbox). The HTML index in the ZIP covers
/// the same use case for now.
/// </summary>
public class PhotoBulkExportService
{
    private readonly PlanscapeDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly ILogger<PhotoBulkExportService> _logger;

    public PhotoBulkExportService(
        PlanscapeDbContext db,
        IFileStorageService storage,
        ILogger<PhotoBulkExportService> logger)
    {
        _db = db; _storage = storage; _logger = logger;
    }

    public sealed record ExportRequest(
        Guid    ProjectId,
        Guid[]? PhotoIds,
        Guid?   AlbumId,
        bool    IncludeOriginals,
        bool    IncludeRedacted,
        bool    IncludeAnnotations,
        bool    IncludeHtmlIndex,
        string? CallerDisplayName);

    public sealed record ExportResult(
        string FileName,
        int    PhotoCount,
        long   ApproxBytes);

    /// <summary>
    /// Phase 179.3 — Stream the ZIP DIRECTLY to the supplied output
    /// stream (typically <c>Response.Body</c>), one entry at a time,
    /// from the storage stream straight into the entry stream. Peak
    /// memory is now bounded by ZipArchive's deflate buffer (~64 KB)
    /// plus one HTTP read buffer per photo — not the whole bundle.
    ///
    /// Old in-memory path could allocate up to 12 GB at the documented
    /// 500-photo cap × 25 MB each.
    /// </summary>
    public async Task<ExportResult> ExportAsync(
        ExportRequest req, Stream output, CancellationToken ct)
    {
        // Resolve target photos
        IQueryable<SitePhoto> q = _db.SitePhotos.AsNoTracking().Where(p => p.ProjectId == req.ProjectId);
        if (req.PhotoIds != null && req.PhotoIds.Length > 0)
        {
            var ids = new HashSet<Guid>(req.PhotoIds);
            q = q.Where(p => ids.Contains(p.Id));
        }
        else if (req.AlbumId.HasValue)
        {
            var albumPhotoIds = _db.PhotoAlbumPhotos.AsNoTracking()
                .Where(ap => ap.AlbumId == req.AlbumId.Value)
                .Select(ap => ap.PhotoId);
            q = q.Where(p => albumPhotoIds.Contains(p.Id));
        }
        var photos = await q.OrderBy(p => p.CapturedAt).ToListAsync(ct);
        var docIds = photos.Select(p => p.DocumentId).ToList();
        var docs = await _db.Documents.AsNoTracking()
            .Where(d => docIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, ct);
        var annotations = req.IncludeAnnotations
            ? await _db.PhotoAnnotations.AsNoTracking()
                .Where(a => photos.Select(p => p.Id).Contains(a.PhotoId))
                .ToListAsync(ct)
            : new List<PhotoAnnotation>();

        long approxBytes = 0;
        // leaveOpen=true: caller (controller) manages output. We only own the zip handle.
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var csv = new StringBuilder();
            csv.AppendLine("seq,id,reason,audience,caption,levelCode,zoneCode,capturedAt,fileName,latitude,longitude");
            int seq = 0;
            foreach (var p in photos)
            {
                seq++;
                if (!docs.TryGetValue(p.DocumentId, out var d) || d.FilePath == null) continue;

                string? path = null;
                if (req.IncludeRedacted && !string.IsNullOrEmpty(p.RedactedFilePath))
                    path = p.RedactedFilePath!;
                else if (req.IncludeOriginals)
                    path = d.FilePath!;
                if (path == null) continue;

                Stream? src;
                try { src = await _storage.GetAsync(path, ct, bypassTenantCheck: true); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Photo export skipped: {Path}", path);
                    continue;
                }
                if (src == null) continue;

                var ext = System.IO.Path.GetExtension(d.FileName);
                var entryName = $"photos/{seq:D4}-{p.CapturedAt:yyyyMMdd-HHmmss}-{Short(p.Id)}{ext}";
                var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
                using (var es = entry.Open())
                {
                    // Stream straight from storage to the entry — never
                    // pulls the whole image into a managed buffer.
                    await src.CopyToAsync(es, 81920, ct);
                }
                approxBytes += d.FileSizeBytes;
                await src.DisposeAsync();

                csv.AppendLine(string.Join(',',
                    seq, p.Id, Csv(p.Reason), Csv(p.Audience), Csv(p.Caption),
                    Csv(p.LevelCode), Csv(p.ZoneCode),
                    p.CapturedAt.ToString("o"), Csv(entryName),
                    p.Latitude?.ToString() ?? "", p.Longitude?.ToString() ?? ""));

                if (req.IncludeAnnotations)
                {
                    var annsForPhoto = annotations.Where(a => a.PhotoId == p.Id).ToList();
                    if (annsForPhoto.Count > 0)
                    {
                        var annEntry = zip.CreateEntry($"annotations/{p.Id}.json");
                        using var aw = new StreamWriter(annEntry.Open());
                        await aw.WriteAsync(System.Text.Json.JsonSerializer.Serialize(
                            annsForPhoto.Select(a => new { a.Id, a.ShapesJson, a.Summary, a.CreatedAt, a.CreatedByName })));
                    }
                }
            }

            var indexEntry = zip.CreateEntry("index.csv");
            using (var iw = new StreamWriter(indexEntry.Open()))
                await iw.WriteAsync(csv.ToString());

            if (req.IncludeHtmlIndex)
            {
                var html = BuildHtmlIndex(photos, docs, req);
                var hEntry = zip.CreateEntry("album.html");
                using var hw = new StreamWriter(hEntry.Open());
                await hw.WriteAsync(html);
            }

            var manifest = new
            {
                exportedAt   = DateTime.UtcNow,
                projectId    = req.ProjectId,
                albumId      = req.AlbumId,
                photoCount   = photos.Count,
                exportedBy   = req.CallerDisplayName,
                redactedOnly = req.IncludeRedacted && !req.IncludeOriginals,
            };
            var mEntry = zip.CreateEntry("manifest.json");
            using (var mw = new StreamWriter(mEntry.Open()))
                await mw.WriteAsync(System.Text.Json.JsonSerializer.Serialize(manifest));
        }
        return new ExportResult(
            FileName: $"photos-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip",
            PhotoCount: photos.Count,
            ApproxBytes: approxBytes);
    }

    private static string Short(Guid g) => g.ToString("N").Substring(0, 8);

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var needs = s.Contains(',') || s.Contains('"') || s.Contains('\n');
        return needs ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    private static string BuildHtmlIndex(
        List<SitePhoto> photos,
        Dictionary<Guid, DocumentRecord> docs,
        ExportRequest req)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html><head><meta charset='utf-8'><title>Site photos export</title>");
        sb.AppendLine("<style>body{font-family:sans-serif;margin:24px;background:#fafafa}");
        sb.AppendLine(".card{background:#fff;border:1px solid #ddd;border-radius:6px;padding:12px;margin:12px 0;display:flex;gap:12px}");
        sb.AppendLine(".card img{width:240px;height:auto;border-radius:4px}");
        sb.AppendLine(".meta{font-size:12px;color:#666}");
        sb.AppendLine(".reason{display:inline-block;padding:2px 8px;border-radius:8px;font-size:11px;color:#fff;font-weight:600}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine($"<h1>Site photos export — {photos.Count} photos</h1>");
        sb.AppendLine($"<div class=meta>Exported {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC by {req.CallerDisplayName ?? "system"}</div>");
        int n = 0;
        foreach (var p in photos)
        {
            n++;
            if (!docs.TryGetValue(p.DocumentId, out var d) || d.FilePath == null) continue;
            var ext = System.IO.Path.GetExtension(d.FileName);
            var rel = $"photos/{n:D4}-{p.CapturedAt:yyyyMMdd-HHmmss}-{Short(p.Id)}{ext}";
            sb.AppendLine("<div class=card>");
            sb.AppendLine($"<img src='{rel}' alt='photo'/>");
            sb.AppendLine("<div>");
            sb.AppendLine($"<div><span class=reason style='background:{ColourFor(p.Reason)}'>{p.Reason}</span></div>");
            sb.AppendLine($"<div><strong>{System.Net.WebUtility.HtmlEncode(p.Caption ?? "(no caption)")}</strong></div>");
            sb.AppendLine($"<div class=meta>{p.LevelCode ?? "—"} / {p.ZoneCode ?? "—"} · captured {p.CapturedAt:yyyy-MM-dd HH:mm}</div>");
            sb.AppendLine("</div></div>");
        }
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string ColourFor(string reason) => reason switch
    {
        "Safety"    => "#C62828",
        "Defect"    => "#E65C00",
        "Issue"     => "#E8912D",
        "Progress"  => "#1565C0",
        "AsBuilt"   => "#2E7D32",
        _           => "#45506E",
    };
}
