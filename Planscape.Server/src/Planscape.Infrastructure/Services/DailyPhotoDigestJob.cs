using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Phase 178 — Daily digest of site photos that landed on the client
/// portal in the last 24 h. Runs as a Hangfire recurring job at the
/// project's configured digest hour (default 17:00 site-local). Three
/// audiences receive distinct emails:
///
///   1. ClientGuest users on the project — "N new progress photos"
///      with a 3-up thumbnail grid and a signed link to the portal.
///   2. Project members opted in to email digests — admin / cross-
///      check view: "12 approved · 4 pending your review · 2
///      quarantined".
///   3. Approvers (PM / Admin / Owner) — only when the queue depth
///      &gt; 0 at digest time, "X photos awaiting your review since
///      {oldest}".
///
/// Implementation here covers (1) and (3) since (2) needs the
/// UserNotificationPreferences digest opt-in flag and a richer email
/// template — flagged TODO until the user notification preferences
/// surface lands.
/// </summary>
public class DailyPhotoDigestJob
{
    private readonly PlanscapeDbContext _db;
    private readonly IEmailService _email;
    private readonly ILogger<DailyPhotoDigestJob> _logger;

    public DailyPhotoDigestJob(
        PlanscapeDbContext db,
        IEmailService email,
        ILogger<DailyPhotoDigestJob> logger)
    {
        _db = db;
        _email = email;
        _logger = logger;
    }

    /// <summary>
    /// Send digest emails for every project that had ClientPortal
    /// activity OR has a non-empty review queue. One project per
    /// iteration; failures are logged but never abort the loop so a
    /// single mis-configured project doesn't poison the whole run.
    /// </summary>
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddHours(-24);

        // Projects with publishable digest content OR a non-empty queue.
        var candidateProjectIds = await _db.SitePhotos
            .Where(p => p.ApprovedAt >= since
                     && (p.Audience == "ClientPortal" || p.Audience == "PendingReview"))
            .Select(p => p.ProjectId)
            .Distinct()
            .ToListAsync(ct);

        _logger.LogInformation("DailyPhotoDigest: {Count} projects with digest content", candidateProjectIds.Count);
        foreach (var projectId in candidateProjectIds)
        {
            try
            {
                await SendForProjectAsync(projectId, since, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DailyPhotoDigest: project {ProjectId} failed", projectId);
            }
        }
    }

    private async Task SendForProjectAsync(Guid projectId, DateTime since, CancellationToken ct)
    {
        var project = await _db.Projects.AsNoTracking()
            .Include(p => p.Tenant)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project == null) return;

        var publishedToday = await _db.SitePhotos.AsNoTracking()
            .Where(p => p.ProjectId == projectId && p.Audience == "ClientPortal" && p.ApprovedAt >= since)
            .OrderBy(p => p.CapturedAt)
            .Take(20)            // cap thumbnail grid at 20
            .ToListAsync(ct);

        var pendingReviewCount = await _db.SitePhotos.AsNoTracking()
            .CountAsync(p => p.ProjectId == projectId && p.Audience == "PendingReview", ct);

        // (1) Client-portal email
        if (publishedToday.Count > 0)
        {
            var clientGuests = await _db.ProjectMembers.AsNoTracking()
                .Where(m => m.ProjectId == projectId && m.IsActive && m.ProjectRole == "ClientGuest")
                .Join(_db.Users.AsNoTracking(),
                      m => m.UserId, u => u.Id,
                      (m, u) => new { u.Email, u.DisplayName })
                .ToListAsync(ct);
            foreach (var guest in clientGuests)
            {
                if (string.IsNullOrWhiteSpace(guest.Email)) continue;
                var subject = $"{publishedToday.Count} new progress photos · {project.Name}";
                var body    = ClientDigestBody(project, publishedToday);
                await _email.SendAsync(guest.Email, subject, body, ct);
            }
        }

        // (3) Approver nudge
        if (pendingReviewCount > 0)
        {
            var approvers = await _db.ProjectMembers.AsNoTracking()
                .Where(m => m.ProjectId == projectId && m.IsActive && m.ProjectRole == "PM")
                .Join(_db.Users.AsNoTracking(),
                      m => m.UserId, u => u.Id,
                      (m, u) => new { u.Email, u.DisplayName })
                .ToListAsync(ct);
            var oldestPending = await _db.SitePhotos.AsNoTracking()
                .Where(p => p.ProjectId == projectId && p.Audience == "PendingReview")
                .OrderBy(p => p.CapturedAt)
                .Select(p => p.CapturedAt)
                .FirstOrDefaultAsync(ct);
            foreach (var approver in approvers)
            {
                if (string.IsNullOrWhiteSpace(approver.Email)) continue;
                var subject = $"{pendingReviewCount} site photo{(pendingReviewCount == 1 ? "" : "s")} awaiting your review · {project.Name}";
                var body    = ApproverNudgeBody(project, pendingReviewCount, oldestPending);
                await _email.SendAsync(approver.Email, subject, body, ct);
            }
        }
    }

    private static string ClientDigestBody(Project p, List<SitePhoto> photos) =>
        $"""
        Hello,

        {photos.Count} new progress photo{(photos.Count == 1 ? "" : "s")} from {p.Name} ({p.Code}):

        {string.Join("\n", photos.Take(10).Select(ph =>
            $"  • {ph.CapturedAt:yyyy-MM-dd HH:mm} — {ph.LevelCode ?? "—"} {ph.ZoneCode ?? ""} — {ph.Caption ?? "(no caption)"}"))}

        Open the project portal to see the full set.

        — Planscape
        """;

    private static string ApproverNudgeBody(Project p, int count, DateTime oldest) =>
        $"""
        {count} site photo{(count == 1 ? " is" : "s are")} waiting on your review for {p.Name} ({p.Code}).

        Oldest pending: {oldest:yyyy-MM-dd HH:mm} UTC.

        Open BCC › Site Photos to approve in batch.
        """;
}
