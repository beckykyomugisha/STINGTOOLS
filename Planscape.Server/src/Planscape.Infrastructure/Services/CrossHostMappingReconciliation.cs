using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Planscape.Core.Constants;
using Planscape.Core.Entities;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Durability backstop for the cross-host <see cref="ExternalElementMapping"/>
/// table.
///
/// WHY THIS EXISTS — the design decision, recorded deliberately:
/// TagSync (<c>/api/tagsync/sync</c>) and ArchiCAD (<c>/api/archicad/{id}/push</c>)
/// write their mapping rows <b>fire-and-forget</b>, in a fresh DI scope, AFTER
/// the user's request has committed. That was a deliberate UX choice: the
/// mapping upsert must never fail (or slow) the user's sync/push — folding it
/// into the request transaction would (a) add the upsert's latency to every
/// sync response (unbounded for large models — up to 50k elements, batched
/// 500), and (b) let a transient DB error roll back an otherwise-successful tag
/// sync. We keep fire-and-forget and instead close its durability gap from two
/// sides: failures are now observable (<see cref="CrossHostMappingAudit"/> →
/// AuditLog), and dropped writes are recoverable (this job). This is strictly
/// better than moving the upsert into the transaction — it preserves the UX
/// AND makes the table eventually consistent.
///
/// WHAT IT RECOVERS — the Revit/TagSync path is fully reconstructable because
/// the user's transaction DID commit the <see cref="TaggedElement"/> row, which
/// carries <c>UniqueId == IfcGlobalId</c>, <c>RevitElementId</c> (the host
/// element id), and <c>Source</c>. This job finds Revit-attributed
/// TaggedElement rows that lack ANY <c>ExternalElementMapping</c> for
/// <c>(ProjectId, IfcGlobalId, host="revit")</c> and creates the missing row.
///
/// LIMITATION — ArchiCAD pushes write NO TaggedElement projection (ArchiCAD has
/// no tag payload), so a dropped ArchiCAD mapping has no row to reconstruct
/// from; those drops are observable via AuditLog but not auto-backfilled here.
/// Non-Revit IfcController ingests are NOT lossy (they upsert mapping +
/// projection in the same request scope), so they need no backfill.
/// </summary>
public class MappingReconciliationJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MappingReconciliationJob> _logger;

    private const int PageSize = 500;

    public MappingReconciliationJob(IServiceScopeFactory scopeFactory, ILogger<MappingReconciliationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [Hangfire.AutomaticRetry(Attempts = 3, OnAttemptsExceeded = Hangfire.AttemptsExceededAction.Delete)]
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 3600)]
    public async Task ExecuteAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanscapeDbContext>();
        // Hangfire runs without an HttpContext, so the global tenant filter sees
        // Guid.Empty and would match no rows. Bypass it and rely on each row's
        // TenantId — reconciliation is intentionally cross-tenant.
        db.BypassTenantFilter = true;

        var now = DateTime.UtcNow;
        int totalCreated = 0;
        // (tenant, project) → created count, for a per-project audit summary.
        var perProject = new Dictionary<(Guid Tenant, Guid Project), int>();

        // Page over Revit-attributed TaggedElement rows that have no matching
        // revit mapping at all (any HostDocumentGuid). After each SaveChanges the
        // just-created rows leave the result set, so the next Take(PageSize)
        // starts at offset 0 — do NOT add Skip() (see DocumentRetentionArchiveJob).
        while (!ct.IsCancellationRequested)
        {
            var missing = await db.TaggedElements
                .Where(t => t.RevitElementId > 0 && t.UniqueId != ""
                            && !db.ExternalElementMappings.Any(m =>
                                   m.ProjectId == t.ProjectId
                                   && m.IfcGlobalId == t.UniqueId
                                   && m.Host == MappingHosts.Revit))
                .Select(t => new { t.TenantId, t.ProjectId, t.UniqueId, t.RevitElementId, t.Tag1, t.CategoryName })
                .Take(PageSize)
                .ToListAsync(ct);

            if (missing.Count == 0) break;

            foreach (var t in missing)
            {
                db.ExternalElementMappings.Add(new ExternalElementMapping
                {
                    TenantId         = t.TenantId,
                    ProjectId        = t.ProjectId,
                    IfcGlobalId      = t.UniqueId,
                    Host             = MappingHosts.Revit,
                    HostElementId    = t.RevitElementId.ToString(),
                    // TaggedElement does not store the source RVT GUID; null is
                    // correct — resolution is by (IfcGlobalId, Host), the doc
                    // guid is only a federation tiebreaker.
                    HostDocumentGuid = null,
                    HostDisplayLabel = string.IsNullOrWhiteSpace(t.Tag1) ? t.CategoryName : t.Tag1,
                    FirstSeenUtc     = now,
                    LastSeenUtc      = now,
                    IngestionCount   = 1,
                });
                var key = (t.TenantId, t.ProjectId);
                perProject[key] = perProject.TryGetValue(key, out var c) ? c + 1 : 1;
                totalCreated++;
            }

            await db.SaveChangesAsync(ct);
            if (missing.Count < PageSize) break;
        }

        // One audit row per project that needed backfill, so a recovered drop is
        // observable in the ISO-19650 trail (not just the app log).
        foreach (var kv in perProject)
        {
            db.AuditLogs.Add(new AuditLog
            {
                TenantId    = kv.Key.Tenant,
                ProjectId   = kv.Key.Project,
                Action      = "cross_host_mapping_reconciled",
                EntityType  = nameof(ExternalElementMapping),
                Source      = "server",
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    host = MappingHosts.Revit,
                    backfilled = kv.Value,
                    note = "Revit-keyed mappings reconstructed from TaggedElement after a dropped fire-and-forget upsert.",
                }),
            });
        }
        if (perProject.Count > 0) await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "MappingReconciliationJob backfilled {Created} Revit mapping(s) across {Projects} project(s).",
            totalCreated, perProject.Count);
    }
}

