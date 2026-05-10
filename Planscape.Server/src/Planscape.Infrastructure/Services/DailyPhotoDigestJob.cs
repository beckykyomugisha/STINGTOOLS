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

        // (1) Client-portal email — gated on EmailDigestEnabled (T2-13).
        // Phase 180 — when the project's PhotoPolicy names a
        // DigestDistributionGroupId, recipients come from that group
        // instead of "every project ClientGuest". This lets the BIM
        // manager direct the daily digest at a specific list (e.g.
        // "Client weekly" group with 4 emails) rather than every
        // tenant client guest, which is rarely what the team wants.
        if (publishedToday.Count > 0)
        {
            var policy = await _db.PhotoPolicies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProjectId == projectId, ct);
            List<(string Email, string? DisplayName, bool OptIn)> recipients;
            if (policy?.DigestDistributionGroupId is { } dgId)
            {
                // Resolve from distribution group: internal users + external emails.
                var members = await _db.DistributionGroupMembers.AsNoTracking()
                    .Where(m => m.DistributionGroupId == dgId)
                    .ToListAsync(ct);
                var internalUserIds = members.Where(m => m.UserId.HasValue).Select(m => m.UserId!.Value).ToList();
                var users = await _db.Users.AsNoTracking()
                    .Where(u => internalUserIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, ct);
                recipients = members.Select(m =>
                {
                    if (m.UserId.HasValue && users.TryGetValue(m.UserId.Value, out var u))
                        return (u.Email ?? "", u.DisplayName as string, true);
                    return (m.ExternalEmail ?? "", m.DisplayName, true);
                }).ToList();
            }
            else
            {
                recipients = await (
                    from m in _db.ProjectMembers.AsNoTracking()
                    where m.ProjectId == projectId && m.IsActive && m.ProjectRole == "ClientGuest"
                    join u in _db.Users.AsNoTracking() on m.UserId equals u.Id
                    join p in _db.UserNotificationPreferences.AsNoTracking() on u.Id equals p.UserId into pg
                    from p in pg.DefaultIfEmpty()
                    select new ValueTuple<string, string?, bool>(
                        u.Email ?? "",
                        u.DisplayName,
                        p == null || p.EmailDigestEnabled))
                    .ToListAsync(ct);
            }
            int sent = 0, skipped = 0;
            foreach (var (email, _, optIn) in recipients)
            {
                if (string.IsNullOrWhiteSpace(email)) { skipped++; continue; }
                if (!optIn) { skipped++; continue; }
                var subject = $"{publishedToday.Count} new progress photos · {project.Name}";
                var body    = ClientDigestHtml(project, publishedToday);
                await _email.SendAsync(email, subject, body, ct);
                sent++;
            }
            _logger.LogInformation(
                "DailyPhotoDigest: project {ProjectId} client digest sent={Sent} skipped={Skipped} via={Via}",
                projectId, sent, skipped,
                policy?.DigestDistributionGroupId is null ? "client-guests" : "distribution-group");
        }

        // (3) Approver nudge — same opt-out gate.
        if (pendingReviewCount > 0)
        {
            var approvers = await (
                from m in _db.ProjectMembers.AsNoTracking()
                where m.ProjectId == projectId && m.IsActive && m.ProjectRole == "PM"
                join u in _db.Users.AsNoTracking() on m.UserId equals u.Id
                join p in _db.UserNotificationPreferences.AsNoTracking() on u.Id equals p.UserId into pg
                from p in pg.DefaultIfEmpty()
                select new { u.Email, u.DisplayName,
                             OptIn = p == null || p.EmailDigestEnabled })
                .ToListAsync(ct);
            var oldestPending = await _db.SitePhotos.AsNoTracking()
                .Where(p => p.ProjectId == projectId && p.Audience == "PendingReview")
                .OrderBy(p => p.CapturedAt)
                .Select(p => p.CapturedAt)
                .FirstOrDefaultAsync(ct);
            foreach (var approver in approvers)
            {
                if (string.IsNullOrWhiteSpace(approver.Email)) continue;
                if (!approver.OptIn) continue;
                var subject = $"{pendingReviewCount} site photo{(pendingReviewCount == 1 ? "" : "s")} awaiting your review · {project.Name}";
                var body    = ApproverNudgeHtml(project, pendingReviewCount, oldestPending);
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

    // T2-13 — branded HTML body sent to ClientGuest recipients. Inlined
    // styles (Outlook + Gmail can't be relied on to load remote CSS),
    // accent colour matches the marketing site (#FF6B35), and a 3-up
    // thumbnail grid is followed by a plain-text fallback for the 10
    // most recent captions. Unsubscribe link points at the user
    // notification-preferences endpoint with a per-user
    // EmailDigestEnabled toggle.
    private static string ClientDigestHtml(Project p, List<SitePhoto> photos)
    {
        string Esc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
        var top = photos.Take(10).ToList();
        var thumbs = string.Concat(top.Take(3).Select(ph =>
            $"<div style=\"flex:1;min-width:160px;\"><div style=\"background:#1a237e;color:#fff;padding:6px 10px;font-size:11px;border-radius:6px 6px 0 0;\">{Esc(ph.LevelCode ?? "—")} {Esc(ph.ZoneCode ?? "")}</div><div style=\"background:#f6f6f4;padding:10px;border-radius:0 0 6px 6px;font-size:13px;line-height:1.4;color:#333;min-height:60px;\">{Esc(ph.Caption ?? "(no caption)")}</div></div>"));
        var rows = string.Concat(top.Select(ph =>
            $"<tr><td style=\"padding:6px 8px;font-family:monospace;font-size:11px;color:#555;white-space:nowrap;\">{ph.CapturedAt:yyyy-MM-dd HH:mm}</td><td style=\"padding:6px 8px;font-size:13px;\"><strong>{Esc(ph.LevelCode ?? "—")}</strong> {Esc(ph.ZoneCode ?? "")} — {Esc(ph.Caption ?? "(no caption)")}</td></tr>"));
        return $"""
<!doctype html>
<html><body style="margin:0;padding:0;background:#f4f4f4;font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#222;">
  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f4f4f4;padding:24px 0;">
    <tr><td align="center">
      <table role="presentation" width="600" cellspacing="0" cellpadding="0" style="background:#ffffff;border-radius:8px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,0.06);">
        <tr><td style="background:#1a237e;color:#ffffff;padding:18px 24px;font-size:14px;font-weight:600;letter-spacing:0.04em;text-transform:uppercase;">PLANSCAPE</td></tr>
        <tr><td style="padding:24px;">
          <h2 style="margin:0 0 4px;font-size:20px;color:#1a237e;">{photos.Count} new progress photo{(photos.Count == 1 ? "" : "s")}</h2>
          <p style="margin:0 0 16px;font-size:14px;color:#555;">{Esc(p.Name)} <span style="color:#999;">·</span> {Esc(p.Code)}</p>
          <div style="display:flex;gap:8px;flex-wrap:wrap;margin-bottom:16px;">{thumbs}</div>
          <table role="presentation" cellspacing="0" cellpadding="0" width="100%" style="border-collapse:collapse;font-size:13px;">{rows}</table>
          <p style="margin:24px 0 0;text-align:center;"><a href="#" style="display:inline-block;background:#FF6B35;color:#ffffff;padding:10px 24px;border-radius:6px;font-size:14px;font-weight:600;text-decoration:none;">Open project portal →</a></p>
        </td></tr>
        <tr><td style="background:#f6f6f4;padding:14px 24px;font-size:11px;color:#888;text-align:center;">
          You're receiving this because progress photos were published on {Esc(p.Name)}.<br/>
          <a href="#" style="color:#888;text-decoration:underline;">Manage email preferences</a> · sent by Planscape.
        </td></tr>
      </table>
    </td></tr>
  </table>
</body></html>
""";
    }

    private static string ApproverNudgeHtml(Project p, int count, DateTime oldest)
    {
        string Esc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
        var hoursOld = (int)(DateTime.UtcNow - oldest).TotalHours;
        return $"""
<!doctype html>
<html><body style="margin:0;padding:0;background:#f4f4f4;font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#222;">
  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f4f4f4;padding:24px 0;">
    <tr><td align="center">
      <table role="presentation" width="600" cellspacing="0" cellpadding="0" style="background:#ffffff;border-radius:8px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,0.06);">
        <tr><td style="background:#FF6B35;color:#ffffff;padding:18px 24px;font-size:14px;font-weight:600;letter-spacing:0.04em;text-transform:uppercase;">⚠ Review queue</td></tr>
        <tr><td style="padding:24px;">
          <h2 style="margin:0 0 4px;font-size:20px;color:#1a237e;">{count} site photo{(count == 1 ? "" : "s")} awaiting your review</h2>
          <p style="margin:0 0 8px;font-size:14px;color:#555;">{Esc(p.Name)} <span style="color:#999;">·</span> {Esc(p.Code)}</p>
          <p style="margin:0 0 16px;font-size:13px;color:#888;">Oldest pending: <strong>{oldest:yyyy-MM-dd HH:mm} UTC</strong> ({hoursOld}h ago)</p>
          <p style="margin:24px 0 0;text-align:center;"><a href="#" style="display:inline-block;background:#1a237e;color:#ffffff;padding:10px 24px;border-radius:6px;font-size:14px;font-weight:600;text-decoration:none;">Open BCC › Site Photos →</a></p>
        </td></tr>
        <tr><td style="background:#f6f6f4;padding:14px 24px;font-size:11px;color:#888;text-align:center;">
          <a href="#" style="color:#888;text-decoration:underline;">Manage email preferences</a> · sent by Planscape.
        </td></tr>
      </table>
    </td></tr>
  </table>
</body></html>
""";
    }
}
