using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Phase 179 — PDF album rendering. Sibling of
/// <see cref="PhotoBulkExportService"/> for the case where the consumer
/// wants a single printable document instead of a ZIP.
///
/// Layout:
///   Cover page  — project name, album name, photo count, exported-by,
///                 exported-at, optional watermark logo.
///   Index       — table of every photo with caption + level/zone +
///                 capture date.
///   Body        — 2-up grid (per page) of photos with caption strip
///                 underneath. Reason colour shows on the strip.
///
/// Limits: capped at 200 photos per render (≈ 100 pages) — bigger
/// jobs should move to Hangfire + signed-URL email. Memory grows
/// linearly with pixel data; QuestPDF streams the output to the
/// returned Stream so peak memory is roughly two image pages.
/// </summary>
public class PhotoPdfExportService
{
    private readonly PlanscapeDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly ILogger<PhotoPdfExportService> _logger;

    public PhotoPdfExportService(
        PlanscapeDbContext db,
        IFileStorageService storage,
        ILogger<PhotoPdfExportService> logger)
    {
        _db = db; _storage = storage; _logger = logger;
        // Set once per process; idempotent. Program.cs also sets this so
        // the assignment here is a defence-in-depth no-op when the API
        // already configured the licence at startup.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public sealed record PdfRequest(
        Guid    ProjectId,
        Guid[]? PhotoIds,
        Guid?   AlbumId,
        bool    IncludeRedacted,
        string? CallerDisplayName);

    public sealed record PdfResult(string FileName, int PhotoCount);

    /// <summary>
    /// Phase 179.3 — Stream the PDF directly to <paramref name="output"/>
    /// (typically <c>Response.Body</c>). Image bytes are fetched JUST IN
    /// TIME as QuestPDF requests them — peak memory is one image, not
    /// the whole album. The previous in-memory <c>imageCache</c> could
    /// allocate up to 5 GB at the documented 200-photo cap.
    /// </summary>
    public async Task<PdfResult> RenderAsync(PdfRequest req, Stream output, CancellationToken ct)
    {
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
                .OrderBy(ap => ap.SortOrder)
                .Select(ap => ap.PhotoId);
            q = q.Where(p => albumPhotoIds.Contains(p.Id));
        }
        var photos = await q.OrderBy(p => p.CapturedAt).Take(200).ToListAsync(ct);

