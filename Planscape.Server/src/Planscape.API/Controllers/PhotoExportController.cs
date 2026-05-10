using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planscape.API.Authorization;
using Planscape.API.Services;
using Planscape.Infrastructure.Data;
using Planscape.Infrastructure.Services;

namespace Planscape.API.Controllers;

/// <summary>
/// Phase 179 — bulk-export endpoint. Streams a ZIP bundle (photos + CSV
/// register + optional HTML index + manifest) directly back to the
/// caller. Capped at 500 photos per request — anything bigger should
/// move to a Hangfire job + signed-URL email.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/photo-export")]
[Authorize]
[ProjectAccess]
public class PhotoExportController : ControllerBase
{
    private const int MaxPhotosPerExport = 500;
    private const int MaxPhotosPerPdf    = 200;
    private readonly PlanscapeDbContext _db;
    private readonly PhotoBulkExportService _exporter;
    private readonly PhotoPdfExportService  _pdfExporter;

    public PhotoExportController(
        PlanscapeDbContext db,
        PhotoBulkExportService exporter,
        PhotoPdfExportService pdfExporter)
    {
        _db = db; _exporter = exporter; _pdfExporter = pdfExporter;
    }

    [HttpPost]
    public async Task<IActionResult> Export(
        Guid projectId,
        [FromBody] PhotoExportRequest req,
        [FromQuery] string format = "zip",
        CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        if (req.PhotoIds == null && req.AlbumId == null)
            return BadRequest(new { error = "ids_or_album_required" });

        var includeOriginals = req.IncludeOriginals ?? IsApprover();
        // ClientGuest can never include originals.
        var role = User.FindFirst("role")?.Value ?? "";
        if (role == "ClientGuest") includeOriginals = false;

        if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
        {
            if (req.PhotoIds != null && req.PhotoIds.Length > MaxPhotosPerPdf)
                return BadRequest(new { error = "batch_too_large_for_pdf", max = MaxPhotosPerPdf });
            // Phase 179.3 — stream straight to Response.Body. No
            // intermediate MemoryStream — peak memory is one image
            // page (≤ 2 photos) instead of the full bundle.
            var fileName = $"photos-{DateTime.UtcNow:yyyyMMdd-HHmmss}.pdf";
            Response.ContentType = "application/pdf";
            Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
            await _pdfExporter.RenderAsync(new PhotoPdfExportService.PdfRequest(
                ProjectId:         projectId,
                PhotoIds:          req.PhotoIds,
                AlbumId:           req.AlbumId,
                IncludeRedacted:   !includeOriginals,
                CallerDisplayName: User.FindFirst("display_name")?.Value),
                Response.Body, ct);
            return new EmptyResult();
        }

        if (req.PhotoIds != null && req.PhotoIds.Length > MaxPhotosPerExport)
            return BadRequest(new { error = "batch_too_large", max = MaxPhotosPerExport });

        // Phase 179.3 — stream the ZIP straight to Response.Body. The
        // service writes one entry at a time from the storage stream
        // into the entry stream — no per-image MemoryStream, no
        // bundle-size MemoryStream.
        var zipName = $"photos-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";
        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{zipName}\"";
        await _exporter.ExportAsync(new PhotoBulkExportService.ExportRequest(
            ProjectId:          projectId,
            PhotoIds:           req.PhotoIds,
            AlbumId:            req.AlbumId,
            IncludeOriginals:   includeOriginals,
            IncludeRedacted:    req.IncludeRedacted ?? !includeOriginals,
            IncludeAnnotations: req.IncludeAnnotations ?? true,
            IncludeHtmlIndex:   req.IncludeHtmlIndex ?? true,
            CallerDisplayName:  User.FindFirst("display_name")?.Value),
            Response.Body, ct);
        return new EmptyResult();
    }

    private bool IsApprover()
    {
        var role = User.FindFirst("role")?.Value ?? "";
        return role is "Admin" or "Owner" or "PM";
    }
}

public record PhotoExportRequest(
    Guid[]? PhotoIds,
    Guid?   AlbumId,
    bool?   IncludeOriginals,
    bool?   IncludeRedacted,
    bool?   IncludeAnnotations,
    bool?   IncludeHtmlIndex);