/// <summary>
/// Records a dropped fire-and-forget cross-host mapping upsert to the AuditLog
/// (and the app log) so the loss is observable. Called from the catch blocks
/// of TagSync / ArchiCAD's best-effort upserts — turning a previously-silent
/// <c>catch { }</c> into an auditable event. Self-contained: opens its own
/// scope (the caller's scope is likely faulted after the failed SaveChanges)
/// and is itself best-effort — if even the audit write fails (DB down), there
/// is nothing more a background task can safely do.
/// </summary>
public static class CrossHostMappingAudit
{
    public static async Task RecordUpsertFailureAsync(
        IServiceScopeFactory scopeFactory, Guid tenantId, Guid projectId,
        string host, int mappingCount, Exception ex)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;

            sp.GetService<ILoggerFactory>()?.CreateLogger("CrossHostMappingAudit")
              .LogError(ex,
                  "Cross-host mapping upsert dropped: project {ProjectId} host {Host} count {Count}. " +
                  "Revit-keyed rows will be backfilled by MappingReconciliationJob.",
                  projectId, host, mappingCount);

            var db = sp.GetRequiredService<PlanscapeDbContext>();
            db.AuditLogs.Add(new AuditLog
            {
                TenantId    = tenantId,            // set explicitly — no tenant context off-request
                ProjectId   = projectId,
                Action      = "cross_host_mapping_upsert_failed",
                EntityType  = nameof(ExternalElementMapping),
                Source      = "server",
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    host,
                    mappingCount,
                    error = ex.Message,
                    note  = "fire-and-forget mapping upsert failed; MappingReconciliationJob backfills Revit-keyed mappings from TaggedElement.",
                }),
            });
            await db.SaveChangesAsync();
        }
        catch
        {
            // Last resort: the audit write itself failed (DB likely down — the
            // same reason the upsert failed). Nothing safe left to do here.
        }
    }
}
