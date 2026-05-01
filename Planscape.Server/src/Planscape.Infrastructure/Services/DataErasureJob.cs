using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// S7.4.1 — daily Hangfire job that hard-deletes tenants whose
/// <c>PendingErasureAt</c> has elapsed. Closes the loop opened by
/// <see cref="Planscape.API.Controllers.DataRightsController"/>:
///
///   1. Owner POSTs /api/data-rights/erase
///      → Tenant.IsActive = false, PendingErasureAt = now + 30d
///   2. (30-day cooling-off; cancel-erase aborts cleanly)
///   3. This job runs daily, picks every tenant where
///      PendingErasureAt &lt;= now, and deletes their rows row-by-row
///      across every tenant-scoped table — plus storage objects under
///      <c>t_{tenantId}/</c> on the configured IFileStorageService.
///
/// Order matters because of FK cascades: child tables first, then
/// parents, finally the Tenant row itself. We use raw SQL DELETE
/// scoped to TenantId rather than relying on EF cascade semantics
/// (some FKs are restrict-on-delete).
///
/// Audit: every erasure logs a row to AuditLogs on the *platform*
/// tenant (so the row survives the deletion of the erased tenant)
/// for regulator-evidence trails.
/// </summary>
public class DataErasureJob
{
    private readonly PlanscapeDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly ILogger<DataErasureJob> _logger;

    public DataErasureJob(PlanscapeDbContext db, IFileStorageService storage, ILogger<DataErasureJob> logger)
    {
        _db = db; _storage = storage; _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        _db.BypassTenantFilter = true;
        var now = DateTime.UtcNow;

        var due = await _db.Tenants
            .Where(t => t.PendingErasureAt != null && t.PendingErasureAt <= now)
            .ToListAsync(ct);
        if (due.Count == 0) return;

        var platformTenantId = await _db.Tenants
            .Where(t => t.Slug == "planscape")
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);

        foreach (var tenant in due)
        {
            try
            {
                await EraseAsync(tenant.Id, ct);
                if (platformTenantId.HasValue && platformTenantId != tenant.Id)
                {
                    _db.AuditLogs.Add(new AuditLog
                    {
                        TenantId   = platformTenantId.Value,
                        Action     = "tenant.erased",
                        EntityType = "Tenant",
                        EntityId   = tenant.Id.ToString(),
                        DetailsJson = $"{{\"slug\":\"{tenant.Slug}\",\"name\":\"{tenant.Name.Replace("\"", "'")}\"}}",
                        Source     = "system",
                    });
                    await _db.SaveChangesAsync(ct);
                }
                _logger.LogWarning("Tenant {Slug} ({Id}) hard-deleted per GDPR/POPIA request.", tenant.Slug, tenant.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DataErasureJob failed for tenant {Id}", tenant.Id);
            }
        }
    }

    private async Task EraseAsync(Guid tenantId, CancellationToken ct)
    {
        // 1. SQL deletes, child → parent. Composite raw SQL keeps it atomic
        //    inside one transaction. Order respects the existing FK graph.
        await _db.Database.ExecuteSqlRawAsync($@"
            BEGIN;
            -- Pin / CRDT
            DELETE FROM ""PinCrdtUpdates""        WHERE ""TenantId"" = '{tenantId}';
            -- Markup + audio
            DELETE FROM ""ModelMarkups""          WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""IssueAudioNotes""       WHERE ""TenantId"" = '{tenantId}';
            -- Scenes
            DELETE FROM ""SceneNodes""            WHERE ""TenantId"" = '{tenantId}';
            -- Issues + attachments
            DELETE FROM ""IssueComments""         WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""IssueAttachments""      WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""Issues""                WHERE ""TenantId"" = '{tenantId}';
            -- Documents
            DELETE FROM ""DocumentApprovals""     WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""DocumentVersions""      WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""DocumentMarkups""       WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""Documents""             WHERE ""TenantId"" = '{tenantId}';
            -- Meetings
            DELETE FROM ""MeetingActionItems""    WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""Meetings""              WHERE ""TenantId"" = '{tenantId}';
            -- Site diaries + stage gates
            DELETE FROM ""SiteDiaryAttachments""  WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""SiteDiaries""           WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""StageGateCriteria""     WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""InformationDeliverables"" WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""StageGates""            WHERE ""TenantId"" = '{tenantId}';
            -- Other tenant-scoped data
            DELETE FROM ""TaggedElements""        WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""ProjectModels""         WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""ScheduleTasks""         WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""CostItems""             WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""ComplianceSnapshots""   WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""Transmittals""          WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""WorkflowRuns""          WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""SeqCounters""           WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""SyncConflicts""         WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""SyncWatermarks""        WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""IssueCustomFieldSchemas"" WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""ProjectMembers""        WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""Projects""              WHERE ""TenantId"" = '{tenantId}';
            -- Billing
            DELETE FROM ""Payments""              WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""Invoices""              WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""Subscriptions""         WHERE ""TenantId"" = '{tenantId}';
            -- Tenant-level
            DELETE FROM ""DevicePushTokens""      WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""LicenseKeys""           WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""OutboundWebhooks""      WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""PlatformConnections""   WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""TenantBrandings""       WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""UserNotificationPreferences"" WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""OutboxMessages""        WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""Users""                 WHERE ""TenantId"" = '{tenantId}';
            DELETE FROM ""AuditLogs""             WHERE ""TenantId"" = '{tenantId}';
            -- Finally the Tenant row itself
            DELETE FROM ""Tenants""               WHERE ""Id""       = '{tenantId}';
            COMMIT;
        ", ct);

        // 2. Storage. We can't enumerate efficiently across providers, so
        //    we issue a delete for the conventional tenant prefix; the
        //    storage adapter is responsible for recursive removal under
        //    that prefix when supported (LocalFileStorageService removes
        //    the directory; S3 issues a delete-objects-by-prefix). When
        //    not supported the orphan files become unreachable
        //    (no DB row references them) and a janitor job cleans up later.
        try
        {
            await _storage.DeleteAsync($"t_{tenantId:N}", ct, bypassTenantCheck: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Storage prefix delete failed for tenant {Id}; rows are gone, files orphan.", tenantId);
        }
    }
}
