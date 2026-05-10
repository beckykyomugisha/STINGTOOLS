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
    private readonly PlanscapeDbContext _db;
    private readonly PhotoBulkExportService _exporter;

    public PhotoExportController(PlanscapeDbContext db, PhotoBulkExportService exporter)
    {
        _db = db; _exporter = exporter;
    }

    [HttpPost]
    public async Task<IActionResult> Export(
        Guid projectId,
        [FromBody] PhotoExportRequest req,
        CancellationToken ct = default)
    {
        if (await this.RequireProjectMemberAsync(_db, projectId, ct) is { } denied) return denied;
        if (req.PhotoIds == null && req.AlbumId == null)
            return BadRequest(new { error = "ids_or_album_required" });
        if (req.PhotoIds != null && req.PhotoIds.Length > MaxPhotosPerExport)
            return BadRequest(new { error = "batch_too_large", max = MaxPhotosPerExport });

        var includeOriginals = req.IncludeOriginals ?? IsApprover();
        // ClientGuest can never include originals.
        var role = User.FindFirst("role")?.Value ?? "";
        if (role == "ClientGuest") includeOriginals = false;

        var result = await _exporter.ExportAsync(new PhotoBulkExportService.ExportRequest(
            ProjectId:          projectId,
            PhotoIds:           req.PhotoIds,
            AlbumId:            req.AlbumId,
            IncludeOriginals:   includeOriginals,
            IncludeRedacted:    req.IncludeRedacted ?? !includeOriginals,
            IncludeAnnotations: req.IncludeAnnotations ?? true,
            IncludeHtmlIndex:   req.IncludeHtmlIndex ?? true,
            CallerDisplayName:  User.FindFirst("display_name")?.Value),
            ct);

        result.ZipStream.Position = 0;
        return File(result.ZipStream, "application/zip", result.FileName);
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
