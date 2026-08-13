using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planscape.Core.Constants;
using Planscape.Core.Entities;
using Planscape.Core.Interfaces;
using Planscape.Infrastructure.Data;

namespace Planscape.Infrastructure.Services;

/// <inheritdoc />
public sealed class IdentityReconciliationService : IIdentityReconciliationService
{
    private readonly PlanscapeDbContext _db;
    private readonly ILogger<IdentityReconciliationService>? _logger;

    public IdentityReconciliationService(PlanscapeDbContext db, ILogger<IdentityReconciliationService>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    public Task<IdentityReconciliationReport> AnalyzeAsync(Guid tenantId, Guid? projectId = null, CancellationToken ct = default)
        => RunAsync(tenantId, projectId, apply: false, ct);

    public Task<IdentityReconciliationReport> ApplyAsync(Guid tenantId, Guid? projectId = null, CancellationToken ct = default)
        => RunAsync(tenantId, projectId, apply: true, ct);

    private async Task<IdentityReconciliationReport> RunAsync(
        Guid tenantId, Guid? projectId, bool apply, CancellationToken ct)
    {
        // IgnoreQueryFilters + explicit tenant: this runs from an admin request
        // whose ambient tenant is the caller's, but the operation is inherently
        // cross-project/whole-tenant, and tombstoned rows must be visible so a
        // live row can win over a tombstoned duplicate.
        IQueryable<TaggedElement> elementsQ = _db.TaggedElements
            .IgnoreQueryFilters().Where(t => t.TenantId == tenantId);
        IQueryable<ExternalElementMapping> mapQ = _db.ExternalElementMappings
            .IgnoreQueryFilters().Where(m => m.TenantId == tenantId);
        if (projectId is Guid pid)
        {
            elementsQ = elementsQ.Where(t => t.ProjectId == pid);
            mapQ = mapQ.Where(m => m.ProjectId == pid);
        }

        // ── 1. Backfill: Revit rows (RevitElementId > 0) with no IfcGlobalId, from
        //       ExternalElementMapping (host=revit, HostElementId = RevitElementId). ──
        var revitMap = (await mapQ.ToListAsync(ct))
            .Where(m => MappingHosts.Normalize(m.Host) == MappingHosts.Revit
                        && !string.IsNullOrWhiteSpace(m.IfcGlobalId)
                        && !string.IsNullOrWhiteSpace(m.HostElementId))
            .GroupBy(m => (m.ProjectId, m.HostElementId))
            .ToDictionary(g => g.Key, g => g.First().IfcGlobalId);

        var backfillCandidates = await elementsQ
            .Where(t => t.RevitElementId > 0 && (t.IfcGlobalId == null || t.IfcGlobalId == ""))
            .ToListAsync(ct);

        int backfilled = 0;
        foreach (var t in backfillCandidates)
        {
            if (revitMap.TryGetValue((t.ProjectId, t.RevitElementId.ToString()), out var gid))
            {
                if (apply) t.IfcGlobalId = gid;
                backfilled++;
            }
        }
        // Persist the backfill FIRST so the grouping below sees the newly-keyed rows.
        if (apply && backfilled > 0) await _db.SaveChangesAsync(ct);

        // ── 2. Merge: collapse each (ProjectId, IfcGlobalId) group to one row. ──
        var withGid = await elementsQ
            .Where(t => t.IfcGlobalId != null && t.IfcGlobalId != "")
            .ToListAsync(ct);

        var groups = withGid
            .GroupBy(t => (t.ProjectId, t.IfcGlobalId))
            .Where(g => g.Count() > 1)
            .ToList();

        int dupGroups = groups.Count;
        int dupRows = groups.Sum(g => g.Count() - 1);
        int merged = 0, revitIdConflicts = 0;

        foreach (var g in groups)
        {
            if (g.Where(t => t.RevitElementId > 0).Select(t => t.RevitElementId).Distinct().Count() > 1)
                revitIdConflicts++;

            if (!apply) continue;

            var members = g.ToList();
            var freshest = members
                .OrderByDescending(t => t.LastModifiedUtc ?? DateTime.MinValue)
                .ThenBy(t => t.Id)
                .First();

            // Primary = the Revit row when one exists, so RevitElementId / UniqueId
            // (both under filtered-UNIQUE indexes) are NEVER mutated while a
            // to-be-deleted sibling still holds the same value — which would risk
            // a transient unique violation inside the SaveChanges transaction on
            // Postgres. Data is taken from the freshest row instead.
            var primary = members
                .Where(t => t.RevitElementId > 0)
                .OrderByDescending(t => t.LastModifiedUtc ?? DateTime.MinValue)
                .ThenBy(t => t.Id)
                .FirstOrDefault() ?? freshest;

            if (!ReferenceEquals(primary, freshest))
                CopyDataFrom(primary, freshest);

            // Keep the newest modification stamp + highest version across the group,
            // and let a live row win over a tombstoned duplicate.
            primary.LastModifiedUtc = members.Max(t => t.LastModifiedUtc) ?? primary.LastModifiedUtc;
            primary.Version = members.Max(t => t.Version);
            if (members.Any(t => t.DeletedAtUtc == null)) primary.DeletedAtUtc = null;

            var others = members.Where(t => !ReferenceEquals(t, primary)).ToList();
            _db.TaggedElements.RemoveRange(others);
            merged += others.Count;
        }

        if (apply && merged > 0) await _db.SaveChangesAsync(ct);

        var report = new IdentityReconciliationReport
        {
            Applied = apply,
            RevitRowsBackfilled = backfilled,
            DuplicateGroups = dupGroups,
            DuplicateRows = dupRows,
            RowsMerged = apply ? merged : 0,
            RevitIdConflicts = revitIdConflicts,
        };
        _logger?.LogInformation(
            "[identity-reconcile] {Mode} tenant={Tenant} project={Project} backfilled={Backfilled} " +
            "dupGroups={Groups} dupRows={Rows} merged={Merged} revitIdConflicts={Conflicts}",
            apply ? "APPLY" : "ANALYZE", tenantId, projectId?.ToString() ?? "(all)",
            backfilled, dupGroups, dupRows, report.RowsMerged, revitIdConflicts);
        return report;
    }

    /// <summary>
    /// Copy every NON-identity field from <paramref name="src"/> onto
    /// <paramref name="dst"/>. Identity + row keys (Id, TenantId, ProjectId,
    /// RevitElementId, UniqueId, IfcGlobalId) are deliberately left untouched.
    /// </summary>
    private static void CopyDataFrom(TaggedElement dst, TaggedElement src)
    {
        dst.Disc = src.Disc; dst.Loc = src.Loc; dst.Zone = src.Zone; dst.Lvl = src.Lvl;
        dst.Sys = src.Sys; dst.Func = src.Func; dst.Prod = src.Prod; dst.Seq = src.Seq;
        dst.Tag1 = src.Tag1; dst.Tag7 = src.Tag7;
        dst.Tag7A = src.Tag7A; dst.Tag7B = src.Tag7B; dst.Tag7C = src.Tag7C;
        dst.Tag7D = src.Tag7D; dst.Tag7E = src.Tag7E; dst.Tag7F = src.Tag7F;
        dst.CategoryName = src.CategoryName; dst.FamilyName = src.FamilyName; dst.TypeName = src.TypeName;
        dst.Status = src.Status; dst.Rev = src.Rev; dst.GridRef = src.GridRef;
        dst.RoomName = src.RoomName; dst.Level = src.Level;
        dst.IsStale = src.IsStale; dst.IsComplete = src.IsComplete; dst.IsFullyResolved = src.IsFullyResolved;
        dst.ValidationErrors = src.ValidationErrors;
        dst.PreviousTag = src.PreviousTag; dst.TagModifiedAt = src.TagModifiedAt;
        dst.SyncedAt = src.SyncedAt; dst.SyncedBy = src.SyncedBy;
        dst.Source = src.Source;
        dst.P6ActivityId = src.P6ActivityId; dst.PercentComplete = src.PercentComplete;
        dst.ActualStart = src.ActualStart; dst.ActualFinish = src.ActualFinish;
    }
}
