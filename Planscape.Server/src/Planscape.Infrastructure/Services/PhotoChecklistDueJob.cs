using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Phase 180 — Daily nudge for active <see cref="PhotoChecklist"/>s
/// approaching or past their <c>DueAt</c>. Runs at 07:00 UTC so the
/// reminder lands in the morning summary.
///
///   * 24 h before due — nudge author + every project member who has
///     fulfilled at least one item ("X items still pending").
///   * Past due — daily nudge until the checklist is Closed or
///     Archived.
///
/// Notifications are best-effort; failures are logged but never fail
/// the loop so a single mis-configured project doesn't poison the run.
/// </summary>
public class PhotoChecklistDueJob
{
    private readonly PlanscapeDbContext _db;
    private readonly INotificationService _notif;
    private readonly ILogger<PhotoChecklistDueJob> _logger;

    public PhotoChecklistDueJob(
        PlanscapeDbContext db,
        INotificationService notif,
        ILogger<PhotoChecklistDueJob> logger)
    {
        _db = db; _notif = notif; _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var due = await _db.PhotoChecklists.AsNoTracking()
            .Where(c => c.Status == "Active" && c.DueAt != null
                     && c.DueAt < now.AddDays(1))
            .ToListAsync(ct);
        _logger.LogInformation("PhotoChecklistDueJob: {Count} due/overdue checklists", due.Count);

        foreach (var c in due)
        {
            try { await NotifyForChecklistAsync(c, now, ct); }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "PhotoChecklistDueJob: checklist {ChecklistId} failed", c.Id);
            }
        }
    }

    private async Task NotifyForChecklistAsync(PhotoChecklist c, DateTime now, CancellationToken ct)
    {
        var pending = await _db.PhotoChecklistItems.AsNoTracking()
            .CountAsync(i => i.ChecklistId == c.Id && i.IsRequired && !i.IsWaived
                         && i.FulfilledByPhotoId == null, ct);
        if (pending == 0) return;

        // Recipients: the checklist author + every coordinator who has
        // fulfilled at least one item on this checklist (de-duplicated).
        var participants = await _db.PhotoChecklistItems.AsNoTracking()
            .Where(i => i.ChecklistId == c.Id && i.FulfilledByUserId != null)
            .Select(i => i.FulfilledByUserId!.Value)
            .Distinct().ToListAsync(ct);
        var recipients = new HashSet<Guid>(participants);
        if (c.CreatedByUserId.HasValue) recipients.Add(c.CreatedByUserId.Value);

        var overdue = c.DueAt < now;
        var title   = overdue ? "Checklist overdue" : "Checklist due tomorrow";
        var msg = overdue
            ? $"\"{c.Name}\" is past due ({pending} required item(s) outstanding)."
            : $"\"{c.Name}\" is due {c.DueAt:yyyy-MM-dd HH:mm} ({pending} required item(s) outstanding).";

        foreach (var uid in recipients)
        {
            try
            {
                await _notif.NotifyUserAsync(uid,
                    title: title,
                    message: msg,
                    data: new { projectId = c.ProjectId, checklistId = c.Id, pending, overdue },
                    ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "PhotoChecklistDueJob: notify {UserId} on {ChecklistId} failed", uid, c.Id);
            }
        }
    }
}
