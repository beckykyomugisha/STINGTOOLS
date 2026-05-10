using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Phase 179 — Daily Hangfire recurring job that walks every project's
/// <see cref="PhotoPolicy"/> and applies its retention rules:
///
///   * RetentionDays set    — Approved/ClientPortal photos older than N
///                            days flip to <c>Withdrawn</c> (audit logged,
///                            client portal stops serving them).
///   * AutoArchiveAfterHandover — once project status hits HANDOVER, any
///                            Reason=Progress photos auto-flip to
///                            <c>Withdrawn</c> the next day.
///   * PhotoAlbum.AutoArchiveAfterDays — same flip applied to album members
///                            after the album's age exceeds the threshold.
///
/// All transitions go through SaveChanges so EF audit triggers fire and
/// the ComplianceSnapshot picks up the change at next scan. The job is
/// idempotent — safe to re-run.
/// </summary>
public class PhotoRetentionJob
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<PhotoRetentionJob> _logger;

    public PhotoRetentionJob(PlanscapeDbContext db, ILogger<PhotoRetentionJob> logger)
    {
        _db = db; _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var policies = await _db.PhotoPolicies.AsNoTracking().ToListAsync(ct);
        int processed = 0, withdrawn = 0;
        foreach (var pol in policies)
        {
            try
            {
                var (got, wd) = await ApplyForProjectAsync(pol, ct);
                processed += got; withdrawn += wd;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PhotoRetentionJob: project {ProjectId} failed", pol.ProjectId);
            }
        }

        await ApplyAlbumRetentionAsync(ct);
        _logger.LogInformation(
            "PhotoRetentionJob complete — {Processed} candidates, {Withdrawn} withdrawn",
            processed, withdrawn);
    }

    private async Task<(int processed, int withdrawn)> ApplyForProjectAsync(
        PhotoPolicy pol, CancellationToken ct)
    {
        int processed = 0, withdrawn = 0;
        if (pol.RetentionDays.HasValue && pol.RetentionDays.Value > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-pol.RetentionDays.Value);
            var stale = await _db.SitePhotos
                .Where(p => p.ProjectId == pol.ProjectId
                         && (p.Audience == "Approved" || p.Audience == "ClientPortal")
                         && p.CapturedAt < cutoff)
                .ToListAsync(ct);
            foreach (var p in stale)
            {
                p.Audience    = "Withdrawn";
                p.WithdrawnAt = DateTime.UtcNow;
                withdrawn++;
            }
            processed += stale.Count;
            await _db.SaveChangesAsync(ct);
        }

        if (pol.AutoArchiveAfterHandover)
        {
            var project = await _db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == pol.ProjectId, ct);
            if (project?.Status == "HANDOVER")
            {
                var progress = await _db.SitePhotos
                    .Where(p => p.ProjectId == pol.ProjectId
                             && p.Reason == "Progress"
                             && (p.Audience == "Approved" || p.Audience == "ClientPortal"))
                    .ToListAsync(ct);
                foreach (var p in progress)
                {
                    p.Audience    = "Withdrawn";
                    p.WithdrawnAt = DateTime.UtcNow;
                    withdrawn++;
                }
                processed += progress.Count;
                await _db.SaveChangesAsync(ct);
            }
        }

        return (processed, withdrawn);
    }

    private async Task ApplyAlbumRetentionAsync(CancellationToken ct)
    {
        var albums = await _db.PhotoAlbums.AsNoTracking()
            .Where(a => a.AutoArchiveAfterDays != null && !a.IsLocked)
            .ToListAsync(ct);
        foreach (var album in albums)
        {
            var cutoff = album.CreatedAt.AddDays(album.AutoArchiveAfterDays!.Value);
            if (DateTime.UtcNow < cutoff) continue;

            var ids = await _db.PhotoAlbumPhotos.AsNoTracking()
                .Where(ap => ap.AlbumId == album.Id)
                .Select(ap => ap.PhotoId).ToListAsync(ct);
            if (ids.Count == 0) continue;

            var photos = await _db.SitePhotos
                .Where(p => ids.Contains(p.Id) &&
                            (p.Audience == "Approved" || p.Audience == "ClientPortal"))
                .ToListAsync(ct);
            foreach (var p in photos)
            {
                p.Audience    = "Withdrawn";
                p.WithdrawnAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(ct);
        }
    }
}