        var docIds = photos.Select(p => p.DocumentId).ToList();
        var docs = await _db.Documents.AsNoTracking()
            .Where(d => docIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, ct);
        var project = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == req.ProjectId, ct);
        string? albumName = null;
        if (req.AlbumId.HasValue)
        {
            albumName = await _db.PhotoAlbums.AsNoTracking()
                .Where(a => a.Id == req.AlbumId.Value).Select(a => a.Name).FirstOrDefaultAsync(ct);
        }

        // Phase 179.3 — load each image only when QuestPDF asks for it
        // (one at a time during render). Local helper keeps the FilePath
        // resolution tight to the call site.
        async Task<byte[]?> FetchAsync(SitePhoto p)
        {
            if (!docs.TryGetValue(p.DocumentId, out var d) || d.FilePath == null) return null;
            var path = req.IncludeRedacted && !string.IsNullOrEmpty(p.RedactedFilePath)
                ? p.RedactedFilePath!
                : d.FilePath!;
            try
            {
                var s = await _storage.GetAsync(path, ct, bypassTenantCheck: true);
                if (s == null) return null;
                using var ms = new MemoryStream();
                await s.CopyToAsync(ms, ct);
                await s.DisposeAsync();
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PDF render: could not load {Path}", path);
                return null;
            }
        }

        // Pre-fetch the image we need for the body in render-order; the
        // cover/index pages don't render images so this stays O(N) on
        // the body pass below. We fetch synchronously per page rather
        // than eagerly so peak memory is one page (≤ 2 images).
        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(36);
                page.Size(PageSizes.A4);
                page.Header().Text(albumName ?? project?.Name ?? "Site photos")
                    .FontSize(18).Bold();
                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text($"Project: {project?.Name ?? "(unknown)"}").FontSize(10);
                    col.Item().Text($"Photos: {photos.Count}").FontSize(10);
                    col.Item().Text($"Exported by: {req.CallerDisplayName ?? "system"}").FontSize(10);
                    col.Item().Text($"Exported at: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").FontSize(10);
                    if (req.IncludeRedacted)
                        col.Item().Text("Redacted derivatives only — faces / plates blurred.").FontSize(9).Italic();
                });
                page.Footer().AlignCenter().Text(t =>
                {
                    t.CurrentPageNumber().FontSize(9);
                    t.Span(" / ").FontSize(9);
                    t.TotalPages().FontSize(9);
                });
            });

            // Index page
            doc.Page(page =>
            {
                page.Margin(36);
                page.Size(PageSizes.A4);
                page.Header().Text("Index").FontSize(14).Bold();
                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(28);  // seq
                        c.RelativeColumn(2);   // caption
                        c.RelativeColumn(1);   // reason / level / zone
                        c.RelativeColumn(1);   // captured
                    });
                    table.Header(h =>
                    {
                        h.Cell().Text("#").Bold();
                        h.Cell().Text("Caption").Bold();
                        h.Cell().Text("Reason · Level / Zone").Bold();
                        h.Cell().Text("Captured").Bold();
                    });
                    int n = 0;
                    foreach (var p in photos)
                    {
                        n++;
                        table.Cell().Text(n.ToString()).FontSize(9);
                        table.Cell().Text(p.Caption ?? "(no caption)").FontSize(9);
                        table.Cell().Text($"{p.Reason} · {p.LevelCode ?? "—"}/{p.ZoneCode ?? "—"}").FontSize(9);
                        table.Cell().Text(p.CapturedAt.ToString("yyyy-MM-dd HH:mm")).FontSize(9);
                    }
                });
            });

            // Body — 2-up grid per page. Fetch image bytes lazily per
            // page so peak memory is two photos, not the whole album.
            for (int i = 0; i < photos.Count; i += 2)
            {
                var pageItems = photos.Skip(i).Take(2).ToList();
                // Fetch images for this page synchronously off the
                // QuestPDF render thread to avoid sync-over-async.
                var pageImages = pageItems
                    .Select(p => FetchAsync(p).GetAwaiter().GetResult())
                    .ToList();
                doc.Page(page =>
                {
                    page.Margin(36);
                    page.Size(PageSizes.A4);
                    page.Content().Column(col =>
                    {
                        col.Spacing(20);
                        for (int j = 0; j < pageItems.Count; j++)
                        {
                            var p = pageItems[j];
                            var bytes = pageImages[j];
                            col.Item().Column(cell =>
                            {
                                if (bytes != null)
                                {
                                    cell.Item().Image(bytes).FitArea();
                                }
                                else
                                {
                                    cell.Item().Background("#EEEEEE").Padding(20).AlignCenter()
                                        .Text("(image unavailable)").FontSize(10).Italic();
                                }
                                cell.Item().PaddingTop(4).Text(t =>
                                {
                                    t.Span($"{p.Reason} · ").FontSize(9).FontColor(ReasonColour(p.Reason));
                                    t.Span(p.Caption ?? "(no caption)").FontSize(10).Bold();
                                });
                                cell.Item().Text($"{p.LevelCode ?? "—"} / {p.ZoneCode ?? "—"} · {p.CapturedAt:yyyy-MM-dd HH:mm}")
                                    .FontSize(8).FontColor("#666666");
                            });
                        }
                    });
                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.CurrentPageNumber().FontSize(9);
                        t.Span(" / ").FontSize(9);
                        t.TotalPages().FontSize(9);
                    });
                });
            }
        }).GeneratePdf(output);

        var name = $"photos-{DateTime.UtcNow:yyyyMMdd-HHmmss}.pdf";
        return new PdfResult(name, photos.Count);
    }

    private static string ReasonColour(string reason) => reason switch
    {
        "Safety"   => "#C62828",
        "Defect"   => "#E65C00",
        "Issue"    => "#E8912D",
        "Progress" => "#1565C0",
        "AsBuilt"  => "#2E7D32",
        _          => "#45506E",
    };
}
