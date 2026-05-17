namespace Planscape.API.BackgroundJobs;

using Hangfire;
using Microsoft.EntityFrameworkCore;
using Planscape.Infrastructure.Data;

/// <summary>
/// GAP-18 — Daily Hangfire job that auto-archives PUBLISHED documents whose
/// retention period has expired. Retention is driven by
/// <c>DocumentRecord.RetentionExpiresAt</c> (nullable DateTime UTC). When the
/// date is in the past and the document is still in PUBLISHED state, the job
/// transitions it to ARCHIVE and appends a history entry so the audit trail
/// is preserved.
///
/// The job is idempotent — re-runs on the same day will find no eligible rows
/// after the first pass.
/// </summary>
[AutomaticRetry(Attempts = 3)]
[Queue("maintenance")]
public class DocumentRetentionArchiveJob
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<DocumentRetentionArchiveJob> _logger;

    public DocumentRetentionArchiveJob(PlanscapeDbContext db, ILogger<DocumentRetentionArchiveJob> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Load in pages of 200 so a large register doesn't pin memory.
        // NOTE: Do NOT use Skip(page * PageSize) here. After SaveChangesAsync the
        // archived documents leave the WHERE CdeStatus='PUBLISHED' result set, so
        // the next query should always start at offset 0 — incrementing page would
        // silently skip every other batch of 200.
        const int PageSize = 200;
        int totalArchived = 0;

        while (true)
        {
            var batch = await _db.Documents
                .Where(d => d.CdeStatus == "PUBLISHED"
                         && d.RetentionExpiresAt != null
                         && d.RetentionExpiresAt <= now)
                .OrderBy(d => d.RetentionExpiresAt)
                .Take(PageSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            foreach (var doc in batch)
            {
                try
                {
                    var oldState = doc.CdeStatus;
                    doc.CdeStatus = "ARCHIVE";
                    doc.UpdatedAt = now;

                    // Append to status history using the same cap logic as the controller.
                    List<object> history;
                    try
                    {
                        history = string.IsNullOrEmpty(doc.StatusHistoryJson)
                            ? new List<object>()
                            : Newtonsoft.Json.JsonConvert.DeserializeObject<List<object>>(doc.StatusHistoryJson)
                              ?? new List<object>();
                        if (history.Count >= 100)
                            history = history.Skip(history.Count - 99).ToList();
                    }
                    catch
                    {
                        history = new List<object>();
                    }

                    history.Add(new
                    {
                        timestamp = now,
                        oldState,
                        newState = "ARCHIVE",
                        suitability = doc.SuitabilityCode,
                        user = "system:retention-archive",
                        reason = $"Retention period expired ({doc.RetentionExpiresAt:yyyy-MM-dd})"
                    });
                    doc.StatusHistoryJson = Newtonsoft.Json.JsonConvert.SerializeObject(history);

                    _logger.LogInformation(
                        "DocumentRetentionArchiveJob: archived document {DocId} ({FileName}) — retention expired {Date}",
                        doc.Id, doc.FileName, doc.RetentionExpiresAt);
                    totalArchived++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "DocumentRetentionArchiveJob: failed to archive document {DocId}", doc.Id);
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("DocumentRetentionArchiveJob: completed — {Count} documents archived", totalArchived);
    }
}
