using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Phase 180 — Resets <see cref="PhotoAlbum"/> membership for every
/// album with a <c>SavedFilterJson</c>. Runs daily at 02:00 UTC, well
/// before the digest. Idempotent: a no-op when the resolved photo set
/// equals current membership.
///
/// Filter schema (PhotoAlbum.SavedFilterJson):
///   reason     — exact match
///   level      — exact match (LevelCode)
///   zone       — exact match (ZoneCode)
///   discipline — joined via Issue.Discipline
///   audienceIn — string array
///   fromDays   — int (capturedAt &gt;= now - N days)
///   limit      — int (default 200)
///
/// Locked albums are skipped (manual curation has been frozen).
/// </summary>
public class PhotoSmartAlbumMaterialiseJob
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<PhotoSmartAlbumMaterialiseJob> _logger;

    public PhotoSmartAlbumMaterialiseJob(
        PlanscapeDbContext db,
        ILogger<PhotoSmartAlbumMaterialiseJob> logger)
    {
        _db = db; _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var smart = await _db.PhotoAlbums
            .Where(a => a.SavedFilterJson != null && !a.IsLocked)
            .ToListAsync(ct);
        _logger.LogInformation("PhotoSmartAlbum: {Count} smart albums to materialise", smart.Count);

        foreach (var album in smart)
        {
            try { await MaterialiseAsync(album, ct); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PhotoSmartAlbum: album {AlbumId} failed", album.Id);
            }
        }
    }

    private sealed class FilterSpec
    {
        public string? Reason { get; set; }
        public string? Level { get; set; }
        public string? Zone { get; set; }
        public string? Discipline { get; set; }
        public string[]? AudienceIn { get; set; }
        public int? FromDays { get; set; }
        public int? Limit { get; set; }
    }

    private async Task MaterialiseAsync(PhotoAlbum album, CancellationToken ct)
    {
        FilterSpec? spec;
        try { spec = System.Text.Json.JsonSerializer.Deserialize<FilterSpec>(album.SavedFilterJson!); }
        catch { _logger.LogWarning("PhotoSmartAlbum: album {AlbumId} bad JSON", album.Id); return; }
        if (spec == null) return;

        var q = _db.SitePhotos.AsNoTracking().Where(p => p.ProjectId == album.ProjectId);
        if (!string.IsNullOrWhiteSpace(spec.Reason)) q = q.Where(p => p.Reason == spec.Reason);
        if (!string.IsNullOrWhiteSpace(spec.Level))  q = q.Where(p => p.LevelCode == spec.Level);
        if (!string.IsNullOrWhiteSpace(spec.Zone))   q = q.Where(p => p.ZoneCode  == spec.Zone);
        if (spec.AudienceIn is { Length: > 0 })      q = q.Where(p => spec.AudienceIn.Contains(p.Audience));
        if (spec.FromDays is > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-spec.FromDays.Value);
            q = q.Where(p => p.CapturedAt >= cutoff);
        }
        if (!string.IsNullOrWhiteSpace(spec.Discipline))
        {
            // Join through the auto-created issue link to read the
            // Issue.Discipline field.
            q = from p in q
                join i in _db.Issues.AsNoTracking() on p.AnchorIssueId equals i.Id
                where i.Discipline == spec.Discipline
                select p;
        }
        var resolved = await q
            .OrderByDescending(p => p.CapturedAt)
            .Select(p => p.Id)
            .Take(Math.Clamp(spec.Limit ?? 200, 1, 500))
            .ToListAsync(ct);
        var resolvedSet = new HashSet<Guid>(resolved);

        var current = await _db.PhotoAlbumPhotos
            .Where(ap => ap.AlbumId == album.Id)
            .ToListAsync(ct);
        var currentSet = new HashSet<Guid>(current.Select(c => c.PhotoId));

        if (resolvedSet.SetEquals(currentSet))
        {
            // No-op — saves a write turn when nothing has changed.
            return;
        }

        var toRemove = current.Where(c => !resolvedSet.Contains(c.PhotoId)).ToList();
        _db.PhotoAlbumPhotos.RemoveRange(toRemove);

        int sort = 0;
        var existingMap = current.ToDictionary(c => c.PhotoId);
        foreach (var pid in resolved)
        {
            sort += 100;
            if (existingMap.TryGetValue(pid, out var existing))
            {
                if (existing.SortOrder != sort) existing.SortOrder = sort;
            }
            else
            {
                _db.PhotoAlbumPhotos.Add(new PhotoAlbumPhoto
                {
                    AlbumId = album.Id, PhotoId = pid,
                    SortOrder = sort,
                });
            }
        }
        album.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "PhotoSmartAlbum: album {AlbumId} reset — added {Add}, removed {Rem}, kept {Keep}",
            album.Id, resolved.Count - existingMap.Keys.Intersect(resolvedSet).Count(),
            toRemove.Count, existingMap.Keys.Intersect(resolvedSet).Count());
    }
}
